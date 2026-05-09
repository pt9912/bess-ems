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

- gRPC-Vertrag fuer `optimization-core` mit Health, Version,
  Optimierungsrequest, Cancellation/Deadline und strukturierten
  Fehler-/Solverstatus-Rueckgaben.
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
  Deadline/Timeout, Fallback-Reason und Command-Latenz.
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
M5-Sidecar-Schnitt entweder bestaetigt oder ergaenzt werden.

| Check | Erwartung |
| ----- | --------- |
| M2-Optimierung | `IScheduleOptimizer`, `OptimizationRun`, Solverstatus und Persistenz sind stabil; Sidecar-Integration ersetzt keinen Domain-Vertrag. |
| M3-Native | In-Process Native bleibt fuer kleine Control-Kerne verfuegbar; M5 fuehrt Sidecar nur fuer MPC/Solver-grosse Kerne ein. |
| M4-Schnitt | Falls Regelleistungs-Reserve-Constraints Teil des MPC-/MILP-Modells werden, muss das M4-Reservemodell abgeschlossen oder als explizite Planannahme dokumentiert sein. |
| ADR-Status | ADR 0004 wird fuer gRPC-Sidecar, Supervisor-/Health-Verhalten, Fallback-Policy und Container-Topologie revalidiert. |
| Replay-Baseline | Bestehende M2/M3-Replay-Datensaetze sind referenzierbar; neue Datensaetze bekommen Manifest, Version und Vergleichsregeln. |
| Toolchain | Protobuf/gRPC-Codegen, Sidecar-Build und Container-Build sind reproduzierbar in Docker/CI. |
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

## Fallback-Matrix

RM-M5-01 muss diese Matrix als Default-Vertrag implementieren oder vor
Code-Aktivierung per Plan-/ADR-Update aendern. Ein Fallback darf niemals
ein stale Sidecar-Ergebnis wiederverwenden.

| Fehlerklasse | Verbindlicher Fallback | Qualitaetsdegradation | Mindestnachweis |
| ------------ | ---------------------- | -------------------- | --------------- |
| Sidecar gesund, Solverstatus usable | Sidecar-Ergebnis wird ueber bestehende Ports uebernommen und persistiert. | Keine. | Contract-Test fuer erfolgreichen Lauf. |
| Timeout/Deadline oder Unavailable vor Ergebnis | Horizon-Optimierung nutzt den konfigurierten lokalen `IScheduleOptimizer`, falls vorhanden; sonst wird keine neue Schedule-Version erzeugt und der letzte gueltige Fahrplan bleibt aktiv. | Optimierungsqualitaet kann auf lokalen LP-/NoOp-Pfad fallen; keine neue Version bei fehlendem lokalen Optimierer. | Timeout-/Unavailable-Test mit `Failed`-Run und Fallback-Reason. |
| Sidecar-Crash waehrend Lauf oder Transportabbruch nach Request-Start | Lauf endet als fehlgeschlagener `OptimizationRun`; Worker bleibt aktiv. Fuer den Regelkreis gilt weiter der letzte gueltige Fahrplan plus bestehende Limiter. | Keine neue Optimierung fuer diesen Lauf; Dispatch folgt bestehendem Fahrplan. | Container-Crash-Test mit weiterlaufendem Worker. |
| Sidecar liefert Infeasible/Failed ohne nutzbare Loesung | Keine neue Schedule-Version; letzter gueltiger Fahrplan bleibt aktiv. | Keine Reoptimierung; Ursache bleibt in Run/Metric sichtbar. | Solverstatus-Mapping-Test fuer nicht nutzbare Stati. |
| Sidecar liefert nicht-finite, schema-ungueltige oder constraint-verletzende Trajektorie | Ergebnis wird verworfen; keine neue Schedule-Version; Dispatch-/Control-Pfad verwendet bestehenden Managed-Limiter oder Safe-Stop, wenn kein gueltiger Setpoint existiert. | Sidecar-Ergebnis unbrauchbar; Safety hat Vorrang vor Optimierungsziel. | Negativtest fuer nicht-finite Werte und Constraint-Verletzung. |
| Ungueltiger Snapshot, stale Telemetrie oder invalider MPC-State vor Sidecar-Aufruf | Kein Sidecar-Aufruf und kein Optimierer-Fallback mit denselben ungueltigen Eingaben; bestehender Control-Precheck erzeugt Safe-Stop/invaliden Snapshot-Pfad. | Optimierung/Dispatch wird abgebrochen, bis valide Eingaben vorliegen. | Precheck-Test mit Nachweis, dass kein Sidecar-Request gesendet wird. |

---

## Replay-Kompatibilitaet

RM-M5-04 fuehrt ein Manifest-/Golden-Diff-Format ein, darf bestehende
M2/M3-Replay-Pipelines aber nicht still brechen.

| Thema | Vorgabe |
| ----- | ------- |
| Formatversion | Neue Datensaetze starten mit Manifest `replay-manifest.v1`; jedes Fixture nennt Schema-Version, Engine-Ziele, Toleranzen, Zeitbasis und Golden-Artefakte. |
| Bestehende M2-Fixtures | Der M2-Telemetrie-Replay-Shape bleibt ueber einen Kompatibilitaets-Loader lauffaehig, bis ein migriertes Manifest im selben PR eingecheckt ist. |
| Bestehende M3-Fixtures | Native-Parity-Cases bleiben als Referenzdatensatz erhalten; M5 darf sie referenzieren oder maschinell nach Manifest v1 spiegeln, aber nicht ohne Ersatz loeschen. |
| Migration | Falls ein Fixture-Format gebrochen wird, muss RM-M5-04 ein Migrationstool oder eine dokumentierte Fixture-Konvertierung mit Golden-Diff-Nachweis liefern. |
| CI-Kompatibilitaet | Alte und neue Replay-Gates duerfen erst zusammengelegt werden, wenn beide fuer mindestens einen PR-Lauf dieselben fachlichen Faelle abdecken. |

---

## Komponenten

| Bereich | Artefakt | LH-Bezug |
| ------- | -------- | -------- |
| Contract | `optimization-core` Protobuf/gRPC-Vertrag fuer Optimize, MPC, Health, Version und Cancellation | LH-OPT-006 |
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
| ⬜ | RM-M5-01 | gRPC-Sidecar `optimization-core` (LP/MILP/MPC) | Protobuf-Vertrag ist versioniert; .NET-Adapter ruft Sidecar mit Deadline/Cancellation auf; Health/Version sind testbar; Solverstatus und Fehler werden in bestehende Optimierungsmodelle gemappt; alle Fehlerklassen aus der Fallback-Matrix sind getestet. |
| ⬜ | RM-M5-02 | MPC-Kernel (State-Space, Kalman, Vorhersagehorizont) | Kernel berechnet finite Trajektorien aus State-Space-Modell und Horizon; Kalman-/Schaetzerpfad behandelt Rauschen, Missing Measurements und unplausible Werte; Tests beweisen SOC-, Leistungs-, Ramp- und Constraint-Einhaltung. |
| ⬜ | RM-M5-03 | Hochfrequente Telemetrie-Filterung im Native Core (optional) | Aktivierung nur bei konkretem Bedarf aus RM-M5-02; Filtervertrag dokumentiert Samplingrate, Einheiten und Fehlerverhalten; .NET-Prechecks bleiben erhalten; Replay-/Numeriktests decken Drift und ungueltige Eingaben ab; invalider Filter-/MPC-State folgt der Fallback-Matrix. |
| ⬜ | RM-M5-04 | Replay-Plattform mit Datensatz-Verwaltung und Sollwertvergleich | Versioniertes Manifest fuer Datensaetze; Loader fuer externe JSON-Fixtures; bestehende M2/M3-Fixtures bleiben ueber Kompatibilitaets-Loader oder Migration mit Golden-Diff-Nachweis lauffaehig; Runner vergleicht Commands/Sollwerte gegen Golden-Dateien und mehrere Engines; Diff-Report trennt erlaubte numerische Toleranz von fachlicher Drift. |
| ⬜ | RM-M5-05 | Erweiterte Metriken / Solverstatus / Command-Latenz | Prometheus-Metriken decken Solverstatus, Laufzeit, Deadline/Timeout, Fallback-Reason, Sidecar-Health und Command-Latenz ab; Tests scrapen erfolgreiche und fehlerhafte Pfade. |
| ⬜ | RM-M5-06 | Container-Orchestrierungstests (Worker + Sidecar) | Compose-/CI-Gate startet Worker und Sidecar, prueft Health, erfolgreichen Optimierungslauf, Sidecar-Crash, Restart und Fallback; Container-Logs enthalten korrelierbare RunId/RequestId. |

---

## Sequenz

1. ADR 0004 fuer M5 revalidieren und den konkreten IPC-/Containervertrag
   festhalten, bevor ein produktionsnaher Sidecar-Pfad gemerged wird.
2. RM-M5-01 zuerst als schmalen Contract-Slice bauen: Health, Version,
   Test-Sidecar, Deadline, Fallback und Status-Mapping.
3. RM-M5-05 parallel zum ersten Sidecar-Slice aktivieren, damit
   Sidecar-Fehler, Deadlines und Fallbacks nicht nachtraeglich
   observierbar gemacht werden muessen.
4. RM-M5-06 frueh als Container-Gate schneiden, sobald Worker und
   Test-Sidecar zusammenspielen.
5. RM-M5-04 danach ausbauen und bestehende M2/M3-Replay-Fixtures
   ueber Kompatibilitaets-Loader lauffaehig halten oder im selben Slice
   mit Golden-Diff-Nachweis migrieren; neue MPC-Faelle bekommen eigene
   Manifestversion.
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
  observierbar.
- Optimierer bleiben ueber bestehende Ports austauschbar; Application und
  Domain referenzieren keinen konkreten Solver und keinen gRPC-Client.
- Solverstatus, RunId, Horizon, Objective-/Qualitaetsinformationen und
  Fehlergrund bleiben mit dem M2-Optimierungsmodell kompatibel.
- Replay-Datensaetze sind versioniert, reproduzierbar und koennen
  Sollwerte/Commands zwischen Managed-, Native- und Sidecar-Pfaden
  vergleichen; bestehende M2/M3-Fixtures bleiben bis zu einer
  nachgewiesenen Migration lauffaehig.
- Container-Gates pruefen Worker + Sidecar inklusive Health,
  Orchestrierung, Crash/Restart und Fallback.
- Metriken fuer Solverstatus, Laufzeit, Deadline/Timeout,
  Fallback-Reason, Sidecar-Health und Command-Latenz sind getestet.
- Roadmap, Quality-Doku und Architektur werden beim Abschluss
  synchronisiert.

---

## Risiken und Entscheidungen

- **gRPC-Vertrag vs. Architektur-Drift.** Architektur §13 und ADR 0004
  sehen gRPC fuer Phase-3-Sidecars vor, aber das Architektur-Open-Item
  `AR-OPEN-002` in `spec/architecture.md` fuehrt gRPC vs. REST-only fuer
  externe Optimierungs-Sidecars noch als offen. M5 muss diese
  Architekturfrage vor produktivem Code per ADR-Update oder
  Architektur-Sync schliessen.
- **Fallback-Semantik.** Ein Sidecar-Fallback kann Optimierungsqualitaet
  verlieren. Die Fallback-Matrix ist der verbindliche Default; jede
  Abweichung braucht Plan-/ADR-Update, weil NoOp, bestehender LP-Adapter,
  letzter gueltiger Fahrplan und Safe-Stop fachlich unterschiedliche
  Antworten sind.
- **Numerische Drift.** MPC, Kalman und native Solver koennen kleine
  Rundungsabweichungen erzeugen. Replay-Toleranzen muessen eng,
  einheitenbezogen und fachlich begruendet sein; Safety-Invarianten haben
  keine Toleranzverletzung.
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
