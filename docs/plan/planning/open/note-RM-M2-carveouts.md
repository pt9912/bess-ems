# Note: RM-M2-Folgewellen-Carveouts (Pre-Push-Review)

**Dokumenttyp:** Notiz / Follow-up-Liste
**Status:** Offen — Punkte aus dem Pre-Push-Review der M2-Folgewellen
RM-M2-MIG (Persistence-Migrations) und RM-M2-HIL (HIL-Simulator),
die der Reviewer als nicht-blockend für den Push klassifiziert
hat. Pflegen, abhaken oder beim nächsten passenden Slice
mitnehmen.
**Bezug:** [`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md),
[`../done/HIL-simulator.md`](../done/HIL-simulator.md),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md)

---

## Adapter-Qualität (Modbus-Pfad)

- [ ] **M3 — `FluentModbusClient.ConnectAsync` synchroner Connect unter Semaphore.**
  Datei: `src/adapters/driven/BatteryEms.Adapters.Modbus/FluentModbusClient.cs:38-57`.
  `_client.Connect(...)` läuft synchron unter dem Gate; bei langsamem
  DNS/TCP-Handshake stehen alle queued Reads/Writes. Mit der Production-
  Singleton-Verdrahtung blockiert das die Regelschleife.
  *Trigger:* echte Geräte oder ein zäh anlaufender Simulator.
  *Aufwand:* ~10 LOC — `Task.Run` um den Connect oder ein
  FluentModbus-`ConnectAsync`-Pendant verwenden.

- [ ] **Mn1 — `ModbusCommandSink` verschluckt Q-Setpoint stillschweigend.**
  Datei: `src/adapters/driven/BatteryEms.Adapters.Modbus/ModbusCommandSink.cs:110-117`.
  Hat das Mapping keinen `reactive_power_setpoint_kvar`-Eintrag, wird
  ein non-null Q-Command kommentarlos verworfen — Operator-Intent
  geht still verloren.
  *Trigger:* sobald jemand Q über ein P-only-Profil fahren will.
  *Aufwand:* ~15 LOC — Log-Warn bei Adapter-Wiring oder Surfacing
  über `CommandDispatchResult.Reason`.

- [ ] **N2 — `RegisterDecoder.Encode` mit `ScaleFactor=0` asymmetrisch zu `Decode`.**
  Datei: `src/adapters/driven/BatteryEms.Adapters.Modbus/RegisterDecoder.cs:32`.
  `Encode` interpretiert raw bits, `Decode` liefert 0. Beide Pfade
  divergieren bei einem ohnehin illegitimen Wert.
  *Trigger:* Profil mit `scale_factor: 0` (sollte am Loader sterben).
  *Aufwand:* ~5 LOC — Reject in `JsonFileConfigurationLoader.LoadModbusMapping`.

---

## Code-Klarheit

- [ ] **M2 — `BessDbMigrator.ExecuteAdvisoryLockAsync` Bool-Switch versteckt asymmetrische Cancellation-Semantik.**
  Datei: `src/adapters/driven/BatteryEms.Adapters.Persistence/BessDbMigrator.cs:97-109`.
  Acquire **muss** CancellationToken propagieren, Release **darf
  nicht** (Postgres gibt session-scoped Locks am Sessionende
  ohnehin frei). Die Bool-Variante hidet das hinter einem
  Parameter.
  *Trigger:* keiner — kosmetisch.
  *Aufwand:* ~10 LOC — Split in `AcquireAdvisoryLockAsync(CT)` und
  `ReleaseAdvisoryLockAsync()`.

---

## Test- und CI-Hygiene

- [ ] **Mn2 — `tests/hil/Dockerfile` kopiert `tests/` ungefiltert.**
  Datei: `tests/hil/Dockerfile:14`.
  `COPY tests/ tests/` defeated den Docker-Layer-Cache; jede
  unrelated Test-Edit bustet das HIL-Image-Build.
  *Aufwand:* ~5 LOC — per-Projekt-COPY oder ein
  `dotnet sln`-pruned Fileset.

- [ ] **Mn4 — `MigrationResourceSetTests` enforceren keine Exklusivität.**
  Datei: `tests/adapters/driven/BatteryEms.Adapters.Persistence.Tests/MigrationResourceSetTests.cs`.
  Tests prüfen „RunOnce enthält `0001_initial.sql`" und „Drafts ist
  leer", aber nicht „nur RunOnce-Pattern-Resourcen sind embedded".
  Ein versehentliches `<EmbeddedResource Include="**/*.json"/>`
  käme durch.
  *Aufwand:* ~10 LOC — Allowlist-Test über das Manifest.

---

## Operational Readiness

- [ ] **Mn3 — `bess-hil-simulator` Service hat keinen Healthcheck.**
  Dateien: `tests/hil/compose.yml`, `deploy/compose.hil.yml`.
  `condition: service_started` lässt den Sidecar als „up" gelten,
  bevor der Modbus-TCP-Listener bereit ist; `bess-ems` rennt beim
  ersten Cycle in ein ECONNREFUSED-Rauschen.
  *Aufwand:* ~5 Zeilen pro Compose-Datei — TCP-Probe (`nc -z
  localhost 502`) als healthcheck, abhängiger Service auf
  `condition: service_healthy` umstellen.

---

## Demo-Polish

- [ ] **Demo-01 — HIL-Closed-Loop-Discharge-Smoke.**
  Aktuell pendelt der Closed-Loop-Demo gegen `deploy/compose.hil.yml`
  auf `no-active-commitment` (kein Fahrplan geladen → Idle/0 kW).
  Ein zusätzlicher Smoke (Bash-Skript oder neuer `[Trait=HIL]`-Test)
  würde via `POST /markets/day-ahead/optimize` einen Discharge
  triggern und assertern: HIL-`active_power_kw` reagiert auf den
  Setpoint, `soc_percent` sinkt monoton.
  *Trigger:* sobald HIL als Demo gezeigt werden soll.
  *Aufwand:* ~50 LOC + ein Fixture-Schedule.

---

## Empfohlene Reihenfolge

1. **Mn3** zuerst (5 Min, beseitigt das ECONNREFUSED-Rauschen sofort).
2. **M3** danach — aktiviert sich erst unter Last, gut zu haben, kein Notfall.
3. **Mn1 + N2** mit dem nächsten Modbus-Profil-Touch (HIL-Erweiterung oder RM-M4 OPC-UA).
4. **M2 + Mn2 + Mn4** als Hygiene-Slice, jederzeit machbar.
5. **Demo-01** sobald HIL als Demo gezeigt werden soll.
