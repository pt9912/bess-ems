# Plan RM-M3 Native Control Core + M2-Folgearbeit

**Dokumenttyp:** Offener Detailplan / M3
**Status:** Verfeinert / implementierungsbereit als Slice-Plan; bleibt
unter `open/`, bis M3 aktiviert wird.
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md) (M3,
RM-M3-01..13), [`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
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

Erfolg heisst nicht nur "Native baut": Der Host muss ohne Native Library,
mit falscher ABI und mit nativen Fehlercodes kontrolliert weiterlaufen,
waehrend der native Pfad fuer definierte Replay-Faelle dieselben Commands
wie die .NET-Referenz liefert.

Der Plan ist bewusst so geschnitten, dass der erste Merge noch keinen
produktiven Verhaltenstausch braucht. ABI, Build, lokale Native-Tests,
Interop, Routing und Gate-Aktivierung koennen getrennt reviewed werden,
solange jeder Slice seine Kompatibilitaets- und Fallback-Tests mitbringt.

---

## Zielbild Und Abnahmeschnitt

| Situation | Erwartetes Verhalten | Mindestnachweis |
| --------- | -------------------- | --------------- |
| Native deaktiviert | Control-Cycle nutzt unveraendert den Managed-Pfad. | Unit-/Integrationstest mit expliziter Konfiguration. |
| `.so` fehlt | Host startet, Control-Cycle nutzt Managed-Fallback, Health/Logs/Metrik nennen `library-missing`. | Container-/Startup-Test ohne `/app/native/libbattery_control_core.so`. |
| ABI-Mismatch | Host startet im M3-Default, Native wird nicht genutzt, Health/Logs/Metrik nennen `abi-mismatch`. | ABI-Test mit bewusst falscher Major/Minor-Version. |
| Native liefert `ok` | Native-Command wird verwendet. | Interop-Test plus Parity-Fall. |
| Native liefert `limited` | Native-Command wird verwendet, Limit-Reason bleibt observierbar. | Parity-Fall fuer Constraint und Ramp. |
| Native liefert Fehlerstatus | Managed-Fallback wird fuer denselben Tick neu berechnet; kein stale Native-Ergebnis. | Negativtest je Fehlerklasse. |
| Native wirft intern/Exception im C++ | Export-Funktion faengt ab und liefert Statuscode; keine C++-Exception verlaesst die ABI. | C++- und Interop-Test. |

Produktions-Policy darf spaeter strenger sein (z. B. Startabbruch bei
erwarteter, aber inkompatibler Library). M3 defaultet aber auf
Fallback, weil Architektur §13.4 und LH-NATIVE-004 den sicheren
Weiterbetrieb verlangen.

---

## Abgrenzung

- **In M3:** Native Library, P/Invoke-Adapter, Runtime-Routing,
  Fallback, ABI-/Struct-Layout-Tests, Native-Gates und Parity-Gates.
- **In M3, aber inkrementell:** Constraint und Ramp bilden den ersten
  nativen Safety-Slice. PID wird erst gezogen, wenn Constraint/Ramp lokal,
  ueber P/Invoke und im Replay-Gate stabil sind.
- **M3-abhaengige Folgearbeit:** Optimistic Schedule Replace
  (OP-OPEN-05), Optimization-Lock-Eviction (OP-OPEN-06) und
  Persistenz-Migrations-Tooling, sobald diese Tracks aktiviert werden.
- **Explizit kein Verhaltenstausch:** Die .NET-Implementierung bleibt
  produktionsfaehig, testbar und das Vergleichsoracle fuer Native. M3
  darf keine fachliche Abkuerzung einfuehren, die nur im Native-Pfad lebt.
- **Nicht in diesem Plan:** HIL-Simulator bleibt eigener offener Plan
  [`HIL-simulator.md`](HIL-simulator.md); Regelleistung/OPC-UA bleibt M4;
  MPC/Solver-Sidecar bleibt M5; Operator UI/Multi-Asset-Skalierung bleibt
  M6.

---

## Aktivierungsbedingungen

M3 kann starten, wenn die M2-Pflichtgates stabil gruen sind und ein
Native-Control-Slice priorisiert wird. Der Persistenz-Migrationspfad aus
[`plan-RM-M2-migration.md`](plan-RM-M2-migration.md) ist **keine**
Vorbedingung fuer RM-M3-01..13, solange diese nur Native-/Interop-Code
beruehren. Er wird zur Vorbedingung, sobald OP-OPEN-05, OP-OPEN-06 oder
eine andere schema-veraendernde M3-Arbeit aktiviert wird.

Mindest-Check vor Aktivierung:

| Check | Erwartung |
| ----- | --------- |
| M2-Baseline | `make lint`, `make arch-check`, `make test`, `make coverage-gate`, `make build` und `make runtime` sind auf `main` gruen. |
| Referenzpfad | Managed Control Kernel ist eindeutig lokalisierbar und kann von Native-Parity-Tests direkt aufgerufen werden. |
| Build-Toolchain | Linux-C++-Build in Docker ist verfuegbar; lokale Host-Toolchain ist Komfort, aber keine Voraussetzung. |
| Migrationsbedarf | Kein RM-M3-01..13-Slice verlangt eine DB-Schema-Aenderung. Falls doch: erst RM-M3-FUP-01 ziehen. |
| Doku-Konflikt | Die bekannte Abweichung `docs/user/quality.md` §5.2 "Mismatch -> Service startet nicht" ist als RM-M3-12-Sync-Punkt akzeptiert und blockiert ABI-/Build-Slices nicht. |

Vor dem ersten produktiven Routing-PR muessen zusaetzlich RM-M3-03,
RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10, RM-M3-11 und RM-M3-12
abgeschlossen sein. Sonst darf Native gebaut und getestet, aber nicht als
bevorzugter Runtime-Pfad aktiviert werden.

---

## Komponenten

| Bereich      | Artefakt                                                     | LH-Bezug                 |
| ------------ | ------------------------------------------------------------ | ------------------------ |
| Native       | `native/battery_control_core/` mit C-ABI und C++-Kern        | LH-NATIVE-001..004       |
| Native       | `battery_control_core.h` mit Snapshot/Limits/Command Structs | LH-NATIVE-002/003        |
| Native       | ABI-Version/API fuer Startup-Kompatibilitaetscheck           | LH-NATIVE-005            |
| Adapter      | `BatteryEms.Adapters.NativeInterop` / P/Invoke-Bindings      | LH-NATIVE-001, LH-ARCH-006 |
| Application  | Routing Native bevorzugt, .NET-Fallback bei Fehler/Abwesenheit | LH-ARCH-006, LH-NF-002 |
| Observability | Health-Detail, Log-Reason und Metriken fuer Native-Status     | LH-NATIVE-004/005, LH-OPS-001 |
| Deploy       | Dockerfile Native-Build-Stage + Runtime-Library              | LH-DEPLOY-003/004        |
| Tests/Gates  | Interop-, Parity-, C++-, Sanitizer- und Coverage-Gates       | LH-TEST-001/005          |

Kanonischer .NET-Adaptername fuer M3 ist
`BatteryEms.Adapters.NativeInterop`. Roadmap, Quality-Doku und Architektur
muessen diesen Namen in RM-M3-12 konsistent verwenden, bevor produktives
Routing gemerged wird.

### Implementierungsanker Im Codebestand

M3 darf den bestehenden Hexagon-Schnitt nicht verwischen. Die relevanten
Startpunkte im heutigen Code sind:

| Thema | Bestehender Anker | M3-Konsequenz |
| ----- | ----------------- | ------------- |
| Constraint-Referenz | `src/hexagon/BatteryEms.Domain/ConstraintLimiter.cs` | Native portiert exakt die Reihenfolge und Reasons von `ConstraintLimiter.Apply`. |
| Ramp-Referenz | `src/hexagon/BatteryEms.Domain/RampLimiter.cs` | Native portiert `dt == 0`, `MaxRampKwPerSecond == 0`, positive/negative Delta-Grenzen und negative-`dt`-Fehler. |
| PID-Referenz | `src/hexagon/BatteryEms.Domain/PidController.cs` | RM-M3-13 portiert erst nach stabiler Constraint/Ramp-Parity; State-Felder bleiben expliziter ABI-Vertrag. |
| Routing-Orchestrierung | `src/hexagon/BatteryEms.Application/Control/ControlCycleUseCase.cs` | Der fachliche Cycle bleibt Application-seitig; Native wird als optionaler Kernel-Port eingehangen, nicht als neuer Use-Case. |
| Metrics-Port | `src/hexagon/BatteryEms.Application/Observability/IControlCycleMetrics.cs` | Native-Observability erweitert Ports oder fuehrt einen separaten Port ein, ohne Prometheus in Application zu ziehen. |
| Telemetrie-Adapter | `src/adapters/driven/BatteryEms.Adapters.Telemetry/` | Konkrete Metriknamen und Labels landen im Driven Adapter, nicht im Domain-Code. |
| Worker Composition | `src/adapters/driving/BatteryEms.Worker/WorkerRegistration.cs` | Registrierung schaltet Native per Konfiguration hinzu; Default bleibt ohne vorhandene `.so` lauffaehig. |
| Tests | `tests/hexagon/BatteryEms.Domain.Tests/*LimiterTests.cs`, `tests/hexagon/BatteryEms.Domain.Tests/PidControllerTests.cs`, `tests/hexagon/BatteryEms.Application.Tests/ControlCycle*Tests.cs` | Golden- und Interop-Tests muessen aus diesen Referenzfaellen ableitbar sein. |

Falls fuer Native ein neuer Application-Port eingefuehrt wird, liegt er
unter `src/hexagon/BatteryEms.Application/Control/` oder einem
benachbarten Application-Namespace. Der Port beschreibt eine fachliche
Kernel-Berechnung, nicht P/Invoke-Details. `BatteryEms.Adapters.NativeInterop`
ist der einzige Ort fuer Loader, ABI-Check, DllImport/LibraryImport,
Struct-Marshalling und native Fehlercode-Mapping.

---

## Technische Leitplanken

- **ABI-Form:** Ein public Header unter
  `native/battery_control_core/include/battery_control_core.h`; exportiert
  nur `extern "C"`-Funktionen, primitive Typen, explizit layoutete Structs
  und numerische Statuscodes. Keine C++-Klassen, Exceptions, STL-Typen oder
  Ownership ueber die Sprachgrenze.
- **Struct-Vertrag:** Snapshot, Limits und Command enthalten nur Felder,
  die der aktuelle .NET-Regelpfad wirklich braucht. Feldreihenfolge,
  Groesse, Alignment, Einheit und Vorzeichen werden im Header dokumentiert
  und durch .NET-Layout-Tests abgesichert.
- **Numerik:** Entladen bleibt positiv, Laden negativ. Nicht-finite Werte
  und unplausibler SOC liefern Statuscodes statt impliziter Korrektur. Fuer
  Ramp ist `dt == 0` ein getesteter Hold wie im Managed-Referenzpfad,
  `dt < 0` ist Fehler; fuer PID bleibt `dt <= 0` Fehler. Toleranz fuer
  Native/.NET-Parity startet bei `1e-6 kW` absolut; jede Lockerung braucht
  ADR oder Plan-Entscheidung.
- **Fehlerverhalten:** Native-Fehler fuehren im M3-Default zum
  .NET-Fallback und zu sichtbarer Signalisierung. Der letzte gueltige
  Native-Status darf nicht stillschweigend wiederverwendet werden.
- **Loader-Strategie:** Runtime sucht die `.so` bevorzugt ueber einen
  expliziten, konfigurierbaren Pfad (`/app/native/` im Container). Globales
  `LD_LIBRARY_PATH` ist nur Fallback, nicht Primaermechanismus.
- **Observability:** Mindestens `native_enabled`, `native_loaded`,
  `native_abi_version`, `native_fallback_reason` und ein Fehlercounter fuer
  native Statuscodes sind in Health/Logs/Metriken abbildbar. Namen werden im
  Implementierungsslice finalisiert, muessen aber stabil getestet sein.
- **Build-Reproduzierbarkeit:** Docker ist die Referenzumgebung fuer
  Compiler, Sanitizer und Coverage. Lokale Builds duerfen andere Pfade
  nutzen, muessen aber dieselben Make-Targets ansteuern.

### Runtime-Konfigurationsvertrag

RM-M3-03/RM-M3-05 finalisieren die konkreten Optionsnamen. Bis dahin gilt
dieser Mindestvertrag fuer Reviews und Tests:

| Option | Default in M3 | Bedeutung |
| ------ | ------------- | --------- |
| `NativeControl:Enabled` | `false` fuer fruehe ABI-/Build-Slices; erst nach RM-M3-12 darf ein Runtime-PR `true` als produktiven Profilwert setzen | Expliziter Schalter, ob der Native-Pfad ueberhaupt versucht wird. |
| `NativeControl:LibraryPath` | `/app/native/libbattery_control_core.so` im Container | Primaerer Loader-Pfad; lokale Tests duerfen temporaere Build-Pfade setzen. |
| `NativeControl:ExpectedAbiMajor` | Wert aus Header-Konstante | Major muss exakt passen. |
| `NativeControl:ExpectedAbiMinor` | Wert aus Header-Konstante | Minor darf kompatibel sein; konkrete Regel wird in RM-M3-01 dokumentiert. |
| `NativeControl:FallbackOnError` | `true` | M3-Default: fehlende Library, ABI-Mismatch und Native-Fehler liefern Managed-Fallback. |

Der Adapter muss seinen Initialisierungszustand als eigenes Ergebnis
modellieren, z. B. `disabled`, `loaded`, `library-missing`,
`abi-mismatch`, `load-failed`. Exceptions aus Loader/ABI-Check duerfen
nicht bis in den Control-Cycle durchschlagen. Ein produktives Profil, das
bei erwarteter aber inkompatibler Library den Start abbricht, braucht
einen eigenen Optionswert und Tests; es ist nicht der M3-Default.

---

## Native-Datenvertrag Baseline

RM-M3-01 fixiert die endgueltigen Feldnamen und numerischen
Statuswerte. Als Startpunkt gilt dieser kleinste Vertrag aus dem heutigen
Managed-Regelpfad:

| Native-Struktur | Felder aus .NET-Quelle | Zweck |
| --------------- | ---------------------- | ----- |
| Snapshot | `soc_percent`, `active_power_kw`, `temperature_celsius`, `available`, `quality_usable` | Eingabe fuer Constraint und Plausi. |
| Limits | `max_charge_power_kw`, `max_discharge_power_kw`, `min_soc_percent`, `max_soc_percent`, `max_ramp_kw_per_second`, `min_temperature_celsius`, `max_temperature_celsius` | Asset- und Safety-Grenzen. |
| Request | `target_active_power_kw`, `previous_active_power_kw`, `dt_seconds`, `has_previous` | Optimizer-Setpoint plus Ramp-Kontext. |
| Command | `active_power_kw`, `mode`, `status`, `reason_code` | Ergebnis fuer .NET-Routing und Observability. |

Nicht im ersten ABI-Slice: Asset-ID-Strings, freie Reason-Texte,
`DateTimeOffset`, `FaultStatus`, `DataQuality.Reason`, Markt- oder
Persistenzdaten. Diese bleiben auf .NET-Seite und werden nur in numerische
Flags oder getestete Reason-Codes uebersetzt.

Stale-Snapshot-Entscheidungen, `ValidUntil` und freie
`DataQuality.Reason`-Texte bleiben Managed-Prechecks des Control-Cycles.
Sie werden in .NET-Control-/Mapping-Tests nachgewiesen, aber nicht als
Native-Parity-Faelle gezaehlt, solange die ABI diese Felder nicht fuehrt.

Export-Baseline fuer RM-M3-01:

- `battery_control_core_abi_version()` liefert eine gepackte
  Major/Minor/Patch-Version oder eine eindeutig dokumentierte Alternative.
- Eine Compute-Funktion nimmt ausschliesslich Pointer auf Input-Structs und
  ein Output-Struct entgegen; Rueckgabe ist ein numerischer Statuscode.
- Optional getrennte Funktionen fuer Constraint/Ramp sind erlaubt, wenn
  sie Tests vereinfachen; produktives Routing darf trotzdem nur ueber eine
  orchestrierte Kernel-Fassade laufen.
- Alle Structs sind `sizeof`-/Offset-testbar, ohne variable Laengen,
  ohne Ownership und ohne allokierten Speicher ueber die Grenze.

---

## Umsetzungsslices

| Slice | Inhalt | Exit-Kriterium |
| ----- | ------ | -------------- |
| M3-A ABI + Build-Skelett | `native/battery_control_core`, Header, CMake, leere/Stub-Implementierung, ABI-Version, Docker-Build-Stage. | Native Library baut reproduzierbar und ABI-Version ist aus C++ und .NET testbar; noch kein Runtime-Routing. |
| M3-B Constraint/Ramp Native | C++-Port von `ConstraintLimiter` und `RampLimiter`, Statuscodes, C++-Unit-Tests, Sanitizer. | C++-Tests erreichen alle Constraint-/Ramp-Reason-Codes und alle Fehlerstatus; keine P/Invoke-Abhaengigkeit. |
| M3-C Interop + Fallback | `BatteryEms.Adapters.NativeInterop`, Loader, P/Invoke-Structs, Konfiguration, Managed-Fallback. | Fehlende `.so`, ABI-Mismatch und Native-Fehler fallen deterministisch auf Managed zurueck. |
| M3-D Parity + Gates | Golden-Datensatz, `test-native-interop`, `test-native-parity`, Native-Coverage, CI-/Make-Integration. | `make gates`/`make ci` ziehen Native-Gates reproduzierbar; Toleranzen dokumentiert. |
| M3-E PID-Slice | PID erst nach stabiler Constraint/Ramp-Parity; eigener ABI-/Struct-Delta nur falls noetig. | PID-Parity gegen `PidController.Step`; negative `dt`, non-finite State und Anti-Windup-Faelle getestet. |
| M3-FUP | OP-OPEN-05/06 und Migrationen nur bei Trigger. | Keine Schema- oder Multi-Replica-Arbeit blockiert den Native-Kern ohne konkreten Bedarf. |

### PR-Schnitt Und Review-Grenzen

Damit M3 reviewbar bleibt, sollen PRs entlang dieser Grenzen geschnitten
werden. Ein PR darf kleiner sein, aber nicht mehrere riskante Grenzen ohne
Not vermischen.

| PR-Schnitt | Erlaubte Aenderungen | Nicht mischen mit |
| ---------- | -------------------- | ----------------- |
| ABI/Build | `native/battery_control_core/**`, CMake/Build-Skripte, Header-Tests ohne Runtime-Routing | Application-Routing, Docker-Runtime-Pfad, PID |
| Native Constraint/Ramp | C++-Implementierung und C++-Tests fuer bestehende Domain-Referenzfaelle | P/Invoke, .NET-Registrierung, Schema/FUP |
| Interop Adapter | Neues Adapterprojekt, P/Invoke-Structs, Loader, ABI-Check, Adapter-Unit-Tests | produktives Routing als Default, Dockerfile-Umstellung |
| Application Routing | Application-Port/Facade, Control-Cycle-Integration, Fallback-Tests, Metrics-Port-Erweiterung | Native-Coverage-Gate, PID-ABI-Delta |
| Container/Gates | Dockerfile, Makefile, CI-Target-Verdrahtung, Native-Testcontainer | fachliche Native-Algorithmik |
| PID | ABI-Delta falls noetig, C++ PID, Interop-Mapping, PID-Parity-Goldens | Constraint/Ramp-Erstaktivierung |
| FUP | Migrationen, OP-OPEN-05/06, Multi-Replica-Semantik | Native-Core-Slices RM-M3-01..13 |

Jeder PR nennt im Body: betroffener Slice `M3-A`..`M3-FUP`,
abgeschlossene RM-M3-IDs, Fallback-Verhalten, Testtargets und ob eine
Native Library fuer die Tests gebaut oder bewusst simuliert wurde.

---

## Arbeitspakete

| Status | ID       | Paket                                                              | Abhaengigkeit | DoD |
| ------ | -------- | ------------------------------------------------------------------ | ------------- | --- |
| ⬜     | RM-M3-01 | C-ABI `battery_control_core.h`                                     | M2 done       | Header definiert stabile Structs fuer Snapshot, Limits, Request und Command inklusive expliziter Feldtypen, Alignment-/Packing-Regeln, Einheiten und Vorzeichenkonvention (Entladen positiv). Header ist C-kompatibel, C++-kompilierbar, versioniert und enthaelt keine ABI-relevanten Includes ausser Standard-C-Integer/Float-Typen. Numerische Status- und Reason-Codes sind ab Merge ABI. |
| ⬜     | RM-M3-02 | C++-Implementierung Constraint + Ramp + Statuscode-Fehlerpfade     | RM-M3-01      | Native Kern bildet zuerst die .NET-Referenzlogik aus `ConstraintLimiter.Apply` und `RampLimiter.Apply` ab; PID folgt danach in RM-M3-13 als eigener inkrementeller Slice, sobald Constraint/Ramp-Parity stabil ist. Statuscodes decken ok, limited, invalid-input, non-finite, negative-dt und unsupported-state ab; keine Exception verlaesst den Export-Pfad. |
| ⬜     | RM-M3-03 | ABI-Versionsfunktion + Startup-Check in .NET                      | RM-M3-01      | Native Library exportiert ABI-Version; .NET prueft beim Start erwartete Major/Minor-Kompatibilitaet. ABI-Mismatch fuehrt im M3-Default zu .NET-Fallback mit klarer Health-/Log-/Metric-Signalisierung; Startabbruch bleibt nur eine explizite Produktions-Policy und darf nicht versehentlich aus `docs/user/quality.md` §5.2 uebernommen werden. |
| ⬜     | RM-M3-04 | P/Invoke-Bindings (`BatteryEms.Adapters.NativeInterop`)            | RM-M3-01..03  | Neuer Driven Adapter referenziert nur Application-Ports/Domain; Struct-Layout-Tests pruefen Groesse, Offsets, Calling Convention, Charset-Unabhaengigkeit und Marshal-Verhalten auf Linux x64. Loader-Pfad ist konfigurierbar und defaultet im Container auf `/app/native/libbattery_control_core.so`. |
| ⬜     | RM-M3-05 | Routing: Native bevorzugt, .NET-Fallback bei Fehler/Abwesenheit    | RM-M3-04      | Control-Pfad nutzt Native, wenn Library vorhanden, kompatibel und nicht deaktiviert ist; bei Ladefehler, ABI-Mismatch oder Native-Fehlercode wird deterministisch auf die Managed-Referenz fuer denselben Tick zurueckgerechnet und der Grund observierbar geloggt/gemessen. Konfiguration kann Native explizit deaktivieren. |
| ⬜     | RM-M3-06 | Multi-Stage Dockerfile mit Native-Build-Stage                      | RM-M3-02..05  | Runtime-Image enthaelt die native `.so` unter `/app/native/`; der Host laedt bevorzugt ueber expliziten Loader-Pfad statt globalem `LD_LIBRARY_PATH`. Build-Stage kompiliert C++ reproduzierbar; Container-Smoke beweist, dass Host die Library findet oder sauber fallbackt. |
| ⬜     | RM-M3-07 | Interop-Tests: Struct Layout, ABI, Werte-Paritaet                  | RM-M3-04      | Tests vergleichen Native-Output gegen .NET-Referenz fuer definierte Snapshots/Limits; Toleranzen sind dokumentiert und eng. Negative Tests decken fehlende `.so`, ABI-Mismatch, non-finite Input und nativen Fehlerstatus ab. |
| ⬜     | RM-M3-08 | C++-Unit-Tests                                                     | RM-M3-02      | Native Tests decken Constraint, Ramp, den PID-Slice sobald RM-M3-13 aktiviert ist, NaN/Inf, Vorzeichen, `dt == 0`, negative `dt`, fehlenden Previous-Power-Kontext und alle Limit-Reason-Codes ab. Testdaten sind so gewaehlt, dass jede Statuscode-Variante mindestens einmal direkt im C++-Test erreicht wird. |
| ⬜     | RM-M3-09 | Native-Quality-Gates                                               | RM-M3-08      | `native-lint`, Sanitizer und Native-Coverage laufen reproduzierbar; Native-Coverage bleibt bei 100 % line fuer `native/src/`; Ausschluesse sind nur mit `Why:`-Kommentar und `native-coverage-exclusions` erlaubt. |
| ⬜     | RM-M3-10 | Native/.NET-Parity-Gate ueber Replay-Datensatz                     | RM-M3-05, RM-M3-07 | Kleiner versionierter Golden-Datensatz aus M1/M2-Simulator-Faellen plus Randfaelle fuer SOC, Ramp, Vorzeichen, non-finite Inputs und negative `dt` liefert fuer Native und .NET identische Commands bis auf dokumentierte Toleranzen; Gate ist Pflicht in `make gates`/`make ci`. Stale-Snapshot- und `ValidUntil`-Faelle bleiben separate Managed-Control-/Mapping-Tests. |
| ⬜     | RM-M3-11 | Makefile-Erweiterung um native Targets                            | RM-M3-09, RM-M3-10 | `native-lint`, `test-native-interop`, `test-native-parity`, `native-coverage-gate`, `native-coverage-report`, `native-coverage-exclusions` existieren; `gates`/`ci` ziehen die M3-Gates mit, sobald sie in CI reproduzierbar sind. |
| ⬜     | RM-M3-12 | Doku-/Contract-Sync fuer Native-Policy und Adaptername             | RM-M3-03, RM-M3-05 | `docs/user/quality.md`, `spec/architecture.md`, `docs/plan/planning/in-progress/roadmap.md` und dieser Plan sind konsistent zur M3-Default-Policy: ABI-Mismatch und Native-Fehler fuehren zu .NET-Fallback; Startabbruch ist eine separate Produktions-Policy mit eigenem Test. Der kanonische Adaptername ist ueberall `BatteryEms.Adapters.NativeInterop`. |
| ⬜     | RM-M3-13 | PID Native-Slice                                                   | RM-M3-10, RM-M3-11 | PID wird erst nach stabiler Constraint/Ramp-Parity aktiviert und bildet `PidController.Step` inklusive State-Update, Anti-Windup, Output-Limits und Fehlerpfaden ab. Falls ein ABI-/Struct-Delta noetig ist, wird es versioniert und durch Layout-/Mapping-Tests abgesichert. C++-, Interop- und Parity-Tests decken `dt <= 0`, non-finite Input/State, Saturation, Anti-Windup und State-Fortschreibung ab; Gates bleiben ohne Native Library im normalen Managed-Pfad lauffaehig. |

---

## Akzeptanzdaten

Der Parity-Datensatz startet klein, aber deckt jede fachliche Grenze des
ersten Native-Slices ab. Mindestfaelle:

| Fall | Erwartung |
| ---- | --------- |
| Within limits | Target bleibt unveraendert, Status `ok`, Reason `within-limits`. |
| Max charge | Target unter `-MaxChargePowerKw` wird auf negative Charge-Grenze begrenzt. |
| Max discharge | Target ueber `MaxDischargePowerKw` wird auf positive Discharge-Grenze begrenzt. |
| SOC max | Ladeanforderung bei `SocPercent >= MaxSocPercent` wird auf 0 begrenzt. |
| SOC min | Entladeanforderung bei `SocPercent <= MinSocPercent` wird auf 0 begrenzt. |
| Temperatur | Temperatur ausserhalb Asset-Grenzen wird auf 0 begrenzt. |
| Unavailable | `Available == false` wird auf 0 begrenzt. |
| Ramp hold | `MaxRampKwPerSecond == 0` oder `dt == 0` haelt Previous Power. |
| Ramp up/down | Positive und negative Delta-Grenzen werden exakt eingehalten. |
| No previous | Erster Tick ueberspringt Ramp wie Managed-Pfad. |
| Non-finite | NaN/Inf in Input oder Ergebnis fuehrt zu Fehlerstatus und Managed-Fallback. |
| Negative dt | Native liefert `negative-dt`; .NET routet auf Managed-Fallback. |

Die Golden-Dateien duerfen synthetisch sein, muessen aber die aktuellen
Domain-Tests spiegeln und pro Fall den erwarteten Command mit Status,
Reason-Code, ActivePowerKw und Mode enthalten.

### Abnahmematrix Nach Testtyp

| Nachweis | Primaerer Ort | Muss mindestens beweisen |
| -------- | ------------- | ------------------------ |
| C++-Unit | `native/battery_control_core/tests/` | Constraint/Ramp-Reasons, nicht-finite Inputs, negative `dt`, Exception-Barriere am Export. |
| ABI-Layout | .NET-Interop-Tests + kleiner C++-Layout-Test | `sizeof`, Offsets, Calling Convention, ABI-Version und Statuscode-Breiten stimmen zwischen Header und P/Invoke. |
| Adapter-Negativtests | `BatteryEms.Adapters.NativeInterop.Tests` | fehlende `.so`, falsche ABI, Loader-Fehler, Native-Fehlerstatus und deaktivierte Native-Option liefern kontrollierten Adapterzustand. |
| Application-Fallback | `BatteryEms.Application.Tests/ControlCycle*` oder neues gezieltes Testprojekt | derselbe Tick wird bei Native-Fehler ueber Managed neu berechnet; kein stale Native-Command wird verwendet. |
| Parity-Golden | `test-native-parity` | Native und Managed liefern fuer Golden-Faelle denselben Command bis zur dokumentierten Toleranz. |
| Container-Smoke | `make runtime` oder dediziertes Native-Smoke-Target | Runtime-Image enthaelt `/app/native/libbattery_control_core.so` und Host kann mit und ohne Library kontrolliert starten. |
| Observability | Application-/Telemetry-Tests | Health/Logs/Metriken unterscheiden disabled, loaded, library-missing, abi-mismatch und native-error. |

Testdaten sollen nicht nur erwartete Powers vergleichen. Pro Fall werden
mindestens `active_power_kw`, `mode`, `status`, `reason_code` und
`fallback_reason` (falls Fallback) asserted. Fuer Floating-Point-Vergleiche
gilt die M3-Starttoleranz `1e-6 kW` absolut; Status, Mode und Reason-Codes
muessen exakt matchen.

---

## M2-Folgearbeit Mit M3-Trigger

| Status | ID              | Paket                                      | Aktivierungsbedingung | DoD |
| ------ | --------------- | ------------------------------------------ | --------------------- | --- |
| ⬜     | RM-M3-FUP-01    | Persistenz-Migrationspfad aktivieren       | OP-OPEN-05/06 oder erste echte Schema-Aenderung | `plan-RM-M2-migration.md` zieht nach `in-progress`; MIG-02..05 liefern d-migrate+DbUp, `__schema_versions`, RunOnce-Migrationen und Host-Cut-over. |
| ⬜     | RM-M3-FUP-02    | Optimistic Schedule Replace (OP-OPEN-05)   | Multi-Replica-Optimize oder schema-veraendernder Schedule-Track | `IScheduleRepository.Replace(schedule, expectedBaseVersion)` plus Dapper-`WHERE version = @expected`; Versionskonflikt wird als `Failed` Run mit Reason `concurrent-version-conflict` auditierbar. |
| ⬜     | RM-M3-FUP-03    | Optimization-Lock-Eviction (OP-OPEN-06)    | Ephemere Asset-IDs, Multi-Tenant-Rotation oder wachsende Test-ID-Sets | `_locks` in `DefaultScheduleOptimizationUseCase` bekommt LRU/TTL-Eviction mit konfigurierbarer Schwelle und Metrik `bess_optimization_lock_table_size`. |
| ⬜     | RM-M3-FUP-04    | Telemetrie-Replay-Harness                  | RM-M2-10 wird priorisiert | Versionierte Telemetrie-Goldens werden abgespielt und resultierende Commands gegen Referenzdaten verglichen; Solver-Replay aus M2 bleibt unveraendert. |

---

## Statuscode-Baseline

Die konkreten numerischen Werte werden in RM-M3-01 festgelegt und danach
als ABI behandelt. Die semantische Mindestmenge ist:

| Code | Bedeutung | .NET-Reaktion |
| ---- | --------- | ------------- |
| `ok` | Command ohne native Begrenzung berechnet | Native-Ergebnis verwenden |
| `limited` | Command wurde durch Constraint/Ramp begrenzt | Native-Ergebnis verwenden, Begrenzung observierbar machen |
| `invalid-input` | Pflichtfeld fehlt oder Wertebereich fachlich ungueltig | .NET-Fallback; Safety-Reason erhalten |
| `non-finite` | NaN/Inf in Eingabe oder Ergebnis | .NET-Fallback; Fehlercounter erhoehen |
| `negative-dt` | `dt < 0` fuer Ramp; `dt <= 0` fuer PID | .NET-Fallback; Tick als fehlerhaft markieren |
| `unsupported-state` | ABI gueltig, aber Zustand im Native-Slice nicht implementiert | .NET-Fallback; Warnung statt Crash |

### Reason-Code-Mindestmenge

Die numerischen Werte werden in RM-M3-01 vergeben. Die Namen hier sind die
semantische Untergrenze und muessen aus den heutigen .NET-Reasons ableitbar
bleiben:

| Reason | Quelle / Fall | Erwarteter Status |
| ------ | ------------- | ----------------- |
| `within-limits` | Constraint/Ramp unveraendert | `ok` |
| `asset-unavailable` | `ConstraintLimiter` begrenzt auf 0, falls Native diesen Precheck bekommt | `limited` oder `invalid-input` nach ABI-Entscheidung; M3-A muss das festlegen |
| `temperature-out-of-range` | Temperatur unter/ueber Asset-Grenze | `limited` |
| `soc-at-max-charge-blocked` | Laden bei SOC >= Max | `limited` |
| `soc-at-min-discharge-blocked` | Entladen bei SOC <= Min | `limited` |
| `max-charge-power` | Target unter negativer Charge-Grenze | `limited` |
| `max-discharge-power` | Target ueber positiver Discharge-Grenze | `limited` |
| `ramp-not-permitted` | `dt == 0` oder `MaxRampKwPerSecond == 0` und Target != Previous | `limited` |
| `ramp-down-clamped` | negative Delta-Grenze verletzt | `limited` |
| `ramp-up-clamped` | positive Delta-Grenze verletzt | `limited` |
| `non-finite-input` | NaN/Inf in ABI-Eingabe | `non-finite` |
| `non-finite-output` | Rechenergebnis nicht endlich | `non-finite` |
| `negative-dt` | `dt < 0` fuer Ramp, `dt <= 0` fuer PID | `negative-dt` |
| `unsupported-state` | gueltige ABI, aber Slice kann Fall fachlich nicht berechnen | `unsupported-state` |

Freie .NET-Reason-Texte duerfen nicht ungefiltert in Native wandern.
Native liefert nur numerische Reason-Codes; Mapping auf die heutigen
Reason-Strings passiert auf .NET-Seite und wird getestet.

---

## Observability-Vertrag

M3 erweitert Observability nur um Native-Status, nicht um neue fachliche
Safety-Semantik. Mindestfelder:

| Signal | Erwartung |
| ------ | --------- |
| Health | Native enabled/disabled, loaded yes/no, ABI expected/actual, letzter Fallback-Reason. |
| Logs | Strukturierte Felder `component=native-control`, `asset_id`, `native_status`, `fallback_reason`, `abi_expected`, `abi_actual`. |
| Metrics | Counter fuer Native-Calls nach Status, Counter fuer Fallbacks nach Reason, Gauge/Info fuer geladene ABI-Version. |
| Tests | Fehlende Library, ABI-Mismatch, Native-Fehlerstatus und Native-deaktiviert erscheinen jeweils in mindestens einem Health-/Metric-/Log-Test. |

Konkrete Metriknamen werden im Implementierungsslice finalisiert. Sie
muessen vor RM-M3-12 in `docs/user/quality.md` oder der Observability-Doku
stabil dokumentiert sein.

---

## Gates

- `make lint`, `make test`, `make arch-check`, `make coverage-gate`
  bleiben Baseline.
- Ab RM-M3-09/RM-M3-11 werden zusaetzlich aktiv:
  `native-lint`, `test-native-interop`, `test-native-parity`,
  `native-coverage-gate` und optional `native-coverage-report`.
- `make gates` und `make ci` duerfen erst auf Native-Gates erweitert
  werden, wenn die Targets reproduzierbar in CI laufen.
- Vor Aktivierung in `make ci` muessen die Native-Targets in dieser
  Reihenfolge lokal und im Docker-Build gruen sein:
  `native-lint`, C++-Unit-Tests, `test-native-interop`,
  `test-native-parity`, `native-coverage-gate`.
- RM-M3-06, RM-M3-10, RM-M3-11 und RM-M3-12 sind
  Gate-Voraussetzungen fuer den ersten PR, der Native-Fallback produktiv
  routet, damit Container-Pfad, Replay-Parity, CI-Targets und Doku-Policy
  zusammen nachgewiesen sind.

---

## Entscheidungen

| Kennung       | Frage                                                        | Entscheidung |
| ------------- | ------------------------------------------------------------ | ----------------- |
| RM-M3-OPEN-01 | Welche Native-Core-Komponenten zuerst: Constraint/Ramp/PID komplett oder inkrementell? | **Geschlossen:** inkrementell. Zuerst Constraint + Ramp parity, danach PID als eigener Slice, weil Constraint/Ramp direkt im heutigen ControlCycle liegen und die kleinste native Safety-Oberflaeche bilden. |
| RM-M3-OPEN-02 | Fallback-Policy bei ABI-Mismatch: Startabbruch oder .NET-Fallback? | **Geschlossen:** M3 defaultet auf .NET-Fallback mit klarer Health-/Log-/Metric-Signalisierung. Startabbruch ist nur eine explizite Produktions-Policy. |
| RM-M3-OPEN-03 | Wo liegt die native Library im Runtime-Image?                 | **Geschlossen:** unter `/app/native/`; der Host nutzt bevorzugt einen expliziten Loader-Pfad statt globalem `LD_LIBRARY_PATH`, sofern die Runtime-Implementierung das sauber erlaubt. |
| RM-M3-OPEN-04 | Welcher Replay-Datensatz wird Parity-Golden?                 | **Geschlossen:** kleiner versionierter Datensatz aus M1/M2-Simulator-Faellen plus Randfaelle fuer SOC, Ramp und Vorzeichen. |
| RM-M3-OPEN-05 | Muss der Persistence-Migrationspfad vor Native aktiviert werden? | **Geschlossen:** nein. Der Migrationspfad ist nur fuer schema-veraendernde FUP-Tracks Vorbedingung; Native RM-M3-01..13 bleiben davon entkoppelt. |
| RM-M3-OPEN-06 | Muessen Native-Statuscodes schon vor Implementierung numerisch fixiert werden? | **Geschlossen:** ja, aber erst im ABI-Slice RM-M3-01. Ab Merge des Headers sind numerische Werte ABI und duerfen nur ueber Major-Version-Bump gebrochen werden. |
| RM-M3-OPEN-07 | Muss die Native Library bei jeder lokalen Testausfuehrung vorhanden sein? | **Geschlossen:** nein. Normale .NET-Tests bleiben ohne Native lauffaehig; Native-spezifische Targets bauen oder mounten die `.so` explizit. |
| RM-M3-OPEN-08 | Wie wird die Abweichung `docs/user/quality.md` Startabbruch vs. M3-Fallback behandelt? | **Geschlossen fuer diesen Plan:** RM-M3-12 synchronisiert die Doku. Architektur §13.4 und LH-NATIVE-004 stuetzen Fallback; Startabbruch bleibt optionale Produktions-Policy. |

---

## Risiken Und Gegenmassnahmen

| Risiko | Gegenmassnahme |
| ------ | -------------- |
| ABI driftet unbemerkt zwischen Header und P/Invoke-Structs | Layout-/Offset-Tests plus ABI-Versionstest in RM-M3-04/RM-M3-07. |
| Native-Fehler veraendern Safety-Semantik | .NET-Referenz bleibt Oracle; Native-Fehler fallen auf .NET zurueck; Parity-Goldens enthalten Safety-Randfaelle. |
| Lokale Toolchain unterscheidet sich von CI | Docker-Build ist Referenz; Make-Targets laufen gegen dieselben Stages. |
| Native Coverage wird durch Ausschluesse entwertet | 100-%-Gate fuer `native/src/` plus `native-coverage-exclusions` mit Default-Toleranz 0. |
| M2-Folgearbeit wird versehentlich mit Native-Kern vermischt | FUP-Trigger bleiben explizit; Schema-Aenderungen ziehen zuerst RM-M3-FUP-01. |
| In-Process-Native kann den Host bei Speicherfehlern crashen | Sehr kleine ABI, Sanitizer, C++-Unit-Tests und Default-Fallback bei gemeldeten Fehlern; groessere Solver-/MPC-Kerne bleiben Sidecar-Thema. |
| Quality-Doku und Plan widersprechen sich zur ABI-Mismatch-Policy | RM-M3-12 ist vor produktivem Routing Pflicht und synchronisiert `docs/user/quality.md` §5.2 mit Architektur §13.4. |

---

## Reihenfolge

1. RM-M3-01 legt ABI und Datenvertrag fest.
2. RM-M3-02/RM-M3-08 implementieren und testen Constraint + Ramp lokal;
   PID folgt danach in RM-M3-13 als inkrementeller Native-Slice.
3. RM-M3-03/RM-M3-04 binden Native an .NET an.
4. RM-M3-05/RM-M3-06 liefern Runtime-Routing und Container-Pfad.
5. RM-M3-07/RM-M3-10 bauen Interop- und Parity-Vertrauen auf.
6. RM-M3-09/RM-M3-11 aktivieren Native-Gates in `make gates`/`make ci`.
7. RM-M3-12 synchronisiert Qualitaets-/Architektur-Doku vor produktivem
   Native-Routing.
8. RM-M3-13 zieht PID erst nach stabiler Constraint/Ramp-Parity und
   aktiviert dafuer eigene C++-, Interop- und Parity-Nachweise.
9. RM-M3-FUP-* werden nur gezogen, wenn ihre Trigger eintreten.

---

## Ready-For-Implementation Checklist

- [ ] Native-PR beruehrt keine Persistenzdateien und keine
  schema-veraendernden Migrationen, ausser ein FUP-Trigger ist bewusst
  aktiviert.
- [ ] Jeder PR nennt den betroffenen Slice M3-A..M3-FUP und die
  abgeschlossenen RM-M3-IDs.
- [ ] Managed-Referenztests bleiben ohne Native Library lauffaehig.
- [ ] Native-spezifische Tests bauen oder mounten ihre `.so` explizit.
- [ ] Jede neue Native-Reason oder jeder Statuscode ist im Header,
  in .NET-Mapping-Tests und in diesem Plan bzw. der finalen Doku
  nachgezogen.
