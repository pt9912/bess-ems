# Plan RM-M2 Persistence-Migrations: vom Idempotent-Init zum versionierten Migrationspfad

**Dokumenttyp:** Aktiver Detailplan / M2-Folgewelle
**Status:** In Umsetzung — MIG-01..06 lokal abgeschlossen, Plan
liegt zwischen `open/` und `done/`, bis die Closure-Commits gepusht
und gegen die OP-OPEN-05/06-Aktivierung in M3 verprobt sind.
**Bezug:**
[`roadmap.md`](roadmap.md),
[`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md)
(§Open RM-M2-OP-OPEN-05/06 — die ersten echten Konsumenten dieses Plans),
[`../done/plan-RM-M1.md`](../done/plan-RM-M1.md) (RM-M1-13 hat
`BessDbInitializer` + `BessDbSchema.cs` als M1-Strategie sanktioniert),
[`spec/architecture.md`](../../../../spec/architecture.md) §11
(„Migrations-Strategie: EF Core Migrations oder FluentMigrator,
idempotent beim Worker-Start anwendbar"),
[`spec/lastenheft.md`](../../../../spec/lastenheft.md) (LH-PERSIST-001..007),
[`docs/user/quality.md`](../../../user/quality.md) §1.4 (Lock-File-
Discipline; das Migrations-Tooling teilt sich denselben
Supply-Chain-Kontext)

---

## Zweck

M2 betreibt das Postgres-Schema mit `BessDbSchema.CreateScript` plus
`BessDbInitializer`, beim Boot ausgeführt — `CREATE TABLE IF NOT EXISTS`
über alle Tabellen, ein einziges Skript, kein Versionsstand auf der
DB-Seite. Diese Strategie ist von der Architektur §11 explizit für M1
sanktioniert („idempotent beim Worker-Start anwendbar"). Sie reicht
für M2-Single-Host und keine produktiven Daten.

Mit dem M2-Abschluss zeichnen sich drei Limitierungen ab, die M3 zwingend
addressieren muss:

1. **Keine Schema-Versionierung.** Eine laufende DB sagt nicht, welche
   Schema-Generation sie bedient. Reviews, Backfills, Rollback-Pläne
   haben keinen Anker.
2. **Multi-Replica-Boot-Race.** RM-M2-OP-OPEN-05 (Optimistic-Concurrency
   in `IScheduleRepository.Replace`) braucht eine `UNIQUE`-Constraint
   auf `schedules(asset_id, type, version)` — eine echte Schema-Änderung,
   keine zusätzliche `IF NOT EXISTS`-Tabelle. Dasselbe Schema-Diff
   gleichzeitig aus N Repliken laufen zu lassen funktioniert mit dem
   heutigen Bootstrap-Pfad nicht zuverlässig.
3. **Schema-Drift unsichtbar.** Spalten umzubenennen, Typen zu ändern
   oder Indizes hinzuzufügen geht heute nur über Down-and-Up. Das
   blockiert jedes Schema-Refactoring im Live-Betrieb.

Dieser Plan kondensiert die Tooling-Entscheidung und das Arbeitspaket,
um `BessDbInitializer` durch einen versionierten Migrationspfad zu
ersetzen, **bevor** M3 die ersten echten Schema-Änderungen mitbringt.

---

## Abgrenzung gegen M2

- M2-Tabellen bleiben unverändert: `BessDbSchema.CreateScript` wird
  Wort-für-Wort zur **Migration `0001_initial.sql`**. Eine leere DB
  durch beide Wege (heute Bootstrap, künftig Migration 0001) muss
  bit-identische `\d+`-Dumps liefern (siehe MIG-03).
- Keine Daten-Migration: M2 hat keine produktiven Daten. Der Cut-Over
  ist „bestehender Initializer-Aufruf wird durch Migrator-Aufruf
  ersetzt". Integrationstests bleiben rot/grün-stabil weil sie
  `TruncateAll` schon haben.
- M2-Optimization-Plan-Status (alle OP-01..09 abgeschlossen) bleibt
  abgeschlossen — dieser Plan dockt am Ende an, nicht parallel.

---

## Komponenten

| Bereich         | Artefakt                                                       | LH-Bezug             |
| --------------- | -------------------------------------------------------------- | -------------------- |
| Schema-Quelle   | `schema/schema.yaml` — neutrales Schema im d-migrate-Format    | LH-PERSIST-007       |
| Build-Time      | d-migrate als Docker-Container (`ghcr.io/pt9912/d-migrate:<digest-pinned>`); `make schema-validate` + `make schema-generate` | LH-PERSIST-007 |
| Persistence     | Runtime-Apply via DbUp (siehe MIG-01)                          | LH-PERSIST-007       |
| Persistence     | Explizit konfigurierte `__schema_versions`-Tracking-Tabelle    | LH-PERSIST-007       |
| Persistence     | `BessDbMigrator` (ersetzt `BessDbInitializer`)                 | LH-PERSIST-007       |
| Persistence     | `src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/RunOnce/` Embedded-Resource-Verzeichnis: `0001_initial.sql`, `0002_*.sql`, … (vom d-migrate-Build erzeugt); `.../Migrations/Drafts/` bleibt nicht eingebettet | LH-PERSIST-007 |
| Tests           | Snapshot-Test: Bootstrap-Stand vs. Migration-0001-Stand        | LH-TEST-001/002      |
| Tests           | Idempotenz-Test: zweimaliges `MigrateAsync` ohne Effekt        | LH-TEST-002          |
| Tests           | Drift-Check: `make schema-generate` gegen den committeten `0001_initial.sql` ergibt einen leeren git-Diff (CI-Gate) | LH-TEST-002 |
| ADR             | `docs/plan/adr/0001-persistence-migrations.md` (erstes ADR im Repo) | spec/architecture §11 |

---

## Arbeitspakete

| Status | ID            | Paket                                                         | Abhängigkeit              | DoD                                                                                                                                                                                                                                          |
| ------ | ------------- | ------------------------------------------------------------- | ------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| ✅     | RM-M2-MIG-01  | ADR `0001-persistence-migrations.md` (erstes ADR im Repo)     | —                         | Erledigt durch [`../../adr/0001-persistence-migrations.md`](../../adr/0001-persistence-migrations.md). Trennt **Build-Time-DDL-Generierung** von **Runtime-Apply** und vergleicht beide Achsen separat. Build-Time: **d-migrate** (Kotlin-CLI, neutraler YAML→DDL-Generator, image-pinned per `ghcr.io/pt9912/d-migrate:<version>`) **vs.** Hand-SQL. Runtime-Apply: **DbUp**, **FluentMigrator**, **EF Core Migrations**, **selbstgeschriebener .NET-Runner**. Entscheidung **d-migrate (Build) + DbUp (Apply)** als Hybrid (OPEN-01). ADR enthält ein Tool-Verifikations-Gate: Container pullbar, Digest pinbar, Commands `schema reverse`, `schema validate` und `schema generate` vorhanden, und Proof-of-Concept bildet alle aktuellen DDL-Features ab (FK `ON DELETE CASCADE`, CHECK-Constraints, Indizes, `TIMESTAMPTZ`, `BIGSERIAL`, UUID). Begründung pro Achse, Konsequenzen, Status „Accepted". |
| ✅     | RM-M2-MIG-02  | Tooling-Setup: d-migrate (Build) + DbUp (Apply)               | RM-M2-MIG-01              | **Build-Seite:** `schema/schema.yaml` als zentrale Schema-Quelle im d-migrate-YAML-Format (Tables, Columns, PKs, Indices, Constraints, Foreign Keys); vor der Produktivverdrahtung läuft ein verifizierender Smoke: `docker pull ghcr.io/pt9912/d-migrate:<pinned-version>@sha256:<digest>`, `d-migrate schema reverse --help`, `d-migrate schema validate --help`, `d-migrate schema generate --help` und ein Mini-Schema mit FK-Cascade, CHECK, Index, `TIMESTAMPTZ`, `BIGSERIAL`, UUID round-tript zu gültigem Postgres-DDL. `make schema-validate` und `make schema-generate` rufen dasselbe digest-gepinnte Image auf — Image-Pin parallel zur NuGet-Lock-File-Disziplin (siehe `docs/user/quality.md §1.4`). Erzeugt versionierte SQL-Files unter `src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/RunOnce/`. **Apply-Seite:** NuGet-Package `dbup-postgresql` zentral in `Directory.Packages.props` gepinnt + im Lock-File; `src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/RunOnce/`-Ordner mit Embedded-Resource-Glob **nur** für `????_*.sql`; `src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/Drafts/` ist Build-Action `None` und wird vom Migrator nicht geladen. `BessDbMigrator` mit `MigrateAsync(CancellationToken)`-API; bestehender `BessDbInitializer` markiert `[Obsolete]` mit Migrationspfad-Hinweis. |
| ✅     | RM-M2-MIG-03  | `schema.yaml` + Migration `0001_initial.sql` als Snapshot     | RM-M2-MIG-02              | `bess-ems/schema/schema.yaml` (per d-migrate-Reverse-Engineering aus einer temporären Postgres-DB, die mit dem heutigen `BessDbSchema.CreateScript` initialisiert wurde, danach manuell gepflegt); `make schema-generate` erzeugt `0001_initial.sql` daraus. Temporärer Cut-Over-Snapshot-Test: Postgres-Container, leere DB → 0001 anwenden → `pg_dump --schema-only` liefert dasselbe DDL wie `BessDbInitializer.InitializeAsync()` auf einer separaten leeren DB, **normalisiert um die Migrator-Journal-Tabelle `__schema_versions`**. Zusätzlich: `make schema-validate` als CI-Gate (d-migrate-Validierung ohne DB-Aufruf). Erst dann gilt der Snapshot als äquivalent. Test wird in MIG-05 entfernt oder durch einen reinen „Migration 0001 erzeugt erwartete Tabellen/Indizes"-Smoke ersetzt. |
| ✅     | RM-M2-MIG-04  | Idempotenz, Versionskontinuität + Lock-Test                   | RM-M2-MIG-02              | Integrationstest in `BatteryEms.Persistence.IntegrationTests`: leere DB → `MigrateAsync` → `__schema_versions` enthält `0001_initial`; erneutes `MigrateAsync` ist no-op. `BessDbMigrator` validiert vor DbUp, dass eingebettete RunOnce-Migrationen bei `0001` starten, lückenlos numeriert sind und keine doppelte Nummer tragen; Test-Seam mit `0002_*` ohne `0001_*` bzw. `0001_*` + `0003_*` wirft vor der DbUp-Ausführung. Zwei parallele `MigrateAsync`-Aufrufe gegen dieselbe DB werden über `pg_advisory_lock(hashtextextended('bess-ems:migrations', 0))` serialisiert; Test belegt, dass nur ein Lauf die Scripts anwendet und die zweite Replica danach no-op ist. |
| ✅     | RM-M2-MIG-05  | Cut-Over: Aufrufer von `BessDbInitializer` umstellen          | RM-M2-MIG-02..04          | `BessHostBuilder` ruft `BessDbMigrator.MigrateAsync` statt `BessDbInitializer.InitializeAsync`; alle 8 bestehenden Persistence-Integrationstests + Modbus/MQTT-Integration durchlaufen ohne Anpassung. Der temporäre MIG-03-Initializer-Vergleich wird entfernt oder entkoppelt, dann werden `BessDbInitializer` + `BessDbSchema.CreateScript` gelöscht (zentrale Schema-Quelle ist ab jetzt `0001_initial.sql`). |
| ✅     | RM-M2-MIG-06  | Vorlagen-Migration für RM-M2-OP-OPEN-05 (draft)               | RM-M2-MIG-05              | Optionaler Entwurf `src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/Drafts/0002_schedules_optimistic_concurrency.sql` als **nicht eingebettete** Datei (Build-Action `None`) oder als Plan-Snippet unter `docs/plan/planning/open/`; CI-Test aus MIG-02 belegt, dass Drafts nicht in den DbUp-Script-Set gelangen. Die echte `0002_*.sql` wird erst committed, wenn OPEN-05 als RM-M3-Item zieht. |

---

## Entscheidungen (geschlossene Punkte)

Alle sechs Punkte sind mit MIG-01..06 entschieden **und umgesetzt** —
die OPEN-NN-IDs bleiben für Cross-Refs aus dem Optimization-Plan und
dem ADR erhalten.

| Kennung           | Frage                                                                                          | Entscheidung |
| ----------------- | ---------------------------------------------------------------------------------------------- | ----------------- |
| RM-M2-MIG-OPEN-01 | Build-Time-DDL-Generator + Runtime-Apply-Tool — welche Kombination? | **Geschlossen durch ADR 0001 + MIG-01/02: Hybrid d-migrate (Build) + DbUp (Apply).** Begründung in zwei Achsen: (1) Build-Time: d-migrate ist ein neutraler YAML→DDL-Generator (Postgres/MySQL/SQLite) mit Schema-Validierung, Schema-Compare und Reverse-Engineering. Für bess-ems heißt das: eine reviewbare Schema-Quelle in YAML, mechanische Initial-Befüllung per `d-migrate schema reverse`, automatisierter DDL-Drift-Check als CI-Gate. Hand-SQL als Alternative spart das zusätzliche Tool, verliert aber Validierung, Cross-DB-Portabilität (relevant wenn perspektivisch SQLite-Tests oder MySQL-Profile dazukommen) und das Reverse-Engineering, das den Cut-Over erst sauber macht. (2) Runtime-Apply: **DbUp** matcht den Dapper-Stack (SQL-First, kein C#-DSL, kein EF-ORM); FluentMigrator hat einen schwereren C#-DSL-Footprint, den ein per-d-migrate-erzeugtes Schema nicht braucht; EF Core Migrations zwingen das Repo in einen ORM-Stack, den wir bewusst gemieden haben; ein selbstgeschriebener .NET-Runner wäre eine Re-Erfindung des DbUp-Funktionsumfangs (Tracking-Tabelle, idempotente Apply-Order, Embedded-Resource-Loader). |
| RM-M2-MIG-OPEN-02 | Forward-only vs. reversible Migrations                                                         | **Geschlossen durch MIG-04: Forward-only.** Ops-only-Deployment, kein Dev-Use-Case für Down-Migrations, kein Rollback-Wunsch im Lastenheft — die `EnsureRunOnceContinuityValid`-Preflight verlangt explizit eine monoton wachsende `0001..N`-Sequenz und schließt Down-Migrations damit auch tooling-seitig aus. |
| RM-M2-MIG-OPEN-03 | Wie wird die Tracking-Tabelle benannt?                                                          | **Geschlossen durch MIG-02: `__schema_versions` (lowercase snake_case).** Implementiert in `BessDbMigrator.RunDbUp` via `JournalToPostgresqlTable(schema: null, table: "__schema_versions")` — keine quoting-pflichtigen Mixed-Case-Identifier, nicht auf DbUp-Provider-Defaults verlassen. Das `__`-Präfix entkoppelt die Tracking-Tabelle vom Domain-Schema und ist leicht aus Backups/Schema-Diffs exkludierbar. |
| RM-M2-MIG-OPEN-04 | Wie wird Migration in CI getestet?                                                              | **Geschlossen durch MIG-05: bestehende `test-integration`-Stage erweitert.** Kein neuer Compose-Service: `PersistenceRoundtripTests.InitializeAsync` ruft jetzt `BessDbMigrator.MigrateAsync`, alle Persistence-Integrationstests prüfen damit implizit die End-to-end-Migration. `BessDbMigratorIntegrationTests` deckt zusätzlich Journal-Eintrag, Idempotenz und Parallel-Lock direkt ab. |
| RM-M2-MIG-OPEN-05 | Migration-Lifecycle: jede neue Tabelle/Spalte als eigene `000N`-Datei vs. monatliche Bündelung | **Geschlossen durch MIG-02/06: eine `000N`-Datei pro logischer Änderung.** Embedded-Resource-Glob `Migrations/RunOnce/????_*.sql` beschränkt eine Migration auf genau eine SQL-Datei, der MIG-06-Draft `Drafts/0002_schedules_optimistic_concurrency.sql` zeigt das Per-Change-Pattern. Bündelung sammelt unzusammenhängende Änderungen in einer schwerer zu reviewenden Migration und ist damit ausgeschlossen. |
| RM-M2-MIG-OPEN-06 | Multi-Replica-Boot-Race: nur eine Replica migriert, oder Lock-Pfad?                             | **Geschlossen durch MIG-04: Lock-Pfad im Migrator.** `BessDbMigrator` nimmt vor DbUp den Postgres-Advisory-Lock `pg_advisory_lock(hashtextextended('bess-ems:migrations', 0))` und gibt ihn im `finally` frei; DbUp-Journaling bleibt für „welche Scripts liefen schon" zuständig, nicht für globale Replica-Serialisierung. `BessDbMigratorIntegrationTests.Two_parallel_MigrateAsync_calls_serialize_via_advisory_lock` belegt das gegen die echte Postgres-Instanz. |

---

## Anschluss an OP-OPEN-05 / OP-OPEN-06

Beide M3-Folgepakete aus dem
[Optimization-Plan](../done/plan-RM-M2-optimization.md) hängen
**direkt** von diesem Migrationspfad ab; mit MIG-05 grün ist der
Migrationspfad freigeschaltet, beide OPEN-Items können als RM-M3-OP-…
aktiviert werden.

- **OP-OPEN-05** (Optimistic-Concurrency in `IScheduleRepository.Replace`):
  Die erste echte (nicht-Snapshot-) Migration und damit der Validator
  des Tooling-Pfads. Stub liegt bereits unter
  [`Migrations/Drafts/0002_schedules_optimistic_concurrency.sql`](../../../../src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/Drafts/0002_schedules_optimistic_concurrency.sql)
  (MIG-06, Build-Action `None`, vom Migrator nicht geladen). Der M3-
  Review entscheidet, ob die ursprünglich angedachte
  `UNIQUE (asset_id, type, version)`-Spalte tatsächlich verdrahtet
  wird — sie ist im aktuellen Single-Row-pro-(asset,type)-Modell mit
  PK `(asset_id, type)` logisch impliziert; der Replace-Pfad braucht
  zwingend nur die `expected_version`-Semantik in der Dapper-SQL
  (`UPDATE … WHERE version = @expected`, 0 affected rows ⇒
  `concurrent-version-conflict`).
- **OP-OPEN-06** (Lock-Table-Eviction): falls die LRU/TTL-Eviction eine
  Hilfstabelle für Telemetrie braucht (z.B. „letzte Optimize-Aktivität
  pro Asset"), kommt das ebenfalls als Folge-Migration. Bis zur M3-
  Aktivierung wird kein Stub vorgeschattet — Schema offen, weil das
  Eviction-Design noch nicht geschlossen ist.

---

## d-migrate-Integration (Build-Seite)

d-migrate ist ein extern gepflegtes Kotlin-CLI, das aus einer neutralen
`schema.yaml` versionierte DDL-Files für Postgres (und perspektivisch
MySQL/SQLite) erzeugt. bess-ems integriert es wie folgt:

- **Schema-Quelle**: `schema/schema.yaml` im Repo, einmalig per
  `d-migrate schema reverse` aus einer temporären Postgres-DB initial
  befüllt, die vorher mit dem heutigen `BessDbSchema.CreateScript`
  erzeugt wurde, danach von Hand gepflegt — die YAML ist die kanonische
  Quelle, das committete `0001_initial.sql` ihr generiertes Artefakt.
- **Tool-Verifikation**: Bevor MIG-02 d-migrate als Build-Gate verdrahtet,
  wird das digest-gepinnte Image gepullt und die erwarteten CLI-Kommandos
  (`schema reverse`, `schema validate`, `schema generate`) werden gegen
  `--help` geprüft. Ein Mini-Schema muss FK `ON DELETE CASCADE`,
  CHECK-Constraints, Indizes, `TIMESTAMPTZ`, `BIGSERIAL` und UUID in
  valides Postgres-DDL übersetzen.
- **CI-Gates**: `make schema-validate` (statische YAML-Prüfung, ohne
  DB) und `make schema-generate` mit anschließendem leerem
  `git diff`-Check (Drift zwischen YAML und committeter SQL ist ein
  Build-Fehler).
- **Image-Pin**: `ghcr.io/pt9912/d-migrate:<version>@sha256:<digest>`
  fest in einer `Makefile`-Variable; das Digest-Pin matcht die
  NuGet-Lock-File-Disziplin aus `docs/user/quality.md §1.4`. Eine
  Versionserhöhung ist ein bewusster Commit, kein schleichendes
  Tag-Movement.
- **Runtime-Trennung**: d-migrate läuft nur zur Build-Zeit. Zur
  Laufzeit kennt der bess-ems-Host das Tool nicht — `BessDbMigrator`
  appliziert die committeten SQL-Files via DbUp (siehe MIG-02).

---

## Risiko / Reihenfolge

| Risiko                                                          | Mitigation                                                                                                  |
| --------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| Migration `0001_initial.sql` weicht vom heutigen Bootstrap-Stand ab | **Erledigt:** MIG-03 hat den Snapshot-Test geliefert (information_schema-Diff M1-Initializer vs. 0001), bei MIG-05 mit dem Initializer-Removal entfernt. Die laufende Absicherung übernimmt seither `BessDbMigratorIntegrationTests.MigrateAsync_applies_0001_and_records_in_journal`. |
| Multi-Replica-Boot-Race auf der Tracking-Tabelle                  | MIG-04 erzwingt `pg_advisory_lock` im Migrator und testet zwei parallele `MigrateAsync`-Aufrufe gegen dieselbe DB. |
| Embedded-Resource-Pfad falsch — Migrationen werden zur Laufzeit nicht gefunden oder Drafts werden versehentlich angewendet | `MigrationResourceSetTests` (MIG-06) listet das Manifest des Adapter-Assemblys, verlangt mindestens `0001_initial.sql` unter `Migrations/RunOnce/` und schließt jegliche Resource unter `Migrations/Drafts/` aus — fängt eine versehentliche `<EmbeddedResource Include="…/Drafts/…"/>`-Zeile vor dem Datenbank-Apply. |
| Versionslücke oder doppelte Migrationsnummer läuft bei DbUp alphabetisch trotzdem weiter | Preflight in `BessDbMigrator` validiert `000N`-Kontinuität vor DbUp; MIG-04 testet fehlende `0001`, Lücke `0001`/`0003` und doppelte Nummern. |
| Bestehende Integrationstests brechen, weil `TruncateAll` jetzt auch `__schema_versions` antastet | `TruncateAllAsync` in `PersistenceRoundtripTests` auf Domain-Tabellen einschränken; Tracking-Tabelle bleibt unberührt. |
| d-migrate kann die heutige DDL nicht vollständig abbilden oder CLI-Kommandos weichen von der Annahme ab | MIG-01/02 enthalten ein explizites Tool-Verifikations-Gate: Image pullbar/digest-pinnbar, Commands vorhanden, Mini-Schema deckt FK-Cascade, CHECK, Index, `TIMESTAMPTZ`, `BIGSERIAL`, UUID ab. Scheitert das Gate, fällt die ADR auf Hand-SQL + DbUp zurück. |
| `schema.yaml` und committeter `0001_initial.sql` driften, weil ein Entwickler die SQL-Datei direkt editiert ohne YAML-Update | `make schema-drift-check` (verdrahtet in `make ci`): re-generiert das SQL aus dem YAML und ruft `git diff --exit-code` gegen den committeten Stand — direkter Edit ohne YAML-Update bricht den CI-Lauf mit einer aktionablen Fehlermeldung. |
| d-migrate-Image-Tag schweigend bewegt sich (Mutable-Tag) und erzeugt anderes DDL beim nächsten Build | Image per Digest pinnen (`@sha256:...`) statt nur Version-Tag, parallel zum NuGet-Lock-File-Discipline. ADR-Entscheidung in MIG-01. |

**Reihenfolge (umgesetzt):**

1. MIG-01 (ADR) zuerst — die Tooling-Entscheidung trieb die Library-Wahl in MIG-02.
2. MIG-02 + MIG-03 nacheinander geliefert (Setup-Skeleton erst, dann Inhalt via reverse-engineering).
3. MIG-04 + MIG-05 sequentiell: erst Tests grün, dann Cut-Over auf den Migrator inkl. Löschung von `BessDbInitializer` + `BessDbSchema`.
4. MIG-06 als nicht eingebetteter Draft + Manifest-Resource-Gate; die echte `0002_*.sql` entsteht erst, wenn OPEN-05 als M3-Item aktiviert wird.

---

## Aktivierungs-Trigger (gezündet)

Der Plan ist nicht spekulativ in `in-progress/` gerutscht — er wurde
proaktiv gezogen, weil der Migrationspfad als Vorbedingung für die
M3-Folgepakete OP-OPEN-05/06 verlangt war und der Multi-Replica-
Boot-Race jetzt sauber adressiert sein soll, bevor die erste echte
Schema-Änderung anliegt. Die ursprünglich vorgesehenen Trigger
(M3-Aktivierung von OP-OPEN-05/06, Multi-Replica-Deployment, erste
echte Schema-Änderung) bleiben damit für nachfolgende Migrationen
relevant — der Tooling-Pfad ist jetzt vorhanden, sodass keiner
dieser Auslöser den Migrator nochmal neu denken muss.
