using System.Threading.Channels;

namespace BatteryEms.Adapters.OpcUa;

// Wrapper around bounded Channel<OpcUaNotification> that detects drops
// from the SDK-callback Producer-Pfad (plan-RM-M4-04 D-03). The
// underlying Channel runs with BoundedChannelFullMode.DropOldest so
// TryWrite always returns true — Drop-Erkennung passiert über die
// monotonen Interlocked-Counter `_writeSeq` und `_readSeq`. Wenn die
// Differenz die Capacity übersteigt, wird das sticky `_overflow`-Flag
// gesetzt.
//
// Race-Eigenschaft: die Zähler sind monoton wachsend; Set/Clear des
// Overflow-Flags läuft über Interlocked.Exchange. Ein Read kann
// zwischen Producer-Increment und Counter-Lesung dazwischenfallen,
// aber das führt höchstens zu einer leicht verzögerten Set-/Clear-
// Aktion. Es kann **keine** Bad-StatusCode-Notification silent
// verschwinden — sobald ein Drop passiert, ist der Flag gesetzt
// (und damit die DataQuality des nächsten emittierten Samples
// degradiert), bis der Channel komplett gedraint ist.
internal sealed class OverflowAwareTelemetryChannel
{
    private readonly Channel<OpcUaNotification> _channel;
    private readonly int _capacity;
    private long _writeSeq;
    private long _readSeq;
    private int _overflow; // 0 = clean, 1 = sticky-overflow

    public OverflowAwareTelemetryChannel(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity), capacity, "capacity must be positive.");
        }
        _capacity = capacity;
        _channel = Channel.CreateBounded<OpcUaNotification>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool HasOverflow => Volatile.Read(ref _overflow) == 1;

    // Producer-side (SDK callback, async-context oder sync). Non-
    // blocking: returns immediately. Sets the sticky overflow flag if
    // the in-flight count after this write exceeds capacity (i. e. the
    // bounded channel just dropped an older entry).
    public bool TryWrite(OpcUaNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        Interlocked.Increment(ref _writeSeq);
        var written = _channel.Writer.TryWrite(notification);
        // With DropOldest the channel's TryWrite always succeeds; the
        // `written` return value is preserved here for symmetry with
        // the post-Complete()-after-Dispose case where TryWrite
        // returns false.
        if (!written)
        {
            return false;
        }
        var inFlight = Interlocked.Read(ref _writeSeq) - Interlocked.Read(ref _readSeq);
        if (inFlight > _capacity)
        {
            Interlocked.Exchange(ref _overflow, 1);
        }
        return true;
    }

    // Consumer-side: drain everything currently buffered. Returns the
    // notifications in FIFO order (oldest-first per Channel
    // semantics); after a clean drain (writer == reader), the sticky
    // overflow flag is cleared.
    public IReadOnlyList<OpcUaNotification> DrainAll()
    {
        var collected = new List<OpcUaNotification>();
        while (_channel.Reader.TryRead(out var notification))
        {
            Interlocked.Increment(ref _readSeq);
            collected.Add(notification);
        }
        if (Interlocked.Read(ref _writeSeq) == Interlocked.Read(ref _readSeq))
        {
            Interlocked.Exchange(ref _overflow, 0);
        }
        return collected;
    }

    // Tear-down (Sub-Slice B's IAsyncDisposable path, D-09): mark the
    // writer as completed so post-dispose TryWrite returns false and
    // the source's drain loop sees an empty + completed channel.
    public void Complete()
    {
        _channel.Writer.TryComplete();
    }
}
