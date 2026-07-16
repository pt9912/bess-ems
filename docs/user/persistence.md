# Persistenz-Runbook

**Bezug:** [`spec/lastenheft.md`](../../spec/lastenheft.md) [§19](../../spec/lastenheft.md#19-persistenz-anforderungen)
([LH-PERSIST-001](../../spec/lastenheft.md#lh-persist-001--speicherung-von-messdaten)..[007](../../spec/lastenheft.md#lh-persist-007--speicherung-von-optimierungsläufen)), [`spec/spezifikation.md`](../../spec/spezifikation.md) [§3](../../spec/spezifikation.md#3-persistenz) (Persistenz)

Dieses Dokument fixiert das Betriebsverhalten der Persistenzschicht für
M1: was wird gespeichert, wie ist die Aufbewahrungspolitik konfiguriert
und wie verhält sich `bess-ems` bei Persistenzfehlern. Code-Pfade siehe
`src/adapters/driven/BatteryEms.Adapters.Persistence/`.

---

## 1. Was wird gespeichert

| Datenklasse        | Tabelle             | Pflichtfelder                                                                                | LH-Bezug          |
| ------------------ | ------------------- | -------------------------------------------------------------------------------------------- | ----------------- |
| Telemetrie         | `telemetry`         | Asset, Zeitstempel, SOC/SOH, Wirk-/Blindleistung, DC-V/I, Temperatur, Verfügbarkeit, DataQuality | [LH-PERSIST-001](../../spec/lastenheft.md#lh-persist-001--speicherung-von-messdaten)    |
| Commands           | `commands`          | CommandId (PK), Asset, Mode, Sollwert, Reason, Source, Dispatch-Erfolg + Reason             | [LH-PERSIST-002](../../spec/lastenheft.md#lh-persist-002--speicherung-von-commands)    |
| Fahrpläne          | `schedules`, `schedule_windows` | (AssetId, Type) PK, Version, Bid Area, halboffene UTC-Fenster                       | [LH-PERSIST-003](../../spec/lastenheft.md#lh-persist-003--speicherung-von-fahrplänen)    |
| Operator-Audit     | `audit_events`      | Zeitstempel, Operator, Action, optional TargetAsset, Reason, Outcome                          | [LH-PERSIST-004](../../spec/lastenheft.md#lh-persist-004--speicherung-von-operator-kommandos)    |

`Append`-Pfade sind die einzige Schreib-API der Anwendung; das
`IOperatorAuditLog`-Port hat **kein** `Delete`/`Truncate` — Audit ist
strukturell append-only ([LH-PERSIST-006](../../spec/lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen)).

---

## 2. Retention-Konfiguration ([LH-PERSIST-006](../../spec/lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen))

### Format

`config/retention.json` (Schema:
[`config/schema/retention.schema.json`](../../config/schema/retention.schema.json),
Beispiel: [`config/examples/retention.json`](../../config/examples/retention.json))

```json
{
  "telemetry_retention": "90.00:00:00",
  "commands_retention": "365.00:00:00",
  "schedules_retention": "30.00:00:00"
}
```

Jeder Eintrag ist ein C#-`TimeSpan`-String (`D.HH:MM:SS`). **Fehlt der
Eintrag, behält die Anwendung diese Datenklasse für immer**. Das ist
kein Bug, sondern die [LH-PERSIST-006](../../spec/lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen)-konforme sichere Voreinstellung.

### Audit-Sonderfall

`operator_audit_retention` ist im Default-Beispiel **bewusst
weggelassen**. Damit greift [LH-PERSIST-006](../../spec/lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen):

> keine automatische Löschung auditrelevanter Daten ohne explizite
> Konfiguration

Wer Audit doch automatisch löschen will, muss den Eintrag explizit
setzen. Der Loader nimmt jeden positiven `TimeSpan` an. Im
`RetentionRunResult` taucht der Default als `OperatorAuditPreserved =
true` auf, damit Logs/Metriken die Inaktivität dieses Pfads sichtbar
machen.

### Lauf

`Application.Persistence.RetentionRunUseCase.ExecuteAsync(policy, ct)`
orchestriert die Lösch-Aufrufe an
`Application.Persistence.IRetentionRepository`. Für jede non-null
`TimeSpan` wird `cutoff = now - retention` berechnet und die
entsprechende `Delete*OlderThanAsync(cutoff, ct)`-Methode gerufen. Null
Einträge überspringen die Klasse vollständig — die Repository wird
nicht angefasst.

Schedules werden gelöscht, wenn **alle** ihre Fenster vor dem Cutoff
enden (`MAX(window_end) < cutoff`); die `schedule_windows`-Zeilen
verschwinden über die `ON DELETE CASCADE`-Beziehung mit.

Der Use Case ist stateless. M1 wired ihn noch nicht periodisch ein; das
übernimmt der Worker in **[RM-M1-19](../plan/planning/done/plan-RM-M1.md)** (Composition Root). Operatoren
können das Verhalten heute über Integrationstests oder spätere
Worker-Endpunkte triggern.

### Datenvolumen-Begrenzung

[LH-PERSIST-006](../../spec/lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen) fordert eine "konfigurierbare Begrenzung oder
Archivierung hochfrequenter Messdaten". M1 deckt das über die
Telemetrie-Retention ab: ein `telemetry_retention` von `30.00:00:00`
hält den Tabellenumfang stabil bei 30 Tagen Hochfrequenz-Daten.
Archivierung in ein zweites Backend (z. B. TimescaleDB-Continuous
Aggregates) ist explizit Nach-MVP ([LH-PERSIST-005](../../spec/lastenheft.md#lh-persist-005--datenbank), Roadmap M6).

### TimescaleDB-Erweiterung (RM-M6-04)

PostgreSQL bleibt der Default. Ab [RM-M6-04](../plan/planning/done/plan-RM-M6-04.md) enthält die
RunOnce-Migration `0005_timescale_telemetry_hypertable.sql` einen
optionalen TimescaleDB-Pfad:

- Wenn `timescaledb` in `pg_available_extensions` nicht sichtbar ist,
  bleibt `telemetry` eine normale PostgreSQL-Tabelle. Die Migration wird
  trotzdem im DbUp-Journal vermerkt.
- Wenn `timescaledb` installiert, aber nicht via
  `shared_preload_libraries` vorgeladen ist, bleibt die Migration
  ebenfalls ein No-op. TimescaleDB muss auf dem Postgres-Server
  vorgeladen und der Server danach neu gestartet sein, bevor der
  Hypertable-Pfad aktiv wird.
- Wenn `timescaledb` verfügbar und durch den Datenbank-User installierbar
  ist, wird `telemetry` auf `recorded_at` zur Hypertable.
- Die fachlichen Persistenz-Ports und `DapperTelemetryRepository` bleiben
  unverändert. Timescale ist ein Schema-/Adapterdetail.
- Continuous Aggregates, Compression Policies und Timescale-native
  Retention sind bewusst nicht Default; sie brauchen reale
  Datenvolumen-/Abfrageprofile und folgen als eigener Hardening-Slice.

---

## 3. Verhalten bei Persistenzfehlern

[LH-PERSIST-006](../../spec/lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen): "definiertes Verhalten, kein undefinierter Regelbetrieb".

### Schreibfehler im Regelkreis

Telemetrie- oder Command-Append-Aufrufe können fehlschlagen, wenn die
Datenbank überlastet ist, eine Verbindung abreißt oder die Platte voll
ist. Das verbindliche Verhalten:

- **Der Regelkreis bleibt aktiv.** Der 1-s-Zyklus liest Snapshots aus
  dem `ISnapshotStore` (in-memory), läuft durch State Machine und
  Limiter und sendet das Ergebnis über den Adapter. Persistenz ist
  Beobachtung, nicht Steuerung.
- **Der Persistenzfehler wird strukturiert geloggt** — JSON mit
  `error.type`, `error.reason`, `data_class`, `asset_id`, `timestamp`.
- **`AdapterStatus.LastError` markiert den degradierten Pfad.** Kommt
  die Persistenz wieder, wird der Status beim nächsten erfolgreichen
  Append zurückgesetzt; bis dahin bleibt der Adapter "Connected mit
  Fehler".
- **Es gibt keinen In-Memory-Buffer.** Verlorene Telemetrie-Punkte
  werden beim Wiederhochfahren der Persistenz nicht nachgeholt — das
  bleibt fachlich akzeptabel, weil der Regelkreis ohnehin den
  in-memory-`ISnapshotStore` als Wahrheit sieht und Telemetrie für M1
  nur als historischer Trend genutzt wird.

### Disk-Voll / Connection-Loss

PostgreSQL meldet `disk full` als generische `Npgsql`-Exception. Der
Adapter:

1. fängt die Exception (CA1031 dokumentiert pro Methode),
2. loggt strukturiert,
3. surfaced den Fehler über `AdapterStatus`/`CommandDispatchResult`
   wie jeden anderen Persistenzfehler.

Ein dauerhaft volles Backend wird **nicht** automatisch durch Retention
freigeräumt — die Retention läuft nur, wenn der Worker sie startet, und
braucht eine funktionierende DB-Verbindung. Operative Wiederherstellung
heißt: Speicher freischaufeln (manuell oder per Retention-Konfiguration
mit aggressiveren Werten), dann den Worker neu anlaufen lassen.

### Initialisierungsfehler

`BessDbMigrator.MigrateAsync` läuft beim Worker-Start ([RM-M2-MIG-05](../plan/planning/done/plan-RM-M2-migration.md)).
Schlägt der Aufruf fehl, **darf der Regelbetrieb nicht starten** — das
ist normativ in [spezifikation.md §3](../../spec/spezifikation.md#3-persistenz) + [LH-CONF-003](../../spec/lastenheft.md#lh-conf-003--validierung-der-konfiguration) festgelegt. Die Composition-Root-Wiring ([RM-M1-19](../plan/planning/done/plan-RM-M1.md))
muss diesen Fehler explizit behandeln.

---

## 4. Test-Gates

| Pfad                                                     | Was wird geprüft                                              |
| -------------------------------------------------------- | ------------------------------------------------------------- |
| `make test` (Domain)                                     | `RetentionPolicy` Invarianten + Default-Konstante             |
| `make test` (Application)                                | `RetentionRunUseCase` mit Fake-Repo: null-skip, audit-preserve, cutoff-Berechnung, Negative-Retention-Reject |
| `make test` (Infrastructure)                             | Loader liest `retention.json`, akzeptiert explizites Audit, lehnt unbekannte Felder + ISO-8601-Strings ab |
| `make test-integration` (Persistence)                    | Echtes Postgres: alte Zeilen weg, neue da, Audit unangetastet bei null-Retention |

---

## 5. Bekannte Lücken (post-M1)

- **Periodische Ausführung**: M1 liefert den Use Case, kein
  Scheduler. [RM-M1-19](../plan/planning/done/plan-RM-M1.md) verdrahtet ihn.
- **Pro-Asset-Retention**: M1-14 schaltet Retention global per
  Datenklasse. Asset-spezifische Retention (z. B. "behalte
  Sicherheits-Asset-Telemetrie länger") ist Nach-MVP.
- **Archivierung statt Löschung**: M1 löscht; Cold-Storage-Archivierung
  ist Nach-MVP (z. B. TimescaleDB-Aggregates oder S3-Export, Roadmap M6).
- **TimescaleDB**: Native Time-Series-Optimierungen folgen mit
  [RM-M6-04](../plan/planning/done/plan-RM-M6-04.md), ohne fachliche Persistenz-API zu ändern ([LH-PERSIST-005](../../spec/lastenheft.md#lh-persist-005--datenbank)).
