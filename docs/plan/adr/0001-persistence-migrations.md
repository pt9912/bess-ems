# ADR 0001 — Persistence-Migrations-Tooling: d-migrate (Build) + DbUp (Apply)

**Status:** Accepted — Tooling-Entscheidung steht; produktive
Verdrahtung wartet auf den ersten Aktivierungs-Trigger aus
[`../planning/open/plan-RM-M2-migration.md`](../planning/open/plan-RM-M2-migration.md).
**Datum:** 2026-05-07
**Bezug:**
[`../planning/open/plan-RM-M2-migration.md`](../planning/open/plan-RM-M2-migration.md)
(RM-M2-MIG-01..06),
[`../planning/done/plan-RM-M2-optimization.md`](../planning/done/plan-RM-M2-optimization.md)
(§Open RM-M2-OP-OPEN-05/06 — die ersten echten Konsumenten),
[`../../../spec/architecture.md`](../../../spec/architecture.md) §11,
[`../../../spec/lastenheft.md`](../../../spec/lastenheft.md)
(LH-PERSIST-001..007),
[`../../user/quality.md`](../../user/quality.md) §1.4
(Lock-File-Discipline)

---

## 1. Kontext

M2 betreibt das Postgres-Schema mit `BessDbSchema.CreateScript` +
`BessDbInitializer` — `CREATE TABLE IF NOT EXISTS` über alle Tabellen,
ein einziges Skript, kein Versionsstand auf der DB-Seite. Die Strategie
ist von Architektur-Spec §11 für M1 explizit sanktioniert
(„idempotent beim Worker-Start anwendbar"); §11 nennt EF Core
Migrations und FluentMigrator als Beispiele, nicht als Ausschlussliste.

Drei Limitierungen blockieren M3:

1. **Keine Schema-Versionierung.** Eine laufende DB sagt nicht,
   welche Schema-Generation sie bedient.
2. **Multi-Replica-Boot-Race** auf `CREATE TABLE IF NOT EXISTS`-
   Schritten, sobald mehr als eine Replica startet. Die erste echte
   neue Constraint, RM-M2-OP-OPEN-05 (`UNIQUE (asset_id, type,
   version)` auf `schedules`), ist nicht „IF NOT EXISTS"-konform.
3. **Schema-Drift unsichtbar.** Spalten umzubenennen, Typen zu
   ändern, Indizes hinzuzufügen geht heute nur über Down-and-Up.

Diese ADR fixiert das Tooling, **bevor** M3 die ersten nicht-additiven
Schema-Änderungen mitbringt. Implementierung folgt erst, wenn einer
der Aktivierungs-Trigger aus
[`../planning/open/plan-RM-M2-migration.md`](../planning/open/plan-RM-M2-migration.md)
zündet.

---

## 2. Entscheidung

| Achse              | Entscheidung                                              | Pin                                                                                |
| ------------------ | --------------------------------------------------------- | ---------------------------------------------------------------------------------- |
| Build-Time-DDL     | **d-migrate** (Kotlin-CLI, neutrales YAML→DDL)            | `ghcr.io/pt9912/d-migrate:<version>@sha256:<digest>` als Makefile-Variable          |
| Runtime-Apply      | **DbUp** (`dbup-postgresql`)                              | NuGet zentral in `Directory.Packages.props` + Lock-File (`packages.lock.json`)      |
| Tracking-Tabelle   | `__schema_versions` (explizit konfiguriert)               | siehe MIG-OPEN-03 — kein Verlassen auf DbUp-Provider-Defaults                       |
| Schema-Quelle      | `schema/schema.yaml` (kanonisch) → `Migrations/RunOnce/000N_*.sql` (generiert) | `make schema-generate` als CI-Gate mit leerem `git diff`             |
| Concurrency        | `pg_advisory_lock(<repo-id>)` im `BessDbMigrator`         | MIG-04 testet zwei parallele `MigrateAsync`-Aufrufe                                 |
| Migration-Stil     | Forward-only, eine `000N`-Datei pro logischer Änderung    | siehe MIG-OPEN-02 / MIG-OPEN-05                                                     |

§11 der Architektur-Spec ist mit MIG-02 um den Hybrid-Pfad zu ergänzen
(EF Core / FluentMigrator bleiben als Alternativen aufgeführt, nicht
gewählt).

---

## 3. Achse 1 — Build-Time-DDL-Generierung

### Optionen

**d-migrate (gewählt).** Neutraler YAML→DDL-Generator für Postgres,
MySQL und SQLite mit `schema reverse`/`schema validate`/`schema
generate`-Kommandos. Vorteile gegenüber Hand-SQL:

- **Schema-Quelle reviewbar.** YAML ist diff-freundlicher als ein
  monolithischer DDL-String, Reviews sehen logische Änderungen statt
  Zeichen-Reflows.
- **Mechanische Initial-Befüllung.** `d-migrate schema reverse`
  erzeugt `schema/schema.yaml` aus dem heutigen
  `BessDbSchema.CreateScript`, ohne dass jemand 130 Zeilen DDL
  manuell überträgt.
- **CI-Gates.** `make schema-validate` (statische YAML-Prüfung,
  ohne DB) und `make schema-generate` mit anschließendem leerem
  `git diff`-Check (Drift zwischen YAML und committeter SQL ist
  ein Build-Fehler).
- **Cross-DB-Portabilität.** Falls perspektivisch SQLite-Tests
  oder MySQL-Profile dazukommen, generiert dieselbe YAML-Quelle
  beide DDLs ohne separates Schema-Set.

Risiko: d-migrate ist ein junges Tool mit kleinem Adoptions-Set.
Mitigation: Tool-Verifikations-Gate (siehe §5) und Digest-Pin auf
das Image; Ausstieg auf Hand-SQL bleibt jederzeit möglich
(`schema/schema.yaml` löschen, `0001_initial.sql` und Folgemigrationen
manuell pflegen — der DbUp-Apply-Pfad ändert sich nicht).

**Hand-SQL (verworfen).** Spart das zusätzliche Tool, verliert aber:

- Schema-Validierung als CI-Gate (heute manuell beim Review).
- Reverse-Engineering für den Initial-Cut-Over (Aufwand wandert
  in einen einmaligen Handschlag).
- Drift-Detection: ohne YAML-Quelle gibt es nichts, gegen das
  ein generierter SQL-Stand vergleichbar wäre.
- Cross-DB-Portabilität (für jede neue Ziel-DB ein eigener
  DDL-Set).

Hand-SQL bleibt das Fallback, falls d-migrate das Verifikations-Gate
nicht besteht.

---

## 4. Achse 2 — Runtime-Apply

### Optionen

**DbUp (gewählt).** SQL-First, kein C#-DSL, kein ORM. Direkt
kompatibel mit dem Dapper-Stack: DbUp lädt versionierte SQL-Files
als Embedded Resources, schreibt eine Tracking-Tabelle und garantiert
idempotente Apply-Order out-of-the-box. NuGet ist zentral pinbar
(`Directory.Packages.props` + Lock-File-Disziplin aus
[`../../user/quality.md §1.4`](../../user/quality.md)).

**FluentMigrator (verworfen).** Schwerer C#-DSL-Footprint
(`Migration`-Subklassen mit `Up`/`Down`-Methoden). Ein per-d-migrate
generiertes Schema braucht keinen DSL — die DSL wäre eine zweite
Wahrheitsquelle parallel zur YAML. Forward-only (MIG-OPEN-02) macht
die `Down`-Methoden ohnehin überflüssig.

**EF Core Migrations (verworfen).** Zwingt das Repo in einen
ORM-Stack, der in M1 bewusst zugunsten von Dapper gemieden wurde.
Ein Persistenz-Adapter mit Dapper für den Lese-/Schreibpfad und
EF Core nur für Migrationen wäre eine instabile Mischung mit
zwei Connection-Strings, zwei Konfigurations-Pfaden und zwei
Test-Aufsätzen.

**Selbstgeschriebener .NET-Runner (verworfen).** Re-Erfindung
von DbUps Funktionsumfang (Tracking-Tabelle, Apply-Order,
Embedded-Resource-Loader, Transaktions-Handling). Kein
Funktionsgewinn, dafür Wartungslast.

---

## 5. Tool-Verifikations-Gate (d-migrate)

Bevor MIG-02 d-migrate als Build-Gate verdrahtet, **muss** ein
Vorab-Smoke folgendes belegen — scheitert eines, fällt diese ADR
auf **Hand-SQL + DbUp** zurück, MIG-02 wird auf reines DbUp
umgeplant und MIG-03 entfällt:

1. **Image pullbar und digest-pinnbar.**
   `docker pull ghcr.io/pt9912/d-migrate:<version>@sha256:<digest>`
   liefert Exit-Code 0 und legt das Image lokal ab.
2. **CLI-Kommandos vorhanden.**
   `d-migrate schema reverse --help`, `d-migrate schema validate
   --help`, `d-migrate schema generate --help` liefern jeweils
   einen Hilfetext mit Exit-Code 0.
3. **DDL-Feature-Coverage über Mini-Schema.** Ein
   Verifikations-`schema.yaml` deckt alle DDL-Konstrukte ab, die
   das heutige `BessDbSchema.CreateScript` benutzt:

   | Konstrukt                         | Vorkommen heute                                                              |
   | --------------------------------- | ---------------------------------------------------------------------------- |
   | Spalten `TEXT NOT NULL`           | überall                                                                      |
   | `BIGSERIAL PRIMARY KEY`           | `telemetry`, `audit_events`                                                  |
   | `UUID PRIMARY KEY`                | `optimization_runs`                                                          |
   | Composite Primary Key             | `schedules (asset_id, type)`, `schedule_windows`, `optimization_objective_breakdowns` |
   | Composite UNIQUE                  | `optimization_objective_breakdowns (run_id, position)`                        |
   | Foreign Key mit `ON DELETE CASCADE` | `schedule_windows` → `schedules`, `optimization_objective_breakdowns` → `optimization_runs` |
   | CHECK-Constraint                  | `optimization_runs.termination_reason` (`length(...) <= 256`)                |
   | `TIMESTAMPTZ`                     | `recorded_at`, `issued_at`, `horizon_start/end`, `created_at`, …             |
   | `DOUBLE PRECISION`                | SOC, Power, Voltage, Current, Temperature, Objective, Solver-Runtime         |
   | `INTEGER`                         | `schedules.version`, `optimization_objective_breakdowns.position`            |
   | `BOOLEAN`                         | `telemetry.available`, `commands.dispatch_success`                           |
   | Default-Wert auf TEXT             | `optimization_runs.constraint_violations_json DEFAULT '[]'`                  |
   | Index auf zwei Spalten mit `DESC` | `idx_telemetry_asset_recorded_at`, `idx_commands_asset_issued_at`            |
   | Reine `IF NOT EXISTS`-Idempotenz  | aktuell überall — entfällt mit dem Versions-Tracker                          |

   `d-migrate schema generate` muss daraus valides Postgres-DDL
   erzeugen, das dasselbe Schema beschreibt wie das heutige
   `BessDbSchema.CreateScript` (Tabellen, Spalten, Constraints,
   Indizes; Whitespace und Reihenfolge-Differenzen sind tolerierbar).

---

## 6. Konsequenzen

### Positiv

- **Zwei orthogonale Sorgen, zwei Tools.** d-migrate kümmert sich
  um „wie sieht das Schema aus", DbUp um „welche Scripts liefen
  schon".
- **Schema-Validierung als CI-Gate.** `make schema-validate`
  scheitert auf YAML-Fehlern, bevor ein Build überhaupt
  versucht, DDL zu generieren.
- **Drift-Detection als CI-Gate.** `make schema-generate` + leerer
  `git diff` erzwingt, dass `schema/schema.yaml` und committete
  Migrationen synchron bleiben.
- **Multi-Replica-Boot-Race adressiert.** `pg_advisory_lock` im
  `BessDbMigrator` (MIG-04) serialisiert parallele Apply-Versuche.
- **Forward-only mit kleinen, reviewbaren Diffs** (MIG-OPEN-02 /
  MIG-OPEN-05) — eine Migration pro logischer Änderung, keine
  monatlichen Sammel-Migrationen.
- **Architektur-Spec §11 bleibt konsistent** — der Hybrid-Pfad
  erfüllt „idempotent beim Worker-Start anwendbar" weiterhin
  (Migrator-Aufruf bleibt Boot-Schritt).

### Negativ

- **Zwei Tools statt eins.** Build-Zeit braucht das d-migrate-Image,
  Runtime braucht DbUp. Mitigation: beide sind digest-/version-
  pinbar, beide haben CI-Gates.
- **d-migrate-Adoptionsrisiko.** Junges Tool mit kleinem
  Anwender-Set. Mitigation: Verifikations-Gate (§5),
  Image-Digest-Pin, dokumentierter Ausstiegspfad auf Hand-SQL
  (s. §3).
- **Architektur-Spec §11 muss erweitert werden.** §11 nennt nur
  EF/FluentMigrator als Beispiele; MIG-02 ergänzt den
  d-migrate+DbUp-Hybrid und kennzeichnet ihn als gewählten Pfad.
- **`BessDbInitializer` + `BessDbSchema.cs` werden in MIG-05
  entfernt.** Bis MIG-05 koexistieren beide Pfade; der Snapshot-
  Test in MIG-03 stellt Bit-Identität sicher.

### Neutral

- **Tracking-Tabelle `__schema_versions`** ist ein zusätzliches
  DB-Objekt. Aus Backups/Schema-Diffs leicht exkludierbar (Präfix
  `__`); MIG-OPEN-03 hat den Namen explizit gemacht, statt sich
  auf DbUp-Provider-Defaults zu verlassen.
- **Make-Targets wachsen** um `schema-validate` und
  `schema-generate`; beide rufen dasselbe digest-gepinnte
  d-migrate-Image auf.

---

## 7. Sequenz und Aktivierung

1. **MIG-01 (diese ADR):** Tooling-Entscheidung. **Erledigt.**
2. **MIG-02 (Tooling-Setup):** d-migrate-Image-Pin, NuGet-Pin für
   DbUp, `BessDbMigrator`-Skelett, Make-Targets,
   §11-Spec-Ergänzung. Aktiviert sich mit dem ersten Trigger
   aus
   [`../planning/open/plan-RM-M2-migration.md`](../planning/open/plan-RM-M2-migration.md).
3. **MIG-03..06:** Snapshot, Idempotenz/Lock-Test, Cut-Over,
   `0002`-Vorlage. Sequenz und DoD im Plan.

Triggernde Ereignisse (eines reicht):

- **RM-M2-OP-OPEN-05** wird als M3-Item geöffnet (`UNIQUE
  (asset_id, type, version)` auf `schedules`).
- **RM-M2-OP-OPEN-06** wird konkretisiert und braucht eine
  Hilfstabelle.
- **Multi-Replica-Deployment** kommt aufs Reissbrett.
- **Erste echte Schema-Änderung** in einer existierenden Tabelle
  (Spalte umbenannt, Typ geändert, Index hinzugefügt).

Bis dahin bleibt diese ADR `Accepted`, der Plan steht unter `open/`,
und der heutige `BessDbInitializer`-Pfad bleibt produktiv.
