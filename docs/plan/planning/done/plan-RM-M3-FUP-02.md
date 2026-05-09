# Plan RM-M3-FUP-02 — Optimistic Schedule Replace

**Dokumenttyp:** Slice-Plan (Folge-Slice)
**Status:** Abgeschlossen — RM-M3-FUP-02 ✅ alle Arbeitspakete (FUP-02-01..07) umgesetzt; CAS via `expectedBaseVersion` auf `IScheduleRepository.Replace` ist im Hexagon und in der Dapper-Variante aktiv, Use-Case fängt Konflikte als `Failed`-Run mit `TerminationCode = "concurrent-version-conflict"`, Tests pinnen Insert-Pfad-Konflikt, Update-Pfad-Konflikt mit Rollback, Use-Case-Materialisierung und das Wiring (Replace mit gelesenem `existing.Version`).
**Bezug:**
[`../open/note-RM-M3-followups.md`](../open/note-RM-M3-followups.md) Block B Item 6 (Trigger-Watch-Eintrag mit Status-Marker),
[`../in-progress/plan-RM-M4.md`](../in-progress/plan-RM-M4.md) (RM-M4-01 ist der erste konkrete Konsument; verlangt Commit-Lock + CAS auf der erwarteten Schedule-Version — siehe `plan-RM-M4.md` §Aktivierungsbedingungen Z. 94 und §Arbeitspakete RM-M4-01 Z. 164),
[`plan-RM-M2-optimization.md`](plan-RM-M2-optimization.md) (RM-M2-OP-OPEN-05 als ursprüngliche Quelle, jetzt ✅),
[`plan-RM-M3.md`](plan-RM-M3.md) (M3-Master-Plan, historischer Anker; die ursprüngliche „M2-Folgearbeit Mit M3-Trigger"-Tabelle ist heute ein Stub-Absatz mit Verweis auf die Open-Note Block B),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md) (LH-PERSIST-003 versionierte Fahrpläne, LH-OPT-009 Reproduzierbarkeit, LH-NF-005 Verfügbarkeit)

---

## 1. Zweck

`IScheduleRepository.Replace(schedule)` ist heute bewusst
**unconditional** — der Doc-Kommentar nennt das beim Namen: zwei
parallele Optimize-Calls können denselben `Version=v3` lesen und
beide einen `v4` schreiben, der zweite Write überschreibt den ersten
still. M2 trägt das, weil eine per-`(asset, type)`-Semaphore in
`DefaultScheduleOptimizationUseCase` Single-Host-Deployments
serialisiert (Carve-out RM-M2-OP-OPEN-05).

Multi-Replica-Hosts und Intraday-Reoptimierung (RM-M4-01) brechen
diese Annahme:

- Die Semaphore lebt im Prozess; zwei Replicas haben zwei
  Semaphores und keinen koordinierten Lock.
- RM-M4-01 verlangt einen eindeutigen Commit-Lock pro
  `(asset_id, schedule_type)` plus optimistic CAS auf der
  erwarteten Schedule-Version (paraphrasiert aus
  `plan-RM-M4.md` §Aktivierungsbedingungen Z. 94 und §RM-M4-01
  Z. 164 — die Source-Lines tragen leicht abweichende
  Wordings, der gemeinsame Nenner ist „atomarer Replace mit
  CAS").

FUP-02 schließt die Lücke: `Replace` bekommt einen
`expectedBaseVersion`-Parameter, die Dapper-Variante erzwingt
CAS in der `ON CONFLICT … DO UPDATE`-Klausel, ein
Versionskonflikt wird als deterministisch auditierbarer
`Failed`-Run mit `TerminationCode = "concurrent-version-conflict"`
plus strukturiertem `TerminationDetail` an die Application-Schicht
weitergereicht.

Der Slice ist **rein optimistisch** (CAS, kein verteiltes Lock):
das passt zur Erwartung „seltene Konflikte; Konflikt ⇒ aufgelöste
Konsequenz statt Wartezeit". Verteilte Locks (Postgres-Advisory,
Redis-Mutex) bleiben separater Slice falls das Konfliktraten-Profil
das fordert.

---

## 2. Aktivierungsbedingungen

| Check | Erwartung | Stand heute |
|-------|-----------|-------------|
| M2-Persistence-Pfad grün | Dapper-`IScheduleRepository` und Migrations-Pipeline laufen reproduzierbar | ✅ (M2-Closure) |
| `schedules.version`-Spalte existiert | INTEGER NOT NULL, PK `(asset_id, type)` | ✅ (`0001_initial.sql` Z. 53–59) |
| `OptimizationSolverStatus.Failed` + `TerminationCode`/`TerminationDetail` vorhanden | M2-OP-04 hat den Run-Auditierungs-Pfad inklusive kebab-case-Code + optionalem Detail (`OptimizationRun.cs` Z. 51–57) | ✅ |
| Migrations-Tooling für additive Schema-Änderung verfügbar | RM-M2-MIG-* mit d-migrate + DbUp | ✅ |
| **Kein Schema-Bedarf für CAS selbst** | `version`-Spalte existiert bereits, kein neues Constraint nötig für reine CAS | ✅ — siehe §6 zur Korrektur des Open-Note-Eintrags |
| RM-M4-01 als nachgelagerter Konsument klar | Plan-RM-M4 nennt CAS-Pflicht | ✅ |

---

## 3. Scope

**In Scope:**

- `IScheduleRepository.Replace(Schedule schedule, int expectedBaseVersion)` —
  Signatur-Erweiterung um den expected-Version-Parameter. Aufrufer
  liefert die Version, die der Read-vor-Replace gesehen hat;
  Repository erzwingt CAS. `expectedBaseVersion = 0` bedeutet
  „keine Vorgängerversion" (Insert-Pfad); `> 0` bedeutet
  „erwarte genau diese Version als Base" (Update-mit-CAS).
- `InMemoryScheduleRepository.Replace`-Implementierung mit
  Versions-Vergleich; Mismatch → `ScheduleConcurrencyConflictException`
  mit getypten `Expected`/`Actual`-Properties.
- `DapperScheduleRepository.Replace`-Implementierung. Heute ist
  Replace ein `INSERT … ON CONFLICT (asset_id, type) DO UPDATE`-
  Upsert (`DapperScheduleRepository.cs:23–29`); FUP-02 splittet
  zwei explizite Pfade gated auf `expectedBaseVersion`:
  - `expectedBaseVersion == 0` → `INSERT … ON CONFLICT DO NOTHING`
    (oder pre-`SELECT … FOR UPDATE`-Probe + Insert in der
    Transaction — Variante im PR begründet); `RowsAffected == 0`
    bedeutet „Zeile existiert bereits, Caller hatte aber 0 erwartet"
    → Conflict-Exception mit `Actual = bestehende Version`.
  - `expectedBaseVersion > 0` → reiner `UPDATE schedules SET …
    WHERE asset_id = @a AND type = @t AND version = @expected`;
    `RowsAffected != 1` → Conflict-Exception. Die Header-`UPDATE`-
    Klausel ist die CAS-Predikat-Stelle, **nicht** die `ON
    CONFLICT`-Klausel des heutigen Upserts (sonst würde der
    Insert-Pfad bei einem stale `expectedBaseVersion` still
    durchgehen).
  - Schedule-Windows-Replace (`DELETE … WHERE asset_id/type` plus
    Loop-Insert) bleibt im selben Transaction-Scope — der Pfad
    ist branch-unabhängig.
- `DefaultScheduleOptimizationUseCase` fängt die
  Conflict-Exception und persistiert einen
  `OptimizationSolverStatus.Failed`-Run mit
  `TerminationCode = "concurrent-version-conflict"` und
  `TerminationDetail = "expected=v3,actual=v4"` (kebab-case-Code,
  ≤64 Zeichen, kein `:` im Code — Schema-Constraints aus
  `OptimizationRun.cs` Z. 99–126).
- Tests: Unit-Tests für InMemory-CAS (zwei Replace-Calls auf
  derselben Base-Version, einer scheitert deterministisch),
  Dapper-Integrationstest gegen Postgres mit zwei parallelen
  Schreibern, Application-Test der den Conflict-Pfad als
  Failed-Run beobachtet.

**Out of Scope (separate Slices):**

- **UNIQUE-Constraint auf `(asset_id, type, version)`** — wäre
  Defense-in-Depth gegen Schema-Drift, aber für die reine
  CAS-Mechanik nicht nötig (Primary-Key `(asset_id, type)` plus
  `version`-Update genügt). Ein
  vorbereiteter Draft existiert bereits unter
  `Migrations/Drafts/0002_schedules_optimistic_concurrency.sql`
  (Build-Action `None`, demonstriert Workflow, nicht aktiv);
  bei Aktivierung wäre der Draft-Header zu prüfen, ob das
  Constraint überhaupt nötig ist (PK + `version`-Spalte
  implizieren ihn schon im aktuellen Single-Row-Modell). Falls
  eine spätere Welle ein echtes Schedule-Versionierungs-Modell
  mit historischer Retention einführt, kommt das Constraint
  dort.
- **Verteilter Lock** (Postgres-Advisory-Lock, Redis-Mutex,
  Zookeeper-Lease) — der CAS-Pfad ist optimistisch; bei seltenen
  Konflikten ist „Konflikt → Failed-Run mit klarem
  TerminationCode" das richtige Verhalten. Hohe Konfliktraten
  (z. B. Multi-Tenant mit ständig parallelen Optimize-Calls)
  würden einen verteilten Lock als eigenen Slice triggern.
- **Migration auf Schema-Versionierung** — falls FUP-01 (erste
  echte Folgemigration) parallel zu FUP-02 zündet, kann das
  Bündel-Slice ein zusätzliches Constraint mitnehmen. Aber
  FUP-02 selbst trägt **keine** Schema-Änderung und bündelt
  nicht zwingend mit FUP-01.
- **Retention/Versions-Historie** der ersetzten Schedules — heute
  ist `Replace` destruktiv (M1-Vertrag); ein Audit-Log alter
  Versionen ist eigener Slice.
- **Schedule-Read mit Version-Stempel als API-Wrapper** — die
  Caller müssen die beim Read gelesene `version` durch ihre
  Optimize-Pipeline tragen; eine API-Shape die Version + Schedule
  als ein Wrapper liefert ist Verbesserung-für-später, nicht
  FUP-02.
- **Auto-Retry nach CAS-Konflikt** — der Use-Case persistiert
  beim Konflikt einen Failed-Run mit `concurrent-version-conflict`
  und kehrt zurück. Ein automatisches Re-Read + Re-Optimize ist
  bewusst **nicht** Teil von FUP-02: bei seltenen Konflikten ist
  „Operator/Scheduler entscheidet über Retry" das richtige
  Verhalten. Wenn Telemetrie hohe Konfliktraten zeigt, wird
  Auto-Retry eigener Folge-Slice (Backoff-Strategie,
  Max-Attempts, Idempotenz-Garantien — nicht trivial,
  separat planen).

---

## 4. Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | FUP-02-01 | `IScheduleRepository.Replace`-Signatur erweitern | Methodensignatur erhält `int expectedBaseVersion`-Parameter (`< 0` → `ArgumentOutOfRangeException` am Vertrags-Boundary); Doc-Kommentar erklärt die CAS-Semantik (`0` = neue Schedule / Insert; `> 0` = erwarte genau diese Base; Mismatch → Exception, kein Silent-Overwrite). Alle fünf Sync-Aufrufer im Repo sind auf den neuen Vertrag aktualisiert: `src/host/BatteryEms.Host/BessConfigurationBootstrap.cs:74` (Bootstrap-Seed → `expectedBaseVersion = 0`), `src/hexagon/BatteryEms.Application/Api/DefaultScheduleOptimizationUseCase.cs:193` (übergibt `baseVersion` aus Z. 172, **nicht** `result.ProducedSchedule.Version`), `tests/hexagon/BatteryEms.Application.Tests/InMemoryScheduleRepositoryTests.cs:26, 29, 39`, `tests/hexagon/BatteryEms.Application.Tests/DefaultScheduleOptimizationUseCaseTests.cs:47`, `tests/adapters/driving/BatteryEms.Api.Tests/SchedulesEndpointTests.cs:39`. Die zwei Async-Aufrufer auf der konkreten Klasse `DapperScheduleRepository.ReplaceAsync` (`tests/integration/BatteryEms.Persistence.IntegrationTests/PersistenceRoundtripTests.cs:133, 141`) sind ebenfalls auf den neuen Parameter umgestellt — `ReplaceAsync` bekommt analog eine erweiterte Signatur. |
| ✅ | FUP-02-02 | `InMemoryScheduleRepository`-Implementierung | Liest die aktuelle Version aus dem internen Dictionary, vergleicht mit `expectedBaseVersion`; Mismatch → `ScheduleConcurrencyConflictException` mit getypten `Expected`/`Actual`-Properties. Test pinnt: zwei Replaces auf derselben Base-Version, der zweite scheitert; Re-Read + Replace mit aktualisierter Base gewinnt. |
| ✅ | FUP-02-03 | `DapperScheduleRepository`-Implementierung mit branched CAS | SQL wird vom heutigen Upsert (`INSERT … ON CONFLICT DO UPDATE`) auf zwei explizite Pfade umgestellt, gated auf `expectedBaseVersion`: (a) `== 0` → Insert-Pfad mit `INSERT … ON CONFLICT (asset_id, type) DO NOTHING`, `RowsAffected == 0` ⇒ Conflict-Exception mit `Actual = bestehende Version` (per `SELECT version`-Probe in derselben Transaction nachgelesen). (b) `> 0` → Update-Pfad mit `UPDATE schedules SET market_bid_area = @MarketBidArea, version = @Version WHERE asset_id = @AssetId AND type = @Type AND version = @expectedBaseVersion`, `RowsAffected != 1` ⇒ Conflict-Exception mit nachgelesenem `Actual`. Schedule-Windows-Replace (`DELETE … + INSERT-Loop`) bleibt im selben Transaction-Scope und ist branch-unabhängig. Migrations-Pfad bleibt unverändert (kein Schema-Change). |
| ✅ | FUP-02-04 | `ScheduleConcurrencyConflictException` als Domain-Type | Neue Exception in `BatteryEms.Application.Markets`, trägt `AssetId` / `ScheduleType` / `ExpectedBaseVersion` / `ActualVersion` als getypte Properties (nicht nur Message). Architektur-Tabu-Test verifiziert dass die Exception nur in `BatteryEms.Application.Markets` (Definition), `BatteryEms.Adapters.Persistence` (Wurf-Stelle) und `BatteryEms.Application.Api` (Use-Case-Catch) referenziert wird; alle anderen Adapter (`Driving`, `Optimization`, `Telemetry`, `NativeInterop`, etc.), `Domain`, `Host`, `Worker` und `Infrastructure` dürfen die Exception nicht referenzieren. |
| ✅ | FUP-02-05 | Conflict-Pfad in `DefaultScheduleOptimizationUseCase` | Mechanik existiert bereits: `ExecuteUnderLockAsync` Z. 172 berechnet `var baseVersion = existing?.Version ?? 0;` — derselbe lokale Wert muss in den `Replace`-Aufruf Z. 193 weitergeleitet werden (nicht `result.ProducedSchedule.Version`, sonst no-op-CAS). Use-Case fängt `ScheduleConcurrencyConflictException` aus dem Replace-Aufruf und materialisiert einen `OptimizationSolverStatus.Failed`-Run mit `TerminationCode = "concurrent-version-conflict"` und `TerminationDetail = $"expected={expected},actual={actual}"`. Code respektiert `OptimizationRun.cs`-Validierung (kebab-case, ≤64 Zeichen, kein `:` im Code; `=` statt `:` als Separator im Detail-Body bleibt sauber). Run wird über `IOptimizationRunRepository.AppendAsync` persistiert (M2-OP-04-Pfad unverändert). Bestehende per-`(asset, type)`-Semaphore bleibt: sie entlastet die Datenbank vom Trivialfall (zwei concurrent Calls **im selben Host**) und liefert dort schnell-fehlschlagende `SemaphoreSlim`-Ordnung; CAS deckt den Multi-Replica-Fall, wo die Semaphores zweier Prozesse nicht koordinieren. |
| ✅ | FUP-02-06 | Tests: Unit + Dapper-Integration + Application-Use-Case | (a) `InMemoryScheduleRepositoryTests` mit Concurrency-Pin (zwei Replaces, einer scheitert deterministisch). (b) Neuer Test im Projekt `tests/integration/BatteryEms.Persistence.IntegrationTests` (analog zu `PersistenceRoundtripTests.Schedule_repository_replaces_full_window_set_atomically`) gegen echten Postgres-Container, zwei parallele Writer auf identischer Base-Version, deterministisch genau einer gewinnt; zusätzlich Test für `expectedBaseVersion = 0` mit existierender Zeile (Insert-Pfad-Konflikt). (c) `DefaultScheduleOptimizationUseCaseTests` pinnt den Failed-Run-Pfad mit `TerminationCode == "concurrent-version-conflict"` und überprüft das `TerminationDetail`-Format (`expected=…,actual=…`). (d) Architektur-Tabu-Test für den neuen Exception-Typ. (e) Wiring-Test: der Use-Case ruft `Replace(schedule, expectedBaseVersion)` mit dem **gelesenen** `existing.Version`, nicht mit `result.ProducedSchedule.Version`. |
| ✅ | FUP-02-07 | Doku-/Plan-Sync | `IScheduleRepository.cs`-Doc-Kommentar von „M2 relies on caller-side serialisation … RM-M2-OP-OPEN-05 to land first" auf „RM-M3-FUP-02 ✅: CAS via expectedBaseVersion" umstellen. `plan-RM-M2-optimization.md`-Open-Tabelle Zeile RM-M2-OP-OPEN-05 → ✅ mit Verweis auf diese Plan-Datei. `note-RM-M3-followups.md` Block B Item 6 Status-Marker setzen + Verweis auf den Done-Plan. Diese Plan-Datei selbst von `open/` nach `done/` verschieben. |

---

## 5. Akzeptanzkriterien

- `make gates` / `make ci` bleiben grün.
- Zwei parallele `Replace`-Calls auf derselben gelesenen
  `Version=v3` resultieren in **genau einem** `v4`-Erfolg auf der
  DB-Seite (Postgres-Integrationstest). Der Verlierer bekommt
  `ScheduleConcurrencyConflictException`.
- Insert-Pfad-Konflikt wird ebenso deterministisch gefangen:
  `Replace(s, expectedBaseVersion=0)` auf einer bereits
  existierenden Zeile `(asset, type)` wirft die Exception, statt
  still durchzugehen.
- Der Use-Case wandelt den Konflikt deterministisch in einen
  `OptimizationSolverStatus.Failed`-Run mit
  `TerminationCode = "concurrent-version-conflict"` und
  `TerminationDetail`-Trace (`expected=…,actual=…`) um —
  observierbar im `IOptimizationRunRepository`.
- Bootstrap-Seed (Single-Host-Start mit Schedule-File) funktioniert
  weiterhin: `expectedBaseVersion = 0` für die neue Schedule, danach
  inkrementell `1`/`2`/...
- Per-Prozess-Semaphore in `DefaultScheduleOptimizationUseCase`
  bleibt — entlastet die Datenbank vom Trivialfall (zwei concurrent
  Calls **im selben Host**) und liefert dort schnell-fehlschlagende
  Ordnung; CAS deckt den Multi-Replica-Fall, wo die Semaphores
  zweier Prozesse nicht koordinieren. Bestehende Single-Host-Tests
  laufen unverändert grün. **Nicht** als „redundant" entfernen.
- Persistence-Schema bleibt unverändert: kein neues Column,
  kein neues Constraint, keine neue Migration. Drift-Check
  (`make schema-drift-check`) bleibt grün.

---

## 6. Risiken und Tradeoffs

- **Optimistic vs. Pessimistic.** CAS ist optimistisch. Bei sehr
  hohen Konfliktraten (mehrere Replicas die im Sekundentakt
  optimieren) wäre das Konfliktrauschen hoch; ein verteilter Lock
  (Postgres-Advisory mit Key `hashtext(asset_id || type)`) wäre
  effizienter, aber teurer (extra Round-Trip, Lock-Lifecycle in
  jeder Replace-Operation). FUP-02 nimmt CAS und macht den Konflikt
  als deterministischen Failed-Run sichtbar; ein Folge-Slice darf
  bei Bedarf auf einen verteilten Lock upgraden.
- **Caller-API-Bruch.** `Replace` bekommt einen Pflicht-Parameter.
  Alle Aufrufer im Repo werden in FUP-02-01 angefasst (fünf Sync-
  plus zwei Async-Sites; Liste in FUP-02-01 vollständig).
  Externe Konsumenten existieren nicht (`IScheduleRepository` ist
  Driven Port der Application-Schicht, kein öffentliches API).
- **Open-Note-Ungenauigkeit.** Block-B-Item-6-Aktivierungspfad in
  `note-RM-M3-followups.md` behauptete in einer früheren Version:
  > „Triggert zusammen mit FUP-01 (Schema-Änderung) — FUP-02
  > produziert eine Schema-Erweiterung (`schedules.version`-
  > Constraint) die dem Migrationspfad einen ersten echten
  > Konsumenten gibt."

  Diese Aussage war falsch: das `version`-Feld existiert bereits
  in `0001_initial.sql`; CAS via `WHERE version = @expected`
  braucht keine Schema-Änderung, also auch keine FUP-01-Bündelung.
  Die Open-Note ist bei Anlage dieses Plans korrigiert worden;
  FUP-02-07 setzt zusätzlich den finalen Status-Marker.
- **Bootstrap-Seed-Sentinel.** `expectedBaseVersion = 0` als
  „neue Schedule, keine Vorgängerversion" ist die festgelegte
  Konvention — konsistent mit `DefaultScheduleOptimizationUseCase.cs`
  Z. 172 (`existing?.Version ?? 0`), das den gleichen Sentinel
  bereits heute auf der Lese-Seite benutzt. Der Vertrag verlangt
  `expectedBaseVersion >= 0` (`< 0` → `ArgumentOutOfRangeException`
  am Boundary). Eine Nullable-Variante (`int?`) wurde verworfen,
  weil der `0`-Sentinel im Codebase bereits etabliert ist.
- **Read-Optimize-Replace-Wiring.** Die Mechanik ist trivial:
  `ExecuteUnderLockAsync` berechnet `baseVersion` schon heute in
  Z. 172 als `existing?.Version ?? 0` und gibt ihn an den Solver
  (`ScheduleOptimizationRequest.BaseScheduleVersion`). FUP-02-05
  reicht denselben lokalen Wert in den `Replace`-Aufruf Z. 193
  durch — eine 1-Zeilen-Verdrahtung. Stolperfalle: `Replace` darf
  **nicht** mit `result.ProducedSchedule.Version` aufgerufen
  werden (no-op-CAS, weil der Solver beim Bauen des Schedules
  bereits `Version = baseVersion + 1` setzt). Test-Pflicht (siehe
  FUP-02-06 Punkt e): pinnen dass der Use-Case mit dem **gelesenen
  `existing.Version`** ruft, nicht mit dem produzierten.

---

## 7. Sequenz

1. **FUP-02-01** (Signatur-Erweiterung) zuerst — bricht den Build
   sauber, alle sieben Konsumenten müssen anschließend angepasst
   werden.
2. **FUP-02-02** (InMemory-Implementierung) + **FUP-02-04**
   (Exception-Type) gemeinsam — InMemory-Impl wirft die Exception,
   Tests pinnen die Round-Trip-Semantik ohne DB.
3. **FUP-02-03** (Dapper-Implementierung mit branched CAS) als
   nächster Slice — die SQL-Umstellung von Upsert auf zwei Pfade
   ist die größte technische Substanz des Slices.
4. **FUP-02-05** (Use-Case-Conflict-Pfad).
5. **FUP-02-06** (Integration-Tests gegen Postgres mit echten
   parallelen Writern) — letzte Verifikationsstufe, kann als
   Carve-out wenn der Slice zu groß wird.
6. **FUP-02-07** (Doku/Plan-Sync, Plan-Datei nach `done/`,
   Open-Note-Block-B-Item-6 markieren).

Bei Trigger durch RM-M4-01: FUP-02 läuft als Vorbedingungs-Slice;
RM-M4-01 referenziert die abgeschlossene Plan-Datei.
