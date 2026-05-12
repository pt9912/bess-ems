# Notiz: M5-Folgearbeiten (Trigger-Watch)

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen — Folgearbeiten zu aktiven M5-Slices, ohne Plan-Heimat im Master-Plan
**Bezug:**
[`../in-progress/plan-RM-M5.md`](../in-progress/plan-RM-M5.md) (Master-Slice-Plan),
[`../done/plan-RM-M5-01.md`](../done/plan-RM-M5-01.md) (Contract-Slice, abgeschlossen mit RM-M5-01-D),
[`../../adr/0005-optimization-core-sidecar-transport.md`](../../adr/0005-optimization-core-sidecar-transport.md)
(Transport-Adoption — Cert-Rotation, Multi-Tenant-Bearer-Token, Drittsprach-Sidecar erbt von §7 Phase-4-Pivot-Triggers),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md)

---

## Zweck

Während der M5-Umsetzung tauchen Folgearbeiten auf, die der jeweilige
Slice **bewusst draußen lässt** — entweder weil sie nicht-trivialen
Eigenscope haben (neuer Driven-Port, eigene PKI-Linie) oder weil ihr
Trigger noch nicht zündet. Damit sie beim nächsten
Trigger-Watch-Scan sichtbar bleiben — statt in Plan-Tabellen-
Kommentaren zu versinken — sind sie hier zentral geführt.

Konvention identisch zur M4-Folgearbeiten-Linie
(`note-RM-M4-followups.md`): die Items hier werden **nicht aktiv
abgearbeitet**, sondern bei jedem neuen M5-Slice-Plan, beim
quartalsweisen Architektur-Review oder beim ersten produktiven
Sidecar-Vorfall gescannt. Beim Zünden eines Triggers entsteht ein
eigener `plan-RM-M5-01-FUP-{slug}.md` in `open/`.

---

## Item F-M5-01: Cert-Rotation für mTLS-Cross-Host-Pfad

**Quelle:** plan-RM-M5-01 §9 + ADR 0005 §4. Analog zu F-18 aus M4-05.
Heute (Post-M5-01): `OptimizationCoreClient.ConnectAsync` materialisiert
den `GrpcChannel` einmalig; Cert-Lifecycle-Events (rotierte mTLS-
Client-Cert, vom Vendor neu ausgestellte Server-Cert) verlangen
Process-Restart. UDS-Default-Topologie hat das Problem nicht (kein
TLS) — der Pfad zündet erst, sobald ein Cross-Host-Sidecar gegen
HTTPS+mTLS gefahren wird.

**Trigger** (eines reicht):

- Erstes Cross-Host-Sidecar-Deployment fährt produktiv und ein
  Cert-Lifecycle-Event tritt auf (Validity-Period läuft ab,
  Operator-Team will Re-Trust ohne Restart).
- Compliance-Anforderung nach Hot-Reload des
  `TrustedServerCertificatesPath` oder `ClientCertificatePath`.
- Vendor rotiert sein Sidecar-Server-Cert proaktiv; bess-ems-Worker
  muss reload-en, bevor der bestehende Trust expired.

**Scope-Skizze** (wenn der Trigger zündet):

- (a) Cert-Watcher auf den Cert-Paths (`FileSystemWatcher` oder
  Polling) mit Throttle gegen Multi-Event-Bursts beim atomic-rename
  oder truncate-write.
- (b) `OptimizationCoreClient.ReloadCertificatesAsync()` rebuilt den
  internen `SocketsHttpHandler` mit dem neuen Client-Cert und ggf.
  einer aktualisierten `RemoteCertificateValidationCallback`-Closure
  gegen den refreshten Trust-Anchor. Bestehende In-Flight-Streams
  laufen aus; neue Calls landen auf dem refreshten Handler.
- (c) Pin-Test gegen einen HTTPS-fähigen Test-Sidecar (Folgelinie zum
  heutigen UDS-only TestSidecar): Server-Cert wird mid-stream
  ausgetauscht, alte Cert wird aus Client-Trust entfernt → Client
  soll Re-Trust laden + nächste RPCs erfolgreich fahren.
- (d) Optional: Pre-Expiration-Warning-Log (EventId 5121?) wenn die
  Client-Cert oder eine getrustete Server-Cert in N Tagen abläuft.

**Aufwandsschätzung:** grob 3-5 Tage inkl. Tests + Edge-Cases
(Cert-File-mid-write, atomic-rename-vs-truncate-detect). Plus eine
Vorab-Linie für den HTTPS-fähigen TestSidecar (heute UDS-only),
~1-2 Tage.

**Aktivierungs-Pfad:** eigener `plan-RM-M5-01-FUP-cert-rotation.md`
Slice-Plan.

---

## Item F-M5-02: Multi-Tenant-Bearer-Token-Auth

**Quelle:** plan-RM-M5-01 §3 + §9 + ADR 0005 §4. Heute (Post-M5-01):
`OptimizationCoreOptions.BearerTokenSource` ist als Slot vorgesehen
und im Adapter durchgereicht, aber **nicht aktiviert** — der Adapter
fährt mTLS (Cross-Host) oder UDS+Filesystem-Perms (Loopback) als
einzige Auth-Achse. Multi-Tenant-/Per-Operator-Bearer-Token-Auth
über gRPC-`CallCredentials` ist explizit Folge-Linie.

**Trigger** (eines reicht):

- Zweite Operator-Instanz oder Multi-Tenant-Future-Pfad zündet —
  ein einzelner mTLS-Trust ist nicht mehr ausreichend, um Per-
  Operator-Sessions auseinanderzuhalten.
- TSO-/Vendor-Spec verlangt zusätzlich zur Cert-Trust einen Bearer-
  Token (z. B. OAuth2-Token-Endpoint oder Vault-issued JWT).
- Compliance-Audit verlangt Audit-fähiges Per-User-Session-Logging
  auf der Sidecar-Wire (Cert-Trust mappt nur auf einen Service-
  Account, nicht auf einen Operator-Account).

**Scope-Skizze** (wenn der Trigger zündet):

- (a) `IOptimizationCoreTokenProvider`-Driven-Port (Application-
  Schicht) — `Task<string?> GetTokenAsync(CancellationToken)` —
  mit Default-Implementation Environment-/File-basiert; Production-
  Implementation gegen Vault o.ä. als eigene Carve-out-Linie.
- (b) `OptimizationCoreClient` baut `CallCredentials.FromInterceptor`
  beim Channel-Build wenn der Token-Provider non-null ist; setzt
  `authorization: Bearer <token>`-Metadata pro Call. Token-Refresh
  liegt beim Provider (TTL-aware-Cache).
- (c) Per-Tenant-Konfiguration: `OptimizationCoreOptions` bekommt
  einen optionalen `TenantId`-Slot, der in der Token-Anfrage
  durchgereicht wird; Adapter ist Multi-Instanz-fähig (ein Adapter
  pro Tenant).
- (d) Pin-Test gegen Test-Sidecar mit Token-Validierungs-Interceptor:
  korrektes Token → Optimize-Success; fehlendes Token →
  `unauthorized_client`-Outcome (existierender Mapper-Eintrag).
- (e) Doku-Update in `docs/user/quality.md` §2.2.3 zum neuen
  Auth-Pfad plus `note-RM-M5-followups.md`-Verweis bei Closure.

**Aufwandsschätzung:** ~1 Woche für den Adapter-Pfad inkl. Tests.
Production-Token-Provider (Vault o.ä.) zusätzliche ~1 Woche, eigene
Linie. Analog zur OPC-UA-F-19-User-Identity-Linie aus M4-05.

**Aktivierungs-Pfad:** eigener `plan-RM-M5-01-FUP-bearer-token.md`
Slice-Plan.

---

## Item F-M5-03: Drittsprach-Sidecar-Produktiv-Implementierung

**Quelle:** plan-RM-M5-01 §3 Out-of-Scope + §9 + ADR 0005 §7
(Phase-4-Pivot-Trigger). Heute (Post-M5-01): der TestSidecar in
`tests/integration/BatteryEms.OptimizationCore.IntegrationTests/`
ist ein in-process `Grpc.AspNetCore`-Stub mit echten Domain-Mocks,
aber **kein produktionsnaher Sidecar-Container** mit HiGHS/OR-Tools/
SCIP-Backend. M5-01 liefert nur den Wire-Vertrag plus Wire-Adapter;
ein echter Drittsprach-Sidecar-Container ist explizit eigene Linie.

**Trigger** (eines reicht):

- Erster konkreter HiGHS- oder OR-Tools-Backend-Wunsch (z. B. ein
  Operator will einen produktionsnahen LP/MIP-Solver hinter dem
  Sidecar fahren statt den heutigen NoOp-/Echo-Pfad).
- RM-M5-02 (MPC-Kernel) braucht ein State-Space-Backend, das in
  einem separaten Prozess laufen muss (z. B. wegen GIL bei Python-
  CVXPY oder wegen Crash-Isolation bei nativen C++-Solvern).
- Container-Orchestrierungs-Gate (RM-M5-06) verlangt ein Sidecar-
  Image, das gegen ein echtes Solver-Backend fährt.

**Scope-Skizze** (wenn der Trigger zündet):

Drei Sub-Linien, die zusammen die F-M5-03-Lieferung definieren:

- **(a) Sprach-Pivot-Diskussion + Backend-Wahl**: eigene Carve-out-
  ADR (analog zu ADR 0005). Kandidaten: Python (CVXPY + HiGHS/SCIP/
  Gurobi-Wrapper; bequem für MPC-Prototyping), Rust (Highs-Bindings
  + `tonic` für gRPC; ABI-stabil, kompiliert deterministisch),
  C++ (CMake + gRPC + Solver-of-Choice; höchste Performance, aber
  Aufwand). Operator-Lifecycle-Kosten (Container-Image-Build,
  Vulnerability-Tracking, Solver-License-Workflow) sind Teil der
  Entscheidung — siehe ADR 0005 §7 Phase-4-Pivot-Trigger
  (Drittsprach-Solver-Lifecycle-Kosten als expliziter In-Process-
  Embed-Pivot-Auslöser).
- **(b) Schwesterprojekt-Skelett**: entweder `bess-optimization-
  core/` als Schwester-Repo oder `optimization-core/sidecar/` als
  Subverzeichnis. Buf-Lint-Gate für die Proto-Vertrag-Disziplin
  (RM-M5-06-Carve-out aus plan-RM-M5-01 §7 Risiken).
- **(c) Production-Smoke-Linie**: `tests/hil/compose.yml`-
  Erweiterung um einen Sidecar-Container-Service. Smoke-Pin in
  einem neuen `BatteryEms.OptimizationCore.E2ETests`-Projekt (oder
  als Compose-Stack im RM-M5-06-Container-Gate aufgehoben).

**Aufwandsschätzung:** Sprach-abhängig. Python-CVXPY-Wrapper grob
2-3 Wochen für ein Backend-Skelett mit einem Solver + LP-Surface;
Rust-Highs-Wrapper grob 3-4 Wochen (höherer Boilerplate, aber
deterministischer Build). Diese Schätzung ist nur das Skelett —
ein produktionsnahes Backend mit Telemetrie, Crash-Recovery und
Image-Build-Pipeline ist eigene Monatslinie. Diese Schätzung wird
beim Trigger-Watch-Scan revalidiert, weil die LOC-Range stark vom
Solver-Wrapper-Stand der Drittsprache abhängt.

**Aktivierungs-Pfad:** Sprach-Pivot-ADR zuerst (eigene Folge-ADR im
`docs/plan/adr/`-Verzeichnis — die ursprünglich vorgemerkte Nummer
0006 ist mittlerweile durch ADR 0006 (MPC-Kernel-Backend-and-Solver)
belegt, die nächste freie Nummer ist 0007 oder höher), dann eigener
Slice-Plan `plan-RM-M5-01-FUP-third-language-sidecar.md` oder
Schwester-Repo-Init.

---

## Item F-M5-05: „Letzter-bekannter-Plan"-Fallback aus IScheduleRepository

**Quelle:** plan-RM-M5-01 §5.1 Korrektur-Pass-Scope-Cut. Heute (Post-
Korrektur): wenn der Sidecar fehlschlägt UND der lokale OR-Tools-
Fallback nicht konfiguriert ist ODER auch er fehlschlägt, geht der
Adapter direkt auf `no_valid_plan` + Safe-Stop. Plan-RM-M5
§Fallback-Matrix erwähnt aber zusätzlich:
> Der Regelkreis nutzt nur einen frischen, kontextkompatiblen
> Fahrplan; fehlt dieser, erzeugt der Control-Pfad Safe-Stop

Das deutet auf einen sekundären Fallback-Pfad „letzter bekannter
Plan aus der Persistenz", den der Plan-Validator dann gegen die
4-Achsen-Regeln prüfen würde. Im Korrektur-Pass bewusst ausgeklammert,
weil ein direkter `IScheduleRepository`-Zugriff vom Adapter aus
Hexagonal-Verletzung wäre (Adapter zieht Domain-Repo statt Application-
Use-Case zu konsultieren) und die Use-Case-Schicht heute keinen
„Plan-from-Repo"-Lookup als Driven-Port exposed.

**Trigger** (eines reicht):

- Erste Operator-Anforderung nach Plan-Persistenz als Backup-Quelle,
  wenn weder Sidecar noch lokaler Fallback verfügbar sind (z. B.
  unkonfigurierter OR-Tools-Slot in einer minimalen Topologie).
- RM-M5-02 (MPC-Kernel) braucht den Pfad als Default-Recovery-
  Strategie wenn der State-Space-Solver-Lauf scheitert.
- Telemetrie zeigt eine operativ relevante Rate von
  no-valid-plan-Safe-Stops, die durch einen letzten gültigen Plan
  vermeidbar wären.

**Scope-Skizze** (wenn der Trigger zündet):

- (a) Neuer Application-Driven-Port `ILastKnownScheduleProvider`
  mit `Task<FallbackPlanCandidate?> GetLatestAsync(string assetId,
  ScheduleType type, CancellationToken ct)` — kapselt den
  `IScheduleRepository.GetByAssetAsync`-Aufruf plus
  `IOptimizationRunRepository.GetLatestByScheduleAsync` für die
  CreatedAt-Stamp-Auflösung.
- (b) `OptimizationCoreScheduleOptimizer` bekommt einen weiteren
  optionalen Slot; Pfad in `TryRunFallbackAsync` erweitert: wenn
  primary-Fallback (OR-Tools) fehlt/scheitert, versuche
  ILastKnownScheduleProvider → Plan-Validator-Check → bei pass
  `FallbackCommitted` mit `local_plan_replayed`-source, bei fail
  no-valid-plan.
- (c) 3-4 zusätzliche Pins (last-known-plan-pass, last-known-plan-
  rejected-by-validator, last-known-plan-not-found,
  last-known-plan-priority-over-or-tools-or-vice-versa).

**Aufwandsschätzung:** ~3-5 Tage inkl. Tests + Application-Schicht-
Erweiterung.

**Aktivierungs-Pfad:** eigener `plan-RM-M5-01-FUP-last-known-plan-
fallback.md` Slice-Plan.

---

## Item F-M5-04: Schedule-Stempel-Erweiterung für Plan-Gültigkeits-Check

**Quelle:** plan-RM-M5-01 §7 Risiko-Punkt + §9. Heute (Post-M5-01):
`DefaultFallbackPlanValidator` vergleicht beim 4-Achsen-Check
(Context-Stamp → Horizon-Alignment → MaxAge → Telemetrie-Drift)
nur die Felder, die heute auf `Schedule` und `BatteryAsset`
materialisiert sind (`assetId`, `scheduleType`, `marketBidArea`,
`version`, `horizonStart`, `horizonEnd`, `timeStep`). Plan-RM-M5
§Fallback-Plan-Gueltigkeit fordert breiter — `constraint-version`,
Market/Reserve-Kontext, Telemetrie-Snapshot-Identität — und der
heutige Validator reduziert das pragmatisch auf das, was die
M2-`Schedule`-Domain trägt.

**Trigger** (eines reicht):

- Erster konkreter Operator-Vorfall, in dem ein Fallback-Plan
  akzeptiert wurde, der semantisch nicht mehr passt (z. B. die
  Constraint-Spec hatte sich geändert, aber der `Schedule`-Stempel
  hat die Änderung nicht reflektiert).
- TSO-/Compliance-Anforderung verlangt einen expliziten
  `constraint-version`-Field im Plan-Audit (z. B. „bei welcher
  Constraint-Revision wurde dieser Plan erzeugt?").
- RM-M5-02 (MPC-Kernel) braucht ein zusätzliches Stempel-Feld
  (z. B. State-Space-Snapshot-Hash, Kalman-Filter-Initial-Stamp),
  das im 4-Achsen-Check mit verglichen werden muss.

**Scope-Skizze** (wenn der Trigger zündet):

- (a) `Schedule`-Domain-Erweiterung: neuer `constraint_version`-
  Slot (text, default `"1.0.0"`), neue Market/Reserve-Kontext-
  Stempel falls erforderlich. Migration `0004_schedule_stamp_
  extension.sql` plus schema.yaml-Update.
- (b) `DefaultFallbackPlanValidator.CheckContextStamp` erweitert
  um die neuen Felder; `FallbackPlanValidatorOptions` bekommt
  Toleranz-Slots für die zusätzlichen Achsen wo sinnvoll.
- (c) Pin-Erweiterung in `FallbackPlanValidatorTests`:
  Constraint-Version-Mismatch → `fallback-context-mismatch`-
  Reason; weitere Stempel-Achsen je nach Trigger.
- (d) Backward-Compat: Pre-Migration-Schedules ohne Stempel-Felder
  laufen mit Default-`"1.0.0"`-Stempel weiter; ein Plan-Gültigkeits-
  Check gegen einen Pre-Migration-Plan ist tolerant (Stempel-Match
  „bei Default-Default").

**Aufwandsschätzung:** grob 3-5 Tage für die ersten zusätzlichen
Stempel-Felder inkl. Migration, Domain-Erweiterung, Validator-
Erweiterung, Pin-Update. Größerer Aufwand wenn parallel die
M2-Optimizations-Output-Surface die Stempel-Felder produzieren
muss (~+2-3 Tage).

**Aktivierungs-Pfad:** eigener `plan-RM-M5-01-FUP-schedule-
stamp.md` Slice-Plan oder Carve-out im RM-M5-02-MPC-Kernel-Slice
wenn MPC der Trigger ist.

---

## Item F-M5-12: Sidecar-First-MPC-Backend (zweiter `IMpcModelSolver`-Adapter)

**Quelle:** [ADR 0006](../../adr/0006-mpc-kernel-backend-and-solver.md)
§6 (Trigger für Backend-Pivot) +
[`../in-progress/plan-RM-M5-02.md`](../in-progress/plan-RM-M5-02.md)
§5 D-02 + §9 Folgearbeiten-Block. Heute (Post-RM-M5-02): MPC läuft
ausschließlich in-process via `LocalOsqpMpcSolver` (Sub-Slice-B-
Lieferung); `BessHostOptions.MpcBackend = "optimization_core"` und
`"bi_modal"` sind reservierte Slot-Werte, die zum Startup-Fehler
`mpc-backend-not-implemented` führen. Ein zweiter, Sidecar-basierter
Solver-Adapter wäre die natürliche Ergänzung wenn einer der unten
genannten Trigger zündet.

**Trigger** (eines reicht, jeder erzwingt eigene Folge-ADR):

- **`sample_time < 10 ms` im operativen Profil** würde Sidecar-First
  ausschließen (Roundtrip-Overhead untolerierbar); F-M5-12 darf in
  diesem Fall **nicht** aktiviert werden. Trigger zielt auf den
  umgekehrten Fall: solange `sample_time >> Sidecar-Roundtrip-p99`
  bleibt, ist Sidecar-First eine Option.
- **Solver-Isolationspflicht.** OSQP-Crash wird in Produktion zur
  operativen Quelle (Plan-RM-M5-02 §7 Risiken-Block nennt das als
  Sub-Slice-B-Risiko; ADR 0004 §4 Trigger 6 = Crash-Isolation ist
  die generische Linie) ODER Operator-Anforderung nach Solver-
  Sandbox (Audit, Sicherheits-Zertifizierung). Sidecar-First wird
  dann „Sidecar primary, in-process-Local-OSQP als Fallback".
- **Multi-Language-Solver-Anforderung.** Operator-Wunsch nach einem
  Python-/cvxpy-/Stan-/JAX-basierten Solver (wahrscheinlicher
  Trigger-Vorbote: F-M5-08 Stochastic-MPC). Sub-Slice-B-Local-OSQP
  bleibt produktiv für die LTI-Linie; F-M5-12 liefert den zweiten
  Backend für die Drittsprach-Linie.
- **Asset-spezifische Operator-Backend-Wahl.** Multi-Asset-Workflow
  mit Asset A: Local-OSQP, Asset B: Sidecar mit Python-Solver. Löst
  zusätzlich Bi-Modal-Folge-ADR aus (Operator-UX-Achse). F-M5-06
  Multi-Asset-MPC ist der wahrscheinliche Trigger-Vorbote.
- **Container-/Pod-Co-Location-Constraint.** Sidecar-Container ist
  ohnehin im Deployment (z. B. weil M5-01-Sidecar in derselben Pod
  läuft) UND Worker-Container-Image-Größe wird relevant (OSQP-Binary
  + Dependencies vs. „nichts mehr im Worker").

**Scope-Skizze** (wenn ein Trigger zündet):

- (a) Eigene **Folge-ADR** (Nummer 0007 oder höher) mit Migrations-
  Plan: welche Trigger zünden, welche Topologie wird gewählt,
  welche Operator-UX-Pflichten kommen mit, wie wird `MpcBackend`-
  Slot-Vokabular erweitert. Pivot ändert nicht silent ADR 0006.
- (b) Zweiter `IMpcModelSolver`-Adapter `OptimizationCoreMpcOptimizer`
  in einem neuen `BatteryEms.Adapters.Optimization.Mpc.Sidecar`-
  Namespace neben dem bestehenden `Local`-Namespace. Adapter
  konsumiert die M5-01-Wire-Infrastruktur (`OptimizationCoreClient`,
  `OptimizationCoreOptions`, `transport-mapping-v1.md`) und füllt
  den heute nur vertragenen `OptimizeMpc`-RPC (RM-M5-01 D-08).
- (c) `IMpcDispatchOptimizer`-DI-Wiring im Composition-Root erweitert:
  - `MpcBackend = "optimization_core"` ⇒ `OptimizationCoreMpcOptimizer`
    als primary; `LocalOsqpFallbackMpcOptimizer` (oder die Sub-Slice-
    C-`IFallbackMpcOptimizer`-Linie) als in-process-Fallback.
  - `MpcBackend = "bi_modal"` bleibt entweder reserviert (eigene
    Folge-ADR wenn der Bi-Modal-Operator-UX-Trigger zündet) oder
    aktiviert beide Adapter mit `MpcOptions.PreferredBackend`-Slot
    (je nach Folge-ADR).
- (d) 8+ zusätzliche Roundtrip-Pins gegen den `OptimizationCoreMpcOptimizer`
  (analog zur Sub-Slice-B-Linie für `LocalOsqpMpcSolver`), plus
  TestSidecar-MPC-Stubs (`OptimalMpcStub` + `ScriptableMpcOutcomeStub`)
  für den In-Process-Test-Pfad — diese Stubs wurden bewusst aus dem
  M5-02-B-Scope geschnitten (siehe ADR 0006 §3 Verworfene
  Alternativen).
- (e) Boot-Gate-Erweiterung: wenn `MpcBackend = "optimization_core"`
  und `RuntimeProfile=Production`, dann ist `OptimizationCoreOptions`
  Pflicht (analog zum M5-01-LP-Adapter-Boot-Gate); fehlt der Slot,
  Startup-Fehler `mpc-sidecar-without-config`.

**Aufwandsschätzung:** ~2-3 Wochen (Trigger-abhängig). Wire-Linie
reuse aus M5-01 (Channel, Idempotency-Store, Fallback-Validator)
verkürzt den Schätzer; produktionsnahes Sidecar-MPC-Backend (z. B.
Python-CVXPY-basiert) ist eigene Linie und triggert F-M5-03
(Drittsprach-Sidecar-Produktiv-Implementierung) als parallele
Schwester-Linie.

**Aktivierungs-Pfad:** Folge-ADR (`docs/plan/adr/0007-mpc-sidecar-
backend.md` o.ä.) zuerst, dann eigener Slice-Plan
`plan-RM-M5-02-FUP-sidecar-backend.md` als Erweiterung des
M5-02-MPC-Kernel-Slice. Note-Eintrag hier wird beim Aktivierungs-
Commit auf den neuen Slice-Plan verlinkt.

---

## Trigger-Watch-Disziplin

Diese Notiz wird **nicht aktiv abgearbeitet**. Sie wird gescannt:

- Beim Beginn jedes neuen M5-Slice-Plans (insbesondere RM-M5-02
  MPC-Kernel, RM-M5-04 Replay-Plattform, RM-M5-06 Container-
  Orchestrierungs-Gate).
- Beim quartalsweisen Architektur-Review.
- Bei jedem Production-Vorfall-Postmortem rund um Sidecar-Pfad
  oder Fallback-Plan-Aktivierung.

Beim Zünden eines Triggers:

1. Item aus dieser Notiz extrahieren.
2. Eigenen Slice-Plan in `docs/plan/planning/open/` anlegen (Name
   `plan-RM-M5-01-FUP-{slug}.md`).
3. Item-Eintrag hier mit Verweis auf den neuen Plan markieren oder
   nach Abschluss entfernen.
4. Roadmap-„Aktueller Stand"-Block ergänzen.

So bleibt die Trigger-Liste lebendig statt in Plan-Tabellen-
Kommentaren zu verschwinden.
