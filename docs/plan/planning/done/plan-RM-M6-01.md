# Plan RM-M6-01 Operator UI (Web)

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M6-01)
**Status:** Abgeschlossen am 2026-05-13. Aktiviert am 2026-05-13 nach
Abschluss von RM-M6-02 und RM-M6-03.
**Bezug:**
[`plan-RM-M6.md`](plan-RM-M6.md)
(M6-Masterplan),
[`plan-RM-M6-02.md`](plan-RM-M6-02.md)
(Multi-Asset-Hosting-Default),
[`plan-RM-M6-03.md`](plan-RM-M6-03.md)
(Helm-/Deployment-Slice),
[`../../adr/0007-multi-asset-hosting-strategy.md`](../../adr/0007-multi-asset-hosting-strategy.md)
(shared Worker als Default)

---

## Ziel

RM-M6-01 liefert eine API-first Operator-Web-Shell fuer die vorhandenen
HTTP-Pfade. Das UI erfindet keine Fachlogik und fuehrt keine direkte
Domain-/Repository-Interaktion aus. Es zeigt nur vorhandene API-Zustaende
an und nutzt fuer Schreibaktionen denselben API-Token-/Audit-Pfad wie
externe Operator-Clients.

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M6-01-A | Operator-API-Leseflaechen | `GET /assets` liefert registrierte Assets fuer die Auswahl; `GET /operator/stops/current?assetId=...` liefert den aktuellen Operator-Stop-Zustand. Beide Endpoints sind duenne Read-Modelle ueber bestehende Application-Ports. |
| ✅ | RM-M6-01-B | Statische Web-Shell | `/operator/` liefert eine statische Shell fuer Health, Regelleistung-Health, Asset-Status, aktuelle Fahrplaene, Optimization-Run-Lookup und Operator-Stop-Aktivierung ueber `POST /operator/stop`. |
| ✅ | RM-M6-01-C | API-/UI-Pins | API-Tests pinnen Asset-Liste, Operator-Stop-Status und das Ausliefern der Web-Shell. Bestehende AuthN/AuthZ-Tests fuer `POST /operator/stop` bleiben die Schreibpfad-Grenze. |
| ➡️ | RM-M6-01-D | Frontend-Hardening | Bewusst als spaeterer Folge-Slice offengelassen: gebuendelte Frontend-Toolchain, visuelle Regressionen und End-to-End-Tests werden erst aktiviert, wenn die UI ueber die statische Shell hinauswaechst. |

---

## Entscheidungen

- **API-first:** Die UI nutzt `fetch` gegen bestehende HTTP-Endpunkte und
  speichert keine fachlichen Entscheidungen im Browser.
- **Kein Build-Step:** Die erste Shell lebt unter
  `src/adapters/driving/BatteryEms.Api/wwwroot/operator`. Damit bleibt
  der Slice ohne Node-/Frontend-Toolchain reproduzierbar.
- **Token-Eingabe:** Schreibaktionen verwenden einen eingegebenen Bearer
  Token. Operator-Identitaet und Audit bleiben serverseitig aus dem Token
  abgeleitet.
- **Asset-Auswahl:** `GET /assets` ist der einzige neue Asset-
  Auswahlpfad. Manuelle Asset-ID-Eingabe bleibt fuer Umgebungen ohne
  Registry-Seed sichtbar.

---

## Akzeptanzkriterien

- `/operator/` ist aus dem API-Host erreichbar.
- Health, Regelleistung-Health, Asset-Status, aktuelle Fahrplaene und
  Optimization-Run-Details werden ueber HTTP gelesen.
- Operator-Stop wird ausschliesslich ueber `POST /operator/stop` und den
  vorhandenen AuthN/AuthZ-/Audit-Pfad aktiviert.
- Die UI benoetigt keinen separaten Build-Schritt.
- `make test` laeuft fuer den Slice gruen.
