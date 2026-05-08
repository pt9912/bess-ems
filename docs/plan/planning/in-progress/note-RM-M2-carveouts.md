# Note: RM-M2-Folgewellen-Carveouts (Pre-Push-Review)

**Dokumenttyp:** Notiz / Follow-up-Liste
**Status:** In Arbeit — Punkte aus dem Pre-Push-Review der
M2-Folgewellen RM-M2-MIG (Persistence-Migrations) und RM-M2-HIL
(HIL-Simulator), die der Reviewer als nicht-blockend für den Push
klassifiziert hat. Wird der unten empfohlenen Reihenfolge nach
abgearbeitet.
**Bezug:** [`../done/plan-RM-M2-migration.md`](../done/plan-RM-M2-migration.md),
[`../done/HIL-simulator.md`](../done/HIL-simulator.md),
[`roadmap.md`](roadmap.md)

---

## Adapter-Qualität (Modbus-Pfad)

- [x] **M3 — `FluentModbusClient.ConnectAsync` synchroner Connect unter Semaphore.** ✅
  Datei: `src/adapters/driven/BatteryEms.Adapters.Modbus/FluentModbusClient.cs`.
  Zwei Hebel: (1) DNS jetzt async via `Dns.GetHostAddressesAsync` und
  **außerhalb** der Semaphore — parallele First-Connect-Caller serialisieren
  nicht mehr auf Resolution. (2) Der synchrone `_client.Connect(...)` läuft
  jetzt in `Task.Run` unter dem Gate — die per-Client-Serialisierung (HIL-
  Race-Fix) bleibt intakt, der Aufrufer-Sync-Context wird während des
  TCP-Handshakes nicht mehr gepinnt.
  Tests: alle Modbus-Unit-Tests grün, HIL-Roundtrip grün.

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

- [x] **Mn3 — `bess-hil-simulator` Service hat keinen Healthcheck.** ✅
  Dateien: `tests/hil/compose.yml`, `deploy/compose.hil.yml`.
  Beide Compose-Dateien: TCP-Probe via Bash-Built-in
  `exec 3<>/dev/tcp/localhost/502` als healthcheck (interval 2s,
  retries 15, start_period 5s) — kein `nc`/`curl` im Simulator-
  Image nötig. Abhängige Services auf `condition: service_healthy`
  umgestellt. Smoke-getestet: `Health: healthy` reproduzierbar,
  `make test-hil-modbus` weiter grün.

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
