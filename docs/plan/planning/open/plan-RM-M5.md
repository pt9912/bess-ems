# Plan RM-M5 MPC, Solver-Sidecar, Replay-Plattform

**Dokumenttyp:** Detailplan / M5 (offen)
**Status:** Offen - abgeleitet aus Roadmap-Milestone M5, noch nicht
aktiviert.
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md) (M5),
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
LH-MON-002, LH-TEST-007)

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
  Deadline/Timeout, Fallback-Reason, `safe_stop`, `no_valid_plan` und
  Command-Latenz.
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
- Harte Echtzeitgarantien. M5 muss Deadlines und Fallback testen, aber
  keine zertifizierte Echtzeitplattform behaupten.

---

## Aktivierungsbedingungen

M5 kann starten, wenn M4 geschlossen ist oder bewusst entschieden wurde,
dass der erste Sidecar-/Replay-Slice fachlich unabhaengig von offenen
M4-Teilen ist. Vor dem ersten produktionsnahen Sidecar-PR muessen die
M2/M3-Basisgates auf `main` gruen sein und ADR 0004 fuer den konkreten
M5-Sidecar-Schnitt entweder bestaetigt oder ergaenzt werden. Das
Architektur-Open-Item `AR-OPEN-002` ist fuer M5 blockierend: Ein
konkreter Transport darf erst implementiert werden, wenn ADR 0004 oder
`spec/architecture.md` die finale Transportentscheidung fuer externe
Optimierungs-Sidecars festhaelt.

| Check | Erwartung |
| ----- | --------- |
| M2-Optimierung | `IScheduleOptimizer`, `OptimizationRun`, Solverstatus und Persistenz sind stabil; Sidecar-Integration ersetzt keinen Domain-Vertrag. |
| M3-Native | In-Process Native bleibt fuer kleine Control-Kerne verfuegbar; M5 fuehrt Sidecar nur fuer MPC/Solver-grosse Kerne ein. |
| M4-Schnitt | Falls Regelleistungs-Reserve-Constraints Teil des MPC-/MILP-Modells werden, muss das M4-Reservemodell abgeschlossen oder als explizite Planannahme dokumentiert sein. |
| ADR-Status | ADR 0004 wird fuer Sidecar-Transport, Supervisor-/Health-Verhalten, Fallback-Policy und Container-Topologie revalidiert. |
| Transportentscheidung | `AR-OPEN-002` ist geschlossen; ADR 0004 oder Architektur §13 nennt den finalen Transport (`gRPC` oder Alternative), Security-/Mocking-/CI-Konsequenzen und den Link zur Entscheidung. |
| Replay-Baseline | Bestehende M2/M3-Replay-Datensaetze sind referenzierbar; neue Datensaetze bekommen Manifest, Version und Vergleichsregeln. |
| Toolchain | Transport-Codegen falls noetig, Sidecar-Build und Container-Build sind reproduzierbar in Docker/CI. |
| Fallback | Fuer jeden Sidecar-Aufruf ist vor Aktivierung dokumentiert, welcher lokale Fallback benutzt wird und welche Semantik dadurch verloren geht. |

---

## Zielbild Und Abnahmeschnitt

| Situation | Erwartetes Verhalten | Mindestnachweis |
| --------- | -------------------- | --------------- |
| Sidecar gesund | Worker ruft `optimization-core` ueber den konfigurierten Adapter auf; RunId, Solverstatus, Horizon und erzeugter Fahrplan werden wie im bestehenden Optimierungsmodell persistiert. | Contract-/Integrationstest mit Test-Sidecar und persistiertem `OptimizationRun`. |
| Sidecar nicht erreichbar | Regelkreis und Worker bleiben lauffaehig; Optimierungsaufruf liefert kontrollierten Fehler/Fallback statt Crash oder haengendem Tick. | Timeout-/Unavailable-Test mit Fallback-Reason in Log/Metrik. |
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
| Retry-Grenze | Automatische Retries sind nur fuer Transportfehler erlaubt, bei denen keine Sidecar-Annahme nachweisbar ist. Sie verwenden dieselbe `request_id`; neue `request_id` bedeutet neuer Lauf und braucht neuen Run-Kontext. |
| Timeout danach Antwort | Wenn ein spaeter Sidecar-Response fuer eine bereits als Timeout behandelte `request_id` ankommt, darf er keinen neuen Plan aktivieren. Er darf nur als late/duplicate observiert werden. |
| Replay | Replay-Datensaetze speichern `request_id` oder eine deterministische Ableitungsregel, damit Retry-/Duplicate-Faelle reproduzierbar sind. |
| Observability | Logs, `OptimizationRun`, Metriken und Container-Orchestrierungstests enthalten `request_id`, `run_id` und `fallback_reason`. |

---

## Fallback-Plan-Gueltigkeit

Ein "letzter gueltiger Fahrplan" ist fuer M5 nur dann als Fallback
verwendbar, wenn er frisch und kontextkompatibel ist. Sonst gilt er als
`no_valid_plan` und der Control-Pfad geht in Safe-Stop.

| Kriterium | Verbindliche Regel |
| --------- | ------------------ |
| Zeitindex | Der aktuelle Tick muss innerhalb des halboffenen Schedule-Horizonts liegen; ausserhalb des Horizon ist der Plan ungueltig. |
| Maximales Alter | Fallback darf nur auf eine Schedule-Version zugreifen, deren Gueltigkeitsfenster den aktuellen Tick abdeckt und deren Version nicht aelter als der konfigurierte `MaxFallbackScheduleAge` ist. Der M5-Default fuer den ersten Sidecar-Slice ist hoechstens ein Optimierungs-/Dispatch-Horizon; ein laengerer Wert braucht Plan-/ADR-Update. |
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
| Timeout/Deadline oder Unavailable vor Ergebnis | Horizon-Optimierung nutzt den konfigurierten lokalen `IScheduleOptimizer`, falls vorhanden; sonst wird keine neue Schedule-Version erzeugt. Der Regelkreis nutzt nur einen frischen, kontextkompatiblen Fahrplan; fehlt dieser, erzeugt der Control-Pfad Safe-Stop mit Reason `no_valid_plan`. | Optimierungsqualitaet kann auf lokalen LP-/NoOp-Pfad fallen; bei fehlendem oder invalidiertem Plan geht Verfuegbarkeit in Safe-Stop statt Optimierungsbetrieb. | Timeout-/Unavailable-Test mit `Failed`-Run, Fallback-Reason, abgelaufenem Plan, Kontext-Mismatch und `no_valid_plan`-Startfall. |
| Sidecar-Crash waehrend Lauf oder Transportabbruch nach Request-Start | Lauf endet als fehlgeschlagener `OptimizationRun`; Worker bleibt aktiv. Fuer den Regelkreis gilt nur ein frischer, kontextkompatibler Fahrplan plus bestehende Limiter; ohne gueltigen Fahrplan gilt Safe-Stop mit Reason `no_valid_plan`. | Keine neue Optimierung fuer diesen Lauf; Dispatch folgt bestehendem Fahrplan oder Safe-Stop. | Container-Crash-Test mit weiterlaufendem Worker, abgelaufenem Plan und Startfall ohne Fahrplan. |
| Sidecar liefert Infeasible/Failed ohne nutzbare Loesung | Keine neue Schedule-Version; ein frischer, kontextkompatibler Fahrplan bleibt aktiv. Ohne gueltigen Fahrplan erzeugt der Control-Pfad Safe-Stop mit Reason `no_valid_plan`. | Keine Reoptimierung; Ursache bleibt in Run/Metric sichtbar; bei Initialzustand kein Optimierungsbetrieb. | Solverstatus-Mapping-Test fuer nicht nutzbare Stati inklusive leerem Schedule-Store und Plan-Invalidation. |
| Sidecar liefert nicht-finite, schema-ungueltige oder constraint-verletzende Trajektorie | Ergebnis wird verworfen; keine neue Schedule-Version; Dispatch-/Control-Pfad verwendet bestehenden Managed-Limiter mit frischem, kontextkompatiblem Fahrplan oder Safe-Stop mit Reason `no_valid_plan`, wenn kein gueltiger Setpoint existiert. | Sidecar-Ergebnis unbrauchbar; Safety hat Vorrang vor Optimierungsziel. | Negativtest fuer nicht-finite Werte, Constraint-Verletzung, Telemetrie-Drift und fehlenden gueltigen Setpoint. |
| Ungueltiger Snapshot, stale Telemetrie oder invalider MPC-State vor Sidecar-Aufruf | Kein Sidecar-Aufruf und kein Optimierer-Fallback mit denselben ungueltigen Eingaben; bestehender Control-Precheck erzeugt Safe-Stop/invaliden Snapshot-Pfad. | Optimierung/Dispatch wird abgebrochen, bis valide Eingaben vorliegen. | Precheck-Test mit Nachweis, dass kein Sidecar-Request gesendet wird. |

---

## Sidecar-Status-Taxonomie

RM-M5-01 muss den transportneutralen Sidecar-Status vor produktivem Code
als versionierten Contract festlegen. Der gewaehlte Transport mappt seine
konkreten Statuscodes, zum Beispiel gRPC-Codes oder HTTP-Status, zuerst
auf diese normierten Transport-Outcomes. Die .NET-Seite darf nur diese
Tabelle in `OptimizationRun.Status`, `TerminationReason` und Metrik-Tags
mappen.

| Normierter Transportstatus | Solverstatus aus Sidecar | M2 `OptimizationSolverStatus` | Metric Tags / Reason | Fallback |
| -------------------------- | ------------------------ | ----------------------------- | -------------------- | -------- |
| `success` | `optimal` | `Optimal` | `status=optimal`, `transport=success`, `fallback=none` | Ergebnis uebernehmen. |
| `success` | `feasible` | `Feasible` | `status=feasible`, `transport=success`, `fallback=none` | Ergebnis uebernehmen, Qualitaetsinfo persistieren. |
| `success` | `infeasible` | `Infeasible` | `status=infeasible`, `transport=success`, `fallback=last_valid_or_safe_stop` | Keine neue Version; Fallback-Matrix. |
| `success` | `unbounded` | `Unbounded` | `status=unbounded`, `transport=success`, `fallback=last_valid_or_safe_stop` | Keine neue Version; Fallback-Matrix. |
| `success` | `time_limit` | `TimeLimit` | `status=time_limit`, `transport=success`, `fallback=last_valid_or_safe_stop` | Nur uebernehmen, wenn Sidecar zugleich nutzbare `feasible`-Loesung markiert; sonst Fallback-Matrix. |
| `success` | `iteration_limit` | `IterationLimit` | `status=iteration_limit`, `transport=success`, `fallback=last_valid_or_safe_stop` | Nur uebernehmen, wenn Sidecar zugleich nutzbare `feasible`-Loesung markiert; sonst Fallback-Matrix. |
| `deadline_exceeded` | kein Ergebnis | `TimeLimit` | `status=time_limit`, `transport=deadline_exceeded`, `fallback=local_optimizer_or_safe_stop` | Fallback-Matrix Timeout/Deadline. |
| `unavailable` | kein Ergebnis | `Failed` | `status=failed`, `transport=unavailable`, `fallback=local_optimizer_or_safe_stop` | Fallback-Matrix Unavailable. |
| `cancelled` durch Caller | kein Ergebnis | `Failed` | `status=failed`, `transport=cancelled`, `fallback=last_valid_or_safe_stop` | Kein Retry; danach derselbe frische-Plan-oder-Safe-Stop-Fallback wie bei Transportabbruch. |
| `invalid_request` / Schemafehler | kein Ergebnis | `Failed` | `status=failed`, `transport=invalid_request`, `fallback=safe_stop_if_no_valid_plan` | Ergebnis verwerfen; kein lokaler Optimierer mit denselben ungueltigen Eingaben. |
| `internal_error` / Crash / Decode-Fehler / unbekannter Status | kein Ergebnis | `Failed` | `status=failed`, `transport=internal_error`, `fallback=last_valid_or_safe_stop` | Fallback-Matrix Crash/Transportabbruch. |

Die Metrik-Statuslabels bleiben snake_case-kompatibel zu M2
(`optimal`, `feasible`, `infeasible`, `unbounded`, `time_limit`,
`iteration_limit`, `failed`). Neue Transport- oder Fallback-Labels duerfen
keine neuen `OptimizationSolverStatus`-Werte erfinden; Details gehoeren in
`TerminationReason`, Run-Warnings und Metric-Tags.

---

## Replay-Kompatibilitaet

RM-M5-04 fuehrt ein Manifest-/Golden-Diff-Format ein, darf bestehende
M2/M3-Replay-Pipelines aber nicht still brechen.

| Thema | Vorgabe |
| ----- | ------- |
| Formatversion | Neue Datensaetze starten mit Manifest `replay-manifest.v1`; jedes Fixture nennt Schema-Version, Engine-Ziele, Toleranzen, Zeitbasis, deterministische `request_id`-Ableitung und Golden-Artefakte. |
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
| Replay | Datensatzmanifest, Fixture-Loader, Golden-/Diff-Format und Vergleichsrunner | LH-TEST-004 |
| Observability | Sidecar-/Solver-/MPC-Metriken und Command-Latenz | LH-MON-002 |
| Deploy/Test | Compose-/Container-Gate fuer Worker + Sidecar | LH-TEST-007 |

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ⬜ | RM-M5-01 | Sidecar `optimization-core` (LP/MILP/MPC) | Transportentscheidung fuer `AR-OPEN-002` ist abgeschlossen; Sidecar-Vertrag ist versioniert; .NET-Adapter ruft Sidecar mit Deadline/Cancellation und `request_id` auf; Health/Version sind testbar; Sidecar-Status-Taxonomie mappt normierten Transportstatus + Solverstatus exakt auf M2-`OptimizationRun`, `TerminationReason` und Metric-Tags; Retry-/Duplicate-Faelle sind idempotent; alle Fehlerklassen aus der Fallback-Matrix und alle Plan-Gueltigkeits-Invalidierungen sind getestet. |
| ⬜ | RM-M5-02 | MPC-Kernel (State-Space, Kalman, Vorhersagehorizont) | Kernel berechnet finite Trajektorien aus State-Space-Modell und Horizon; Kalman-/Schaetzerpfad behandelt Rauschen, Missing Measurements und unplausible Werte; Tests beweisen SOC-, Leistungs-, Ramp- und Constraint-Einhaltung; Fixture-Laeufe sind ueber Seed, Solver-Optionen und Runtime-/Numerik-Version reproduzierbar. |
| ⬜ | RM-M5-03 | Hochfrequente Telemetrie-Filterung im Native Core (optional) | Aktivierung nur bei konkretem Bedarf aus RM-M5-02; Filtervertrag dokumentiert Samplingrate, Einheiten und Fehlerverhalten; .NET-Prechecks bleiben erhalten; Replay-/Numeriktests decken Drift und ungueltige Eingaben ab; invalider Filter-/MPC-State folgt der Fallback-Matrix. |
| ⬜ | RM-M5-04 | Replay-Plattform mit Datensatz-Verwaltung und Sollwertvergleich | Versioniertes Manifest fuer Datensaetze; Loader fuer externe JSON-Fixtures; bestehende M2/M3-Fixtures bleiben ueber Kompatibilitaets-Loader lauffaehig, bis 100 % der Pflichtfallliste in kleinen Folge-PRs mit Golden-Diff-Nachweis migriert sind; Manifest enthaelt Seed, Determinismusmodus, Runtime-/Numerik-Versionen, Solver-Optionen, `request_id`-Regel und Toleranzen; Runner vergleicht Commands/Sollwerte gegen Golden-Dateien und mehrere Engines; Diff-Report trennt erlaubte numerische Toleranz von fachlicher Drift. |
| ⬜ | RM-M5-05 | Erweiterte Metriken / Solverstatus / Command-Latenz | Prometheus-Metriken decken Solverstatus, Laufzeit, Deadline/Timeout, Fallback-Reason, `safe_stop`, `no_valid_plan`, Sidecar-Health und Command-Latenz ab; Tests scrapen erfolgreiche und fehlerhafte Pfade. |
| ⬜ | RM-M5-06 | Container-Orchestrierungstests (Worker + Sidecar) | Compose-/CI-Gate startet Worker und Sidecar, prueft Health, erfolgreichen Optimierungslauf, Sidecar-Crash, Restart und Fallback; Container-Logs enthalten korrelierbare RunId/RequestId. |

---

## Sequenz

1. `AR-OPEN-002` schliessen: ADR 0004 oder Architektur §13 muss die
   finale Transportentscheidung inklusive Security-, Mocking- und
   CI-Konsequenzen enthalten, bevor ein produktionsnaher Sidecar-Pfad
   gemerged wird.
2. RM-M5-01 zuerst als schmalen Contract-Slice bauen: Health, Version,
   Test-Sidecar, Deadline, Idempotenz, Fallback und Status-Mapping.
3. RM-M5-05 parallel zum ersten Sidecar-Slice aktivieren, damit
   Sidecar-Fehler, Deadlines und Fallbacks nicht nachtraeglich
   observierbar gemacht werden muessen.
4. RM-M5-06 frueh als Container-Gate schneiden, sobald Worker und
   Test-Sidecar zusammenspielen.
5. RM-M5-04 danach ausbauen und bestehende M2/M3-Replay-Fixtures
   ueber Kompatibilitaets-Loader lauffaehig halten. Migrationen duerfen
   in kleinen Folge-PRs laufen; der alte Pfad wird erst entfernt, wenn
   Golden-Diff-Nachweise und ein ueberlappender CI-Lauf vorliegen. Neue
   MPC-Faelle bekommen eigene Manifestversion.
6. RM-M5-02 erst auf stabilem Contract-/Replay-Fundament aktivieren,
   damit numerische Drift und Constraint-Verletzungen reproduzierbar
   diskutiert werden koennen.
7. RM-M5-03 nur ziehen, wenn RM-M5-02 eine echte hochfrequente
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
  Fallback-Reason, `safe_stop`, `no_valid_plan`, Sidecar-Health und
  Command-Latenz sind getestet.
- Roadmap, Quality-Doku und Architektur werden beim Abschluss
  synchronisiert.

---

## Risiken und Entscheidungen

- **Transportvertrag vs. Architektur-Drift.** Architektur §13 und ADR
  0004 nennen gRPC als Phase-3-Kandidat, aber `AR-OPEN-002` in
  `spec/architecture.md` fuehrt gRPC vs. REST-only fuer externe
  Optimierungs-Sidecars noch als offen. Bis zur Entscheidung bleibt der
  Plan transportneutral; M5 muss die Architekturfrage vor produktivem Code
  per ADR-Update oder Architektur-Sync schliessen.
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
