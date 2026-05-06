namespace BatteryEms.Adapters.Mqtt.Tests;

internal static class TestHelpers
{
    public static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(5);
        }
        if (!predicate())
        {
            throw new TimeoutException("predicate never became true");
        }
    }
}
