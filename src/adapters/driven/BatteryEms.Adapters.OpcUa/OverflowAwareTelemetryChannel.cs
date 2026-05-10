using System.Threading.Channels;

namespace BatteryEms.Adapters.OpcUa;

// Wrapper around bounded Channel<OpcUaNotification> that detects drops
// from the SDK-callback Producer-Pfad (plan-RM-M4-04 D-03). The
// underlying Channel runs with BoundedChannelFullMode.DropOldest so
// TryWrite always returns true — Drop-Erkennung passiert über die
// monotonen Interlocked-Counter `_writeSeq` und `_readSeq`. Wenn die
// Differenz die Capacity übersteigt, wird das `_overflow`-Flag gesetzt.
//
// Flag-Semantik (plan §148): das Flag steht solange in einem
// Backlog-Zustand und wird beim **nächsten Drain** unkonditional
// gelöscht — der Channel ist nach dem TryRead-Loop per Definition
// leer. **Counter-Equality wäre die falsche Clear-Bedingung**: writeSeq
// zählt jeden TryWrite (inkl. der gedroppten); readSeq nur tatsächlich
// gelieferte Items. Nach dem ersten Drop-Event divergieren die beiden
// permanent — die Equality-Bedingung würde das Flag für immer
// stehenlassen, was nicht der Plan-Vorgabe entspricht.
//
// Race-Eigenschaft: die Zähler sind monoton wachsend; Set/Clear läuft
// via Interlocked.Exchange. Set kann leicht verzögert sein (Producer
// inkrementiert writeSeq, dann Reader bedient sich, _bevor_ Producer
// die Differenz prüft) — das verzögert nur den Set, verliert aber
// keine Bad-StatusCode-Notification (die wartet im Channel oder ist
// als Drop bereits in der Differenz sichtbar). Clear ist
// unkonditional am Drain-Ende, also race-frei mit Producer-Inkrement.
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
    // notifications in FIFO order (oldest-first per Channel semantics).
    // The overflow flag is cleared unconditionally at the end of the
    // drain — the bounded channel is empty by definition after the
    // TryRead loop, which matches plan §148. Subsequent overflow
    // detection (next TryWrite that drops) re-arms the flag.
    public IReadOnlyList<OpcUaNotification> DrainAll()
    {
        var collected = new List<OpcUaNotification>();
        while (_channel.Reader.TryRead(out var notification))
        {
            Interlocked.Increment(ref _readSeq);
            collected.Add(notification);
        }
        Interlocked.Exchange(ref _overflow, 0);
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
