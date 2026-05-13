# ADR 0008 - Edge Controller Boundary

**Status:** Accepted - RM-M6-05 legt Edge-Controller als
Integrations- und Schutzgrenze fest. Das Docker-EMS bleibt
supervisory/control-orchestrating und behauptet keine harte
Echtzeitfaehigkeit.
**Datum:** 2026-05-13
**Bezug:**
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§10.3 (Hardware-Schutzgrenze),
[`../../../spec/lastenheft.md`](../../../spec/lastenheft.md)
LH-SAFE-001, LH-RT-004 und LH-RISK-001,
[`../planning/done/plan-RM-M6.md`](../planning/done/plan-RM-M6.md)
(RM-M6-05),
[`../planning/done/plan-RM-M6-05.md`](../planning/done/plan-RM-M6-05.md)
(Detail-Slice),
[`../planning/done/plan-RM-M6-06.md`](../planning/done/plan-RM-M6-06.md)
(Regelleistungs-Zertifizierungsgate),
[`0005-optimization-core-sidecar-transport.md`](0005-optimization-core-sidecar-transport.md)
(Sidecar-/Transportgrenze),
[`0007-multi-asset-hosting-strategy.md`](0007-multi-asset-hosting-strategy.md)
(shared Worker und Worker-pro-Asset)

---

## 1. Kontext

M6 enthaelt einen Edge-Controller-Pfad, weil harte Echtzeit,
zertifizierungsnahe Schutzfunktionen und herstellernahe Regelungen nicht
im normalen Docker-/Worker-Regelkreis versprochen werden duerfen. Die
bestehenden Spezifikationen ziehen diese Grenze bereits:

- LH-SAFE-001 verlangt einen softwareseitigen Emergency Stop, stellt aber
  klar, dass kuerzere Reaktionszeiten ausserhalb des Docker-EMS ueber
  Hardware, BMS, Wechselrichter oder Edge-Steuerung geloest werden.
- LH-RT-004 beschreibt den 1-s-Regelzyklus und schliesst harte
  Echtzeitgarantien im .NET-/Docker-EMS aus.
- LH-RISK-001 fuehrt die fehlende Hardware-Schutzkette als hohes Risiko
  und verlangt eine klare Produktgrenze.
- Architektur §10.3 grenzt Software-Stop, BMS-/PCS-Schutz und
  Hardware-Schutzketten voneinander ab.

RM-M6-05 entscheidet deshalb keinen konkreten Vendor-Adapter. Der Slice
schliesst die Architekturfrage: Welche Verantwortung bleibt im EMS, wann
wird ein Edge-Pfad erforderlich, und welche Mindestsemantik muss eine
spaetere Edge-Integration einhalten?

---

## 2. Entscheidung

Das EMS bleibt die supervisory Schicht fuer Markt, Optimierung,
Operator-Workflows, Audit, Persistenz und den 1-s-Control-Cycle. Harte
Echtzeit, zertifizierte Schutzfunktionen und sub-cycle Actuator-Gates
liegen ausserhalb des Docker-EMS in BMS, PCS, Hardware-Schutzkette oder
einem dedizierten Edge-/Herstellercontroller.

| Achse | Entscheidung |
| ----- | ------------ |
| EMS-Verantwortung | Markt- und Fahrplanlogik, Optimierung, Dispatch-Priorisierung, Soft-Emergency-Stop im naechsten Zyklus, Persistenz, Audit, Replay, Operator-API/UI. |
| Edge-Verantwortung | Sub-cycle-Regelung, herstellernahe Interlocks, zertifizierungsnahe oder standortnahe Schutzlogik, schnelle Freigabe-/Sperrlogik fuer Aktoren, falls das Produkt dies verlangt. |
| BMS/PCS/Hardware | Physische Limits, Batteriezellschutz, Wechselrichterschutz, Not-Aus-Kette und finaler Schutz vor unsicheren Zustaenden. |
| Default-Topologie | Kein Edge-Zwang fuer M6. Shared Worker gemaess ADR 0007 bleibt Default, solange keine harte Echtzeit-, Zertifizierungs- oder Vendor-Anforderung einen Edge-Pfad triggert. |
| Edge-Deployment | Wenn Edge erforderlich wird, laeuft er mit eigener Lifecycle-, Restart-, Health- und Fault-Domain nahe am Asset oder Herstellercontroller. |
| Kommandosemantik | EMS-Kommandos bleiben auditierbare Sollwerte. Edge/BMS/PCS duerfen sie nach lokaler Schutzlogik begrenzen, sperren oder verwerfen. |
| Ausfallsemantik | Fehlender, alter oder inkompatibler Edge-Status darf nicht als aktive Steuerfreigabe gelten. Spaetere Adapter muessen fail-closed oder explizit vendor-safe dokumentiert sein. |

---

## 3. Trigger fuer Edge-Aktivierung

Ein eigener Edge-Adapter-, Edge-Deployment- oder Vendor-Contract-Slice
wird erst gestartet, wenn mindestens eines dieser Signale vorliegt:

- Ein Produkt verlangt Reaktionszeiten unterhalb des EMS-Zyklus, z. B.
  10-100 ms, deterministische Jitter-Grenzen oder Schutzlogik im
  sub-cycle Bereich.
- Eine Zertifizierung, ein TSO-/DSO-Konzept, ein Netzschutzkonzept oder
  eine Herstellerfreigabe verlangt eine getrennte Steuer-/Schutzinstanz.
- Ein BMS, PCS, Gateway oder lokaler Controller bringt ein festes
  Protokoll und eine verbindliche Command-/Interlock-Semantik mit.
- Ein Standort verlangt asset-nahe Ausfuehrung, getrennte Secrets,
  getrennte Restart-Domaenen oder Offline-Faehigkeit ohne zentrale
  Worker-Verfuegbarkeit.
- Der normale Worker-Fanout bleibt fachlich korrekt, aber Performance,
  Safety Case oder Betreibergrenzen verlangen eine eigene Fault-Domain.

---

## 4. Mindestvertrag fuer spaetere Edge-Pfade

Eine konkrete Edge-Integration muss mindestens diese Punkte liefern:

- Versionierter Contract fuer Kommandos, Status, Limits, Freigaben,
  Heartbeat und Fehlercodes.
- Freshness-Regel fuer Edge-Status und Telemetrie, inklusive maximalem
  Alter und Verhalten bei Ueberschreitung.
- Explizite Mapping-Tabelle fuer EMS-Command -> Edge-/Vendor-Command
  inklusive Begrenzung, Sperre, Rampen und sicherem Zustand.
- Health-, Readiness- und Kompatibilitaetscheck, bevor das EMS
  Edge-Faehigkeit in Status oder UI anzeigt.
- Auditierbare Entscheidung, ob ein Command ausgegeben, begrenzt,
  verworfen oder vom Edge blockiert wurde.
- Sicherheits- und Betriebsdokumentation fuer AuthN/AuthZ, Secret-
  Rotation, Netzwerksegmentierung und lokale Wartungszustaende.
- Tests oder HIL-Smokes fuer Verlust des Edge-Heartbeats, stale
  Telemetrie, inkompatible Version, lokale Sperre und Recovery.

Der konkrete Transport bleibt absichtlich offen. Zulaessige spaetere
Pfade sind z. B. vorhandene Feldadapter (Modbus, MQTT, OPC-UA) oder ein
versionierter Sidecar-/gRPC-Contract. Der Transport ist dem Produkt- und
Herstellerkontext untergeordnet.

---

## 5. Konsequenzen

- Kein M6-Arbeitspaket darf aus dem Docker-EMS harte Echtzeit- oder
  Schutzkettenfaehigkeit ableiten.
- Operator-UI und API duerfen Edge-Faehigkeit nur anzeigen, wenn ein
  konkreter Edge-Contract und Health-Status vorhanden sind.
- Worker-pro-Asset aus ADR 0007 bleibt ein moegliches Deployment-Pattern,
  aber keine automatische Edge-Implementierung.
- RM-M6-06 ist als Readiness-Gate geschlossen. Eine produktive
  zertifizierungsnahe Regelleistungswelle darf nur mit konkretem
  Produkt-/TSO-/Anlagenkonzept starten und muss auf diesen
  Edge-Boundary verweisen, sobald harte Echtzeit betroffen ist.
- Folgearbeiten werden als Trigger-Watch gefuehrt, bis ein Vendor-,
  Protokoll- oder Standorttrigger vorliegt.

---

## 6. Nicht-Ziele

- Keine generische Edge-Plattform ohne Produkt- oder Vendor-Kontext.
- Keine neue harte Echtzeitzusage fuer .NET, Docker, Kubernetes oder den
  shared Worker.
- Keine Umgehung von bestehender AuthN/AuthZ-, Audit-, Persistenz- oder
  Replay-Semantik.
- Keine Zertifizierungszusage ohne externen Safety Case und
  hardwareseitige Schutzkette.
