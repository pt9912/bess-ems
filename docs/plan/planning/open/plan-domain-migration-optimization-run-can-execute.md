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

Die Run-Erzeugung erfolgt nach dieser Migration in einem gemeinsamen
Result-Building-Schritt: Solver-Ergebnis und alle aktivierten Guards werden
zuerst gesammelt, danach wird genau ein finaler immutable `OptimizationRun`
konstruiert. Es gibt keinen nachträglichen Mutationsschritt und keinen zweiten
"Wrap-Run" für dasselbe Ergebnis.

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
   - Default für bestehende Produzenten: `true` ist nur für Pfade zulässig, die
     nachweislich keine aktivierten Guard-Beiträge kennen (z. B. NoOp-Optimierer,
     Test-Fixtures oder reine Legacy-Leser). Alle neuen produktiven Produzenten
     schreiben `CanExecute` ausschließlich über den Combiner.
   - Optionales Audit-Metadatum `CanExecuteSource` oder äquivalenter Wire-Wert
     vorsehen, damit `computed_from_guards` von `legacy_backfill` unterscheidbar ist.
   - Invarianten ergänzen:
     - `CanExecute=true` ist nur zulässig, wenn `HasUsableSolution=true`.
     - `CanExecute=false` ist auch bei `Optimal`/`Feasible` zulässig, wenn ein
       fachlicher Guard die Ausführung sperrt.
   - Aggregations-Hook einführen:
     - eine Guard-Pipeline oder ein Computed-Combiner sammelt Beiträge wie
       `solver_result_executable`, `config_ok`, `schema_ok`, `source_ok` und
       `robust_ok`;
     - nachfolgende Slices hängen ihre `*_ok`-Beiträge ausschließlich an diesem
       Vertrag an;
     - der Combiner berechnet monoton `CanExecute = HasUsableSolution && all(_ok)`.

2. Konstruktor-Aufrufer und Factories
   - alle direkten `OptimizationRun`-Aufrufer aktualisieren; die konkrete Liste ist
     per `rg "new OptimizationRun\\(" src tests` vor Umsetzung zu ziehen und umfasst
     mindestens Optimizer, Use-Case-Fehlerpfade, Repository-Readmodelle, Tests und
     Fixtures.
   - bekannte heutige Schwerpunkte:
     - `NoOpScheduleOptimizer`,
     - `OptimizationCoreResultFactory` und `OptimizationCoreScheduleOptimizer`,
     - `OrToolsScheduleOptimizer`,
     - `DefaultScheduleOptimizationUseCase` und `DefaultIntradayReoptimizationUseCase`,
     - `DapperOptimizationRunRepository` und `InMemoryOptimizationRunRepository`,
     - `ScheduleOptimizationResult`-/Dispatcher-nahe Konsumenten,
     - Tests und Fixtures.
   - Result-Lifecycle:
     - Solver-Adapter liefern ein Solver-Rohresultat mit Solverstatus,
       Solver-TerminationCode und optionalem Solver-Detail.
     - Guard-Pipeline bewertet dieses Rohresultat zusammen mit Config-/Schema-/
       Source-/Robustheitsbeiträgen.
     - Die Result-Factory baut daraus einen finalen `OptimizationRun`.
     - Wenn ein Guard den `TerminationCode` ersetzt (z. B.
       `reserve-robustness-needs-restore`), muss der originale Solver-Code im
       `TerminationDetail` erhalten bleiben, z. B.
       `format=kv1;solver_code=or-tools-optimal;reason=INTRADAY_RESTORE_REQUIRED`.
     - Für neu erzeugte Guard-/Robustheits-Details ist das verbindliche
       Detailformat `format=kv1` plus eine `;`-getrennte `key=value`-Liste ohne
       `:`-Separator.
       Robustheitsgründe verwenden mindestens
       `format=kv1;reason=<LIMITING_REASON_CODE>` und optional
       `solver_code=<original-code>` gemäß
       [`plan-ler-fcr-reserve-robustness.md`](plan-ler-fcr-reserve-robustness.md).
       Bestehende freie `TerminationDetail`-Strings werden bei der Migration
       nicht syntaktisch umgeschrieben; sie bleiben Legacy-Auditdaten. Neue
       Guard-Ergebnisse dürfen keine gemischten Freitext-/`key=value`-Formate
       erzeugen. Parser müssen Legacy-Strings als Freitext behandeln, sobald
       kein führendes `format=kv1;` vorhanden ist.

3. Persistenz und Wire
   - `can_execute` in allen produktiven Stores hinzufügen:
     - `schema/schema.yaml` erweitern,
     - d-migrate-generierte RunOnce-Migration `000N_*.sql` erzeugen,
     - `DapperOptimizationRunRepository` Read/Write-Mapper aktualisieren,
     - In-Memory-Repository und Persistenztests synchron anpassen.
   - Bestehende Daten migrieren:
     - bei `status in {optimal, feasible}` initial `can_execute=true`,
     - sonst `can_execute=false`,
     - `can_execute_source=legacy_backfill` oder ein gleichwertiger
       Diskriminator wird gesetzt; dieser Wert bedeutet "kein historischer Guard
       bekannt", nicht "alle heutigen Guards aktiv grün".
     - Ein `legacy_backfill`-Run darf von Replay-/Lesepfaden als historisch
       nutzbare Solver-Lösung angezeigt werden, ist aber keine hinreichende
       Aktivierungsgrundlage für einen neuen Dispatch/Re-Run. Vor jedem erneuten
       operativen Dispatch muss die aktuelle Guard-Pipeline aktiv durchlaufen und
       mit einem nicht-legacy Diskriminator auditierbar sein.
     - spätere fachliche Hard-Stops überschreiben auf `false`.
   - Wire-Mapper und API-/DTO-Ausgaben erweitern.
   - API-Kompatibilität:
     - Producer-Vertrag: `can_execute` ist nach der Migration ein required Feld in
       Optimierungs-Run-Responses.
     - Wire-Kompatibilität: Die JSON-Änderung ist additiv; bestehende externe
       Konsumenten dürfen das unbekannte Feld ignorieren.
     - Konsumenten-Vertrag: neue interne Dispatcher-/Scheduler-Konsumenten müssen
       `can_execute` verwenden und dürfen `HasUsableSolution` nicht mehr allein als
       Ausführungsgate interpretieren.
     - Während eines kontrollierten Übergangsfensters darf der Read-Pfad für alte
       Datensätze den oben beschriebenen Initialwert ableiten; nach Abschluss der
       Migration muss das persistierte Feld maßgeblich sein.
   - Proto-/Optimization-Core-Mapping erweitern, soweit `OptimizationRun` über
     den Sidecar-Pfad materialisiert wird.

4. Konsumenten
   - Alle operativen `HasUsableSolution`-Verbraucher im Scheduler-/Dispatcher-,
     API- und Replay-Aktivierungspfad auf `HasUsableSolution && CanExecute`
     umstellen.
   - Migrationsreihenfolge:
     1. Persistenz-/Readmodelle und API-Ausgabe liefern `can_execute`.
     2. Interne Dispatcher-/Scheduler-Konsumenten schalten auf das konjunktive Gate.
     3. Externe Verbraucher erhalten Release-Hinweis; die alte Interpretation
        `HasUsableSolution` allein ist danach nur noch Anzeige-/Analyseinformation.
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
- [ ] Guard-Pipeline bzw. Computed-Combiner ist als API-Vertrag eingeführt und
  berechnet `CanExecute` aus den Slice-Beiträgen monoton.
- [ ] Alle Konstruktor-Aufrufer, Factories, Tests und Fixtures sind migriert.
- [ ] Store-/Wire-/API-/Proto-Mappings führen `can_execute`.
- [ ] Legacy-Backfill ist über `can_execute_source=legacy_backfill` oder einen
  gleichwertigen Diskriminator von aktiv geprüften Guard-Ergebnissen unterscheidbar.
- [ ] Replay-/Re-Run-/Dispatch-Pfade akzeptieren `legacy_backfill` nicht als
  alleinige Aktivierungsgrundlage; vor erneutem operativem Dispatch wird ein
  aktueller Guard-Pass persistiert.
- [ ] Re-Klassifikation nach Solver-Ergebnis erhält originale Solver-Provenance im
  finalen Run-Audit (`format=kv1;solver_code=...` oder äquivalentes Feld).
- [ ] Operative Konsumenten nutzen `HasUsableSolution && CanExecute`.
- [ ] Migration vorhandener Daten ist dokumentiert und getestet.
- [ ] Markt-/Co-Location- und LER/FCR-Slices referenzieren dieses Dokument als
  abgeschlossene Voraussetzung.
