# Plan RM-M4 Regelleistung und OPC-UA

**Dokumenttyp:** Detailplan / M4 (offen)
**Status:** Offen — abgeleitet aus Roadmap-Milestone M4, noch nicht aktiviert.
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
  übersteuern, aber niemals Emergency Stop, Geräte-, Wechselrichter-,
  Netz- oder SOC-/Ramp-Grenzen.
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
  Emergency Stop, Sicherheitsgrenzen, Regelleistungsaktivierung,
  verbindliche Marktverpflichtungen, Intraday, Day-Ahead, lokale
  Optimierung.
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
`main` grün sind. Vor dem ersten Implementierungs-PR müssen außerdem die
konkreten Regelleistungsprodukte benannt sein, mindestens als
Planungsannahme. Ohne diese Annahme darf M4 nur die generischen
Reservierungs- und Adapterpfade bauen, aber keine produktive
Aktivierungssemantik behaupten.

| Check | Erwartung |
| ----- | --------- |
| M3-Closure | Native Control Core und produktives Routing sind abgeschlossen; M4 verändert den Control-Kernel nicht ohne eigenen Slice. |
| Marktmodell | `MarketCommitment` und Priorisierung aus M2 bleiben kompatibel; Regelleistungsaktivierung ergänzt den bestehenden Prioritätspfad. |
| Optimierung | Bestehender Schedule-Optimizer kann um Resthorizont- und Reserve-Constraints erweitert werden, ohne Day-Ahead-Semantik zu brechen. |
| Adapter-Port | OPC-UA kann über `IBatteryTelemetrySource` und `IBatteryCommandSink` modelliert werden. Abweichungen brauchen Plan-Update vor Code. |
| Testumgebung | Ein OPC-UA-Simulator ist als CI- oder Docker-Pfad verfügbar oder wird als erstes M4-Arbeitspaket gebaut. |
| Produktannahme | Relevantes Regelleistungsprodukt, Zeitschritt, Aktivierungsrichtung und Leistungsinterpretation sind dokumentiert. |

---

## Zielbild Und Abnahmeschnitt

| Situation | Erwartetes Verhalten | Mindestnachweis |
| --------- | -------------------- | --------------- |
| Intraday-Reoptimierung | Ein bestehender Fahrplan kann ab einem Resthorizont neu bewertet und als neue Version abgelegt werden. Bei Optimiererfehlern, invaliden Inputs oder unzulässigen Constraints bleibt die bisherige gültige Zukunftsversion aktiv. | Use-Case-/Optimizer-Test mit unverändertem Vergangenheitsfenster, neuer Zukunftsversion und Fehlerpfad ohne Schedule-Replace. |
| Regelleistungsreservierung | Reservierte Lade- und Entladeleistung wird für Day-Ahead und Intraday blockiert. | Solver-Test, der Reserve-Bänder verletzt hätte und nun begrenzt wird. |
| Aktivierungssignal aktiv | Der Regelkreis nimmt den Aktivierungs-Setpoint vor Marktcommitments und Fahrplänen, bleibt aber innerhalb Safety-, SOC-, Ramp- und Geräte-Limits. | Control-Cycle-Test mit konkurrierendem Day-Ahead/Intraday-Commitment. |
| Aktivierungssignal ungültig/stale | Kein normaler Fahrplan wird durch ein unvalides Signal verdrängt; der Zustand ist observierbar. | Negativtest für stale Timestamp, nicht-finite Leistung und fehlende Asset-Zuordnung. |
| OPC-UA Lesen | Node-Werte werden in interne Telemetrie gemappt. | Adaptertest gegen Simulator inklusive Datentyp- und Unit-Mapping. |
| OPC-UA Schreiben | Leistungssollwerte werden auf konfigurierte NodeIds geschrieben. | Simulator-Integrationstest mit Command-Result. |
| OPC-UA Subscription | Änderungen ausgewählter Nodes werden ereignisbasiert verarbeitet. | Integrationstest mit Subscription-Update ohne Polling-Only-Pfad. |
| OPC-UA StatusCode schlecht | Wert wird als ungültig markiert und nicht als gesunde Telemetrie verwendet. | Adaptertest für Bad/Uncertain StatusCode. |
| OPC-UA Security | Zertifikat, Security Mode und Security Policy sind konfigurierbar. | Konfigurations-/Handshake-Test gegen gesicherten Simulator oder Testserver. |
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

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ⬜ | RM-M4-01 | Intraday-Reoptimierung (Resthorizont) | Neuer Use-Case erzeugt eine Schedule-Version nur für den zukünftigen Resthorizont; Vergangenheitsfenster bleiben unverändert. Bei Optimiererfehlern, invaliden Inputs oder unzulässigen Constraints wird kein leerer/teilweiser Fahrplan geschrieben; die bisherige gültige Zukunftsversion bleibt aktiv und der Fehler ist persistier-/observierbar. Tests decken UTC, halboffene Fenster, Versionsnummern, vorhandene Marktcommitments und den Fallback-ohne-Replace-Pfad ab. |
| ⬜ | RM-M4-02 | Reservierungs-Modell für Regelleistung + Solver-Constraints | Domain-Modell für Lade-/Entlade-Reserve mit Zeitfenster und Asset-Bezug; Optimizer begrenzt verfügbare Leistung entsprechend. Tests zeigen, dass Day-Ahead- und Intraday-Optimierung Reserve-Bänder nicht verletzen. |
| ⬜ | RM-M4-03 | Regelleistungs-Aktivierungssignal-Verarbeitung mit Priorisierung | Aktivierungssignal wird validiert, zeitlich begrenzt und im Regelkreis vor Marktcommitments/Fahrplänen berücksichtigt. Safety-Limits bleiben vorrangig; stale/ungültige Signale sind observierbar und verdrängen keinen validen Fahrplan. |
| ⬜ | RM-M4-04 | OPC-UA-Adapter (Lesen, Schreiben, Subscriptions, StatusCode) | Adapter-Projekt `BatteryEms.Adapters.OpcUa` implementiert die bestehenden Driven-Ports `IBatteryTelemetrySource` und `IBatteryCommandSink`; Node-Werte werden in interne Telemetrie gemappt; Commands schreiben konfigurierte NodeIds; Subscriptions liefern Updates; schlechte StatusCodes markieren Daten ungültig. |
| ⬜ | RM-M4-05 | OPC-UA-Security (Zertifikate, Security Mode/Policy) | Konfiguration validiert Endpoint, Security Mode, Security Policy und Zertifikatspfade; unsichere Defaults sind explizit und testbar. Gesicherter Handshake ist gegen Simulator/Testserver nachgewiesen. |
| ⬜ | RM-M4-06 | MQTT QoS und Command-ACK-Korrelation | QoS-Level ist pro Publisher/Subscriber konfigurierbar; Commands tragen `CommandId`; ACKs korrelieren deterministisch und Mismatch/Timeout erzeugen einen fehlerhaften `CommandDispatchResult`. |
| ⬜ | RM-M4-07 | Versionierte OPC-UA-Mappings in Config | `config/schema/opcua-mapping.schema.json` validiert NodeIds, Richtung, Datentyp, Skalierung, Unit und Device-Point-Metadaten. Beispiel-Mapping liegt unter `config/examples/adapters/`; Loader-Tests decken gültige und ungültige Mappings ab. |
| ⬜ | RM-M4-08 | Integrationstests OPC-UA gg. Simulator | Docker-/CI-fähiger Simulatorpfad testet Lesen, Schreiben, Subscription, StatusCode und Security-Basispfad. Quality-Doku wird mit konkretem Testtarget aktualisiert. |

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

- Bei aktiver Regelleistungsanforderung übersteuert der Regelkreis den
  normalen Fahrplan, ohne Sicherheitsgrenzen zu verletzen.
- Emergency Stop, Geräte-/Netzgrenzen, SOC-Grenzen und Ramp-Limits haben
  weiterhin höhere Priorität als Regelleistungsaktivierung.
- Day-Ahead- und Intraday-Optimierung verletzen keine reservierten
  Regelleistungsbereiche.
- Ein bestehender Fahrplan kann für einen Resthorizont neu bewertet und
  versioniert ersetzt werden.
- Scheitert die Resthorizont-Reoptimierung, wird kein leerer oder
  teilweiser Fahrplan aktiv; die bisherige gültige Zukunftsversion bleibt
  der Fallback.
- OPC-UA integriert sich über `IBatteryTelemetrySource` und
  `IBatteryCommandSink`; die zentrale Regelpipeline braucht keinen
  protokollspezifischen Zweig.
- OPC-UA-Mappings sind versioniert, schema-validiert und mit
  Device-Point-Metadaten kompatibel.
- Schlechte OPC-UA-StatusCodes und stale Aktivierungssignale werden als
  ungültig behandelt und sind in Tests sichtbar.
- MQTT-Command-ACK-Korrelation ist über `CommandId` getestet, inklusive
  Timeout und falscher ACK-ID.
- Quality-Doku und Roadmap werden beim Abschluss synchronisiert.

---

## Risiken und Entscheidungen

- **Regelleistungsprodukt offen.** LH-OPEN-002 fragt weiterhin, welche
  Produkte konkret relevant sind. M4 darf deshalb mit einem generischen
  Aktivierungsmodell starten, muss Produktannahmen aber vor produktiver
  Aktivierung festhalten.
- **Echtzeitfähigkeit.** Wenn Aktivierungsfristen enger sind als der
  aktuelle .NET-Regelkreis belastbar erfüllt, ist ein ADR für Native-,
  Edge- oder Out-of-Process-Design nötig. Das ist nicht implizit Teil von
  M4.
- **OPC-UA-Bibliothek.** Die konkrete .NET-OPC-UA-Library wird im ersten
  Adapter-Slice entschieden. Auswahlkriterien: Security-Support,
  Subscription-Stabilität, Testserver-/Simulatorfähigkeit,
  Lizenzkompatibilität und CI-Reproduzierbarkeit.
- **Mapping-Drift.** Hersteller-NodeIds und Firmware-Versionen können
  auseinanderlaufen. Deshalb muss RM-M4-07 versionierte Mapping-Dateien
  und Schema-Tests vor produktiver Adapteraktivierung liefern.
- **Prioritätskonflikte.** M2 hat Marktcommitments bereits priorisiert;
  M4 erweitert diese Ordnung. Tests müssen zeigen, dass bestehende
  Day-Ahead-/Intraday-Pfade nicht versehentlich höhere Priorität behalten.
