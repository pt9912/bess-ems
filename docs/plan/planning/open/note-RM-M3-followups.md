# Notiz: M3-Follow-up-Slices (Trigger-Watch)

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen — acht vor-Trigger Follow-up-Items aus der M3-Closure, in zwei Blöcken
**Bezug:**
[`../done/plan-RM-M3.md`](../done/plan-RM-M3.md) (Master-Slice-Plan, geschlossen — die M2-Folgewellen-Tabelle „M2-Folgearbeit Mit M3-Trigger" ist Quelle für Block B),
[`../done/plan-RM-M3-D2.md`](../done/plan-RM-M3-D2.md) (M3-D2-Slice „Out of Scope"-Block — Quelle für Block A),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md),
[`../../adr/0003-native-kernel-language.md`](../../adr/0003-native-kernel-language.md) (Sprach-Pivot-Trigger §4),
[`../../adr/0004-native-kernel-process-isolation.md`](../../adr/0004-native-kernel-process-isolation.md) (Out-of-Process-Pivot-Trigger §4)

---

## Zweck

Die M3-Closure hat den Native Control Core von in-tree-
Implementierung über volle Quality-Gates bis zur produktiven
DI-Aktivierung gezogen (RM-M3-01..13 + M3-D2). Acht Follow-up-
Items wurden im Zuge der Closure als „Out of Scope / separate
Folge-Slice" benannt. Damit sie beim nächsten Trigger-Watch
sichtbar bleiben — statt in geschlossenen Plänen unter `done/`
zu verschwinden — sind sie hier zentral in zwei Blöcken geführt:

- **Block A (Items 1–4):** M3-Closure-Out-of-Scope-Items aus dem
  `plan-RM-M3-D2.md`-Slice — Architektur-/Operations-Themen die
  beim Bauen der Native-Aktivierung als bewusste Verschiebungen
  formuliert wurden.
- **Block B (Items 5–8):** M2-Folgewellen mit M3-Trigger aus
  dem `plan-RM-M3.md`-Master-Plan — Carve-outs aus der
  M2-Optimization- und Migration-/Replay-Welle, die mit
  konkreten externen Triggern in M3+ landen würden.

Kein Item zündet aktuell. Diese Notiz ist **Trigger-Watch-
Material**, kein Slice-Plan: der konkrete Plan entsteht erst,
wenn ein Trigger zündet, und zwar in einem von vier Formaten —
abhängig von der Item-Klasse:

- `plan-RM-M3-D3.md` für PID-Routing (Item 1, M3-Folge-Slice
  analog zu plan-RM-M3-D2.md).
- Eigener Operations-/Observability-Slice **ohne** RM-M3-Prefix
  für Items 2 + 3 (die FUP-Reservierung 01..04 gehört Block B,
  diese Items sind eine andere Trigger-Klasse).
- Eigene ADR plus zugehöriger Slice-Plan für Item 4 (Architektur-
  Pivots, ADR 0003/0004 als normative Trigger-Quellen).
- `plan-RM-M3-FUP-NN.md` für Items 5–8 (M2-Folgewellen mit
  reservierten IDs RM-M3-FUP-01..04), oder als Carve-out-Sektion
  innerhalb des auslösenden Plans.

---

# Block A — M3-Closure-Out-of-Scope

Quelle: `plan-RM-M3-D2.md` „Out of Scope"-Block. Architektur-/
Operations-Themen, die beim Bauen der M3-D2-Aktivierung als
bewusste Verschiebungen formuliert wurden.

## Item 1: M3-D3 — PID-Routing in den Regelzyklus

**Trigger:** Ein konkreter Konsument von `PidController.Step`
materialisiert im Regelzyklus. Heute ist `BatteryEms.Domain.PidController`
eine reine Domain-Primitive ohne produktive Verdrahtung (RM-M2-08
lieferte ihn als „Soll" / Domain-Primitive). Ohne Konsument bringt
eine Routing-Aktivierung nichts.

**Beobachtbare Trigger-Bedingungen** (eines reicht):

- Ein Anwendungs-Use-Case verlangt weichen Setpoint-Tracking
  (z. B. Power-Following statt direktem Limit-und-Forward — der
  Konsument würde `IControlCycleUseCase` ergänzen oder eine neue
  Use-Case-Klasse aufmachen, die PID inline einbettet).
- Frequency-Tracking / FCR-naher Pfad braucht PI/PID-Stabilisierung
  über die Constraint+Ramp-Pipeline hinaus.
- Ein Plan-Slice formuliert konkret „Konsument X soll PID nutzen".

**Scope-Skizze für M3-D3** (wenn der Trigger zündet):

- Neuer Driven-Port `IPidKernel` in `BatteryEms.Application` analog
  zu `IControlKernel`, mit `PidStep(state, options, input) → result`.
- `ManagedPidKernel` in `BatteryEms.Application.Control` als Wrapper
  um `BatteryEms.Domain.PidController.Step`.
- `NativePidFallbackKernel` in `BatteryEms.Adapters.NativeInterop`
  analog zu `NativeFallbackControlKernel`, ruft
  `NativeControlKernel.PidStep` (aus RM-M3-13).
- DI-Erweiterung: `AddBessNativeControl` registriert auch
  `IPidKernel` analog zur `IControlKernel`-Logik.
- Replay-Parity-Fixture für PID happy-path-Cases in
  `tests/fixtures/native_parity/pid_cases.v1.json` (separates Schema,
  weil PID-Cases andere Felder brauchen).
- Wire-Tests durch P/Invoke + Replay-Parity gegen den realen
  `bcc_pid_step`-Export (Native Coverage 100 % bleibt erhalten).

**Aufwandsschätzung:** grob 1–2 Wochen, ähnlich M3-D2 plus die
Replay-Fixture.

**Aktivierungs-Pfad:** eigener `plan-RM-M3-D3.md`, nach
`docs/plan/planning/open/` während der Planung, dann nach
`in-progress/` für die Umsetzung, dann nach `done/`. Roadmap-
Eintrag unter „Aktueller Stand" und in der Übersichts-Tabelle.
Der Master-Plan `done/plan-RM-M3.md` bekommt **keinen** Nach-
Eintrag — er ist abgeschlossen; M3-D3 läuft als eigener
Slice-Plan analog zu `plan-RM-M3-D2.md`.

---

## Item 2: Production-Profil-Defaults zentralisieren

**Trigger:** Operations-Reibung mit den heutigen `appsettings.json`-
Stages. Aktuell leben Konfigurations-Defaults verteilt:

- `src/host/BatteryEms.Host/appsettings.json` (Standard-Defaults)
- `deploy/compose.yml` (Container-spezifische Env-Variables)
- Test-Hosts (eigene `appsettings.json` pro Test-Projekt)

**Beobachtbare Trigger-Bedingungen** (eines reicht):

- Mehrere Operations-Profile (Test / Staging / Production / Native-
  On / Native-Off) müssen tatsächlich nebeneinander gepflegt werden
  und die Env-Variable-basierte Override-Strategie reicht nicht mehr.
- Ein konkreter Operations-Anlass: Profil-Drift zwischen Staging und
  Production hat zu einer fehlerhaften Deployment-Konfiguration
  geführt, Postmortem fordert zentrale Profile.
- Compliance-Anforderung verlangt audit-trail-fähige Profil-Versionierung.

**Scope-Skizze:**

- `appsettings.Production.json` / `appsettings.Native.json` /
  `appsettings.Test.json` als reproduzierbare Standardprofile;
  klare Override-Hierarchie (Standard → Profil → Env-Variable).
- Profile-Schema-Validierung beim Start (analog zur Modbus-/MQTT-
  Mapping-Validation aus RM-M1) — fehlerhaftes Profil kippt den Boot.
- Doku-Update in `docs/user/quality.md` §4 mit der neuen Profil-Matrix.

**Aufwandsschätzung:** grob 1 Woche, primär Operations- und Doku-
Arbeit; null Application-Code.

**Aktivierungs-Pfad:** eigener Operations-Hardening-Slice (z. B.
im Zuge eines Multi-Replica-Deployments oder eines Compliance-
Audits). **Nicht** als `RM-M3-FUP-NN`-Eintrag — die FUP-Reservierung
01..04 gehört Block B (M2-Folgewellen mit M3-Trigger). Eigene
ID-Klasse oder direkt als Operations-Slice ohne RM-M3-Prefix.

---

## Item 3: NativeControl-Gesundheits-Endpoint

**Trigger:** Operator-Anforderung, dass der heutige `/health`-
Endpoint zu grob ist. Aktuell deckt `/health` nur Container-/
Postgres-Health ab; den Loader-Status (`Disabled` / `LibraryMissing` /
`LoadFailed` / `AbiMismatch` / `Loaded`) sieht ein Operator nur
in den strukturierten Logs (`native_control_status=*`).

**Beobachtbare Trigger-Bedingungen** (eines reicht):

- Ein Pod-Crash-Investigation zeigt dass das
  Standard-Monitoring-Dashboard den Loader-Status nicht
  separat ausweist und der Vorfall dadurch erst spät erkannt wurde.
- Standard-Operations-Probe-Tooling (z. B. Kubernetes
  Liveness/Readiness-Differenzierung) verlangt eine separate
  `/health/native`-Probe, die nur den Native-Pfad bewertet.
- Ein neuer SLO/SLA verlangt eine messbare „Native-aktiv"-
  Verfügbarkeit, getrennt von der allgemeinen Container-Verfügbarkeit.

**Scope-Skizze:**

- Neuer Endpoint `/health/native` in `BatteryEms.Api` (analog zu
  `/health`), der den `NativeControlLoadResult`-Status aus dem
  DI-Container ausliest und als JSON-Body emittiert
  (`{"status": "loaded", "library_path": "...", "abi_version": "0.2.0"}`).
- Status-Persistierung beim Start: `NativeControlLoadResult` als
  Singleton im Container (heute wird er in `AddBessNativeControl`
  konstruiert und nur intern für die Factory verwendet — eine
  zusätzliche Singleton-Registrierung würde den Endpoint
  versorgen).
- Prometheus-Metrik `bess_native_control_status{status="..."}` als
  Gauge analog zu den existierenden `BatteryEms.Adapters.Telemetry`-
  Mustern.
- Architektur-Tabu-Test verifiziert dass `/health/native` keinen
  zusätzlichen Adapter-Cross-Reference einführt.

**Aufwandsschätzung:** grob 2–3 PT, kleiner Slice. Primär API-
Endpoint + DI-Anpassung + Metric.

**Aktivierungs-Pfad:** eigener kleiner Slice, möglicherweise
gebündelt mit anderen Observability-Erweiterungen (z. B.
`bess_native_control_calls_total` /
`bess_native_control_fallback_total` Counter-Metriken). **Nicht**
als `RM-M3-FUP-NN`-Eintrag — die FUP-Reservierung 01..04 gehört
Block B. Eigene ID-Klasse oder direkt als Observability-Slice
ohne RM-M3-Prefix.

---

## Item 4: Out-of-Process / Sprach-Pivot

**Trigger:** Eine der fünf Sprach-Trigger aus
[`ADR 0003 §4`](../../adr/0003-native-kernel-language.md) oder
eine der sieben Process-Isolation-Trigger aus
[`ADR 0004 §4`](../../adr/0004-native-kernel-process-isolation.md)
zündet. Beide ADRs sind die normative Trigger-Liste — diese Notiz
fasst sie nicht zusammen, sie verweist auf sie.

**Bündel-Trigger:** ADR 0003 Trigger 2 (MPC/Phase-3) und ADR 0004
Trigger 2 (Phase-3-Komponenten in Scope) sind faktisch dasselbe
Architektur-Event aus zwei Blickwinkeln. Wenn dieser Trigger zündet,
wird er durch eine **gemeinsame Folge-ADR** plus einen **gemeinsamen
Slice-Plan** adressiert (Aufwand grob 5–10 Wochen, ADR 0004 §3).

**Aktivierungs-Pfad:** ADR-Nummern werden sequenziell beim
Schreiben vergeben — die nächste freie Nummer ist heute **0005**.
Die genaue Reservierung ergibt sich daraus, in welcher Reihenfolge
die ADRs landen:

- Sprach-Pivot allein zuerst: neue ADR `00NN-native-kernel-rust-pivot.md`
  (mit `NN` = nächster freier Slot zum Schreibzeitpunkt); bestehende
  ADR 0003 wird zu „Superseded by 00NN".
- Out-of-Process-Pivot allein zuerst: neue ADR
  `00NN-native-kernel-out-of-process.md`; bestehende ADR 0004 wird
  zu „Superseded by 00NN".
- Bündel-Pivot (beide Achsen in einem Schritt): eine gemeinsame ADR
  (z. B. `00NN-native-kernel-pivot.md`); beide bestehenden ADRs
  werden zu „Superseded by 00NN".

**Aktueller Status:** kein Trigger zündet. Diese Notiz hält die
Trigger-Watch-Pflicht fest — beim nächsten architektonischen Review
ist die Trigger-Liste in den ADRs explizit gegen die Roadmap zu
prüfen.

---

# Block B — M2-Folgewellen mit M3-Trigger

Quelle: `plan-RM-M3.md` Tabelle „M2-Folgearbeit Mit M3-Trigger"
(RM-M3-FUP-01..04). Konkrete Carve-outs aus M2-Optimization
([`../done/plan-RM-M2-optimization.md`](../done/plan-RM-M2-optimization.md))
und M2-Migration ([`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md))
sowie aus M2-10 (Telemetrie-Replay), die mit konkreten externen
Triggern in M3+ landen würden. Anders als Block A (Architektur-/
Operations-Themen aus M3-D2) sind die Block-B-Items
**fachlich-konkret** — Schema-Migration, Schedule-Repository-
Erweiterung, Optimization-Lock-Eviction, Replay-Werkzeuge —
mit klar benannten DoD-Sätzen aus dem ursprünglichen Plan.

## Item 5: RM-M3-FUP-01 — Erste echte Folgemigration aktivieren

**Trigger:** OP-OPEN-05 oder OP-OPEN-06 wird konkretisiert, oder
eine andere echte Schema-Änderung wird gebraucht. Heute fährt der
Migrationspfad aus
[`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md)
nur den `0001_initial.sql`-Snapshot; eine zweite Migration ist
noch nicht erzeugt.

**DoD (aus plan-RM-M3.md übernommen):** Der abgeschlossene
Migrationspfad wird konsumiert: `schema/schema.yaml` wird
angepasst, eine echte `Migrations/RunOnce/0002_*.sql` wird
erzeugt/committed, Drafts bleiben nicht eingebettet,
`schema-validate`/`schema-drift-check` bleiben grün und der
Runtime-Migrator appliziert die Änderung idempotent.

**Aktivierungs-Pfad:** eigener `plan-RM-M3-FUP-01.md` mit dem
Auslöser-Trigger im Body, oder als Carve-out-Slice innerhalb des
auslösenden Plans (z. B. wenn OP-OPEN-05 zündet, wird FUP-01 Teil
des OP-OPEN-05-Slices).

---

## Item 6: RM-M3-FUP-02 — Optimistic Schedule Replace (OP-OPEN-05)

**Trigger:** Multi-Replica-Optimize oder RM-M4-01
(Intraday-Reoptimierung — Plan-RM-M4 verlangt explizit „atomarer
Commit-Lock pro `(asset_id, schedule_type)` plus optimistic
compare-and-swap auf der erwarteten Schedule-Version"). Heute ist
`IScheduleRepository.Replace` unconditional; eine
per-`(asset, type)`-Semaphore in
`DefaultScheduleOptimizationUseCase` serialisiert Single-Host-
Konflikte, hat aber bei mehreren Replicas keinen koordinierten
Effekt → Last-Write-Wins.

**DoD (aus plan-RM-M3.md):** `IScheduleRepository.Replace(schedule,
expectedBaseVersion)` plus Dapper-`WHERE version = @expected`;
Versionskonflikt wird als `Failed` Run mit Reason
`concurrent-version-conflict` auditierbar.

**Aktivierungs-Pfad:** Slice-Plan
[`plan-RM-M3-FUP-02.md`](plan-RM-M3-FUP-02.md) liegt bereits
in `open/` (Trigger-Watch zündet, Plan ist vorgezogen). FUP-02
trägt **keine** Schema-Änderung — `schedules.version` existiert
seit dem M2-`0001_initial.sql` als `INTEGER NOT NULL`, reine CAS
via `WHERE version = @expected` braucht weder neue Spalte noch
neues Constraint. FUP-02 bündelt deshalb **nicht zwingend** mit
FUP-01; FUP-01 zündet erst durch echten Schema-Bedarf aus einem
anderen Slice.

---

## Item 7: RM-M3-FUP-03 — Optimization-Lock-Eviction (OP-OPEN-06)

**Trigger:** Ephemere Asset-IDs (Test-Setups mit dynamisch
erzeugten IDs), Multi-Tenant-Rotation (Mandant-Wechsel auf
einem Host) oder wachsende Test-ID-Sets. Heute hält
`DefaultScheduleOptimizationUseCase` einen `_locks`-Dictionary
ohne Eviction — bei langlebigen Hosts mit vielen ID-Variationen
würde die Hashtabelle unbeschränkt wachsen.

**DoD (aus plan-RM-M3.md):** `_locks` in
`DefaultScheduleOptimizationUseCase` bekommt LRU/TTL-Eviction
mit konfigurierbarer Schwelle und Metrik
`bess_optimization_lock_table_size`.

**Aktivierungs-Pfad:** eigener `plan-RM-M3-FUP-03.md`. Kleine
Slice (~2 PT), könnte zusammen mit einem Multi-Tenant-Slice
gebündelt werden.

---

## Item 8: RM-M3-FUP-04 — Replay-Carve-outs nach RM-M2-10

**Trigger:** Externe Fixtures (JSON-File-Loader für externe
Replay-Datensätze), Operator-Replay (CLI-Werkzeug für ad-hoc
Replay), Multi-Asset-Replay (Koordination mehrerer Asset-Streams)
oder Production-Replay (Compare-against-Production-Replay) werden
konkret gebraucht.

**DoD (aus plan-RM-M3.md):** Der in RM-M2-10 gelieferte
Telemetrie-Replay-Harness bleibt bestehen. M3+ ergänzt nur
konkrete Folge-Slices wie JSON-File-Loader unter
`tests/fixtures/replay/`, Operator-CLI/Make-Target,
Multi-Asset-Replay-Koordination oder Compare-against-Production-
Replay; Solver-Replay aus M2 bleibt unverändert.

**Aktivierungs-Pfad:** wahrscheinlich mehrere Mini-Slices, jeder
für eine konkrete Replay-Variante. Eigene `plan-RM-M3-FUP-04-*.md`-
Dateien je Variante.

---

## Trigger-Watch-Disziplin

Diese Notiz wird **nicht aktiv abgearbeitet**. Sie wird gescannt:

- Beim Beginn jedes neuen Slice-Plans (insbesondere wenn der Slice
  PID, MPC, Multi-Asset, Realzeit-Anforderungen oder Zertifizierung
  berührt).
- Beim quartalsweisen Architektur-Review.
- Bei jedem Production-Crash-Postmortem (Items 3 + 4 sind hier
  relevant).

Beim Zünden eines Triggers:

1. Item aus dieser Notiz extrahieren.
2. Eigenen Slice-Plan in `docs/plan/planning/open/` anlegen:
   - Block A: `plan-RM-M3-D3.md` (PID-Routing), Operations-/
     Observability-Slices für Profile-Defaults und Health-Endpoint,
     neue ADR + zugehöriger Plan für die Architektur-Pivots.
   - Block B: `plan-RM-M3-FUP-NN.md` pro FUP-Item (oder
     Carve-out-Sektion innerhalb des auslösenden Plans, falls der
     Trigger ein anderer Plan ist — z. B. ein RM-M4-01-Slice
     der FUP-02 als Vorbedingungs-Slice einplant).
3. Item-Eintrag hier mit Verweis auf den neuen Plan markieren oder
   nach `done/` verschieben sobald der Slice abgeschlossen ist.
4. Roadmap-„Aktueller Stand"-Block ergänzen.

So bleibt die Trigger-Liste lebendig statt in einem closed-Plan zu
verschwinden.
