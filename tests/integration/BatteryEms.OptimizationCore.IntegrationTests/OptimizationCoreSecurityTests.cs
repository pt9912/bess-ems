using BatteryEms.Adapters.OptimizationCore;
using BatteryEms.Application.Optimization;
using BatteryEms.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BatteryEms.OptimizationCore.IntegrationTests;

// Plan-RM-M5-01-C step 3 Security-Pins. Härtung der Production-Linie
// gegen die typischen Konfigurations-Fehlbedienungen.
//
// **mTLS-Cert-Mismatch** (plan §4 Sub-Slice C Pin 2) wird mit dem
// echten Cert-Trust-Bridge-Slice mitgeliefert — heute haben wir keinen
// mTLS-fähigen TestSidecar (Kestrel-UDS-Listener fährt h2c ohne TLS).
// Marker: Sub-Slice-D-Carve-out wenn ein Production-Smoke-Test gegen
// einen externen Sidecar gefahren wird.
[Trait("Category", "Integration")]
[Collection("OptimizationCore Integration")]
public sealed class OptimizationCoreSecurityTests
{
    // Pin 1: Production+plaintext-http → Boot-Fehler (Sub-Slice A
    // EnsureValid-Pin re-pinnt hier auf der DI-Aufbau-Schicht).
    [Fact]
    public void Production_profile_with_plaintext_http_endpoint_throws_at_construction()
    {
        var options = new OptimizationCoreOptions
        {
            SidecarEndpoint = new Uri("http://localhost:5001"),
            RuntimeProfile = OptimizationCoreRuntimeProfile.Production,
        };
        var client = new OptimizationCoreClient(options);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new OptimizationCoreScheduleOptimizer(
                client,
                options,
                new InMemoryOptimizationIdempotencyStore(),
                new Defaults.FixedClock(),
                NullLogger<OptimizationCoreScheduleOptimizer>.Instance));
        Assert.Contains(
            "optimization-core-not-hardened-in-production",
            ex.Message,
            StringComparison.Ordinal);
    }

    // Pin 2: Production-Profile + UDS-Socket mit world-readable Mode
    // (0644) → Connect wirft `optimization-core-uds-permissions-not-
    // locked`. Wir legen den Socket manuell als File an (nicht als
    // echten Socket — der Mode-Check liest nur die Filesystem-Perms,
    // nicht ob es ein Socket ist).
    [Fact]
    public async Task Production_profile_with_world_readable_uds_throws_at_connect()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            // UDS-Mode-Enforcement ist Linux/macOS-spezifisch; auf
            // Windows skipped der Adapter den Check (siehe
            // EnsureUdsPermissionsLockedIfRequired).
            return;
        }
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            "BatteryEms",
            "OptimizationCore",
            $"insecure-{Guid.NewGuid():N}.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
        try
        {
            await File.WriteAllBytesAsync(socketPath, Array.Empty<byte>());
            File.SetUnixFileMode(socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            var options = new OptimizationCoreOptions
            {
                SidecarEndpoint = new Uri($"unix://{socketPath}"),
                RuntimeProfile = OptimizationCoreRuntimeProfile.Production,
            };
            await using var client = new OptimizationCoreClient(options);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.ConnectAsync(default));
            Assert.Contains(
                "optimization-core-uds-permissions-not-locked",
                ex.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            try { File.Delete(socketPath); }
#pragma warning disable CA1031
            catch { }
#pragma warning restore CA1031
        }
    }

    // Pin 3: Production-Profile + UDS-Socket mit Mode 0600 → Connect
    // OK (UDS-Mode-Check lässt durch; der Channel scheitert erst
    // beim ersten RPC wenn der Sidecar nicht antwortet, aber das ist
    // nicht Teil des UDS-Mode-Pins).
    [Fact]
    public async Task Production_profile_with_locked_uds_passes_uds_mode_check()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            "BatteryEms",
            "OptimizationCore",
            $"locked-{Guid.NewGuid():N}.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
        try
        {
            await File.WriteAllBytesAsync(socketPath, Array.Empty<byte>());
            File.SetUnixFileMode(socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);

            var options = new OptimizationCoreOptions
            {
                SidecarEndpoint = new Uri($"unix://{socketPath}"),
                RuntimeProfile = OptimizationCoreRuntimeProfile.Production,
            };
            await using var client = new OptimizationCoreClient(options);

            // ConnectAsync wirft NICHT — UDS-Mode-Check passt.
            await client.ConnectAsync(default);
            Assert.True(client.IsConnected);
        }
        finally
        {
            try { File.Delete(socketPath); }
#pragma warning disable CA1031
            catch { }
#pragma warning restore CA1031
        }
    }

    // Pin 4: HilSimulator-Profile + UDS-Socket mit Mode 0644 → durch
    // (Mode-Check ist Production-only).
    [Fact]
    public async Task Hil_simulator_profile_with_world_readable_uds_passes()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }
        var socketPath = Path.Combine(
            Path.GetTempPath(),
            "BatteryEms",
            "OptimizationCore",
            $"hil-{Guid.NewGuid():N}.sock");
        Directory.CreateDirectory(Path.GetDirectoryName(socketPath)!);
        try
        {
            await File.WriteAllBytesAsync(socketPath, Array.Empty<byte>());
            File.SetUnixFileMode(socketPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

            var options = new OptimizationCoreOptions
            {
                SidecarEndpoint = new Uri($"unix://{socketPath}"),
                RuntimeProfile = OptimizationCoreRuntimeProfile.HilSimulator,
            };
            await using var client = new OptimizationCoreClient(options);

            await client.ConnectAsync(default);
            Assert.True(client.IsConnected);
        }
        finally
        {
            try { File.Delete(socketPath); }
#pragma warning disable CA1031
            catch { }
#pragma warning restore CA1031
        }
    }
}
