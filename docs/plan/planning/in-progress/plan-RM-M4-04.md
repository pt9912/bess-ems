# Plan RM-M4-04 — OPC-UA-Adapter (Lesen, Schreiben, Subscriptions, StatusCode)

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M4-04)
**Status:** Offen — wird in Sub-Slices RM-M4-04-A..D umgesetzt
**Bezug:**
[`plan-RM-M4.md`](plan-RM-M4.md) (Master-Plan, RM-M4-04-Zeile mit DoD und LH-Bezug),
[`../done/plan-RM-M4-03.md`](../done/plan-RM-M4-03.md) (RM-M4-03 D-06 + F-09: Driving-Port-Form für Aktivierungs-Source — OPC-UA-Activation-Subscription bleibt Folgearbeit, nicht in M4-04),
[`../open/note-RM-M4-followups.md`](../open/note-RM-M4-followups.md) (F-07 OPC-UA-Mapping-Migration v1→v2 als Template-Slice; F-09 Source-Wire-Adapter inkl. OPC-UA-Activation-Source),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md) (LH-OPCUA-001 Lesen, LH-OPCUA-002 Schreiben, LH-OPCUA-003 Subscriptions, LH-OPCUA-004 StatusCode, LH-OPCUA-005 Security — letzteres separater Slice RM-M4-05)

---

## 1. Zweck

RM-M4-04 ist die produktive Verdrahtung des OPC-UA-Protokollstapels
in die bestehenden Driven-Ports `IBatteryTelemetrySource` und
`IBatteryCommandSink`. RM-M4-07 hat das versionierte Mapping-Schema +
JSON-Loader für `OpcUaMappingConfiguration`/`OpcUaNodeMapping`
geliefert; M4-04 nimmt diese Konfiguration als Eingabe und stellt
einen funktionsfähigen OPC-UA-Client-Adapter bereit, der pro Node-
Mapping die DoD-pflichtigen Operationen (read, subscribe, write)
ausführt und schlechte StatusCodes per `DataQuality.ProtocolError(...)`
ans Application-Layer weiterreicht.

Aus dem Master-Plan RM-M4-04-DoD (gekürzt):

> Adapter-Projekt `BatteryEms.Adapters.OpcUa` implementiert die
> bestehenden Driven-Ports `IBatteryTelemetrySource` und
> `IBatteryCommandSink`; Node-Werte werden in interne Telemetrie
> gemappt; Commands schreiben konfigurierte NodeIds; Subscriptions
> liefern Updates; schlechte StatusCodes markieren Daten ungültig.

Der Slice ist substantiell: neues Adapter-Projekt + OPC-UA-NuGet +
Client-Port-Abstraktion (testbar ohne echten Server) +
Telemetry-Source mit Read+Subscribe+StatusCode-Mapping +
Command-Sink mit Write+Acknowledgment + Composition-Root-Branch +
HIL-Integration gegen einen OPC-UA-Simulator. Daher ist er in vier
Sub-Slices aufgeteilt (RM-M4-04-A bis RM-M4-04-D) — jeder einzeln
review- und committierbar.

Security-Härtung (Zertifikate, Security Mode/Policy, Production-Fail-
Closed) ist **nicht** im Scope — das ist Master-Arbeitspaket
RM-M4-05 mit eigenem DoD. M4-04 läuft mit `SecurityMode=None` gegen
den Simulator (D-04).

---

## 2. Aktivierungsbedingungen

| Check | Erwartung | Stand heute |
|-------|-----------|-------------|
| RM-M4-07 (OPC-UA-Mapping-Schema + Loader) ✅ | `OpcUaMappingConfiguration` + `OpcUaNodeMapping` Records existieren; `JsonFileConfigurationLoader.LoadOpcUaMapping` liefert validierte Mappings | ✅ |
| `IBatteryTelemetrySource` + `IBatteryCommandSink` Ports | Existieren in `Application/IO/`; Modbus + MQTT setzen sie heute produktiv um | ✅ |
| Composition-Root-Branch-Stelle | `BessHostBuilder.BuildApp` hat den Modbus/MQTT/NoOp-Triage-Block ab `BessHostBuilder.cs` etabliert; OPC-UA-Branch fügt sich symmetrisch ein | ✅ |
| OPC-UA-NuGet-Paket | Heute **nicht** in `Directory.Packages.props` referenziert; M4-04-A führt es ein (siehe D-01) | ⬜ |
| Architektur-Tabu für Hexagon | `Opc.Ua` ist in `FrameworkTaboosForHexagon` der Architektur-Tests gelistet — Domain/Application dürfen nicht referenzieren, Adapter-Projekt darf | ✅ |
| HIL-Simulator-Verfügbarkeit | Modbus-Simulator existiert; ein OPC-UA-Simulator (für RM-M4-04-D-Integration) ist heute nicht verdrahtet — M4-04-D entscheidet entweder Embedded-Test-Server (Bibliotheks-Stub) oder Docker-Side-Container | 🟡 |
| RM-M4-05 (OPC-UA-Security) | Eigenständiger Slice; M4-04 läuft mit `SecurityMode=None` gegen den Simulator und die Health-Surface meldet `security-not-hardened` als bewusstes Pre-M4-05-Signal | n/a |

---

## 3. Scope

**In Scope (RM-M4-04-A..D zusammen):**

- **OPC-UA-NuGet-Aufnahme** in `Directory.Packages.props` mit
  Version-Pin (siehe D-01).
- **Adapter-Projekt** `BatteryEms.Adapters.OpcUa` mit `csproj`,
  `AssemblyMarker`, Architektur-Tabu-Kompatibilität (Adapter darf
  `Opc.Ua` referenzieren; Domain/Application nicht).
- **`IOpcUaClient`-Driven-Port** + **`OpcUaClient`**-Production-Impl
  (wraps das gewählte SDK) + **`FakeOpcUaClient`**-Test-Stub (D-02).
  Der Port abstrahiert Connect/Disconnect, ReadValue, WriteValue,
  CreateSubscription, MonitoredItem-Notification-Stream — analog zur
  bestehenden `IModbusClient`/`IMqttClient`-Linie.
- **`OpcUaAdapterOptions`** mit `EndpointUrl`, `SessionName`,
  `KeepAliveIntervalMs`, `ReadTimeoutMs`, `ReconnectBackoff` u. ä.;
  Defaults konservativ (siehe D-04 für Security-Defaults).
- **`OpcUaTelemetrySource`** implementiert `IBatteryTelemetrySource`:
  pro Mapping-Node mit `direction=read` Pollen, `direction=subscribe`
  als MonitoredItem registrieren, `IAsyncEnumerable<BatteryTelemetry>`
  über einen `Channel<BatteryTelemetry>` füttern. StatusCode-Mapping
  per `DataQuality`-Translator (LH-OPCUA-004, D-06).
- **`OpcUaCommandSink`** implementiert `IBatteryCommandSink`: schreibt
  pro Command den passenden Mapping-Node mit `writable=true`. Bestätigung
  über `WriteResult`-StatusCode; mismatch oder schlechter StatusCode →
  `CommandDispatchResult.Failure(...)` mit kebab-case-Reason.
- **`OpcUaRegistration.AddBessOpcUa(...)`** als DI-Erweiterung analog
  zu `AddBessModbus`/`AddBessMqtt`.
- **`BessHostOptions`-Erweiterung** um `OpcUaMappingPath`,
  `OpcUaEndpointUrl`, `OpcUaSessionName` etc.;
  **`BessConfigurationBootstrap`-Erweiterung** um den Loader-Aufruf;
  **`BessHostBuilder`-Branch** um die OPC-UA-Aktivierung gegen den
  Modbus/MQTT/NoOp-Triage.
- **Unit-Tests** mit `FakeOpcUaClient` für Telemetry-Source +
  Command-Sink + StatusCode-Mapping.
- **HIL-Integration** in `tests/integration/BatteryEms.Hil.IntegrationTests`
  oder einem neuen Sub-Tree gegen einen OPC-UA-Simulator (D-07
  entscheidet über Bibliotheks-Stub vs. Docker-Side-Container).

**Out of Scope (separate Slices):**

- **OPC-UA-Security-Härtung** — RM-M4-05 (Zertifikate, Security
  Mode/Policy, Production-Fail-Closed, AllowUnsecured-Pattern). M4-04
  läuft mit `SecurityMode=None` und einer Konfig-Warnung.
- **OPC-UA-Activation-Source** — RM-M4-03 D-06 / F-09. Der
  `IRegelleistungActivationUseCase`-Driving-Port ist heute der
  Eingangspunkt; ein OPC-UA-Subscribe-Adapter, der Aktivierungs-
  Signale auf den Use-Case hebelt, ist Folgearbeit (entweder als
  Carve-out hier oder als eigener F-09-Slice).
- **Mapping-Migration v1→v2** — F-07 (Template-Slice), zündet sobald
  ein realer Schema-Bruch im Mapping-Format gefordert ist.
- **Server-Seitige OPC-UA-Funktionen** (Method-Calls,
  HistoricalAccess, Events, Alarms) — M4-04 deckt nur die DoD-
  pflichtigen Read/Write/Subscribe-Operationen + StatusCode-Mapping.
- **Multi-Server / Endpoint-Failover** — heutiges Modell ist
  Single-Endpoint pro Asset.
- **OPC-UA-Type-System-Marshalling jenseits der Mapping-DataTypes** —
  Mapping-Schema enthält `bool`/`int*`/`uint*`/`float`/`double`/
  `string`; Strukturen, Arrays oder enum-Encodings sind eigene
  Folgearbeit.

---

## 4. Sub-Slices

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ⬜ | RM-M4-04-A | Adapter-Projekt + `IOpcUaClient`-Port + `FakeOpcUaClient` + `OpcUaAdapterOptions` + StatusCode-Mapper-Primitiv — **~500-700 LOC** | Neues Projekt `src/adapters/driven/BatteryEms.Adapters.OpcUa/` mit `BatteryEms.Adapters.OpcUa.csproj` (PackageReference auf das gewählte OPC-UA-NuGet, ProjectReferences auf Application + Domain analog zu Modbus/Mqtt). `AssemblyMarker.cs` als Trockenlink für Architektur-Tests. `Directory.Packages.props` bekommt den Version-Pin (siehe D-01). Driven Port `IOpcUaClient` mit den DoD-pflichtigen Operationen: `Task ConnectAsync(CancellationToken)`, `Task DisconnectAsync(CancellationToken)`, `Task<OpcUaReadResult> ReadAsync(string nodeId, CancellationToken)`, `Task<OpcUaWriteResult> WriteAsync(string nodeId, object value, OpcUaDataType dataType, CancellationToken)`, `Task<IOpcUaSubscription> CreateSubscriptionAsync(int monitoringIntervalMs, CancellationToken)`. `IOpcUaSubscription` exponiert `AddMonitoredItem(string nodeId, OpcUaDataType dataType)` und `IAsyncEnumerable<OpcUaNotification> NotificationsAsync(CancellationToken)`. **`OpcUaReadResult`/`OpcUaWriteResult`/`OpcUaNotification`** als Records mit `NodeId`, `Value` (object?, type-discriminated), `StatusCode` (uint32 — die OPC-UA-Wire-Repräsentation, siehe D-06), `SourceTimestamp`. **`FakeOpcUaClient`** für Tests: in-memory Knoten-Map, scriptable StatusCodes, fakeable Subscription-Notifications-Stream. **`OpcUaAdapterOptions`** mit `EndpointUrl` (required), `SessionName` (Default `"bess-ems"`), `ReadTimeout=TimeSpan.FromSeconds(5)`, `ConnectTimeout=TimeSpan.FromSeconds(15)`, `KeepAliveInterval=TimeSpan.FromSeconds(10)`, `ReconnectBackoffStart=TimeSpan.FromSeconds(1)`, `ReconnectBackoffMax=TimeSpan.FromSeconds(30)`, `EnsureValid()`-Pattern. **`OpcUaStatusCodeMapper`** als pure static: `Map(uint statusCode) → DataQuality` — gut/uncertain/bad-Klassen aus dem OPC-UA-StatusCode-Top-Bit-Schema; Bad → `DataQuality.ProtocolError($"opcua-bad-{statusCodeName}")`, Uncertain → `DataQuality.Stale($"opcua-uncertain-{statusCodeName}")`, Good → `DataQuality.Valid` (siehe D-06). Tests (Adapters.OpcUa.Tests, neues Test-Projekt): IOpcUaClient-Konstruktor-Guards, FakeOpcUaClient-Roundtrip (Read/Write/Subscribe), Options-Validation (Defaults-Pin auf `EndpointUrl`-Required + Timeout-Plausibility), StatusCode-Mapping pro OPC-UA-Severity-Klasse (Good/Bad/Uncertain mit konkreten Codes wie `Bad_NotConnected=0x80AB0000`, `Uncertain_LastUsableValue=0x40A40000`). |
| ⬜ | RM-M4-04-B | `OpcUaTelemetrySource` (Read + Subscribe + StatusCode + IAsyncEnumerable) — **~500-700 LOC** | `OpcUaTelemetrySource` implementiert `IBatteryTelemetrySource`. Konstruktor: `(IOpcUaClient, OpcUaMappingConfiguration, OpcUaAdapterOptions, IClock, ILogger)`. Beim ersten `ReadAsync(ct)`-Aufruf: `ConnectAsync` (mit Reconnect-Backoff bei Failure), Subscriptions für alle Mapping-Knoten mit `direction=subscribe` anlegen, `MonitoringIntervalMs` aus dem Mapping pro Knoten verwenden. **Read-Pfad** (`direction=read`): per Tick die Lese-Knoten samplen, Werte über `ScaleFactor` skalieren, in eine `BatteryTelemetry`-Domain-Instanz aggregieren. **Subscribe-Pfad** (`direction=subscribe`): Notifications aus `IOpcUaSubscription.NotificationsAsync` lesen und per `Channel<BatteryTelemetry>` an `ReadAsync` weiterreichen (D-03). **DataQuality-Aggregation**: das schlechteste StatusCode pro Telemetry-Sample dominiert die DataQuality des emittierten `BatteryTelemetry`-Eintrags (LH-OPCUA-004 — Pin: ein einzelner `Bad`-Knoten setzt das gesamte Sample auf `ProtocolError`, ein `Uncertain` ohne `Bad` setzt auf `Stale`, alle `Good` ⇒ `DataQuality.Valid`). **`AdapterStatus`** wird pro Connect/Disconnect aktualisiert; `Status.Connected` = aktive Session und Subscription am Ziel. Reconnect-Schleife auf transienten Fehlern mit exponentiellem Backoff bis `ReconnectBackoffMax`; cancellation cooperative. **Domain-Mapping-Helper** `OpcUaTelemetryAssembler` extrahiert die Soc/Soh/Power-Felder aus den verfügbaren Mappings und füllt `BatteryTelemetry` (analog zur `ModbusTelemetryAssembler`-Linie). Tests (Adapters.OpcUa.Tests): Read-Sample-Pin (gemappte Werte → BatteryTelemetry-Felder), Subscribe-Notification-Pin (Push aus FakeSubscription → ReadAsync emittiert), StatusCode-Aggregation-Pin (worst-of), ScaleFactor-Pin, Reconnect-Backoff-Pin (zwei aufeinanderfolgende ConnectAsync-Failures dann Success), Cancellation-aborts-Read, fehlende Mapping-Pflichtfelder ⇒ Konstruktor-Throw, Konstruktor-Null-Args. |
| ⬜ | RM-M4-04-C | `OpcUaCommandSink` + DI + Composition-Root + Bootstrap-Loader-Wiring — **~400-600 LOC** | `OpcUaCommandSink` implementiert `IBatteryCommandSink`. Konstruktor: `(IOpcUaClient, OpcUaMappingConfiguration, BatteryAsset, OpcUaAdapterOptions, IClock, ILogger)`. `WriteAsync(BatteryCommand, ct)` schlägt den ActivePower-Setpoint-Knoten (und ggf. ReactivePower) im Mapping nach (Knoten mit `direction=write` + `writable=true`), wendet den (umgekehrten) `ScaleFactor` an, ruft `IOpcUaClient.WriteAsync`, mappt das Ergebnis: Good-StatusCode ⇒ `CommandDispatchResult.Ok(...)`; Bad-StatusCode ⇒ `CommandDispatchResult.Failure($"opcua-write-bad-{statusCodeName}")`; Mismatch (Knoten nicht writable / nicht im Mapping) ⇒ `CommandDispatchResult.Failure("opcua-mapping-not-writable")`. **`AdapterWriteLimiter`-Pfad**: das Setpoint-Clamping bleibt vor dem Sink (`ConstraintLimiter`/`AdapterWriteLimiter` aus M2/M3); der Sink schreibt, was er bekommt. **`OpcUaRegistration.AddBessOpcUa(...)`** in `Adapters.OpcUa/OpcUaRegistration.cs` analog zu `AddBessModbus`/`AddBessMqtt`: registriert `OpcUaMappingConfiguration` + `OpcUaAdapterOptions` + `IOpcUaClient` (Production: `OpcUaClient`, Test: caller injects) + `IBatteryTelemetrySource` + `IBatteryCommandSink`. **`BessHostOptions`** erhält `OpcUaMappingPath`, `OpcUaEndpointUrl`, `OpcUaSessionName?`. **`BessConfigurationBootstrap`** lädt das Mapping über `JsonFileConfigurationLoader.LoadOpcUaMapping(path)` (RM-M4-07-Pfad). **`BessHostBuilder`** bekommt eine OPC-UA-Branch nach Modbus/vor MQTT (Reihenfolge ist Operator-Wahl): wenn `runtimeConfig.OpcUaMapping is not null` und `EndpointUrl` gesetzt sind, wird `AddBessOpcUa(...)` registriert; sonst falls Modbus/MQTT/NoOp wie heute. Tests (Adapters.OpcUa.Tests): Sink-Write-Pin (Setpoint-Mapping + ScaleFactor + Good-StatusCode → Ok), Bad-StatusCode → Failure mit kebab-Reason, Knoten-nicht-writable → Failure, Mapping-nicht-vorhanden → Failure, Konstruktor-Null-Args. Tests (Host.Tests / Worker.Tests bei Bedarf): Composition-Root-Branch wählt OPC-UA wenn konfiguriert; sonst NoOp-Pin. |
| ⬜ | RM-M4-04-D | HIL-Integration gegen OPC-UA-Simulator + End-to-End-Roundtrip — **~300-500 LOC** (Swing-Item, siehe §7) | Neues Test-Projekt `tests/integration/BatteryEms.OpcUa.IntegrationTests/` analog zur Modbus/Mqtt-Integration-Linie (oder Carve-out in `BatteryEms.Hil.IntegrationTests` — siehe D-07). Setup gegen einen OPC-UA-Simulator: entweder `OPCFoundation.NetStandard.Opc.Ua.Server`-Embedded-TestServer im Test-Prozess (kein zusätzliches Compose-Asset; D-07 Wahl a) oder ein Sidecar-Container im `tests/integration/docker-compose.yml` (D-07 Wahl b). Pinned Tests: **End-to-End-Read** — Simulator emittiert SOC/Power/Temp-Werte, `OpcUaTelemetrySource.ReadAsync` produziert `BatteryTelemetry` mit DataQuality.Valid und korrekten Zahlen. **End-to-End-Subscribe** — Simulator ändert einen Subscribe-Knoten, der Telemetry-Stream emittiert die neue Probe innerhalb `MonitoringIntervalMs * 2`. **End-to-End-Write** — `OpcUaCommandSink.WriteAsync` schreibt den Setpoint, der Simulator zeigt den geschriebenen Wert (Roundtrip-Verifikation). **End-to-End-StatusCode** — Simulator markiert einen Knoten mit Bad-StatusCode (z. B. via Override-Hook), das emittierte Sample trägt `DataQuality.ProtocolError(...)`. **End-to-End-Reconnect** — Server abreissen + neu starten, Adapter reconnected, Stream läuft weiter. **`make test-hil-opcua`**-Target (oder `make test-integration`-Erweiterung) führt das Projekt aus. |

---

## 5. Design-Entscheidungen

**D-01 OPC-UA-NuGet-Wahl: OPC Foundation Reference Stack
(`OPCFoundation.NetStandard.Opc.Ua`).** Trade-off:

- (a) **OPC Foundation Reference Stack** (`OPCFoundation.NetStandard.Opc.Ua`) — Apache 2.0 Code mit RCL-Klausel
  („Reciprocal Community License") für Nicht-OPC-Foundation-Mitglieder
  bei kommerziellem Einsatz. **Voll feature-konform** mit allen
  OPC-UA-Spec-Profilen, ist die kanonische Referenz, von TSO/EVU/
  Vendor-Spec mit hoher Wahrscheinlichkeit erwartet.
- (b) **`Workstation.UaClient`** — MIT, leichter, third-party
  community-stack. Deckt Read/Write/Subscribe + StatusCode, fehlt
  aber bei einigen Sicherheits-/Profil-Features die für RM-M4-05
  relevant werden könnten (z. B. selbst-signierte Zertifikatsketten,
  bestimmte SecurityPolicies).
- (c) **Kommerzielle SDKs** (Unified Automation, Prosys, ascolab) —
  für M4-04 overkill, separate Lizenz-Diskussion.

**Wahl: (a) OPC Foundation Reference Stack** — RCL-Klausel ist für
diesen Adapter unkritisch, weil das BESS-EMS-Repository nicht den
OPC-UA-Stack selbst weiterverteilt (Adapter-Code dependiert nur).
Voll-Feature-Kompatibilität wiegt schwerer als die ~5x grössere
Binär-Footprint im Vergleich zu (b). Falls die RCL-Klausel später
zum Lizenzkonflikt wird, ist Wechsel auf (b) eine F-Folgearbeit;
der `IOpcUaClient`-Port (D-02) hält den Wechsel-Aufwand klein.

**D-02 `IOpcUaClient`-Port um den SDK herum.** Doppelte Schicht
(SDK-Stack → Port-Wrapper → Adapter-Logik) statt direktem SDK-Aufruf
in `OpcUaTelemetrySource`/`OpcUaCommandSink`. Begründung: Tests gegen
`FakeOpcUaClient` ohne echten Server, klare Lizenz-/Wechsel-Grenze
zum SDK (D-01), und Konsistenz mit dem etablierten Modbus
(`IModbusClient`)/Mqtt (`IMqttClient`)-Pattern. Kosten: ~150 LOC
zusätzlicher Wrapper-Code; der zugehörige Test-Coverage-Gewinn ist
deutlich grösser. Tradeoff akzeptiert.

**D-03 Subscription-Update-Stream über `Channel<BatteryTelemetry>`.**
Optionen:

- (a) **`System.Threading.Channels.Channel<BatteryTelemetry>`** —
  bounded, mit Backpressure-Policy. Producer (Subscription-Notification-
  Handler) `WriteAsync`, Consumer (`OpcUaTelemetrySource.ReadAsync`)
  `ReadAllAsync`. Lock-frei, async-nativ, Standard-BCL.
- (b) `BlockingCollection<BatteryTelemetry>` — synchron, älter, nicht
  async-friendly.
- (c) `Subject<BatteryTelemetry>` aus Reactive-Extensions — würde
  zusätzliche NuGet-Dependency einführen.

**Wahl: (a)**. Bounded Channel mit `BoundedChannelFullMode.DropOldest`
für Subscription-Drift unter Last; Read-Pfad ist Pull-Tick, nicht
betroffen. Channel-Capacity per Options konfigurierbar (Default 256).

**D-04 Security-Default für M4-04 ist `SecurityMode=None`.** Master-
Plan-Trennung M4-04 (Connectivity) vs. M4-05 (Security). M4-04
liefert einen funktionsfähigen Adapter gegen den Simulator mit
unverschlüsselter Verbindung; die Konfig-Validierung emittiert eine
strukturierte Warnung („opcua-security-not-hardened"); der Adapter
ist **nicht produktiv freigegeben** bis M4-05 zündet — analog zur
M4-06-`MqttNetClient`-Linie und D-04 dort. M4-04-A's
`OpcUaAdapterOptions` enthält bereits `SecurityMode`/
`SecurityPolicy`-Properties (Default `None`/`""`), damit M4-05 die
Härtung ohne Options-Schema-Bruch dranhängen kann.

**D-05 OPC-UA-Activation-Source nicht in M4-04.** RM-M4-03 D-06
hat den `IRegelleistungActivationUseCase`-Driving-Port als
Eingangspunkt fixiert; ein OPC-UA-Subscribe-Adapter, der TSO-
Aktivierungs-Signale auf den Use-Case hebelt, wäre **eigener Slice**.
Das M4-04-DoD listet ihn nicht, und das Aktivierungs-Subscription-
Mapping würde ein separates Mapping-Schema (oder eine Erweiterung
des heutigen `OpcUaNodeMapping.Direction`-Enum um
`activation-subscribe`) verlangen. F-09 in den M4-Followups deckt
das ab; M4-04-D pinnt explizit, dass Activation-Source-Subscriptions
**nicht** bedient werden, und der Health-Endpoint
`/health/regelleistung` aus M4-03 zeigt weiter `last_activation: null`,
solange F-09 nicht zündet.

**D-06 StatusCode → DataQuality-Mapping nutzt das OPC-UA-Severity-
Top-Bit-Schema.** OPC-UA-StatusCodes sind 32-bit Integers; die
oberen zwei Bits kodieren die Severity:
- `0x00xxxxxx` ⇒ Good
- `0x40xxxxxx` ⇒ Uncertain
- `0x80xxxxxx` ⇒ Bad

Mapping:
- Good ⇒ `DataQuality.Valid`
- Uncertain ⇒ `DataQuality.Stale($"opcua-uncertain-{name}")`
- Bad ⇒ `DataQuality.ProtocolError($"opcua-bad-{name}")`

Der `name`-Suffix kommt aus einer kleinen statischen Lookup-Map für
die häufigsten Codes (`BadNotConnected`, `BadCommunicationError`,
`BadTimeout`, `BadInternalError`, `UncertainLastUsableValue`,
`UncertainSensorNotAccurate`, `UncertainSubNormal`, …); unbekannte
Codes ⇒ Hex-Suffix (`opcua-bad-0xab123456`). Pin-Test sichert, dass
neue Codes nicht silent unter den Tisch fallen.

**D-07 HIL-Setup für RM-M4-04-D.** Optionen:

- (a) **Embedded `OPCFoundation.NetStandard.Opc.Ua.Server`** im
  Test-Prozess. Vorteile: kein zusätzliches Compose-Asset, schneller
  Test-Run, Test-Override-Hook für Bad-StatusCodes über die Server-
  API. Nachteile: höherer Test-Speicher-Footprint, Server-Stack ist
  Teil des Test-DLLs.
- (b) **Dockerized OPC-UA-Simulator-Sidecar** (z. B. eine bestehende
  Free-OPC-UA- oder open62541-basierte Container-Image). Vorteile:
  externes Asset, real-istischer. Nachteile: zusätzliches Compose-
  Service, langsamerer Startup, fragiler bei CI-Runner-Wechsel.

**Wahl: (a) Embedded TestServer.** Begründung: Modbus-Integration
fährt heute mit `pymodbus`-Sidecar (b-Stil), aber das Modbus-Compose
wurde von Anfang an aufgesetzt; OPC-UA bekommt mit (a) den schnellsten
Test-Run und vermeidet, eine vendor-spezifische Container-Image als
neue CI-Abhängigkeit zu verdrahten. F-Folgearbeit wenn Vendor-Compat-
Profile-Tests einen externen realen Server brauchen.

**D-08 ScaleFactor wird in Telemetry-Source applied.** Mapping
trägt `ScaleFactor` (Default 1.0); der Telemetry-Source multipliziert
den gelesenen Wert vor der Domain-Aufbereitung; der Command-Sink
**dividiert** vor dem Schreiben (Round-Trip-Erhaltung). Pin in beiden
Sub-Slices.

---

## 6. Akzeptanzkriterien

- `make gates` und `make test-integration` bleiben grün; ein neuer
  `make test-hil-opcua`-Target (oder integriert) ist grün.
- **LH-OPCUA-001 Lesen**: Read-Sample produziert `BatteryTelemetry`
  mit gemappten Feldern; ScaleFactor wird applied.
- **LH-OPCUA-002 Schreiben**: `OpcUaCommandSink.WriteAsync` schreibt
  den ActivePower-Setpoint-Knoten (Roundtrip-verifiziert in M4-04-D);
  Bad-StatusCode → typisierter Failure-Reason.
- **LH-OPCUA-003 Subscriptions**: ein Subscribe-Knoten mit
  `MonitoringIntervalMs=1000` emittiert Updates innerhalb 2 Sekunden
  nach Server-seitiger Wertänderung.
- **LH-OPCUA-004 StatusCode**: ein einzelner Bad-Knoten setzt das
  Sample auf `DataQuality.ProtocolError("opcua-bad-...")`; Uncertain
  setzt auf `DataQuality.Stale("opcua-uncertain-...")`.
- **Composition-Root-Branch**: bei gesetzter `OpcUaMappingPath` +
  `OpcUaEndpointUrl` registriert `BessHostBuilder` `OpcUaTelemetrySource`
  + `OpcUaCommandSink`; sonst Modbus/MQTT/NoOp wie heute.
- **Architektur-Tabu-Test**: `BatteryEms.Adapters.OpcUa` darf
  `Opc.Ua.*` referenzieren; Domain + Application bleiben sauber.
- **Adapter-Modul-Trennung**: `OpcUaTelemetrySource`/`OpcUaCommandSink`
  schreiben **nicht** ohne Setpoint-Clamping (`AdapterWriteLimiter`-
  Pfad bleibt vorgeschaltet).

---

## 7. Risiken und Tradeoffs

- **NuGet-Lizenz-Risiko (D-01).** OPC Foundation RCL ist für reine
  Adapter-Nutzung (kein Re-Distribution des Stacks selbst)
  unproblematisch, sollte aber im Lizenz-Audit dokumentiert sein.
  Falls eine zukünftige Compliance-Linie den Stack ausschliesst,
  ist Wechsel auf (b) per `IOpcUaClient`-Port-Vertrag geringe
  Migration (~200 LOC im OpcUaClient).
- **Embedded-TestServer-Footprint (D-07).** OPC-UA-Server-Stack als
  Test-DLL-Dependency ist ~5-10 MB extra im Test-Image. CI-Run-Time
  bleibt unter 30 s pro HIL-Test (analog zu Modbus heute). Wenn der
  Speicher- oder Image-Druck auf CI-Runnern problematisch wird, ist
  Wechsel auf (b) Dockerized-Simulator separater Slice.
- **Subscribe-Backpressure (D-03).** Bei hoher Notification-Rate
  könnten ältere Samples gedropt werden (`DropOldest`). Realistic
  delta: bei 1Hz Subscriptions vernachlässigbar. Pin-Test
  dokumentiert das Verhalten unter Last.
- **Sub-Slice-D-Swing-Risiko.** HIL-Integration ist erfahrungsgemäss
  aufwendig (SetupTimeouts, Server-Restart-Stabilität); ~300 LOC
  Untergrenze, ~500 LOC Obergrenze realistisch. Falls D-Slice über
  500 LOC ausläuft, ist Carve-out eines Reconnect-Test-Sub-Slices
  D-Reconnect zulässig.
- **Sub-Slice-C-Test-Rebasing.** `BessHostBuilder` und
  `BessConfigurationBootstrap` werden um eine OPC-UA-Branch
  erweitert. Modbus/MQTT-Hostbuilder-Tests bleiben unverändert
  (additiv). `BessHostOptions` bekommt drei neue Properties; ein
  Operator mit ausschliesslich Modbus-Konfiguration sieht keinen
  Verhaltens-Unterschied (Default-Pfad bleibt Modbus/Mqtt/NoOp).
- **Mapping-Schema-Drift.** RM-M4-07 hat das v1-Schema fixiert; M4-04
  ist erster realer Konsument der nicht-Aktivierungs-Felder. Wenn
  M4-04-Implementation Felder findet, die v1 nicht abdeckt (z. B.
  Per-Knoten-Timeouts, Per-Knoten-Auth-Override jenseits des heutigen
  `AuthRequired`-Strings), zündet F-07 (Mapping-Migration v1→v2-
  Template-Slice) — wahrscheinlich erst bei M4-05.
- **OPC-UA-Type-System-Lücken.** Das v1-Mapping deckt skalare
  Datentypen (`bool`/`int*`/`uint*`/`float`/`double`/`string`).
  Strukturen, Arrays und Enums sind **nicht** im Scope von M4-04;
  Mapping-Knoten mit unbekanntem `data_type` (auf Loader-Ebene
  geblockt) erreichen den Adapter nie, aber der Server kann ein
  Sample mit unerwartetem OPC-UA-Type liefern (Pin-Test:
  `DataQuality.ProtocolError("opcua-type-mismatch")`).

---

## 8. Sequenz

1. **RM-M4-04-A** (Adapter-Projekt + Port + Fake + Options + StatusCode-
   Mapper) zuerst — keine externen Abhängigkeiten ausser dem NuGet-Pin
   in `Directory.Packages.props`. Bricht den Build nicht (rein
   additiv); Architektur-Tabu-Test verifiziert.
2. **RM-M4-04-B** (Telemetry-Source mit Read+Subscribe+StatusCode-
   Aggregation) — konsumiert A. Channel-basierter Subscribe-Stream
   ist die anspruchsvollste Mechanik des Slices.
3. **RM-M4-04-C** (Command-Sink + DI + Composition-Root +
   Bootstrap-Loader-Wiring) — die produktive Verdrahtung. Hier zündet
   die Composition-Root-Branch.
4. **RM-M4-04-D** (HIL-Integration gegen Embedded-TestServer) — die
   End-zu-End-Pins. Greift in alle Schichten ein, deshalb am Ende.

Jeder Sub-Slice schliesst mit einem eigenen Commit und einem
Review-Pass (analog zum etablierten Pattern bei RM-M4-01/03/06/07).
Bei Slice-Closure markiert der letzte Commit die RM-M4-04-Zeile in
`plan-RM-M4.md` als ✅ und ergänzt eine Implementierungs-Zusammenfassung;
der Slice-Plan wandert von `in-progress/` nach `done/`.

---

## 9. Folgearbeiten (gehen in `note-RM-M4-followups.md`)

Die folgenden Items werden bei diesem Slice nicht implementiert,
gehen aber als F-Items in die Trigger-Watch-Notiz:

- **F-13 OPC-UA-Activation-Subscribe** — Trigger: TSO-/Vendor-Spec
  liefert ein OPC-UA-basiertes Aktivierungs-Subscription-Profil.
  Scope: Erweiterung des Mapping-Schemas (v1→v2 via F-07-Pattern) um
  `activation-subscribe`-Direction; neuer Adapter, der die Notifications
  auf `IRegelleistungActivationUseCase.ReceiveAsync` hebelt. Aufwand
  grob 1-2 Wochen, eigener Slice. Verschiebt `last_activation: null`
  auf produktive Werte im `/health/regelleistung`-Endpoint.
- **F-14 OPC-UA-Multi-Server / Endpoint-Failover** — Trigger:
  Operator-/Compliance-Anforderung an redundante OPC-UA-Endpunkte.
  Heute: Single-Endpoint pro Asset.
- **F-15 OPC-UA-Method-Calls / HistoricalAccess / Events** — Trigger:
  TSO-/Vendor-Profil verlangt zusätzliche OPC-UA-Funktionen jenseits
  Read/Write/Subscribe.
- **F-16 OPC-UA-Type-System-Erweiterung** (Strukturen, Arrays, Enums) —
  Trigger: Mapping-Anforderung verlangt Daten jenseits skalarer
  Typen. Verbindet sich mit F-07 (Mapping-Migration v1→v2).
- **F-17 Dockerized OPC-UA-Simulator-Sidecar** — Trigger:
  Vendor-Compat-Profil-Tests verlangen einen realen externen Server
  statt des Embedded-TestServers (D-07 Wahl b).
