# Edge-Controller-Grenze

## Zweck

Dieses Dokument beschreibt, wo die Verantwortung des `bess-ems` endet
und wann ein Edge-Controller, BMS, PCS oder eine Hardware-Schutzkette
uebernehmen muss. Es ist eine Betreiber- und Integrationssicht auf
[RM-M6-05](../plan/planning/done/plan-RM-M6-05.md) und [ADR 0008](../plan/adr/0008-edge-controller-boundary.md).

Das EMS koordiniert Markt, Fahrplaene, Optimierung, Operator-Workflows,
Audit und den normalen 1-s-Dispatch. Es ersetzt keinen Hardware-Not-Aus,
keine BMS-Schutztechnik und keine zertifizierte Wechselrichter- oder
Netzschutzfunktion.

---

## Verantwortlichkeiten

| Komponente | Verantwortung | Nicht-Verantwortung |
| ---------- | ------------- | ------------------- |
| `bess-ems` | Markt-/Fahrplanlogik, Optimierung, Priorisierung, Soft-Emergency-Stop im naechsten Zyklus, persistierte Commands, Audit, Replay, Operator-API/UI. | Harte Echtzeit, zertifizierte Schutzketten, finaler physischer Zell-/Wechselrichterschutz. |
| Edge-/Herstellercontroller | Sub-cycle-Regelung, lokale Interlocks, schnelle Freigabe-/Sperrlogik, vendor-spezifische Command-Umsetzung, falls das Produkt dies verlangt. | Marktlogik, EMS-Audit, langfristige Optimierung und Ersatz der BMS-/PCS-Schutzfunktionen. |
| BMS | Batteriegrenzen, Zell-/Rackschutz, SOC/SOH, Alarme, Freigaben und Sperren. | Wirtschaftliche Optimierung oder Marktpriorisierung. |
| PCS/Wechselrichter | Umsetzung von P/Q-Sollwerten, Wechselrichterschutz, lokale Rampen, Betriebszustand und Fehler. | Vertrags- oder Portfolioentscheidung des EMS. |
| Hardware-Schutzkette | Not-Aus, elektrische Schutztechnik, physische Verriegelungen und Anlagen-Safety-Case. | Softwareseitige Optimierungs- oder Dispatchlogik. |

---

## Wann ein Edge-Pfad noetig wird

Ein Edge-Controller ist kein Default fuer M6. Er wird relevant, wenn eine
konkrete Anlage oder ein konkretes Produkt mindestens einen dieser
Trigger hat:

- Reaktionszeiten unterhalb des EMS-Zyklus, z. B. 10-100 ms oder harte
  Jitter-Grenzen.
- Zertifizierungs-, TSO-/DSO-, Netzschutz- oder Herstellervorgaben mit
  eigener lokaler Steuer-/Schutzinstanz.
- Ein vorhandener BMS-/PCS-/Gateway-Controller mit verbindlichem
  Protokoll und fester Interlock-Semantik.
- Offline- oder asset-naher Betrieb, der getrennte Restart-, Secret- und
  Fault-Domaenen verlangt.
- Ein Standort, an dem der normale Worker korrekt bleibt, aber
  Performance oder Safety Case eine dedizierte Edge-Fault-Domain
  verlangen.

---

## Mindestanforderungen an eine Integration

Eine konkrete Edge-Integration muss vor produktiver Aktivierung
mindestens klaeren:

- Versionierter Contract fuer Commands, Status, Limits, Heartbeat,
  Freigaben, Sperren und Fehlercodes.
- Freshness-Regel fuer Edge-Status und Telemetrie inklusive maximalem
  Alter.
- Mapping von EMS-Command zu Edge-/Vendor-Command, inklusive Begrenzung,
  Rampen, Sperren und sicherem Zustand.
- Health- und Kompatibilitaetscheck, bevor UI oder API Edge-Faehigkeit
  anzeigen.
- Auditpfad fuer ausgegebene, begrenzte, verworfene oder durch Edge
  blockierte Commands.
- Security-Konzept fuer AuthN/AuthZ, Secrets, Netzwerksegmentierung und
  Wartungszustaende.
- Tests oder HIL-Smokes fuer Heartbeat-Verlust, stale Telemetrie,
  inkompatible Version, lokale Sperre und Recovery.

---

## Stoerfallverhalten

Die Grundregel lautet: Ein unbekannter oder veralteter Edge-Zustand ist
keine Steuerfreigabe.

| Situation | Erwartetes Verhalten |
| --------- | -------------------- |
| Edge-Heartbeat fehlt oder ist zu alt | EMS darf Edge-Faehigkeit nicht als aktiv melden; Command-Ausgabe muss fail-closed oder vendor-safe dokumentiert sein. |
| Edge-Contract-Version ist inkompatibel | Integration bleibt deaktiviert, bis Version und Mapping explizit kompatibel sind. |
| Edge meldet lokale Sperre | EMS protokolliert die Sperre und darf keinen wirksamen Command gegen diese Sperre erzwingen. |
| BMS/PCS begrenzt Leistung | EMS speichert den eigenen Sollwert und muss die Begrenzung als lokalen Schutz-/Adaptereffekt sichtbar machen. |
| Operator setzt Soft-Emergency-Stop | EMS wechselt im naechsten Zyklus in sicheren Zustand; kuerzere Reaktionszeiten bleiben Aufgabe der Hardware-/Edge-Schutzkette. |

---

## Weiterer Ausbau

Der konkrete Transport ist produktabhaengig. Moegliche Pfade sind
vorhandene Feldadapter wie Modbus, MQTT oder OPC-UA, oder ein
versionierter Sidecar-/gRPC-Contract mit UDS/mTLS. Die Wahl wird erst in
einem Folge-Slice getroffen, wenn Hersteller, Protokoll, Latenzziel und
Standorttopologie bekannt sind.
