# Plan RM-M3 Native Control Core + M2-Folgearbeit

**Dokumenttyp:** Offener Detailplan / M3
**Status:** Vorgemerkt; bleibt unter `open/`, bis M3 aktiviert wird.
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md) (M3,
RM-M3-01..11), [`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
(OP-OPEN-05/06), [`plan-RM-M2-migration.md`](plan-RM-M2-migration.md)
(Migrationspfad als Vorbedingung fuer schema-veraendernde Folgearbeit),
[`docs/user/quality.md`](../../../user/quality.md) (Native-Gates ab M3),
[`spec/architecture.md`](../../../../spec/architecture.md) (§4.2, §13),
[`spec/lastenheft.md`](../../../../spec/lastenheft.md) (LH-NATIVE-*,
LH-ARCH-006, LH-TEST-005)

---

## Zweck

M3 liefert die native Control-Core-Welle aus der Roadmap: eine
`battery_control_core`-Bibliothek mit stabiler C-ABI, C++-Implementierung
fuer Constraint-, Ramp- und PID-Logik, P/Invoke-Bindings und sauberem
.NET-Fallback. Die bestehende .NET-Logik bleibt Referenzpfad und
Vergleichsoracle; M3 darf keine Safety-Semantik aus M1/M2 veraendern.

Zusaetzlich buendelt dieser Plan die M2-Folgearbeit mit M3-Triggern. Sie
blockiert den nativen Kern nicht pauschal, wird aber Pflicht, sobald ein
M3-Slice eine echte Schema-Aenderung oder Multi-Replica-Semantik braucht.

---

## Abgrenzung

- **In M3:** Native Library, P/Invoke-Adapter, Runtime-Routing,
  Fallback, ABI-/Struct-Layout-Tests, Native-Gates und Parity-Gates.
- **M3-abhaengige Folgearbeit:** Optimistic Schedule Replace
  (OP-OPEN-05), Optimization-Lock-Eviction (OP-OPEN-06) und
  Persistenz-Migrations-Tooling, sobald diese Tracks aktiviert werden.
- **Nicht in diesem Plan:** HIL-Simulator bleibt eigener offener Plan
  [`HIL-simulator.md`](HIL-simulator.md); Regelleistung/OPC-UA bleibt M4;
  MPC/Solver-Sidecar bleibt M5; Operator UI/Multi-Asset-Skalierung bleibt
  M6.

---

## Aktivierungsbedingungen

M3 kann starten, wenn die M2-Pflichtgates stabil gruen sind und ein
Native-Control-Slice priorisiert wird. Der Persistenz-Migrationspfad aus
[`plan-RM-M2-migration.md`](plan-RM-M2-migration.md) ist **keine**
Vorbedingung fuer RM-M3-01..11, solange diese nur Native-/Interop-Code
beruehren. Er wird zur Vorbedingung, sobald OP-OPEN-05, OP-OPEN-06 oder
eine andere schema-veraendernde M3-Arbeit aktiviert wird.

---

## Komponenten

| Bereich      | Artefakt                                                     | LH-Bezug                 |
| ------------ | ------------------------------------------------------------ | ------------------------ |
| Native       | `native/battery_control_core/` mit C-ABI und C++-Kern        | LH-NATIVE-001..004       |
| Native       | `battery_control_core.h` mit Snapshot/Limits/Command Structs | LH-NATIVE-002/003        |
| Native       | ABI-Version/API fuer Startup-Kompatibilitaetscheck           | LH-NATIVE-005            |
| Adapter      | `BatteryEms.Adapters.NativeInterop` / P/Invoke-Bindings      | LH-NATIVE-001, LH-ARCH-006 |
| Application  | Routing Native bevorzugt, .NET-Fallback bei Fehler/Abwesenheit | LH-ARCH-006, LH-NF-002 |
| Deploy       | Dockerfile Native-Build-Stage + Runtime-Library              | LH-DEPLOY-003/004        |
| Tests/Gates  | Interop-, Parity-, C++-, Sanitizer- und Coverage-Gates       | LH-TEST-001/005          |

---

## Arbeitspakete

| Status | ID       | Paket                                                              | Abhaengigkeit | DoD |
| ------ | -------- | ------------------------------------------------------------------ | ------------- | --- |
| ⬜     | RM-M3-01 | C-ABI `battery_control_core.h`                                     | M2 done       | Header definiert stabile Structs fuer Snapshot, Limits und Command inklusive expliziter Feldtypen, Alignment-/Packing-Regeln und Vorzeichenkonvention (Entladen positiv). Header ist C-kompatibel, C++-kompilierbar und versioniert. |
| ⬜     | RM-M3-02 | C++-Implementierung Constraint + Ramp + Statuscode-Fehlerpfade     | RM-M3-01      | Native Kern bildet zuerst die .NET-Referenzlogik fuer ConstraintLimiter und RampLimiter ab; PID folgt danach als eigener inkrementeller Slice, sobald Constraint/Ramp-Parity stabil ist. Statuscodes decken ok, limited, invalid-input, non-finite, negative-dt und unsupported-state ab. |
| ⬜     | RM-M3-03 | ABI-Versionsfunktion + Startup-Check in .NET                      | RM-M3-01      | Native Library exportiert ABI-Version; .NET prueft beim Start erwartete Major/Minor-Kompatibilitaet. ABI-Mismatch fuehrt im M3-Default zu .NET-Fallback mit klarer Health-/Log-/Metric-Signalisierung; Startabbruch bleibt nur eine explizite Produktions-Policy. |
| ⬜     | RM-M3-04 | P/Invoke-Bindings (`BatteryEms.Adapters.NativeInterop`)            | RM-M3-01..03  | Neuer Driven Adapter referenziert nur Application-Ports/Domain; Struct-Layout-Tests pruefen Groesse, Offsets, Calling Convention und Marshal-Verhalten. |
| ⬜     | RM-M3-05 | Routing: Native bevorzugt, .NET-Fallback bei Fehler/Abwesenheit    | RM-M3-04      | Control-Pfad nutzt Native, wenn Library vorhanden und kompatibel ist; bei Ladefehler, ABI-Mismatch oder Native-Fehlercode wird deterministisch auf .NET referenziert und der Grund observierbar geloggt/gemessen. |
| ⬜     | RM-M3-06 | Multi-Stage Dockerfile mit Native-Build-Stage                      | RM-M3-02..05  | Runtime-Image enthaelt die native `.so` unter `/app/native/`; der Host laedt bevorzugt ueber expliziten Loader-Pfad statt globalem `LD_LIBRARY_PATH`. Build-Stage kompiliert C++; Container-Smoke beweist, dass Host die Library findet oder sauber fallbackt. |
| ⬜     | RM-M3-07 | Interop-Tests: Struct Layout, ABI, Werte-Paritaet                  | RM-M3-04      | Tests vergleichen Native-Output gegen .NET-Referenz fuer definierte Snapshots/Limits; Toleranzen sind dokumentiert und eng. |
| ⬜     | RM-M3-08 | C++-Unit-Tests                                                     | RM-M3-02      | Native Tests decken Constraint, Ramp, PID, NaN/Inf, Vorzeichen und negative `dt` ab. |
| ⬜     | RM-M3-09 | Native-Quality-Gates                                               | RM-M3-08      | `native-lint`, Sanitizer und Native-Coverage laufen reproduzierbar; Ausschluesse sind dokumentiert. |
| ⬜     | RM-M3-10 | Native/.NET-Parity-Gate ueber Replay-Datensatz                     | RM-M3-05, RM-M3-07 | Kleiner versionierter Golden-Datensatz aus M1/M2-Simulator-Faellen plus Randfaelle fuer SOC, Ramp und Vorzeichen liefert fuer Native und .NET identische Commands bis auf dokumentierte Toleranzen; Gate ist Pflicht in `make gates`/`make ci`. |
| ⬜     | RM-M3-11 | Makefile-Erweiterung um native Targets                            | RM-M3-09, RM-M3-10 | `native-lint`, `test-native-interop`, `test-native-parity`, `native-coverage-gate`, `native-coverage-report`, `native-coverage-exclusions` existieren; `gates`/`ci` ziehen die M3-Gates mit. |

---

## M2-Folgearbeit Mit M3-Trigger

| Status | ID              | Paket                                      | Aktivierungsbedingung | DoD |
| ------ | --------------- | ------------------------------------------ | --------------------- | --- |
| ⬜     | RM-M3-FUP-01    | Persistenz-Migrationspfad aktivieren       | OP-OPEN-05/06 oder erste echte Schema-Aenderung | `plan-RM-M2-migration.md` zieht nach `in-progress`; MIG-02..05 liefern d-migrate+DbUp, `__schema_versions`, RunOnce-Migrationen und Host-Cut-over. |
| ⬜     | RM-M3-FUP-02    | Optimistic Schedule Replace (OP-OPEN-05)   | Multi-Replica-Optimize oder schema-veraendernder Schedule-Track | `IScheduleRepository.Replace(schedule, expectedBaseVersion)` plus Dapper-`WHERE version = @expected`; Versionskonflikt wird als `Failed` Run mit Reason `concurrent-version-conflict` auditierbar. |
| ⬜     | RM-M3-FUP-03    | Optimization-Lock-Eviction (OP-OPEN-06)    | Ephemere Asset-IDs, Multi-Tenant-Rotation oder wachsende Test-ID-Sets | `_locks` in `DefaultScheduleOptimizationUseCase` bekommt LRU/TTL-Eviction mit konfigurierbarer Schwelle und Metrik `bess_optimization_lock_table_size`. |
| ⬜     | RM-M3-FUP-04    | Telemetrie-Replay-Harness                  | RM-M2-10 wird priorisiert | Versionierte Telemetrie-Goldens werden abgespielt und resultierende Commands gegen Referenzdaten verglichen; Solver-Replay aus M2 bleibt unveraendert. |

---

## Gates

- `make lint`, `make test`, `make arch-check`, `make coverage-gate`
  bleiben Baseline.
- Ab RM-M3-09/RM-M3-11 werden zusaetzlich aktiv:
  `native-lint`, `test-native-interop`, `test-native-parity`,
  `native-coverage-gate` und optional `native-coverage-report`.
- `make gates` und `make ci` duerfen erst auf Native-Gates erweitert
  werden, wenn die Targets reproduzierbar in CI laufen.

---

## Entscheidungen

| Kennung       | Frage                                                        | Entscheidung |
| ------------- | ------------------------------------------------------------ | ----------------- |
| RM-M3-OPEN-01 | Welche Native-Core-Komponenten zuerst: Constraint/Ramp/PID komplett oder inkrementell? | **Geschlossen:** inkrementell. Zuerst Constraint + Ramp parity, danach PID als eigener Slice, weil Constraint/Ramp direkt im heutigen ControlCycle liegen und die kleinste native Safety-Oberflaeche bilden. |
| RM-M3-OPEN-02 | Fallback-Policy bei ABI-Mismatch: Startabbruch oder .NET-Fallback? | **Geschlossen:** M3 defaultet auf .NET-Fallback mit klarer Health-/Log-/Metric-Signalisierung. Startabbruch ist nur eine explizite Produktions-Policy. |
| RM-M3-OPEN-03 | Wo liegt die native Library im Runtime-Image?                 | **Geschlossen:** unter `/app/native/`; der Host nutzt bevorzugt einen expliziten Loader-Pfad statt globalem `LD_LIBRARY_PATH`, sofern die Runtime-Implementierung das sauber erlaubt. |
| RM-M3-OPEN-04 | Welcher Replay-Datensatz wird Parity-Golden?                 | **Geschlossen:** kleiner versionierter Datensatz aus M1/M2-Simulator-Faellen plus Randfaelle fuer SOC, Ramp und Vorzeichen. |
| RM-M3-OPEN-05 | Muss der Persistence-Migrationspfad vor Native aktiviert werden? | **Geschlossen:** nein. Der Migrationspfad ist nur fuer schema-veraendernde FUP-Tracks Vorbedingung; Native RM-M3-01..11 bleiben davon entkoppelt. |

---

## Reihenfolge

1. RM-M3-01 legt ABI und Datenvertrag fest.
2. RM-M3-02/RM-M3-08 implementieren und testen Constraint + Ramp lokal;
   PID folgt danach als inkrementeller Native-Slice.
3. RM-M3-03/RM-M3-04 binden Native an .NET an.
4. RM-M3-05/RM-M3-06 liefern Runtime-Routing und Container-Pfad.
5. RM-M3-07/RM-M3-10 bauen Interop- und Parity-Vertrauen auf.
6. RM-M3-09/RM-M3-11 aktivieren Native-Gates in `make gates`/`make ci`.
7. RM-M3-FUP-* werden nur gezogen, wenn ihre Trigger eintreten.
