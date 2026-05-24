# Plan: Domain-Migration OptimizationRun.CanExecute

**Dokumenttyp:** Pre-Slice / offen
**Status:** Open - Voraussetzung für Markt-/Co-Location- und LER/FCR-Slices
**Datum:** 2026-05-24
**Bezug:**
[`plan-market-colocation-model.md`](plan-market-colocation-model.md),
[`plan-ler-fcr-reserve-robustness.md`](plan-ler-fcr-reserve-robustness.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)

---

## Ziel

`OptimizationRun` erhält ein persistiertes hartes Ausführungsgate `CanExecute`.
Das bestehende `HasUsableSolution` bleibt die Solver-Ergebnis-Sicht
(`Optimal`/`Feasible`), reicht aber nicht mehr als operative
Ausführungsentscheidung.

Normativ gilt nach der Migration:

```text
Run darf ausgeführt werden <=> HasUsableSolution && CanExecute
```

Jeder Slice darf `CanExecute` nur von `true` auf `false` ziehen. Kein Slice darf
ein bereits gesetztes `false` wieder auf `true` setzen.

---

## Ausgangslage

Heute ist `OptimizationRun` ein immutable Domain-Objekt mit positionalem
Konstruktor und harten Invarianten. `HasUsableSolution` ist ein computed
Property auf Basis von `OptimizationSolverStatus.Optimal` oder
`OptimizationSolverStatus.Feasible`.

Nicht vorhanden:

- persistiertes Feld `CanExecute` / `can_execute`,
- Wire-/DB-/Proto-Mapping für das Ausführungsgate,
- konjunktive Nutzung `HasUsableSolution && CanExecute` in allen operativen
  Konsumenten.

---

## Scope

1. Domain
   - `OptimizationRun` um `CanExecute` als Konstruktorparameter und Property erweitern.
   - Default für bestehende Produzenten: `true`, sofern kein expliziter Hard-Stop
     vorliegt.
   - Invarianten ergänzen:
     - `CanExecute=true` ist nur zulässig, wenn `HasUsableSolution=true`.
     - `CanExecute=false` ist auch bei `Optimal`/`Feasible` zulässig, wenn ein
       fachlicher Guard die Ausführung sperrt.

2. Konstruktor-Aufrufer und Factories
   - alle direkten `OptimizationRun`-Aufrufer aktualisieren:
     - `NoOpScheduleOptimizer`,
     - `OptimizationCoreResultFactory`,
     - OR-Tools-/Optimization-Core-Mapper,
     - Use-Case-Fehlerpfade,
     - Tests und Fixtures.

3. Persistenz und Wire
   - `can_execute` in allen produktiven Stores hinzufügen.
   - Bestehende Daten migrieren:
     - bei `status in {optimal, feasible}` initial `can_execute=true`,
     - sonst `can_execute=false`,
     - spätere fachliche Hard-Stops überschreiben auf `false`.
   - Wire-Mapper und API-/DTO-Ausgaben erweitern.
   - Proto-/Optimization-Core-Mapping erweitern, soweit `OptimizationRun` über
     den Sidecar-Pfad materialisiert wird.

4. Konsumenten
   - Alle operativen `HasUsableSolution`-Verbraucher im Scheduler-/Dispatcher-,
     API- und Replay-Aktivierungspfad auf `HasUsableSolution && CanExecute`
     umstellen.
   - Reine Anzeige-/Analysepfade dürfen `HasUsableSolution` weiterhin separat
     ausgeben, müssen aber `CanExecute` daneben zeigen.

5. Tests
   - Domain-Tests für:
     - `Optimal/Feasible` + `CanExecute=true`,
     - `Optimal/Feasible` + `CanExecute=false`,
     - Nicht-Solution-Status + `CanExecute=false`,
     - Nicht-Solution-Status + `CanExecute=true` wird abgewiesen.
   - Persistenz-Roundtrip für `can_execute`.
   - API-/Wire-Roundtrip.
   - Scheduler-/Dispatcher-Guard:
     `OptimizationSolverStatus.Feasible` + `CanExecute=false` darf nie in den
     ausführbaren Pfad gelangen.

---

## Abhängigkeiten

Dieser Pre-Slice muss vor diesen Slices abgeschlossen sein:

- [`plan-market-colocation-model.md`](plan-market-colocation-model.md)
- [`plan-ler-fcr-reserve-robustness.md`](plan-ler-fcr-reserve-robustness.md)

Die Slices dürfen danach nur fachliche Gründe für `CanExecute=false` liefern,
nicht mehr die Domain-/Persistenzmigration selbst besitzen.

---

## Definition of Done

- [ ] `OptimizationRun` trägt `CanExecute` als persistiertes Domain-Feld.
- [ ] Alle Konstruktor-Aufrufer, Factories, Tests und Fixtures sind migriert.
- [ ] Store-/Wire-/API-/Proto-Mappings führen `can_execute`.
- [ ] Operative Konsumenten nutzen `HasUsableSolution && CanExecute`.
- [ ] Migration vorhandener Daten ist dokumentiert und getestet.
- [ ] Markt-/Co-Location- und LER/FCR-Slices referenzieren dieses Dokument als
  abgeschlossene Voraussetzung.
