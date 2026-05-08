# Note: RM-M2-Folgewellen-Carveouts (Pre-Push-Review)

**Dokumenttyp:** Notiz / Follow-up-Liste
**Status:** Abgeschlossen — alle sieben Carve-outs aus dem Pre-Push-
Review der M2-Folgewellen RM-M2-MIG (Persistence-Migrations) und
RM-M2-HIL (HIL-Simulator) wurden in der empfohlenen Reihenfolge
abgearbeitet (Mn3 → M3 → Mn1 + N2 → M2 + Mn2 + Mn4 → Demo-01).
**Bezug:** [`plan-RM-M2-migration.md`](plan-RM-M2-migration.md),
[`HIL-simulator.md`](HIL-simulator.md),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md)

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

- [x] **Mn1 — `ModbusCommandSink` verschluckt Q-Setpoint stillschweigend.** ✅
  Datei: `src/adapters/driven/BatteryEms.Adapters.Modbus/ModbusCommandSink.cs`.
  `WriteAsync` hängt jetzt `;q-dropped:no-mapping` an die Reason an,
  wenn ein non-zero `ReactivePowerKvar` an einem Q-losen Mapping
  ankommt. Dispatch-Result bleibt erfolgreich (P-Write ging durch),
  aber der Audit-Trail zeigt den Operator-Intent-Verlust. Zwei Tests
  (positiv non-zero Q + kein Mapping → q-dropped, negativ zero Q →
  kein Tag).

- [x] **N2 — `RegisterDecoder.Encode` mit `ScaleFactor=0` asymmetrisch zu `Decode`.** ✅
  Datei: `config/schema/modbus-mapping.schema.json`.
  Schema lehnt `scale_factor: 0` jetzt mit `"not": {"const": 0}` ab —
  ein offensichtlich kaputtes Profil scheitert vor dem Loader, der
  Decoder/Encoder-Asymmetrie wird gar nicht erst sichtbar. Loader-
  Test pinnt das Verhalten.

---

## Code-Klarheit

- [x] **M2 — `BessDbMigrator.ExecuteAdvisoryLockAsync` Bool-Switch versteckt asymmetrische Cancellation-Semantik.** ✅
  Datei: `src/adapters/driven/BatteryEms.Adapters.Persistence/BessDbMigrator.cs`.
  In `AcquireAdvisoryLockAsync(NpgsqlConnection, CancellationToken)`
  und `ReleaseAdvisoryLockAsync(NpgsqlConnection)` gesplittet — die
  Acquire-Seite trägt den Token explizit (Boot-Cancel muss den Wait
  abbrechen können), die Release-Seite nimmt **keinen** Token (ein
  abgebrochenes Unlock würde den Lock bis Sessionende halten, was
  strikt schlechter ist als unconditional Unlock). Same wire
  semantics, klarere Intent — alle bisherigen Migrator-Tests
  weiterhin grün.

---

## Test- und CI-Hygiene

- [x] **Mn2 — `tests/hil/Dockerfile` kopiert `tests/` ungefiltert.** ✅
  Datei: `tests/hil/Dockerfile`.
  COPY-Liste auf das Minimum reduziert: nur das HIL-Test-Projekt
  (`tests/integration/BatteryEms.Hil.IntegrationTests/`) plus
  `tests/Directory.Build.props` (NoWarn-Overrides für die
  Test-only Analyzer-Regeln). Jede unrelated Test-Edit bustet den
  HIL-Image-Layer jetzt nicht mehr.

- [x] **Mn4 — `MigrationResourceSetTests` enforceren keine Exklusivität.** ✅
  Datei: `tests/adapters/driven/BatteryEms.Adapters.Persistence.Tests/MigrationResourceSetTests.cs`.
  Neuer Test `Embedded_resource_set_contains_only_RunOnce_migration_scripts`
  fenced das Manifest: jede Assembly-Resource muss entweder dem
  RunOnce-Pattern entsprechen oder auf einer expliziten Allowlist
  stehen (heute leer für `.g.resources`-Future-Proofing). Ein
  versehentliches `<EmbeddedResource Include="**/*.json"/>` würde
  jetzt sofort kippen.

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

- [x] **Demo-01 — HIL-Closed-Loop-Discharge-Smoke.** ✅
  Datei: `scripts/hil-closed-loop-smoke.sh`, Make-Target
  `make test-hil-closed-loop`.
  Bash-Smoke gegen `deploy/compose.hil.yml` mit drei Asserts:
  (1) `POST /markets/day-ahead/optimize` antwortet mit
  `status=optimal`, (2) bess-ems emittiert `mode=Discharge` mit
  positivem `power_kw`, (3) `bess-hil-simulator` logt
  `[MODBUS] Command received` mit non-zero P. Trap räumt den
  Stack immer ab. Verlangt `BESS_HIL_OPERATOR_TOKEN` in der
  Umgebung. Smoke-getestet end-to-end gegen die echte HIL-
  Pipeline: optimize → dispatch → Modbus-Setpoint im Simulator
  durchgehend grün.

---

## Empfohlene Reihenfolge

1. **Mn3** zuerst (5 Min, beseitigt das ECONNREFUSED-Rauschen sofort).
2. **M3** danach — aktiviert sich erst unter Last, gut zu haben, kein Notfall.
3. **Mn1 + N2** mit dem nächsten Modbus-Profil-Touch (HIL-Erweiterung oder RM-M4 OPC-UA).
4. **M2 + Mn2 + Mn4** als Hygiene-Slice, jederzeit machbar.
5. **Demo-01** sobald HIL als Demo gezeigt werden soll.
