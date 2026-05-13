# Plan RM-M6-03 Kubernetes-Deployment + Helm Charts

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M6-03)
**Status:** Abgeschlossen am 2026-05-13. Aktiviert am 2026-05-13 nach
Abschluss von RM-M6-02.
**Bezug:**
[`../in-progress/plan-RM-M6.md`](../in-progress/plan-RM-M6.md)
(M6-Masterplan),
[`plan-RM-M6-02.md`](plan-RM-M6-02.md)
(Multi-Asset-Hosting-Default),
[`../../adr/0007-multi-asset-hosting-strategy.md`](../../adr/0007-multi-asset-hosting-strategy.md)
(shared Worker als Default),
[`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md)
(trigger-getriebene Abweichungen)

---

## Ziel

RM-M6-03 liefert einen Kubernetes-/Helm-Pfad fuer die bestehende
bess-ems-Topologie, ohne Compose als Referenzpfad zu entfernen. Der
Chart muss den ADR-0007-Default abbilden: shared Worker mit per-Asset
fan-out. Worker-pro-Asset bleibt ein bewusstes Deployment-Pattern, kein
neues Domain-Modell.

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M6-03-A | Helm-Chart-Skelett | `deploy/helm/bess-ems` rendert den bess-ems Worker/API-Host, Service, Asset-Konfiguration, Secrets, Postgres-StatefulSet und Probes. Shared Worker ist Default; `workerPerAsset` rendert je Asset eine Deployment- und Service-Instanz. |
| ✅ | RM-M6-03-B | Optionale Sidecars | Chart kann optional Mosquitto und ein optimization-core Sidecar-Service rendern. UDS-/mTLS-Werte sind explizit sichtbar; mTLS wird nur mit vollständigen Secrets und HTTPS-Endpoint gerendert. |
| ✅ | RM-M6-03-C | Helm-Gate | `make helm-lint` fuehrt `helm lint` und Render-Smokes fuer shared Worker, Worker-pro-Asset, optimization-core, optimization-core HTTPS/mTLS und MQTT aus. Das Target ist bewusst noch nicht in `make ci`, bis das Kubernetes-Gate stabil ist. |
| ✅ | RM-M6-03-D | Deployment-Dokumentation | [`deploy/helm/bess-ems/README.md`](../../../../deploy/helm/bess-ems/README.md) dokumentiert Values, Secrets, Probe-/Rollout-Verhalten, UDS-/mTLS-Optionen und Compose-vs-Helm-Grenze. |
| ➡️ | RM-M6-03-E | Cluster-Smoke | Bewusst als Follow-up nach [`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md) ausgelagert. RM-M6-03 bleibt ein clusterlos reproduzierbarer Helm-Slice; ein kind/k3d- oder Cluster-Smoke wird erst aktiviert, wenn die Umgebung normiert ist. |

---

## Entscheidungen

- **Chart-Pfad:** `deploy/helm/bess-ems`, parallel zu `deploy/compose.yml`.
- **Default-Topologie:** `topology.mode=shared` mit Multi-Asset-
  ConfigMap (`assets.json`).
- **Worker-pro-Asset:** `topology.mode=workerPerAsset` rendert pro Asset
  eine eigene Deployment- und Service-Instanz mit einzelner `asset.json`,
  damit die API nicht ueber Pods mit disjunkten Asset-Registries
  load-balanced.
- **Replica-Schutz:** `replicaCount` ist auf `1` fixiert, bis RM-M6
  Leader-Election oder verteiltes per-Asset-Locking definiert. Mehrere
  Replikas wuerden sonst parallele Control-Zyklen ausfuehren.
- **IO-Default:** NoOp IO bleibt Default. MQTT ist optional und fuer
  Single-Asset-Topologien gedacht, bis per-Asset Adapter-Zuordnung als
  eigener Trigger zündet.
- **Postgres:** Chart-interner StatefulSet ist der Entwicklungs- und
  Referenzpfad. Production kann spaeter externe Postgres-Secrets und
  managed Datenbanken als eigenen Hardening-Pass bekommen.
- **Sidecar:** optimization-core kann als Service gerendert oder ueber
  `externalEndpoint` angebunden werden. Production muss ADR 0005
  beachten: UDS oder HTTPS/mTLS statt plaintext HTTP.
- **mTLS:** `optimizationCore.transport.mtls.enabled=true` verlangt
  `https://` in `optimizationCore.externalEndpoint` sowie Secrets fuer
  Client-Zertifikat und vertrauenswuerdige Server-Zertifikate. Der Chart
  mountet diese Secrets und setzt die Host-Options-Pfade.

---

## Akzeptanzkriterien

- Helm rendert ohne Clusterzugriff fuer shared Worker und Worker-pro-Asset.
- `/health` ist Readiness- und Liveness-Probe fuer den bess-ems Host.
- Postgres nutzt Secret fuer Passwort/Connection-String und ein PVC fuer
  Daten.
- Asset-Konfiguration liegt als ConfigMap/Volume vor und bleibt explizit.
- `replicaCount > 1` wird vom Chart abgelehnt.
- HTTPS/mTLS fuer externe optimization-core Endpoints rendert Secret-
  Mounts und die zugehoerigen Host-Options-Umgebungsvariablen; fehlende
  Secrets werden abgelehnt.
- Compose bleibt dokumentierter Runtime-Smoke und wird durch Helm nicht
  ersetzt.

---

## Closure

RM-M6-03 ist abgeschlossen: Der Helm-Chart rendert den shared-Worker-
Default, Worker-pro-Asset, Postgres, Mosquitto, optimization-core und
den externen HTTPS/mTLS-Pfad. `make helm-lint` ist das reproduzierbare
clusterlose Gate; GitHub CI ist fuer den Abschluss-Commit gruen.

Der Cluster-Smoke bleibt bewusst ausserhalb dieses Abschlusses, weil
noch keine kind/k3d- oder Cluster-Standardumgebung festgelegt ist. Das
Follow-up ist in der M6-Trigger-Watch dokumentiert und darf erst in ein
Pflichtgate wandern, wenn die Umgebung reproduzierbar ist.
