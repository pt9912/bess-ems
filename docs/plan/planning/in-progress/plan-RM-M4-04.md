# Plan RM-M4-04 — OPC-UA-Adapter (Lesen, Schreiben, Subscriptions, StatusCode)

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M4-04)
**Status:** Offen — wird in Sub-Slices RM-M4-04-A..D umgesetzt
**Bezug:**
[`plan-RM-M4.md`](plan-RM-M4.md) (Master-Plan, RM-M4-04-Zeile mit DoD und LH-Bezug),
[`../done/plan-RM-M4-03.md`](../done/plan-RM-M4-03.md) (RM-M4-03 D-06 + F-09: Driving-Port-Form für Aktivierungs-Source — OPC-UA-Activation-Subscription bleibt Folgearbeit, nicht in M4-04),
[`../open/note-RM-M4-followups.md`](../open/note-RM-M4-followups.md) (F-07 OPC-UA-Mapping-Migration v1→v2 als Template-Slice; F-09 alle Source-Wire-Adapter inkl. eines OPC-UA-Activation-Source-Adapters — M4-04 lehnt das F-09-Carve-out ab und lässt es bei F-09),
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
| Architektur-Tabu für Hexagon | `Opc.Ua` ist in `FrameworkTaboosForHexagon` der Architektur-Tests gelistet; das verhindert Referenzen aus Domain/Application. Im Adapter-Projekt ist `Opc.Ua` schlicht außerhalb des Tabu-Scopes — es gibt keinen positiven Allow-Test, der den Adapter prüft, und es ist auch keiner gefordert | ✅ |
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
  Signale auf den Use-Case hebelt, ist **F-09-Folgearbeit** — M4-04
  lehnt das von plan-RM-M4-03 §9 angebotene M4-04-Carve-out ab
  (siehe D-05). Die F-ID bleibt einheitlich F-09 in der M4-Followup-
  Notiz.
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
| ⬜ | RM-M4-04-A | Adapter-Projekt + `IOpcUaClient`-Port + `FakeOpcUaClient` + `OpcUaAdapterOptions` + StatusCode-Mapper-Primitiv — **~500-700 LOC** | Neues Projekt `src/adapters/driven/BatteryEms.Adapters.OpcUa/` mit `BatteryEms.Adapters.OpcUa.csproj` (PackageReference auf das gewählte OPC-UA-NuGet, ProjectReferences auf Application + Domain analog zu Modbus/Mqtt). `AssemblyMarker.cs` als Trockenlink für Architektur-Tests. `Directory.Packages.props` bekommt den Version-Pin (siehe D-01). Driven Port `IOpcUaClient` mit den DoD-pflichtigen Operationen: `Task ConnectAsync(CancellationToken)`, `Task DisconnectAsync(CancellationToken)`, `Task<OpcUaReadResult> ReadAsync(string nodeId, CancellationToken)`, `Task<OpcUaWriteResult> WriteAsync(string nodeId, object value, OpcUaDataType dataType, CancellationToken)`, `Task<IOpcUaSubscription> CreateSubscriptionAsync(int monitoringIntervalMs, CancellationToken)`. `IOpcUaSubscription` exponiert `AddMonitoredItem(string nodeId, OpcUaDataType dataType)` und `IAsyncEnumerable<OpcUaNotification> NotificationsAsync(CancellationToken)`. **`OpcUaReadResult`/`OpcUaWriteResult`/`OpcUaNotification`** als Records mit `NodeId`, `Value` (object?, type-discriminated), `StatusCode` (uint32 — die OPC-UA-Wire-Repräsentation, siehe D-06), `SourceTimestamp`. **`FakeOpcUaClient`** für Tests: in-memory Knoten-Map, scriptable StatusCodes, fakeable Subscription-Notifications-Stream. **`OpcUaAdapterOptions`** mit `EndpointUrl` (required), `SessionName` (Default `"bess-ems"`), `ReadTimeout=TimeSpan.FromSeconds(5)`, `ConnectTimeout=TimeSpan.FromSeconds(15)`, `KeepAliveInterval=TimeSpan.FromSeconds(10)`, `ReconnectBackoffStart=TimeSpan.FromSeconds(1)`, `ReconnectBackoffMax=TimeSpan.FromSeconds(30)`, `DefaultMonitoringIntervalMs=1000` (Fallback wenn Mapping-Knoten kein per-Knoten-`MonitoringIntervalMs` trägt), `SubscriptionChannelCapacity=256`, plus die Pre-M4-05-Security-Slots `SecurityMode=None`, `SecurityPolicy=""`, `AllowUnsecured=false`, `AllowUnsecuredReason=null` (siehe D-04 — diese Felder existieren in M4-04-A bewusst, damit M4-05 die Härtung ohne Options-Schema-Bruch dranhängen kann), `EnsureValid()`-Pattern. **`OpcUaStatusCodeMapper`** als pure static: `Map(uint statusCode) → DataQuality` — gut/uncertain/bad-Klassen aus dem OPC-UA-StatusCode-Top-Bit-Schema; Bad → `DataQuality.ProtocolError($"opcua-bad-{statusCodeName}")`, Uncertain → `DataQuality.Stale($"opcua-uncertain-{statusCodeName}")`, Good → `DataQuality.Valid` (siehe D-06). Tests (Adapters.OpcUa.Tests, neues Test-Projekt): IOpcUaClient-Konstruktor-Guards, FakeOpcUaClient-Roundtrip (Read/Write/Subscribe), Options-Validation (Defaults-Pin auf `EndpointUrl`-Required + Timeout-Plausibility), **Security-Startup-Guard (D-04)** — drei Pins: (a) Default-Options (`SecurityMode=None`, `AllowUnsecured=false`) → `EnsureValid()` wirft mit `opcua-security-not-hardened`; (b) `AllowUnsecured=true` + leerer `AllowUnsecuredReason` → wirft mit `opcua-security-not-hardened`; (c) `AllowUnsecured=true` + nicht-leerer `AllowUnsecuredReason` → `EnsureValid()` lässt durch, ILogger-Warning emittiert mit kebab-Reason. StatusCode-Mapping pro OPC-UA-Severity-Klasse (Good/Bad/Uncertain mit konkreten Codes wie `Bad_NotConnected=0x80AB0000`, `Uncertain_LastUsableValue=0x40A40000`). |
| ⬜ | RM-M4-04-B | `OpcUaTelemetrySource` (Read + Subscribe + StatusCode + IAsyncEnumerable) — **~500-700 LOC** | `OpcUaTelemetrySource` implementiert `IBatteryTelemetrySource`. Konstruktor: `(IOpcUaClient, OpcUaMappingConfiguration, OpcUaAdapterOptions, IClock, ILogger)`. Beim ersten `ReadAsync(ct)`-Aufruf: `ConnectAsync` (mit Reconnect-Backoff bei Failure), Subscriptions für alle Mapping-Knoten mit `direction=subscribe` anlegen, `MonitoringIntervalMs` aus dem Mapping pro Knoten verwenden — wenn ein Subscribe-Knoten `MonitoringIntervalMs=null` trägt, fällt der Adapter auf `OpcUaAdapterOptions.DefaultMonitoringIntervalMs` (Default 1000ms) zurück (Pin-Test). **Read-Pfad** (`direction=read`): per Tick die Lese-Knoten samplen, Werte über `ScaleFactor` skalieren, in eine `BatteryTelemetry`-Domain-Instanz aggregieren. **Subscribe-Pfad** (`direction=subscribe`): Notifications aus `IOpcUaSubscription.NotificationsAsync` lesen und per bounded `Channel<BatteryTelemetry>` mit `DropOldest` + non-blocking `TryWrite` aus dem SDK-Callback an `ReadAsync` weiterreichen (D-03). **Sticky Overflow-Flag**: bei Channel-voll-Detection wird `_subscriptionOverflowFlag` gesetzt; solange gesetzt, ist die emittierte `DataQuality` mindestens `Stale("opcua-subscription-overflow")`; Flag wird gelöscht sobald der nächste Read-Tick den Channel leer drained (Pin-Test). **DataQuality-Aggregation**: das schlechteste StatusCode pro Telemetry-Sample dominiert die DataQuality des emittierten `BatteryTelemetry`-Eintrags (LH-OPCUA-004 — Pin: ein einzelner `Bad`-Knoten setzt das gesamte Sample auf `ProtocolError`, ein `Uncertain` ohne `Bad` setzt auf `Stale`, alle `Good` ⇒ `DataQuality.Valid`). **`AdapterStatus`** wird pro Connect/Disconnect aktualisiert; `Status.Connected` = aktive Session und Subscription am Ziel. Reconnect-Schleife auf transienten Fehlern mit exponentiellem Backoff bis `ReconnectBackoffMax`; cancellation cooperative. **Domain-Mapping-Helper** `OpcUaTelemetryAssembler` extrahiert die Soc/Soh/Power-Felder aus den verfügbaren Mappings und füllt `BatteryTelemetry` (analog zur `ModbusTelemetryAssembler`-Linie). Tests (Adapters.OpcUa.Tests): Read-Sample-Pin (gemappte Werte → BatteryTelemetry-Felder), Subscribe-Notification-Pin (Push aus FakeSubscription → ReadAsync emittiert), StatusCode-Aggregation-Pin (worst-of), ScaleFactor-Pin, Reconnect-Backoff-Pin (zwei aufeinanderfolgende ConnectAsync-Failures dann Success), Cancellation-aborts-Read, **Subscription-Overflow-Pin** (Channel über `BoundedChannelFullMode.DropOldest`-Schwelle treiben → `_subscriptionOverflowFlag` gesetzt → emittierte Samples `DataQuality.Stale("opcua-subscription-overflow")` → Channel drainen → nächste Samples wieder Valid), fehlende Mapping-Pflichtfelder ⇒ Konstruktor-Throw, Konstruktor-Null-Args. |
| ⬜ | RM-M4-04-C | `OpcUaCommandSink` + DI + Composition-Root + Bootstrap-Loader-Wiring — **~400-600 LOC** | `OpcUaCommandSink` implementiert `IBatteryCommandSink`. Konstruktor: `(IOpcUaClient, OpcUaMappingConfiguration, BatteryAsset, OpcUaAdapterOptions, IClock, ILogger)`. `WriteAsync(BatteryCommand, ct)` schlägt den ActivePower-Setpoint-Knoten (und ggf. ReactivePower) im Mapping nach (Knoten mit `direction=write` + `writable=true`), wendet den (umgekehrten) `ScaleFactor` an, ruft `IOpcUaClient.WriteAsync`, mappt das Ergebnis: Good-StatusCode ⇒ `CommandDispatchResult.Ok(...)`; Bad-StatusCode ⇒ `CommandDispatchResult.Failure($"opcua-write-bad-{statusCodeName}")`; Mismatch (Knoten nicht writable / nicht im Mapping) ⇒ `CommandDispatchResult.Failure("opcua-mapping-not-writable")`. **`AdapterWriteLimiter`-Pfad**: das Setpoint-Clamping bleibt vor dem Sink (`ConstraintLimiter`/`AdapterWriteLimiter` aus M2/M3); der Sink schreibt, was er bekommt. **`OpcUaRegistration.AddBessOpcUa(...)`** in `Adapters.OpcUa/OpcUaRegistration.cs` analog zu `AddBessModbus`/`AddBessMqtt`: registriert `OpcUaMappingConfiguration` + `OpcUaAdapterOptions` + `IOpcUaClient` (Production: `OpcUaClient`, Test: caller injects) + `IBatteryTelemetrySource` + `IBatteryCommandSink`. **`BessHostOptions`** erhält `OpcUaMappingPath`, `OpcUaEndpointUrl`, `OpcUaSessionName?`. **`BessConfigurationBootstrap`** lädt das Mapping über `JsonFileConfigurationLoader.LoadOpcUaMapping(path)` (RM-M4-07-Pfad). **`BessHostBuilder`** bekommt eine OPC-UA-Branch in den Modbus/MQTT/NoOp-Triage. **Mehrfach-Konfiguration ist fail-closed**: wenn der Operator gleichzeitig Modbus, MQTT und/oder OPC-UA konfiguriert hat (mehr als eine Adapter-Familie mit `MappingPath` + Endpoint gesetzt), wirft der Composition-Root einen Startup-Fehler `multiple-io-adapters-configured` mit der Liste der erkannten Konfigurationen — der Operator muss sich für genau eine Quelle entscheiden. Diese Pin-Linie verhindert silent-override-Bugs durch wechselnde if/else-Reihenfolgen. Ist genau eine Familie konfiguriert, wird sie wie heute (Modbus/MQTT) bzw. neu (OPC-UA) registriert; ist keine konfiguriert, bleibt der NoOp-Pfad. Die Reihenfolge der Branches im Code ist damit semantisch ohne Wirkung (alle exklusiv) und kann der Reviewability dienen (z. B. Modbus → MQTT → OPC-UA). Tests (Adapters.OpcUa.Tests): Sink-Write-Pin (Setpoint-Mapping + ScaleFactor + Good-StatusCode → Ok), Bad-StatusCode → Failure mit kebab-Reason, Knoten-nicht-writable → Failure, Mapping-nicht-vorhanden → Failure, **`ScaleFactor==0` im Mapping → Sink fail-closed** (`opcua-mapping-scale-zero`; das JSON-Schema verbietet `scale_factor=0` bereits am Loader, aber programmatisch konstruierte Mappings — z. B. in Tests, oder via einer zukünftigen non-JSON-Quelle — würden im Sink durch Null teilen; defensive Pin), Konstruktor-Null-Args. Tests (Host.Tests / Worker.Tests bei Bedarf): Composition-Root-Branch wählt OPC-UA wenn konfiguriert; sonst NoOp-Pin. |
| ⬜ | RM-M4-04-D | HIL-Integration gegen OPC-UA-Simulator + End-to-End-Roundtrip — **~300-500 LOC** (Swing-Item, siehe §7) | Neues Test-Projekt `tests/integration/BatteryEms.OpcUa.IntegrationTests/` analog zur Modbus/Mqtt-Integration-Linie (oder Carve-out in `BatteryEms.Hil.IntegrationTests` — siehe D-07). Setup gegen einen OPC-UA-Simulator: entweder `OPCFoundation.NetStandard.Opc.Ua.Server`-Embedded-TestServer im Test-Prozess (kein zusätzliches Compose-Asset; D-07 Wahl a) oder ein Sidecar-Container im `tests/integration/docker-compose.yml` (D-07 Wahl b). Pinned Tests: **End-to-End-Read** — Simulator emittiert SOC/Power/Temp-Werte, `OpcUaTelemetrySource.ReadAsync` produziert `BatteryTelemetry` mit DataQuality.Valid und korrekten Zahlen. **End-to-End-Subscribe** — Simulator ändert einen Subscribe-Knoten, der Telemetry-Stream emittiert die neue Probe innerhalb `MonitoringIntervalMs * 2`. **End-to-End-Write** — `OpcUaCommandSink.WriteAsync` schreibt den Setpoint, der Simulator zeigt den geschriebenen Wert (Roundtrip-Verifikation). **End-to-End-StatusCode** — Simulator markiert einen Knoten mit Bad-StatusCode (z. B. via Override-Hook), das emittierte Sample trägt `DataQuality.ProtocolError(...)`. **End-to-End-Reconnect** — Server abreissen + neu starten, Adapter reconnected, Stream läuft weiter. **`make test-hil-opcua`**-Target (oder `make test-integration`-Erweiterung) führt das Projekt aus. |

---

## 5. Design-Entscheidungen

**D-01 OPC-UA-NuGet-Wahl: OPC Foundation Reference Stack
(`OPCFoundation.NetStandard.Opc.Ua`).** Trade-off:

- (a) **OPC Foundation Reference Stack** (`OPCFoundation.NetStandard.Opc.Ua`) —
  Quell-Code GPL-2.0 mit RCL-Ausnahme für OPC-Foundation-Mitglieder;
  Binär-Distribution über NuGet unter dem **OPC Foundation
  Redistributables Agreement v1.3**. **Voll feature-konform** mit
  allen OPC-UA-Spec-Profilen, ist die kanonische Referenz, von TSO/
  EVU/Vendor-Spec mit hoher Wahrscheinlichkeit erwartet.
- (b) **`Workstation.UaClient`** — MIT, leichter, third-party
  community-stack. Deckt Read/Write/Subscribe + StatusCode, fehlt
  aber bei einigen Sicherheits-/Profil-Features die für RM-M4-05
  relevant werden könnten (z. B. selbst-signierte Zertifikatsketten,
  bestimmte SecurityPolicies).
- (c) **Kommerzielle SDKs** (Unified Automation, Prosys, ascolab) —
  für M4-04 overkill, separate Lizenz-Diskussion.

**Wahl: (a) OPC Foundation Reference Stack.** Reine Adapter-Nutzung
ohne Re-Distribution des Stacks selbst ist mit der Redistributables-
Agreement-Linie unproblematisch (Adapter-Code dependiert nur), aber
sie gehört vor M4-04-A's Commit ins Lizenz-Audit-Register. Voll-
Feature-Kompatibilität wiegt schwerer als die ~5x grössere Binär-
Footprint im Vergleich zu (b). Falls die Lizenz-Linie später zum
Compliance-Konflikt wird, ist Wechsel auf (b) eine F-Folgearbeit;
der `IOpcUaClient`-Port (D-02) hält den Wechsel-Aufwand klein
(~200 LOC im Production-`OpcUaClient`).

**D-02 `IOpcUaClient`-Port um den SDK herum.** Doppelte Schicht
(SDK-Stack → Port-Wrapper → Adapter-Logik) statt direktem SDK-Aufruf
in `OpcUaTelemetrySource`/`OpcUaCommandSink`. Begründung: Tests gegen
`FakeOpcUaClient` ohne echten Server, klare Lizenz-/Wechsel-Grenze
zum SDK (D-01), und Konsistenz mit dem etablierten Modbus
(`IModbusClient`)/Mqtt (`IMqttClient`)-Pattern. Kosten: ~150 LOC
zusätzlicher Wrapper-Code; der zugehörige Test-Coverage-Gewinn ist
deutlich grösser. Tradeoff akzeptiert.

**D-03 Subscription-Update-Stream über bounded
`Channel<BatteryTelemetry>` mit `DropOldest` + non-blocking
`TryWrite` aus dem SDK-Callback + sticky Overflow-Flag.** Optionen
zum Kanal-Backbone:

- (a) **`System.Threading.Channels.Channel<BatteryTelemetry>`** —
  bounded, mit konfigurierbarer FullMode-Policy. Producer
  (Subscription-Notification-Handler) `TryWrite` (synchron, lock-frei),
  Consumer (`OpcUaTelemetrySource.ReadAsync`) `ReadAllAsync`.
  Standard-BCL.
- (b) `BlockingCollection<BatteryTelemetry>` — synchron, älter, nicht
  async-friendly.
- (c) `Subject<BatteryTelemetry>` aus Reactive-Extensions — würde
  zusätzliche NuGet-Dependency einführen.

**Wahl: (a)** mit der konkreten Backpressure-Strategie:

1. **Channel-Konfiguration:** bounded mit
   `BoundedChannelFullMode.DropOldest` und Default-Capacity 256
   (per Options konfigurierbar).
2. **Producer-Pfad (SDK-Notification-Callback)** ist
   **non-blocking**: der Callback ruft `Channel.Writer.TryWrite(...)`
   und kehrt sofort zurück. Damit kann der OPC-Foundation-SDK-
   Dispatcher-Thread niemals durch unsere Backpressure blockieren —
   genau die Falle, die ein `Wait`-Mode beim Callback-Pfad
   erzeugen würde.
3. **Drop-Detection + sticky Overflow-Flag:**
   `BoundedChannelFullMode.DropOldest` lässt `TryWrite` immer
   `true` zurück (alter Eintrag wird gedropt). Um den Drop trotzdem
   zu detektieren, hält der Source einen Counter
   `_droppedNotificationCount` und ein paralleles
   `_subscriptionOverflowFlag`: wenn der Channel beim
   `TryWrite`-Zeitpunkt **voll war** (Implementierung über einen
   simplen pre-check `Reader.Count >= Capacity` oder über einen
   separaten Wrapper, der den Drop-Event surfaced), wird der Flag
   gesetzt. **Sub-Slice-B-Aggregation** liest den Flag: solange
   gesetzt, ist die `DataQuality` jedes emittierten
   `BatteryTelemetry`-Samples **mindestens** `Stale("opcua-
   subscription-overflow")` (Pin-Test). Der Flag wird gelöscht,
   sobald der Channel beim nächsten `ReadAsync`-Cycle vollständig
   gedrained ist.
4. **Optional: Background-Drainer** falls der Sub-Slice-B-Code-Pfad
   einfacher wird, kann ein dedizierter Background-Task den SDK-
   Notification-Dispatch (Producer) vom Aggregations-Tick
   (Consumer) entkoppeln — der Reviewer hatte das vorgeschlagen.
   Sub-Slice-B-Implementation entscheidet darüber bei der konkreten
   Codestruktur; Default ohne Background-Task ist akzeptabel weil
   der Callback ohnehin nur `TryWrite` macht.

Damit greifen beide Sicherheits-Eigenschaften: (i) der SDK-Thread
**blockiert nie** auf Backpressure (non-blocking TryWrite); (ii)
ein verlorenes `Bad`-StatusCode-Sample wird **nicht silent
geschluckt**, sondern degradiert die nächsten emittierten Samples
auf `DataQuality.Stale("opcua-subscription-overflow")` bis der
Drainer aufholt. Das deckt sowohl die first-pass-Review-Kritik an
`DropOldest` (Bad-Sample-silent-loss) als auch die external-pass-
Review-Kritik an `Wait` (Callback-Thread-Blockade) ab.

**D-04 Security-Default für M4-04 ist `SecurityMode=None` mit
hartem `AllowUnsecured`-Startup-Guard.** Master-Plan-Trennung M4-04
(Connectivity) vs. M4-05 (Security). M4-04 liefert einen
funktionsfähigen Adapter gegen den Simulator mit unverschlüsselter
Verbindung — aber **nicht silent**: die Master-Plan-RM-M4-05-Zeile
pinnt das Pattern „`SecurityMode=None` braucht explizites
`AllowUnsecured=true` plus nicht-leeren `AllowUnsecuredReason`,
emittiert eine strukturierte Warnung und ist bei `RuntimeProfile=
Production` ein Startup-Fehler." M4-04-A implementiert davon den
**bool-Layer**: `OpcUaAdapterOptions.EnsureValid()` schlägt fehl
mit `opcua-security-not-hardened` wenn `SecurityMode == None` und
`AllowUnsecured == false`, ODER wenn `AllowUnsecured == true` aber
`AllowUnsecuredReason` leer/null ist. Bei akzeptiertem
`AllowUnsecured=true` + nicht-leerem `AllowUnsecuredReason` startet
der Adapter mit einer strukturierten ILogger-Warnung
(LoggerMessage-EventId, kebab-case-Reason). M4-05 layert die
**RuntimeProfile-Awareness** drauf (Production-Profil ⇒ auch
`AllowUnsecured=true` reicht nicht), ohne den Options-Shape zu
brechen.

Das ist der **non-blocking-warning vs. blocker**-Split, den ein
Reviewer mit Recht klar sehen möchte: M4-04 ist **blocker auf der
bool-Achse** (kein Startup ohne `AllowUnsecured=true`), nur warning
auf der RuntimeProfile-Achse (M4-05 dreht das in einen weiteren
Blocker um, sobald RuntimeProfile materialisiert ist). Akzeptanz-
Kriterien §6 + Sub-Slice-A-Tests pinnen den Startup-Failure
explizit.

M4-04-A's `OpcUaAdapterOptions` enthält damit `SecurityMode=None`,
`SecurityPolicy=""`, `AllowUnsecured=false`, `AllowUnsecuredReason=null`
als Defaults — Operator muss sich aktiv für unverschlüsselten
Betrieb entscheiden (zwei explizite Felder), bevor der Adapter
überhaupt startet. Analog zum M4-06-`MqttNetClient`-Pattern
(F-04-Verweis) bleibt der Adapter **nicht für Production gegen
einen echten Server freigegeben**, bevor M4-05 die volle Härtung
dranhängt.

**D-05 OPC-UA-Activation-Source nicht in M4-04.** RM-M4-03 D-06
hat den `IRegelleistungActivationUseCase`-Driving-Port als
Eingangspunkt fixiert; ein OPC-UA-Subscribe-Adapter, der TSO-
Aktivierungs-Signale auf den Use-Case hebelt, wäre **eigener Slice**.
Das M4-04-DoD listet ihn nicht, und das Aktivierungs-Subscription-
Mapping würde ein separates Mapping-Schema (oder eine Erweiterung
des heutigen `OpcUaNodeMapping.Direction`-Enum um
`activation-subscribe`) verlangen.

Plan-RM-M4-03 §9 F-09 hat dieses Carve-out explizit M4-04 angeboten
("falls RM-M4-04 bei seiner Implementierung den Scope erweitert,
kann ein OPC-UA-Activation-Source-Carve-out dort landen, sonst ist
es F-09"); **M4-04 lehnt ab — die F-ID bleibt F-09**, kein
neues F-Item für die OPC-UA-Spezialisierung (das wäre Doppel-
Tracking). M4-04-D pinnt explizit, dass Activation-Source-
Subscriptions **nicht** bedient werden, und der Health-Endpoint
`/health/regelleistung` aus M4-03 zeigt weiter `last_activation:
null`, solange F-09 nicht zündet.

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
- **Architektur-Tabu-Test**: Domain + Application bleiben `Opc.Ua`-frei
  (negativer Tabu-Test — der einzige, den der Architektur-Suite hier
  fährt). Der neue Adapter muss diese Eigenschaft beim Bau nicht
  brechen; ein positiver „Adapter darf"-Test wird nicht eingeführt.
- **Security-Startup-Guard (D-04)**: `OpcUaAdapterOptions.EnsureValid()`
  wirft mit `opcua-security-not-hardened` wenn `SecurityMode==None`
  und `AllowUnsecured==false`, ODER wenn `AllowUnsecured==true` mit
  leerem `AllowUnsecuredReason`. Bei akzeptiertem `AllowUnsecured=
  true` + nicht-leerem Reason startet der Adapter mit strukturiertem
  ILogger-Warning. **Pin in Sub-Slice A** (`Options-Defaults-werfen`,
  `AllowUnsecured-true-ohne-Reason-wirft`, `AllowUnsecured-true-
  mit-Reason-startet-mit-Warning`).
- **Adapter-Modul-Trennung**: `OpcUaTelemetrySource`/`OpcUaCommandSink`
  schreiben **nicht** ohne Setpoint-Clamping (`AdapterWriteLimiter`-
  Pfad bleibt vorgeschaltet).

---

## 7. Risiken und Tradeoffs

- **NuGet-Lizenz-Risiko (D-01).** Die Binär-Distribution über NuGet
  läuft unter dem **OPC Foundation Redistributables Agreement v1.3**
  (Quell-Code des Reference-Stacks ist GPL-2.0 mit RCL-Klausel für
  OPC-Foundation-Mitglieder). Reine Adapter-Nutzung (kein
  Re-Distribution des Stacks selbst) ist erfahrungsgemäss
  unproblematisch, sollte aber im Lizenz-Audit dokumentiert sein.
  Falls eine zukünftige Compliance-Linie den Stack ausschliesst,
  ist Wechsel auf (b) `Workstation.UaClient` (MIT) per
  `IOpcUaClient`-Port-Vertrag geringe Migration (~200 LOC im
  OpcUaClient).
- **OPC-UA-Variant-Decoding (Sub-Slice A/B).** Die Reference-Stack-
  `Session.ReadValueAsync`-API retourniert `DataValue`, dessen
  `Value` als boxed `Variant` mit Typ-Diskriminator vorliegt. Pro
  `OpcUaDataType` braucht `OpcUaClient` deterministisches Unboxing
  (mit Pin-Tests pro Wire-Typ); Mismatch zwischen Mapping-`data_type`
  und tatsächlichem Server-Variant-Type ⇒ `DataQuality.ProtocolError
  ("opcua-type-mismatch")`. Das ist die Stelle, an der ein Server,
  der gegen das Mapping driftet, sich offenbart.
- **Cancellation-Token-Honorierung in der SDK-Session (Sub-Slice
  A/B).** Der OPC-Foundation-Reference-Stack honoriert
  `CancellationToken` historisch nicht durchgängig in der Session-
  I/O-Pfad — lange TCP-Reads können den Token ignorieren bis der
  Session-`OperationTimeout` zündet. Der `IOpcUaClient`-Wrapper muss
  pro Aufruf den `OperationTimeout` engpässen plus eigene
  `CancellationTokenRegistration → Session.Close()` setzen, damit
  cooperative cancellation auch unter dem Stack durchschlägt.
  Pin-Test in Sub-Slice A (`Cancellation-aborts-pending-Read`) sichert
  das.
- **Embedded-TestServer-Footprint + StatusCode-Override-Aufwand
  (D-07 Wahl a).** OPC-UA-Server-Stack als Test-DLL-Dependency ist
  ~5-10 MB extra im Test-Image; das ist erträglich. **Aufwendiger ist
  die `StatusCode`-Override-Mechanik**: der Reference-Stack-
  `Sample.NodeManager` exponiert nicht out-of-the-box pro Knoten
  einen scriptbaren `StatusCode`-Slot. Sub-Slice D braucht einen
  kleinen Test-NodeManager-Subtype mit per-Test-konfigurierbarem
  `StatusCode`-Feld (~80-150 LOC). Falls dieser Subtype-Aufwand das
  Sub-Slice-D-Budget sprengt, ist Wechsel auf D-07 Wahl (b)
  Docker-Sidecar das Fallback (F-16 zündet).
- **Sub-Slice-D-LOC-Budget realistisch.** Die Master-Tabelle nennt
  300-500 LOC; mit dem oben genannten Test-NodeManager-Subtype
  (~80-150 LOC) plus Reconnect-Test plus 5 End-to-End-Pins liegt der
  realistische Endwert bei **400-600 LOC**, nicht 300. Wenn D-Slice
  über 600 LOC ausläuft, ist Carve-out eines Reconnect-Test-Sub-
  Slices `D-Reconnect` zulässig.
- **Sub-Slice-C-Test-Rebasing + CA1506-Suppression bleibt.**
  `BessHostBuilder` und `BessConfigurationBootstrap` werden um eine
  OPC-UA-Branch erweitert. Modbus/MQTT-Hostbuilder-Tests bleiben
  unverändert (additiv). `BessHostOptions` bekommt drei neue
  Properties; ein Operator mit ausschliesslich Modbus-Konfiguration
  sieht keinen Verhaltens-Unterschied (Default-Pfad bleibt Modbus/
  Mqtt/NoOp). Die bereits bestehende `[SuppressMessage("Maintainability",
  "CA1506")]`-Annotation auf `BuildApp` (`BessHostBuilder.cs:39-42`)
  bleibt erhalten — das Composition-Root-Coupling steigt durch die
  neue Branch leicht, ist aber intrinsisch zur Komposition und nicht
  zur Refactoring-Frage.
- **Mapping-Schema-Drift — M4-04 ist der wahrscheinlichste F-07-
  Trigger.** RM-M4-07 hat das v1-Schema fixiert; M4-04 ist erster
  realer Konsument der nicht-Aktivierungs-Felder. Wenn die
  M4-04-Implementation Felder findet, die v1 nicht abdeckt (z. B.
  Per-Knoten-Timeouts, Per-Knoten-Auth-Override jenseits des heutigen
  `AuthRequired`-Strings, oder Subscribe-Specific-Fields wie
  `DeadbandType`/`DeadbandValue`), kommt jeder Pflichtfeld-Bedarf
  mit einem eigenen F-07-Slice (Mapping-Migration v1→v2 als
  Template). Das ist das im
  `note-RM-M4-followups.md` Item F-07 hinterlegte Aktivierungs-
  Szenario.
- **OPC-UA-Type-System-Lücken.** Das v1-Mapping deckt skalare
  Datentypen (`bool`/`int*`/`uint*`/`float`/`double`/`string`).
  Strukturen, Arrays und Enums sind **nicht** im Scope von M4-04;
  Mapping-Knoten mit unbekanntem `data_type` (auf Loader-Ebene
  geblockt) erreichen den Adapter nie, aber der Server kann ein
  Sample mit unerwartetem OPC-UA-Variant-Type liefern (Pin-Test:
  `DataQuality.ProtocolError("opcua-type-mismatch")` — siehe oben
  unter Variant-Decoding).

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
Bei Slice-Closure (analog zu plan-RM-M4-03 §10) ergänzt der letzte
Commit:

- ein **§10 Auslieferung**-Block mit der Liste der vier Sub-Slice-
  Commits (plus etwaige Review-Pass-Commits), Validierungs-Status
  (`make gates`, `make test-integration`, `make test-hil-opcua`),
  und einem Hinweis darauf, dass der Adapter bewusst nicht produktiv
  schaltbar ist bevor RM-M4-05 die Security-Härtung dranhängt;
- die RM-M4-04-Zeile in `plan-RM-M4.md` flippt auf ✅ mit einer
  Implementierungs-Zusammenfassung im Master-Plan;
- der Slice-Plan wandert von `in-progress/` nach `done/`,
  Cross-References werden an die neuen relativen Pfade angepasst.

---

## 9. Folgearbeiten (gehen in `note-RM-M4-followups.md`)

Die folgenden Items werden bei diesem Slice nicht implementiert,
gehen aber als F-Items in die Trigger-Watch-Notiz:

**Kein neues F-Item für OPC-UA-Activation-Source-Subscribe.** Die
Folgearbeit liegt bei **F-09** (M4-03-Followups Item) — siehe D-05
oben. M4-04 lehnt das M4-03-§9-Carve-out ab; F-09 bleibt der
Tracking-Home, wenn ein TSO-/Vendor-Spec ein OPC-UA-basiertes
Aktivierungs-Subscription-Profil verlangt.

- **F-13 OPC-UA-Multi-Server / Endpoint-Failover** — Trigger: konkret
  z. B. dual-Verteilnetzbetreiber-Endpoint mit Hot-Standby für
  N-1-Resilienz, oder ein Vendor-spezifisches Redundancy-Cluster-
  Profil aus der TSO-Spec. Heute: Single-Endpoint pro Asset; der
  `OpcUaAdapterOptions.EndpointUrl` ist eine einzelne URL.
- **F-14 OPC-UA-Method-Calls / HistoricalAccess / Events** — Trigger:
  konkret z. B. TSO-Spec verlangt Aufruf einer Server-Method
  (`Reset`, `RequestStatus`) als Teil des Aktivierungs-/
  Quittierungs-Pfades, oder Auslesen historischer Frequenz-Werte
  über HistoricalAccess für Compliance-Auditing.
- **F-15 OPC-UA-Type-System-Erweiterung** (Strukturen, Arrays, Enums) —
  Trigger: konkret z. B. ein BMS-Vendor exportiert Cell-Voltage-
  Arrays oder strukturierte Fault-Codes als OPC-UA-`ExtensionObject`,
  die sich nicht in den heutigen `bool`/`int*`/`uint*`/`float`/
  `double`/`string`-`OpcUaDataType` mappen lassen. Verbindet sich
  mit F-07 (Mapping-Migration v1→v2).
- **F-16 Dockerized OPC-UA-Simulator-Sidecar** — Trigger:
  Vendor-Compat-Profil-Tests verlangen einen realen externen Server
  statt des Embedded-TestServers (D-07 Wahl b), **oder** der
  Embedded-TestServer-`StatusCode`-Override-Aufwand (siehe §7) bricht
  das Sub-Slice-D-Budget.
