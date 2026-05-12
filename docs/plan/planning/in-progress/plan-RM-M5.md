# Plan RM-M5 MPC, Solver-Sidecar, Replay-Plattform

**Dokumenttyp:** Detailplan / M5 (aktiv)
**Status:** In Arbeit - aktiviert am 2026-05-11 nach Abschluss von M4
und ADR 0005 (gRPC-Adoption schließt AR-OPEN-002). Erstes Arbeitspaket
RM-M5-01 hat eigenen Slice-Plan [`done/plan-RM-M5-01.md`](../done/plan-RM-M5-01.md) (abgeschlossen am 2026-05-11 inkl. Sub-Slice-C-Korrektur-Pass). RM-M5-02 hat eigenen Slice-Plan [`done/plan-RM-M5-02.md`](../done/plan-RM-M5-02.md) (abgeschlossen am 2026-05-12 — MPC-Kernel-Backend mit Local-OSQP, Kalman-Estimator, Replay-Stempeln und Production-Gates). RM-M5-04 hat eigenen Slice-Plan [`done/plan-RM-M5-04.md`](../done/plan-RM-M5-04.md) (abgeschlossen am 2026-05-12 — Manifest-/Fixture-/Golden-Diff-Grundlage, M2/M3-Migration, Replay-Diff-Reports und Sidecar-/MPC-Engine-Vergleiche). RM-M5-05 hat eigenen Slice-Plan [`done/plan-RM-M5-05.md`](../done/plan-RM-M5-05.md) (abgeschlossen am 2026-05-12 — Prometheus-Metriken fuer Sidecar-Health, Fallback-Taxonomie, Terminalzustand und Laufzeit). RM-M5-06 hat eigenen Slice-Plan [`done/plan-RM-M5-06.md`](../done/plan-RM-M5-06.md) (abgeschlossen am 2026-05-12 — Worker-plus-Sidecar-Compose-Gate mit Sidecar-Stopp, Fallback und Restart). RM-M5-07 hat eigenen Slice-Plan [`done/plan-RM-M5-07.md`](../done/plan-RM-M5-07.md) (abgeschlossen am 2026-05-12 — source-neutraler Preisreihen-Port plus Import/API-Referenzpfad).
**Bezug:**
[`roadmap.md`](roadmap.md) (M5),
[`../../adr/0005-optimization-core-sidecar-transport.md`](../../adr/0005-optimization-core-sidecar-transport.md)
(gRPC-Transport-Adoption, schließt AR-OPEN-002),
[`../../adr/0003-native-kernel-language.md`](../../adr/0003-native-kernel-language.md)
(Phase-3-MPC als Re-Evaluierungsanker),
[`../../adr/0004-native-kernel-process-isolation.md`](../../adr/0004-native-kernel-process-isolation.md)
(Sidecar-Re-Evaluierung fuer MPC/Solver),
[`../../../user/quality.md`](../../../user/quality.md) (§2.5 Replay,
§2.6 Container, §5.2 Native/Sidecar),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
(§8.2 Optimierungs-Interface, §13 Native Core / Sidecars),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(LH-OPT-002/003/006, LH-CTRL-005/006, LH-NATIVE-001, LH-TEST-004,
LH-MON-002, LH-TEST-007, LH-OPEN-003, LH-MKT-008)

---

## Zweck

M5 zieht die Phase-3-Themen aus der Roadmap in einen umsetzbaren Slice:
ein out-of-process `optimization-core`-Sidecar fuer LP/MILP/MPC-nahe
Optimierung, einen MPC-/State-Space-Kern, eine ausgebaute
Replay-Plattform und Sidecar-spezifische Observability- und
Container-Gates.

Der Plan baut auf der M2-Optimierungsbasis und dem M3-Native-Control-Core
auf. M2 liefert `IScheduleOptimizer`, Optimierungslauf-Persistenz,
Solverstatus und erste Replay-Nachweise. M3 liefert den nativen
In-Process-Pfad fuer kleine, deterministische Control-Kerne. M5 darf diese
Grenzen nicht verwischen: groessere numerische Kerne, Solver und
MPC-State laufen ueber einen expliziten IPC-Vertrag, waehrend der
Regelkreis bei Sidecar-Ausfall kontrolliert auf vorhandene .NET- oder
NoOp-/Schedule-Fallbacks zurueckfaellt.

M5 ist kein UI- oder Multi-Asset-Skalierungsmeilenstein. Die
Replay-Plattform darf Datensaetze und Vergleiche fuer spaetere
Operator-Workflows vorbereiten, aber M6 bleibt der Ort fuer UI,
zertifizierungsnahe Regelleistungsvertiefung und breite
Multi-Asset-Bedienung.

---

## Abgrenzung

**In Scope:**

- Transportneutraler Sidecar-Vertrag fuer `optimization-core` mit Health,
  Version, Optimierungsrequest, Cancellation/Deadline,
  Request-Idempotenz und strukturierten Fehler-/Solverstatus-Rueckgaben.
- Driven Adapter im .NET-Host, der das Sidecar hinter bestehenden
  Optimierungsports integriert und Solver-Auswahl per Konfiguration
  erlaubt.
- LP/MILP/MPC-faehige Request-/Response-Modelle, ohne Application oder
  Domain an einen konkreten Solver zu koppeln.
- Quellenneutraler Preisreihen-Port (`IPriceSeriesSource`) plus
  Import/API-Pfad fuer Optimierungs-Preise. Der M5-Default bindet keine
  externen Marktpreis-Anbieter direkt an.
- MPC-Kernel mit State-Space-Modell, Schaetzer-/Kalman-Pfad,
  Vorhersagehorizont und Constraint-Ausgabe, soweit fuer LH-CTRL-005/006
  notwendig.
- Optionaler Native-Core-Slice fuer hochfrequente
  Telemetrie-Filterung, falls der konkrete MPC-/State-Space-Pfad diese
  Vorverarbeitung braucht.
- Replay-Plattform mit versionierten Datensaetzen,
  Metadaten/Manifesten, Golden-Command-/Sollwertvergleich und
  Drift-Auswertung zwischen Managed-, Native-, Solver- und
  Sidecar-Pfaden.
- Metriken fuer Solverstatus, Solver-/MPC-Laufzeit, Sidecar-Health,
  Deadline/Timeout, `fallback_source`, `fallback_reason`,
  Terminalzustand und Command-Latenz.
- Container-Orchestrierungstests fuer Worker + Sidecar inklusive
  Healthcheck, Startreihenfolge, Crash/Restart und Fallback.

**Out of Scope:**

- Ersatz des M3-In-Process-Control-Kernels durch ein Sidecar ohne
  konkreten MPC-/Solver-Trigger.
- Operator-UI fuer Replay-Datensatzverwaltung; bleibt M6 oder ein
  eigener UI-Slice.
- Produktive Multi-Asset-Flottenoptimierung, Scheduling-UI oder
  Edge-Orchestrierung; bleibt M6.
- Regelleistungsprodukt- oder Praequalifikationslogik; M4/M6.
- Freie Solver-Experimentierflaeche ohne Port-, Replay- und
  Observability-Vertrag.
- Externe Marktpreis-Anbieteradapter (z. B. ENTSO-E, EPEX, Tibber,
  aWATTar oder Aggregatoren). Solche Adapter brauchen eine eigene
  Quellenentscheidung mit Lizenz-, Auth-, Rate-Limit- und Caching-Pruefung.
- Harte Echtzeitgarantien. M5 muss Deadlines und Fallback testen, aber
  keine zertifizierte Echtzeitplattform behaupten.

---

## Aktivierungsbedingungen

M5 ist seit 2026-05-11 aktiviert: M4 ist abgeschlossen (alle 8 Pflicht-
Slices ✅) und ADR 0005 schliesst `AR-OPEN-002` mit gRPC-Adoption,
Security-Achse (UDS-Default + mTLS-Cross-Host) und Mocking-/CI-
Konsequenzen. M2/M3-Basisgates auf `main` sind gruen. Damit ist der
formale Transport-Blocker geloest und RM-M5-01 darf Code beginnen.

| Check | Erwartung |
| ----- | --------- |
| M2-Optimierung | `IScheduleOptimizer`, `OptimizationRun`, Solverstatus und Persistenz sind stabil; Sidecar-Integration ersetzt keinen Domain-Vertrag. |
| M3-Native | In-Process Native bleibt fuer kleine Control-Kerne verfuegbar; M5 fuehrt Sidecar nur fuer MPC/Solver-grosse Kerne ein. |
| M4-Schnitt | Falls Regelleistungs-Reserve-Constraints Teil des MPC-/MILP-Modells werden, muss das M4-Reservemodell abgeschlossen oder als explizite Planannahme dokumentiert sein. |
| ADR-Status | ✅ ADR 0005 fixiert gRPC-Transport, Security-Achse (UDS-Default / mTLS-Cross-Host), Contract-Versionierung, Mocking-Strategie und Phase-4-Pivot-Trigger. ADR 0004 bleibt fuer den In-Process-Pfad des `battery_control_core` unveraendert (orthogonale Linie). |
| Transportentscheidung | ✅ `AR-OPEN-002` geschlossen mit ADR 0005; `spec/architecture.md` §18 referenziert die Closure-Zeile. |
| Transport-Mapping | Vor RM-M5-01-Freeze existiert ein versioniertes Transport-Mapping-Dokument, das konkrete Transportcodes auf die normierten Outcomes der Sidecar-Status-Taxonomie mappt und Retry-, Cancellation-, Deadline- und Unavailable-Regeln festlegt. |
| Contract-Version | Worker und Sidecar melden `contract_version`, `min_compatible_version`, `max_compatible_version` und Feature-Flags im Health/Version-Check. Inkompatible Versionen blockieren Sidecar-Aktivierung hart und fallen vor Request-Start auf lokalen Fallback/Safe-Stop. |
| Security-Freeze | Vor produktionsnahem RM-M5-01-Freeze ist der Sidecar-Security-Vertrag abgeschlossen: AuthN/AuthZ, verschluesselter Transport oder geschuetzter lokaler Socket, Secret-Handling und Negativtests fuer unautorisierte Clients sind dokumentiert und gate-faehig. |
| Replay-Baseline | Bestehende M2/M3-Replay-Datensaetze sind referenzierbar; neue Datensaetze bekommen Manifest, Version und Vergleichsregeln. |
| Toolchain | Transport-Codegen falls noetig, Sidecar-Build und Container-Build sind reproduzierbar in Docker/CI. |
| Fallback | Fuer jeden Sidecar-Aufruf ist vor Aktivierung dokumentiert, welcher lokale Fallback benutzt wird und welche Semantik dadurch verloren geht. |

---

## Zielbild Und Abnahmeschnitt

| Situation | Erwartetes Verhalten | Mindestnachweis |
| --------- | -------------------- | --------------- |
| Sidecar gesund | Worker ruft `optimization-core` ueber den konfigurierten Adapter auf; RunId, Solverstatus, Horizon und erzeugter Fahrplan werden wie im bestehenden Optimierungsmodell persistiert. | Contract-/Integrationstest mit Test-Sidecar und persistiertem `OptimizationRun`. |
| Sidecar nicht erreichbar | Regelkreis und Worker bleiben lauffaehig; Optimierungsaufruf liefert kontrollierten Fehler/Fallback statt Crash oder haengendem Tick. | Timeout-/Unavailable-Test mit `fallback_reason` in Log/Metrik. |
| Sidecar crasht waehrend Lauf | Offener Request endet deterministisch mit Fehlerstatus; nach Restart kann ein neuer Lauf starten. | Container-Orchestrierungstest Worker + Sidecar mit Crash/Restart. |
| MPC erzeugt Trajektorie | MPC-Ausgabe respektiert SOC-, Leistungs-, Ramp- und Geraete-/Netzgrenzen; nachgelagerte Limiter muessen keine unzulaessige Trajektorie retten. | MPC-Testfall gegen definierte State-Space-/Horizon-Fixture plus Limiter-Invariantentest. |
| Kalman-/State-Space-Schaetzung | Schaetzer liefert finite, plausible Zustandswerte und markiert unbrauchbare Eingaben als ungueltig. | Numerischer Kerneltest mit Rausch-/Missing-Measurement-Faellen. |
| Replay-Datensatzvergleich | Derselbe Datensatz kann gegen mehrere Engines laufen und erzeugt reproduzierbare Commands/Sollwerte oder eine erklaerte Drift. | Replay-CLI-/Testtarget mit Manifest, Golden-Datei und Diff-Report. |
| Erweiterte Metriken | Solverstatus, Sidecar-Health, Laufzeit, Deadline/Timeout, Fallback und Command-Latenz sind im Prometheus-Pfad sichtbar. | Metrics-Test mit erfolgreichem Lauf, Timeout und Fallback. |

---

## Request-Idempotenz Und Retry

Jeder Sidecar-Aufruf traegt eine eindeutige `request_id` und eine
fachliche Idempotency-Key-Kombination aus `asset_id`, Schedule-Type,
Horizon, Input-Versionen, Constraint-/Limit-Version und Engine-Version.
`run_id` darf daraus abgeleitet oder separat vergeben werden, muss aber
eindeutig mit `request_id` korrelierbar bleiben.

| Thema | Regel |
| ----- | ----- |
| Deduplizierung | Wiederholte Requests mit gleicher `request_id` duerfen hoechstens einen `OptimizationRun` und hoechstens eine Schedule-Version erzeugen. |
| Persistenter Idempotency-Store | Der Worker fuehrt den Idempotency-Store in derselben persistierten Datenbank wie `OptimizationRun`/Schedule-Versionen. Der Sidecar bleibt fuer Aktivierungseffekte stateless und darf keine eigene wirksame Idempotency-Persistenz fuehren. Sidecar- und Fallback-Aktivierung muessen vor jeder Wirkung einen persistenten Idempotency-Eintrag mit Unique-Constraint auf `request_id` anlegen oder laden. Der Terminalzustand wird atomar und restart-fest gespeichert; ein Worker-Restart darf dieselbe `request_id` nur anhand dieses Stores fortsetzen oder als Duplicate verwerfen. |
| Retry-Grenze | Automatische Retries sind nur fuer Transportfehler erlaubt, bei denen keine Sidecar-Annahme nachweisbar ist. Sie verwenden dieselbe `request_id`; neue `request_id` bedeutet neuer Lauf und braucht neuen Run-Kontext. |
| Atomare Finalisierung | Pro `request_id` gibt es genau einen atomaren Terminalzustand: `sidecar_committed`, `fallback_committed`, `cancelled` oder `failed_no_activation`. Die erste erfolgreiche Compare-and-Set-Finalisierung gewinnt; alle spaeteren Responses oder Retries muessen den vorhandenen Terminalzustand lesen und duerfen keine zweite Aktivierung ausloesen. |
| Konsistenzmodell | Worker schreibt Idempotency-Terminalzustand, `OptimizationRun` und optional erzeugte Schedule-Version in einer lokalen DB-Transaktion oder in einer dokumentierten Outbox-Sequenz mit eindeutigem Reconciliation-Test. Der Sidecar-Response allein aktiviert nie einen Plan; Aktivierung passiert nur durch den Worker nach erfolgreichem CAS im Store. |
| Timeout danach Antwort | Wenn ein spaeter Sidecar-Response fuer eine bereits als Timeout behandelte und per `fallback_committed` finalisierte `request_id` ankommt, darf er keinen neuen Plan aktivieren. Er darf nur als `late_response_ignored` observiert werden. |
| Replay | Replay-Datensaetze speichern `request_id` oder eine deterministische Ableitungsregel, damit Retry-/Duplicate-Faelle reproduzierbar sind. |
| Observability | Logs, `OptimizationRun`, Metriken und Container-Orchestrierungstests enthalten `request_id`, `run_id`, Terminalzustand und `fallback_reason`. |

---

## Fallback-Taxonomie

Alle Fallback-Entscheidungen verwenden zwei kanonische Labels:
`fallback_source` beschreibt den genutzten Ausfuehrungspfad,
`fallback_reason` beschreibt den ausloesenden Grund. Zusammengesetzte
Labels wie `last_valid_or_safe_stop` oder `local_optimizer_or_safe_stop`
duerfen nicht als Metrik-, Replay- oder Run-Werte verwendet werden.

| Feld | Erlaubte Werte |
| ---- | -------------- |
| `fallback_source` | `none`, `sidecar_result`, `local_optimizer`, `last_valid_schedule`, `safe_stop`, `no_activation` |
| `fallback_reason` | `none`, `deadline_exceeded`, `sidecar_unavailable`, `transport_cancelled`, `transport_internal_error`, `invalid_request`, `solver_infeasible`, `solver_unbounded`, `solver_time_limit`, `solver_iteration_limit`, `no_valid_plan`, `fallback_plan_expired`, `fallback_context_mismatch`, `fallback_telemetry_drift`, `invalid_snapshot`, `invalid_mpc_state`, `contract_incompatible`, `unauthorized_client`, `duplicate_request`, `late_response_ignored` |

Wenn eine Entscheidung fachlich zwischen altem Fahrplan und Safe-Stop
waehlt, wird zuerst die Plan-Gueltigkeit geprueft. Danach ist der Wert
eindeutig: `fallback_source=last_valid_schedule` bei frischem,
kontextkompatiblem Plan oder `fallback_source=safe_stop` mit
`fallback_reason=no_valid_plan` beziehungsweise konkretem
Invalidierungsgrund.

---

## Fallback-Plan-Gueltigkeit

Ein "letzter gueltiger Fahrplan" ist fuer M5 nur dann als Fallback
verwendbar, wenn er frisch und kontextkompatibel ist. Sonst gilt er als
`no_valid_plan` und der Control-Pfad geht in Safe-Stop.

| Kriterium | Verbindliche Regel |
| --------- | ------------------ |
| Zeitindex | Der aktuelle Tick muss innerhalb des halboffenen Schedule-Horizonts liegen; ausserhalb des Horizon ist der Plan ungueltig. |
| Maximales Alter | `MaxFallbackScheduleAge` ist eine UTC-Dauer in Sekunden beziehungsweise eine .NET-`TimeSpan`-Konfiguration. Gemessen wird `current_tick_utc - schedule_created_at_utc`; wenn kein Schedule-Erstellzeitpunkt existiert, gilt `OptimizationRun.CreatedAt` der erzeugenden Version. Zusaetzlich muss der aktuelle Tick im Schedule-Gueltigkeitsfenster liegen. Der M5-Default fuer den ersten Sidecar-Slice ist `min(Schedule.TimeStep, 2 * ControlCycleInterval)` pro Asset/Schedule-Type; fehlt eine dieser Groessen, ist `MaxFallbackScheduleAge` explizit zu konfigurieren. |
| Kontext-Stempel | Fallback-Entscheidungen vergleichen `asset_id`, Asset-Set, Schedule-Type, Horizon-Start/-Ende, Time-Step, Constraint-/Limit-Version und Markt-/Reserve-Kontext. Jede Abweichung invalidiert den Plan. |
| Telemetrie-Drift | SOC, verfuegbare Lade-/Entladeleistung, Netzlimit, Temperatur-/Availability-Status oder Reserve-Kontext duerfen nicht ausserhalb der Toleranzen liegen, mit denen der Plan erzeugt wurde. Drift ausserhalb der Toleranz invalidiert den Plan hart. |
| Versionierung | Ein neuerer erfolgreicher Plan fuer dasselbe Asset und denselben Schedule-Type ersetzt aeltere Fallback-Kandidaten. Versionskonflikt oder unbekannte Base-Version ist `no_valid_plan`. |
| Observability | Jede Invalidation traegt einen maschinenlesbaren Reason, mindestens `fallback_plan_expired`, `fallback_context_mismatch`, `fallback_telemetry_drift` oder `no_valid_plan`. |

RM-M5-01 muss diese Checks vor jeder Wiederverwendung eines alten Plans
testen. Die Checks duerfen nicht erst in einem spaeteren MPC-Slice
nachgereicht werden.

---

## Fallback-Matrix

RM-M5-01 muss diese Matrix als Default-Vertrag implementieren oder vor
Code-Aktivierung per Plan-/ADR-Update aendern. Ein Fallback darf niemals
ein stale Sidecar-Ergebnis wiederverwenden. Wenn kein lokaler Optimierer
und kein nach `Fallback-Plan-Gueltigkeit` gueltiger letzter Fahrplan oder
Setpoint existiert, ist
`no_valid_plan` immer ein expliziter Safe-Stop-Pfad mit Log/Metrik/Event,
nicht ein stiller Optimierungs-Fallback.

| Fehlerklasse | Verbindlicher Fallback | Qualitaetsdegradation | Mindestnachweis |
| ------------ | ---------------------- | -------------------- | --------------- |
| Sidecar gesund, Solverstatus usable | Sidecar-Ergebnis wird ueber bestehende Ports uebernommen und persistiert. | Keine. | Contract-Test fuer erfolgreichen Lauf. |
| Timeout/Deadline oder Unavailable vor Ergebnis | Horizon-Optimierung nutzt den konfigurierten lokalen `IScheduleOptimizer`, falls vorhanden; sonst wird keine neue Schedule-Version erzeugt. Der Regelkreis nutzt nur einen frischen, kontextkompatiblen Fahrplan; fehlt dieser, erzeugt der Control-Pfad Safe-Stop mit `fallback_reason=no_valid_plan`. | Optimierungsqualitaet kann auf lokalen LP-/NoOp-Pfad fallen; bei fehlendem oder invalidiertem Plan geht Verfuegbarkeit in Safe-Stop statt Optimierungsbetrieb. | Timeout-/Unavailable-Test mit `Failed`-Run, `fallback_reason`, abgelaufenem Plan, Kontext-Mismatch und `no_valid_plan`-Startfall. |
| Sidecar-Crash waehrend Lauf oder Transportabbruch nach Request-Start | Lauf endet als fehlgeschlagener `OptimizationRun`; Worker bleibt aktiv. Fuer den Regelkreis gilt nur ein frischer, kontextkompatibler Fahrplan plus bestehende Limiter; ohne gueltigen Fahrplan gilt Safe-Stop mit `fallback_reason=no_valid_plan`. | Keine neue Optimierung fuer diesen Lauf; Dispatch folgt bestehendem Fahrplan oder Safe-Stop. | Container-Crash-Test mit weiterlaufendem Worker, abgelaufenem Plan und Startfall ohne Fahrplan. |
| Sidecar liefert Infeasible/Failed ohne nutzbare Loesung | Keine neue Schedule-Version; ein frischer, kontextkompatibler Fahrplan bleibt aktiv. Ohne gueltigen Fahrplan erzeugt der Control-Pfad Safe-Stop mit `fallback_reason=no_valid_plan`. | Keine Reoptimierung; Ursache bleibt in Run/Metric sichtbar; bei Initialzustand kein Optimierungsbetrieb. | Solverstatus-Mapping-Test fuer nicht nutzbare Stati inklusive leerem Schedule-Store und Plan-Invalidation. |
| Sidecar liefert nicht-finite, schema-ungueltige oder constraint-verletzende Trajektorie | Ergebnis wird verworfen; keine neue Schedule-Version; Dispatch-/Control-Pfad verwendet bestehenden Managed-Limiter mit frischem, kontextkompatiblem Fahrplan oder Safe-Stop mit `fallback_reason=no_valid_plan`, wenn kein gueltiger Setpoint existiert. | Sidecar-Ergebnis unbrauchbar; Safety hat Vorrang vor Optimierungsziel. | Negativtest fuer nicht-finite Werte, Constraint-Verletzung, Telemetrie-Drift und fehlenden gueltigen Setpoint. |
| Ungueltiger Snapshot, stale Telemetrie oder invalider MPC-State vor Sidecar-Aufruf | Kein Sidecar-Aufruf und kein Optimierer-Fallback mit denselben ungueltigen Eingaben; bestehender Control-Precheck erzeugt Safe-Stop/invaliden Snapshot-Pfad. | Optimierung/Dispatch wird abgebrochen, bis valide Eingaben vorliegen. | Precheck-Test mit Nachweis, dass kein Sidecar-Request gesendet wird. |

---

## Sidecar-Status-Taxonomie

RM-M5-01 muss den transportneutralen Sidecar-Status vor produktivem Code
als versionierten Contract festlegen. Der gewaehlte Transport mappt seine
konkreten Statuscodes, zum Beispiel gRPC-Codes oder HTTP-Status, zuerst
auf diese normierten Transport-Outcomes. Jede Sidecar-Antwort enthaelt
zusaetzlich `has_usable_solution` und `solution_quality` (`optimal`,
`feasible`, `none`). Die .NET-Seite darf nur diese Tabelle in
`OptimizationRun.Status`, `TerminationReason` und Metrik-Tags mappen.
Das konkrete Transport-Mapping ist ein eigenes versioniertes Artefakt und
ist vor RM-M5-01-Freeze Pflicht.

| Normierter Transportstatus | Solverstatus aus Sidecar | Usable-Signal | M2 `OptimizationSolverStatus` | Kanonische Tags | Fallback |
| -------------------------- | ------------------------ | ------------- | ----------------------------- | --------------- | -------- |
| `success` | `optimal` | `has_usable_solution=true`, `solution_quality=optimal` | `Optimal` | `fallback_source=sidecar_result`, `fallback_reason=none` | Ergebnis uebernehmen. |
| `success` | `feasible` | `has_usable_solution=true`, `solution_quality=feasible` | `Feasible` | `fallback_source=sidecar_result`, `fallback_reason=none` | Ergebnis uebernehmen, Qualitaetsinfo persistieren. |
| `success` | `infeasible` | `has_usable_solution=false`, `solution_quality=none` | `Infeasible` | `fallback_source` aus Fallback-Matrix, `fallback_reason=solver_infeasible` | Keine neue Version; Fallback-Matrix. |
| `success` | `unbounded` | `has_usable_solution=false`, `solution_quality=none` | `Unbounded` | `fallback_source` aus Fallback-Matrix, `fallback_reason=solver_unbounded` | Keine neue Version; Fallback-Matrix. |
| `success` | `time_limit` | `has_usable_solution=true`, `solution_quality=feasible` | `Feasible` | `fallback_source=sidecar_result`, `fallback_reason=none`, `termination=time_limit_with_feasible_solution` | Ergebnis uebernehmen; Resource-Limit bleibt in `TerminationReason`/Warnings sichtbar. |
| `success` | `time_limit` | `has_usable_solution=false`, `solution_quality=none` | `TimeLimit` | `fallback_source` aus Fallback-Matrix, `fallback_reason=solver_time_limit` | Keine neue Version; Fallback-Matrix. |
| `success` | `iteration_limit` | `has_usable_solution=true`, `solution_quality=feasible` | `Feasible` | `fallback_source=sidecar_result`, `fallback_reason=none`, `termination=iteration_limit_with_feasible_solution` | Ergebnis uebernehmen; Resource-Limit bleibt in `TerminationReason`/Warnings sichtbar. |
| `success` | `iteration_limit` | `has_usable_solution=false`, `solution_quality=none` | `IterationLimit` | `fallback_source` aus Fallback-Matrix, `fallback_reason=solver_iteration_limit` | Keine neue Version; Fallback-Matrix. |
| `deadline_exceeded` | kein Ergebnis | `has_usable_solution=false`, `solution_quality=none` | `TimeLimit` | `fallback_source` aus Fallback-Matrix, `fallback_reason=deadline_exceeded` | Fallback-Matrix Timeout/Deadline. |
| `unavailable` | kein Ergebnis | `has_usable_solution=false`, `solution_quality=none` | `Failed` | `fallback_source` aus Fallback-Matrix, `fallback_reason=sidecar_unavailable` | Fallback-Matrix Unavailable. |
| `cancelled` durch Caller | kein Ergebnis | `has_usable_solution=false`, `solution_quality=none` | `Failed` | `fallback_source` aus Fallback-Matrix, `fallback_reason=transport_cancelled` | Kein Retry; danach derselbe frische-Plan-oder-Safe-Stop-Fallback wie bei Transportabbruch. |
| `invalid_request` / Schemafehler | kein Ergebnis | `has_usable_solution=false`, `solution_quality=none` | `Failed` | `fallback_source=no_activation` oder `safe_stop`, `fallback_reason=invalid_request` | Ergebnis verwerfen; kein lokaler Optimierer mit denselben ungueltigen Eingaben. |
| `internal_error` / Crash / Decode-Fehler / unbekannter Status | kein Ergebnis | `has_usable_solution=false`, `solution_quality=none` | `Failed` | `fallback_source` aus Fallback-Matrix, `fallback_reason=transport_internal_error` | Fallback-Matrix Crash/Transportabbruch. |

Die Metrik-Statuslabels bleiben snake_case-kompatibel zu M2
(`optimal`, `feasible`, `infeasible`, `unbounded`, `time_limit`,
`iteration_limit`, `failed`). Neue Transport- oder Fallback-Labels duerfen
keine neuen `OptimizationSolverStatus`-Werte erfinden und muessen aus der
Fallback-Taxonomie stammen; Details gehoeren in `TerminationReason`,
Run-Warnings und Metric-Tags. Ein `ProducedSchedule` darf im heutigen
M2-Modell nur bei `Optimal` oder `Feasible` persistiert werden;
resource-limit Faelle mit nutzbarer Loesung muessen deshalb als `Feasible`
mit passendem Termination-Code gemappt werden.

---

## Contract-Versionen Und Rollout

Rolling Deployments duerfen keine implizite Contract-Migration ausloesen.
Vor jedem Sidecar-Request prueft der Worker den Version-/Feature-Handshake.

| Thema | Regel |
| ----- | ----- |
| Kompatibilitaetsfenster | Sidecar und Worker nennen `contract_version`, `min_compatible_version` und `max_compatible_version`. Aktivierung ist nur erlaubt, wenn beide Versionen in der gegenseitigen Range liegen. |
| Feature-Signale | Optionale Contract-Features wie `has_usable_solution`, Idempotency-Terminalzustaende, deterministische Seeds oder Security-Modi werden als explizite Feature-Flags gemeldet. Fehlt ein fuer den Request benoetigtes Feature, wird kein Sidecar-Request gesendet. |
| Inkompatibilitaet | Inkompatible Version, fehlendes Pflichtfeature oder unbekannte Major-Version fuehrt vor Sidecar-Aufruf zu `failed_no_activation` plus lokalem Fallback/Safe-Stop gemaess Fallback-Matrix. |
| Tests | RM-M5-01 braucht Mixed-Version-Tests: Worker alt/Sidecar neu, Worker neu/Sidecar alt, unbekannte Major-Version und fehlendes Feature-Flag. |

---

## Replay-Kompatibilitaet

RM-M5-04 fuehrt ein Manifest-/Golden-Diff-Format ein, darf bestehende
M2/M3-Replay-Pipelines aber nicht still brechen.

| Thema | Vorgabe |
| ----- | ------- |
| Formatversion | Neue Datensaetze starten mit Manifest `replay-manifest.v1`; jedes Fixture nennt Schema-Version, Engine-Ziele, Toleranzen, Zeitbasis, deterministische `request_id`-Ableitung und Golden-Artefakte. |
| Schema-Validation | Replay-Loader validieren Manifest und Fixture vor Ausfuehrung strikt gegen versionierte Schemas. Unbekannte `schema_version`, unbekannte Pflichtfelder, unbekannte Top-Level-Felder oder nicht explizit erlaubte Legacy-Versionen werden reject-by-default mit maschinenlesbarem Fehler pro Fixture abgelehnt. |
| Feld-Lebenszyklus | Jedes Manifest-Feld ist im Schema als `required`, `optional`, `deprecated` oder `tolerated_legacy` klassifiziert. `required` fehlt -> Fehler; unbekannte Felder -> Fehler; `optional` braucht Default und Drift-Wirkung; `deprecated` erzeugt Warnung mit Entferndatum; `tolerated_legacy` ist nur fuer benannte Altformate und nur im Kompatibilitaets-Loader erlaubt. |
| Determinismus | Jedes Manifest dokumentiert `seed`, `solver_deterministic_mode`, Solver-/Runtime-/Numerik-Versionen, Time-Step, Rundungsmodus und aktivierte Solver-Optionen. Zufalls- oder Rauschmodelle duerfen nur ueber Manifest-Seed laufen. |
| Bestehende M2-Fixtures | Der M2-Telemetrie-Replay-Shape bleibt ueber einen Kompatibilitaets-Loader lauffaehig. Migration darf in kleinen Folge-PRs erfolgen; der Kompatibilitaets-Loader darf erst entfernt werden, wenn ein migriertes Manifest dieselben fachlichen Faelle abdeckt und mindestens ein CI-Lauf beide Pfade verglichen hat. |
| Bestehende M3-Fixtures | Native-Parity-Cases bleiben als Referenzdatensatz erhalten; M5 darf sie referenzieren oder maschinell nach Manifest v1 spiegeln, aber nicht ohne Ersatz loeschen. |
| Migration | Falls ein Fixture-Format gebrochen wird, muss RM-M5-04 ein Migrationstool oder eine dokumentierte Fixture-Konvertierung mit Golden-Diff-Nachweis liefern. Die bestehende M2/M3-Fallliste ist dabei zu 100 % zu inventarisieren; jedes entfernte Fixture braucht ein gemapptes Manifest-v1-Aequivalent oder eine begruendete Planentscheidung. |
| Diff-Toleranz | Toleranzen stehen pro Fixture-Typ im Manifest. Commands, Safe-Stop-Reasons und Solverstatus muessen exakt matchen; numerische Leistungs-/SOC-Werte brauchen einheitenbezogene absolute und relative Toleranzen; Safety-Invarianten haben null Toleranz. |
| CI-Kompatibilitaet | Alte und neue Replay-Gates duerfen erst zusammengelegt werden, wenn 100 % der bestehenden M2/M3-Pflichtfaelle in beiden Pfaden laufen, der Golden-Diff fuer alle Pflichtfaelle innerhalb ihrer Manifest-Toleranzen liegt und mindestens ein ueberlappender CI-Lauf diese Matrix nachweist. |

---

## Komponenten

| Bereich | Artefakt | LH-Bezug |
| ------- | -------- | -------- |
| Contract | `optimization-core` Sidecar-Vertrag fuer Optimize, MPC, Health, Version, Cancellation und Request-Idempotenz | LH-OPT-006 |
| Adapter | .NET Driven Adapter fuer Sidecar-Aufrufe hinter bestehenden Optimierungsports | LH-OPT-002/006 |
| Native/Sidecar | `optimization_core` Service mit Solver-/MPC-Backend-Wahl | LH-OPT-002/003/006 |
| Native/Kernel | `state_space_core` oder Sidecar-interner MPC-Kernel mit State-Space/Kalman/Horizon | LH-CTRL-005/006 |
| Native optional | Hochfrequente Telemetrie-Filterung im nativen Pfad | LH-NATIVE-001 |
| Application | Fallback- und Deadline-Policy fuer Optimierungs- und Dispatch-nahe Sidecar-Aufrufe | LH-NF-002, LH-OPT-006 |
| Application | `IPriceSeriesSource`, `PriceSeries`/`PriceSeriesRequest` und quellenneutraler Import/API-Pfad | LH-OPEN-003, LH-MKT-008 |
| Replay | Datensatzmanifest, Fixture-Loader, Golden-/Diff-Format und Vergleichsrunner | LH-TEST-004 |
| Observability | Sidecar-/Solver-/MPC-Metriken und Command-Latenz | LH-MON-002 |
| Deploy/Test | Compose-/Container-Gate fuer Worker + Sidecar | LH-TEST-007 |

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M5-01 | Sidecar `optimization-core` (LP/MILP/MPC) | Slice-Plan: [`done/plan-RM-M5-01.md`](../done/plan-RM-M5-01.md). `proto/optimization-core/v1/optimization_core.proto` mit Service `OptimizationCore` (Health, Version, Optimize-streaming, OptimizeMpc-Vertrag, Cancel), Major-Version im Paket-Namespace. `BatteryEms.Adapters.OptimizationCore`-Adapter implementiert M2-`IScheduleOptimizer` (D-01) über `Grpc.Net.Client.GrpcChannel`, UDS-Default für Loopback / mTLS für Cross-Host gemäß ADR 0005. `OptimizationCoreOptions.EnsureValid` wirft `optimization-core-not-hardened-in-production` bei Production+plaintext (D-02 analog M4-05) plus `optimization-core-uds-permissions-not-locked` bei Mode≠0600. Persistenter `optimization_idempotency`-Store mit Unique-Constraint auf `request_id` (D-03); atomarer Terminalzustand via CAS; Late-Response-Pfad `late_response_ignored`. Sidecar-Status-Taxonomie-Mapping in `transport-mapping-v1.md` (D-04 versioniertes Artefakt) und `OptimizationCoreStatusMapper`-static. Fallback-Matrix mit lokalem OR-Tools-Fallback (via `IFallbackScheduleOptimizer`-Driven-Port + `BessHostOptions.OptimizationCoreFallbackBackend`) oder Safe-Stop (`no_valid_plan`) gemäß plan-RM-M5 §Fallback-Matrix; Plan-Validator-Aufruf gegen den Fallback-Output (Kontext-Stempel / Horizon-Alignment / MaxFallbackScheduleAge; Telemetrie-Drift skip'd da Adapter keinen Snapshot im Scope hat). 25 Pins (5 happy + 4 negativ + 4 mixed-version + 4 security + 3 adapter-side idempotency + 5 local-fallback) in `tests/integration/BatteryEms.OptimizationCore.IntegrationTests/` via In-Process `EmbeddedOptimizationCoreSidecar`-Fixture (`Grpc.AspNetCore` + UDS) plus 13 Persistence-Pins in `BatteryEms.Persistence.IntegrationTests` für den Dapper-backed Idempotency-Store. `make test-hil-optimization-core` jetzt Mandatory in `make gates` und `make ci`. RM-M5-02 (MPC-Kernel), RM-M5-04 (Replay-Plattform), RM-M5-05 (Erweiterte Metriken), RM-M5-06 (Container-Orchestrierungs-Gate) bleiben eigene Slices. F-Items für Cert-Rotation, Multi-Tenant-Bearer-Token-Auth, produktionsnaher Drittsprach-Sidecar, Schedule-Stempel-Erweiterung, „Letzter-bekannter-Plan"-Fallback siehe [`../open/note-RM-M5-followups.md`](../open/note-RM-M5-followups.md). |
| ✅ | RM-M5-02 | MPC-Kernel (State-Space, Kalman, Vorhersagehorizont) | Slice-Plan: [`done/plan-RM-M5-02.md`](../done/plan-RM-M5-02.md). `BatteryEms.Application.Mpc` mit State-Space-Domain-Typen, Orchestrator-Port, Constraint-Validator, Kalman-Estimator und Local-OSQP-Adapter (`BessHostOptions.MpcBackend=local_osqp`). Default-Boot ohne Backend registriert keinen `IMpcDispatchOptimizer`; reservierte Backends `optimization_core`/`bi_modal` bleiben F-M5-12 und werfen `mpc-backend-not-implemented`. `MpcRunIdentity` bildet das 8-Achsen-Replay-Tuple und Stamps (`solver_config_hash`, `estimator_config_hash`, `numerik_stamp_json`, `p0_frobenius_display`); `mpc_runs` Migration + Dapper/InMemory-Repositories liefern Replay-/Retention-Hooks. Worker ruft MPC optional pro Tick/Asset; Production verlangt lokalen Fallback-Port und `MonotonicAnchoredClock`. Pins decken Constraints, Kalman-Robustheit, OSQP-Status/Determinismus, Identity-Achsen, Cross-Run-Determinismus, Fallback-Stamps, Worker-Wiring und Composition-Gates ab. |
| ⬜ | RM-M5-03 | Hochfrequente Telemetrie-Filterung im Native Core (optional) | Aktivierung nur bei konkretem Bedarf aus RM-M5-02; Filtervertrag dokumentiert Samplingrate, Einheiten und Fehlerverhalten; .NET-Prechecks bleiben erhalten; Replay-/Numeriktests decken Drift und ungueltige Eingaben ab; invalider Filter-/MPC-State folgt der Fallback-Matrix. |
| ✅ | RM-M5-04 | Replay-Plattform mit Datensatz-Verwaltung und Sollwertvergleich | Slice-Plan: [`done/plan-RM-M5-04.md`](../done/plan-RM-M5-04.md). Sub-Slice A: Versioniertes Manifest-v1, externer Telemetrie-Fixture-Loader, Golden-Command-Loader und Golden-Diff-Grundlage sind vorhanden; Loader validieren reject-by-default gegen bekannte Schema-Versionen und unbekannte Felder, Manifest-Schema klassifiziert Felder als required/optional/deprecated/tolerated_legacy, Manifest enthaelt Seed, Determinismusmodus, Runtime-/Numerik-Versionen, Solver-Option, `request_id`-Regel und Toleranzen; `make test-replay` ist eigenes Gate und in `make gates` / `make ci` verdrahtet. Sub-Slice B: alle vier M2-Pflichtfaelle haben Manifest-v1-Fixture/Golden und laufen parallel zum alten Harness; M3 Native-Parity `cases.v1.json` ist per `native-control-parity`-Manifest referenziert. Sub-Slice C: `NativeParityEngineComparisonRunner` treibt Managed-vs-Native aus dem Manifest und liefert Diff-Klassen `numeric_tolerance`/`business_drift`. Sub-Slice D: `replay-diff-report.v1` serialisiert M2- und M3-Diffs in Assertion-Output und optional unter `BESS_REPLAY_REPORT_DIR`. Sub-Slice E: `local-mpc-engine-comparison` und `optimization-core-sidecar-comparison` pinnen MPC-Orchestrator- und Sidecar-Engine-Vergleiche ueber `make test-mpc-property` und `make test-hil-optimization-core`. |
| ✅ | RM-M5-05 | Erweiterte Metriken / Solverstatus / Command-Latenz | Slice-Plan: [`done/plan-RM-M5-05.md`](../done/plan-RM-M5-05.md). Bestehende `PrometheusOptimizationRunMetrics` decken Solverstatus und Laufzeit ab; `PrometheusControlCycleMetrics` deckt Command-Latenz ab. Neu: `IOptimizationCoreMetrics` + `PrometheusOptimizationCoreMetrics` fuer `bess_optimization_core_*` mit `fallback_source`, `fallback_reason`, `terminal_state`, Sidecar-Health und Run-Duration. Tests scrapen erfolgreiche und fehlerhafte Pfade; der In-Process-Test-Sidecar pinnt Metrics-Writes im echten Adapterfluss. |
| ✅ | RM-M5-06 | Container-Orchestrierungstests (Worker + Sidecar) | Slice-Plan: [`done/plan-RM-M5-06.md`](../done/plan-RM-M5-06.md). `make test-optimization-core-compose` baut `bess-ems-runtime` plus `optimization-core-test-sidecar`, startet Worker/API-Host und Sidecar im Compose-Netz, prueft `/health`, Sidecar-Commit, Sidecar-Stopp mit lokalem `or_tools`-Fallback, Restart-Recovery, Prometheus-Terminalzustandslabels und korrelierbare `run_id`/`request_id`-Logs. Das Gate ist in `make ci` verdrahtet. |
| ✅ | RM-M5-07 | Preisreihen-Port und quellenneutraler Import/API-Pfad | Slice-Plan: [`done/plan-RM-M5-07.md`](../done/plan-RM-M5-07.md). `IPriceSeriesSource` und `IPriceSeriesImportSink` als source-agnostic Application-Ports; normalisierte `PriceSeries` mit Marktgebiet, Produkt, Preisart, Einheit, Zeitraster und expliziter Quelle; `POST /markets/price-series/import` importiert provider-neutral; Day-Ahead- und Intraday-Optimierung koennen eine `price_series`-Referenz aufloesen und geben Werte plus Einheit an `ScheduleOptimizationCommand`/`IntradayReoptimizationCommand` weiter. Tests nutzen synthetische Daten. Externe Anbieteradapter bleiben out-of-scope bis Lizenz-/Nutzungs-, Auth-, Rate-Limit- und Caching-Pruefung dokumentiert sind. |

---

## Sequenz

1. ✅ `AR-OPEN-002` geschlossen via ADR 0005 (2026-05-11). gRPC ueber
   HTTP/2 ist der Sidecar-Transport, UDS-Default fuer Loopback, mTLS
   fuer Cross-Host, Mocking via In-Process Grpc.AspNetCore-TestSidecar.
2. Security-Freeze fuer RM-M5-01 abschliessen: AuthN/AuthZ,
   Transportverschluesselung oder geschuetzter lokaler Socket,
   Secret-Handling und Negativtests fuer unautorisierte Clients sind
   Go/No-Go-Kriterien, bevor ein produktionsnaher Sidecar-Pfad gemerged
   wird.
3. RM-M5-01 zuerst als schmalen Contract-Slice bauen: Health, Version,
   Test-Sidecar, Deadline, Idempotenz, Fallback und Status-Mapping.
4. RM-M5-05 parallel zum ersten Sidecar-Slice aktivieren, damit
   Sidecar-Fehler, Deadlines und Fallbacks nicht nachtraeglich
   observierbar gemacht werden muessen.
5. RM-M5-06 frueh als Container-Gate schneiden, sobald Worker und
   Test-Sidecar zusammenspielen.
6. RM-M5-07 parallel zur Optimierungs-/Replay-Basis schneiden, bevor
   MPC-Faelle echte Preisinputs brauchen. Der Slice liefert nur Port,
   Modell und Import/API-Default, keine externen Anbieteradapter.
7. RM-M5-04 danach ausbauen und bestehende M2/M3-Replay-Fixtures
   ueber Kompatibilitaets-Loader lauffaehig halten. Migrationen duerfen
   in kleinen Folge-PRs laufen; der alte Pfad wird erst entfernt, wenn
   Golden-Diff-Nachweise und ein ueberlappender CI-Lauf vorliegen. Neue
   MPC-Faelle bekommen eigene Manifestversion.
8. RM-M5-02 erst auf stabilem Contract-/Replay-Fundament aktivieren,
   damit numerische Drift und Constraint-Verletzungen reproduzierbar
   diskutiert werden koennen.
9. RM-M5-03 nur ziehen, wenn RM-M5-02 eine echte hochfrequente
   Filteranforderung ausweist.

---

## Akzeptanzkriterien

- MPC-Laeufe erzeugen zulaessige Trajektorien, die SOC-, Leistungs-,
  Ramp-, Geraete- und Netzgrenzen nicht verletzen.
- Sidecar-Crash, Timeout oder Unavailable beeintraechtigt den Regelkreis
  nicht; die Fallback-Matrix ist implementiert, getestet und
  observierbar. Wenn kein gueltiger Fahrplan oder Setpoint existiert,
  ist `no_valid_plan` ein expliziter Safe-Stop, kein stiller Fallback.
- Alte Fahrplaene duerfen nur als Fallback dienen, wenn Zeitindex,
  Maximalalter, Version, Asset-/Constraint-Kontext und Telemetrie-Drift
  die Plan-Gueltigkeitsregeln erfuellen.
- Optimierer bleiben ueber bestehende Ports austauschbar; Application und
  Domain referenzieren keinen konkreten Solver und keinen transport- oder
  solver-spezifischen Client.
- Solverstatus, RunId, Horizon, Objective-/Qualitaetsinformationen und
  Fehlergrund bleiben mit dem M2-Optimierungsmodell kompatibel;
  Transport- und Sidecar-Status werden ausschliesslich ueber die
  Sidecar-Status-Taxonomie gemappt.
- Wiederholte Sidecar-Calls sind ueber `request_id` idempotent; Duplicate-
  oder Late-Responses duerfen keinen zweiten Run und keine zweite
  Schedule-Version erzeugen.
- Replay-Datensaetze sind versioniert, reproduzierbar und koennen
  Sollwerte/Commands zwischen Managed-, Native- und Sidecar-Pfaden
  vergleichen; bestehende M2/M3-Pflichtfaelle bleiben bis zu einer
  vollstaendigen, toleranzgeprueften Migration lauffaehig. MPC-/Replay-
  Fixtures dokumentieren Seed, Determinismusmodus, Runtime-/Numerik-
  Versionen und Solver-Optionen.
- Container-Gates pruefen Worker + Sidecar inklusive Health,
  Orchestrierung, Crash/Restart und Fallback.
- Metriken fuer Solverstatus, Laufzeit, Deadline/Timeout,
  `fallback_source`, `fallback_reason`, Terminalzustand, Sidecar-Health
  und Command-Latenz sind getestet.
- Preisreihen koennen ueber einen quellenneutralen Import/API-Pfad in
  Optimierungsrequests eingehen. Der Default enthaelt keine API-Keys,
  keine unklar lizenzierten Marktdaten und keine Scraper gegen
  Marktportale.
- Roadmap, Quality-Doku und Architektur werden beim Abschluss
  synchronisiert.

---

## Risiken und Entscheidungen

- **Transportvertrag vs. Architektur-Drift.** ✅ Geloest durch ADR 0005
  (2026-05-11): gRPC ueber HTTP/2 ist der adoptierte Transport, UDS fuer
  Loopback / mTLS fuer Cross-Host. `spec/architecture.md` §18
  AR-OPEN-002 traegt die Closure-Zeile. Der Plan bleibt **trotzdem**
  transportneutral in den normierten Status-Bezeichnern
  (`success` / `deadline_exceeded` / `unavailable` etc.); das konkrete
  Transport-Mapping ist ein eigenes versioniertes Artefakt aus
  RM-M5-01-A. Phase-4-Pivot-Trigger (z.B. harter Realtime-Bound) sind in
  ADR 0005 §7 benannt.
- **Fallback-Semantik.** Ein Sidecar-Fallback kann Optimierungsqualitaet
  verlieren. Die Fallback-Matrix ist der verbindliche Default; jede
  Abweichung braucht Plan-/ADR-Update, weil NoOp, bestehender LP-Adapter,
  letzter gueltiger Fahrplan und Safe-Stop fachlich unterschiedliche
  Antworten sind.
- **Numerische Drift.** MPC, Kalman und native Solver koennen kleine
  Rundungsabweichungen erzeugen. Replay-Toleranzen muessen eng,
  einheitenbezogen und fachlich begruendet sein; Safety-Invarianten haben
  keine Toleranzverletzung.
- **Nichtdeterministische Solver-/MPC-Laeufe.** Rauschen, Random Seeds,
  Solver-Heuristiken oder unterschiedliche numerische Runtime-Versionen
  koennen Golden-Diffs instabil machen. RM-M5-02/RM-M5-04 muessen
  Determinismus explizit konfigurieren und im Manifest dokumentieren.
- **Stateful MPC.** Ein MPC-Kern mit internem Zustand ist ein anderer
  Vertrag als die M3-Value-Step-Funktionen. Reset, Snapshot-Isolation,
  Asset-Bezug und Replay-Reproduzierbarkeit muessen getestet werden.
- **Container-Komplexitaet.** Aus einem Worker-Container wird eine
  Orchestrierung mit Sidecar. Health, Startreihenfolge, Netzwerk,
  Timeouts und Logs muessen Teil des Gates sein, nicht nur Deployment-Doku.
- **Replay-Plattform-Scope.** M2 hat bewusst keinen Operator-Replay-CLI,
  JSON-Fixture-Loader oder Multi-Asset-Replay gebaut. M5 darf diese
  Luecken schliessen, muss dabei aber die M2/M3-Kompatibilitaetsstrategie
  einhalten und sollte UI- und Flottenfunktionen nicht vorziehen.
- **Marktpreisquellen und Open Source.** Konkrete Anbieteradapter koennen
  Lizenz-, Auth-, Rate-Limit- und Caching-Pflichten in das Projekt ziehen.
  RM-M5-07 liefert deshalb nur den source-agnostic Port und Import/API-
  Default. Jeder externe Anbieter braucht eine eigene dokumentierte
  Quellenentscheidung.
