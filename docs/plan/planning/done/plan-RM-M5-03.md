# Plan RM-M5-03 — Hochfrequente Telemetrie-Filterung im Native Core

**Dokumenttyp:** Abschlussnotiz / M5 Detail-Slice  
**Status:** Abgeschlossen am 2026-05-13  
**Bezug:** [`../in-progress/plan-RM-M5.md`](../in-progress/plan-RM-M5.md),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md),
[`../../adr/0003-native-kernel-language.md`](../../adr/0003-native-kernel-language.md),
[`../../../user/quality.md`](../../../user/quality.md),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(LH-NATIVE-001, LH-NATIVE-003/004/005, LH-TEST-005)

---

## Ergebnis

RM-M5-03 liefert einen schmalen, additiven Native-Filtervertrag im
bestehenden `battery_control_core`. Der neue Export
`battery_control_core_filter_telemetry` filtert SOC, aktive Leistung und
Temperatur mit einem deterministischen First-Order-IIR:

```text
y_next = alpha * measurement + (1 - alpha) * previous
```

Der Vertrag bleibt bewusst klein: Einheiten entsprechen dem bestehenden
Native-Snapshot (`SOC %`, `active_power_kw`, `temperature_celsius`,
`dt_seconds`), `alpha` liegt in `[0, 1]`, der erste gültige Messwert
seedet den Filter bei `initialized=0`, und Drift-/Sample-Period-Guards
brechen mit maschinenlesbaren Reason-Codes ab. Es gibt keine Allocation
über die ABI-Grenze und keine Pointer-Retention.

## Gelieferte Artefakte

| Bereich | Umsetzung |
| ------- | --------- |
| Native ABI | ABI-Bump `0.2.0 → 0.3.0`; neue Structs `bcc_telemetry_filter_state_t`, `bcc_telemetry_filter_options_t`, `bcc_telemetry_filter_input_t`, `bcc_telemetry_filter_output_t`; neuer Export `battery_control_core_filter_telemetry`. |
| Reason-Codes | Append-only `BCC_REASON_FILTER_INVALID_OPTIONS=17`, `BCC_REASON_FILTER_SAMPLE_PERIOD=18`, `BCC_REASON_FILTER_TELEMETRY_DRIFT=19`. |
| Native Implementierung | `compute.c` validiert Non-Finite, Optionsbereich, Sample-Fenster und Drift; Cold-Boot ignoriert vorherige Filterwerte analog zum bestehenden First-Tick-Vertrag. |
| .NET Adapter | `BccTelemetryFilter*`-P/Invoke-Structs, Constants, `INativeLibraryGateway.CallFilterTelemetry`, `SystemNativeLibraryGateway`-Exportbindung und `NativeControlKernel.FilterTelemetry`. |
| Tests | Native doctests für Cold-Boot, IIR-Update, alpha=0/1, Non-Finite, invalid options, Sample-Fenster, Drift und Null-Pointer; .NET-Layout-/Wrapper-Pins; Integrationstests gegen echte `.so`. |

## Entscheidungen

- **D-01: Kein MPC-Orchestrator-Umbau in diesem Slice.** RM-M5-03
  stellt den nativen Filterpfad und die ABI bereit. Die produktive
  Policy, wann MPC/Replay diesen Filter verwendet, bleibt oberhalb des
  Native-Adapters.
- **D-02: Drift ist `INVALID_INPUT`, kein `LIMITED`.** Ein
  Drift-Verstoß ist ein unbrauchbarer Filter-/MPC-State gemäß
  Fallback-Matrix, keine technische Begrenzung eines gültigen Messwerts.
- **D-03: Sample-Period-Fenster ist inklusiv.** `dt_seconds` muss in
  `[min_sample_period_seconds, max_sample_period_seconds]` liegen; das
  macht Replay-Fixtures und Hochfrequenz-Sampling deterministisch.
- **D-04: State wird bei Drift erhalten.** Bei Drift liefert die
  Ausgabe den vorherigen gefilterten State zurück, damit Caller ohne
  Allocation entscheiden können, ob sie verwerfen, replayen oder
  Fallback triggern.

## Nachweise

- `make native-build` — grün; baut `libbattery_control_core.so` und
  führt die native doctest-Suite über `ctest` aus.
- `make test-native-interop` — grün; 42 Integrationstests gegen echte
  `.so`, inklusive vier neuer Telemetrie-Filter-Pins.
- `docker run --rm bess-ems-test-native-interop:latest dotnet test tests/adapters/driven/BatteryEms.Adapters.NativeInterop.Tests/BatteryEms.Adapters.NativeInterop.Tests.csproj --configuration Release --no-build --no-restore --logger "console;verbosity=normal"` — grün; 60 Unit-Tests inklusive Layout- und Wrapper-Pins.

## Bewusst draußen

- Produktive Aktivierung im MPC-Orchestrator oder Worker-Wiring.
- Prometheus-Metriken nur für Filterstatus.
- Breite Replay-Matrix mit realen Hochfrequenz-Datensätzen und
  Performance-SLOs. Der ABI-Pfad ist vorhanden; die Nutzungs-Policy
  bleibt ein eigener, bedarfsgesteuerter Slice.
