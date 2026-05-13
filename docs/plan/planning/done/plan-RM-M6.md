# Plan RM-M6 Skalierung, UI, Edge / Multi-Asset

**Dokumenttyp:** Detailplan / M6 (abgeschlossen)
**Status:** Abgeschlossen am 2026-05-13. Aktiviert am 2026-05-13 nach
Abschluss von M5. Die Arbeitspakete RM-M6-01 bis RM-M6-06 sind
abgeschlossen oder als Trigger-Gate geschlossen;
`AR-OPEN-006` ist mit ADR 0007 geschlossen (shared Worker als Default,
Worker-pro-Asset als Deployment-/Isolation-Pattern).
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md) (M6),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
(§13 Phase 4, §18 `AR-OPEN-006`),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(§28.3 spaetere Erweiterungen, LH-PERSIST-005, LH-RISK-001,
LH-OPEN-005, LH-OPEN-006),
[`../../../user/persistence.md`](../../../user/persistence.md)
(TimescaleDB als kompatibler Folgeausbau),
[`plan-RM-M5.md`](plan-RM-M5.md)
(M5-Closure und post-M5-Grenzen),
[`../open/note-RM-M5-followups.md`](../open/note-RM-M5-followups.md)
(Sidecar-/MPC-Trigger-Watch),
[`../../adr/0007-multi-asset-hosting-strategy.md`](../../adr/0007-multi-asset-hosting-strategy.md)
(Multi-Asset-Hosting-Strategie),
[`../../adr/0008-edge-controller-boundary.md`](../../adr/0008-edge-controller-boundary.md)
(Edge-Controller-Boundary),
[`plan-RM-M6-01.md`](plan-RM-M6-01.md)
(abgeschlossener Operator-UI-Slice),
[`plan-RM-M6-02.md`](plan-RM-M6-02.md)
(abgeschlossener Multi-Asset-Hosting-Slice),
[`plan-RM-M6-03.md`](plan-RM-M6-03.md)
(abgeschlossener Kubernetes-/Helm-Slice),
[`plan-RM-M6-04.md`](plan-RM-M6-04.md)
(abgeschlossener TimescaleDB-Erweiterungs-Slice),
[`plan-RM-M6-05.md`](plan-RM-M6-05.md)
(abgeschlossener Edge-Controller-Boundary-Slice),
[`plan-RM-M6-06.md`](plan-RM-M6-06.md)
(abgeschlossener Regelleistungs-Zertifizierungsgate-Slice),
[`../../../user/edge-controller.md`](../../../user/edge-controller.md)
(Betreiber-/Integrationssicht auf die Edge-Grenze),
[`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md)
(M6-Trigger-Watch)

---

## Zweck

M6 zieht die Phase-4-Themen aus der Roadmap in einen umsetzbaren Rahmen:
Multi-Asset-Hosting, Operator-UI, Kubernetes/Helm, optionale
TimescaleDB-Erweiterung, Edge-Abgrenzung und zertifizierungsnahe
Regelleistungsvertiefung.

Der Meilenstein ist bewusst kein "alles skalieren"-Blankoscheck. M1-M5
haben API-first, Single-Asset-Regelkreis, M4-Regelleistungsbasis und M5
Sidecar-/MPC-/Replay-Pfade geliefert. M6 muss zuerst klaeren, wie mehrere
Assets im Host modelliert, isoliert und observiert werden, bevor UI,
Kubernetes-Topologie oder Edge-Pfade produktiv werden.

---

## Abgrenzung

**In Scope:**

- Multi-Asset-Hosting-Strategie inklusive Asset-Isolation,
  Scheduling-/Locking-Modell, Konfigurationsform und Observability-Labels.
- Operator-UI als API-first Web-Ausbau auf vorhandenen HTTP- und
  Audit-Pfaden; keine fachliche Bypass-Logik im UI.
- Kubernetes-/Helm-Deployment fuer Worker, API, Postgres und Sidecars,
  ohne die Compose-Referenz sofort zu entfernen.
- TimescaleDB als kompatible Persistenz-Erweiterung fuer Zeitreihen- und
  Retention-Bedarf, ohne fachliche Persistenzmodelle an Timescale zu
  koppeln.
- Edge-Controller-Abgrenzung fuer harte Echtzeit- oder
  zertifizierungsnahe Anforderungen.
- Zertifizierungsnahe Regelleistungsintegration als eigener Slice, erst
  nach Produkt-/TSO-/Anlagenkonzept-Klaerung.

**Out of Scope:**

- Harte Echtzeitgarantien im Docker-/Worker-Regelkreis.
- UI-only-Workflow, der bestehende API-, AuthN/AuthZ- oder Audit-Pfade
  umgeht.
- TimescaleDB als neuer Default oder Voraussetzung fuer MVP-Betrieb.
- Multi-Asset-MPC oder per-Asset-Sidecar-Topologie ohne
  Hosting-Entscheidung und Folge-ADR.
- Zertifizierungszusage ohne externe Produkt-, Hardware- und
  Anlagenkonzept-Abgrenzung.

---

## Aktivierungsbedingungen

M6 ist seit 2026-05-13 aktiviert: M5 ist abgeschlossen, die
Sidecar-/MPC-/Replay-Basis steht, und die offenen Phase-4-Fragen sind in
Roadmap, Lastenheft und Architektur sichtbar.

| Check | Erwartung |
| ----- | --------- |
| M5-Closure | ✅ `plan-RM-M5.md` ist geschlossen; RM-M5-01..07 sind gruen. |
| API-first | Operator-Funktionen bleiben bis zur UI ueber HTTP-API, AuthN/AuthZ und Audit nutzbar. |
| Multi-Asset-Blocker | ✅ `AR-OPEN-006` ist mit ADR 0007 geschlossen; konkrete Flotten-, UI- und Kubernetes-Slices referenzieren den shared-Worker-Default. |
| Timescale-Grenze | PostgreSQL bleibt Default; TimescaleDB darf nur kompatible Erweiterung sein. |
| Edge-Grenze | Harte Echtzeit und zertifizierungsrelevante Schutzfunktionen bleiben ausserhalb des Docker-Regelkreises oder brauchen eigenen Edge-/Herstellerpfad. |

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M6-01 | Operator UI (Web) | Abgeschlossen: [`plan-RM-M6-01.md`](plan-RM-M6-01.md). API-first Web-Shell unter `/operator/` fuer vorhandene Operator-Funktionen. Nutzt bestehende AuthN/AuthZ-/Audit- und HTTP-Pfade; keine fachliche Bypass-Logik. Erste Views: Health/Status, aktive Fahrplaene, Optimierungs-Run-Lookup und Operator-Stop-Status. |
| ✅ | RM-M6-02 | Multi-Asset-Flottensteuerung / Hosting-Strategie | Abgeschlossen: [`plan-RM-M6-02.md`](plan-RM-M6-02.md). ADR 0007 schliesst `AR-OPEN-006`: shared Worker mit per-Asset fan-out ist Default; Worker-pro-Asset bleibt Deployment-/Isolation-Pattern. Trigger-Watch fuer Parallel-Fanout, Worker-pro-Asset, per-Asset-Sidecar und Multi-Asset-MPC: [`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md). |
| ✅ | RM-M6-03 | Kubernetes-Deployment + Helm Charts | Abgeschlossen: [`plan-RM-M6-03.md`](plan-RM-M6-03.md). Helm-Chart fuer Worker/API, Postgres, optionale Sidecars und Secrets/Volumes mit shared-Worker-Default, Worker-pro-Asset-Rendering, UDS-/mTLS-Werten, `replicaCount`-Schutz und `make helm-lint`. Compose bleibt Referenzpfad bis ein Cluster-Gate stabil ist. |
| ✅ | RM-M6-04 | TimescaleDB-Erweiterung | Abgeschlossen: [`plan-RM-M6-04.md`](plan-RM-M6-04.md). Erster kompatibler Adapter-/Migrationspfad: `telemetry` wird nur bei verfuegbarer TimescaleDB-Extension zur Hypertable; Plain-Postgres bleibt Default und No-op-Pfad. Continuous Aggregates/Compression bleiben Folge-Slice. |
| ✅ | RM-M6-05 | Edge-Controller-Integration | Abgeschlossen: [`plan-RM-M6-05.md`](plan-RM-M6-05.md). ADR 0008 normiert die Edge-Controller-Grenze: EMS bleibt supervisory/1-s-Dispatch; harte Echtzeit, zertifizierungsnahe Schutzlogik und finale Aktorensperren liegen in Edge/Herstellercontroller, BMS/PCS oder Hardware-Schutzkette. Konkrete Edge-Adapter bleiben trigger-getriebene Folgearbeit. |
| ✅ | RM-M6-06 | Zertifizierungsnahe Regelleistungsintegration | Abgeschlossen als Readiness-/Trigger-Gate: [`plan-RM-M6-06.md`](plan-RM-M6-06.md). Vertieft M4-Regelleistung nicht spekulativ, sondern pinnt die Pflichttrigger fuer Produktregeln, TSO-/Vendor-Schnittstellen, Nachweise, Audit/Replay, Security-Profile und Edge-/Hardwaregrenze. Produktive Zertifizierungswelle bleibt Folgearbeit. |

---

## Sequenz

1. RM-M6-02 ist abgeschlossen: Multi-Asset-Hosting ist gehaertet;
   `AR-OPEN-006` ist mit ADR 0007 geschlossen.
2. RM-M6-03 ist abgeschlossen: Kubernetes/Helm ist auf der entschiedenen
   Hosting-Topologie geschnitten; Cluster-Smoke bleibt Follow-up.
3. RM-M6-01 ist abgeschlossen: Operator-Web-Shell auf stabiler
   Asset-/Operator-API-Sicht ist als API-first statische Shell geliefert.
4. RM-M6-04 ist abgeschlossen: TimescaleDB als kompatibler
   Telemetrie-Migrationspfad; PostgreSQL bleibt weiter Default.
5. RM-M6-05 ist abgeschlossen: Edge-Controller ist als
   Integrations-/Schutzgrenze dokumentiert; das Docker-EMS behauptet
   keine harte Echtzeitfaehigkeit.
6. RM-M6-06 ist abgeschlossen als Gate: keine produktive
   Zertifizierungswelle ohne konkretes Produkt-/TSO-/Anlagenkonzept;
   Folge-Slices muessen M4-Folgearbeiten und die Edge-/Hardwaregrenze aus
   ADR 0008 referenzieren.

---

## Akzeptanzkriterien

- `AR-OPEN-006` ist geschlossen und in ADR 0007 normativ entschieden.
- Multi-Asset-Betrieb hat ein klares Isolation-, Locking-, Clocking-,
  Persistenz- und Observability-Modell.
- UI-, Kubernetes-, Timescale-, Edge- und Zertifizierungs-Slices koennen
  auf diese Topologie referenzieren, ohne eigene Asset-Semantik zu
  erfinden.
- PostgreSQL bleibt lauffaehiger Default; TimescaleDB ist kompatibler
  Ausbau.
- Harte Echtzeit- und Schutzketten bleiben in Lastenheft und Architektur
  klar ausserhalb des Docker-Regelkreises abgegrenzt; ADR 0008 macht die
  Edge-Verantwortung und die Trigger fuer konkrete Integrationen
  normativ.
- Zertifizierungsnahe Regelleistungsintegration ist als Readiness-Gate
  geschlossen: Produktregeln, TSO-/Vendor-Schnittstellen, Nachweise,
  Security-Profile, Audit/Replay und Edge-/Hardwaregrenze muessen vor
  produktiver Aktivierung konkret vorliegen.

---

## Closure-Review vom 2026-05-13

**Ergebnis:** M6 ist vollstaendig innerhalb des dokumentierten Scopes
umgesetzt. Es bleibt keine offene M6-Pflichtimplementierung. Die
verbleibenden Punkte sind bewusst als Trigger-Watch oder Gate
ausgelagert, weil sie externe Produkt-, Cluster-, Performance-,
Hersteller-, TSO- oder Standortentscheidungen brauchen.

| Bereich | Review-Befund |
| ------- | ------------- |
| RM-M6-01 Operator UI | Implementiert: `/operator/` statische Web-Shell, `/assets`, `/operator/stops/current`, bestehende Health-/Status-/Schedule-/Optimization-Run-Pfade und `POST /operator/stop` ueber vorhandenen AuthN/AuthZ-/Audit-Pfad. Tests pinnen Asset-Liste, Stop-Status und statische UI-Dateien. Frontend-Bundling/E2E bleibt bewusst Folgearbeit. |
| RM-M6-02 Multi-Asset | Implementiert: ADR 0007, Multi-Asset-Config-Validierung inklusive leerer Liste und doppelter `asset_id`, Host-Registry-Seed, shared Worker fan-out, per-Asset Fehlerisolation, Metriken und Traces. Parallel-Fanout, per-Asset-Sidecar und Multi-Asset-MPC bleiben Trigger-Watch. |
| RM-M6-03 Kubernetes/Helm | Implementiert: Chart unter `deploy/helm/bess-ems` mit shared Worker, Worker-pro-Asset-Rendering, Postgres, Secrets/Volumes, Probes, optionalem MQTT, optionalem optimization-core Service und HTTPS/mTLS-Werten. `replicaCount > 1` wird abgelehnt, bis Leader-Election oder verteiltes Locking definiert ist. Cluster-Smoke ist bewusst Folgearbeit. |
| RM-M6-04 TimescaleDB | Implementiert: RunOnce-Migration `0005_timescale_telemetry_hypertable.sql` ist Plain-Postgres-kompatibel, erkennt `timescaledb`, erstellt die Extension nur wenn verfuegbar/installierbar und wandelt `telemetry` optional zur Hypertable. Continuous Aggregates, Compression und Retention-Policies bleiben Folgearbeit. |
| RM-M6-05 Edge-Grenze | Implementiert als Architektur-/Integrationsentscheidung: ADR 0008 und `docs/user/edge-controller.md` trennen EMS, Edge/Herstellercontroller, BMS/PCS und Hardware-Schutzkette. Ein konkreter Vendor-/Edge-Adapter ist bewusst nicht Teil von M6. |
| RM-M6-06 Zertifizierung | Implementiert als Readiness-/Trigger-Gate: M4/M5-Bausteine sind referenziert, fehlende externe Produkt-/TSO-/Anlagenpflichten sind benannt, und produktive Zertifizierungswellen bleiben Folge-Slices mit externem Regelwerk, Nachweisen, Security-Profil und Edge-/Hardwaregrenze. |

Nicht als M6-Defekt bewertet:

- kein Kubernetes-Cluster-Smoke in `make ci`, solange keine
  standardisierte kind/k3d- oder Zielcluster-Umgebung existiert;
- kein `replicaCount > 1` fuer den kombinierten Worker/API-Host, weil
  verteiltes per-Asset-Locking oder Leader-Election nicht definiert ist;
- kein konkreter Edge-/Vendor-Adapter ohne Produkt-, Protokoll-,
  Latenz- oder Standorttrigger;
- keine produktive Zertifizierungszusage ohne externes Regelwerk und
  Safety-/Nachweispaket;
- keine Timescale-spezifischen Aggregates/Compression ohne reale
  Datenvolumen und Abfrageprofile;
- keine neue Frontend-Toolchain, solange die statische API-first Shell
  den dokumentierten UI-Scope abdeckt.

---

## Risiken und Entscheidungen

- **Multi-Asset-Topologie als Blocker.** Ohne `AR-OPEN-006` wuerden UI,
  Kubernetes und Sidecar-Topologie jeweils eigene Asset-Semantik
  erfinden. Deshalb ist RM-M6-02 erster Slice.
- **UI vor API-Stabilitaet.** UI darf keine neue Fachlogik schaffen.
  Mitigation: UI nur ueber vorhandene oder explizit geplante Operator-API.
- **TimescaleDB-Kopplung.** Hypertables duerfen nicht in Domain- oder
  Application-Ports leaken. Mitigation: Adapter-/Migrationsgrenze und
  PostgreSQL-Default-Pins.
- **Edge-/Zertifizierungsversprechen.** EMS-Software ersetzt keine
  Hardware-Schutzkette. Mitigation: RM-M6-05 ist mit ADR 0008
  geschlossen; RM-M6-06 ist als Gate geschlossen; konkrete Edge-Adapter
  und produktive Zertifizierungswellen starten nur mit expliziter
  Produkt-, Hersteller-, TSO- oder Anlagenkonzept-Abgrenzung.
