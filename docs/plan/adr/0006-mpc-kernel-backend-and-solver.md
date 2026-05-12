# ADR 0006 — MPC-Kernel: Backend-Topologie (Local-First) und Solver (OSQP)

**Status:** Accepted — Local-First mit OSQP als QP-Solver für den
MPC-Kernel; einziger produktiver MPC-Pfad in Sub-Slice
[`RM-M5-02-B`](../planning/done/plan-RM-M5-02.md). Schließt
die D-02-Achse aus
[`plan-RM-M5-02.md`](../planning/done/plan-RM-M5-02.md) §5.
**Datum:** 2026-05-12
**Bezug:**
[`../planning/done/plan-RM-M5-02.md`](../planning/done/plan-RM-M5-02.md)
(§2 Aktivierungsbedingungen Zeile „ADR 0006", §5 D-02 Solver-Backend-
Wahl, §5 D-07 Fallback-Linie, §9 Folgearbeiten),
[ADR 0005 — Optimization-Core Sidecar: Transport (gRPC)](0005-optimization-core-sidecar-transport.md)
(§7 Phase-4-Pivot-Trigger — der 1-ms-Latenz-Bound aus Trigger 1 ist
Grenze zwischen Local-First und Sidecar-First für die MPC-Linie),
[ADR 0004 — Native Control Kernel: Process Isolation](0004-native-kernel-process-isolation.md)
(§4 Trigger 7 Performance-Solver — generischer Performance-Trigger,
hier konkretisiert für die MPC-QP-Solver-Surface),
[`../planning/done/plan-RM-M5-01.md`](../planning/done/plan-RM-M5-01.md)
(M5-01-Sidecar-Linie ist Reuse-Kandidat für F-M5-12 wenn der Pivot
zündet),
[`../planning/open/note-RM-M5-followups.md`](../planning/open/note-RM-M5-followups.md)
(F-M5-12 — deferred Sidecar-First-Linie).

---

## 1. Kontext

Plan
[`plan-RM-M5-02.md`](../planning/done/plan-RM-M5-02.md)
ist der MPC-Backend-Slice für den optimization-core. §5 D-02 hat die
Solver-Backend-Wahl als **offene Pflicht-Entscheidung vor Sub-Slice-B-
Start** geführt mit drei Varianten:

- **(a) Local-First** — QP-Solver in-process im Worker; Sidecar opt-in
  über eigenen Slot.
- **(b) Sidecar-First** — `OptimizeMpc`-RPC aus M5-01 ist primary;
  lokaler Fallback erbt sich aus der M5-01-Korrektur-Pass-Linie.
- **(c) Bi-Modal** — beide Backends gleichberechtigt registriert,
  Operator wählt per `BessHostOptions.MpcBackend`-Slot.

Der Plan-Default-Vorschlag (§5 D-02 letzter Absatz) war (c) Bi-Modal
mit Local-First-Default; gleichzeitig betont der Scope-Cut in §1 dass
RM-M5-02 **nur** State-Space-LTI + Kalman + Constraints liefert.
Bi-Modal-Coordination ist nicht im Cut explizit drin, und Sub-Slice-B
DoD-Forderung „8+ Roundtrip-Pins" wird bei Bi-Modal pro Backend zur
Pflicht — das Pin-Volumen verdoppelt sich.

MPC läuft auf Sub-Sekunden-Tick (Plan §3 `sample_time` 250 ms–1 s als
Standard-Range; spezifische Asset-Linien können auch 100 ms ansteuern).
Plan §7 Risiken-Block macht den Latenz-Korridor explizit: gRPC-UDS-
Roundtrip-p50 200 µs–2 ms (siehe
[ADR 0005](0005-optimization-core-sidecar-transport.md) §3) liegt
**innerhalb** der MPC-Sample-Time-Range, ist aber pro-Tick-Overhead
auf einer 4-Hz-Linie nicht vernachlässigbar (250 ms Tick · 2 ms p99 ≈
0,8 % Tick-Time pro Roundtrip). Mit harten MPC-Latenz-Anforderungen
(z. B. Primärregelleistung 100 ms) kippt der Sidecar-Pfad aus dem
Korridor — siehe
[ADR 0005](0005-optimization-core-sidecar-transport.md) §7 Trigger 1
(1-ms-Latenz-Bound).

Diese ADR schließt D-02 mit (a) Local-First + OSQP, dokumentiert die
**schmale Backend-Abstraktion** als Designvorgabe damit (b) Sidecar-
First später als F-M5-12 ergänzt werden kann ohne (c) Bi-Modal
aufzubauen, und benennt die konkreten Trigger für den Pivot.

---

## 2. Entscheidung

| Achse | Entscheidung | Pin / Trigger |
| ----- | ------------ | -------------- |
| Backend-Topologie | **(a) Local-First** — QP-Solver läuft in-process im Worker. `BessHostOptions.MpcBackend = "local_osqp"` ist der Sub-Slice-D-Aktivierungs-Default. | Sub-Slice-B liefert genau **einen** `IMpcModelSolver`-Adapter (`LocalOsqpMpcSolver`); Sub-Slice-D Default-Bootstrap-Pin pinnt `MpcBackend == "local_osqp"` als einzigen Wert, der `IMpcDispatchOptimizer` im DI-Container registriert. `"optimization_core"` und `"bi_modal"` sind **reservierte Namen**, die heute zu einem `mpc-backend-not-implemented`-Startup-Fehler führen — der Slot bleibt für F-M5-12 frei. |
| QP-Solver | **OSQP** (Operator-Splitting QP, ADMM-basiert, MIT-Lizenz) hinter der `IMpcModelSolver`-Schnittstelle. | Sub-Slice-B Solver-Adapter `LocalOsqpMpcSolver`; `solver_config_hash` (D-09 Identity-Tuple) hasht OSQP-Version + Threading-Flag + Polish-Flag + Eps-Abs/Rel + Max-Iter. |
| Backend-Abstraktion | `IMpcModelSolver`-Driven-Port bleibt **die einzige Solver-Surface**; der Adapter hängt am Konfigurations-Slot `MpcBackend`. Ein zweiter Adapter (Sidecar-Pfad) wird **nicht** in Sub-Slice B angelegt. Plan §5 D-07 `IFallbackMpcOptimizer`-Driven-Port bleibt orthogonal — Fallback ist in-process, nicht Sidecar. | Sub-Slice-B Architektur-Pin: nur ein Solver-Adapter im `BatteryEms.Adapters.Optimization.Mpc`-Namespace; F-M5-12 öffnet den zweiten Adapter. |
| Fallback-Pfad | `IFallbackMpcOptimizer`-Driven-Port (D-07 Plan §5) bleibt in-process; **kein** M5-01-Sidecar-Fallback-Reuse für den MPC-Pfad. | Sub-Slice-C Fallback-Wire-Integration-Pin verlangt `IFallbackMpcOptimizer` im DI-Container wenn `MpcBackend != null` und `RuntimeProfile=Production` (Plan §6 Acceptance-Criterium). |
| Non-Goal Sub-Slice B | **Kein gleichberechtigter Sidecar-Backend-Pfad.** `BessHostOptions.MpcBackend = "optimization_core"` ist Startup-Fehler `mpc-backend-not-implemented`; `MpcBackend = "bi_modal"` ebenfalls. | Sub-Slice-B Negativ-Pin: Boot mit reserviertem Namen ⇒ DI-Build-Fehler mit dem konkreten Reason-Code. |
| Boot-Gate-Reihenfolge | **Backend-Validation läuft als erste MPC-Boot-Gate-Achse.** Reservierte `MpcBackend`-Werte zünden `mpc-backend-not-implemented` **vor** allen anderen Production-Gates (Fallback aus D-07, Monotonic-Clock aus D-09). Sonst sähe der Operator unter `RuntimeProfile=Production` den falschen Reason-Code (`mpc-production-without-fallback-pathway` statt `mpc-backend-not-implemented`). | Sub-Slice-D Boot-Gate-Reihenfolge-Pin (Plan §4 Sub-Slice-D-DoD); Sub-Slice-B Composition-Root-Test bestätigt die Reihenfolge bereits beim Adapter-Wireup. |
| Deferred (F-M5-12) | **Sidecar-First-MPC-Backend** wird als eigene Folgearbeit gepflegt (`note-RM-M5-followups.md` F-M5-12) mit konkreten Triggern (§6 unten). Bei Aktivierung: zweiter Adapter `OptimizationCoreMpcOptimizer` neben `LocalOsqpMpcSolver`, `MpcBackend = "optimization_core"` aktiviert ihn, `IFallbackMpcOptimizer` wird Local-OSQP-Adapter. | F-M5-12 lädt eine eigene ADR-Linie (0007 o.ä.) wenn ein Trigger aus §6 zündet — nicht silent erweitern. |
| Pivot-Kriterien | **Trigger-getrieben** (§6 unten); jeder einzelne Trigger erzwingt eine neue ADR (Pivot ändert nicht silent ADR 0006). | F-M5-12-Trigger-Watch ist Pflicht in jeder Sub-Slice-D-Closure-Checkliste — siehe §8 Sequenz Punkt 5. |

---

## 3. Achse 1 — Backend-Topologie-Optionen

### Local-First (gewählt für RM-M5-02-B)

Konkrete Wins für die **MPC-/QP-Sub-Sekunden-Surface**:

Geordnet nach Trade-off-Gewicht (treibende Achsen zuerst):

- **Sub-Slice-B Pin-Volumen (treibend).** Plan §4 Zeile RM-M5-02-B
  fordert „8+ Roundtrip-Pins". Mit Local-First sind das 8 Pins für
  **einen** Adapter; mit Bi-Modal würden 16 Pins fällig (8 pro
  Backend), mit Sidecar-First plus in-process-Fallback ebenfalls.
  Pin-Volumen ist direkt Sub-Slice-B-Risiko (Plan §4 LOC-Schätzung
  800–1200; Bi-Modal würde diese Linie sprengen).
- **Pflichtpfad-Komplexität (treibend).** Plan-§7-Risiken-Block
  nennt „Sidecar healthy + gRPC ok + Idempotency ok" als drei
  Voraussetzungen pro MPC-Tick im Sidecar-First-Pfad. Local-First
  reduziert das auf „Solver-Adapter loaded" — Plan §6 Acceptance-
  Criterium `mpc-production-without-fallback-pathway` ist die
  einzige Boot-Gate-Achse, die noch greift.
- **Coherent Fallback-Linie (treibend).** Plan §5 D-07 verlangt
  `IFallbackMpcOptimizer` als Driven-Port. Mit Local-First ist
  Primary und Fallback **beide in-process** — ein einziger Solver-
  Engine-Bug kann beide treffen, aber die operative Linie ist
  einfacher (keine Cross-Process-Fallback-Race). Bei Sidecar-First
  würde der Fallback eine zweite Solver-Engine-Linie sein (in-
  process) — doppelte Pflege.
- **CVE-Lifecycle synchron mit Worker.** Plan §7 nennt OSQP-Solver-
  Bugs als Sub-Slice-B-Risiko. OSQP-Updates ziehen mit dem Worker-
  Container-Build mit; kein zweiter Sidecar-Container mit eigenem
  Patch-Window.
- **Latenz-Korridor (sekundär; allein nicht entscheidend).** Die
  gRPC-Roundtrip-Latenz ist auf der MPC-Tick-Frequenz tolerierbar
  — M5-01-LP-Linie hat denselben p50-Korridor (200 µs – 2 ms) als
  „im Rauschen" akzeptiert (siehe ADR 0005 §3 Latenz-Hit-Block).
  Für `sample_time = 250 ms` sind 2 ms p99 etwa 0,8 % Tick-Time;
  bei `sample_time = 100 ms` etwa 2 %. Allein wäre das kein
  Pivot-Grund — die Achse zählt erst harmonisch mit den treibenden
  drei (Pin-Volumen + Pflichtpfad-Komplexität + Fallback-Kohärenz).
  Für `sample_time < 10 ms` (ADR 0005 §7 Trigger 1) kippt die
  Latenz-Linie alleinstehend; bis dahin ist sie ein Begleitargument,
  nicht das Hauptargument.
- **Sub-Slice-B Solver-Time-Bound (Begleitargument).** OSQP-Solve-
  Time für ein typisches BESS-Modell (1–8 States, Horizon 16–32
  Schritte, 10–50 Constraints) liegt in der Größenordnung 100 µs
  – 5 ms in-process; Sample-Time-Headroom für künftige Verschärfung
  ist gegeben ohne ADR-Pivot.

Konkrete Trade-offs:

- **Native-Binary im Worker-Process.** OSQP wird via P/Invoke
  eingebunden (`libosqp.so` aus dem OSQP-NuGet-Wrapper-Paket — analog
  zur OR-Tools-Linie aus M2). Solver-Crash ist Worker-Crash; ADR 0004
  §4 Trigger 6 (Crash-Isolation) bleibt als Folge-Trigger offen wenn
  OSQP-Solver-Crashes in Produktion auftreten.
- **Solver-Universum begrenzt.** Drittsprach-Solver (cvxpy-Wrapper,
  Python-spezifische Linien) sind nicht direkt erreichbar. Operator-
  Wunsch nach einem Python-Solver wäre ein konkreter F-M5-12-Trigger
  (§6 Punkt 3).
- **Plattform-Bindings.** OSQP-Binary muss für jedes Worker-
  Container-Target gebaut/eingebunden werden. Linux-x64 ist der
  M5-Default-Target — Sub-Slice B liefert den Linux-x64-Binary; ARM64
  / Windows folgen wenn Operator-Trigger zündet (Plan §7 nennt nicht-
  Linux-Targets nicht).

### Verworfene Alternativen

#### Sidecar-First — verworfen für Sub-Slice B

Begründung gegen Sub-Slice-B-Adoption (akzeptiert als Folgearbeit
F-M5-12):

- **Roundtrip-Overhead auf Sub-Sekunden-Linie.** Siehe §1 oben —
  bei `sample_time = 100 ms` werden 2 ms gRPC-p99 zu 2 % Tick-Time,
  und Tick-Slip-Risiko steigt nicht-linear (Plan §7 Risiken nennt
  „Tick-Drift" als Sub-Slice-C-Pflicht für `MonotonicAnchoredClock` —
  der Sidecar-Roundtrip-Jitter wäre ein zweiter Drift-Vektor neben
  der Wall-Clock-Linie).
- **Sub-Slice-B-DoD-Volumen.** Plan §4 verlangt 8+ Roundtrip-Pins für
  den primären Backend + `TestSidecar`-MPC-Stubs (`OptimalMpcStub`
  + `ScriptableMpcOutcomeStub`). Sidecar-First würde das zusätzlich
  zur in-process-Fallback-Linie verlangen — die LOC-Schätzung des
  Plans (800–1200) reicht dafür nicht.
- **Pflichtpfad-Healthy-Gate.** Plan §6 Acceptance-Criterium
  `mpc-production-without-fallback-pathway` ist Boot-Gate. Mit
  Sidecar-First wird der Healthy-Check pro Tick zur zweiten Achse;
  Plan §6 Sidecar-Status-Taxonomie aus M5-01 müsste auf MPC-Tick-
  Frequenz adaptiert werden — eigener Komplexitätsblock.
- **Reuse-Argument.** M5-01-Sidecar-Linie liefert Channel +
  Idempotency-Store + Fallback-Validator + TestSidecar — alles
  wiederverwendbar. Der **strukturelle** Reuse bleibt für F-M5-12
  erhalten (siehe §5 unten — die `IMpcModelSolver`-Abstraktion ist
  schmal genug). Der **operative** Reuse (Sidecar als zweiter
  Backend) ist nicht im Sub-Slice-B-Scope-Cut.

Bleibt als F-M5-12-Folgearbeit; konkrete Trigger §6.

#### Bi-Modal — verworfen ohne Folge-Linie

Begründung gegen Sub-Slice-B-Adoption:

- **Doppeltes Pin-Volumen.** Plan §4 Sub-Slice-B-LOC-Schätzung (800–
  1200) wird gesprengt; Plan-Review-Pässe 1–5 haben den Cut bewusst
  schmal gehalten.
- **Scope-Creep gegen Plan §1 Scope-Cut.** „macht NUR State-Space-
  LTI + Kalman + Constraints" — Bi-Modal-Coordination
  (Primary/Fallback-Reihenfolge per Asset, Per-Tick-Backend-Switch,
  Latenz-Telemetrie pro Backend) ist eine eigene Achse.
- **Operator-UX-Frage offen.** Bi-Modal heißt: Operator muss pro
  Asset entscheiden, welcher Backend primary ist. Diese Operator-UX
  ist nicht in M5-02-Scope und kein anderer Slice deckt sie heute —
  bleibt eine eigene Folgearbeit wenn der Trigger zündet.

Bi-Modal ist **nicht** in der F-Folgearbeiten-Liste; wenn ein
Operator-Trigger einen Bi-Modal-Bedarf zündet, wird das in einer
neuen ADR mit eigener Aktivierungs-Sequenz behandelt.

---

## 4. Achse 2 — Solver-Wahl

### OSQP (gewählt)

Konkrete Wins für die **MPC-QP-Surface**:

- **MPC-Standard in BESS-Literatur.** OSQP ist die übliche QP-Engine
  in Battery-Energy-Storage-Dispatch-Papern (ADMM-basiert, Warm-
  Starting zwischen aufeinanderfolgenden MPC-Schritten, Block-Sparse-
  Matrix-Format) und wird in akademischen Vergleichsstudien
  konsistent als „first choice für QP-MPC" geführt. Kein Eigenbau-
  Risiko an einer für die Branche neuen Solver-Wahl.
- **Lizenz.** Apache-2.0 / MIT-kompatibel — kein Lizenz-Audit-Block
  für die Worker-Container-Distribution.
- **Determinismus unter Single-Thread.** OSQP ist Single-Thread-
  deterministisch wenn der Polish-Schritt deaktiviert wird; das
  passt zu Plan §5 D-04 `DeterministicMode = Strict` (Single-Thread-
  Solver-Disziplin). Plan §5 D-04 bullet listete OR-Tools-QP /
  OSQP / HiGHS als Sub-Achse; OSQP ist die Linie mit dem klarsten
  Strict-Mode-Pfad (HiGHS-QP-Determinism-Vertrag ist noch jung,
  OR-Tools-QP ist eher LP-orientiert).
- **C-Bibliothek mit existierenden .NET-Bindings.** Es existieren
  NuGet-Wrapper (z. B. `osqp-net`, `OSQP.NET`); Sub-Slice B
  entscheidet zwischen Wrapper-Reuse und Eigenbau-P/Invoke (analog
  zur M3-Native-Kernel-P/Invoke-Linie aus ADR 0004) im Sub-Slice-
  Plan-Review.
- **Warm-Starting.** OSQP unterstützt explizit Warm-Start zwischen
  aufeinanderfolgenden Solves — natürlicher Fit für die MPC-
  Receding-Horizon-Linie, wo aufeinanderfolgende Solves nur leicht
  verschoben sind.

Konkrete Trade-offs:

- **QP-only.** OSQP löst (Box-)QP, nicht MILP. Plan §1 Scope-Cut
  schließt MILP-MPC explizit aus (F-M5-07 wäre der Trigger);
  Sub-Slice B muss sicherstellen dass das LTI-Modell aus Sub-Slice A
  reines QP produziert (keine ganzzahligen Variablen). Constraint-
  Linie aus Sub-Slice A (SOC/Power/Ramp-Boxen) ist QP-kompatibel.
- **Polish-Schritt vs. Determinismus.** OSQP-Polish kann die
  Lösungsqualität verbessern, ist aber numerisch instabiler unter
  Single-Thread-Strict-Mode. Sub-Slice B pinnt Polish=off im
  `Strict`-Modus.
- **Pre-Solve.** OSQP hat einen optionalen Pre-Solve-Schritt; muss
  unter `Strict` deaktiviert / pin-deterministisch konfiguriert
  werden (Plan §5 D-04 Strict-Disziplin).
- **Byte-identical-Determinismus ist empirisch, nicht formal
  garantiert.** OSQP-Single-Thread-Determinismus mit `polish=off`/
  `scaling=0`/`warm_start=false` ist Praxis, nicht im OSQP-Repo als
  Vertrag dokumentiert; SIMD-Reihenfolge in Sparse-Matrix-Operations
  oder Pre-Solve-Akkumulation kann Floating-Point-Driften
  einführen. Plan §5 D-04 fordert „byte-für-byte identische
  Stempel-Hashes" als Cross-Run-Determinism-Pin — wenn OSQP unter
  den gepinnten Flags **nicht** byte-identical liefert, bricht der
  Sub-Slice-D-D-04-Pin.

  **Sub-Slice-B-Validierungs-Pin (Pflicht):** Empirische Bestätigung
  in einem dedizierten Pin (z. B. `LocalOsqpSolverDeterminismTests`):
  derselbe MPC-Step zweimal in Folge ⇒ byte-identische
  `MpcTrajectory.Points` über SIMD/Sparse-Reihenfolge. Wenn der
  Pin in Sub-Slice B bricht, kommt eine Fallback-Reaktion in zwei
  Schritten:

  1. **Strict-Mode wird Toleranz-Mode mit hartem Bound.** Plan §5
     D-04 wird angepasst — `Strict` heißt dann „≤ 1e-12 relativ pro
     Schedule-Point" statt „byte-identical", die Stempel-Hashes
     der Identity-Tuple-Felder (asset_id, tick, sample_time,
     model_version, solver_config_hash, …) bleiben byte-identical
     weil sie aus deterministischen Serialisierungen entstehen,
     nicht aus Float-Trajektorien.
  2. **Cross-Run-Determinism-Pin wird auf Toleranz-basierte
     Vergleich umgestellt.** Sub-Slice-D-Cross-Run-Determinism-Pin
     bekommt eine zweite Achse: byte-identical für Identity-Tuple-
     Hashes, ≤ 1e-12 relativ für Float-Trajektorien.

  Diese zwei-Stufen-Fallback hält Plan §5 D-04 Reproduzierbarkeits-
  Vertrag operativ, auch wenn OSQP-Determinismus empirisch
  schwächer als angenommen ausfällt. Reviewer-Pflicht: Sub-Slice B
  liefert den Validierungs-Pin und meldet das Ergebnis im Closure-
  Commit; bei Bruch wird Plan §5 D-04 + Sub-Slice-D-DoD in einer
  Folge-Anpassung aktualisiert (eigene Plan-Review-Iteration).

### Verworfene Solver-Alternativen

- **OR-Tools-QP.** Reuse-Vorteil zur M2-LP-Linie ist nett, aber
  OR-Tools ist primär LP-/MILP-orientiert; QP-Support ist via
  `MathOpt`-Wrapper über externe QP-Solver (OSQP / HiGHS) — die
  Reuse-Linie löst sich auf. Plus: OR-Tools-Wire ist gegen LP-CP-
  SAT-Constraints optimiert, nicht gegen sparse-QP-Block-Strukturen.
- **HiGHS-QP.** QP-Support in HiGHS ist relativ neu (HiGHS 1.6+);
  Plan §5 D-04 verlangt `DeterministicMode = Strict` — HiGHS-QP-
  Determinism-Vertrag ist nicht so klar dokumentiert wie OSQP-
  Single-Thread-No-Polish. Bleibt als Folgearbeit wenn OSQP-spezifische
  Pain-Points auftreten (eigene Folge-ADR).
- **MOSEK / Gurobi.** Kommerziell-lizenzpflichtig; Lizenz-Audit + Per-
  Instance-Provisioning ist Operator-Friction. Bleibt nicht in der
  M5-Liste; eigene ADR wenn ein konkreter Operator das fordert.
- **Custom-Solver (Eigenbau).** Sub-Slice-B-LOC-Schätzung (800–1200)
  reicht für einen Eigenbau-QP-Solver nicht; Plan §1 Scope-Cut
  schließt eigene Numerik aus.

---

## 5. Achse 3 — Backend-Abstraktion (schmal halten für F-M5-12)

Damit Sub-Slice B die Tür für F-M5-12 nicht zumacht, müssen drei
Design-Disziplinen halten:

1. **`IMpcModelSolver` bleibt die einzige Solver-Surface.** Der Sub-
   Slice-A-Port (`SolveAsync(state, model, options, trajectoryAnchor,
   ct) → Task<MpcTrajectory>`) ist Backend-agnostisch — er kennt
   weder gRPC noch In-Process-P/Invoke. F-M5-12 fügt einen zweiten
   Adapter `OptimizationCoreMpcOptimizer : IMpcModelSolver` hinzu;
   die Application-Schicht bleibt unverändert.

2. **`BessHostOptions.MpcBackend` ist der einzige Backend-Wahl-Slot.**
   Sub-Slice B mappt:
   - `null` ⇒ MPC deaktiviert (kein `IMpcDispatchOptimizer` im DI)
   - `"local_osqp"` ⇒ `LocalOsqpMpcSolver` registriert
   - `"optimization_core"` / `"bi_modal"` ⇒ Startup-Fehler
     `mpc-backend-not-implemented` (Sub-Slice-B-Negativ-Pin)

   F-M5-12 ändert nur die letzten beiden Zellen — `"optimization_core"`
   wird gültig, `"bi_modal"` bleibt (oder kippt zu „eigener
   Folge-ADR" wenn der Bi-Modal-Trigger explizit dazu kommt).

3. **Sub-Slice-B Adapter-Namespace ist Backend-spezifisch.** Der
   Backend-spezifische Sub-Namespace (z. B. `…Mpc.Local` für
   `LocalOsqpMpcSolver`) ist Sub-Slice-B-Plan-Entscheidung — die
   ADR fixiert nur die Disziplin („pro Backend ein eigener Sub-
   Namespace im `BatteryEms.Adapters.Optimization.Mpc.*`-Baum,
   damit F-M5-12 einen Sibling-Namespace ohne Code-Konflikt
   einfügen kann"), nicht den konkreten Folder-Pfad. Verschiebt
   Sub-Slice B den Adapter z. B. nach `…Mpc.Osqp` statt `…Mpc.Local`,
   bleibt die ADR-Aussage gültig.

Verstößt Sub-Slice B gegen eine dieser Disziplinen (z. B. weil ein
OSQP-spezifisches Detail in den `IMpcModelSolver`-Port leakt), wird
F-M5-12 teurer — Pin-Volumen wandert in den Plan-Review für F-M5-12.
Sub-Slice-B-Architektur-Pin asseritert die drei Disziplinen.

---

## 6. Trigger für Backend-Pivot (F-M5-12)

Jeder einzelne Trigger erzwingt eine neue ADR (0007 oder höher) mit
Migrations-Plan; die hiesige ADR bleibt unverändert bis ein Pivot
formal aktiviert wird. Trigger-Watch ist Pflicht in jeder Sub-Slice-D-
Closure-Checkliste (§8 Punkt 5).

1. **`sample_time < 10 ms` im operativen Profil.**
   ADR 0005 §7 Trigger 1 nennt den 1-ms-Latenz-Bound für die
   MPC-Inner-Loop als Phase-4-Pivot — dort als Shared-Memory-Pfad.
   Für die hiesige Backend-Wahl ist der Trigger umgekehrt: solange
   `sample_time` deutlich über der Sidecar-Roundtrip-p99-Latenz liegt
   (Faktor ~50× = 100 ms / 2 ms p99), ist Sidecar tolerierbar — bei
   `sample_time < 10 ms` wird Sidecar untolerierbar, und Local-First
   wird Pflicht ohne Folgearbeit. **Heute fester Stand:** der hier
   gewählte Local-First-Pfad **deckt bereits jeden** `sample_time < 10
   ms`-Bedarf — der Trigger zielt auf die F-M5-12-Sidecar-Linie und
   dort ist er ein **Blocker** (Sidecar-First darf nicht aktiviert
   werden wenn `sample_time < 10 ms`). Die ADR 0005 §7 Trigger 1
   Phase-4-Shared-Memory-Linie wird wieder relevant, wenn ein
   in-process-QP-Solver-Call selbst den 1-ms-Bound überschreitet
   (heute nicht der Fall).

2. **Solver-Isolationspflicht.**
   Wenn die Solver-Engine zu einer Crash-Quelle wird (Plan §7
   Risiken-Block nennt OSQP-Solver-Bugs als Sub-Slice-B-Risiko; ADR
   0004 §4 Trigger 6 ist die generische Crash-Isolation-Linie) ODER
   wenn Operator-Anforderung nach Solver-Sandbox aufkommt (Audit,
   Sicherheits-Zertifizierung), zündet der Sidecar-First-Pfad als
   Crash-Isolation-Linie. F-M5-12 wird dann „Sidecar-First mit
   in-process-Local-OSQP als Fallback" — die Topologie kippt, der
   Solver bleibt.

3. **Multi-Language-Solver-Anforderung.**
   Wenn ein konkreter Operator-Trigger eine Python- /
   cvxpy-/Stan-/JAX-basierte Solver-Linie verlangt (z. B. weil ein
   bestehender ML-Forecaster im selben Sidecar laufen soll —
   F-M5-08 Stochastic-MPC ist ein wahrscheinlicher Trigger-Vorbote)
   wird Sidecar-First die natürliche Linie. Sub-Slice-B-Local-OSQP
   bleibt produktiv für die LTI-Linie; F-M5-12 liefert den zweiten
   Backend für die Drittsprach-Linie.

4. **Asset-spezifische Operator-Backend-Wahl.**
   Wenn ein konkreter Multi-Asset-Workflow Asset-spezifische
   Backend-Wahl verlangt (Asset A: Local-OSQP, Asset B: Sidecar mit
   Python-Solver), zündet F-M5-12 + ein **zusätzlicher**
   Bi-Modal-Trigger (eigene Folge-ADR weil die Operator-UX-Achse aus
   §3 oben dann fällig wird). Plan §9 F-M5-06 Multi-Asset-MPC ist der
   wahrscheinliche Trigger-Vorbote.

5. **Container-/Pod-Co-Location-Constraint.**
   Wenn alle drei konkreten Bedingungen gleichzeitig zutreffen:
   (a) M5-01-`optimization-core`-Sidecar läuft bereits in derselben
       Pod als Compose-/Pod-Service (überprüfbar über
       `deploy/compose.yml` o.ä.);
   (b) Worker-Container-Image-Größe überschreitet ein konkretes
       Operator-Limit (heutiger Bezugspunkt: das `runtime`-Image
       aus `Dockerfile` Zeile 297+, gemessen via
       `docker image inspect --format='{{.Size}}'`; konkretes
       Limit wird zur Trigger-Aktivierung in der F-M5-12-
       Folge-ADR fixiert — Default-Schwelle bei der Eröffnung: **>
       800 MB**);
   (c) DevOps-/Operator-Ticket fordert explizit Worker-Image-
       Reduktion (Audit-Trail im Issue-Tracker, kein
       Stimmungs-Trigger).
   Erst wenn alle drei Bedingungen gleichzeitig erfüllt sind,
   kippt die Total-Cost-Linie zu Sidecar-First. Eine einzelne
   Bedingung allein ist kein Trigger — der Reviewer aktiviert
   F-M5-12 nicht, weil das Worker-Image „groß fühlt", sondern weil
   ein gemessener Schwellwert plus ein konkretes Ticket vorliegen.

Diese Trigger ändern die ADR nicht silent — jeder bekommt eine
eigene Folge-ADR mit Migrations-Plan und einer Aktualisierung von
F-M5-12 in `note-RM-M5-followups.md`.

---

## 7. Konsequenzen

### Positiv

- **D-02 ist geschlossen.** Plan-RM-M5-02 §2 Aktivierungsbedingungen
  Zeile „ADR 0006" wird auf „Geschlossen mit ADR 0006" gesetzt;
  Sub-Slice B kann starten.
- **Sub-Slice-B-DoD passt in die LOC-Schätzung.** Ein einziger
  Solver-Adapter + Constraint-Property-Pin-Reuse aus Sub-Slice A +
  8 Roundtrip-Pins für OSQP liegt in der 800–1200-LOC-Spanne.
  Bi-Modal hätte das gesprengt.
- **Coherent in-process-Pflichtpfad.** Worker startet, lädt
  OSQP-Adapter, `IMpcDispatchOptimizer` ist auflösbar; keine
  zweite-Linien-Healthy-Gate (Sidecar-Reachability). Plan §6
  Acceptance-Criterium bleibt einfach.
- **Fallback-Linie kohärent.** Plan §5 D-07 in-process-Fallback
  liefert keine Cross-Process-Race; Sub-Slice C kann den Kalman-
  Filter ohne Sidecar-Reachability-Schwankungen pinnen.
- **F-M5-12-Pfad ist freigehalten.** Die schmale `IMpcModelSolver`-
  Surface aus Sub-Slice A + die `MpcBackend`-Slot-Reserve-Namen aus
  §2 Zeile „Backend-Topologie" sind die Joker für die
  Sidecar-Erweiterung. Kein Re-Plumbing in Sub-Slice B nötig.

### Negativ

- **Native-Binary im Worker-Process.** OSQP `libosqp.so` (oder
  Windows-Äquivalent) muss in den Worker-Container — ADR 0004 §4
  Trigger 6 (Crash-Isolation) bleibt als Folge-Trigger explizit
  offen. Mitigation: Sub-Slice-B-CI-Stage baut OSQP mit Sanitizer-
  Flags (analog zur `native-sanitizer`-Stage aus dem M3-Native-
  Kernel-Build); Crash-Telemetrie pro OSQP-Solve aus Sub-Slice-C-
  Metriken.
- **Solver-Universum begrenzt auf OSQP.** Wenn ein Operator
  einen anderen Solver verlangt (HiGHS-QP, MOSEK, …), wird das ohne
  Sidecar-Pfad eine zweite In-Process-Solver-Adapter-Linie — vorerst
  als „weitere D-02-Sub-Achse" in der hiesigen ADR-Konsequenzen-Linie
  offen. Heute kein konkreter Trigger.
- **Patch-Window zieht mit Worker mit.** OSQP-CVE → Worker-Image-
  Rebuild → Worker-Container-Restart. Plan §7 nennt das als
  Sub-Slice-B-Risiko. Mitigation: Operator-Notification-Linie aus
  M1-15-Quality-Doku § 5 (Sicherheitsupdates).

### Neutral

- **`spec/architecture.md` §13.1 / §13.2 bleibt unverändert.** Phase
  3 nennt „Native Sidecar via gRPC" für MPC/State-Space/Solver-
  Anbindung als Phasenmodell-Slot — die hiesige ADR formalisiert
  dass der Sidecar-Pfad **nicht jetzt** aktiviert wird, sondern als
  F-M5-12-Folgearbeit verlinkt bleibt. Phase-3 ist damit nicht
  abgeschlossen; die ADR 0005 Phase-3-Adoption für die LP-Linie
  (M5-01) bleibt unverändert.
- **`spec/architecture.md` §18 AR-OPEN-Liste** bekommt keinen neuen
  Eintrag — D-02 war eine Plan-interne Achse, nicht eine offene
  Architektur-Frage. Closure passiert in
  [`plan-RM-M5-02.md`](../planning/done/plan-RM-M5-02.md) §5
  D-02-Block, nicht in der Architektur-Spec.
- **ADR 0005 bleibt `Accepted`.** Der Sidecar-Transport für die
  M5-01-LP-Linie bleibt produktiv; die hiesige ADR 0006 macht keine
  Aussage über die LP-Linie und keinen Pivot in M5-01.

---

## 8. Sequenz und Aktivierung

1. **Plan-RM-M5-02.md aktualisieren** (im selben Commit wie diese
   ADR):
   - §2 Aktivierungsbedingungen Zeile „ADR 0006" auf
     „Geschlossen mit ADR 0006 (Local-First mit OSQP)" setzen.
   - §5 D-02 Block auf „Fixiert mit ADR 0006: (a) Local-First mit
     OSQP" setzen; Default-Vorschlag-Absatz entfernen oder als
     „historischer Default-Vorschlag, durch ADR 0006 überschrieben"
     markieren.
   - §9 Folgearbeiten-Block: neuer Eintrag F-M5-12 mit den Triggern
     aus §6 dieser ADR (Cross-Reference auf
     `note-RM-M5-followups.md` F-M5-12).
2. **`note-RM-M5-followups.md` ergänzen:** neuer Eintrag F-M5-12
   mit den fünf Triggern aus §6.
3. **Sub-Slice-B starten.** Plan-RM-M5-02 §8 Sequenz Schritt 3
   Punkt 2 — RM-M5-02-B liefert `LocalOsqpMpcSolver` + 8+
   Roundtrip-Pins + Constraint-Property-Pin-Reuse gegen den echten
   Solver.
4. **OSQP-Binding-Wahl** (Sub-Slice-B-Plan-Review): NuGet-Wrapper-
   Reuse (`osqp-net` o.ä.) vs. Eigenbau-P/Invoke. Falls Eigenbau-
   P/Invoke gewählt wird, Sub-Slice B trägt eine eigene D-Entscheidung
   im Sub-Slice-B-Plan-Header.
5. **F-M5-12-Trigger-Watch.** Sub-Slice-D-Closure-Checkliste
   ergänzt einen Punkt „Hat einer der ADR-0006 §6-Trigger gezündet
   während M5-02-Lieferung?". Wenn ja, F-M5-12-Aktivierung anstoßen
   (eigene ADR 0007).

Bis ein Trigger zündet bleibt diese ADR `Accepted` und Local-First
mit OSQP ist die produktive Backend-Linie für die MPC-Surface.
