# Plan RM-M4 Regelleistung und OPC-UA

**Dokumenttyp:** Detailplan / M4 (abgeschlossen)
**Status:** Abgeschlossen am 2026-05-11 — alle 8 Pflicht-Slices ✅ (RM-M4-01..08).
Closure-Commit `9197a99` (RM-M4-05-D + M4 closure). Folgearbeiten F-01..F-19
sind als Trigger-Watches in [`../open/note-RM-M4-followups.md`](../open/note-RM-M4-followups.md).
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md) (M4),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(LH-MKT-002/004/005/006, LH-OPCUA-001..005, LH-MQTT-004/005,
LH-CONF-002, LH-TEST-003, LH-OPEN-002),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
(§8.1 Adapter-Interfaces, §8.2 Optimierungs-Interface),
[`../../../user/quality.md`](../../../user/quality.md) (§2.2
Integrationstests, §5.3 Adapter-Mapping-Schema)

---

## Zweck

M4 zieht die nach-MVP-Funktionen aus der Roadmap in einen umsetzbaren
Slice: Intraday-Reoptimierung über den Resthorizont,
Regelleistungsreservierung, Verarbeitung von
Regelleistungsaktivierungen und einen OPC-UA-Adapter über dieselben
internen Adapter-Interfaces wie Modbus und MQTT.

Der Plan trennt bewusst drei fachliche Ebenen:

- **Planung:** Intraday- und Regelleistungsreservierungen verändern
  versionierte Fahrpläne und Solver-Constraints.
- **Regelkreis:** Aktivierungssignale dürfen den normalen Fahrplan
  übersteuern, aber niemals Emergency Stop oder Safety-Limits
  (Geräte-/Wechselrichter-/Netzgrenzen, SOC-Grenzen und Ramp-Limits).
- **Feldintegration:** OPC-UA liefert Telemetrie und Command-Schreibpfade
  über `IBatteryTelemetrySource` und `IBatteryCommandSink`, ohne die
  zentrale Regelpipeline zu verändern.

M4 ist kein zertifizierungsnaher Regelleistungsnachweis. Die Roadmap
führt diese Vertiefung später unter M6. M4 liefert die Produkt- und
Adapterfähigkeit, die dafür benötigt wird.

---

## Abgrenzung

**In Scope:**

- Intraday-Reoptimierung für einen bestehenden Fahrplan-Resthorizont.
- Modellierung reservierter Lade- und Entladeleistung für
  Regelleistung.
- Solver-Constraints, die Reservierungen für Day-Ahead und Intraday
  einhalten.
- Aktivierungssignal-Verarbeitung mit Priorität nach LH-MKT-006:
  Emergency Stop, Safety-Limits, Regelleistungsaktivierung,
  verbindliche Marktverpflichtung, Intraday-Fahrplan,
  Day-Ahead-Fahrplan, lokale Optimierung.
- OPC-UA-Lese-, Schreib- und Subscription-Pfade über die bestehenden
  Adapter-Ports.
- OPC-UA-StatusCode-Auswertung: schlechte Werte werden als ungültig
  markiert und dürfen keinen validen Snapshot vortäuschen.
- OPC-UA-Security-Konfiguration für Zertifikate, Security Mode und
  Security Policy.
- MQTT-QoS- und Command-ACK-Korrelation, soweit sie für denselben
  Command-/Dispatch-Vertrag gebraucht wird.
- Versionierte OPC-UA-Mappings unter `config/` mit Schema-Validierung.
- Integrationstests gegen einen OPC-UA-Simulator.

**Out of Scope:**

- Regulatorische Präqualifikation oder zertifizierungsnahe
  Regelleistungsintegration; bleibt M6.
- MPC, Solver-Sidecar oder gRPC-Optimizer; bleibt M5.
- Neues internes Adapter-Interface nur für OPC-UA. M4 muss das vorhandene
  Port-Modell verwenden oder eine separat begründete Architekturänderung
  dokumentieren.
- Native-/Edge-Pivot für harte Echtzeit-Regelleistung. Falls ein Produkt
  Latenzen fordert, die der .NET-Regelkreis nicht belastbar erfüllt,
  braucht das einen eigenen ADR-/Folgeslice.

---

## Aktivierungsbedingungen

M4 kann starten, wenn M3 geschlossen ist und die M2/M3-Basisgates auf
`main` grün sind. Die konkrete Regelleistungsprodukt-Annahme ist in
LH-OPEN-002 dokumentiert: FCR, aFRR und mFRR sind relevant; M4 weist
FCR-Reservierung und aFRR-Aktivierung als erste reale Produktprofile
nach. mFRR bleibt im Marktmodell vorbereitet, aber nicht als produktiver
Aktivierungspfad für M4 vorausgesetzt.

| Check | Erwartung |
| ----- | --------- |
| M3-Closure | Native Control Core und produktives Routing sind abgeschlossen; M4 verändert den Control-Kernel nicht ohne eigenen Slice. |
| Marktmodell | `MarketCommitment` und Priorisierung aus M2 bleiben kompatibel; Regelleistungsaktivierung ergänzt den bestehenden Prioritätspfad. |
| Optimierung | Bestehender Schedule-Optimizer kann um Resthorizont- und Reserve-Constraints erweitert werden, ohne Day-Ahead-Semantik zu brechen. |
| Schedule-Write-Atomizität | Resthorizont-Reoptimierung nutzt einen eindeutigen Commit-Lock pro `(asset_id, schedule_type)` plus optimistic compare-and-swap auf der erwarteten Schedule-Version. Kollidierende Writer brechen ohne Replace ab und müssen den aktuellen Schedule neu laden. |
| Adapter-Port | OPC-UA kann über `IBatteryTelemetrySource` und `IBatteryCommandSink` modelliert werden. Abweichungen brauchen Plan-Update vor Code. |
| Testumgebung | Ein OPC-UA-Simulator ist als CI- oder Docker-Pfad verfügbar oder wird als erstes M4-Arbeitspaket gebaut. |
| Produktannahme | FCR, aFRR und mFRR sind als relevante Produktfamilien dokumentiert; FCR-Reservierung und aFRR-Aktivierung sind die ersten realen M4-Produktprofile. Zeitschritt, Aktivierungsrichtung und Leistungsinterpretation sind je Profil explizit. |
| Produktiv-Gate | Default bleibt `Regelleistung:ProductionActivationEnabled=false`. Produktive RL-Aktivierung darf nur starten, wenn Produktannahme, Profilfreigabe, Jitter-Kalibrierung, Zeitbasis-Health, Dedupe-Store-Health und Security-Profil grün sind; sonst werden Aktivierungen als nicht dispatch-relevant markiert. |
| Zeitbasis | Aktivierungssignale werden auf UTC-Zeitstempel gegen `IClock.UtcNow` validiert; Produktionsprofile brauchen synchronisierte Systemzeit (z. B. NTP) oder blockieren aktive Regelleistungsaktivierung. `max_age`, `future_skew_tolerance` und dedupe-Fenster sind konfigurierbar und pro Profil getestet. Wiederholte Zeitbasis-Verletzungen schalten RL-Aktivierung in `TimebaseDegraded`, bis die Zeitquelle stabil ist. |
| Runtime-Profil | Host/Deployment setzen ein explizites Runtime-Profil (`Development`, `Test`, `Production`). Sicherheitskritische M4-Optionen validieren gegen dieses Profil; ein fehlendes Profil ist in produktionsnahen Deployments ein Startup-Fehler. |

---

## Zielbild Und Abnahmeschnitt

| Situation | Erwartetes Verhalten | Mindestnachweis |
| --------- | -------------------- | --------------- |
| Intraday-Reoptimierung | Ein bestehender Fahrplan kann ab einem Resthorizont neu bewertet und als neue Version atomar abgelegt werden. Bei Optimiererfehlern, invaliden Inputs, unzulässigen Constraints oder CAS-Konflikten bleibt die bisherige gültige Zukunftsversion aktiv. | Use-Case-/Optimizer-Test mit unverändertem Vergangenheitsfenster, neuer Zukunftsversion, Fehlerpfad ohne Schedule-Replace und parallelem Writer-Konflikt. |
| Regelleistungsreservierung | Reservierte Lade- und Entladeleistung wird für Day-Ahead und Intraday blockiert. | Solver-Test, der Reserve-Bänder verletzt hätte und nun begrenzt wird. |
| Aktivierungssignal aktiv | Der Regelkreis nimmt den Aktivierungs-Setpoint vor Market Commitments und Fahrplänen, bleibt aber innerhalb Safety-Limits. In Production passiert das nur bei `Regelleistung:ProductionActivationEnabled=true` und grünem Produkt-/Profil-/Health-Gate. | Control-Cycle-Test mit konkurrierendem Day-Ahead-/Intraday-Commitment plus Negativtest für deaktiviertes Production-Gate. |
| Aktivierungssignal ungültig/stale | Kein normaler Fahrplan wird durch ein unvalides Signal verdrängt. M4 startet mit konservativem Default `max_age=2s` und `future_skew_tolerance=500ms`, beide Werte sind aber Profilkonfiguration und müssen mit OPC-UA-/Netzjitter kalibriert werden, bevor Regelleistung produktiv aktivierbar ist. Systematische Zeitbasisfehler führen nach Debounce in `TimebaseDegraded`; in diesem Zustand werden RL-Aktivierungen stumm geschaltet und observierbar als nicht dispatch-relevant markiert. | Negativtest für stale Timestamp, future skew, nicht-finite Leistung, fehlende Asset-Zuordnung, Debounce in `TimebaseDegraded` und Wiederherstellung nach stabiler Zeitbasis plus Profiltests für Default und produktionsspezifisches Tuning. |
| OPC-UA Lesen | Node-Werte werden in interne Telemetrie gemappt. | Adaptertest gegen Simulator inklusive Datentyp- und Unit-Mapping. |
| OPC-UA Schreiben | Leistungssollwerte werden auf konfigurierte NodeIds geschrieben. | Simulator-Integrationstest mit Command-Result. |
| OPC-UA Subscription | Änderungen ausgewählter Nodes werden ereignisbasiert verarbeitet. | Integrationstest mit Subscription-Update ohne Polling-Only-Pfad. |
| OPC-UA StatusCode schlecht | Wert wird als ungültig markiert und nicht als gesunde Telemetrie verwendet. | Adaptertest für Bad/Uncertain StatusCode. |
| OPC-UA Security | Default ist ein sicherer Modus (`SignAndEncrypt`) plus Policy aus einer expliziten Allowlist. M4-Allowlist startet mit `Basic256Sha256`; weitere Policies brauchen Plan-/Test-Update. `SecurityMode=None` ist nur per explizitem Opt-in mit `AllowUnsecured=true` und `AllowUnsecuredReason` erlaubt, emittiert eine Warnung und ist bei `RuntimeProfile=Production` ein Startup-Fehler. | Konfigurations-/Handshake-Test gegen gesicherten Simulator oder Testserver plus Negativtest für ungesicherten Production-Start, fehlendes Profil und nicht-allowlistete Policy. |
| MQTT Command-ACK | Commands tragen `CommandId`; ACKs werden mit dem richtigen Command korreliert. | MQTT-Integrationstest mit QoS-Konfiguration und ACK-Mismatch-Negativfall. |

---

## Komponenten

| Bereich | Artefakt | LH-Bezug |
| ------- | -------- | -------- |
| Domain | Regelleistungsreserve als zeitbezogene Leistungsband-Reservation | LH-MKT-004 |
| Domain | Aktivierungssignal mit Richtung, Leistung, Gültigkeitsfenster und Quelle | LH-MKT-005/006 |
| Application | Resthorizont-Reoptimierungs-Use-Case auf bestehenden Schedule-Versionen | LH-MKT-002 |
| Application | Erweiterung des Dispatch-/Commitment-Prioritätspfads für Aktivierungen | LH-MKT-005/006 |
| Optimization | Reserve-Constraints im Schedule-Optimizer | LH-MKT-004 |
| Adapter | `BatteryEms.Adapters.OpcUa` für Lesen, Schreiben, Subscriptions und StatusCodes | LH-OPCUA-001..004 |
| Adapter | OPC-UA-Security-Optionen und Zertifikatspfad-Validierung | LH-OPCUA-005 |
| Adapter | MQTT-QoS-/ACK-Hardening im bestehenden MQTT-Adapter | LH-MQTT-004/005 |
| Config | `config/schema/opcua-mapping.schema.json` und Beispiel-Mappings | LH-CONF-002 |
| Tests | OPC-UA-Simulator-Integration und QoS-/ACK-Szenarien | LH-TEST-003 |

---

## Prioritätsvertrag

M4 verwendet für den Regelkreis eine explizite
`ControlPriorityClass`-Ordnung. Niedrigere Zahl gewinnt; ein niedrigerer
Eintrag darf durch keinen höheren Eintrag überschrieben werden.

| Rang | Klasse | Bedeutung / Tiebreak |
| ---- | ------ | -------------------- |
| 1 | `EmergencyStop` | Jeder aktive Stop gewinnt; mehrere Stop-Quellen werden als ein Stop-Zustand zusammengeführt. |
| 2 | `SafetyLimit` | Geräte-/Wechselrichter-/Netzgrenzen, SOC-Grenzen und Ramp-Limits; deterministische Clamp-Reihenfolge bleibt Teil des Control-Kernel-Vertrags. |
| 3 | `RegelleistungsActivation` | Nur valide, nicht-stale Aktivierungssignale. Totaler Tiebreak: höchste `sequence_number`; wenn keine Sequence vorhanden ist, neuester valider `signal_timestamp_utc`; danach lexikografisch kleinster Tupelwert `(source_id, activation_id/message_id)`. Exakte Wiederholungen mit gleicher `source_id` und `activation_id`/`message_id` sind innerhalb des dedupe-Fensters idempotent und dürfen re-applied werden; vollständiger Gleichstand mit widersprüchlichem Payload ist `ambiguous-duplicate`, hat keinen Gewinner und wird nicht dispatch-relevant. |
| 4 | `BindingMarketCommitment` | Verbindliche Marktverpflichtung (`MarketCommitment` mit `BindingState=Binding`) gemäß bestehendem M2-Modell. |
| 5 | `IntradaySchedule` | Nicht-bindender Intraday-Fahrplan. |
| 6 | `DayAheadSchedule` | Nicht-bindender Day-Ahead-Fahrplan. |
| 7 | `LocalOptimization` | Fallback ohne höher priorisierte Quelle. |

Die Terminologie im Plan folgt dieser Tabelle: „Safety-Limits" meint
immer Rang 2, „Market Commitments" meint das bestehende Domain-Modell,
und „verbindliche Marktverpflichtung" ist Rang 4.

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M4-01 | Intraday-Reoptimierung (Resthorizont) | Domain/Application: neuer Driving Port `IIntradayReoptimizationUseCase` plus `IntradayReoptimizationCommand` (composition mit `ScheduleOptimizationCommand` für geteilte Validierung) und `DefaultIntradayReoptimizationUseCase`. Der Use-Case akquiriert per-asset Lock, liest die existierende Intraday-Baseline (D-01: fehlt → `intraday-baseline-missing`), prüft Window-Boundary-Alignment auf `residualStart` (D-02: misalignment → `residual-start-not-aligned`), ruft `IReserveRepository.FindActive` für den Resthorizont (M4-02-Reserve-Cap-Pfad), feedet den optimizer mit `ScheduleOptimizationRequest` für `[residualStart, horizonEnd)` und kombiniert past-Windows + neue Future-Windows zu Schedule v+1. Replace nutzt CAS via `expectedBaseVersion = existing.Version` (RM-M3-FUP-02-Pfad); CAS-Konflikt synthetisiert einen Failed-Run mit `concurrent-version-conflict`. Solver-Failure (kein `ProducedSchedule`) wird als Failed-Run persistiert ohne Replace; existierender Schedule bleibt aktiv. API: neuer Endpoint `POST /markets/intraday/reoptimize` (D-04: synchron, Operator-policy-guarded), Wire-Body `IntradayReoptimizationRequestBody`. DI: `IIntradayReoptimizationUseCase` als Singleton in `AddBessApplicationInMemoryStores`. Tests: 13 Application-Unit-Pins (Happy-Path-Past+New-Combine, baseline-missing, residual-start-not-aligned, Solver-Failure-ohne-Replace, CAS-Conflict-Pfad, Reserve-Bands-Resthorizont, Wiring-Pin `existing.Version` statt produced.Version, Window-Boundary-Edge-Cases, Per-Key-Lock-Serialization, Null-Command, Dispose-Idempotenz/Disposed-Throw) + 7 API-Endpoint-Pins (401/403, 400 missing field, 404 unknown asset, 200+baseline-missing, 200+residual-start-not-aligned, 200+no-solver-configured). **Design-Entscheidungen** (verbindlich für den Slice): **D-01** Reoptimierung verlangt eine existierende Intraday-Baseline; kein impliziter Cold-Start aus Day-Ahead. Fehlende Baseline ⇒ Failed-Run mit `intraday-baseline-missing` (Cold-Start ist eigene Folgearbeit, siehe `note-RM-M4-followups.md` F-01). **D-02** `residualStart` muss an einer Window-Grenze des bestehenden Schedules liegen; Misalignment ⇒ Failed-Run mit `residual-start-not-aligned`. Operator pickt einen Step-Boundary (Snap-to-Boundary-Toleranz ist Trigger-Watch, siehe `note-RM-M4-followups.md` F-02). **D-03** Replace bleibt destruktiv per M1-Vertrag (`IScheduleRepository.cs:17-18`); Past-Windows-Audit-History ist RM-M1-14, kein M4-Bedarf. **D-04** Synchroner HTTP-Endpoint `POST /markets/intraday/reoptimize` analog zu M2-Day-Ahead; Async-Job-Modell ist globaler Carve-out unter RM-M2-OP-OPEN-04, kein M4-spezifischer Trigger. |
| ✅ | RM-M4-02 | Reservierungs-Modell für Regelleistung + Solver-Constraints | Domain: `ReserveProduct` (Fcr/Afrr/Mfrr) + `ReserveDirection` (Symmetric/Up/Down) + `ReserveBand`-Klasse mit Validierung (FCR↔Symmetric, AFRR/MFRR↔Up oder Down, halboffenes [Start,End)-Fenster, PowerKw als Magnitude ≥ 0). Application: Driven Port `IReserveRepository` + `InMemoryReserveRepository`; `ScheduleOptimizationRequest` trägt `IReadOnlyList<ReserveBand>` (Default leer ⇒ M2-Pfad bit-identisch); `DefaultScheduleOptimizationUseCase` ruft `IReserveRepository.FindActive(asset, horizonStart, horizonEnd)` und reicht durch. OR-Tools-Adapter: `ComputeReserveCaps` deduziert pro Step die overlapping Bands per `band.Covers(stepStart)` und reduziert Charge-/Discharge-Caps (Symmetric beidseitig, Up nur Discharge, Down nur Charge); Über-Commit terminiert mit `reserve-exceeds-capacity` Code statt LP-infeasible (Toleranz `ReserveCapEpsilon = 1e-9` gegen Cancellation-Noise). Tests: 9 Domain-Pins (Direction-Product-Matrix, Half-open, Magnitude-Constraints), 11 OR-Tools-Pins (Empty-Regression, FCR-symmetrisch, AFRR-Up, AFRR-Down, MFRR-modellier­barkeit, Multi-Band-Summation, Out-of-Horizon, Foreign-Asset-Filter, Over-Commit-Termination, Half-open-Boundary, ScheduleType-DayAhead+Intraday). DI registriert `InMemoryReserveRepository` als Singleton. **Bewusst draußen** mit konkretem Trigger: (a) **Sub-Step-Band-Alignment / Any-Overlap-Semantik** — heutiges Step-Mapping ist Point-Sample auf `stepStart`, was bei aligned 15-min-Bändern + 15-min-`TimeStep` ein No-op ist; bei `TimeStep=1h` mit halbsteppigen Bändern werden sub-step-Bänder still verfehlt. Trigger: erstes Operator-UI das sub-Step-Bands erlaubt, oder Intraday mit gemischten Step-Granularitäten. Fix: Any-Overlap mit anteiliger Deduktion, oder Alignment-Precondition (~10 LOC). (b) **mFRR-Aktivierungs-Metadaten** — Activation-Time, Energy-Product-Flavor; das Modell unterscheidet mFRR und aFRR heute nur via `Product`-Enum, ohne Metadaten. Trigger: RM-M4-03 für produktive Aktivierung. (c) **LP-Strafkosten für Reserveverletzung** — Domain-Shape (`Penalty`-Feld auf `ReserveBand` plus Validation) ist ~4 LOC patchbar; die LP-Penalty-Komponente selbst (Slack-Variablen, Objective-Beitrag, Tests) ist separater Slice unter RM-M2-04-OPT-RESERVE. (d) **Persistente Dapper-`IReserveRepository`** — heute nur In-Memory, seeded-at-startup. Trigger: erstes Operator-API für Reserve-Pflege oder Hourly-Roll-Forward-Seeder; gleichzeitig wird Eviction nötig (`ConcurrentBag` heute unbounded). (e) **Symmetric für nicht-FCR** — heutige Validierungs-Matrix ist FCR-only Symmetric; Trigger: erste TSO-Produkt-Spec die symmetrische aFRR/mFRR fordert. (f) **API-Endpoint für Reserve-Pflege** — operator-facing CRUD, eigener Slice. |
| ✅ | RM-M4-03 | Regelleistungs-Aktivierungssignal-Verarbeitung mit Priorisierung | Slice-Plan: [`done/plan-RM-M4-03.md`](../done/plan-RM-M4-03.md). Domain: `RegelleistungActivation`-Klasse (alle DoD-Felder), `ActivationValidationResult`-Record + kebab-case-Reason-Code-Konstanten, `TimebaseHealth`-Enum + `TimebaseDebounceState`-Domain-Primitive (3-in-10/5-stable, Konstanten domain-verdrahtet per D-04), `RegelleistungOptions` mit Master-DoD-Defaults (`MaxAge=2s`, `FutureSkewTolerance=500ms`, `DedupeWindow=10s`, gepinnt) + operator-tunable `MaxEntriesPerSource` + `ProductionActivationEnabled`/`ProductTrustEstablished` (beide Default `false`), `ActivationTimeValidator` als pure static (per-Sample stateless gegen `IClock.UtcNow`). `ReserveProduct`/`ReserveDirection` aus M4-02 wiederverwendet (D-08). Application: Driving Port `IRegelleistungActivationUseCase` + `DefaultRegelleistungActivationUseCase`; Driven Ports `IActivationDedupeStore` (Accept/ReplayIdempotent/DedupeConflict/AmbiguousDuplicate/DedupeStoreInvalid), `IActivationDispatchSource` (single-slot mit Tiebreak per §148), `ITimebaseHealthSource`, `IProductionPreconditionProvider` (Default fail-closed bis F-12 + Healthy-Test-Stub), `IRegelleistungActivationStateStore`. `ActivationValidator`-Orchestrator führt 4-Step-Pipeline (Schema → UTC-Time → TimebaseDegraded → Dedupe) — Dedupe als letzter Schritt per DoD. Use-Case wendet Production-Gate (Master-Switch → Pre-Conditions → mFRR fail-closed per D-05) und feedet `IActivationDispatchSource` bei Dispatch-Relevant. Audit-Trail per `ILogger`-LoggerMessage (Event 4100) + In-Memory `LastActivationSnapshot` für `/health/regelleistung`. Persistence: `DapperActivationDedupeStore` mit `INSERT … ON CONFLICT (source_id, activation_id) DO NOTHING` + Retention-CTE; Tracker-Load fail-closed für vier DoD-Sub-Cases (a/b/c/d) mit sticky `_invalid` + `ResetForRecovery()`. Schema: `regelleistung_activations`-Tabelle in `schema/schema.yaml`; `0001_initial.sql` regeneriert; `0002_regelleistung_activations.sql` als idempotente Delta-Migration (`CREATE TABLE IF NOT EXISTS`) — erster realer Konsument von RM-M3-FUP-01 ✅. Optimizer-Integration via D-09 Wahl c: `ScheduleFollowingDispatchOptimizer` bekommt Konstruktor-Dep auf `IActivationDispatchSource`, aktive Aktivierung gewinnt (Rang 3) über alle MarketCommitments; `DispatchRequest`-Format unverändert; `NoOpActivationDispatchSource` füttert die existierenden M2-Dispatch-Test-Konstruktionen. API: `GET /health/regelleistung` mit JSON `{ timebase, dedupe_store, production_gate, preconditions, last_activation }`. Tests: 5 Domain-Tests (~440 Zeilen) — Konstruktor-Guards, Defaults-Pin, Time-Boundaries, Debounce-State-Maschine; 8 Application-Test-Files (~110 Pins) — Validator-Order-Pin (Replay-Hit bei TimebaseDegraded → `timebase-degraded`), Validation-Pipeline-Reasons, Dispatch-Source-Tiebreak-Matrix, Use-Case-Production-Gate-Pfade, Race-Tiebreak (höchste seq → newer timestamp → lex-smaller Tupel), aFRR-Up/Down-Profil-Pins, mFRR-modelable-aber-not-dispatched, TimebaseDebounce-Pipeline-Recovery; 12 Persistence-Integration-Pins (Restart-Replay, Conflict-After-Restart, Tracker-Load-Fail-Closed pro Sub-Case, 0002-idempotent, Persistenz-Determinismus); 1 API-Endpoint-Test. **Design-Entscheidungen** (D-01 Validation-Reihenfolge, D-02 FUP-01 inline, D-03 Production-Gate mehrstufig + Security-Profile fail-closed bis F-12, D-04 Debounce-Konstanten domain-verdrahtet, D-05 mFRR modelable-not-dispatched, D-06 Source-Adapter-Driving-Port-Form, D-07 Dapper-Stack reuse, D-08 ReserveProduct/ReserveDirection reuse, D-09 c IActivationDispatchSource additiv) sind im Slice-Plan dokumentiert. **Bewusst draußen** mit konkretem Trigger: F-08 produktive mFRR-MOLS/MARI-Aktivierung; F-09 konkrete Source-Wire-Adapter (RM-M4-04 deckt nur Telemetrie/Command, Activation-Subscribe ist F-09 oder RM-M4-04-Carve-out); F-10 TSO-Aktivierungsquittung; F-11 Dedupe-Store-Migration v1→v2-Template; F-12 generisches RuntimeProfile/Security-Profile als Production-Gate-Signal (`security-profile-enforcement-not-wired` bleibt heute fail-closed). |
| ✅ | RM-M4-04 | OPC-UA-Adapter (Lesen, Schreiben, Subscriptions, StatusCode) | Slice-Plan: [`done/plan-RM-M4-04.md`](../done/plan-RM-M4-04.md). Adapter-Projekt `BatteryEms.Adapters.OpcUa` implementiert die bestehenden Driven-Ports `IBatteryTelemetrySource` und `IBatteryCommandSink`; Production-Wrapper `OpcUaClient` bindet `OPCFoundation.NetStandard.Opc.Ua.Client` (MIT-lizenziert per D-01) gegen `Opc.Ua.Client.Session`. `OpcUaTelemetrySource` liefert Read+Subscribe mit Worst-of-DataQuality (LH-OPCUA-004), Sticky-Overflow-Flag bei Channel-Drops (D-03) und IAsyncDisposable-Lifecycle (D-09); `OpcUaCommandSink` schreibt Setpoints mit ScaleFactor-Reverse + StatusCode-Auswertung. Mehrere konfigurierte IO-Familien (Modbus + MQTT + OPC-UA) sind fail-closed via `IoAdapterTriage`. Security: heute `MessageSecurityMode.None` mit AllowUnsecured-Startup-Guard auf der bool-Achse (D-04) — produktive Härtung folgt mit RM-M4-05; OPC-UA-Activation-Source bleibt F-09. Embedded TestServer im neuen `tests/integration/BatteryEms.OpcUa.IntegrationTests/`-Projekt fährt 5 pinned End-to-End-Tests (Read, Subscribe, Write, StatusCode, Reconnect) gegen einen process-internen `OPCFoundation.NetStandard.Opc.Ua.Server`-NodeManager — kein Compose-Sidecar. Makefile-Target `make test-hil-opcua` führt das Projekt aus. **Bewusst draußen** mit konkretem Trigger: F-09 OPC-UA-Activation-Source-Subscribe (M4-04 lehnt M4-03-Carve-out ab); F-13 Multi-Server-/Endpoint-Failover; F-14 Method-Calls/HistoricalAccess/Events; F-15 Type-System-Erweiterung (Strukturen/Arrays/Enums); F-16 Compose-Sidecar-Fallback wenn Embedded-TestServer-Aufwand explodiert (heute nicht gezündet). |
| ✅ | RM-M4-05 | OPC-UA-Security (Zertifikate, Security Mode/Policy) | Slice-Plan: [`done/plan-RM-M4-05.md`](../done/plan-RM-M4-05.md). `OpcUaRuntimeProfile`-Field (`Development`/`HilSimulator`/`Production`, Default `Production`) plus `SecurityPolicy`-Allowlist (M4-Start: `Basic256Sha256`; Erweiterung verlangt Planänderung per D-04) auf `OpcUaAdapterOptions`. Production-Default schwenkt auf `SecurityMode=SignAndEncrypt`. `EnsureValid` wirft `opcua-security-not-hardened-in-production` bei `RuntimeProfile=Production` + `SecurityMode=None` (D-02 — der AllowUnsecured-Bool ist im Production-Profile bewusst nicht ausreichend), `opcua-security-policy-not-allowlisted` bei Off-Allowlist-Policy, `opcua-allow-unsecured-with-secure-mode-inconsistent` bei Konfigurations-Inkonsistenz. `OpcUaClient` bindet `AddSignAndEncryptPolicies` plus echtes Cert-Trust ohne AutoAccept im Production-Profile; `RuntimeProfile=HilSimulator|Development` behält das pre-M4-05-AutoAccept-Verhalten für Test-Defaults. Embedded TestServer-Fixture aus M4-08-A um SignAndEncrypt-Policies + bidirektionale Trust-Bridge erweitert. 6 pinned Security-Pins in `OpcUaSecurityTests.cs` (Secure-Handshake SignAndEncrypt, Sign-Mode-Handshake, Allowlist-Reject, Production-Fail-Closed, HilSimulator-Override, Trust-Store-Miss). `make test-hil-opcua` läuft jetzt mit 13 Pins gesamt in `make gates` und `make ci`. Cross-Adapter-RuntimeProfile-Source bleibt **F-12** (M4-03-Followup); Allowlist-Erweiterung ist **F-17**; Cert-Rotation/Renewal ist **F-18**; User/Token-Identity ist **F-19** (alle in `note-RM-M4-followups.md`). |
| ✅ | RM-M4-06 | MQTT QoS und Command-ACK-Korrelation | Adapter: neuer `MqttQualityOfService`-Enum (Spec-Wire-Werte 0/1/2) plus `MqttQosOptions`-Record mit per-Channel-Defaults (CommandPublish/CommandAckSubscribe/StatusSubscribe/FaultSubscribe = `AtLeastOnce`, TelemetrySubscribe = `AtMostOnce`). `MqttAdapterOptions` trägt jetzt `QoS`-Property mit `QoSOrDefault`-Fallback. `IMqttClient.PublishAsync`/`SubscribeAsync` haben einen `qos`-Parameter; `MqttNetClient` mappt zur MQTTnet-`MqttQualityOfServiceLevel`. `MqttCommandSink` zieht `CommandPublish`/`CommandAckSubscribe` aus den Options durch; `MqttTelemetrySource` zieht `TelemetrySubscribe`. CommandId-basierte ACK-Korrelation war schon da; Mismatch (ACK mit unbekannter CommandId) wird silent gedropt → Pending-Command läuft in `ack-timeout`-Failed-Run, jetzt explizit gepinnt. Multiple-Pending-Korrelation pro CommandId pin-getestet. SECURITY-Kommentar in `MqttNetClient.cs` von „M2 work" auf F-04-Verweis umgestellt. Tests: 9 neue Mqtt-Adapter-Pins (Defaults, Wire-Werte, QoSOrDefault-Fallback, CommandSink-Publish-QoS, CommandSink-ACK-Subscribe-QoS, TelemetrySource-Subscribe-QoS, Custom-Override, ACK-Mismatch, Multiple-Pending). FakeMqttClient-Test-Stub erweitert um QoS-Recording plus `SubscribedTopicNames`-Convenience-Projektion. **Design-Entscheidungen** (verbindlich für den Slice): **D-01** TLS und Broker-Auth bleiben im RM-M4-06-Slice draußen — der Adapter spricht plaintext-TCP zum Broker. Der SECURITY-Kommentar in `MqttNetClient.cs` ist bei Implementierung auf F-04 umgestellt. **`MqttNetClient` ist nicht für Production gegen einen echten Broker freigegeben, bevor F-04 zündet.** **D-02** ACK-Tracking ist in-process: `ConcurrentDictionary<commandId, TCS>` lebt im `MqttCommandSink`-Singleton. Reconnect oder Process-Restart verliert Pending-Commands; das resultierende `ack-timeout` ist die akzeptierte Recovery-Semantik. Persistente Cross-Restart-Tracking ist Folgearbeit F-03. **Mismatch-Semantik**: ein ACK mit unbekanntem oder fremdem `CommandId` wird silent gedropt — es gibt **keinen** dedizierten `ack-mismatch`-Reason. Begründung: ein Fremd-ACK hat kein zuordenbares Originating-Command auf der EMS-Seite, also keine sinnvolle Stelle wo ein Failed-Result attached werden könnte. Das betroffene wartende Command surfaced den Misserfolg über `ack-timeout` (DoD-konform, „Mismatch erzeugen einen fehlerhaften CommandDispatchResult"). **D-03** QoS-Defaults für Production: Command-Publish und Command-ACK-Subscribe `AtLeastOnce`, Telemetrie-Subscribe `AtMostOnce` (Stream-Charakter). `MqttQosOptions` trägt heute nur diese drei Channels — Status- und Fault-Subscribe-Slots werden erst dann ergänzt, wenn der zugehörige Subscriber-Konsument landet (MqttTelemetrySource subscribed heute nur `telemetry`). `ExactlyOnce` (QoS 2) wird **nicht** als Default angeboten: der App-level-ACK liefert Idempotenz auf der MQTT-Round-Trip-Schicht zusätzlich zum Broker-Round-Trip; QoS-2-Overhead (PUBREC/PUBREL/PUBCOMP) trägt auf dieser Schicht keinen messbaren Mehrwert. **Warn-don't-block**: ein Operator kann `ExactlyOnce` via `MqttAdapterOptions.QoS` setzen wenn eine TSO/Compliance-Anforderung das fordert; es gibt **bewusst keine** Startup-Validierung gegen den Wert. Falls eine zukünftige Compliance-Linie eine explizite Bestätigung verlangt (analog zum geplanten OPC-UA `AllowUnsecured`-Pattern), zündet F-06. **D-04** MQTTv3.1.1-Shape bleibt erhalten — v5-spezifische Properties (User Properties für Multi-Tenant-Routing-Metadaten, strukturierte Reason-Codes für Ablehnungs-Diagnostik) sind nicht im Slice. Folgearbeit F-05 wenn der Broker auf v5 hochzieht. |
| ✅ | RM-M4-07 | Versionierte OPC-UA-Mappings in Config | Schema-Datei `config/schema/opcua-mapping.schema.json` mit `schema_version: ["v1"]`-Constraint, `additionalProperties:false`/`unevaluatedProperties:false` (Drift-Detection ohne Zusatz-Logik), `device-point.json`-Embed für LH-DOM-005-Metadaten, NodeId-Pattern für OPC-UA-Notation (`ns=N;i=…`/`s=…`/`g=GUID`/`b=…`), `direction`-Enum (`read`/`write`/`subscribe`), `data_type`-Enum (`bool`/`int*`/`uint*`/`float`/`double`/`string`), `scale_factor` mit `not:{const:0}`-Schutz, `if/then`-Validation für `writable=true` ⇒ `write_cadence`+`auth_required` und `direction=write` ⇒ `writable=true`. Application: `OpcUaMappingConfiguration` (record: SchemaVersion, ProfileName, Nodes-List) + `OpcUaNodeMapping` (record mit Name/NodeId/Direction/DataType/ScaleFactor/Writable/AuthRequired plus optional WriteCadence, MonitoringIntervalMs, DevicePoint). Driving Port: `IConfigurationLoader.LoadOpcUaMapping(filePath)`. Loader: `JsonFileConfigurationLoader` lädt das Schema ergänzend zu Modbus/MQTT, **prüft `schema_version` vor JSON-Schema-Validation für strukturierte `unsupported-schema-version`-Diagnose** (besser als generische enum-violation), parst zu `OpcUaMappingConfiguration`. Beispiel: `config/examples/adapters/opcua.simulator.json` mit 7 Nodes (SOC, ActivePower, ReactivePower, Temperature, FaultCode, ActivePowerSetpoint, ReactivePowerSetpoint) — Mix aus `read`/`subscribe`/`write` Directions plus realistischen NodeIds in string-Notation. Tests: 10 Pins (Beispiel-Mapping lädt, missing required field, scale_factor=0, malformed node_id, writable-ohne-write_cadence, direction=write-ohne-writable=true, unbekanntes Feld, deprecated v0, incompatible v2, missing schema_version). **Design-Entscheidungen** (verbindlich für den Slice): **D-01** Eine Datei = ein Profil. Multi-Vendor-Mix in einer Datei und `$ref`-Include-Mechanismus sind out-of-scope (sibling-pattern mit Modbus/MQTT). Bei späterem Bedarf eigene Folgearbeit, heute kein Trigger. **D-02** Migration v1→v2 ist leer für M4-07 — nur v1 existiert, kein Migration-Code im Loader. Erste echte Migration ist Folgearbeit F-07 (Template-Slice; siehe `note-RM-M4-followups.md`). **D-03** Strikter Schema-Drift-Check: `additionalProperties: false` + `unevaluatedProperties: false` analog zu Modbus/MQTT. Ein unbekanntes Feld kippt den Boot, kein silent-ignore — Drift-Detection läuft dadurch implizit ohne Zusatz-Logik. **D-04** NodeId-Validation ist strukturell, nicht semantisch — Pattern-Check gemäß OPC-UA-Spec (`ns=N;i=…`/`s=…`/`g=GUID`/`b=…`); kein Round-Trip gegen einen echten Server in M4-07. Semantischer Pfad (NodeId existiert in Server-Namespace, Datentyp matcht) kommt mit RM-M4-04 (Adapter) wenn Discovery aktiv ist — bereits im RM-M4-04-DoD-Wortlaut adressiert. | **Design-Entscheidungen** (verbindlich für den Slice): **D-01** Eine Datei = ein Profil. Multi-Vendor-Mix in einer Datei und `$ref`-Include-Mechanismus sind out-of-scope (sibling-pattern mit Modbus/MQTT). Bei späterem Bedarf eigene Folgearbeit, heute kein Trigger. **D-02** Migration v1→v2 ist leer für M4-07 — nur v1 existiert, kein Migration-Code im Loader. Erste echte Migration ist Folgearbeit F-07 (Template-Slice; siehe `note-RM-M4-followups.md`). **D-03** Strikter Schema-Drift-Check: `additionalProperties: false` + `unevaluatedProperties: false` analog zu Modbus/MQTT. Ein unbekanntes Feld kippt den Boot, kein silent-ignore — Drift-Detection läuft dadurch implizit ohne Zusatz-Logik. **D-04** NodeId-Validation ist strukturell, nicht semantisch — Pattern-Check gemäß OPC-UA-Spec (`ns=N;i=…`/`s=…`/`g=GUID`/`b=…`); kein Round-Trip gegen einen echten Server in M4-07. Semantischer Pfad (NodeId existiert in Server-Namespace, Datentyp matcht) kommt mit RM-M4-04 (Adapter) wenn Discovery aktiv ist — bereits im RM-M4-04-DoD-Wortlaut adressiert. |
| ✅ | RM-M4-08 | Integrationstests OPC-UA gg. Simulator | Slice-Plan: [`done/plan-RM-M4-08.md`](../done/plan-RM-M4-08.md). Multi-Cycle-Reconnect-Pin (drei Server-Restart-Cycles in einer Source-Lifetime, post-Sample-Assertion `SubscriptionCount==1`, post-Dispose `SubscriptionCount==0`) und Concurrent-Source-Sink-mit-Restart-Pin (30 Commands über 3s + Restart bei 1.5s probt `_connectGate`/`_stateGate`-Contention) im Embedded TestServer-Projekt grün; `make test-hil-opcua` läuft im Pflicht-`make ci` und `make gates`. Race-/Tiebreak-/Duplikat-/Process-Restart-Replay-/TimebaseDegraded-/Persistenz-Pins liegen in **RM-M4-03 ✅** (8 Application-Test-Files mit ~110 Pins + 12 Persistence-Pins) und werden bewusst nicht via OPC-UA-Wire dupliziert (D-01). Security-Basispfad gegen OPC-UA-Simulator ist **RM-M4-05** (separates Slice). Aktivierungsjitter via OPC-UA-Wire und **Failover-Replay-Pin via OPC-UA-Reconnect** sind **F-09 (a)/(b)/(c)** in `note-RM-M4-followups.md` mit konkretem Trigger (TSO-Spec mit OPC-UA-Aktivierungsendpoint oder Operator-Anforderung nach Mid-Stream-Reconnect-Replay-Verifikation). Quality-Doku trägt `make test-hil-opcua` als Mandatory Gate. **Bug-Fund** in M4-08-A: der Multi-Cycle-Pin hat einen client-seitigen Subscription-Leak in `OpcUaClient._subscriptions` aufgedeckt (SDK setzt `Subscription.Id` nach `DeleteAsync` zurück) — gefixt via cached-Id im Wrapper. |

---

## Sequenz

1. RM-M4-07 vorziehen, sobald der OPC-UA-Adapter startet: Mapping-Schema
   und Beispielprofil stabilisieren die Adapterarbeit.
2. RM-M4-04 und RM-M4-08 gemeinsam schneiden: Adapter ohne Simulator-Gate
   bleibt zu riskant.
3. RM-M4-05 nach dem Basispfad ergänzen, bevor OPC-UA produktiv
   aktivierbar wird.
4. RM-M4-01 und RM-M4-02 als Optimierungswelle bauen, weil
   Resthorizont und Reserve-Constraints denselben Schedule-Optimizer
   berühren.
5. RM-M4-03 erst aktivieren, wenn Reserve-Modell und Prioritätsvertrag
   klar sind.
6. RM-M4-06 unabhängig härten, aber vor M4-Abschluss ins Gate nehmen,
   damit Command-Acknowledgement für alle Command-Pfade konsistent ist.

---

## Akzeptanzkriterien

- Bei aktiver valider Regelleistungsanforderung übersteuert der
  Regelkreis den normalen Fahrplan, ohne Safety-Limits zu verletzen.
- In Production ist RL-Dispatch hart gated: Ohne
  `Regelleistung:ProductionActivationEnabled=true`, dokumentierte
  Produktannahme, Profilfreigabe und grüne Health-Checks bleibt jede
  Aktivierung nicht dispatch-relevant.
- `ControlPriorityClass` ist im Regelkreis und in Tests nachgewiesen:
  `EmergencyStop`, `SafetyLimit`, `RegelleistungsActivation`,
  `BindingMarketCommitment`, `IntradaySchedule`, `DayAheadSchedule`,
  `LocalOptimization`.
- Emergency Stop und Safety-Limits haben weiterhin höhere Priorität als
  Regelleistungsaktivierung.
- Day-Ahead- und Intraday-Optimierung verletzen keine reservierten
  Regelleistungsbereiche.
- Ein bestehender Fahrplan kann für einen Resthorizont neu bewertet und
  versioniert, atomar und per CAS ersetzt werden.
- Scheitert die Resthorizont-Reoptimierung, wird kein leerer oder
  teilweiser Fahrplan aktiv; die bisherige gültige Zukunftsversion bleibt
  der Fallback.
- Kollidierende Schedule-Writer erzeugen keinen Last-Writer-Wins-Effekt:
  einer gewinnt den Commit-Lock/CAS, alle anderen brechen sichtbar ab und
  müssen mit frischem Schedule-State neu planen.
- OPC-UA integriert sich über `IBatteryTelemetrySource` und
  `IBatteryCommandSink`; die zentrale Regelpipeline braucht keinen
  protokollspezifischen Zweig.
- OPC-UA-Mappings sind versioniert, schema-validiert und mit
  Device-Point-Metadaten kompatibel.
- Schlechte OPC-UA-StatusCodes und stale Aktivierungssignale werden als
  ungültig behandelt und sind in Tests sichtbar; stale/future-skew
  folgen den RM-M4-03-Profilwerten.
- Aktivierungs-Dedupe ist idempotent: exakte Wiederholungen werden
  innerhalb des dedupe-Fensters akzeptiert, widersprüchliche Replays und
  gleichrangige Mehrquellenkonflikte werden deterministisch verworfen oder
  deterministisch auf einen persistierten Gewinner reduziert.
- Dedupe greift nie vor Safety-/Zeitvalidierung: Auch ein bekannter
  Replay-Key muss bei jeder Rezeption `max_age`, `future_skew_tolerance`
  und `TimebaseDegraded` bestehen, bevor er erneut dispatch-relevant sein
  darf.
- Der Gewinner bei gleichrangigen Aktivierungssignalen ist vollständig
  deterministisch: `sequence_number`, dann `signal_timestamp_utc`, dann
  lexikografischer `(source_id, activation_id/message_id)`-Tiebreak; bei
  vollständigem Gleichstand mit widersprüchlichem Payload gibt es keinen
  Gewinner.
- Dedupe-/Replay-Schutz überlebt Restart, Failover und OPC-UA-Reconnect:
  der letzte akzeptierte Aktivierungs-Checkpoint pro `source_id` ist
  persistent und versioniert.
- Dedupe-Retention ist begrenzt und sicher: Kompaktierung hält mindestens
  den letzten Checkpoint und alle noch gültigen/replay-relevanten
  Einträge; Größenlimit- und Kompaktierungstests verhindern unbounded
  growth.
- Ist der persistente Dedupe-/Replay-Tracker beschädigt, inkompatibel
  oder nicht eindeutig migrierbar, bleibt RL-Aktivierung fail-closed
  deaktiviert; Health/Logs/Metriken melden `dedupe-store-invalid`.
- Bei instabiler Zeitquelle geht RL-Aktivierung nach Debounce in
  `TimebaseDegraded`; bis zur Wiederherstellung bleibt der Regelkreis im
  sicheren Fahrplan-/Fallback-Pfad und markiert RL-Aktivierungen als nicht
  dispatch-relevant.
- OPC-UA startet in Production nicht mit `SecurityMode=None`; ungesicherte
  Testprofile brauchen explizites Opt-in, nicht-leeren Grund und Warn-Log.
  `RuntimeProfile=Production` ist technisch validiert; fehlendes oder
  widersprüchliches Profil failt geschlossen.
- OPC-UA akzeptiert nur allowlistete Security Policies; für M4 ist
  `Basic256Sha256` die einzige initiale Policy. Unbekannte, schwächere
  oder nicht dokumentiert freigegebene Policies failen geschlossen.
- MQTT-Command-ACK-Korrelation ist über `CommandId` getestet, inklusive
  Timeout und falscher ACK-ID.
- Quality-Doku und Roadmap werden beim Abschluss synchronisiert.

---

## Risiken und Entscheidungen

- **Produktprofil-Freigabe.** LH-OPEN-002 ist fachlich geklärt: relevant
  sind FCR, aFRR und mFRR. Das ersetzt aber keine produktive
  Profilfreigabe. M4 startet mit FCR-Reservierung und aFRR-Aktivierung
  als realen Produktprofilen; mFRR bleibt vorbereitet, aber nicht
  produktiv aktivierend. Das Production-Gate bleibt bis zur
  profilbezogenen Freigabe hart deaktiviert
  (`Regelleistung:ProductionActivationEnabled=false`).
- **Echtzeitfähigkeit.** Wenn Aktivierungsfristen enger sind als der
  aktuelle .NET-Regelkreis belastbar erfüllt, ist ein ADR für Native-,
  Edge- oder Out-of-Process-Design nötig. Das ist nicht implizit Teil von
  M4.
- **OPC-UA-Bibliothek.** Die konkrete .NET-OPC-UA-Library wird im ersten
  Adapter-Slice entschieden. Auswahlkriterien: Security-Support,
  Subscription-Stabilität, Testserver-/Simulatorfähigkeit,
  Lizenzkompatibilität und CI-Reproduzierbarkeit.
- **Mapping-Drift.** Hersteller-NodeIds und Firmware-Versionen können
  auseinanderlaufen. Deshalb muss RM-M4-07 versionierte Mapping-Dateien,
  Backward-Compatibility-/Migrationstests und Startup-Fail-Closed bei
  inkompatiblen Mapping-Versionen vor produktiver Adapteraktivierung
  liefern.
- **Prioritätskonflikte.** M2 hat Market Commitments bereits priorisiert;
  M4 erweitert diese Ordnung um `ControlPriorityClass`. Tests müssen
  zeigen, dass bestehende Day-Ahead-/Intraday-Pfade nicht versehentlich
  höhere Priorität behalten und dass Tiebreaks deterministisch sind.
- **Profil- und Latenzdrift.** Die Default-Fenster für Aktivierungssignale
  sind konservative Startwerte, nicht Produktgarantien. Produktive
  Regelleistung braucht gemessene Jitter-/Latenzprofile und dokumentierte
  Optionswerte, sonst bleibt Aktivierung in Production deaktiviert.
- **Persistenter Replay-Schutz.** Dedupe nur im Speicher reicht nicht,
  weil Restart, Failover und OPC-UA-Reconnect alte Signale erneut liefern
  können. RM-M4-03 muss daher einen versionierten Checkpoint pro Quelle
  persistieren oder die Aktivierung bleibt nicht produktionsfähig.
- **Storage-Fehler im Commit-/Checkpoint-Pfad.** Fällt der Commit-Lock,
  Schedule-CAS oder Dedupe-Checkpoint-Store aus, darf kein partieller
  Schedule und keine RL-Aktivierung wirksam werden. Der Pfad failt
  geschlossen, nutzt den bisherigen Fahrplan/Fallback und emittiert
  Recovery-/Rollback-Diagnose.
