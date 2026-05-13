# Plan RM-M6-05 Edge-Controller-Integration

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M6-05)
**Status:** Abgeschlossen am 2026-05-13.
**Bezug:**
[`../in-progress/plan-RM-M6.md`](../in-progress/plan-RM-M6.md)
(M6-Masterplan),
[`../../adr/0008-edge-controller-boundary.md`](../../adr/0008-edge-controller-boundary.md)
(Edge-Controller-Boundary),
[`../../../user/edge-controller.md`](../../../user/edge-controller.md)
(Betreiber-/Integrationsdoku),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
§10.3,
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
LH-SAFE-001, LH-RT-004 und LH-RISK-001,
[`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md)
(Trigger-Watch fuer konkrete Edge-Folgearbeiten)

---

## Ziel

RM-M6-05 schliesst die Edge-Controller-Frage als Architektur- und
Integrationsgrenze. Das Ergebnis ist keine spekulative Vendor-
Implementierung, sondern ein belastbarer Boundary-Contract: EMS,
Edge/Herstellercontroller, BMS/PCS und Hardware-Schutzkette haben klare
Verantwortungen, und spaetere Edge-Adapter starten nur mit konkretem
Produkt-, Protokoll- oder Standorttrigger.

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M6-05-A | Architekturentscheidung | ADR 0008 legt fest, dass harte Echtzeit und zertifizierungsnahe Schutzfunktionen ausserhalb des Docker-EMS liegen. |
| ✅ | RM-M6-05-B | Verantwortungsmatrix | EMS, Edge/Herstellercontroller, BMS/PCS und Hardware-Schutzkette sind in ADR und Anwenderdoku getrennt beschrieben. |
| ✅ | RM-M6-05-C | Trigger und Mindestvertrag | Edge-Aktivierung, Contract-Pflichten, Heartbeat/Freshness, fail-closed Semantik und HIL-/Testanforderungen sind dokumentiert. |
| ✅ | RM-M6-05-D | Plan-/Roadmap-Sync | M6-Masterplan, Roadmap, Architekturhinweis und M6-Follow-up-Watch referenzieren die abgeschlossene Edge-Grenze. |
| ➡️ | RM-M6-05-E | Konkreter Edge-/Vendor-Adapter | Bewusst Folgearbeit: erst mit Hersteller, Protokoll, Latenzziel, Zertifizierungsanforderung oder Standorttopologie. |

---

## Entscheidungen

- **Kein Edge-Zwang im M6-Default:** Shared Worker bleibt gemaess ADR
  0007 der Default. Edge ist ein Integrationspfad, kein versteckter
  Runtime-Zwang.
- **EMS bleibt supervisory:** Marktlogik, Optimierung, Operator-API/UI,
  Audit, Persistenz, Replay und 1-s-Dispatch bleiben im EMS.
- **Harte Echtzeit bleibt ausserhalb:** Sub-cycle-Regelung,
  zertifizierungsnahe Schutzlogik und finaler Aktorenschutz gehoeren zu
  BMS, PCS, Hardware-Schutzkette oder dediziertem Edge-/Herstellerpfad.
- **Fail-closed statt stiller Freigabe:** Fehlender, alter oder
  inkompatibler Edge-Status darf nie als aktive Steuerfreigabe
  interpretiert werden.
- **Transport erst mit Produktkontext:** Modbus, MQTT, OPC-UA, gRPC oder
  UDS/mTLS sind moegliche Pfade, aber keine M6-05-Entscheidung ohne
  konkreten Hersteller- oder Standortvertrag.

---

## Akzeptanzkriterien

- Architektur und Lastenheft bleiben konsistent: Docker-/Worker-EMS
  behauptet keine harte Echtzeit.
- Es gibt eine normative ADR fuer die Edge-Grenze.
- Betreiber- und Integrationsdoku beschreibt klare Verantwortlichkeiten,
  Trigger, Mindestvertrag und Stoerfallverhalten.
- Folgearbeiten sind sichtbar, aber nicht spekulativ umgesetzt.
- RM-M6-06 kann auf die Edge-Grenze referenzieren, bevor
  zertifizierungsnahe Regelleistung vertieft wird.

## Verifikation

- ✅ `git diff --check`
