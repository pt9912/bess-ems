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
   - Kein produktiver Produzent darf `CanExecute` direkt setzen; produktive
     Pfade schreiben `CanExecute` ausschließlich über den Combiner.
     `CanExecute=true` als Default ist nur für Pfade zulässig, die nachweislich
     keine aktivierten Guard-Beiträge kennen (z. B. NoOp-Optimierer,
     Test-Fixtures oder reine Legacy-Leser).
   - Optionales Audit-Metadatum `CanExecuteSource` oder äquivalenter Wire-Wert
     vorsehen, damit `computed_from_guards` von `legacy_backfill` unterscheidbar ist.
   - Invarianten ergänzen:
     - `CanExecute=true` ist nur zulässig, wenn `HasUsableSolution=true`.
     - `CanExecute=false` ist auch bei `Optimal`/`Feasible` zulässig, wenn ein
       fachlicher Guard die Ausführung sperrt.
     - Die bestehende Domain-Invariante bleibt unverändert:
       `HasUsableSolution=true` impliziert weiterhin `ProducedSchedule != null`,
       unabhängig von `CanExecute`. Ein Guard darf die Ausführung sperren, aber
       keine Solver-Lösung ohne Schedule-Referenz persistieren.
   - Aggregations-Hook einführen:
     - eine Guard-Pipeline oder ein Computed-Combiner sammelt Beiträge wie
       `solver_result_executable`, `config_ok`, `schema_ok`, `source_ok` und
       `robust_ok`;
     - nachfolgende Slices hängen ihre `*_ok`-Beiträge ausschließlich an diesem
       Vertrag an;
     - Sammelbeiträge wie `source_ok` sind all-or-nothing: mehrere Slices oder
       mehrere Serien dürfen denselben Slot bedienen, und jeder einzelne
       `source_ok=false`-Beitrag zieht das aggregierte `source_ok` auf `false`;
     - nicht aktivierte Beiträge gelten im jeweiligen Lauf als neutral `true`;
     - der Combiner berechnet monoton `CanExecute = HasUsableSolution && all(_ok)`.

### Gemeinsame Run-Mapping-Matrix

Dieser Pre-Slice ist die autoritative Quelle für die gemeinsame
`OptimizationSolverStatus`-/`TerminationCode`-/`CanExecute`-Matrix. Fach-Slices
dürfen nur ihre fachlichen Grundcodes und `*_ok`-Beiträge ergänzen; sie
duplizieren diese Matrix nicht.

Code-Konventionen:
- `TerminationCode` bleibt niedrig kardinal, kebab-case und familienpräfigiert
  (z. B. `reserve-robustness-needs-restore`).
- `format=kv1;reason=...` verwendet für maschinenlesbare fachliche Gründe
  `SNAKE_CAPS` (z. B. `RECOVERY_TIMEOUT`). Diese Trennung ist Absicht:
  `TerminationCode` gruppiert Dashboard-/Persistenzklassen, `reason` trägt den
  slicenahen Detailgrund.

| Ergebnisklasse | OptimizationSolverStatus | TerminationCode (Beispiel) | CanExecute |
| --- | --- | --- | --- |
| Gültiger Plan/Plan verwendbar | `Optimal` oder `Feasible` | bestehende Erfolgs-Codes, z. B. `or-tools-optimal` oder `or-tools-feasible-not-proven-optimal` | `true` |
| Solver-seitige mathematische Infeasibility | `Infeasible` | bestehender Solver-Code, z. B. `or-tools-infeasible` | `false` |
| Domain-spezifisch erklärbare Infeasibility (`MODEL_INFEASIBLE`) | `Infeasible` | bestehender Solver-Code, z. B. `or-tools-infeasible`; Domain-Grund in `TerminationDetail=format=kv1;reason=<DOMAIN_REASON>` | `false` |
| Solver-seitig unbeschränktes Modell | `Unbounded` | bestehender Solver-Code, z. B. `or-tools-unbounded` | `false` |
| Time Limit ohne ausführbaren Plan | `TimeLimit` | bestehender Timeout-Code, z. B. `or-tools-time-limit` | `false` |
| Iteration Limit ohne ausführbaren Plan | `IterationLimit` | bestehender Iterations-Code, sofern vom Solver geliefert | `false` |
| Reiner Rechenfehler/Solverfehler | `Failed` | Solver-spezifische harte Codes, z. B. `or-tools-abnormal`, `or-tools-model-invalid`, `or-tools-not-solved` | `false` |
| Konfigurationsfehler (`CONFIG_*`) | `Failed` | `config-invalid` oder `config-inconsistent` | `false` |
| Schematafehler (`SCHEMA_INCONSISTENT`) | `Failed` | `schema-inconsistent` | `false` |
| Fachlicher Guard erfordert Nacharbeit trotz Solver-Lösung | eigentliches Solverergebnis (`Optimal` oder `Feasible`) | fachlicher Guard-Code, z. B. `reserve-robustness-needs-restore` | `false` |
| Fachliche Source-/Policy-/Robustheitsblockade | `Failed` | fachlicher harter Code, z. B. `source-*`, `policy-*`, `reserve-robustness-*` | `false` |

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
     - `format=kv1`-Werte dürfen keinen Doppelpunkt enthalten, damit der neue
       `kv1`-Parser eine einfache, kanonische `;`-/`=`-Grammatik behält. Die
       äußere Domain-Wireform trennt zwar nur am ersten `:`, aber Werte mit
       natürlichem Doppelpunkt (z. B. ISO-Zeitstempel oder URLs) müssen vor
       Persistierung in `kv1` percent-encoded werden; neue Parser dekodieren nur
       innerhalb von `format=kv1`. Preis-/Forecast-Slices planen aktuell keine
       `series_version` im `TerminationDetail`; falls ein späterer Source-Guard
       eine Version dort auditieren muss, gilt dieselbe Percent-Encoding-Regel.
     - Neue `format=kv1`-`TerminationDetail`-Strings dürfen maximal 1024 Zeichen
       lang sein. Längere Details werden vor Persistierung als Guard-/Schemafehler
       abgelehnt; Slices dürfen keine stillen Trunkierungen erzeugen.

3. Persistenz und Wire
   - `can_execute` in allen produktiven Stores hinzufügen:
     - `schema/schema.yaml` erweitern,
     - d-migrate-generierte RunOnce-Migration `000N_*.sql` erzeugen,
     - `DapperOptimizationRunRepository` Read/Write-Mapper aktualisieren,
     - In-Memory-Repository und Persistenztests synchron anpassen.
   - `termination_reason`-Kapazität im selben Pre-Slice migrieren:
     - Ausgangslage: die bestehende Wireform persistiert `TerminationCode` und
       `TerminationDetail` als `code:detail` in
       `optimization_runs.termination_reason`; `schema/schema.yaml` und
       `0001_initial.sql` begrenzen diese Spalte aktuell auf 256 Zeichen.
     - Vor produktiver Erzeugung von `format=kv1`-Details muss
       `schema/schema.yaml` `termination_reason.max_length` entfernen oder auf
       mindestens `2048` erhöhen, und d-migrate muss eine passende RunOnce-
       Migration erzeugen (`VARCHAR(2048)` oder `TEXT`).
     - Die Detail-Obergrenze von 1024 Zeichen gilt erst nach dieser
       Spaltenmigration; sie ist so zu testen, dass die komposierte Form
       `TerminationCode:TerminationDetail` vollständig in die migrierte Spalte
       passt.
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
   - Roundtrip-Test ergänzen: `TerminationCode` plus
     `TerminationDetail=format=kv1;reason=...;solver_code=...` muss nach
     Persistierung, `ParseTerminationReason` und erneuter Ausgabe bytegleich
     erhalten bleiben; ein Detailwert mit unescaped `:` wird abgewiesen.
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
       inkl. unverändert required `ProducedSchedule != null`,
     - Nicht-Solution-Status + `CanExecute=false`,
     - Nicht-Solution-Status + `CanExecute=true` wird abgewiesen.
   - Domain-Invariantentest: `Optimal`/`Feasible` + `CanExecute=false` ohne
     `ProducedSchedule` wird weiterhin vom `OptimizationRun`-Konstruktor
     abgewiesen.
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
Solver-spezifische Audit-Metadaten wie `solver_scope` sind nicht Bestandteil
dieses Pre-Slices; falls ein Slice sie ohne Request-Snapshot replayfähig im Run
persistieren muss, braucht es einen separaten Audit-Pre-Slice. Bis dahin gilt:
produktive Replays ohne immutable Request-Snapshot sind nicht freigegeben.

---

## Definition of Done (DoD)

- [ ] `OptimizationRun` trägt `CanExecute` als persistiertes Domain-Feld.
- [ ] Guard-Pipeline bzw. Computed-Combiner ist als API-Vertrag eingeführt und
  berechnet `CanExecute` aus den Slice-Beiträgen monoton.
- [ ] Alle Konstruktor-Aufrufer, Factories, Tests und Fixtures sind migriert.
- [ ] Store-/Wire-/API-/Proto-Mappings führen `can_execute`; die persistierte
  `termination_reason`-Wireform ist auf mindestens 2048 Zeichen oder `TEXT`
  migriert.
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
