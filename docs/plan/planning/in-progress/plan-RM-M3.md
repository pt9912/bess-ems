# Plan RM-M3 Native Control Core + M2-Folgearbeit

**Dokumenttyp:** Aktiver Detailplan / M3
**Status:** In Arbeit — RM-M3-01 (C-ABI-Header) wird als Erstes gezogen,
restliche Pakete folgen in der unten dokumentierten Slice-Reihenfolge.
Aktivierungsbedingungen vor Beginn verifiziert: M2-Baseline grün auf
`main`, Referenzpfad lokalisierbar, Build-Toolchain via Docker
verfügbar, kein Migrationsbedarf für RM-M3-01..13.
**Bezug:**
[`roadmap.md`](roadmap.md) (M3,
RM-M3-01..13), [`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
(OP-OPEN-05/06), [`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md)
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

| Situation | Konfiguration | Erwartetes Verhalten | Mindestnachweis |
| --------- | ------------- | -------------------- | --------------- |
| Native deaktiviert | `NativeControl:Enabled=false` | Control-Cycle nutzt unveraendert den Managed-Pfad; Health/Logs/Metrik nennen `disabled`, nicht `library-missing`. | Unit-/Integrationstest mit expliziter Konfiguration. |
| `.so` fehlt | `NativeControl:Enabled=true`, Library-Pfad zeigt auf fehlende Datei | Host startet, Control-Cycle nutzt Managed-Fallback, Health/Logs/Metrik nennen `library-missing`. | Container-/Startup-Test ohne `/app/native/libbattery_control_core.so`. |
| ABI-Mismatch | `NativeControl:Enabled=true`, Library vorhanden | Host startet im M3-Default, Native wird nicht genutzt, Health/Logs/Metrik nennen `abi-mismatch`. | ABI-Test mit bewusst falscher Major/Minor-Version. |
| Native liefert `ok` | `NativeControl:Enabled=true`, ABI kompatibel | Native-Command wird verwendet. | Interop-Test plus Parity-Fall. |
| Native liefert `limited` | `NativeControl:Enabled=true`, ABI kompatibel | Native-Command wird verwendet, Limit-Reason bleibt observierbar. | Parity-Fall fuer Constraint und Ramp. |
| Native liefert fallbackfaehigen Fehlerstatus aus gueltigem .NET-Kontext | `NativeControl:Enabled=true`, ABI kompatibel | Managed-Fallback wird fuer denselben Tick neu berechnet; kein stale Native-Ergebnis. | Negativtest fuer native Fehler aus gueltigen Eingaben, z. B. `unsupported-state` oder nicht-finites Native-Ergebnis. |
| Nicht-finite oder fachlich ungueltige Control-Eingabe | unabhaengig von Native | Application-/Mapping-Precheck liefert Safe-Stop oder invaliden Snapshot/Dispatch, bevor Native oder Managed-Kernel gerechnet werden; kein Fallback-Loop mit denselben ungueltigen Werten. | Tests fuer Snapshot-SOC/SOH/Active-Power/Temperatur, Dispatch-Target, Limits/Request-Mapping. |
| Native wirft intern/Exception im C++ | `NativeControl:Enabled=true`, ABI kompatibel | Export-Funktion faengt ab und liefert Statuscode; keine C++-Exception verlaesst die ABI. | C++- und Interop-Test. |

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
  (OP-OPEN-05), Optimization-Lock-Eviction (OP-OPEN-06) und erste echte
  Folgemigrationen ueber das vorhandene Migrations-Tooling, sobald diese
  Tracks aktiviert werden.
- **Explizit kein Verhaltenstausch:** Die .NET-Implementierung bleibt
  produktionsfaehig, testbar und das Vergleichsoracle fuer Native. M3
  darf keine fachliche Abkuerzung einfuehren, die nur im Native-Pfad lebt.
- **Nicht in diesem Plan:** HIL-Simulator bleibt eigener abgeschlossener Plan
  [`../done/HIL-simulator.md`](../done/HIL-simulator.md); Regelleistung/OPC-UA bleibt M4;
  MPC/Solver-Sidecar bleibt M5; Operator UI/Multi-Asset-Skalierung bleibt
  M6.

---

## Aktivierungsbedingungen

M3 kann starten, wenn die M2-Pflichtgates stabil gruen sind und ein
Native-Control-Slice priorisiert wird. Der Persistenz-Migrationspfad aus
[`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md) ist
bereits abgeschlossen und **keine** Vorbedingung fuer RM-M3-01..13,
solange diese nur Native-/Interop-Code beruehren. Er wird erst konsumiert,
sobald OP-OPEN-05, OP-OPEN-06 oder eine andere schema-veraendernde
M3-Arbeit aktiviert wird.

Mindest-Check vor Aktivierung:

| Check | Erwartung |
| ----- | --------- |
| M2-Baseline | `make lint`, `make arch-check`, `make test`, `make coverage-gate`, `make build` und `make runtime` sind auf `main` gruen. RM-M2-10 ist abgeschlossen; die Telemetrie- und Solver-Replay-Nachweise laufen derzeit ueber die bestehenden Testprojekte im `make test`-/CI-Pfad. Falls spaeter ein dediziertes `make test-replay` eingefuehrt wird, darf es zusaetzlich Gate werden, ist aber keine aktuelle M3-Aktivierungsvoraussetzung. |
| Referenzpfad | Managed Control Kernel ist eindeutig lokalisierbar und kann von Native-Parity-Tests direkt aufgerufen werden. |
| Build-Toolchain | Linux-C++-Build in Docker ist verfuegbar; lokale Host-Toolchain ist Komfort, aber keine Voraussetzung. |
| Migrationsbedarf | Kein RM-M3-01..13-Slice verlangt eine DB-Schema-Aenderung. Falls doch: erst RM-M3-FUP-01 ziehen. |
| Doku-Policy | `docs/user/quality.md` §5.2 und Architektur §13.4 beschreiben dieselbe M3-Default-Policy: ABI-Mismatch fuehrt zu .NET-Fallback, nicht zu Startabbruch. RM-M3-03 haelt diesen Vertrag beim ABI-Policy-Slice synchron; der erste PR mit ABI-Mismatch-Verhalten darf keinen widerspruechlichen Quality-Vertrag hinterlassen. Bekannter Doku-Drift vor M3: `docs/user/quality.md` nennt fuer den Header noch `native/include/battery_control_core.h`; RM-M3-03 oder spaetestens RM-M3-12 muss den Pfad auf `native/battery_control_core/include/battery_control_core.h` angleichen. |
| Managed-Precheck-Gap | Der heutige Snapshot-/Control-Pfad faengt bereits unbrauchbare Qualitaet, stale Snapshots, `Available == false`, SOC/SOH ausserhalb 0..100 und nicht-finite Active Power ab. Vor RM-M3-05 muessen zusaetzlich nicht-finite SOC/SOH, nicht-finite oder unplausible Temperatur sowie nicht-finite Dispatch-/Limit-/Request-Mapping-Werte als Managed-Precheck nachgewiesen sein. |

RM-M3-05 darf Routing nur hinter expliziter Test-/Profilkonfiguration
einfuehren. Vor dem ersten PR, der Native als produktiven Default oder als
bevorzugten Runtime-Pfad in einem produktionsnahen Profil aktiviert,
muessen RM-M3-03, RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10, RM-M3-11 und
RM-M3-12 abgeschlossen sein. Sonst darf Native gebaut, geladen und
konfiguriert getestet, aber nicht als produktiver Default aktiviert
werden.

---

## Komponenten

| Bereich      | Artefakt                                                     | LH-Bezug                 |
| ------------ | ------------------------------------------------------------ | ------------------------ |
| Native       | `native/battery_control_core/` mit C-ABI und C++-Kern        | LH-NATIVE-001..004       |
| Native       | `battery_control_core.h` mit Snapshot/Limits/Command Structs | LH-NATIVE-002/003        |
| Native       | ABI-Version/API fuer Startup-Kompatibilitaetscheck           | LH-NATIVE-005            |
| Adapter      | `BatteryEms.Adapters.NativeInterop` / P/Invoke-Bindings      | LH-NATIVE-001, LH-ARCH-006 |
| Application  | Routing zunaechst explizit aktivierbar; produktiv Native bevorzugt erst nach Gates, .NET-Fallback bei Fehler/Abwesenheit | LH-ARCH-006, LH-NF-002 |
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

Die Verantwortungsgrenze fuer Fallback ist zweigeteilt: Der Adapter liefert
einen stabilen Initialisierungszustand und native Call-Ergebnisse inklusive
Status-/Reason-Codes. Die Application entscheidet im Control-Cycle, ob das
Native-Ergebnis verwendet wird oder ob derselbe Tick ueber die Managed-
Referenz neu berechnet wird. Wenn Architektur §13.4 von automatischem
Fallback spricht, meint das diese Port-/Application-Komposition, nicht einen
Adapter, der eigenstaendig andere Adapter oder Use-Cases aufruft.

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
  und unplausibler SOC werden im Application-/Mapping-Precheck abgefangen,
  bevor Native oder der Managed-Kernel rechnen. Falls solche Werte trotzdem
  die ABI erreichen, liefert Native einen Fehlerstatus; .NET darf dann nicht
  denselben ungueltigen Input blind in die Managed-Referenz weiterreichen.
  Fuer Ramp ist `dt == 0` ein getesteter Hold wie im Managed-Referenzpfad,
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
| `NativeControl:FallbackOnError` | `true` | M3-Default: fehlende Library, ABI-Mismatch und native Fehler aus gueltigem .NET-Kontext liefern Managed-Fallback. Precheck-/Mapping-Fehler mit ungueltigen Eingaben bleiben Safe-Stop/invalides Mapping, kein Blind-Fallback. |

Der Adapter muss seinen Initialisierungszustand als eigenes Ergebnis
modellieren, z. B. `disabled`, `loaded`, `library-missing`,
`abi-mismatch`, `load-failed`. Exceptions aus Loader/ABI-Check duerfen
nicht bis in den Control-Cycle durchschlagen. Ein produktives Profil, das
bei erwarteter aber inkompatibler Library den Start abbricht, braucht
einen eigenen Optionswert und Tests; es ist nicht der M3-Default.

`library-missing` ist nur ein Loader-Zustand, wenn Native explizit
aktiviert ist. Bei `NativeControl:Enabled=false` wird die Library nicht
gesucht; Health/Logs/Metriken muessen diesen Fall als `disabled`
ausweisen.

---

## Native-Datenvertrag Baseline

RM-M3-01 fixiert die endgueltigen Feldnamen und numerischen
Statuswerte. Als Startpunkt gilt dieser kleinste Vertrag aus dem heutigen
Managed-Regelpfad:

| Native-Struktur | Felder aus .NET-Quelle | Zweck |
| --------------- | ---------------------- | ----- |
| Snapshot | `soc_percent`, `active_power_kw`, `temperature_celsius` | Eingabe fuer Constraint und Plausi nach Managed-Prechecks. |
| Limits | `max_charge_power_kw`, `max_discharge_power_kw`, `min_soc_percent`, `max_soc_percent`, `max_ramp_kw_per_second`, `min_temperature_celsius`, `max_temperature_celsius` | Asset- und Safety-Grenzen. |
| Request | `target_active_power_kw`, `previous_active_power_kw`, `dt_seconds`, `has_previous` | Optimizer-Setpoint plus Ramp-Kontext. |
| Command | `active_power_kw`, `mode`, `status`, `reason_code` | Ergebnis fuer .NET-Routing und Observability. |

Nicht im ersten ABI-Slice: Asset-ID-Strings, freie Reason-Texte,
`DateTimeOffset`, `Available`, `FaultStatus`, `DataQuality.Reason`, Markt-
oder Persistenzdaten. Diese bleiben auf .NET-Seite und werden nur in
numerische Flags oder getestete Reason-Codes uebersetzt, falls ein
spaeterer ABI-Slice sie wirklich braucht.

Stale-Snapshot-Entscheidungen, `ValidUntil` und freie
`DataQuality.Reason`-Texte bleiben Managed-Prechecks des Control-Cycles.
Dasselbe gilt fuer `Available == false`, unbrauchbare Snapshot-Qualitaet
und nicht-finite oder unplausible Werte in Snapshot, Dispatch, Limits oder
Request. Der aktuelle Managed-Pfad ist dafuer noch nicht vollstaendig
M3-bereit: Er deckt stale/unusable Snapshot-Qualitaet, `Available ==
false`, SOC/SOH ausserhalb 0..100 und nicht-finite Active Power ab, aber
nicht alle nicht-finiten SOC/SOH-/Temperatur- und Mapping-Werte. Diese
Luecke ist Teil von RM-M3-05, bevor Native-Routing in einem Control-Cycle
aktiviert wird. Die .NET-Precheck-Tests muessen mindestens SOC, SOH, Active
Power und Temperatur auf finite/plausible Werte abdecken; zusaetzliche
Limit- und Request-Werte werden im Mapping vor Kernel-Aufruf validiert.
Diese Faelle werden in .NET-Control-/Mapping-Tests nachgewiesen, aber
nicht als Native-Parity-Faelle gezaehlt, solange die ABI diese Felder nicht
fuehrt.

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
| M3-A ABI + Build-Skelett | `native/battery_control_core`, Header, CMake, leere/Stub-Implementierung, ABI-Version, frueher nativer Build-Stage ohne Runtime-Image-Pfad. | Native Library baut reproduzierbar im Build-Container und ABI-Version ist aus C++ testbar; noch kein Runtime-Routing und noch kein Worker-Image mit `/app/native/`. |
| M3-B Constraint/Ramp Native | C++-Port von `ConstraintLimiter` und `RampLimiter`, Statuscodes, C++-Unit-Tests, Sanitizer. | C++-Tests erreichen alle Constraint-/Ramp-Reason-Codes und alle Fehlerstatus; keine P/Invoke-Abhaengigkeit. |
| M3-C Interop + Fallback | `BatteryEms.Adapters.NativeInterop`, Loader, P/Invoke-Structs, Konfiguration, Routing hinter expliziter Test-/Profilkonfiguration, Managed-Fallback. | Fehlende `.so`, ABI-Mismatch und native Fehler aus gueltigem .NET-Kontext fallen deterministisch auf Managed zurueck; produktive Default-Aktivierung bleibt gesperrt. |
| M3-D Parity + Gates | Golden-Datensatz, Interop-/Parity-Runner, Native-Coverage, danach CI-/Make-Integration. | Parity und Coverage laufen zuerst standalone reproduzierbar; erst RM-M3-11 verdrahtet sie in `make gates`/`make ci`; noch keine produktive Profilaktivierung in diesem Slice. |
| M3-D2 Produktive Profilaktivierung | Separater Folge-Slice nach abgeschlossenen RM-M3-03, RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10, RM-M3-11 und RM-M3-12. | Ein produktionsnahes Profil darf Native als bevorzugten Runtime-Pfad setzen; Fallback-, Container-, Interop-, Gate- und Doku-Nachweise sind bereits auf `main` gruen. |
| M3-E PID-Slice | PID erst nach stabiler Constraint/Ramp-Parity; eigener ABI-/Struct-Delta nur falls noetig. | PID-Parity gegen `PidController.Step`; negative `dt`, non-finite State und Anti-Windup-Faelle getestet. |
| M3-FUP | OP-OPEN-05/06 und Migrationen nur bei Trigger. | Keine Schema- oder Multi-Replica-Arbeit blockiert den Native-Kern ohne konkreten Bedarf. |

`RM-M3-06` bleibt bewusst eine Roadmap-ID. Innerhalb dieses Plans wird sie
in zwei Review-Grenzen geschnitten: Teil 1 ist der fruehe Native-Build-Stage
und darf nach RM-M3-01 im ABI/Build-Schnitt landen; Teil 2 ist der
Runtime-Image-Pfad `/app/native/` plus Container-Smoke und braucht das
Routing-/Fallback-Verhalten aus RM-M3-05. Diese Unterteilung erzeugt keine
neuen Roadmap-IDs.

### PR-Schnitt Und Review-Grenzen

Damit M3 reviewbar bleibt, sollen PRs entlang dieser Grenzen geschnitten
werden. Ein PR darf kleiner sein, aber nicht mehrere riskante Grenzen ohne
Not vermischen.

| PR-Schnitt | Erlaubte Aenderungen | Nicht mischen mit |
| ---------- | -------------------- | ----------------- |
| ABI/Build | `native/battery_control_core/**`, CMake/Build-Skripte, frueher nativer Docker-Build-Stage, Header-Tests ohne Runtime-Routing | Application-Routing, Worker-Runtime-Image-Pfad `/app/native/`, PID |
| Native Constraint/Ramp | C++-Implementierung und C++-Tests fuer bestehende Domain-Referenzfaelle | P/Invoke, .NET-Registrierung, Schema/FUP |
| Interop Adapter | Neues Adapterprojekt, P/Invoke-Structs, Loader, ABI-Check, Adapter-Unit-Tests | produktives Routing als Default, Dockerfile-Umstellung |
| Application Routing | Application-Port/Facade, Control-Cycle-Integration hinter expliziter Test-/Profilkonfiguration, Fallback-Tests, Metrics-Port-Erweiterung | produktive Default-Aktivierung, Native-Coverage-Gate, PID-ABI-Delta |
| Container/Gates | Worker-Runtime-Image-Pfad `/app/native/`, Makefile, CI-Target-Verdrahtung, Native-Testcontainer | fachliche Native-Algorithmik, produktive Profilaktivierung |
| Produktive Profilaktivierung | Separater Folge-PR nach abgeschlossenen RM-M3-03, RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10, RM-M3-11 und RM-M3-12; setzt ein produktionsnahes Profil auf Native-bevorzugt. | fachliche Native-Algorithmik, Gate-Erstaktivierung, Doku-Sync |
| PID | ABI-Delta falls noetig, C++ PID, Interop-Mapping, PID-Parity-Goldens | Constraint/Ramp-Erstaktivierung |
| FUP | Migrationen, OP-OPEN-05/06, Multi-Replica-Semantik | Native-Core-Slices RM-M3-01..13 |

Jeder PR nennt im Body: betroffener Slice `M3-A`..`M3-FUP` bzw.
`M3-D2`, abgeschlossene RM-M3-IDs, Fallback-Verhalten, Testtargets und ob
eine Native Library fuer die Tests gebaut oder bewusst simuliert wurde.

---

## Arbeitspakete

| Status | ID       | Paket                                                              | Abhaengigkeit | DoD |
| ------ | -------- | ------------------------------------------------------------------ | ------------- | --- |
| ✅     | RM-M3-01 | C-ABI `battery_control_core.h`                                     | M2 done       | Header `native/battery_control_core/include/battery_control_core.h` definiert die vier Structs (`bcc_snapshot_t`, `bcc_limits_t`, `bcc_request_t`, `bcc_command_t`), 6 Statuscodes, 13 Reason-Codes (1:1 zu den heutigen managed Reason-Strings) und 4 Mode-Werte (passend zu `BatteryEms.Domain.CommandMode`). ABI-Version 0.1.0 als gepacktes uint32 via `battery_control_core_abi_version()`. Booleans als int32_t 0/1, keine ABI-relevanten Includes ausser `<stdint.h>`, `extern "C"`-Block für C++-Kompatibilität. Discharge-positive Vorzeichenkonvention dokumentiert. Header parst sauber unter `gcc -xc -fsyntax-only -Wall -Wextra -pedantic -std=c11` und `g++ -xc++ -std=c++17`. README `native/battery_control_core/README.md` hält Slice-Status für die folgenden M3-Pakete. |
| ✅     | RM-M3-02 | C++-Implementierung Constraint + Ramp + Statuscode-Fehlerpfade     | RM-M3-01      | `native/battery_control_core/src/compute.cpp` bildet `ConstraintLimiter.Apply` und `RampLimiter.Apply` 1:1 ab (Reason-Reihenfolge, Sign-Konvention, dt==0/max_ramp==0-Pfade). Combination-Regel: finale Leistung = Ramp-Ergebnis, Reason folgt Constraint wenn Constraint limitiert hat (Plan §Akzeptanzdaten). Statuscodes ok/limited/invalid-input/non-finite/negative-dt/unsupported-state alle erreichbar; ein `try/catch (...)` um den Body verhindert dass eine Exception die C-ABI verlässt. Erste hand-rolled Test-Suite unter `tests/test_compute.cpp` (15 Cases / 43 Asserts grün) deckt alle 13 Reason-Codes plus NaN-Input, negative dt und Null-Pointer. PID kommt mit RM-M3-13. Sanitizer + framework-basierte breite Coverage folgen mit RM-M3-08/09. |
| ⬜     | RM-M3-03 | ABI-Versionsfunktion + Startup-Check in .NET                      | RM-M3-01      | Native Library exportiert ABI-Version; `BatteryEms.Adapters.NativeInterop` liefert einen minimalen Loader-/Startup-Check, der bei `NativeControl:Enabled=false` nicht laedt, bei fehlender Library `library-missing`, bei Ladefehler `load-failed`, bei inkompatibler Major/Minor-Version `abi-mismatch` und bei kompatibler Library `loaded` meldet. `docs/user/quality.md` §5.2 bleibt zur M3-Default-Policy synchron: ABI-Mismatch fuehrt zu .NET-Fallback mit klarer Health-/Log-/Metric-Signalisierung; Startabbruch bleibt nur eine explizite Produktions-Policy mit eigenem Optionswert und Test. Noch keine produktive Compute-Bindings oder Routing-Aktivierung. |
| ⬜     | RM-M3-04 | P/Invoke-Bindings (`BatteryEms.Adapters.NativeInterop`)            | RM-M3-01..03  | Neuer Driven Adapter referenziert nur Application-Ports/Domain; Compute-Bindings und P/Invoke-Structs fuer Snapshot, Limits, Request und Command pruefen erwartete ABI-Major/Minor-Kompatibilitaet vor jedem produktiven Native-Call. Struct-Layout-Tests pruefen Groesse, Offsets, Calling Convention, Charset-Unabhaengigkeit und Marshal-Verhalten auf Linux x64. Loader-Pfad ist konfigurierbar und defaultet im Container auf `/app/native/libbattery_control_core.so`. |
| ⬜     | RM-M3-05 | Routing hinter expliziter Konfiguration, .NET-Fallback bei Fehler/Abwesenheit | RM-M3-04      | Control-Pfad kann Native nutzen, wenn Library vorhanden, kompatibel, explizit aktiviert und die Control-Eingaben endlich/plausibel sind; bei Ladefehler, ABI-Mismatch oder Native-Fehlercode aus gueltigen Eingaben wird deterministisch auf die Managed-Referenz fuer denselben Tick zurueckgerechnet und der Grund observierbar geloggt/gemessen. Nicht-finite oder fachlich ungueltige Snapshot-/Dispatch-/Limit-/Request-Werte werden vor Kernel-Aufruf als Managed-Precheck behandelt und fuehren nicht zu einem Blind-Fallback mit denselben ungueltigen Werten; Tests decken mindestens SOC, SOH, Active Power, Temperatur, Dispatch-Target und Native-Request-/Limit-Mapping ab. Konfiguration kann Native explizit deaktivieren; produktive Default-Aktivierung ist nicht Teil von RM-M3-05. |
| 🟡     | RM-M3-06 | Multi-Stage Dockerfile mit Native-Build-Stage                     | Teil 1: RM-M3-01; Teil 2: RM-M3-05 | **Teil 1 ✅:** `native-build` Stage in `Dockerfile` (debian:bookworm-slim + build-essential + cmake), kompiliert das `native/battery_control_core/`-Tree reproduzierbar und ruft `ctest` auf — der Smoke-Test aus RM-M3-02 läuft im selben Layer mit. `native/battery_control_core/CMakeLists.txt` baut sowohl die `.so` als auch das Test-Binary mit `-Wall -Wextra -Wpedantic -Werror -std=c++17`. Make-Target `make native-build` triggert die Stage ohne `make ci` anzufassen. **Teil 2 ⬜:** Runtime-Image-Pfad `/app/native/` + Container-Smoke folgen nach RM-M3-04/05, wenn Routing+Loader die `.so` konsumieren können. |
| ⬜     | RM-M3-07 | Interop-Tests: Struct Layout, ABI, Werte-Paritaet                  | RM-M3-02, RM-M3-04 | Layout-/ABI-Tests pruefen die P/Invoke-Grenze; Werte-Paritaet setzt die Native-Constraint/Ramp-Implementierung aus RM-M3-02 voraus. Tests vergleichen Native-Output gegen .NET-Referenz fuer definierte Snapshots/Limits; Toleranzen sind dokumentiert und eng. Negative Tests decken deaktivierte Native-Option, fehlende `.so` bei aktivierter Native-Option, ABI-Mismatch, non-finite ABI-Input und nativen Fehlerstatus aus gueltigem .NET-Kontext ab. |
| ⬜     | RM-M3-08 | C++-Unit-Tests                                                     | RM-M3-02      | Native Tests decken Constraint, Ramp, den PID-Slice sobald RM-M3-13 aktiviert ist, NaN/Inf, Vorzeichen, `dt == 0`, negative `dt`, fehlenden Previous-Power-Kontext und alle Limit-Reason-Codes ab. Testdaten sind so gewaehlt, dass jede Statuscode-Variante mindestens einmal direkt im C++-Test erreicht wird. |
| ⬜     | RM-M3-09 | Native-Quality-Gates                                               | RM-M3-08      | `native-lint`, Sanitizer und Native-Coverage laufen reproduzierbar; Native-Coverage bleibt bei 100 % line fuer `native/battery_control_core/src/`; Ausschluesse sind nur mit `Why:`-Kommentar und `native-coverage-exclusions` erlaubt. |
| ⬜     | RM-M3-10 | Native/.NET-Parity-Gate ueber Replay-Datensatz                     | RM-M3-05, RM-M3-07 | Kleiner versionierter Golden-Datensatz aus M1/M2-Simulator-Faellen plus Randfaelle fuer SOC, Ramp, Vorzeichen und kombinierte Constraint/Ramp-Begrenzung liefert fuer Native und .NET identische Commands bis auf dokumentierte Toleranzen. Parity-Runner/Testprojekt laeuft standalone reproduzierbar; die Pflichtverdrahtung in `make gates`/`make ci` erfolgt erst in RM-M3-11. Negative `dt`, nicht-finite Snapshot-/Dispatch-/Limit-/Request-Werte, Stale-Snapshot- und `ValidUntil`-Faelle bleiben separate Managed-Control-/Mapping-Tests und duerfen nicht als Native-vs-Managed-Parity mit ungueltigen Inputs modelliert werden. |
| ⬜     | RM-M3-11 | Makefile-Erweiterung um native Targets                            | RM-M3-09, RM-M3-10 | `native-lint`, `test-native-interop`, `test-native-parity`, `native-coverage-gate`, `native-coverage-report`, `native-coverage-exclusions` existieren; `gates`/`ci` ziehen die M3-Gates mit, sobald alle Targets standalone und in CI reproduzierbar sind. |
| ⬜     | RM-M3-12 | Doku-/Contract-Sync fuer Native-Policy und Adaptername             | RM-M3-03, RM-M3-05 | `docs/user/quality.md`, `spec/architecture.md`, `docs/plan/planning/in-progress/roadmap.md` und dieser Plan sind konsistent zur M3-Default-Policy: ABI-Mismatch und native Fehler aus gueltigem .NET-Kontext fuehren zu .NET-Fallback; Startabbruch ist eine separate Produktions-Policy mit eigenem Test. RM-M3-03 stellt diese Policy frueh sicher; RM-M3-12 ist der abschliessende Sync vor produktivem Routing. Der kanonische Adaptername ist ueberall `BatteryEms.Adapters.NativeInterop`; der kanonische Header-Pfad ist `native/battery_control_core/include/battery_control_core.h`; der kanonische Coverage-Scope ist `native/battery_control_core/src/`. |
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
| Ramp hold | `MaxRampKwPerSecond == 0` oder `dt == 0` haelt Previous Power. |
| Ramp up/down | Positive und negative Delta-Grenzen werden exakt eingehalten. |
| No previous | Erster Tick ueberspringt Ramp wie Managed-Pfad. |
| Constraint und Ramp begrenzen beide | Finale Leistung entspricht Ramp-Ergebnis; Command-Reason folgt der heutigen Managed-Prioritaet: Constraint-Reason vor Ramp-Reason. |
| Non-finite ABI-Input | Native liefert Fehlerstatus; Application-Tests beweisen zusaetzlich, dass solche Werte normalerweise vor Kernel-Aufruf als invalid Snapshot/Dispatch/Mapping behandelt werden. |
| Negative dt | Native liefert `negative-dt`; .NET signalisiert kontrollierten Fallback/Safe-Stop ohne stale Native-Ergebnis und ohne Managed-Ramp mit negativer Zeit erneut aufzurufen. |

Die Golden-Dateien duerfen synthetisch sein, muessen aber die aktuellen
Domain-Tests spiegeln und pro Fall den erwarteten Command mit Status,
Reason-Code, ActivePowerKw und Mode enthalten.

`Available == false`, unbrauchbare Snapshot-Qualitaet, stale Snapshots,
ungueltiger SOC/SOH, nicht-finite SOC/SOH/Active-Power/Temperatur und
nicht-finite Werte aus Dispatch, Limits oder Request sind
Managed-Precheck-Faelle. Sie bekommen eigene Control-/Mapping-Tests,
zaehlen aber nicht als Native-Parity-Goldens, solange der erste ABI-Slice
diese Felder nicht fuehrt.

### Abnahmematrix Nach Testtyp

| Nachweis | Primaerer Ort | Muss mindestens beweisen |
| -------- | ------------- | ------------------------ |
| C++-Unit | `native/battery_control_core/tests/` | Constraint/Ramp-Reasons, nicht-finite Inputs, negative `dt`, Exception-Barriere am Export. |
| ABI-Layout | .NET-Interop-Tests + kleiner C++-Layout-Test | `sizeof`, Offsets, Calling Convention, ABI-Version und Statuscode-Breiten stimmen zwischen Header und P/Invoke. |
| Adapter-Negativtests | `BatteryEms.Adapters.NativeInterop.Tests` | deaktivierte Native-Option, fehlende `.so` bei aktivierter Native-Option, falsche ABI, Loader-Fehler und Native-Fehlerstatus liefern kontrollierten Adapterzustand. |
| Application-Fallback | `BatteryEms.Application.Tests/ControlCycle*` oder neues gezieltes Testprojekt | derselbe Tick wird bei Native-Fehler aus gueltigen Eingaben ueber Managed neu berechnet; kein stale Native-Command wird verwendet; nicht-finite Snapshot-SOC/SOH/Active-Power/Temperatur sowie Dispatch-/Mapping-Werte werden vor Kernel-Aufruf abgefangen. |
| Parity-Golden | `test-native-parity` | Native und Managed liefern fuer Golden-Faelle denselben Command bis zur dokumentierten Toleranz, inklusive kombinierter Constraint/Ramp-Begrenzung und heutiger Reason-Prioritaet. |
| Container-Smoke | `make runtime` oder dediziertes Native-Smoke-Target | Runtime-Image enthaelt `/app/native/libbattery_control_core.so` und Host kann mit und ohne Library kontrolliert starten. |
| Observability | Application-/Telemetry-Tests | Health/Logs/Metriken unterscheiden disabled, loaded, library-missing, abi-mismatch und native-error. |

Testdaten sollen nicht nur erwartete Powers vergleichen. Pro Fall werden
mindestens `active_power_kw`, `mode`, `status`, `reason_code`,
`fallback_reason` (falls Fallback) und bei orchestrierten
Constraint/Ramp-Faellen die .NET-kompatible Reason-Prioritaet asserted.
Fuer Floating-Point-Vergleiche gilt die M3-Starttoleranz `1e-6 kW`
absolut; Status, Mode und Reason-Codes muessen exakt matchen.

---

## M2-Folgearbeit Mit M3-Trigger

| Status | ID              | Paket                                      | Aktivierungsbedingung | DoD |
| ------ | --------------- | ------------------------------------------ | --------------------- | --- |
| ⬜     | RM-M3-FUP-01    | Erste echte Folgemigration ueber vorhandenen Migrationspfad aktivieren | OP-OPEN-05/06 oder erste echte Schema-Aenderung | Der abgeschlossene Migrationspfad aus [`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md) wird konsumiert: `schema/schema.yaml` wird angepasst, eine echte `Migrations/RunOnce/0002_*.sql` wird erzeugt/committed, Drafts bleiben nicht eingebettet, `schema-validate`/`schema-drift-check` bleiben gruen und der Runtime-Migrator appliziert die Aenderung idempotent. |
| ⬜     | RM-M3-FUP-02    | Optimistic Schedule Replace (OP-OPEN-05)   | Multi-Replica-Optimize oder schema-veraendernder Schedule-Track | `IScheduleRepository.Replace(schedule, expectedBaseVersion)` plus Dapper-`WHERE version = @expected`; Versionskonflikt wird als `Failed` Run mit Reason `concurrent-version-conflict` auditierbar. |
| ⬜     | RM-M3-FUP-03    | Optimization-Lock-Eviction (OP-OPEN-06)    | Ephemere Asset-IDs, Multi-Tenant-Rotation oder wachsende Test-ID-Sets | `_locks` in `DefaultScheduleOptimizationUseCase` bekommt LRU/TTL-Eviction mit konfigurierbarer Schwelle und Metrik `bess_optimization_lock_table_size`. |
| ⬜     | RM-M3-FUP-04    | Replay-Carve-outs nach RM-M2-10            | Externe Fixtures, Operator-Replay, Multi-Asset-Replay oder Production-Replay werden gebraucht | Der in RM-M2-10 gelieferte Telemetrie-Replay-Harness bleibt bestehen. M3+ ergaenzt nur konkrete Folge-Slices wie JSON-File-Loader unter `tests/fixtures/replay/`, Operator-CLI/Make-Target, Multi-Asset-Replay-Koordination oder Compare-against-Production-Replay; Solver-Replay aus M2 bleibt unveraendert. |

---

## Statuscode-Baseline

Die konkreten numerischen Werte werden in RM-M3-01 festgelegt und danach
als ABI behandelt. Die .NET-Reaktion unterscheidet bewusst zwischen
fallbackfaehigen nativen Fehlern aus gueltigem .NET-Kontext und
Precheck-/Mapping-Fehlern, bei denen derselbe ungueltige Input nicht noch
einmal blind in den Managed-Kernel wandern darf. Die semantische
Mindestmenge ist:

| Code | Bedeutung | .NET-Reaktion |
| ---- | --------- | ------------- |
| `ok` | Command ohne native Begrenzung berechnet | Native-Ergebnis verwenden |
| `limited` | Command wurde durch Constraint/Ramp begrenzt | Native-Ergebnis verwenden, Begrenzung observierbar machen |
| `invalid-input` | Pflichtfeld fehlt oder Wertebereich fachlich ungueltig | Precheck-/Mapping-Fehler: kein Blind-Fallback mit denselben ungueltigen Werten; wenn der Application-Precheck den Fall nicht vorher abgefangen hat, Tick als fehlerhaft markieren und Safe-Stop/invalides Mapping signalisieren |
| `non-finite` | NaN/Inf in Eingabe oder Ergebnis | Bei nicht-finiten Eingaben: Precheck-/Mapping-Fehler ohne Blind-Fallback. Bei nicht-finitem Native-Ergebnis aus gueltigen Eingaben: .NET-Fallback und Fehlercounter |
| `negative-dt` | `dt < 0` fuer Ramp; `dt <= 0` fuer PID | Precheck-/Mapping-Fehler, sofern der Control-Cycle die Zeitbasis liefert; .NET-Fallback nur, wenn der restliche Kontext fachlich gueltig ist und der Managed-Pfad nicht erneut mit negativer Zeit aufgerufen wird |
| `unsupported-state` | ABI gueltig, aber Zustand im Native-Slice nicht implementiert | .NET-Fallback; Warnung statt Crash |

### Reason-Code-Mindestmenge

Die numerischen Werte werden in RM-M3-01 vergeben. Die Namen hier sind die
semantische Untergrenze und muessen aus den heutigen .NET-Reasons ableitbar
bleiben:

| Reason | Quelle / Fall | Erwarteter Status |
| ------ | ------------- | ----------------- |
| `within-limits` | Constraint/Ramp unveraendert | `ok` |
| `asset-unavailable` | Managed-Precheck im aktuellen Control-Cycle; nicht Teil des ersten ABI-Slice, solange `Available` nicht ueber die ABI geht | Kein Native-Reason im ersten Slice; bei spaeterem ABI-Delta neu entscheiden |
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
- RM-M3-09/RM-M3-10 liefern Native-Quality- und Parity-Nachweise zuerst
  standalone reproduzierbar. RM-M3-11 standardisiert danach die Make-Targets:
  `native-lint`, `test-native-interop`, `test-native-parity`,
  `native-coverage-gate`, optional `native-coverage-report` und
  `native-coverage-exclusions`.
- `make gates` und `make ci` duerfen erst auf Native-Gates erweitert
  werden, wenn die Targets reproduzierbar in CI laufen.
- Vor Aktivierung in `make ci` muessen die Native-Targets in dieser
  Reihenfolge lokal und im Docker-Build gruen sein:
  `native-lint`, C++-Unit-Tests, `test-native-interop`,
  `test-native-parity`, `native-coverage-gate`.
- RM-M3-03, RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10, RM-M3-11 und RM-M3-12 sind
  Gate-Voraussetzungen fuer den ersten PR, der Native als produktiven
  Default oder bevorzugten Runtime-Pfad in einem produktionsnahen Profil
  aktiviert, damit ABI-Policy, Fallback, Container-Pfad, Interop,
  Replay-Parity, CI-Targets und
  Doku-Policy zusammen nachgewiesen sind. RM-M3-05 darf vorher nur
  explizit aktivierbares Routing fuer Tests/Profile liefern.

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
| RM-M3-OPEN-08 | Wie wird die Native-ABI-Policy in Quality-Doku und Plan behandelt? | **Geschlossen:** `docs/user/quality.md` §5.2, Architektur §13.4 und dieser Plan beschreiben denselben M3-Default: ABI-Mismatch fuehrt zu .NET-Fallback. Startabbruch bleibt optionale Produktions-Policy. |
| RM-M3-OPEN-09 | Darf produktive Native-Profilaktivierung im selben PR wie Container-/Gate-Erstverdrahtung passieren? | **Geschlossen:** nein. Produktive Profilaktivierung ist ein eigener Folge-PR, nachdem RM-M3-03, RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10, RM-M3-11 und RM-M3-12 abgeschlossen und auf `main` gruen sind. |

---

## Risiken Und Gegenmassnahmen

| Risiko | Gegenmassnahme |
| ------ | -------------- |
| ABI driftet unbemerkt zwischen Header und P/Invoke-Structs | Layout-/Offset-Tests plus ABI-Versionstest in RM-M3-04/RM-M3-07. |
| Native-Fehler veraendern Safety-Semantik | .NET-Referenz bleibt Oracle; fallbackfaehige native Fehler aus gueltigem .NET-Kontext fallen auf .NET zurueck; Precheck-/Mapping-Fehler werden ohne Blind-Fallback als Safe-Stop/invalides Mapping behandelt; Parity-Goldens enthalten Safety-Randfaelle. |
| Lokale Toolchain unterscheidet sich von CI | Docker-Build ist Referenz; Make-Targets laufen gegen dieselben Stages. |
| Native Coverage wird durch Ausschluesse entwertet | 100-%-Gate fuer `native/battery_control_core/src/` plus `native-coverage-exclusions` mit Default-Toleranz 0. |
| M2-Folgearbeit wird versehentlich mit Native-Kern vermischt | FUP-Trigger bleiben explizit; Schema-Aenderungen ziehen zuerst RM-M3-FUP-01. |
| In-Process-Native kann den Host bei Speicherfehlern crashen | Sehr kleine ABI, Sanitizer, C++-Unit-Tests und Default-Fallback bei gemeldeten Fehlern; groessere Solver-/MPC-Kerne bleiben Sidecar-Thema. |
| Quality-Doku und Plan driften zur ABI-Mismatch-Policy auseinander | RM-M3-03 haelt `docs/user/quality.md` §5.2 und Architektur §13.4 beim ABI-Policy-Slice synchron; RM-M3-12 bleibt der finale Contract-Sync vor produktivem Routing. |

---

## Reihenfolge

1. RM-M3-01 legt ABI und Datenvertrag fest.
2. RM-M3-06 Teil 1 kann nach RM-M3-01 den fruehen Native-Build-Stage
   liefern, ohne Runtime-Image-Pfad oder Routing zu aktivieren.
3. RM-M3-02/RM-M3-08 implementieren und testen Constraint + Ramp lokal;
   PID folgt danach in RM-M3-13 als inkrementeller Native-Slice.
4. RM-M3-03 liefert ABI-Version, .NET-Startup-Check und Policy-Sync;
   RM-M3-04 liefert die Compute-Bindings und Struct-Layout-Tests.
5. RM-M3-05 liefert explizit aktivierbares Runtime-Routing mit
   Fallback; RM-M3-06 Teil 2 liefert danach den Runtime-Container-Pfad.
6. RM-M3-07/RM-M3-10 bauen Interop- und Parity-Vertrauen auf; RM-M3-10
   bleibt bis RM-M3-11 standalone und ist noch nicht `make ci`-Pflicht.
7. RM-M3-09/RM-M3-11 aktivieren Native-Gates in `make gates`/`make ci`.
8. RM-M3-12 synchronisiert Qualitaets-/Architektur-Doku vor produktivem
   Native-Routing.
9. Erst nach RM-M3-03, RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10,
   RM-M3-11 und RM-M3-12 darf ein Profil Native als produktiven Default
   oder bevorzugten Runtime-Pfad setzen.
10. RM-M3-13 zieht PID erst nach stabiler Constraint/Ramp-Parity und
   aktiviert dafuer eigene C++-, Interop- und Parity-Nachweise.
11. RM-M3-FUP-* werden nur gezogen, wenn ihre Trigger eintreten.

---

## Ready-For-Implementation Checklist

- [ ] Native-PR beruehrt keine Persistenzdateien und keine
  schema-veraendernden Migrationen, ausser ein FUP-Trigger ist bewusst
  aktiviert.
- [ ] Jeder PR nennt den betroffenen Slice M3-A..M3-FUP bzw. M3-D2 und
  die abgeschlossenen RM-M3-IDs.
- [ ] Managed-Referenztests bleiben ohne Native Library lauffaehig.
- [ ] Native-spezifische Tests bauen oder mounten ihre `.so` explizit.
- [ ] Jede neue Native-Reason oder jeder Statuscode ist im Header,
  in .NET-Mapping-Tests und in diesem Plan bzw. der finalen Doku
  nachgezogen.
- [ ] Produktive Profilaktivierung passiert erst in einem Folge-PR,
  nachdem RM-M3-03, RM-M3-05, RM-M3-06, RM-M3-07, RM-M3-10, RM-M3-11
  und RM-M3-12 auf `main` gruen sind.
