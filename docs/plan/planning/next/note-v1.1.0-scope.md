# Notiz: v1.1.0 Scope (Internal Refinement)

**Status:** Planning — vor Slice-Aktivierung. Diese Notiz fixiert den
Scope-Wurf für die nächste Minor-Version; sie ersetzt **keinen**
Slice-Plan und wird in `done/` (nach Release) oder umgeräumt
(falls Theme verworfen wird).
**Datum:** 2026-05-14
**Theme:** Internal Refinement — Items ohne externen Anlass, die
präventiv technischen Wert haben oder Carve-outs aus M2/M3 abräumen.
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md) („Aktueller
Stand"),
[`../open/note-RM-M3-followups.md`](../open/note-RM-M3-followups.md)
(Items 1, 7, 8 — Kandidaten A, C, D),
[`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md)
(Item F-M6-03-01 — Kandidat B),
[`../../../user/releasing.md`](../../../user/releasing.md) (Tag-
Vertrag, Voraussetzungen),
[`../../../user/quality.md`](../../../user/quality.md) §8
(Release-Gates), [CHANGELOG](../../../../CHANGELOG.md)

---

## Zweck

`v1.0.0` wurde am 2026-05-14 veröffentlicht. Das gesamte
M3-/M4-/M5-/M6-Followup-Backlog ist trigger-getrieben dokumentiert
(`open/note-RM-M*-followups.md`); aktive externe Trigger sind heute
**keine** im Anflug. Theme C (Internal Refinement) sammelt Items, die
auch ohne externen Anlass sinnvoll umgesetzt werden können — entweder
weil sie präventiv technischen Wert haben oder weil sie M3-Carve-outs
sauberer machen.

Diese Notiz fixiert pro Kandidat:
- die Quelle (welcher Follow-up-Eintrag)
- den Stand „heute" vs. „nach v1.1.0"
- ob es eine offene Entscheidung gibt, die v1.1.0 braucht
- den Aktivierungs-Pfad (eigener Slice-Plan in `open/` oder direkt
  `in-progress/`)

---

## Scope-Kandidaten

### Kandidat A: RM-M3-FUP-03 — Optimization-Lock-Eviction (OP-OPEN-06)

**Pflicht-Kandidat, klein, trigger-frei rechtfertigbar.**

- **Quelle:** [`note-RM-M3-followups.md` Item 7](../open/note-RM-M3-followups.md).
- **Stand heute:** **Zwei** Use Cases halten unbounded
  `_locks`-Dictionaries, beide Singleton-registriert in
  `ApplicationServiceRegistration`:
  1. `DefaultScheduleOptimizationUseCase` —
     `ConcurrentDictionary<(AssetId, ScheduleType), SemaphoreSlim>`
     für die `read-optimise-write`-Serialisierung des Day-Ahead-/
     Intraday-Schedule-Schreibpfads
     (`src/hexagon/BatteryEms.Application/Api/DefaultScheduleOptimizationUseCase.cs:70`).
  2. `DefaultIntradayReoptimizationUseCase` —
     `ConcurrentDictionary<string, SemaphoreSlim>` (per Asset)
     für die Resthorizont-Reoptimierung
     (`src/hexagon/BatteryEms.Application/Api/DefaultIntradayReoptimizationUseCase.cs:51`).

  Bei langlebigen Hosts mit vielen Asset-ID-Variationen (Multi-
  Tenant, ephemere Test-IDs) wachsen beide Hashtabellen
  unbeschränkt. **Scope-Erweiterung gegenüber Ursprungs-Notiz:**
  Die Originalformulierung in
  [`note-RM-M3-followups.md` Item 7](../open/note-RM-M3-followups.md)
  nennt nur `DefaultScheduleOptimizationUseCase`. v1.1.0 zieht den
  Intraday-Use-Case mit hinein, weil er dieselbe Klasse von
  Memory-Leak hat und die Closure-Definition sonst die Hälfte des
  Problems übrig lässt. Die Ursprungs-Notiz wird in derselben
  Slice-Welle nachgezogen, damit FUP-03-Closure und Followup-Note
  konsistent bleiben.
- **Nach v1.1.0:** Geteilter Eviction-Mechanismus (LRU oder TTL)
  mit konfigurierbarer Schwelle plus Gauge-Metrik
  `bess_optimization_lock_table_size{use_case="..."}` (Label
  unterscheidet die zwei Pfade). Implementiert entweder als eigener
  Helper-Type oder als zwei separate Eviction-Policies mit
  gemeinsamem Vertrag.

  **Observability-Vertrag (Pflicht-DoD):** Die Gauge-Metrik fügt sich
  nicht in `IOptimizationRunMetrics.Record(OptimizationRun)` ein
  (Port ist per-Run, nicht per-Gauge). Es entsteht ein neuer
  Observability-Port `IOptimizationLockMetrics` analog zu den
  bestehenden Ports
  (`IOptimizationRunMetrics`/`IControlCycleMetrics`/`IOptimizationCoreMetrics`)
  mit Pflicht-Implementierungen und der bestehenden
  **Layer-Trennung zwischen Application- und Telemetry-Adapter**:
  - `NoOpOptimizationLockMetrics` in
    `BatteryEms.Application.Observability` (Default), registriert
    via `ApplicationServiceRegistration` — wie
    `NoOpOptimizationRunMetrics` und die anderen NoOp-Defaults.
  - `PrometheusOptimizationLockMetrics` in
    `BatteryEms.Adapters.Telemetry/Prometheus/`, registriert in
    `TelemetryRegistration.AddBessTelemetry()` neben den
    bestehenden `PrometheusControlCycleMetrics`,
    `PrometheusOptimizationRunMetrics` und
    `PrometheusOptimizationCoreMetrics`. **Nicht** in
    `ApplicationServiceRegistration` registrieren — dort gehört
    nur der NoOp-Default hin (sonst Layering-Bruch und
    Telemetry-Host würde NoOp nicht ersetzen).
  - Unit-Test für die Gauge-Ausgabe in den Telemetry-Tests (gibt
    der Adapter den aktuellen Tabellen-Stand korrekt frei,
    Label-Set passt).
  - Registration-Test, dass `AddBessTelemetry()` den NoOp-Default
    durch den Prometheus-Adapter ersetzt — analog zu existierenden
    Registration-Tests für die anderen Metrics-Ports.
- **Begründung trotz fehlendem externen Trigger:** Präventive
  Hardening-Maßnahme; das Risiko-Profil verschlechtert sich mit
  jeder produktiven Stunde stillschweigend (Memory-Leak-Klasse),
  während der Fix klein und reversibel ist.
- **Vertrags-Erhalt (Pflicht-DoD):** Die Serialisierungs-Garantie
  („zwei parallele Aufrufe für denselben Key dürfen nicht denselben
  Base-Stand lesen und gegenseitig überschreiben") darf durch
  Eviction **nicht gebrochen werden**. `SemaphoreSlim` exponiert
  keinen öffentlichen Waiter-Count, und das naive
  Dictionary-Lookup-dann-Increment-Pattern hat eine Race: Eviction
  kann zwischen Lookup und Lease-Increment dieselbe Instanz
  entfernen. Daher konkrete Pflichten:
  - **Lease-Reservierung als atomar-verifizierte Operation
    (`TryAcquireLease`):** Pro Key wird ein Eintrag mit
    `(SemaphoreSlim semaphore, int leaseCount, int generation,
    bool tombstoned)` geführt. Acquire läuft als Schleife:
    (1) Eintrag lookup, (2) `tombstoned`-Flag prüfen,
    (3) `leaseCount` via `Interlocked.Increment` reservieren,
    (4) **nach** der Reservierung verifizieren, dass der
    Dictionary-Eintrag noch dieselbe aktive Generation hat
    (Instanz-Identität oder `generation`-Vergleich) — wenn nicht,
    Lease via `Interlocked.Decrement` zurücknehmen und mit
    `GetOrAdd` neu laden. Nur ein im selben Schritt verifizierter
    Lease darf auf `WaitAsync` gehen. Release dekrementiert
    `leaseCount` nach `semaphore.Release`.
  - **Cancellation-Pflicht (`WaitAsync` mit `CancellationToken`):**
    Wenn `WaitAsync(cancellationToken)` per
    `OperationCanceledException` abbricht, **wurde der Semaphore
    nicht gehalten** — `Release()` darf nicht aufgerufen werden
    (würde `SemaphoreFullException` werfen), aber der bereits
    reservierte `leaseCount` muss via `Interlocked.Decrement`
    zurückgenommen werden. Sonst bleiben abgebrochene Aufrufer
    als künstlich aktive Leases hängen und Eviction wird dauerhaft
    blockiert. Standard-Muster: separater `try/catch` um
    `WaitAsync` für Lease-Rollback bei Cancellation, dann separater
    `try/finally` für `Release` + Lease-Decrement im Happy-Path.
    Pflicht-Unit-Test: Caller, dessen `CancellationToken` zwischen
    Lease-Reservierung und Semaphore-Akquise gefeuert wird, hinter­
    lässt `leaseCount == 0`.
  - **Idle-only mit Tombstone-Pattern:** Eviction-Kandidaten
    werden anhand „seit X Sekunden ohne Acquire/Release" gewählt.
    Eviction setzt zuerst `tombstoned = true` (per
    `Interlocked.CompareExchange` auf einen Status-Slot), prüft
    dann `leaseCount == 0`, und entfernt erst danach den Eintrag
    via **conditional remove mit Instanz-Identität** (semantisch:
    „entferne diesen Eintrag nur, wenn er noch genau diese
    Instanz ist"). Die exakte API-Form (Cast auf
    `ICollection<KeyValuePair<,>>` und `Remove(pair)`, eigene
    Tombstone-CAS, oder ein verfügbarer
    `TryRemove(KeyValuePair)`-Overload) ist Slice-Plan-
    Entscheidung — der Vertrag ist die Instanz-bedingte
    Entfernung, nicht eine spezifische BCL-Methode.
  - **Abgebrochener Remove (Tombstone bleibt, Eintrag bleibt
    aktiv):** Wenn der Final-Check `leaseCount == 0` scheitert,
    weil zwischen Tombstone-Setzen und Final-Check ein neuer
    Acquire das Lease hochgezogen hat, **darf** der tombstoned
    Eintrag nicht im Dictionary verharren. Sonst spinnt jeder
    neue Acquirer im Re-Load-Pfad (`GetOrAdd` ersetzt einen
    existierenden Eintrag nicht). Bei abgebrochenem Remove **muss**
    das `tombstoned`-Flag per CAS auf `false` zurückgesetzt
    werden (selbe Instanz wird wieder normal nutzbar — alte
    Acquirer-Referenzen und der gerade hochgezogene neue
    Acquirer benutzen denselben Semaphore und bleiben
    serialisiert). **Replacement durch eine frische Generation
    ist hier explizit verboten**, weil das die Per-Key-
    Serialisierung bricht: alte Caller auf der alten Semaphore
    und neue Caller auf der neuen Semaphore würden parallel in
    denselben Critical Section eintreten. Replacement passiert
    ausschließlich auf dem normalen Eviction-Pfad **nachdem**
    `leaseCount == 0` final bestätigt und der Eintrag entfernt
    wurde — der nächste `GetOrAdd` legt dann eine frische
    Generation an. Pflicht-Unit-Test: „Tombstone gesetzt während
    aktive Lease hält; neuer Acquire kommt **vor** finalem
    Remove" — beweist dass weder Spin noch Deadlock entsteht,
    der neue Acquire auf demselben Semaphore landet, und
    Serialisierung erhalten bleibt.
  - **Dispose der entfernten Semaphore:** Nach erfolgreicher
    Entfernung aus dem Dictionary **und** finaler Bestätigung
    `leaseCount == 0` muss die `SemaphoreSlim`-Instanz disposed
    werden (sie hält intern unmanaged-Ressourcen, insbesondere
    ein lazy `AvailableWaitHandle`). Ein vergessener Dispose
    reduziert den Dictionary-Leak, lässt aber Semaphore-Ressourcen
    unnötig liegen.
  - **Race-sicheres Re-Add:** Wenn `TryAcquireLease` einen
    Tombstone oder Generations-Mismatch entdeckt, läuft
    `GetOrAdd` **nicht** sofort — dieser würde den noch nicht
    entfernten tombstoned Eintrag zurückliefern und der Caller
    spinnt. Stattdessen retry-Schleife mit drei möglichen
    Auflösungen:
    (a) **Tombstone wurde zurückgesetzt** (CAS auf `false`,
    siehe Eviction-Abandoned-Pfad unten) — Caller liest den
    Eintrag erneut und reserviert auf derselben Instanz.
    (b) **Tombstone-Eintrag ist inzwischen entfernt** (Eviction
    hat den conditional remove erfolgreich abgeschlossen) —
    `GetOrAdd` legt eine frische Instanz an.
    (c) **Eviction-Sweep ist noch in der Tombstone→Remove-
    Phase** — Caller wartet kurz (bounded backoff, z. B. einige
    `SpinWait`/`Thread.Yield` und bei Erfolglosigkeit
    `await Task.Yield()`) und versucht erneut. Hartes Timeout
    nach N Iterationen (Slice-Plan: konkreter Wert) wirft, damit
    pathologische Sweep-Bugs nicht unbegrenzt blockieren statt
    sichtbar zu failen.
  - **Concurrency-Test "Acquire racing with Eviction-Remove":**
    Pflicht-Unit-Test, der genau die Race-Sequenz „Caller hat
    Instanz-Referenz, Eviction setzt Tombstone und entfernt,
    Caller ruft Lease-Reservierung auf" trifft und beweist, dass
    der Caller entweder (a) auf die neue Generation springt oder
    (b) seinen Lease beim Verify-Schritt zurücknimmt und
    sauber neu lädt.
  - **Concurrency-Test "Sweep während paralleler Calls":**
    Zweiter Pflicht-Test, der einen Eviction-Sweep parallel zu
    zwei aktiv haltenden Callern fährt und beweist, dass kein
    Eintrag mit `leaseCount > 0` entfernt wird.
- **Aufwand:** ~4-5 PT (Slice + gemeinsame Eviction-Logik +
  Lease/Refcount-Wrapper + Metrik mit Label + zwei Concurrency-
  Tests + Aktualisierung der Ursprungs-Followup-Notiz +
  Dokumentation). **Doku-Ort:** wird im Slice-Plan festgelegt;
  `quality.md` §6 ist Native-/.NET-Parity (nicht passend),
  Persistenz-Doku ist ebenfalls fachfremd — vermutlich neuer
  Operations-/Metrik-Abschnitt in `quality.md` oder kurze
  Betriebsnotiz im Application-User-Doc.
- **Slice-Plan:** `plan-RM-M3-FUP-03.md` (entsteht in `open/` →
  `in-progress/` → `done/`).
- **Offene Entscheidungen** (Coverage **nicht** mehr offen —
  beide Use Cases sind harter Closure-Bestandteil, siehe Stand-
  heute-Block):
  - Default-Schwelle: TTL-basiert (z. B. 24h idle) oder
    LRU-Capacity (z. B. 1000) — beide?
  - Gemeinsamer Helper-Type für beide Use Cases vs. zwei separate
    Implementierungen mit dupliziertem Pattern.
  - Metrik-Labelstrategie (`use_case="schedule"|"intraday"` vs.
    zwei getrennte Metriken).

---

### Kandidat B: F-M6-03-01 — Kubernetes Cluster-Smoke

**Pflicht-Kandidat, klein, trigger-frei rechtfertigbar.**

- **Quelle:** [`note-RM-M6-followups.md`](../open/note-RM-M6-followups.md)
  Item F-M6-03-01.
- **Stand heute:** Helm-Chart `deploy/helm/bess-ems` ist mit
  RM-M6-03 geliefert (✓), `make helm-lint` rendert fünf Topologie-
  Varianten (shared, worker-per-asset, optimization-core,
  optimization-core-mtls, mqtt) — aber **kein** Smoke-Lauf fährt
  das Chart tatsächlich gegen einen Cluster (k3d/kind/minikube),
  prüft Pod-Health und tear-down.
- **Nach v1.1.0:** Neuer Make-Target `make helm-cluster-smoke`
  (Cluster-Tool als Slice-Plan-Entscheidung, Vorschlag k3d,
  Compose-Smoke-Vorbild). Workflow-Integration
  bewusst **nicht als blockierendes Gate**, sondern als
  **path-filtered optionaler PR-Check** plus `nightly`-Schedule
  auf `main`.

  **Topologie-Coverage (Pflicht-DoD per F-M6-03-01-Source-Spec):**
  Der Smoke installiert mindestens **shared Worker UND
  worker-pro-asset** (`topology.mode=shared` plus
  `topology.mode=workerPerAsset`), jeweils Rollout-Wait,
  Health-Probe und sauberes Uninstall. Ohne beide Topologien
  schließt v1.1.0 F-M6-03-01 nur partiell. Optionaler mTLS-
  Pfad nur mit Test-Secrets, falls Slice das einschließt.

  **Image-Strategie (Pflicht-Slice-Entscheidung):** Das Chart
  referenziert per Default lokale, nicht-publizierte Images
  (`bess-ems-runtime:latest`, optional `bess-ems-optimization-core-test-sidecar:latest`).
  Ein echter k3d-Smoke muss explizit eine der zwei Strategien
  wählen:
  1. **„Build + Import"** — Smoke baut die benötigten Images
     lokal (`make build`) und lädt sie via `k3d image import`
     in den Cluster. Validiert „Code-Stand in diesem PR ist
     chart-installierbar". Erkauft sich aber einen viel weiteren
     Pflicht-Input-Set: alles, was den Image-Build beeinflusst
     (`src/**`, `Dockerfile`, `Directory.Build.props`,
     `global.json`, `Directory.Packages.props`, ...). Path-Filter
     wird damit nahezu nutzlos.
  2. **„Published Image"** — Smoke setzt `image.repository` und
     `image.tag` per `--set` auf ein publiziertes Image
     (z. B. `ghcr.io/pt9912/bess-ems:latest` aus dem letzten
     erfolgreichen Release). Validiert „Chart ist gegen ein
     bekannt-gutes Image installierbar". Schmaler Path-Filter
     funktioniert. Erkennt aber **keine** Drift zwischen
     PR-Code und Chart-Vertrag — diese Validierung passiert
     unverändert im Release-Workflow gegen den frisch gebauten
     Tag.

  **Empfehlung für v1.1.0:** Strategie **(2)** Published Image,
  weil der PR-Smoke explizit nur Chart-Installierbarkeit
  validieren soll. Strategie (1) ist v1.2+-Erweiterung, wenn
  Bedarf entsteht.

  **Pull-Path-Pflicht (Strategie 2):** `ghcr.io/pt9912/bess-ems`
  ist nach dem v1.0.0-Push per Default **privat**
  (siehe [`releasing.md`](../../../user/releasing.md) §5.2).
  Ein k3d-Cluster mit anonymen Pulls würde reproduzierbar mit
  HTTP 401 scheitern. Slice-Plan muss eine der drei Optionen
  wählen:
  - **(P1) GHCR-Paket auf public umstellen** (Operations-
    Entscheidung — einmalig in den GHCR-Settings, hat
    Verbreitungs-Konsequenzen).
  - **(P2) Workflow-eigenen `GITHUB_TOKEN` für GHCR-Login,
    dann `docker pull` auf dem Runner und `k3d image import`**
    (Image landet im Cluster ohne dass der Cluster selbst
    GHCR-Zugriff braucht; GHCR bleibt privat).
  - **(P3) ImagePullSecret im k3d-Cluster** mit Workflow-
    `GITHUB_TOKEN` als Credential. Aufwendiger als (P2), weil
    Secret im Cluster gemanagt werden muss.
  Empfehlung **(P2)** als Pragmatik: GHCR-Sichtbarkeit ist
  Operations-Domain, der Smoke sollte nicht davon abhängen.

  **Bewusste Lücke:** Code-vs-Chart-Kompatibilität (frisch
  gebautes Image gegen frisch gerendertes Chart) wird **heute
  nirgendwo** automatisiert validiert — der aktuelle
  Release-Workflow baut zwar das Image, fährt aber kein
  `helm install` gegen den frisch gebauten Tag. Diese Lücke ist
  ein separates Folge-Item (vermutlich „Release-time chart-smoke
  gate" als neuer F-M6-03-Folge-Slice nach v1.1.0); der v1.1.0-
  Smoke schließt sie nicht und beansprucht das auch nicht. Wer
  in v1.1.0 Code-vs-Chart-Drift einführt, sieht es erst beim
  Operator-`helm install` — derselbe Stand wie heute.

  Mit Strategie (2) deckt der Path-Filter alle Inputs des
  PR-Smoke-Laufs ab. **Nightly auf `main` läuft unconditional**
  (kein Path-Filter — Scheduled Runs haben keinen PR-Diff zum
  Filtern, und nur ein verlässlich jede Nacht laufender Job
  beweist die für die Promotion geforderte Stabilitäts-Serie).
  Der Filter-Mechanismus wechselt **bei der Required-Promotion**:
  - **v1.1.0 (Stufe 1, optionaler PR-Check):** GitHub-`paths`-
    Filter im Workflow-Trigger. Wenn keiner der unten genannten
    Pfade berührt ist, startet der Job nicht.
  - **Ab Stufe 2 (required):** GitHub erlaubt für `required`-
    Checks keinen `paths`-Filter (`required` mit `paths` blockt
    sonst alle Nicht-Helm-PRs, weil der Job nie startet).
    Trigger wird auf `pull_request` ohne `paths` umgestellt, und
    der Skip-Sentinel innerhalb des Jobs (`git diff --name-only`
    gegen denselben Pfad-Set) gibt bei Nicht-Helm-PRs **Success**
    zurück. Die *logische* Pfad-Liste bleibt also gleich; nur
    der Mechanismus wechselt von Workflow-Filter zu Job-internem
    Skip.

  Die logische Pfad-Liste für beide Mechanismen:
  - `deploy/helm/**` (das Chart)
  - `Makefile` (Target-Definition)
  - `.github/workflows/cluster-smoke.yml` (oder wie immer der
    Workflow heißt — Job-Spec und Skip-Sentinel)
  - `scripts/helm-cluster-smoke*` (falls Helper-Skripte unter
    `scripts/` entstehen)

  Der `optimizationCore`-Sidecar bleibt im PR-Smoke abgeschaltet
  (`--set optimizationCore.enabled=false`), weil das zugehörige
  Image (`bess-ems-optimization-core-test-sidecar`) nicht
  publiziert wird. Die Sidecar-Topologie wird in einer späteren
  Smoke-Welle abgedeckt.

  Der „Gate"-Charakter (Pflicht-Lauf, PR-blocking, Release-
  Vorbedingung) wird **bewusst aufgeschoben** bis stabile Lauf-
  Serie nachgewiesen ist.
- **Begründung trotz fehlendem externen Trigger:** Helm-Lint-Render
  ohne Cluster-Apply ist kein Vertrag — der erste Operator, der
  `helm install` macht, könnte heute auf YAML-aber-nicht-Kubernetes-
  konforme Manifeste stoßen. Smoke kompensiert.
- **Aufwand:** ~3-4 PT (k3d-Setup im Workflow, Pod-Wait, Health-
  Probe, Tear-Down, Path-Filter + Nightly-Schedule).
- **Slice-Plan:** `plan-RM-M6-FUP-03-01.md`.
- **Promotion-Pfad** (explizit, damit „optional" nicht „dauerhaft
  optional" wird):
  1. **v1.1.0:** Path-filtered PR-Check mit dem oben genannten
     vollständigen Filter (`deploy/helm/**` + `Makefile` +
     Workflow-Datei + ggf. `scripts/helm-cluster-smoke*`) plus
     **unconditional** nightly auf `main` (kein Path-Filter, weil
     Scheduled Runs keinen PR-Diff haben und die Stabilitäts-
     Serie sonst nicht beweisbar ist). Failures werden im Issue-
     Tracker erfasst, blocken aber keine PRs.
  2. **Nach 4 Wochen ununterbrochen grünem Nightly:** Promotion
     zu `required`. **Pflicht-Begleitänderung:** Trigger wird auf
     `pull_request` ohne `paths`-Filter umgestellt und der Job
     bekommt einen frühen Skip-Check (z. B. `git diff --name-only`
     gegen `deploy/helm/**`), der bei Nicht-Helm-PRs sauber als
     **Success** zurückkehrt. Ohne dieses Sentinel-Pattern blockt
     ein `required`-Job mit `paths`-Filter alle Nicht-Helm-PRs,
     weil GitHub ihn dann nie startet.
  3. **Nach weiteren 4 Wochen grünem PR-Gate:** Aufnahme in
     `make ci` und damit Voraussetzung für `make fullbuild` und
     Release-Workflow.
- **Offene Entscheidungen:** k3d vs. kind vs. minikube
  (Vorschlag: k3d, weil leichtester Footprint im CI).

---

### Kandidat C: M3-D3 — PID-Routing in den Regelzyklus

**Bedingungs-Kandidat — braucht Konsumenten-Entscheidung vor
Aktivierung.**

- **Quelle:** [`note-RM-M3-followups.md` Item 1](../open/note-RM-M3-followups.md).
- **Stand heute:** `BatteryEms.Domain.PidController` ist als reine
  Domain-Primitive vorhanden (RM-M2-08), per Wire-Tests gegen die
  native `.so` validiert (RM-M3-13), aber **nicht** produktiv im
  Regelzyklus verdrahtet.
- **Nach v1.1.0 (wenn aktiviert):** `IPidKernel`-Driven-Port plus
  `ManagedPidKernel`/`NativePidFallbackKernel` plus DI-Verdrahtung
  plus Replay-Parity-Fixture (`pid_cases.v1.json`).
- **Aufwand:** ~1-2 Wochen, vergleichbar mit M3-D2.
- **Slice-Plan:** `plan-RM-M3-D3.md` (würde direkt in `in-progress/`).
- **Blocker (= offene Entscheidung für v1.1.0):** Ohne konkreten
  Konsumenten ist die Routing-Infrastruktur Selbstzweck. Bevor
  M3-D3 in v1.1.0 zugesagt wird, muss **einer** der folgenden
  Konsumenten definiert sein:
  - Power-Following-Use-Case (weicher Setpoint-Tracking) in
    `IControlCycleUseCase`
  - Frequency-Tracking / FCR-naher Pfad mit PI/PID-Stabilisierung
  - Ein Operator-/Customer-Use-Case, der konkret „X soll PID
    nutzen" formuliert
- **Empfehlung:** **Erst Kandidat A + B liefern und v1.1.0 damit
  closen.** M3-D3 in v1.2.0 oder einem späteren Release, sobald
  ein Konsument materialisiert. Andernfalls bauen wir Routing-
  Infrastruktur auf Verdacht — exakt das Anti-Muster, gegen das
  ADR 0009/0011/0012 systematisch entscheiden.

---

### Kandidat D: M3-FUP-04 — Replay-Carve-outs nach RM-M2-10

**Carve-out-Kandidat — Source-Note ist stale, vor Scope-Entscheid
zuerst Follow-up-Note bereinigen.**

- **Quelle:** [`note-RM-M3-followups.md` Item 8](../open/note-RM-M3-followups.md).
- **Source-Drift (wichtig):** Die Ursprungs-Notiz listet vier
  Carve-out-Varianten als „offen", aber **RM-M5-04** hat
  mindestens den ersten Punkt bereits geliefert:
  - **JSON-File-Loader für externe Replay-Datensätze ✓** —
    `TelemetryReplayJsonLoader` + `ReplayManifestLoader`
    (`replay-manifest.v1`, `telemetry-replay-fixture.v1`,
    `telemetry-replay-golden.v1`) plus `replay-diff-report.v1`-
    JSON-Report (siehe
    [`../done/plan-RM-M5-04.md`](../done/plan-RM-M5-04.md)
    RM-M5-04-A/D).
  Die übrigen drei Carve-outs (Operator-Replay-CLI, Multi-Asset-
  Replay-Koordination, Compare-against-Production-Replay) sind
  vermutlich noch offen, müssen aber gegen RM-M5-04-Output
  gegengeprüft werden, bevor sie als „v1.1.0-Kandidat" oder
  „weiterhin offen" qualifiziert werden können.
- **Empfehlung für v1.1.0:** **Aus Scope**, plus vorgelagerter
  Mini-Task: **Followup-Note `note-RM-M3-followups.md` Item 8
  vor jeder weiteren Scope-Entscheidung an den RM-M5-04-Lieferstand
  anpassen** (was bleibt offen, was ist geschlossen). Diese
  Bereinigung passt eher in eine separate „Source-Note-Refresh"-
  Welle als in den v1.1.0-Slice-Strang; sie ist die Voraussetzung,
  damit Kandidat D in v1.2+ überhaupt sauber bewertet werden kann.

---

## Empfohlener v1.1.0-Scope (Stand 2026-05-14)

| Item | Kandidat | Entscheidung |
| ---- | -------- | ------------ |
| RM-M3-FUP-03 Lock-Eviction (beide Use Cases) | A | **In Scope** — präventiv, deckt beide unbounded `_locks`-Tabellen ab |
| F-M6-03-01 Cluster-Smoke (path-filtered + nightly, kein Gate) | B | **In Scope** — klein, deckt Lücke ab; Promotion zu Gate über mehrere Releases |
| M3-D3 PID-Routing               | C | **Aus Scope** — braucht Konsumenten-Entscheidung; v1.2+ |
| M3-FUP-04 Replay-Carve-outs     | D | **Aus Scope** — alle Varianten trigger-getrieben |

**Geschätzte Größe v1.1.0:** ~7-9 PT (Kandidat A 4-5 PT, Kandidat B
3-4 PT) plus Release-Vorbereitung gemäß `releasing.md` §2 — knapp
zwei Arbeitswochen Brutto.

**SemVer-Begründung:** Minor (v1.0.x → v1.1.0), weil zwei neue
Capabilities kommen (Lock-Eviction-Konfiguration in beiden
Optimization-Use-Cases + optionaler Cluster-Smoke). Keine Breaking
Changes; keine API-Änderungen.

---

## Out of Scope (bewusst nicht in v1.1.0)

Diese Items sind weiterhin trigger-getrieben dokumentiert; ein
Drift in v1.1.0 wäre Anti-Muster:

- **OIDC/mTLS** (AR-OPEN-007 Folge-ADR) — wartet auf konkretes
  Production-Deployment
- **OPC-UA Security-Erweiterungen** (F-17/F-18/F-19) — warten auf
  Produktions-Use-Case
- **Cold-Start-Bootstrap** (F-01), **Alignment-Toleranz** (F-02),
  **persistente ACK** (F-03), **MQTTv5-Properties** (F-05),
  **OPC-UA-Mapping-Migration** (F-07), **OPC-UA-Activation-Source**
  (F-09) — Operations-Trigger
- **MPC-Sidecar-First** (F-M5-12) — wartet auf konkreten Sidecar-
  Bedarf
- **Multi-Asset-MPC** (F-M6-02-04) — wartet auf Flotten-Use-Case
- **Parallel-Fanout im shared Worker** (F-M6-02-01) — wartet
  auf Tick-Budget-/Performance-Trigger (gemessene Tick-Dauer
  überschreitet `CycleInterval`-Budget, langsames Asset blockiert
  andere)
- **Worker-pro-Asset als Deployment-Pattern** (F-M6-02-02) —
  wartet auf Isolation-/Fault-Domain-Trigger
- **Per-Asset-Sidecar oder Sidecar-Pool** (F-M6-02-03) — wartet
  auf Asset-spezifisches Optimization-/MPC-Backend-Bedarf
- **Edge-Adapter** (F-M6-05-01) — wartet auf konkrete Hardware-
  Auswahl
- **Zertifizierungswelle** (F-M6-06-01) — wartet auf TSO-/Anlagen-
  konzept
- **MILP-Optimierung** — bewusst auf LP+QP zurückgestellt
  (siehe Lastenheft §28.2)
- **Northbound-Export** — ADR 0012, trigger-basiert

---

## Aktivierungs-Pfad

1. **Diese Notiz reviewen + Scope bestätigen** (heute).
2. **Slice-Pläne anlegen** in `open/`:
   - `plan-RM-M3-FUP-03.md` (Lock-Eviction)
   - `plan-RM-M6-FUP-03-01.md` (Cluster-Smoke)
3. **Pläne nach `in-progress/` ziehen** und Slices liefern (jeweils
   eigener Branch / PR per Slice).
4. **CHANGELOG.md aktualisieren:** Einträge aus dem
   `## [Unreleased]`-Block in einen neuen Block `## [1.1.0] -
   YYYY-MM-DD` verschieben; `[Unreleased]` bleibt als leere
   Sektions-Stubs (`### Added` / `### Changed` / `### Fixed`)
   bestehen. Pattern siehe [`docs/user/releasing.md`](../../../user/releasing.md)
   §2 Punkt 3 und der `[1.0.0]`-Eintrag in
   [CHANGELOG](../../../../CHANGELOG.md) als Vorbild.
5. **Helm-Chart-Version** in `Chart.yaml`: `appVersion` auf `1.1.0`
   (folgt der App-Version), `version` konsistent erhöhen
   (voraussichtlich ebenfalls `1.1.0`, weil das Chart keine
   eigenen Änderungen unabhängig von der App hat). Per
   `releasing.md` §1 dürfen `version` und `appVersion` ab v1.1.0
   divergieren — für diese spezifische Minor ist Synchronisierung
   aber die naheliegende Wahl.
6. **Pflicht-Voraussetzungen vor Tag** durchgehen
   (`releasing.md` §2 — jede Verletzung ist ein **Stop**):
   - `git status` ist leer (main clean).
   - `make fullbuild` lokal grün (alle M1–M6-Gates + Compose-Smoke).
   - `make lock-refresh` produziert zero-diff.
   - `make helm-lint` grün.
   - Native-ABI-Bump prüfen: hat sich `native/battery_control_core/`
     verändert? (Für v1.1.0 unwahrscheinlich, weil Kandidaten A+B
     den Native-Kernel nicht anfassen — verifizieren!)
   - `docs/plan/planning/open/note-RM-M*-followups.md`-Scan: kein
     Eintrag durch v1.1.0-Auslieferung zwingend getriggert.
7. **`make release-assets VERSION=v1.1.0`** lokale Trockenübung
   (`releasing.md` §7 — produziert die 7 Asset-Dateien, ohne Push).
   Dieser Schritt **ersetzt nicht** Punkt 6; er ist eine zusätzliche
   Sanity-Übung der Asset-Pipeline.
8. **Tag setzen** (`releasing.md` §3): annotierter Tag
   `git tag -a v1.1.0 -m "..."` plus `git push origin v1.1.0`.
   Release-Workflow auf GitHub beobachten.
9. **Diese Notiz** nach `done/` (analog zu wie Slice-Pläne nach
   Abschluss umziehen) oder löschen, falls v1.1.0-Scope sich noch
   verschiebt.

---

## Offene Entscheidungen vor Slice-Start

1. **Bestätigung Scope-Wahl A + B** (nicht A + B + C, nicht A + B + D).
2. **Lock-Eviction-Strategie** für RM-M3-FUP-03 (Vorschlag:
   gemeinsame TTL 24h + LRU 1000, idle-only mit `TryAcquireLease`-
   Pattern (Lease-Reservierung + Generations-Verify + Rollback bei
   Mismatch + bounded-retry-Schleife bei Tombstone-Encounter),
   Tombstone-Eviction mit conditional remove auf Instanz-Identität,
   CAS-Rückstellung des Tombstones bei abgebrochenem Remove,
   `Dispose` der entfernten Semaphore. **Vier** Pflicht-
   Concurrency-Tests namentlich:
   (i) „Acquire racing with Eviction-Remove",
   (ii) „Sweep während paralleler Calls",
   (iii) „Cancellation zwischen Lease-Reservierung und Semaphore-
   Akquise" (kein `Release`, aber Lease-Decrement),
   (iv) „Tombstone gesetzt während aktive Lease hält; neuer
   Acquire kommt vor finalem Remove" (kein Spin, Serialisierung
   bleibt erhalten). Details im DoD von Kandidat A.
3. **Cluster-Smoke-Tool** für F-M6-03-01 (Vorschlag: k3d).
4. **Cluster-Smoke-Promotion-Pfad** bestätigen: path-filtered
   optional + nightly in v1.1.0 → PR-required (mit Sentinel-Skip-
   Check und Trigger-Wechsel zu allen PRs) nach 4 Wochen grün →
   `make ci`-Aufnahme nach weiteren 4 Wochen.
