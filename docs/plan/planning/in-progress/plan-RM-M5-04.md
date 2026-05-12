# Plan RM-M5-04 Replay-Plattform

Status: in Arbeit seit 2026-05-12. Dieser Slice baut die M5-Replay-Basis
inkrementell aus, ohne die bestehenden M2/M3-Replay-Pfade zu entfernen.

## Ziel

Replay-Datensaetze bekommen ein versioniertes Manifest, externe JSON-Fixtures,
Golden-Dateien und einen Diff-Report, der numerische Toleranzverletzungen von
fachlicher Drift trennt. Bestehende M2/M3-Pflichtfaelle bleiben lauffaehig, bis
ein ueberlappender CI-Lauf den neuen und alten Pfad verglichen hat.

## Sub-Slices

| Status | ID | Inhalt | Nachweis |
| ------ | -- | ------ | -------- |
| ✅ | RM-M5-04-A | Manifest-v1, Telemetrie-Fixture-v1, Golden-Command-v1, reject-by-default Loader und Golden-Diff-Grundlage | `tests/hexagon/BatteryEms.Application.Tests/Replay/*`, `tests/fixtures/replay/rm-m5-04/telemetry-linear/*`, `make test-replay` |
| ✅ | RM-M5-04-B | Vollstaendige M2/M3-Kompatibilitaetsmigration mit Golden-Diff-Matrix | Vier M2-Pflichtfaelle haben Manifest-v1-Fixture/Golden und laufen im `make test-replay` parallel zum alten Harness; M3 Native-Parity `cases.v1.json` ist per Manifest-v1 `repo://` referenziert und bleibt ueber `make test-native-parity` aktiv |
| ✅ | RM-M5-04-C | Manifestgetriebener Engine-Vergleich | `native-control-parity`-Manifest treibt Managed-vs-Native-Vergleich per `NativeParityEngineComparisonRunner`; Sidecar/MPC-Engine-Vergleich bleibt fuer RM-M5-04-D bzw. RM-M5-06-Orchestrierungsnaehe |
| ✅ | RM-M5-04-D | Entwickler-/CI-Report fuer Replay-Diffs | Maschinenlesbarer `replay-diff-report.v1`-JSON-Report fuer M2-Telemetrie-Replays und M3 Native-Parity; `BESS_REPLAY_REPORT_DIR` schreibt CI-Artefakte |

## RM-M5-04-A Ergebnis

- `ReplayManifestLoader` akzeptiert nur `replay-manifest.v1`, bekannte
  Top-Level- und Nested-Felder, bekannte Replay-Art und relative Fixture-Pfade.
- Das Manifest klassifiziert Felder in required/optional/deprecated/
  tolerated_legacy und traegt Seed, Determinismusmodus, Runtime-/Numerikstamp,
  Solveroption, `request_id`-Regel, Toleranzen und M2-Kompatibilitaetsinventar.
- `TelemetryReplayJsonLoader` laedt externe `telemetry-replay-fixture.v1`
  Dateien in den bestehenden `TelemetryReplayHarness`.
- `ReplayGoldenJsonLoader` und `ReplayGoldenComparer` vergleichen Commands
  gegen `telemetry-golden-command.v1`; Diff-Klassen sind `numeric_tolerance`
  und `business_drift`.
- `make test-replay` ist als eigenes Docker-/Make-Gate vorhanden und in
  `make gates` / `make ci` verdrahtet.

## RM-M5-04-B Ergebnis

- M2-Kompatibilitaet ist nicht mehr nur inventarisiert: `telemetry-linear`,
  `telemetry-schedule-following`, `telemetry-missing-valid-recovery` und
  `telemetry-stale-valid-recovery` besitzen jeweils Manifest, Fixture und
  Golden-Datei unter `tests/fixtures/replay/rm-m5-04/`.
- `TelemetryReplayJsonLoader` unterstuetzt optionale Schedules, damit der
  bestehende Schedule-Following-Golden-Fall ohne Sonderpfad ueber die neue
  Fixture-Struktur laeuft.
- Das M3-Native-Parity-Set bleibt Single Source of Truth unter
  `tests/fixtures/native_parity/cases.v1.json`; das neue
  `native-control-parity`-Manifest referenziert diesen Datensatz per `repo://`
  und pinnt alle 25 Case-Namen als Kompatibilitaetsinventar.
- `make test-replay` prueft alte M2-Harness-Tests und neue Manifest-Goldens im
  selben Gate; `make test-native-parity` bleibt der ueberlappende M3-Pfad.

## RM-M5-04-C Ergebnis

- `NativeParityManifestReplayTests` laedt das RM-M5-04-Manifest
  `tests/fixtures/replay/rm-m5-04/native-parity/manifest.v1.json`, loest die
  `repo://`-Referenz auf `tests/fixtures/native_parity/cases.v1.json` auf und
  prueft, dass Manifest-Inventar und Datensatz exakt dieselben 25 Cases
  enthalten.
- `NativeParityEngineComparisonRunner` fuehrt jeden manifestgelisteten Case
  durch `ManagedControlKernel` und den realen `NativeControlKernel` und erzeugt
  einen Diff-Report mit `numeric_tolerance` und `business_drift`.
- Der bestehende M3-Parity-Test nutzt dieselbe Engine-Ausfuehrung wie der neue
  manifestgetriebene Runner; damit gibt es keinen zweiten, abweichenden
  Native-/Managed-Ausfuehrungspfad.
- `make test-native-parity` ist der ausfuehrende Engine-Vergleich fuer C. Der
  Sidecar-/MPC-Engine-Vergleich bleibt bewusst offen, weil er einen
  Optimierungs-/Orchestrierungsdatensatz braucht und besser mit RM-M5-04-D bzw.
  RM-M5-06 gekoppelt wird.

## RM-M5-04-D Ergebnis

- `ReplayDiffReportJsonWriter` serialisiert M2-Telemetrie-Golden-Diffs als
  `replay-diff-report.v1` mit Dataset, Replay-Art, Match-Status,
  Difference-Anzahl und strukturierten `numeric_tolerance`-/
  `business_drift`-Eintraegen.
- `NativeParityEngineComparisonReportJsonWriter` nutzt dieselbe Report-Schema-
  Version fuer den manifestgetriebenen M3 Managed-vs-Native-Vergleich und
  erweitert Difference-Eintraege um `case_name`, `engine` und `field`.
- Beide Replay-Gates schreiben den JSON-Report in die Assertion-Message und
  optional als Datei unter `BESS_REPLAY_REPORT_DIR`, damit CI die Reports als
  Artefakte sammeln kann.
