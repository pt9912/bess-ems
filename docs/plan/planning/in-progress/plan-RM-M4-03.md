# Plan RM-M4-03 — Regelleistungs-Aktivierungssignal-Verarbeitung

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M4-03)
**Status:** Offen — wird in Sub-Slices RM-M4-03-A..E umgesetzt
**Bezug:**
[`plan-RM-M4.md`](plan-RM-M4.md) (Master-Plan, RM-M4-03-Zeile mit DoD und LH-Bezug),
[`plan-RM-M4.md` §138 ff.](plan-RM-M4.md) (`ControlPriorityClass`-Vertrag — Rang 3 für `RegelleistungsActivation`),
[`../open/note-RM-M4-followups.md`](../open/note-RM-M4-followups.md) (F-08/F-09 als Folgearbeiten dieses Slices),
[`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md) (Migrations-Pipeline mit DbUp + d-migrate; RM-M3-FUP-01 ist die offene Folgemigration die RM-M4-03 als ersten realen Konsumenten konsumiert),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md) (LH-MKT-005/006 Aktivierung im Regelkreis, LH-MON-003 Trace-Sichtbarkeit, LH-OPS-001 Profil-Validation)

---

## 1. Zweck

RM-M4-03 ist die produktive Verdrahtung von Regelleistungs-
Aktivierungssignalen in den Regelkreis. Das Signal kommt aus einer
externen Quelle (TSO-Spec, Vendor-Protokoll), wird gegen
Source-/Payload-Schema, UTC-Zeitfenster und Zeitbasis-Stabilität
validiert, in einem persistenten Dedupe-/Replay-Tracker auf
Idempotenz geprüft, und — bei akzeptierter Rezeption — über
`IActivationDispatchSource` als Top-Aktivierung in den Dispatch-Pfad
eingespeist. Sie gewinnt damit per Port-Vertrag über alle
MarketCommitment-basierten Schedules (`ControlPriorityClass.
RegelleistungsActivation`-Rang-3-Verhalten gemäß Master-Plan §138 ff.;
**kein** neuer Domain-Enum — siehe D-09). Safety-Limits (Rang 2)
bleiben vorrangig.

Aus dem Master-Plan RM-M4-03-DoD (gekürzt):

> Aktivierungssignal wird validiert, zeitlich begrenzt und im
> Regelkreis vor Market Commitments/Fahrplänen berücksichtigt.
> Mindestens ein aFRR-positiv- und ein aFRR-negativ-Profiltest
> weisen Richtung, 15-min-Bezug, Leistungsinterpretation und
> Priorität gegen konkurrierende Fahrpläne nach. mFRR-
> Aktivierungsenergie bleibt als Produkt modellierbar, aber
> produktive MOLS-/MARI-Aktivierung ist kein M4-DoD.

Der Slice ist substantiell: Domain-Modell + Time-Validation +
Debounce-State-Machine + persistenter Idempotenz-Tracker (mit
Schema-Migration als ersten realen RM-M3-FUP-01-Konsumenten) +
Validation-Pipeline + Use-Case + Dispatch-Integration +
Production-Gate. Daher ist er in fünf Sub-Slices aufgeteilt
(RM-M4-03-A bis RM-M4-03-E) — jeder einzeln review- und
committierbar.

---

## 2. Aktivierungsbedingungen

| Check | Erwartung | Stand heute |
|-------|-----------|-------------|
| RM-M4-01 (Intraday-Reoptimierung) ✅ | Schedule-Replace-CAS und Use-Case-Conflict-Pfad existieren | ✅ |
| RM-M4-02 (Reservierungs-Modell) ✅ | Reserve-Bands fließen in den Optimizer | ✅ |
| `ControlPriorityClass`-Vertrag (Plan-RM-M4 §138 ff.) | Rang 3 für `RegelleistungsActivation` ist normativ definiert | ✅ |
| `IClock.UtcNow` als Zeitquelle | Existiert (RM-M2-Application/Time/IClock) | ✅ |
| Migrations-Pipeline (DbUp + d-migrate) verfügbar | RM-M2-MIG-* abgeschlossen | ✅ |
| RM-M3-FUP-01 (erste echte Folgemigration) | Trigger-Watch in note-RM-M3-followups.md, kein realer Konsument bislang | 🟡 — RM-M4-03-B zündet ihn |
| `Regelleistung:ProductionActivationEnabled` Konfigurations-Slot | Existiert nicht heute, wird in RM-M4-03-D angelegt | ⬜ |
| Source-Adapter für Aktivierungssignal-Empfang | Driving-Port-Shape genügt für M4-03; konkrete Source-Wire-Adapter (OPC-UA, MQTT, HTTP) sind eigene Slices (siehe „Bewusst draußen" + F-09) | n/a |

---

## 3. Scope

**In Scope (RM-M4-03-A..E zusammen):**

- **Domain-Modell:** `RegelleistungActivation`-Record mit allen
  DoD-genannten Feldern (`source_id`, `activation_id`/`message_id`,
  `sequence_number`, `signal_timestamp_utc`, `product`, `direction`,
  `power_kw`, `valid_from`/`valid_until`, `payload_hash`).
  `ActivationValidationResult`-typisiertes Outcome.
  `TimebaseHealth`-Enum + `TimebaseDebounceState`-Domain-Primitive.
  `Product`-Wiederverwendung des bestehenden `ReserveProduct` aus
  RM-M4-02 wenn das Modell deckt — sonst eigenes Enum `ActivationProduct`.
- **Time-Validation:** `ActivationTimeValidator` mit
  konfigurierbaren Toleranzen (`max_age=2s`, `future_skew_tolerance=
  500ms`, alle Defaults aus DoD).
- **TimebaseDegraded-State-Machine:** 3-Verletzungen-in-10-Zyklen-
  Debounce, 5-stabile-Zyklen-Recover oder expliziter Health-Recover.
  Domain-Primitive ohne Persistenz (cycle-state, lebt im
  ControlCycle-Use-Case).
- **Idempotenz-Tracker (Dedupe):** Driven Port `IActivationDedupeStore`
  + In-Memory + **persistente Dapper-Variante** mit
  `0002_regelleistung_activations.sql`-Migration via DbUp +
  `schema/schema.yaml`-Update via d-migrate. Erster realer
  RM-M3-FUP-01-Konsument.
- **Validation-Pipeline:** `ActivationValidator` orchestriert
  Source-/Payload-Schema → UTC-Window → TimebaseDegraded → Dedupe
  in **dieser Reihenfolge** (DoD-Wortlaut „jede Rezeption ... muss
  zuerst ... erst danach Idempotenz/Dedupe").
- **Use-Case:** `IRegelleistungActivationUseCase` als Driving Port,
  empfängt validierte Aktivierung, integriert mit Dispatch-Pfad
  (`ScheduleFollowingDispatchOptimizer` erweitert um Rang 3).
- **Production-Gate:** `Regelleistung:ProductionActivationEnabled`-
  IConfiguration-Flag (Default `false`). Bei `false` werden
  Aktivierungen validiert + persistiert (Audit), aber **nicht
  dispatch-relevant**. Bei `true` zusätzlich Pflicht-Checks:
  Produktannahme dokumentiert, Systemzeit synchronisiert (Trigger:
  Zeitbasis-Health), Dedupe-Store healthy, Security-Profile grün —
  fehlt eines: nicht dispatch-relevant.
- **Health-Endpoint:** `/health/regelleistung` exponiert
  `timebase-degraded`, `dedupe-store-invalid`, `production-gate-
  status` als JSON.
- **Profiltests:** aFRR-positiv (Up, 15-min-Bezug,
  Leistungsinterpretation, Priorität gegen konkurrierenden Schedule
  → Aktivierung gewinnt), aFRR-negativ (Down). mFRR-Modellierbarkeit
  als Domain-Pin (Persistierbar, kein produktiver Aktivierungspfad).
- **Race-/Restart-Tests:** konkurrierende valide Signale aus
  mehreren Quellen, vollständige Tiebreak-Gleichstände,
  Duplikat-Replay, Restart-/Failover-Replay aus persistentem
  Dedupe-Tracker, widersprüchliche Wiederholung, TimebaseDegraded-
  Debounce, deterministische Persistenz des gewählten Gewinners.

**Out of Scope (separate Slices):**

- **Konkrete Aktivierungs-Source-Wire-Adapter** — RM-M4-03 stellt
  nur den Driving-Port `IRegelleistungActivationUseCase` bereit;
  ein OPC-UA-Source-Adapter folgt mit RM-M4-04, ein
  MQTT-Subscribe-Source-Adapter wäre Folgearbeit F-09 (siehe
  `note-RM-M4-followups.md`).
- **Produktive MOLS-/MARI-Aktivierung (mFRR)** — DoD-Wortlaut
  schließt das aus M4-03 aus. mFRR ist nur als Produkt im
  Domain-Modell und im Persistence-Tracker abbildbar. Folgearbeit
  F-08 wenn produktive Aktivierungs-Energie gefordert.
- **Cross-Region-Dedupe / Multi-Tenant-Sharding** — heutiges
  Modell ist single-tenant, single-region. Pro `source_id` bleibt
  der Dedupe-Speicher local-scoped.
- **Reserve-Schedule-Erstellung** — RM-M4-02 deckt das Reserve-
  *Bänder*-Modell; das *Aktivierungs*-Signal kommt von außen,
  nicht aus eigener Optimierung.
- **Aktivierungs-Quittungen / Cashout-Reporting an TSO** — M4-03
  empfängt + dispatcht; TSO-Reporting ist eigenständiges Thema
  (Compliance/Settlement-Welle, nicht in M4 verortet).
- **Migration-Template-Slice (Dedupe-Store v1 → v2)** — siehe
  F-11 in §9 (analog zum F-07-Pattern für OPC-UA-Mapping).

---

## 4. Sub-Slices

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ⬜ | RM-M4-03-A | Domain-Modell + Time-Validation + Debounce-State-Machine + `RegelleistungOptions`-Type — **~500-700 LOC** | `RegelleistungActivation`-Record (alle DoD-Felder), `ActivationValidationResult`-typisiertes Outcome, `TimebaseHealth`-Enum + `TimebaseDebounceState`-Domain-Primitive analog zu `PidController.Step`. **`RegelleistungOptions`-Type wird in A eingeführt** (Felder: `MaxAge=2s`, `FutureSkewTolerance=500ms`, `DedupeWindow=10s`, `MaxEntriesPerSource=…`; alle Defaults aus DoD wörtlich). Sub-Slice B konsumiert die Dedupe-Felder, Sub-Slice D fügt das `ProductionActivationEnabled`-Feld plus die Production-Gate-Pre-Condition-Konfiguration hinzu. **Defaults-Pin-Test**: `MaxAge==TimeSpan.FromSeconds(2)`, `FutureSkewTolerance==TimeSpan.FromMilliseconds(500)`, `DedupeWindow==TimeSpan.FromSeconds(10)` — verhindert dass die Master-DoD-Werte still driften. `MaxEntriesPerSource` ist **bewusst nicht gepinnt** und bleibt operator-tunable (Master-DoD-Wortlaut „eine konfigurierte Obergrenze") — der Defaults-Pin-Test dokumentiert das mit einem expliziten Kommentar. `ActivationTimeValidator` ist **per-Sample stateless** (kein Monotonic-Window-State, kein Cross-Sample-Memory): jede Rezeption wird isoliert gegen `IClock.UtcNow` geprüft. Negative Alter außerhalb `FutureSkewTolerance` → fail-closed; ein einzelner Sample mit negativem Alter ist also bereits ein Clock-Rückspring-Verdacht. „Erkannte Clock-Rücksprünge" als Master-DoD-Wortlaut wird per-Sample interpretiert (siehe Risiken §7); ein zukünftiges Monotonic-State-Primitive wäre Folgearbeit, falls eine Cross-Sample-Konsistenz-Anforderung präzisiert wird. Domain-Tests: Gültigkeitsfenster-Pin, Future-Skew-Pin, Stale-Timestamp-Pin, Negative-Alter-fail-closed-Pin (Clock-Rücksprung als per-Sample-Erkennung), Debounce-State-Maschine (3 in 10 → Degraded; 5 stabil oder Recover → Healthy), Mode-Pin für `TimebaseDegraded` (Aktivierungen nicht dispatch-relevant), Defaults-Pin-Test (siehe oben). Reuse-Check: `ReserveProduct`-Enum aus M4-02 — falls semantisch deckend wiederverwenden, sonst eigenes `ActivationProduct`. |
| ⬜ | RM-M4-03-B | Idempotenz-Tracker (Driven Port + InMemory + persistente Dapper-Variante mit FUP-01-Migration) — **~700-900 LOC** | Driven Port `IActivationDedupeStore` mit `TryAccept(activation, payloadHash) → AcceptResult` (Accepted, Replay_Idempotent, Rejected_DedupeConflict, Rejected_AmbiguousDuplicate, Rejected_DedupeStoreInvalid), wobei `Rejected_DedupeConflict` **nur** auf dieselbe `(source_id, activation_id)`-Identität mit unterschiedlichem Payload trifft; `Rejected_AmbiguousDuplicate` ist der dedizierte Konfliktpfad für konkurrierende Kandidaten, für die der Tiebreak keine eindeutige Reihenfolge liefern kann. `InMemoryActivationDedupeStore` für Tests (per-`source_id`-Dictionary mit Retention-LRU). **`DapperActivationDedupeStore`** mit neuer Tabelle `regelleistung_activations` (`source_id`, `activation_id`/`message_id`, `sequence_number`, `signal_timestamp_utc`, `payload_hash`, `winner_chosen_at`) — **erste reale Migration `0002_regelleistung_activations.sql` via DbUp + `schema/schema.yaml`-Update via d-migrate**, damit ist RM-M3-FUP-01 als erster Konsument zünden. **„Versioniert gespeichert"-Lesart**: Master-DoD-Wortlaut wird über die DbUp-Migration-Version (`0002_*.sql`) abgedeckt, **keine** per-Row `tracker_format_version`-Spalte heute — Schema-Drift wird strukturell durch DbUp + d-migrate-YAML-Drift-Check (`make schema-drift-check`) erkannt. Falls eine zukünftige Welle per-Row-Versionierung verlangt (z. B. Multi-Version-Koexistenz im selben Tisch), ist das F-11 (siehe §9). **Schreibmuster ist append/upsert mit `INSERT … ON CONFLICT (source_id, activation_id) DO NOTHING`-Semantik** — kein CAS-Pfad analog zu `schedules`. Replay-Detection läuft über `payload_hash`-Vergleich beim ON-CONFLICT-Treffer (gleicher Hash = Replay-Idempotent, anderer Hash = Rejected_DedupeConflict). **Tracker-Load fail-closed** über die vier Master-DoD-Sub-Cases (alle vier rufen denselben `dedupe-store-invalid`-State, deaktivieren RL-Aktivierung bis Recovery): (a) **inkompatibler Checkpoint** — DbUp-Schema-Version nicht erkennbar oder neuer als die Application-Version; (b) **übergroßer Checkpoint** — pro `source_id` mehr Einträge als `RegelleistungOptions.MaxEntriesPerSource` (z. B. nach abgebrochener Kompaktierung); (c) **teilkorrupter Checkpoint** — Eintrag mit ungültigem `payload_hash`/Timestamp/Sequence-Number; (d) **parse-/Decode-Fail** — generischer Fehler beim Lesen einer Tracker-Zeile. Retention-Kompaktierung pro `source_id`: mindestens letzter Checkpoint + alle Einträge in `max(MaxAge + FutureSkewTolerance + DedupeWindow, 60s)` + Obergrenze `RegelleistungOptions.MaxEntriesPerSource` (operator-tunable Default — siehe Sub-Slice A). Tests: Replay-Idempotenz, Conflict-Detection, Tiebreak-Gleichstand, Restart-Replay (persistent), **Tracker-Load-Fail-Closed pro Sub-Case (a/b/c/d) je ein Pin**, Retention-Kompaktierung-respektiert-Replay-Fenster, Upsert-Pattern-Pin (gleicher Hash = idempotent, anderer Hash = Conflict). |
| ⬜ | RM-M4-03-C | Validation-Pipeline (orchestriert A+B) — **~250-350 LOC** | `ActivationValidator` orchestriert in **dieser Reihenfolge** (DoD-Wortlaut): (1) Source-/Payload-Schema-Check (Field-Presence, ID-Format), (2) UTC-Zeitfenster-Check via `ActivationTimeValidator`, (3) `TimebaseDegraded`-Check (auch wenn die Aktivierung selbst Schema-konform ist, blockiert `TimebaseDegraded` jede Rezeption), (4) Dedupe-Check via `IActivationDedupeStore.TryAccept` — **Idempotenz/Dedupe als allerletzter Schritt**, sodass ein Replay-Hit immer noch durch Time-Validation und Timebase-State muss. Returns: `ActivationValidationResult` mit Reason-Code in der projektüblichen kebab-case-Form (analog zu `intraday-baseline-missing`, `concurrent-version-conflict`, `dedupe-store-invalid` aus früheren Slices — keine neue Längen- oder Trennzeichen-Constraint). Tests: Reihenfolge-Pin (Replay-Hit bei TimebaseDegraded → `timebase-degraded` als Reason, **nicht** `replay-idempotent`), alle Reason-Codes pinnen, Schema-konformes-aber-misaligntes-Signal. |
| ⬜ | RM-M4-03-D | Use-Case + Dispatch-Integration (D-09 Wahl c) + Production-Gate (D-03 Pre-Conditions) + Health-Endpoint — **~600-900 LOC** (Swing-Item, siehe §7) | Driving Port `IRegelleistungActivationUseCase.ReceiveAsync(activation)` → typisiertes Outcome. Use-Case ruft `ActivationValidator`, persistiert das Outcome (auch Rejected-Pfade — Audit-Trail), bei Accepted: feedet **neuen Driven Port `IActivationDispatchSource`** (D-09 Wahl c — additiv, kein DispatchRequest-Format-Change). **`ScheduleFollowingDispatchOptimizer` bekommt zusätzliche Konstruktor-Dependency** auf `IActivationDispatchSource`; pro Tick fragt der Optimizer den Port nach einer Top-Aktivierung — wenn vorhanden, gewinnt sie über alle Schedules (`ControlPriorityClass.RegelleistungsActivation` als Verhaltens-Vertrag, kein neuer Domain-Enum; siehe D-09 für die Architektur-Begründung). **Die aktivierende Quelle liefert nur Signale mit `valid_from <= now <= valid_until`; der Optimizer verwirft abgelaufene Kandidaten aktiv pro Tick gegen `IClock.UtcNow`, wodurch `valid_until` nicht über Ticks ausgenutzt werden kann.** Test-Stub `NoOpActivationDispatchSource` füttert die existierenden M2-Dispatch-Test-Konstruktionen mit dem neuen Konstruktor-Param. **Production-Gate via `IProductionPreconditionProvider`** (D-03): die vier Pre-Conditions (Produktannahme, Time-Sync, Dedupe-Store-Health, Security-Profile) durchlaufen den Provider; Security-Profile retourniert heute fail-closed `security-profile-enforcement-not-wired` bis F-12 zündet. `RegelleistungOptions.ProductionActivationEnabled` (Default `false`) ist der Master-Switch; bei `false` markiert der Use-Case alle Outcomes als `not-dispatch-relevant`. mFRR-Branch: auch bei `true` und allen Pre-Conditions grün, `Product=Mfrr` ist immer `not-dispatch-relevant` (D-05). Neuer Endpoint `GET /health/regelleistung` mit JSON-Body: `{ "timebase": "healthy|degraded", "dedupe_store": "healthy|invalid", "production_gate": "enabled|disabled", "preconditions": { ... }, "last_activation": { ... } }`. Tests: Pre-Condition-Negativ-Pfade (`security-profile-enforcement-not-wired` aus dem Provider, `TimebaseDegraded`, `dedupe-store-invalid`), `ProductionActivationEnabled=false`-Outcome-Markierung, mFRR-not-dispatch-relevant auch bei vollem Production-Gate, M2-Dispatch-Test-Konstruktion durch den NoOp-Stub. |
| ⬜ | RM-M4-03-E | Profiltests + Race/Restart-Replay-Tests — **~500-800 LOC** (Swing-Item, siehe §7) | **Test-Projekt-Verteilung**: Profiltests + Race-Tests (deterministisch, kein DB) landen in `tests/hexagon/BatteryEms.Application.Tests`; **Restart-Replay-Tests + Persistenz-Determinismus-Tests landen in `tests/integration/BatteryEms.Persistence.IntegrationTests`** (existing Postgres-Fixture-Projekt aus RM-M2-MIG/RM-M3-FUP-02 reusen, kein neues Fixture-Projekt). aFRR-positiv-Profiltest: Aktivierung mit `Direction=Up`, 15-min-Bezug, eindeutige Leistungsinterpretation (`PowerKw>0` = Discharge), konkurrierender DayAhead-Schedule (Rang 6), Aktivierung gewinnt im Dispatch (Pin: Setpoint = Aktivierungs-PowerKw, NICHT Schedule-PowerKw). aFRR-negativ-Profiltest: gleiches Schema mit `Direction=Down` (Charge). mFRR-Modellierbarkeit: Domain-Pin dass `Product=Mfrr` validiert + persistiert wird, kein produktiver Dispatch-Pfad (Use-Case markiert `not-dispatch-relevant` für mFRR auch bei `ProductionActivationEnabled=true`). Race-Tests: zwei valide Aktivierungen aus verschiedenen `source_id` in der gleichen Tick (Tiebreak: höchste sequence_number, dann jüngster signal_timestamp_utc, dann lex-kleinster Tupel `(source_id, activation_id)`). Tiebreak-Definition ist nur für unterschiedliche Kandidaten; `ambiguous-duplicate` wird separat auf konkurrierenden Kandidaten mit gleicher Rankenlage ohne eindeutige Tiebreakbarkeit getestet. Duplikat-Replay (gleicher CommandId + Payload) → idempotent akzeptiert. **Restart-Replay (Postgres-Fixture)**: persistierter Tracker wird nach Process-Restart geladen, Replay eines kürzlich akzeptierten Signals → Replay_Idempotent. Widersprüchliche Wiederholung mit gleicher ID + anderem Payload → Rejected_DedupeConflict. TimebaseDegraded-Debounce: 3 stale-Signale in Folge → Degraded; 5 fresh-Signale Recover. **Persistenz-Determinismus (Postgres-Fixture)**: nach explizit konstruiertem Tiebreak-Szenario ist der gewählte Gewinner persistiert + bei Reload identisch reproduzierbar. |

---

## 5. Design-Entscheidungen

**D-01 Validation-Reihenfolge ist DoD-Wortlaut, nicht reorder-bar.**
Source-/Payload-Schema → UTC-Window → TimebaseDegraded → Dedupe.
Dedupe **als letzter Schritt** ist kritisch: ein Replay-Hit darf
nicht die früheren Checks umgehen, weil eine Wiederholung aus
einer kompromittierten Quelle (gleiche ID, alter Payload) den
gleichen UTC-Window-Check bestehen muss wie das Original.

**D-02 Persistenter Dedupe-Tracker triggert RM-M3-FUP-01 inline.**
Die DoD verlangt explizit „versioniert gespeichert, beim Start
geladen und nach Failover/Reconnect weiterverwendet". Damit ist
M4-03-B der erste reale Migrations-Konsument; FUP-01 wird inline
gezogen statt vorgelagert (kleiner Slice für FUP-01 alleine wäre
Placeholder-Migration ohne Konsumenten — Anti-Pattern).

**D-03 Production-Gate ist mehrstufig + fail-closed.**
`ProductionActivationEnabled=false` (Default) → Aktivierung
**nicht dispatch-relevant**, aber vollständig auditiert. Bei
`true` zusätzlich: Produktannahme + Time-Sync + Dedupe-Store-
Health + Security-Profile. Fehlt **eine** dieser Bedingungen,
fällt der Use-Case zurück auf `not-dispatch-relevant` mit
strukturiertem Audit-Reason. Ein Operator sieht das im
`/health/regelleistung`-Endpoint.

**Concrete shape der vier Pre-Conditions** (RM-M4-03-D
introduces these as a single `IProductionPreconditionProvider`-
Driven-Port):
- **Produktannahme:** Konfig-Eintrag
  `Regelleistung:ProductTrustEstablished=true` (boolescher
  Operator-Trust-Stempel — verlangt explizites Setzen, kein
  Default).
- **Time-Sync:** `TimebaseHealth==Healthy` aus dem in Sub-Slice A
  eingeführten `TimebaseDebounceState`. **Pin-Test:** explizite
  `TimebaseDegraded`-Konstellation → `not-dispatch-relevant`.
- **Dedupe-Store-Health:** kein `dedupe-store-invalid`-State im
  `DapperActivationDedupeStore`. **Pin-Test:** simulierter
  beschädigter Checkpoint → `not-dispatch-relevant`.
- **Security-Profile:** **heute placeholder** — der
  `IProductionPreconditionProvider` retourniert in M4-03-D einen
  synthetischen `security-profile-enforcement-not-wired`-Reason
  für jede Aktivierung wenn `ProductionActivationEnabled=true`
  ist. Heißt: solange F-12 nicht gezündet hat, kann der
  Production-Gate **im Production-Code-Pfad** nicht grün werden
  — das ist intentional fail-closed bis ein realer
  Security-Profile-Check existiert (siehe F-12 in §9).
  **Test-Override:** `IProductionPreconditionProvider` ist als
  Driven Port designed; Tests injizieren ein Test-Double
  (`HealthyProductionPreconditionProvider`-Test-Stub analog zum
  `NoOpActivationDispatchSource`-Pattern), das alle vier
  Pre-Conditions als `Healthy` retourniert. Ohne diese
  Test-Override-Möglichkeit wäre §6-Akzeptanzkriterium 1 (aFRR-
  Profiltests dispatchen) heute nicht erreichbar weil der
  Production-Code-Pfad fail-closed bleibt bis F-12 zündet.
  **Pin-Tests (zwei Varianten):**
  - **Production-Pfad-Pin:** `ProductionActivationEnabled=true`
    + production-code-`IProductionPreconditionProvider`-
    Implementation → sonst-perfekte Aktivierung wird
    `not-dispatch-relevant` mit Reason
    `security-profile-enforcement-not-wired`.
  - **Test-Override-Pin:** `ProductionActivationEnabled=true`
    + `HealthyProductionPreconditionProvider`-Stub → Aktivierung
    ist dispatch-relevant (das ist die Voraussetzung für die
    aFRR-Profiltests in Sub-Slice E).

**D-04 TimebaseDegraded-Debounce-Konstanten sind nicht
operator-konfigurierbar.** 3-in-10 / 5-stabil sind im
Domain-Primitive verdrahtet, weil der Schwellwert die
Debounce-Charakteristik fest definiert (dünne Konfigurations-
Oberfläche heißt nicht „alles konfigurierbar"). Operator-
konfigurierbar sind nur die **Time-Validation-Toleranzen**
(`max_age`, `future_skew_tolerance`, `dedupe_window`) per
`RegelleistungOptions`.

**D-05 mFRR ist Domain-modellierbar, nicht produktiv aktivierbar.**
DoD-Wortlaut: „mFRR-Aktivierungsenergie bleibt als Produkt
modellierbar, aber produktive MOLS-/MARI-Aktivierung ist kein
M4-DoD." `ActivationProduct.Mfrr` durchläuft die ganze
Validierungs-Pipeline (auditiert, persistiert), wird aber
**zwangsläufig** als `not-dispatch-relevant` markiert — auch bei
`ProductionActivationEnabled=true`. Folgearbeit F-08 wenn der
produktive Pfad gefordert ist.

**D-06 Aktivierungs-Source-Adapter ist Driving-Port-Form.**
M4-03 stellt nur `IRegelleistungActivationUseCase.ReceiveAsync(...)`
als Eingangsschnittstelle bereit. Konkrete Source-Wire-Adapter
(OPC-UA-Subscription, MQTT-Topic, HTTP-Endpoint, TSO-spezifisches
Protokoll) bleiben Source-Slice — OPC-UA mit RM-M4-04, andere
als Folgearbeit F-09.

**D-07 Tracker-Persistenz nutzt das bestehende Postgres-via-Dapper-
Stack.** Kein neuer Backend-Typ (kein Redis, kein dediziertes
Idempotenz-System). Das passt zum existierenden Persistence-
Pattern (`schedules`, `optimization_runs`) und die Migrations-
Pipeline (DbUp + d-migrate-YAML) ist bereits etabliert.

**D-08 `Product`-Enum-Wiederverwendung von M4-02 wenn semantisch
deckend.** `ReserveProduct (Fcr/Afrr/Mfrr)` deckt die drei
Produktfamilien — RegelleistungActivation kann das wiederverwenden
statt eigenes `ActivationProduct` einzuführen. RM-M4-03-A
entscheidet das nach Code-Sicht: wenn `ReserveProduct.Fcr/Afrr/Mfrr`
ohne Felder-Kollision dupliziert, Wiederverwendung; sonst
eigenes Enum mit Cross-Mapping. Default-Lesart: Wiederverwendung
ist sauberer (keine zwei parallele Produkt-Familien-Enums).

**D-09 Dispatch-Priority-Surface.** Der Master-Plan §138 ff.
definiert `ControlPriorityClass` als **textuellen Vertrag** —
heute existiert im Code nur `MarketCommitmentPriority` (ranking
über `MarketCommitment`-Instanzen). RM-M4-03-D muss eine
Aktivierung als priority-Quelle **außerhalb** der `MarketCommitment`-
Welt einführen, weil Aktivierungen kein Marktcommitment sind.
Drei Implementierungs-Optionen — der Slice **wählt explizit (c)**:
- (a) Neuer `ControlPriorityClass`-Enum in `BatteryEms.Domain` +
  unifying-Ranker über Tagged-Union (`MarketCommitment` |
  `RegelleistungActivation`). ~80-150 LOC, größere Domain-
  Refactoring-Welle.
- (b) `DispatchRequest` bekommt `ActivationCandidate?`-Field,
  `ScheduleFollowingDispatchOptimizer` re-rankt inline. ~40-80 LOC,
  aber alle existierenden M2-Dispatch-Tests müssen re-baselined
  werden weil das Record-Format sich ändert.
- **(c) Neuer Driven Port `IActivationDispatchSource` den der
  Optimizer pre-merge konsultiert.** `ScheduleFollowingDispatchOptimizer`
  bekommt eine zusätzliche Konstruktor-Dependency auf den Port;
  der Optimizer fragt den Port pro Tick nach einer Top-Aktivierung
  und wenn vorhanden, gewinnt sie über alle Schedules (ähnlich
  wie `MarketCommitmentPriority` heute Rang 4 über Schedule-
  Rang 5/6 gewinnt). DispatchRequest-Format **bleibt unverändert**;
  M2-Dispatch-Tests bleiben grün; nur die Konstruktor-DI muss in
  Tests durchgereicht werden (Test-Stub `NoOpActivationDispatchSource`).
  Die Aktivierungs-Priorität ist über den Port-Vertrag — nicht
  über einen unifying-Enum — durchgesetzt: wenn der Port eine
  Aktivierung liefert, gewinnt sie. ~60-100 LOC inkl. Tests.

Begründung der Wahl (c) gegen (a)/(b):
- (a) ist die sauberste Architektur, aber zieht eine Domain-
  Refactoring-Welle die der Slice nicht tragen sollte.
- (b) ist am kleinsten in LOC, aber das Record-Format-Rebasing
  erzeugt Diff-Lärm in nicht verwandten M2-Tests und erschwert
  Reviews.
- (c) ist **additiv**: M2-Tests bleiben unangetastet, neue
  Dispatch-Pfade werden nur durch den neuen Port aktiviert. Die
  Konstruktor-DI-Änderung ist mechanisch (Test-Factory-Default
  auf `NoOpActivationDispatchSource`).

---

## 6. Akzeptanzkriterien

- `make gates` und `make test-integration` bleiben grün.
- aFRR-positiv- und aFRR-negativ-Profiltests dispatchen die
  Aktivierung statt des konkurrierenden Schedules
  (Rang 3 > Rang 6) bei `ProductionActivationEnabled=true` und
  allen Pre-Conditions grün.
  Bis F-12 gezündet ist, bleibt der produktive Gate-Pfad wegen
  `security-profile-enforcement-not-wired` intentional fail-closed;
  diese beiden Profiltests dürfen deshalb auf den Test-Durchfluss mit
  `HealthyProductionPreconditionProvider`-Override abstützen.
- Bei `ProductionActivationEnabled=false` (Default) gewinnen die
  Schedules — die Aktivierung ist auditiert aber `not-dispatch-
  relevant`.
- Restart-Replay: ein vor Process-Restart akzeptiertes Signal,
  identisch nach Restart wieder eingespeist, surfaced als
  `Replay_Idempotent`. Persistierter Tracker hat den Eintrag
  überlebt.
- Tiebreak-Gleichstand mit widersprüchlichem Payload → `ambiguous-
  duplicate`, kein Setpoint-Update.
- `TimebaseDegraded`-Debounce zündet nach 3-in-10 fail-closed,
  Recover nach 5-stabil oder explizit. Während `Degraded` werden
  Aktivierungen nicht dispatch-relevant.
- `dedupe-store-invalid`-Pfad: Tracker-Load fail mit beschädigtem
  Checkpoint → Aktivierung deaktiviert, Health-Endpoint
  reflektiert; nach Recovery wieder aktivierbar.
- Migrationspfad: `make schema-validate` + `make schema-drift-
  check` grün; `0002_regelleistung_activations.sql` via DbUp
  appliziert sauber + idempotent.
- Architektur-Tabu-Test: neue typisierte Exceptions/Domain-Types
  bleiben in den erlaubten Modulen (Domain + Application.Markets/
  Realtime/Persistence + entsprechende Adapter; keine Driving-
  Adapter-Querverweise).

---

## 7. Risiken und Tradeoffs

- **Slice-Größe.** RM-M4-03 ist mit ~2500-4000 LOC der größte
  M4-Slice (Envelope nach Review-Pass aufgeweitet — Sub-Slice D
  und E sind die Swing-Budget-Items, siehe untenstehender
  Risiken-Punkt). Sub-Slice-Aufteilung (A→E) entschärft den
  Review-Aufwand pro Commit, bringt aber 5 Commits + 5 Reviews.
  Tradeoff akzeptiert.
- **Sub-Slice-D-Test-Rebasing-Kosten.** Die Wahl D-09 (c)
  (`IActivationDispatchSource`-Driven-Port) ist explizit darauf
  optimiert dass `DispatchRequest`-Format und damit M2-Dispatch-
  Tests **unverändert** bleiben. Aber die Konstruktor-DI von
  `ScheduleFollowingDispatchOptimizer` ändert sich (zusätzliche
  Dependency). Alle Test-Konstruktionsstellen
  (`ScheduleFollowingDispatchOptimizerTests`,
  `DefaultScheduleOptimizationUseCaseTests`-Build-Factory analog,
  Worker-Tests die den Optimizer instantiieren) müssen den neuen
  Konstruktor-Param mit einem Default-Stub füttern. Realistic
  delta: +50-100 LOC im Test-Pfad. Falls D-09 (a) oder (b) gegen
  Empfehlung gewählt würde, wäre das Rebasing 100-200 LOC mehr.
- **Per-Sample-Clock-Validation vs. „erkannte Clock-Rücksprünge".**
  Der Master-DoD-Wortlaut „erkannte Clock-Rücksprünge" könnte als
  Cross-Sample-State gelesen werden (Monotonic-Window). Sub-Slice A
  interpretiert das **per-Sample**: ein Sample mit negativem Alter
  außerhalb `FutureSkewTolerance` ist bereits ein Clock-Rückspring-
  Verdacht und wird fail-closed abgelehnt. Diese Interpretation
  reicht für die DoD-Akzeptanz, weil ein systematischer Clock-Rückspring
  durch wiederholte Samples in der `TimebaseDebounceState`-Maschine
  (3-in-10) ohnehin als `TimebaseDegraded` materialisiert wird. Falls
  zukünftige Spec eine Cross-Sample-Monotonic-State-Anforderung
  präzisiert, ist das eigene Folgearbeit (~30-50 LOC).
- **FUP-01-Bündelung.** RM-M4-03-B zieht die erste reale Folge-
  migration. Das ist kein Risiko per se — es ist genau wofür
  FUP-01 reserviert ist. Aber: FUP-01 hat heute keinen eigenen
  Plan; M4-03-B's Migration-Patch wäre damit auch das *erste
  Beispiel* dafür wie das Migrations-Pattern in der Praxis
  aussieht. Wenn ein Reviewer gedacht hat „FUP-01 wird ein eigener
  Slice" muss er hier umdenken — Plan-Note in der Roadmap
  ergänzen wenn das passiert.
- **Source-Adapter-Lücke.** M4-03 endet beim `IRegelleistungActivationUseCase.
  ReceiveAsync(...)`-Eingang. Ohne konkreten Source-Wire-Adapter
  ist der Slice **kein produktiver End-to-End-Pfad** — er ist
  „bis-zum-Driving-Port". Der erste produktive End-to-End kommt
  mit RM-M4-04 (OPC-UA-Source). Tests pinnen das mit einem
  Test-Stub-Source.
- **Migration-Template-Pfad.** Die `0002_*.sql`-Migration in M4-03-B
  ist die *erste*, also etabliert sie das Pattern (DDL-Form,
  Idempotenz-Stil, schema.yaml-Nachzug). Spätere Migrationen
  reusen das Pattern. Wenn das Pattern später umgestellt werden
  muss (Beispiel: schema.yaml-only statt direktes SQL), ist das
  ein eigener Folge-Slice — F-07-analog für Dedupe-Schema.
- **TimebaseDegraded-Wechselwirkung mit Tests.** Tests müssen die
  Time-Validierung deterministisch machen → `IClock` als Test-Stub.
  Die existierenden `FakeClock`/`FixedClock`-Patterns (siehe
  Application.Tests + Optimization.Tests) reichen.
- **`ProductionActivationEnabled=true`-Test-Konstellation.** Die
  Pre-Conditions (Produktannahme + Time-Sync + Dedupe-Store-Health
  + Security-Profile) sind im Test-Pfad teilweise „weiche" Checks;
  M4-03-D-Tests müssen jeden Negativ-Pfad explizit pinnen, sonst
  ist der Production-Gate ein passiver Schalter ohne Schutz-
  Wirkung.

---

## 8. Sequenz

1. **RM-M4-03-A** (Domain + Time-Validation + Debounce) zuerst —
   keine externen Abhängigkeiten, reine Domain-Primitives. Bricht
   den Build nicht (rein additiv).
2. **RM-M4-03-B** (Idempotenz-Tracker + persistente Variante mit
   FUP-01-Migration) — größter strukturierter Patch, weil DDL +
   Migration + Dapper + In-Memory + schema.yaml + Tests in einer
   atomaren Einheit landen müssen damit der Migrationspfad
   testbar ist.
3. **RM-M4-03-C** (Validation-Pipeline) — kombiniert A+B. Reihen-
   folge-Pins sind das Hauptasset.
4. **RM-M4-03-D** (Use-Case + Dispatch + Production-Gate +
   Health-Endpoint) — die produktive Verdrahtung. Hier zündet die
   Erweiterung von `ScheduleFollowingDispatchOptimizer`. Tests
   müssen weiter Single-Asset / Single-Tick deterministisch
   bleiben.
5. **RM-M4-03-E** (Profiltests + Race/Restart-Tests) — die
   DoD-geforderten Ende-zu-Ende-Pins. Greift in alle Schichten
   ein, deshalb am Ende.

Jeder Sub-Slice schließt mit einem eigenen Commit und einem
Review-Pass (analog zum etablierten Pattern bei RM-M4-01/06/07).
Bei Slice-Closure markiert der letzte Commit die RM-M4-03-Zeile
in `plan-RM-M4.md` als ✅ und ergänzt eine Implementierungs-
Zusammenfassung.

---

## 9. Folgearbeiten (gehen in `note-RM-M4-followups.md`)

Die folgenden Items werden bei diesem Slice nicht implementiert,
gehen aber als F-Items in die Trigger-Watch-Notiz:

- **F-08 Produktive mFRR-MOLS/MARI-Aktivierung** — Trigger:
  TSO-/Compliance-Anforderung dass produktive mFRR-Aktivierungs-
  Energie gefordert ist (heute nur modellierbar, kein Dispatch-
  Pfad).
- **F-09 Konkrete Aktivierungs-Source-Wire-Adapter** — Trigger:
  konkrete Source-Spec von einem TSO oder Vendor. **Wichtig:** der
  RM-M4-04-DoD-Wortlaut (Master-Plan) deckt heute nur
  `IBatteryTelemetrySource` und `IBatteryCommandSink` für OPC-UA
  ab — eine Aktivierungs-Subscription ist **nicht** im RM-M4-04-
  DoD versprochen. F-09 deckt daher **alle** Source-Wire-Adapter
  inkl. eines OPC-UA-Activation-Source-Adapters; falls RM-M4-04
  bei seiner Implementierung den Scope erweitert, kann ein
  OPC-UA-Activation-Source-Carve-out dort landen, sonst ist es
  F-09. Typische andere Kandidaten: MQTT-Subscribe-Source oder
  HTTP-Webhook-Source. Verdient eigenen Slice je Source weil die
  Validation der Source-Authentizität source-spezifisch ist.
- **F-10 Aktivierungs-Quittung / Cashout-Reporting an TSO** —
  Trigger: erste Compliance-/Settlement-Anforderung dass die EMS
  bestätigte Aktivierungen an die TSO zurückreporten muss.
  Möglicherweise eigene Welle (Compliance/Settlement) statt M4.
- **F-11 Dedupe-Store-Migration v1→v2 (Template)** — analog zu
  F-07 (OPC-UA-Mapping-Migration). Trigger: erstes echtes
  Schema-Update am `regelleistung_activations`-Tisch.
- **F-12 Generisches RuntimeProfile / Security-Profile als
  Production-Gate-Signal** — Trigger: Compliance-Audit oder
  TSO-Audit verlangt ein materialisiertes „Security-Profile-grün"-
  Signal als Voraussetzung für `ProductionActivationEnabled=true`.
  Heute (D-03) hält der `IProductionPreconditionProvider` das
  als synthetischen `security-profile-enforcement-not-wired`-Reason
  fail-closed — produktiv grün wird der Production-Gate erst
  wenn F-12 zündet. **Scope-Skizze:** generisches `RuntimeProfile`
  (Development/Test/Production) als Application-Type +
  `SecurityProfileHealth`-Signal aus dem Adapter-Layer (was als
  „grün" zählt: alle Adapter mit konfiguriertem Security-Profil,
  TLS aktiv wo verlangt, Auth-Token-Quelle valide, etc.). RM-M4-05
  (OPC-UA-Security) führt OPC-UA-spezifisches Profile-Gating ein —
  F-12 hebt das zu einem Cross-Adapter-Health-Signal. Aufwand
  grob 1-2 Wochen, eigener Slice oder Carve-out in einem
  Production-Hardening-Welle-Slice (analog zur F-04-MQTT-TLS-
  Diskussion).
