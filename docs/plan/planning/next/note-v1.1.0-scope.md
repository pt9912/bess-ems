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
  unbeschränkt. **v1.1.0 schließt beide oder ist kein FUP-03-Closure.**
- **Nach v1.1.0:** Geteilter Eviction-Mechanismus (LRU oder TTL)
  mit konfigurierbarer Schwelle plus Metrik
  `bess_optimization_lock_table_size{use_case="..."}` (Label
  unterscheidet die zwei Pfade). Implementiert entweder als eigener
  Helper-Type oder als zwei separate Eviction-Policies mit
  gemeinsamem Vertrag.
- **Begründung trotz fehlendem externen Trigger:** Präventive
  Hardening-Maßnahme; das Risiko-Profil verschlechtert sich mit
  jeder produktiven Stunde stillschweigend (Memory-Leak-Klasse),
  während der Fix klein und reversibel ist.
- **Vertrags-Erhalt (Pflicht-DoD):** Die Serialisierungs-Garantie
  („zwei parallele Aufrufe für denselben Key dürfen nicht denselben
  Base-Stand lesen und gegenseitig überschreiben") darf durch
  Eviction **nicht gebrochen werden**. Konkret:
  - **Idle-only-Eviction:** Ein `SemaphoreSlim`, an dem ein
    Aufrufer aktiv hält oder wartet (`CurrentCount == 0` oder
    Waiter-Count > 0), darf **nicht** evicted werden.
  - **Refcount- oder Waiter-aware Tracking:** Eviction-Kandidaten
    werden anhand „seit X Sekunden idle, kein aktiver Wait" gewählt,
    nicht anhand reinem LRU-Alter.
  - **Race-sicheres Re-Add:** Falls ein Aufrufer für einen
    just-evicted Key kommt, muss der `GetOrAdd`-Pfad einen neuen
    Semaphore liefern ohne dass ein paralleler Aufrufer den
    bereits-gelöschten Semaphore nochmal sieht.
  - **Concurrency-Test über die Eviction-Grenze:** Pflicht-Unit-
    Test, der zwei parallele Aufrufer auf denselben Key während
    eines Eviction-Sweeps fährt und beweist, dass sie weiterhin
    seriell ausgeführt werden.
- **Aufwand:** ~3-4 PT (Slice + gemeinsame Eviction-Logik +
  Metrik mit Label + Concurrency-Test + Dokumentation in
  `quality.md` §6 oder Persistenz-Doku).
- **Slice-Plan:** `plan-RM-M3-FUP-03.md` (entsteht in `open/` →
  `in-progress/` → `done/`).
- **Offene Entscheidungen:**
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
  (k3d-basiert, Compose-Smoke-Vorbild). Workflow-Integration
  bewusst **nicht als blockierendes Gate**, sondern als
  **path-filtered optionaler PR-Check** auf Änderungen unter
  `deploy/helm/**` plus `nightly`-Schedule auf `main`. Der
  „Gate"-Charakter (Pflicht-Lauf, PR-blocking, Release-Vorbedingung)
  wird **bewusst aufgeschoben** bis stabile Lauf-Serie nachgewiesen ist.
- **Begründung trotz fehlendem externen Trigger:** Helm-Lint-Render
  ohne Cluster-Apply ist kein Vertrag — der erste Operator, der
  `helm install` macht, könnte heute auf YAML-aber-nicht-Kubernetes-
  konforme Manifeste stoßen. Smoke kompensiert.
- **Aufwand:** ~3-4 PT (k3d-Setup im Workflow, Pod-Wait, Health-
  Probe, Tear-Down, Path-Filter + Nightly-Schedule).
- **Slice-Plan:** `plan-RM-M6-FUP-03-01.md`.
- **Promotion-Pfad** (explizit, damit „optional" nicht „dauerhaft
  optional" wird):
  1. **v1.1.0:** Path-filtered optional + nightly. Failures werden
     im Issue-Tracker erfasst, blocken aber keine PRs.
  2. **Nach 4 Wochen ununterbrochen grünem Nightly:** Job wird
     `required` für alle PRs (PR-blocking-Gate).
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

**Carve-out-Kandidat — braucht Sub-Slice-Auswahl vor Aktivierung.**

- **Quelle:** [`note-RM-M3-followups.md` Item 8](../open/note-RM-M3-followups.md).
- **Stand heute:** Telemetrie-Replay-Harness aus RM-M2-10 plus
  Solver-Replay aus M2 ✓. Vier konkrete Carve-out-Varianten sind
  als Folgearbeit dokumentiert:
  1. JSON-File-Loader für externe Replay-Datensätze
  2. Operator-Replay-CLI (Make-Target plus minimaler Wrapper)
  3. Multi-Asset-Replay-Koordination
  4. Compare-against-Production-Replay
- **Blocker (= offene Entscheidung für v1.1.0):** Welche der vier
  Varianten kommt in v1.1.0? Alle vier sind trigger-getrieben
  (externe Fixtures / Operator-Bedarf / Multi-Asset-Setup /
  Production-Vergleich).
- **Empfehlung:** **Keine** der vier in v1.1.0, weil keiner der
  Trigger heute aktiv ist. Wenn ein Sub-Slice in v1.1.0 wäre, dann
  am ehesten **(2) Operator-Replay-CLI** als „Quality-of-Life"-
  Maßnahme ohne externen Trigger — aber das verändert den
  v1.1.0-Charakter von „Internal Refinement" zu „neue Operator-
  Capability". Lieber separat triggern.

---

## Empfohlener v1.1.0-Scope (Stand 2026-05-14)

| Item | Kandidat | Entscheidung |
| ---- | -------- | ------------ |
| RM-M3-FUP-03 Lock-Eviction (beide Use Cases) | A | **In Scope** — präventiv, deckt beide unbounded `_locks`-Tabellen ab |
| F-M6-03-01 Cluster-Smoke (path-filtered + nightly, kein Gate) | B | **In Scope** — klein, deckt Lücke ab; Promotion zu Gate über mehrere Releases |
| M3-D3 PID-Routing               | C | **Aus Scope** — braucht Konsumenten-Entscheidung; v1.2+ |
| M3-FUP-04 Replay-Carve-outs     | D | **Aus Scope** — alle Varianten trigger-getrieben |

**Geschätzte Größe v1.1.0:** ~1-2 Wochen (2 Slices à 3-4 PT, plus
Release-Vorbereitung gemäß `releasing.md` §2).

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
- **Per-Asset-Sidecar/Worker-pro-Asset** (F-M6-02-01/02/03) —
  warten auf Isolation-Trigger
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
   gemeinsame TTL 24h + LRU 1000, idle-only mit Waiter-Awareness,
   Pflicht-Concurrency-Test über Eviction-Grenze).
3. **Lock-Eviction-Coverage:** Schedule + Intraday gemeinsam
   (Vorschlag) oder Intraday als separater Folge-FUP?
4. **Cluster-Smoke-Tool** für F-M6-03-01 (Vorschlag: k3d).
5. **Cluster-Smoke-Promotion-Pfad** bestätigen: path-filtered
   optional + nightly in v1.1.0 → PR-required nach 4 Wochen grün
   → `make ci`-Aufnahme nach weiteren 4 Wochen.
