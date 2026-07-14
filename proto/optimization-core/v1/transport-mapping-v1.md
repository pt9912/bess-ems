# Transport-Mapping v1 — gRPC-Status → Normierte Outcomes

**Dokumenttyp:** Versioniertes Artefakt ([RM-M5-01](../../../docs/plan/planning/done/plan-RM-M5-01.md) D-04)
**Status:** Aktiv für `optimization-core` Contract-Version 1.x
**Bezug:**
[`optimization_core.proto`](optimization_core.proto) (Service-Vertrag),
[`../../../docs/plan/planning/done/plan-RM-M5.md`](../../../docs/plan/planning/done/plan-RM-M5.md)
§Sidecar-Status-Taxonomie + §Fallback-Matrix,
[`../../../docs/plan/planning/done/plan-RM-M5-01.md`](../../../docs/plan/planning/done/plan-RM-M5-01.md)
D-04 (versioniertes Mapping-Artefakt),
[`../../../docs/plan/adr/0005-optimization-core-sidecar-transport.md`](../../../docs/plan/adr/0005-optimization-core-sidecar-transport.md)
§8 Konsequenzen (`OK/DEADLINE_EXCEEDED/UNAVAILABLE/…`-Mapping als Standard).

---

## 1. Zweck

Diese Tabelle mappt gRPC-Statuscodes (`Grpc.Core.StatusCode`) plus den
Sidecar-Payload (`solver_status` + `has_usable_solution`) auf die
normierten Outcomes der plan-RM-M5 §Sidecar-Status-Taxonomie:
`OptimizationRun.Status` (M2-Modell), `TerminationReason` und die
beiden Metric-Tags `fallback_source` + `fallback_reason`.

`OptimizationCoreStatusMapper`-static im Adapter implementiert diese
Tabelle 1:1. Jede Änderung verlangt einen Plan-Slice (D-04) — kein
Code-only-Schwenk, damit der Operator-Vertrag (Metric-Tags + Run-
Status) stabil bleibt.

Worker prüft **vor** der Persistenz: `OptimizationRun.ProducedSchedule`
wird **nur** gesetzt wenn die Tabelle `fallback_source=sidecar_result`
liefert (= das Sidecar hat eine nutzbare Lösung geliefert UND der
Worker akzeptiert sie). Alle Fallback-Pfade führen zu **keiner** neuen
Schedule-Version; der Control-Pfad fällt auf den lokalen Optimierer
oder den letzten gültigen Fahrplan oder Safe-Stop gemäß plan-RM-M5
§Fallback-Matrix zurück.

---

## 2. Mapping-Tabelle

| gRPC-StatusCode | Sidecar-Payload (`solver_status` + `has_usable_solution`) | M2 `OptimizationSolverStatus` | `fallback_source` | `fallback_reason` | Schedule-Persistenz |
| --------------- | --------------------------------------------------------- | ----------------------------- | ----------------- | ----------------- | ------------------- |
| `OK` | `OPTIMAL` + `has_usable_solution=true` | `Optimal` | `sidecar_result` | `none` | ✅ persistieren |
| `OK` | `FEASIBLE` + `has_usable_solution=true` | `Feasible` | `sidecar_result` | `none` | ✅ persistieren (Solution-Quality im `solution_quality`-Tag) |
| `OK` | `INFEASIBLE` + `has_usable_solution=false` | `Infeasible` | gemäß Fallback-Matrix | `solver_infeasible` | ❌ keine neue Version |
| `OK` | `UNBOUNDED` + `has_usable_solution=false` | `Unbounded` | gemäß Fallback-Matrix | `solver_unbounded` | ❌ keine neue Version |
| `OK` | `TIME_LIMIT` + `has_usable_solution=true` | `Feasible` | `sidecar_result` | `none` (mit `termination_code=TIME_LIMIT_WITH_FEASIBLE_SOLUTION`) | ✅ persistieren als Feasible |
| `OK` | `TIME_LIMIT` + `has_usable_solution=false` | `TimeLimit` | gemäß Fallback-Matrix | `solver_time_limit` | ❌ keine neue Version |
| `OK` | `ITERATION_LIMIT` + `has_usable_solution=true` | `Feasible` | `sidecar_result` | `none` (mit `termination_code=ITERATION_LIMIT_WITH_FEASIBLE_SOLUTION`) | ✅ persistieren als Feasible |
| `OK` | `ITERATION_LIMIT` + `has_usable_solution=false` | `IterationLimit` | gemäß Fallback-Matrix | `solver_iteration_limit` | ❌ keine neue Version |
| `OK` | `FAILED` (egal welcher `has_usable_solution`-Wert) | `Failed` | gemäß Fallback-Matrix | `transport_internal_error` | ❌ keine neue Version |
| `DEADLINE_EXCEEDED` | — (kein Payload) | `TimeLimit` | gemäß Fallback-Matrix | `deadline_exceeded` | ❌ keine neue Version |
| `UNAVAILABLE` | — | `Failed` | gemäß Fallback-Matrix | `sidecar_unavailable` | ❌ keine neue Version |
| `CANCELLED` (durch Caller) | — | `Failed` | gemäß Fallback-Matrix | `transport_cancelled` | ❌ keine neue Version, kein Retry |
| `INVALID_ARGUMENT` | — | `Failed` | `no_activation` oder `safe_stop` | `invalid_request` | ❌ Ergebnis verwerfen, kein lokaler Optimierer mit denselben ungültigen Eingaben |
| `UNAUTHENTICATED` / `PERMISSION_DENIED` | — | `Failed` | gemäß Fallback-Matrix | `unauthorized_client` | ❌ keine neue Version |
| `INTERNAL` / `UNKNOWN` / sonstiger Code | — | `Failed` | gemäß Fallback-Matrix | `transport_internal_error` | ❌ keine neue Version |
| Decode-/Parse-Fehler im Stream | n/a | `Failed` | gemäß Fallback-Matrix | `transport_internal_error` | ❌ keine neue Version |
| Pre-Request Version-/Feature-Mismatch | n/a | `Failed` | `no_activation` oder `safe_stop` | `contract_incompatible` | ❌ kein Request gesendet |
| Späte Antwort nach Timeout-Finalisierung | n/a | `Failed` | unverändert (bereits final) | `late_response_ignored` | ❌ keine zweite Aktivierung |

---

## 3. Reihenfolge der Klassifikation

`OptimizationCoreStatusMapper.Classify(...)` arbeitet in dieser
Reihenfolge:

1. **Pre-Request-Gates**: Contract-Version-Compat + Feature-Flag-Check.
   Inkompatibilität → `contract_incompatible`-Fallback ohne Sidecar-
   Roundtrip.
2. **Idempotency-Store-Lookup**: existierende `request_id` mit
   Terminalzustand → `late_response_ignored` oder reuse des
   Terminalzustands; kein zweiter Sidecar-Call.
3. **gRPC-Status-Code** (auf der Aufruf-Schicht):
   - `OK` → weiter zu Schritt 4 (Payload-Auswertung).
   - Alle anderen Codes → direkt aus Tabelle in §2 mappen.
4. **Sidecar-Payload** (nur wenn `OK`): `solver_status` +
   `has_usable_solution` → aus Tabelle in §2 mappen.

---

## 4. Solution-Quality-Tag

`OptimizeResult.solution_quality` ist ein observability-friendly
Label (kebab-case), das die kombinierte Lese-Linie aus
`solver_status` + `has_usable_solution` abkürzt:

| Kombination | `solution_quality` |
| ----------- | ------------------ |
| `OPTIMAL` + `has_usable_solution=true` | `optimal` |
| `FEASIBLE` + `has_usable_solution=true` | `feasible` |
| `TIME_LIMIT`/`ITERATION_LIMIT` + `has_usable_solution=true` | `feasible` (mit `termination_code` für Quelle) |
| Alle anderen Fälle | `none` |

Wird in Metric-Tags und als Run-Audit-Feld verwendet; nicht
load-bearing für die Persistenz-Entscheidung (das macht
`has_usable_solution` direkt).

---

## 5. Versionierung

- **v1** (heute): diese Tabelle. Gilt für `contract_version=1.x`.
- Erweiterungen (neue gRPC-Status-Codes, neue
  `OptimizationSolverStatus`-Werte, neue Solver-Outcomes wie
  z.B. „interrupted-by-cancellation-with-partial-solution"): eigene
  v2-Tabelle in `transport-mapping-v2.md` plus Code-Schwenk im
  Mapper.
- Worker und Sidecar prüfen `contract_version` pre-erstem-Optimize;
  v1-Worker gegen v2-Sidecar mit `min_compatible_version=2.0` →
  `contract_incompatible`-Fallback.

---

## 6. Out-of-Scope für v1

Diese Mappings sind in v1 **bewusst nicht** abgedeckt; jedes ist eine
eigene F-Item-Linie wenn der Trigger zündet:

- **gRPC-`ABORTED`** (Konflikt mit aktuell-laufendem-Stream): keine
  Operator-Anforderung heute; Trigger wäre Multi-Worker-gegen-Single-
  Sidecar-Topologie.
- **gRPC-`FAILED_PRECONDITION`**: heute fallen alle Pre-Condition-
  Fehler unter `INVALID_ARGUMENT` (Sidecar-seitige Klassifikation).
  Trennung kommt wenn der Sidecar `FAILED_PRECONDITION` für
  Solver-State-Inconsistencies nutzt ([RM-M5-02](../../../docs/plan/planning/done/plan-RM-M5-02.md) MPC-State-Reset-Pfad).
- **MPC-spezifische Outcomes** (`InvalidMpcState`,
  `KalmanFilterDiverged`): kommen mit [RM-M5-02](../../../docs/plan/planning/done/plan-RM-M5-02.md) als additive Felder
  im selben `SolverStatus`-Enum oder als neuer Outcome-Slot in
  `OptimizeMpcResponse`. Vertrag-Reserve für diese Slots ist in
  `optimization_core.proto` § OptimizeMpcResponse (Field-Numbers
  1-50 reserviert).
