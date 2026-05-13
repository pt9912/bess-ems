# Plan RM-M6-04 TimescaleDB-Erweiterung

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M6-04)
**Status:** Abgeschlossen am 2026-05-13.
**Bezug:**
[`plan-RM-M6.md`](plan-RM-M6.md)
(M6-Masterplan),
[`../done/plan-RM-M6-01.md`](../done/plan-RM-M6-01.md)
(Operator-UI-Slice),
[`../done/plan-RM-M6-02.md`](../done/plan-RM-M6-02.md)
(Multi-Asset-Hosting-Default),
[`../done/plan-RM-M6-03.md`](../done/plan-RM-M6-03.md)
(Helm-/Deployment-Slice),
[`../../../user/persistence.md`](../../../user/persistence.md)
(Persistenz-Runbook)

---

## Ziel

RM-M6-04 liefert den ersten TimescaleDB-kompatiblen Persistenzpfad fuer
hochvolumige Telemetrie, ohne PostgreSQL als Default zu ersetzen und ohne
Domain- oder Application-Ports zu veraendern. Timescale bleibt ein
Adapter-/Migrationsdetail.

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M6-04-A | Optionaler Timescale-Guard | Eine RunOnce-Migration erkennt `timescaledb` ueber `pg_available_extensions`; auf Plain-Postgres ist sie ein No-op und wird trotzdem sauber journalisiert. |
| ✅ | RM-M6-04-B | Telemetrie-Hypertable | Wenn `timescaledb` verfuegbar und installierbar ist, wird `telemetry` auf `recorded_at` zur Hypertable. Der Primaerschluessel wird nur in diesem Pfad Timescale-kompatibel auf `(id, recorded_at)` erweitert. |
| ✅ | RM-M6-04-C | Regression Pins | Unit-Tests pinnen die Guard-Form der Migration; Integrationstests beweisen, dass Plain-Postgres nach der Migration weiter Telemetrie schreiben/lesen kann. |
| ➡️ | RM-M6-04-D | Aggregates/Compression | Bewusst spaeterer Folge-Slice fuer Continuous Aggregates, Compression/Retention-Policies und Timescale-spezifische Betriebswerte, sobald reale Datenvolumen/Abfrageprofile vorliegen. |

---

## Entscheidungen

- **PostgreSQL bleibt Default:** `postgres:16` und bestehende
  Connection-Strings bleiben gueltig. Keine Timescale-Abhaengigkeit im
  Host-Start.
- **No-op auf Plain-Postgres:** Fehlt `timescaledb`, laeuft die Migration
  weiter und hinterlaesst nur einen DbUp-Journal-Eintrag.
- **Keine Port-Aenderung:** `ITelemetryRepository` und
  `DapperTelemetryRepository` bleiben unveraendert. Hypertable-Details
  leaken nicht in Domain/Application.
- **Telemetrie zuerst:** Nur die hochfrequente `telemetry`-Tabelle wird
  optional hypertable-faehig. Commands, Schedules und Audit bleiben
  normale relational modellierte Tabellen.

---

## Akzeptanzkriterien

- Plain-Postgres-Migration bleibt erfolgreich und idempotent.
- `0005_timescale_telemetry_hypertable.sql` ist als RunOnce-Migration
  eingebettet und numerisch kontinuierlich.
- Telemetrie-Append und Latest-Query funktionieren nach der Migration auf
  Plain-Postgres unveraendert.
- TimescaleDB ist dokumentiert als optionale Beschleunigung, nicht als
  neuer Default oder fachliche Voraussetzung.

## Verifikation

- ✅ `make test`
- ✅ `make test-integration`
