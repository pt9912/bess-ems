using BatteryEms.Application.Control;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BatteryEms.Adapters.NativeInterop;

// M3-D2 DI-Wiring für den Native Control Core. Bisher hat
// `ControlCycleUseCase` auf einen Konstruktor-Default
// (`new ManagedControlKernel()`) zurückgegriffen, sobald der DI-
// Container keinen `IControlKernel` registriert hatte. Mit M3-D2
// wird die Wahl zwischen Managed-Referenz und
// Native+Fallback-Routing zur expliziten Konfigurations-Frage:
// `NativeControl:Enabled` schaltet die Aktivierung um, der Loader
// (RM-M3-03) entscheidet vom dlopen-/ABI-Ergebnis aus, ob die
// Native-Variante oder ein deterministischer Managed-Fallback
// landet, und `AbortOnAbiMismatch=true` setzt die opt-in
// Production-Policy aus `docs/user/quality.md` §5.2 um.
public static class NativeInteropRegistration
{
    // Konfigurations-Sektion analog zu den anderen
    // Adapter-Registrierungen — `appsettings.json` oder die
    // produktionsnahe Profil-Datei (z. B. `appsettings.Native.json`)
    // setzt `NativeControl:Enabled=true` und ggf. einen abweichenden
    // `NativeControl:LibraryPath`. Default-Section-Name ist
    // bewusst aus dem Adapter heraus exportiert, damit der Host
    // ihn nicht hartcodieren muss.
    public const string ConfigurationSection = "NativeControl";

    public static IServiceCollection AddBessNativeControl(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var options =
            configuration.GetSection(ConfigurationSection).Get<NativeControlOptions>()
            ?? new NativeControlOptions();

        // Loader und Fallback-Adapter brauchen Logger; wir
        // registrieren `IControlKernel` als Singleton-Factory,
        // damit der Logger-Kontext aus dem ServiceProvider zur
        // Build-Zeit aufgelöst wird (statt eines ad-hoc-
        // `LoggerFactory` an dieser Stelle). Die Factory zieht
        // den Loader-Run nur einmal beim ersten `IControlKernel`-
        // Resolve — die DI-Container-interne Singleton-Cache
        // garantiert, dass der teure dlopen + ABI-Check pro Host-
        // Instanz exakt einmal passiert.
        services.AddSingleton<IControlKernel>(sp =>
            BuildControlKernel(options, sp));

        return services;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design", "CA1031",
        Justification = "DI factory for IControlKernel: any unexpected error during the dlopen / ABI handshake must surface as a hard startup failure rather than a silent fallback, so we let the InvalidOperationException from ApplyAbortPolicy propagate and rethrow other unexpected errors with context.")]
    internal static IControlKernel BuildControlKernel(
        NativeControlOptions options,
        IServiceProvider services)
    {
        var loaderLogger = services.GetRequiredService<ILogger<NativeControlLoader>>();
        var loader = new NativeControlLoader(loaderLogger);
        var loadResult = loader.TryLoad(options);

        // Production-policy escape hatch (`AbortOnAbiMismatch=true`):
        // ABI-Mismatch wird zum harten Startup-Fehler, statt still in
        // den Managed-Pfad zurückzufallen. ApplyAbortPolicy throw't
        // `InvalidOperationException`, die der DI-Resolve weiterreicht.
        NativeControlLoader.ApplyAbortPolicy(loadResult, options);

        if (!loadResult.IsLoaded)
        {
            // Default-Fallback-Pfad gemäß `quality.md` §5.2: bei
            // `Disabled` / `LibraryMissing` / `LoadFailed` /
            // `AbiMismatch` (ohne Abort-Flag) registriert der Host
            // die Managed-Referenz, der Regelkreis bleibt aktiv.
            // Der Loader hat bereits den passenden Log-Event
            // emittiert (`native_control_status=…`), kein
            // zusätzliches Logging hier.
            return new ManagedControlKernel();
        }

        // Loaded → NativeControlKernel um den OS-Handle aus dem
        // Loader-Result, plus eine Managed-Instanz als Tick-lokaler
        // Fallback bei nativen Fehlern aus validem Kontext.
        var handle = loadResult.Handle
            ?? throw new InvalidOperationException(
                "Loaded NativeControlLoadResult must carry a non-null Handle "
                + $"(NativeControlLoadResult.Loaded contract). Path: {loadResult.LibraryPath}.");
        var native = new NativeControlKernel(handle);
        var managed = new ManagedControlKernel();
        var fallbackLogger =
            services.GetRequiredService<ILogger<NativeFallbackControlKernel>>();
        return new NativeFallbackControlKernel(native, managed, fallbackLogger);
    }
}
