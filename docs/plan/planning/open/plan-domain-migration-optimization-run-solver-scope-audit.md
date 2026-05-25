# Plan: Domain-Migration OptimizationRun.SolverScopeAudit

**Dokumenttyp:** Pre-Slice / offen
**Status:** Open - nur erforderlich, wenn produktive Replays ohne Request-Snapshot freigegeben werden sollen
**Datum:** 2026-05-25
**Bezug:**
[`plan-domain-migration-optimization-run-can-execute.md`](plan-domain-migration-optimization-run-can-execute.md),
[`plan-market-colocation-model.md`](plan-market-colocation-model.md)

---

## Ziel

`solver_scope` (`LP`/`MILP`) bleibt im aktuellen Co-Location-Scope im immutable
Optimierungsrequest-Snapshot auditierbar. Produktive Replays ohne diesen Snapshot
sind nicht freigegeben.

Dieser Pre-Slice wird nur aktiviert, wenn ein späterer Produkt- oder
Replay-Betrieb `solver_scope` direkt auf `OptimizationRun` oder einem
äquivalenten Run-Audit-Record persistieren muss.

## Scope

- Persistierbaren Audit-Ort für `solver_scope` definieren, ohne die autoritative
  `CanExecute`-Matrix aus
  [`plan-domain-migration-optimization-run-can-execute.md`](plan-domain-migration-optimization-run-can-execute.md)
  zu duplizieren.
- Migration, Store-/Wire-Mapping und API-/Replay-Ausgabe für den gewählten
  Audit-Ort festlegen.
- Backfill-Regel für historische Runs definieren:
  `solver_scope` darf nicht geraten werden; ohne Request-Snapshot bleibt der Wert
  `unknown`/`not_available` und nicht replayfähig.

## Nicht-Ziele

- Keine Änderung der Solver-Auswahlregel für Co-Location.
- Keine neue `OptimizationSolverStatus`- oder `TerminationCode`-Matrix.
- Keine Freigabe snapshotfreier produktiver Replays ohne explizite Migration.

## Definition of Done

- [ ] Audit-Feld oder Audit-Record für `solver_scope` ist schema- und wireseitig
  definiert.
- [ ] `LP`/`MILP`-Werte roundtrippen in Store, API und Replay-Ausgabe.
- [ ] Historische Runs ohne Request-Snapshot werden nicht still klassifiziert.
- [ ] Co-Location-Replays prüfen `solver_scope` gegen den ursprünglichen
  Request-Scope und blockieren Abweichungen.
- [ ] Dokumente, die snapshotfreie Replays erwähnen, verlinken auf diesen
  Pre-Slice.
