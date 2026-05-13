# Notiz: M6-Folgearbeiten (Trigger-Watch)

**Dokumenttyp:** Vorabklaerung / Trigger-Watch
**Status:** Offen - Folgearbeiten aus RM-M6-02/RM-M6-05 ohne aktiven
Implementierungsscope
**Bezug:**
[`../done/plan-RM-M6-02.md`](../done/plan-RM-M6-02.md)
(Multi-Asset-Hosting-Slice, abgeschlossen am 2026-05-13),
[`../done/plan-RM-M6-03.md`](../done/plan-RM-M6-03.md)
(Kubernetes-/Helm-Slice, abgeschlossen am 2026-05-13),
[`../done/plan-RM-M6-05.md`](../done/plan-RM-M6-05.md)
(Edge-Controller-Boundary-Slice, abgeschlossen am 2026-05-13),
[`../in-progress/plan-RM-M6.md`](../in-progress/plan-RM-M6.md)
(M6-Masterplan),
[`../../adr/0007-multi-asset-hosting-strategy.md`](../../adr/0007-multi-asset-hosting-strategy.md)
(shared Worker als M6-Default),
[`../../adr/0008-edge-controller-boundary.md`](../../adr/0008-edge-controller-boundary.md)
(Edge-Controller-Boundary)

---

## Zweck

RM-M6-02 hat den Multi-Asset-Default festgelegt und gepinnt: shared
Worker mit per-Asset fan-out. RM-M6-05 hat die Edge-Controller-Grenze
festgelegt: harte Echtzeit, zertifizierungsnahe Schutzlogik und finale
Aktorensperren bleiben ausserhalb des Docker-EMS. Einige Folgearbeiten
bleiben bewusst trigger-getrieben, weil sie eigene Topologie-,
Performance-, Produkt- oder Herstellerentscheidungen brauchen. Diese
Notiz haelt sie sichtbar, ohne die abgeschlossenen Slices mit
spekulativer Implementierung zu ueberladen.

Die Items hier werden beim Start von RM-M6-01 (Operator UI), RM-M6-03
(Kubernetes/Helm), beim Architektur-Review oder beim ersten produktiven
Multi-Asset-Engpass gescannt. Nach Abschluss von RM-M6-03 bleibt diese
Notiz auch der Sammelpunkt fuer Kubernetes-Folgehaertung, solange noch
kein eigenes Cluster-Smoke-Slice aktiv ist. Wenn ein Trigger zuendet,
entsteht ein eigener Slice-Plan in `open/` oder ein klar abgegrenztes
Unterpaket im ausloesenden M6-Plan.

---

## Item F-M6-02-01: Parallel-Fanout im shared Worker

**Quelle:** ADR 0007 §2/§5 und RM-M6-02 "Bewusst draussen".
Heute fuehrt `ControlCycleHostedService` die Assets pro Tick
sequenziell aus. Das ist der einfachste deterministische Default und
passt, solange die Tick-Dauer unter dem konfigurierten
`CycleInterval`-Budget bleibt.

**Trigger** (eines reicht):

- Die gemessene Tick-Dauer ueberschreitet mit mehreren Assets
  reproduzierbar das Budget, z. B. p95/p99 nahe oder ueber
  `WorkerOptions.CycleInterval`.
- Ein langsames Asset blockiert andere Assets trotz isolierter
  Fehlerbehandlung operativ sichtbar.
- RM-M6-03 definiert eine shared-Worker-Topologie mit Asset-Zahl, die
  sequenziell nicht mehr in das Regelkreisfenster passt.

**Scope-Skizze** (wenn der Trigger zuendet):

- Bounded parallelism, konfigurierbar als `MaxConcurrentAssets` oder
  aehnlicher Worker-Options-Slot.
- Per-Asset Cancellation-/Timeout-Grenzen ohne Abbruch des gesamten
  Ticks.
- Metriken fuer Tick-Gesamtdauer, per-Asset-Dauer und
  Fanout-Queue-/Parallelitaetsgrad.
- Tests fuer Fehlerisolation, nicht-deterministische Completion-Reihenfolge
  und Persistenz genau eines Commands pro erfolgreichem Asset.
- Review aller gemeinsam genutzten In-Memory-Stores auf Thread-Safety,
  bevor Parallel-Fanout aktiviert wird.

**Aktivierungs-Pfad:** eigener
`plan-RM-M6-02-FUP-parallel-fanout.md` oder Performance-Unterpaket in
RM-M6-03, falls Kubernetes-Sizing der konkrete Ausloeser ist.

---

## Item F-M6-02-02: Worker-pro-Asset als Helm-/Deployment-Pattern

**Quelle:** ADR 0007 §2/§5. Worker-pro-Asset ist zulaessig, aber nicht
Default. Es ist ein Deployment- und Isolationsthema, kein neues
Domain-Modell.

**Trigger** (eines reicht):

- Ein Kubernetes-Folge-Slice verlangt getrennte Pods, ServiceAccounts,
  Secrets, PersistentVolumes oder Restart-Domaenen pro Asset.
- Mandanten-, Betreiber-, Audit- oder Zertifizierungsgrenzen verbieten
  gemeinsame Prozess-/Secret-Domaenen.
- Ein Asset-Fehler stoert trotz lokaler Fehlerbehandlung andere Assets
  messbar und Isolation auf Prozess-/Pod-Ebene ist die angemessene
  Gegenmassnahme.

**Scope-Skizze** (wenn der Trigger zuendet):

- Helm-Values fuer `mode: shared-worker | worker-per-asset`.
- Per-Asset ConfigMap-/Secret-Bindings und eindeutige Kubernetes-Labels
  fuer `asset_id`, Mandant und Fault-Domain.
- Health-/Readiness-/Liveness-Probes pro Worker-Instanz.
- Dokumentierte Semantik: Ports und Persistenz bleiben `asset_id`-
  zentriert; Worker-pro-Asset veraendert nur Deployment-Isolation.
- CI-/Smoke-Pin fuer mindestens zwei Worker-Instanzen mit getrennten
  Asset-Konfigurationen.

**Aktivierungs-Pfad:** eigener RM-M6-Folgeplan oder Teil eines
Kubernetes-Hardening-/Cluster-Smoke-Slices. RM-M6-03 liefert bereits das
grundlegende Worker-pro-Asset-Rendering; weitere Trennung von Secrets,
ServiceAccounts oder Fault-Domains ist Folgehaertung.

---

## Item F-M6-03-01: Kubernetes Cluster-Smoke / CI-Gate

**Quelle:** RM-M6-03 "Cluster-Smoke" und `make helm-lint`.
Der Helm-Chart rendert reproduzierbar ohne Clusterzugriff. Ein echtes
Kubernetes-Gate ist bewusst nicht Teil des RM-M6-03-Abschlusses, solange
keine standardisierte kind-/k3d- oder Zielcluster-Umgebung definiert ist.

**Trigger** (eines reicht):

- CI oder Release-Prozess stellt reproduzierbar kind, k3d oder einen
  dedizierten Test-Cluster bereit.
- Kubernetes wird vom reinen Deployment-Artefakt zum verpflichtenden
  Release-Pfad.
- Ein Helm-Render-Fehler kann nicht mehr durch `helm lint` und
  `helm template` abgedeckt werden, z. B. wegen Admission Policies,
  StorageClass-/Ingress-Vorgaben oder Secret-/ServiceAccount-Grenzen.

**Scope-Skizze** (wenn der Trigger zuendet):

- `make helm-cluster-smoke` oder analoges Target mit klarer
  Cluster-Auswahl und Timeout-Grenze.
- Install/upgrade in isoliertem Namespace, Rollout-Wait fuer bess-ems,
  Postgres und optionale Sidecars, danach sauberes Uninstall.
- Mindestens shared Worker und worker-pro-asset rendern/installieren;
  optionaler mTLS-Pfad nur mit Test-Secrets.
- Dokumentierte Abgrenzung zwischen lokal optionalem Smoke und
  CI-pflichtigem Gate.

**Aktivierungs-Pfad:** eigener RM-M6-Folgeplan, bevor Kubernetes in
`make ci` oder Release-Gates aufgenommen wird.

---

## Item F-M6-02-03: Per-Asset-Sidecar oder Sidecar-Pool

**Quelle:** ADR 0005 §7, ADR 0006 §3/§5 und ADR 0007 §2. Der heutige
Sidecar-/MPC-Pfad ist request- und `asset_id`-bezogen, aber nicht an
eine eigene Sidecar-Instanz pro Asset gebunden.

**Trigger** (eines reicht):

- Ein Asset braucht ein anderes Optimierungs-/MPC-Backend als andere
  Assets.
- Ein Sidecar wird zum Performance- oder Fault-Isolation-Engpass im
  shared-Worker-Betrieb.
- Vendor-, Lizenz- oder Hardwarebindung verlangt getrennte Sidecar-
  Instanzen pro Asset oder Asset-Gruppe.
- Multi-Asset-MPC braucht bewusst einen Fleet-Sidecar, waehrend
  Einzelasset-MPC weiter pro Asset laeuft.

**Scope-Skizze** (wenn der Trigger zuendet):

- Konfigurationsmodell fuer Sidecar-Bindings: shared Default,
  per-Asset Override und optionaler Pool.
- Adapter-/Client-Factory keyed by `asset_id` oder `sidecar_binding_id`,
  ohne `asset_id` aus dem Application-Port zu entfernen.
- Health, Version, Contract-Kompatibilitaet und Metriken pro Binding.
- Tests fuer Routing, Failover-Grenzen und falsche Binding-Konfiguration.
- Doku zu Security-/Secret-Grenzen bei Cross-Host-Sidecars.

**Aktivierungs-Pfad:** eigener Folge-Slice oder Teil eines
Sidecar-/Kubernetes-Hardening-Plans, je nach Ausloeser.

---

## Item F-M6-02-04: Multi-Asset-MPC / gekoppelte Flottenoptimierung

**Quelle:** ADR 0006 §5, ADR 0007 §3 und RM-M6-02. Multi-Asset-MPC ist
keine Hosting-Frage. Es braucht ein eigenes Fachmodell fuer gekoppelte
Assets, gemeinsame Netzpunkte, Constraints und Operator-Workflows.

**Trigger** (eines reicht):

- Zwei oder mehr Batterien teilen einen Netzanschlusspunkt, eine
  gemeinsame Leistungsgrenze oder eine gemeinsame Marktposition.
- Ein Operator-Workflow verlangt Flottenoptimierung statt unabhaengiger
  Einzelasset-Optimierung.
- Regelleistungs- oder Marktproduktlogik koppelt Assets ueber einen
  gemeinsamen Abruf, eine Reserveverpflichtung oder eine Portfolio-
  Grenze.
- Replay-/Audit-Anforderung verlangt reproduzierbare
  Cross-Asset-Entscheidungen.

**Scope-Skizze** (wenn der Trigger zuendet):

- Neues Fleet-Use-Case-/Optimizer-Modell statt Erweiterung des
  normalen Control-Cycle-Fanouts.
- Explizite Kopplungsconstraints fuer Netzpunkt, Reserve, SOC-Band,
  Rampen oder Portfolio-Ziel.
- Fleet-Replay-Fixtures mit mehreren Asset-Streams und Golden-
  Entscheidungen.
- Persistenz-/Auditmodell fuer Fleet-Run plus abgeleitete per-Asset
  Commands.
- Entscheidung, ob Fleet-MPC im .NET-Prozess, im bestehenden Sidecar
  oder in einem dedizierten Fleet-Sidecar laeuft.

**Aktivierungs-Pfad:** eigener RM-M6-Folgeplan, wahrscheinlich nach
RM-M6-01/RM-M6-03, sobald UI-Workflow und Deployment-Topologie klar sind.

---

## Item F-M6-05-01: Konkreter Edge-/Vendor-Adapter

**Quelle:** ADR 0008 §3/§4 und RM-M6-05. Der Edge-Pfad ist als
Integrationsgrenze entschieden, aber kein konkreter Hersteller-,
Protokoll- oder Standortvertrag ist aktiv.

**Trigger** (eines reicht):

- Ein BMS, PCS, Gateway oder Edge-Controller ist als Produktkomponente
  ausgewaehlt und bringt ein verbindliches Protokoll mit.
- Ein Standort oder Produkt verlangt sub-cycle Reaktionszeiten,
  deterministische Jitter-Grenzen oder zertifizierungsnahe lokale
  Schutzlogik.
- Ein TSO-/DSO-, Netzschutz- oder Herstellerkonzept verlangt eine
  getrennte lokale Steuer-/Schutzinstanz.
- Ein Betreiber verlangt asset-nahe Offline-Faehigkeit, getrennte
  Restart-Domaenen, eigene Secrets oder getrennte Wartungszustaende.

**Scope-Skizze** (wenn der Trigger zuendet):

- Versionierter Contract fuer Commands, Status, Limits, Freigaben,
  Heartbeat und Fehlercodes.
- Transportentscheidung passend zum Produkt: vorhandener Feldadapter
  (Modbus, MQTT, OPC-UA) oder eigener Sidecar-/gRPC-Pfad.
- Freshness-, Health- und Kompatibilitaetschecks vor Aktivierung in
  API/UI.
- Fail-closed oder explizit vendor-safe dokumentiertes Verhalten bei
  Heartbeat-Verlust, stale Telemetrie, lokaler Sperre und Recovery.
- Audit-/Replay-Pins fuer ausgegebene, begrenzte, verworfene oder durch
  Edge blockierte Commands.
- HIL- oder Integrationstest fuer die relevanten Stoerfallpfade.

**Aktivierungs-Pfad:** eigener
`plan-RM-M6-05-FUP-edge-adapter.md` oder Teil von RM-M6-06, falls die
zertifizierungsnahe Regelleistungsintegration den Edge-Trigger ausloest.
