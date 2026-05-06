namespace BatteryEms.Host;

// Single Main entrypoint for the bess-ems process. Boot sequence is
// owned by BessHostBuilder so tests can resolve the same composition.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1052", Justification = "Program is a WebApplicationFactory<TEntryPoint> marker; static would break the test host.")]
public class Program
{
    protected Program() { }

    public static void Main(string[] args)
    {
        var app = BessHostBuilder.BuildApp(args);
        app.Run();
    }
}
