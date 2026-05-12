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
| ⬜ | RM-M5-04-B | Vollstaendige M2/M3-Kompatibilitaetsmigration mit Golden-Diff-Matrix | Alle Pflichtfaelle haben Manifest-v1-Aequivalent und alter/neuer Pfad laufen ueberlappend |
| ⬜ | RM-M5-04-C | MPC-/Optimization-Core-Replay-Runner mit Engine-Vergleich | Manifest kann Managed, Native und Sidecar-Sollwerte vergleichen |
| ⬜ | RM-M5-04-D | Entwickler-/CI-Report fuer Replay-Diffs | Maschinenlesbarer Report mit `numeric_tolerance` vs `business_drift` |

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
