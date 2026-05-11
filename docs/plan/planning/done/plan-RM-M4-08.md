# Plan RM-M4-08 — Integrationstests OPC-UA gg. Simulator (Closure-Slice)

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M4-08)
**Status:** Erledigt — RM-M4-08-A abgeschlossen; 7 Pins in `make test-hil-opcua` grün, in `make gates` und `make ci` verdrahtet; F-09 in `note-RM-M4-followups.md` als eigenständiges Item angelegt; Master-Plan-Wortlaut korrigiert
**Bezug:**
[`plan-RM-M4.md`](plan-RM-M4.md) (Master-Plan, RM-M4-08-Zeile mit DoD und LH-Bezug),
[`./plan-RM-M4-04.md`](./plan-RM-M4-04.md) (RM-M4-04-D liefert die 5 OPC-UA-Adapter-Happy-Path-Pins gegen den Embedded TestServer; M4-08-A ergänzt **zwei** Pins die echtes Adapter-Verhalten testen statt bestehende Tests zu duplizieren — siehe Review-Pass §H1),
[`./plan-RM-M4-03.md`](./plan-RM-M4-03.md) (RM-M4-03 deckt Race/Tiebreak/Duplikat-Replay/**Restart**-Replay/TimebaseDegraded-Pins **bewusst auf der Use-Case-Schicht** ab — M4-08 dupliziert diese Pins **nicht** via OPC-UA-Wire; **Failover-Replay-via-OPC-UA-Reconnect** bleibt aber strukturell ungeklärt und erbt nach F-09 — siehe D-01 + D-03),
[`../open/note-RM-M4-followups.md`](../open/note-RM-M4-followups.md) (M4-08-A's Closure ergänzt dort das **F-09-Item** mit dem expliziten Sub-Bullet „Failover-Replay-Pin via OPC-UA-Reconnect" — siehe §3 In-Scope und D-03),
[`../../../user/quality.md`](../../../user/quality.md) (Quality-Doku, bekommt das `make test-hil-opcua`-Target als verbindliches OPC-UA-Closure-Gate; per D-02 wandert das Target sowohl in `make gates` als auch in `make ci`)

---

## 1. Zweck

RM-M4-08 ist das **Closure-Slice der OPC-UA-Linie**. RM-M4-04-D
hat den Embedded TestServer plus 5 happy-path-Pins (Read, Subscribe,
Write, StatusCode, Reconnect) geliefert — RM-M4-08-A ergänzt
**zwei** echte Negativ-/Stress-Pfade auf der OPC-UA-Adapter-Schicht
(Multi-Cycle-Reconnect, Concurrent-Source-Sink-mit-Restart-Injection),
zieht das `make test-hil-opcua`-Target in `make gates` UND `make ci`
(analog zur `test-native-*`-Verdrahtung), und korrigiert den
driftenden Master-DoD-Wortlaut.

Das Slice ist **bewusst schmal**. Eine erste Plan-Version listete fünf
Pins; der externe Review hat drei davon als Duplikate existierender
Tests entlarvt:

- *Subscribe-Storm-Overflow*: bereits gepinnt in
  `tests/adapters/driven/BatteryEms.Adapters.OpcUa.Tests/OpcUaTelemetrySourceTests.cs:349-393`
  + `:502-543` (FakeOpcUaClient durchläuft denselben
  `OverflowAwareTelemetryChannel`; das Wire-Layer hat keine eigene
  Channel-Mechanik, die der Unit-Test verfehlt).
- *IoAdapterTriage-Multi-IO-Boot*: bereits gepinnt in
  `tests/hexagon/BatteryEms.Application.Tests/IoAdapterTriageTests.cs:31-47`
  mit allen vier `(modbus, mqtt, opcua)`-Kombinationen.
- *Mapping-Validation-Edge-Case*: Unit-Test-natürlich (FakeOpcUaClient
  reicht); der ursprünglich vorgeschlagene Wire-Pfad-Pin würde wegen
  `Bad`-StatusCode auf einem nicht-existenten Knoten sogar ein
  `ProtocolError`-Sample emittieren statt das versprochene
  „kein Sample".

Diese drei sind aus M4-08 herausgenommen. Übrig bleiben zwei
Pin-Kategorien, die **echtes** Wire-Verhalten testen, das der
Unit-Test nicht abdeckt: Multi-Cycle-Reconnect über drei Server-
Restart-Zyklen in einer einzigen Source-Lifetime, und
Concurrent-Source-Sink-unter-Restart-Injection (probt das
`_connectGate` plus `_stateGate`-Interaktion, die die
SDK-Thread-Safety-Garantie für reine Read+Write nicht abdeckt).

Die Master-DoD von RM-M4-08 (`plan-RM-M4.md:171`) listet zusätzlich
Aktivierungsjitter, Race-/Tiebreak-/Duplikat-Replay-/
Restart-Replay-/TimebaseDegraded-Pins. Diese sind:

- **bereits ✅** durch RM-M4-03 (8 Application-Test-Files mit ~110
  Pins + 12 Persistence-Integration-Pins) auf der Use-Case- und
  Persistence-Schicht abgedeckt — siehe D-01.
- **mit einer namhaften Lücke**: das Master-DoD-Wort „**Failover**-
  Replay" koppelt sich laut `plan-RM-M4.md:295-298` explizit an
  OPC-UA-Reconnect-Re-Delivery; RM-M4-03's Restart-Replay-Pins
  simulieren nur Process-Restart, nicht Mid-Stream-OPC-UA-Reconnect-
  Re-Delivery. Diese Lücke ist heute strukturell nicht testbar, weil
  es keinen OPC-UA-Activation-Source-Adapter gibt (M4-04-D-05 hat
  das M4-03-§9-Carve-out abgelehnt). RM-M4-08-A räumt das **nicht
  silent unter den Tisch**, sondern ergänzt explizit das F-09-Item
  in `note-RM-M4-followups.md` um den Sub-Bullet „Failover-Replay-
  Pin via OPC-UA-Reconnect" (D-03 Konsequenz).
- Aktivierungsjitter via OPC-UA + Security-Basispfad → F-09 bzw.
  RM-M4-05 (siehe §3 Out-of-Scope).

Der Master-DoD-Wortlaut von RM-M4-08 wird bei Closure entsprechend
**korrigiert**: Pins die in M4-03 ✅ liegen werden namentlich
verlinkt; Pins die F-09/M4-05 brauchen werden ebenfalls namentlich
verlinkt; **Failover-Replay-via-OPC-UA-Reconnect** wird explizit
auf F-09 verschoben mit Trigger-Beschreibung. Damit verschwindet die
Obligation nicht — sie wandert mit named Trigger.

---

## 2. Aktivierungsbedingungen

- **RM-M4-04 ✅** (`plan-RM-M4.md:167`) — der Embedded TestServer-
  Fixture-Pfad existiert, der Production-`OpcUaClient` ist bind-fest
  gegen die OPC-Foundation-Reference-Stack 1.5.378.x.
- **RM-M4-07 ✅** (`plan-RM-M4.md:170`) — das Mapping-Schema-File
  liefert die Test-Fixture-Inputs.
- **RM-M4-03 ✅** (`plan-RM-M4.md:166`) — die Race-/Tiebreak-/
  Dedupe-Pin-Linie liegt auf der Use-Case-Schicht; M4-08 verzichtet
  darauf, sie via OPC-UA-Wire zu duplizieren.

**Kein** abhängiger Slice nach M4-08-A — Security-Basispfade
(RM-M4-05) und Aktivierungsjitter via OPC-UA (F-09) sind separate
Slices und werden **nicht** rückwirkend in M4-08 ergänzt (siehe D-04).

---

## 3. Scope

**In Scope (RM-M4-08-A):**

- **Zwei Negativ-/Stress-Pins** im existierenden
  `tests/integration/BatteryEms.OpcUa.IntegrationTests/`-Projekt, in
  einer neuen Datei `OpcUaNegativeTests.cs` (D-06):
  - **Multi-Cycle-Reconnect-Pin** — drei aufeinanderfolgende
    `RestartAsync`-Cycles innerhalb einer einzigen `OpcUaTelemetry-
    Source`-Stream-Lifetime; jeder Cycle setzt einen anderen Marker-
    Wert auf `Battery.Temperature` (z. B. 25 → 31 → 37 → 42), der
    Stream emittiert nach jedem Cycle ein Sample mit dem post-restart-
    Marker (`DataQuality.Valid`). Plus zwei client-seitige
    Subscription-Tracker-Assertions: (1) **nach jedem Cycle**, sobald
    das post-restart-Marker-Sample angekommen ist, gilt
    `OpcUaClient.SubscriptionCount == 1` (genau die neue Subscription
    in der Map); (2) **post-Dispose** der Source gilt
    `OpcUaClient.SubscriptionCount == 0` (alles abgeräumt). Beide
    Assertions zusammen catchen die Bug-Klasse „jeder Cycle leakt
    eine Subscription, aber `OpcUaClient.DisposeAsync` cleart die
    Map am Ende doch noch". Server-seitige Cleanup ist OPC-UA-Spec-
    garantiert via Session-Close (alle Subscriptions auf der
    geschlossenen Session werden vom `SessionManager` automatisch
    entfernt) — kein zusätzlicher Pin nötig (siehe D-05).
  - **Concurrent-Source-And-Sink-mit-Restart-Pin** — Source und
    Sink teilen denselben `OpcUaClient`-Singleton (entspricht der
    Composition-Root-Linie); `Task.WhenAll(source.ReadAsync()-take-bis-Restart-fertig,
    sink.WriteAsync()-Loop-30-Commands-über-3s,
    host.RestartAsync()-bei-1.5s)` läuft 5 s. Asserts: (a) alle
    Sink-Writes haben entweder `result.Success==true` oder
    `result.Reason` matcht `opcua-write-bad-*`/`opcua-sink-disposed`/
    `opcua-write-failed`; kein Throw, kein Zombie-State. (b) Source
    emittiert nach Restart **mindestens ein** Sample mit
    `DataQuality.Valid`. (c) `_client.IsConnected == true` am Ende.
    Probt das `_connectGate`-`_stateGate`-Zusammenspiel unter
    realer Contention, was die SDK-Thread-Safety-Garantie für reine
    Read+Write **nicht** abdeckt.
- **`OpcUaTestServerFixture` per-class** statt collection-shared
  (M2-Fix): D-06 bricht das xUnit-`IClassFixture<>`-Sharing zwischen
  `OpcUaRoundtripTests` und `OpcUaNegativeTests`. Beide Klassen
  bleiben in `[Collection("OpcUa Integration")]` (Serialisierung),
  bekommen aber je eine eigene Fixture-Instanz (~6s extra Startup-
  Zeit, klare State-Isolation).
- **Test-Defaults für M4-08-A pinnen `KeepAliveInterval = TimeSpan.
  FromSeconds(2)`** (L4/D-07): die Multi-Cycle-Reconnect-Timing-
  Triple `(KeepAliveInterval, PollingInterval, ReconnectBackoff-
  Start)` bekommt einen expliziten Defaults-Slot — ohne den hängt
  der Source bis zu 10 s an einer toten Session, bevor die
  ConsecutiveFailures-Schwelle die Recovery zündet.
- **`make test-hil-opcua` zieht in `make gates` UND `make ci`**
  (D-02): direkter Eintrag in beide Makefile-Targets, analog zur
  bestehenden `test-native-{interop,parity}`-Verdrahtung in `gates`.
  Das gilt sowohl für die fünf existierenden M4-04-D-Pins als auch
  für die zwei neuen — kein Opt-in mehr für die OPC-UA-Linie.
- **Quality-Doku-Update** (`docs/user/quality.md`): ein neuer
  Abschnitt unter „M4 Mandatory Gates" listet `make test-hil-
  opcua` (jetzt mandatory) mit der vollständigen Pin-Inventory
  (5 happy-path aus M4-04-D + 2 negativ/stress aus M4-08-A);
  Cross-Reference zu `make test-integration` (Modbus/MQTT-Compose)
  und `make test-hil-modbus` (Bess-HIL-Simulator opt-in wegen
  externer Container-Abhängigkeit) klärt die Konventionsgrenze.
- **`note-RM-M4-followups.md` F-09-Update**: das Item F-09 ist
  heute **nur** in den Slice-Plan-Cross-References dokumentiert,
  nicht als eigenständiges `## Item F-09:`-Header in der
  Followups-Note. M4-08-A legt das Item explizit an (mit Sub-Bullets:
  konkrete Source-Wire-Adapter, **Failover-Replay-Pin via OPC-UA-
  Reconnect**, Aktivierungsjitter-Pins via OPC-UA-Wire) und
  benennt einen konkreten Trigger („erste TSO-Spec mit
  OPC-UA-Aktivierungsendpoint, oder explizite operator-anforderung
  nach OPC-UA-Mid-Stream-Reconnect-Replay-Verifikation").
- **Master-Plan-Wortlaut-Cleanup** (D-03): die RM-M4-08-Zeile in
  `plan-RM-M4.md:171` wird bei Closure umformuliert mit expliziten
  Verweisen auf RM-M4-03/RM-M4-05/F-09 plus dem F-09-Sub-Item für
  Failover-Replay.

**Out of Scope (separate Slices / Folgearbeiten):**

- **Aktivierungsjitter-Profile via OPC-UA-Wire** → **F-09**.
  Trigger: konkrete TSO-Spec, die Aktivierungssignale auf einem
  OPC-UA-Endpoint statt auf der heutigen Driving-Port-Form liefert.
- **Failover-Replay-Pin via OPC-UA-Reconnect** → **F-09 (Sub-Bullet,
  von M4-08-A neu eingefügt).** Trigger: F-09 zündet (OPC-UA-
  Activation-Source-Adapter existiert) — dann ist der Pin als
  Mid-Stream-Reconnect-Re-Delivery-Test gegen `IRegelleistung-
  ActivationUseCase.ReceiveAsync` formulierbar. Die
  Reconnect-Mechanik selbst ist in M4-04-D abgedeckt; was fehlt ist
  der Source-Adapter, der eine Aktivierung über die Subscription
  liefert.
- **Race-/Tiebreak-/Duplikat-Replay-/Process-Restart-Replay-/
  TimebaseDegraded-Pins** sind in **RM-M4-03 ✅** abgedeckt. M4-08
  dupliziert sie **nicht** via OPC-UA-Wire — siehe D-01.
- **Security-Basispfad gegen OPC-UA-Simulator** → **RM-M4-05**.
  Pre-M4-05 ist der einzige Security-relevante Pin der
  AllowUnsecured-Startup-Guard, der bereits in M4-04-A liegt.
- **Multi-Server / Endpoint-Failover** → **F-13** (M4-04-Followup).
- **Method-Calls / HistoricalAccess / Events** → **F-14**.
- **Type-System-Erweiterung (Strukturen, Arrays, Enums)** → **F-15**.
- **Compose-Sidecar-Fallback** → **F-16** (zündet nur wenn der
  Embedded TestServer-Pfad bricht; heute grün).

---

## 4. Sub-Slices

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M4-08-A | OPC-UA-Integration-Closure: 2 Negativ-/Stress-Pins + per-class Fixture + KeepAlive-Pinning + `gates`/`ci`-Promotion + Quality-Doku + F-09-Note + Master-Plan-Wortlaut-Cleanup — **~150-250 LOC** | Neue Datei `tests/integration/BatteryEms.OpcUa.IntegrationTests/OpcUaNegativeTests.cs` mit den zwei in §3 genannten Pins. **Multi-Cycle-Reconnect-Pin** wiederholt das M4-04-D-Reconnect-Pattern dreimal in Folge auf einer einzigen Source. Pro Cycle: SetValue Marker auf `Battery.Temperature`, `RestartAsync(15s-CT)` auf der Fixture, dann auf das nächste Sample mit dem neuen Marker warten (Schwelle: `Math.Abs(temp - marker) < 0.5 && DataQuality.Valid`); **post-Sample-Assertion**: `client.SubscriptionCount == 1` (Recovery hat eine neue Subscription registriert und die alte abgeräumt, bevor das Marker-Sample fließen konnte). Settle-Wait 200ms vor dem nächsten Cycle. Nach dem dritten Cycle: Source disposen, dann **post-Dispose-Assertion**: `client.SubscriptionCount == 0` (alle Subscriptions sauber abgeräumt — der bestehende `internal RemoveSubscription`-Pfad in `OpcUaClient.cs` macht das deterministisch, kein SDK-Internal-Zugriff nötig). **Concurrent-Source-Sink-mit-Restart-Pin** baut einen `OpcUaClient`, teilt ihn zwischen `OpcUaTelemetrySource` und `OpcUaCommandSink`, startet `Task.WhenAll` mit drei Tasks: (a) Source liest und sammelt Samples bis zum Stream-Ende oder 5s timeout; (b) Sink schreibt 30 Setpoint-Commands über 3 Sekunden mit ~100ms Pause zwischen den Commands; (c) Restart-Task wartet 1.5 s und ruft dann `host.RestartAsync(15s-CT)`. Asserts (per §3): kein Throw, mindestens ein Source-Sample post-Restart mit `DataQuality.Valid`, `_client.IsConnected==true` am Ende, alle Sink-Writes mit kebab-Reasons (kein silenter zombie-state). **`OpcUaNegativeTests` und `OpcUaRoundtripTests` bekommen je eine eigene `IClassFixture<OpcUaTestServerFixture>`-Instanz** (kein collection-level fixture sharing); die `[Collection("OpcUa Integration")]`-Annotation bleibt erhalten für serialisierte Ausführung. Beide Klassen erben das `IAsyncLifetime.InitializeAsync`-Reset-Pattern aus M4-04-D-Review-Fix M7. **Test-Defaults**: in `Defaults.cs` wird `KeepAliveInterval = TimeSpan.FromSeconds(2)` ergänzt (D-07); kein Production-Default-Change. **`make gates` + `make ci`-Promotion**: beide Targets werden um `test-hil-opcua` erweitert (analog zu `test-native-{interop,parity}`, die ebenfalls in beide Targets verdrahtet sind). In `make ci` läuft `test-hil-opcua` vor `test-integration` (Reihenfolge nach Stabilität: process-internal vor compose-Sidecar). **`note-RM-M4-followups.md`** bekommt einen neuen Abschnitt `## Item F-09: OPC-UA-Activation-Source-Adapter (incl. Failover-Replay-via-Reconnect)` mit Trigger-Beschreibung, Implementierungs-Skizze (Driving-Port-Adapter analog zu Modbus-Activation-Source-Vision in M4-03 §9), und expliziter Sub-Bullet-Liste: (a) Source-Wire-Adapter selbst, (b) Failover-Replay-Pin via OPC-UA-Reconnect, (c) Aktivierungsjitter-Profile-Pins. **Quality-Doku-Update** in `docs/user/quality.md`: neuer Abschnitt unter „Mandatory Gates" listet `make test-hil-opcua` mit Pin-Inventory (5+2=7 Pins gesamt); klare Abgrenzung zu `make test-integration` und `make test-hil-modbus`. **Master-Plan-Wortlaut-Cleanup**: bei Closure flippt RM-M4-08-Zeile in `plan-RM-M4.md` auf ✅ und die DoD-Spalte wird neu formuliert mit expliziten Cross-References (siehe D-03 für den genauen Wortlaut). **Bewusst draußen** auf Sub-Slice-Ebene: F-13 Multi-Server-Pins, F-15 Type-Marshalling-Pins, RM-M4-05-Security-Pins; alle haben separate F-IDs und entweder keinen Trigger heute oder warten auf eigene Slices. |

---

## 5. Design-Entscheidungen

**D-01 RM-M4-03-Pins werden NICHT via OPC-UA-Wire dupliziert —
mit einer namhaften Failover-Replay-Lücke, die F-09 erbt.**
Die Master-DoD von RM-M4-08 (`plan-RM-M4.md:171`) listet
„konkurrierende gültige Aktivierungssignale, vollständige Tiebreak-
Gleichstände, Duplikat-Replay, Restart-/**Failover**-Replay aus
persistentem Dedupe-Tracker, widersprüchliche Wiederholung,
TimebaseDegraded-Debounce, deterministische Persistenz". Diese Pins
liegen größtenteils auf der **Use-Case-Schicht** in RM-M4-03:

- 8 Application-Test-Files mit ~110 Pins (`tests/hexagon/BatteryEms.
  Application.Tests/Regelleistung*Tests.cs`),
- 12 Persistence-Integration-Pins gegen Postgres (`tests/integration/
  BatteryEms.Persistence.IntegrationTests/Activation*Tests.cs`),
- 1 API-Endpoint-Test (`/health/regelleistung`).

Der Validator-Order ist Wire-agnostisch (gleiche kebab-Reasons egal
ob Driving-Port oder OPC-UA-Subscribe), Race/Tiebreak ist
deterministisch Use-Case-intern, **Process**-Restart-Replay ist
Persistence-Schicht. Eine Replikation auf OPC-UA-Wire bringt für
**diese** Pins keinen neuen Pin-Wert.

**Aber:** der Master-DoD-Begriff „**Failover**-Replay" plus
`plan-RM-M4.md:295-298` („Persistenter Replay-Schutz. Dedupe nur im
Speicher reicht nicht, weil Restart, Failover und **OPC-UA-Reconnect**
alte Signale erneut liefern können") koppelt eine spezifische
Replay-Quelle an OPC-UA-Mid-Stream-Reconnect: Server kappt die
Session, Source reconnectet, Server liefert die letzte Aktivierung
erneut, der Use-Case sieht sie als Replay-Idempotent (gleicher
`payload_hash`) statt als neue Aktivierung.

Heute ist dieser Pfad **strukturell nicht testbar**, weil:

- es keinen OPC-UA-Activation-Source-Adapter gibt
  (`IRegelleistungActivationSource` über OPC-UA-Subscription auf den
  Use-Case hebeln);
- M4-04-D-05 das M4-03-§9-Carve-out für genau diesen Adapter
  abgelehnt hat;
- M4-08 das Carve-out ebenfalls ablehnt (es wäre kein
  Integrationstest-Slice mehr, sondern ein zweiter Adapter-Slice).

**Konsequenz:** RM-M4-08-A schließt diese Lücke **nicht**, lässt sie
aber **nicht silent verschwinden**. Sub-Slice-A's DoD trägt explizit
den F-09-Note-Update als Pflicht-Aufgabe; die F-09-Item-Beschreibung
in `note-RM-M4-followups.md` bekommt einen neuen Sub-Bullet
„Failover-Replay-Pin via OPC-UA-Reconnect" mit konkretem Trigger.
Die korrigierte Master-DoD von RM-M4-08 (D-03) verlinkt diesen
Sub-Bullet namentlich.

**D-02 `make test-hil-opcua` wandert in den Pflicht-`make ci`-Pfad.**
Eine erste Plan-Version argumentierte für Opt-in via „analog zur
HIL-Modbus-Linie". Der Review hat nachgewiesen, dass die HIL-Modbus-
Opt-in **infrastrukturell** begründet ist (externer
`bess-hil-simulator:local`-Container), während `test-hil-opcua`
vollständig process-internal läuft (Embedded TestServer im selben
.NET-Prozess; siehe `Dockerfile:368-385` Kommentar). Der einzige
verbleibende Grund für Opt-in wäre **Flakiness-Furcht** — und die
beiden Pins in M4-08-A sind zeitlich gut bounded (jeder Cycle ≤ 2s
Settle, gesamter Multi-Cycle ≤ 10s; Concurrent-Pin ≤ 5s).

Damit:
- M4-08-A erweitert sowohl das `Makefile:gates`- als auch das
  `Makefile:ci`-Target um `test-hil-opcua`. Beide bekommen denselben
  neuen Eintrag — die Konvention dafür ist **bereits etabliert**:
  `make gates` enthält schon `test-native-{interop,parity}` (siehe
  `Makefile:170`), die ebenfalls keine M1-Unit-Tests sind, sondern
  M3-mandatorische Integration-Tests gegen den nativen Kernel.
  `test-hil-opcua` reiht sich in dieselbe „Mandatory-Integration"-
  Linie ein — process-internal, kein externes Asset, deterministisch
  bounded.
- Die fünf existierenden M4-04-D-Pins fallen damit ebenfalls in den
  Pflicht-Pfad (sie waren schon als `make test-hil-opcua`-Schritt
  benannt, aber bisher nicht in `make gates`/`make ci` verdrahtet).
- Operator-Workflow: `make gates` für schnelles Pre-Commit-Feedback
  catched OPC-UA-Regressionen ohne Compose-Overhead. `make ci`
  bleibt der Vollpipeline-Run.

Falls die Pins auf CI-Container-Jitter doch flake produzieren: das
ist eine **Hardening-Aufgabe** im selben Slice (Timing-Toleranzen
großzügiger, oder Recovery-Threshold tunen), kein Grund für ein
neues Opt-in-Regime.

**D-03 Master-DoD-Wortlaut wird bei Closure korrigiert — mit
expliziter Verlinkung der einen unbedeckt-bleibenden Obligation.**
Der heutige RM-M4-08-DoD-Wortlaut in `plan-RM-M4.md:171` ist eine
Copy-Paste-Mischung aus M4-03-Race-Pins und einer ursprünglichen
End-to-End-Vision für M4-08. Pins die M4-03 schon erfüllt, lassen
sich nicht ein zweites Mal in M4-08 erfüllen.

Bei Closure wird RM-M4-08 in `plan-RM-M4.md` umformuliert. Der neue
Wortlaut wird **vor Implementation hier festgelegt**, damit der
Closure-Reviewer nicht aus den D-03-Constraints rückwärts den
Wortlaut rekonstruieren muss:

> **DoD-Replacement-Text (verbindlich):**
>
> Multi-Cycle-Reconnect-Pin (drei Server-Restart-Cycles in einer
> Source-Lifetime, post-Sample-Assertion `SubscriptionCount==1`,
> post-Dispose `SubscriptionCount==0`) und Concurrent-Source-Sink-
> mit-Restart-Pin (30 Commands über 3s + Restart bei 1.5s probt
> `_connectGate`/`_stateGate`-Contention) im Embedded TestServer-
> Projekt grün; `make test-hil-opcua` läuft im Pflicht-`make ci`
> und `make gates`. Race-/Tiebreak-/Duplikat-/Process-Restart-
> Replay-/TimebaseDegraded-/Persistenz-Pins liegen in **RM-M4-03 ✅**
> (8 Application-Test-Files mit ~110 Pins + 12 Persistence-Pins).
> Security-Basispfad gegen OPC-UA-Simulator ist **RM-M4-05**
> (separates Slice). Aktivierungsjitter via OPC-UA-Wire und
> **Failover-Replay-Pin via OPC-UA-Reconnect** sind **F-09 (a)/(b)/
> (c)** in `note-RM-M4-followups.md` mit konkretem Trigger
> (TSO-Spec mit OPC-UA-Aktivierungsendpoint oder Operator-
> Anforderung nach Mid-Stream-Reconnect-Replay-Verifikation).
> Quality-Doku trägt `make test-hil-opcua` als Mandatory Gate.

Der Closure-Reviewer matcht die Implementation gegen genau diesen
Text — keine Rückwärts-Rekonstruktion. Falls die Implementation
einen Wert ändern muss (z. B. Pin-Anzahl oder Pin-Beschreibung), wird
der Replacement-Text hier zuerst korrigiert.

Das ist **kein Slice-Aufweichen**: die Race/Process-Restart/Dedupe-
Pins **sind erfüllt** (auf der richtigen Schicht in M4-03); Failover-
Replay-via-Reconnect ist **nicht** erfüllt, wandert aber mit
namhaftem Trigger an F-09. Der Reviewer hat zurecht darauf gepocht,
dass dieser Sub-Pfad nicht silent unter „Aktivierungsjitter" subsumiert
wird — F-09 trägt ihn jetzt als eigenständigen Sub-Bullet.

**D-04 Security-Basispfad-Pins explizit auf RM-M4-05 deferred —
M4-08 wird NICHT rückwirkend ergänzt.**
Pre-M4-05 ist der einzige Security-relevante Pin der AllowUnsecured-
Startup-Guard, der bereits in M4-04-A liegt
(`OpcUaAdapterOptionsTests`). Ein Pin gegen SignAndEncrypt-Handshake,
Allowlist-Policy oder RuntimeProfile-Production-Guard verlangt das
M4-05-Slice — der Plan dort wird die Pin-Liste mit M4-08 abgleichen.

Falls M4-05 nach M4-08 landet (heutige Reihenfolge ist offen), wird
M4-08 **nicht** rückwirkend ergänzt; M4-05 trägt seine Security-
Basispfad-Pins selbst. (Die erste Plan-Version hatte in §2 einen
„Sub-Slice-B"-Promise — der ist gestrichen, weil er D-04 widersprach
und einen Phantom-F-Item-Status für M4-08-B geschaffen hätte.)

**D-05 Subscription-Leak-Check ist client-seitig deterministisch,
kein SDK-Internal-Zugriff.**
Eine erste Plan-Version wollte server-seitig `_application.Server.
CurrentInstance.SubscriptionManager.Subscriptions.Count` lesen, um
cross-cycle leaks zu finden. Der Review hat gezeigt, dass diese
Bug-Klasse **nicht existiert**: bei jedem `_client.DisconnectAsync`
in der Recovery-Schleife schließt der OPC-UA-Server die ganze
Session, und der `SessionManager` räumt damit **alle** Subscriptions
dieser Session automatisch ab — ein cross-cycle Leak ist
spec-by-design ausgeschlossen.

**Der echte Leak-Pfad ist client-seitig:** unsere
`OpcUaClient._subscriptions`-`ConcurrentDictionary` wird über den
internen `RemoveSubscription(uint subscriptionId)`-Aufruf am Ende
von `OpcUaSubscription.DisposeAsync` gepflegt. Wenn die Mid-Stream-
Recovery vergisst, die alte Subscription zu disposen, bleibt der
Eintrag in der client-internen Map liegen — *das* ist die testbare
Bug-Klasse.

Sub-Slice-A's DoD weist daher den Multi-Cycle-Reconnect-Pin auf zwei
client-seitige Assertions: post-Sample pro Cycle
`client.SubscriptionCount == 1`, post-Source-Dispose
`client.SubscriptionCount == 0`. Beide deterministisch (Recovery
disposed alte Subscription bevor neue erzeugt wird; `OpcUaClient.
DisposeAsync` cleart die Map explizit) und ohne SDK-Internal-Zugriff.

**Visibility-Shape pinnen**: Statt `_subscriptions` als Feld zu
exposen (würde den Wrapper-Typ `OpcUaSubscription` und die ganze
`ConcurrentDictionary<uint, OpcUaSubscription>`-Mechanik leaken),
bekommt `OpcUaClient.cs` einen einzigen `internal int
SubscriptionCount => _subscriptions.Count`-Property. Tests sehen
nur den Count, nicht die Wrapper-Instanzen — keine Hebel für
zukünftige Tests auf die Subscription-Internals.

**D-06 Pin-Datei-Layout: neue Datei `OpcUaNegativeTests.cs` plus
per-class Fixture-Sharing.**
M4-04-D-Tests in `OpcUaRoundtripTests.cs` sind happy-path-positiv-
Pins; M4-08-Pins sind negativ/stress. Eine separate Datei trennt
die Stilrichtungen (Reviewability) und vermeidet eine 600-LOC-Test-
Datei.

**Beide Klassen bekommen je eine eigene `IClassFixture<OpcUaTest-
ServerFixture>`-Instanz** — keine collection-level Fixture-Sharing
über `[CollectionDefinition].FixtureClasses`. Die Fixture lebt pro
Test-Klasse, nicht pro Test-Collection. Begründung: Multi-Cycle-
Reconnect ist State-mutierend (Restart-Cycles); ein Crash mitten im
Cycle würde sonst die nachfolgende Roundtrip-Klasse mit halb-
gestarteten Listenern oder verschobenen Variable-Werten erwischen.
Per-class Fixture kostet ~6s extra Startup-Zeit (zweimal
EmbeddedTestServerHost.StartAsync), liefert dafür harte State-
Isolation.

`[Collection("OpcUa Integration")]` mit `DisableParallelization=
true` bleibt — wir wollen nur einen embedded Server gleichzeitig auf
loopback. Per-Test-Reset (`InitializeAsync` setzt alle Test-Knoten
auf Defaults) wird in eine Helper-Methode `OpcUaTestServerFixture.
ResetNodeBaseline()` extrahiert und von beiden Test-Klassen aufgerufen.

**Regression-Guard**: die `OpcUaIntegrationCollection`-Marker-Klasse
ist heute leer. Wenn jemand später ein-zeilig
`ICollectionFixture<OpcUaTestServerFixture>` ergänzt (vermeintlich
als „Optimierung" für Startup-Zeit), kippt die per-class Isolation
silent. Sub-Slice-A platziert daher einen expliziten
`// DO NOT add ICollectionFixture<...> here — per-class instance is
required (M4-08-A D-06)`-Kommentar in der Klasse.

**D-07 Test-Profile pinnt `KeepAliveInterval = 2s`.**
Multi-Cycle-Reconnect-Timing hängt vom Triple `(KeepAliveInterval,
PollingInterval, ReconnectBackoffStart)` ab. Mit dem heutigen
`Defaults.ForHilSimulator`-Wert (KeepAliveInterval übernimmt
Production-Default = 10s) lag der Source nach jedem Stop bis zu 10s
am toten Session-Handle, bevor die ConsecutiveFailures-Schwelle die
Recovery zündet — ein Multi-Cycle-Test mit 10s-Settle pro Cycle
würde 30s+ dauern und CI-Container-Jitter ausgesetzt sein.

`Defaults.ForHilSimulator` bekommt einen expliziten
`KeepAliveInterval = TimeSpan.FromSeconds(2)`-Slot (Test-only;
Production-Default in `OpcUaAdapterOptions` bleibt 10s). Damit
detektiert die Session den Disconnect deterministisch innerhalb
2s, die ConsecutiveFailures-Schwelle (=2 Failed-Reads) zündet
unabhängig davon nach 200ms (PollingInterval=100ms × 2). Recovery
erfolgt im low-Sekunden-Bereich, jeder Cycle ≤ 2s Settle.

**Bewusste Lücke**: der Production-Default `KeepAliveInterval=10s`
wird damit von keinem Integration-Test mehr durchgespielt — eine
Regression, die nur am 10s-Wert sichtbar wäre, würde in CI nicht
aufschlagen. Mitigation: die Production-Defaults sind unit-test-
gepinnt in `OpcUaAdapterOptionsTests`; der Source/Recovery-Pfad ist
KeepAlive-agnostisch (nutzt nur `ConsecutiveFailures` und
`!_client.IsConnected` als Trigger). Eine wertorientierte
Regression im 10s-Wert wäre eine Code-Pfad-Änderung, die im
Source-Code-Review ohnehin sichtbar ist; der Trade-off
„deterministisches CI-Timing > exakte Production-Default-
Exercise" überwiegt.

---

## 6. Akzeptanzkriterien

- **Zwei Negativ-/Stress-Pins** im neuen `OpcUaNegativeTests.cs`-File
  grün (Multi-Cycle-Reconnect mit client-seitiger Subscription-
  Tracker-Assertion, Concurrent-Source-Sink-mit-Restart-Injection).
- **`make test-hil-opcua` grün** mit jetzt 7 Pins (5 happy-path aus
  M4-04-D + 2 negativ/stress aus M4-08-A).
- **`make ci` grün** mit `test-hil-opcua` als neuem Pflicht-Schritt
  (D-02-Konsequenz). Bei lokalem Run: `make ci` läuft ohne externe
  Container.
- **`make gates` grün** mit `test-hil-opcua` als neuem Pflicht-
  Schritt — analog zur bestehenden `test-native-{interop,parity}`-
  Verdrahtung in `gates`. Pre-Commit-Workflow erfasst OPC-UA-
  Regressionen ohne Compose-Overhead.
- **`OpcUaTestServerFixture` per-class** für beide Test-Klassen
  (`OpcUaRoundtripTests`, `OpcUaNegativeTests`); kein Fixture-State-
  Sharing zwischen den Klassen. Verifizierbar im fixture-Lifecycle-
  Trace (zwei `StartAsync`-Aufrufe pro `make test-hil-opcua`-Run).
- **`Defaults.ForHilSimulator` pinnt `KeepAliveInterval = 2s`** mit
  Kommentar warum (D-07).
- **`note-RM-M4-followups.md`** trägt jetzt explizit das Item F-09
  mit Sub-Bullet „Failover-Replay-Pin via OPC-UA-Reconnect" und
  konkretem Trigger.
- **Quality-Doku** (`docs/user/quality.md`) listet `make test-hil-
  opcua` als Mandatory Gate mit der vollständigen Pin-Inventory
  + Konventionsabgrenzung zu `make test-integration` und
  `make test-hil-modbus`.
- **Slice-Plan** in `docs/plan/planning/done/plan-RM-M4-08.md` (von
  in-progress nach done verschoben).
- **Master-Plan-Zeile RM-M4-08** flippt auf ✅ mit korrigiertem
  DoD-Wortlaut, der explizit die Verweise auf M4-03/M4-05/F-09 plus
  den F-09-Sub-Bullet trägt (D-03).
- **Master-DoD-Self-Consistency-Check**: nach Closure verlinkt jede
  ursprüngliche Master-DoD-Bestandzeile entweder einen ✅-Slice oder
  ein konkretes F-Item mit Trigger; keine verwaiste Pin-Promise.

---

## 7. Risiken und Tradeoffs

- **Multi-Cycle-Reconnect-Pin Timing-Sensitivität.** Der Pin hängt
  vom Triple `(KeepAliveInterval=2s, PollingInterval=100ms,
  ReconnectBackoffStart=200ms)` ab. Ändert ein Sub-Slice einen
  dieser Werte, kann der Pin zur Cycle-Anzahl-Falsifikation flake.
  Mitigation: alle drei Werte sind in `Defaults.ForHilSimulator`
  zentral; D-07 pinnt sie explizit mit Kommentar. Reviewer-Hinweis:
  bei einem Production-Default-Schwenk in `OpcUaAdapterOptions`
  (z. B. `KeepAliveInterval` von 10s auf 30s) muss `Defaults.cs`
  beim Review mit auf den Tisch.
- **Concurrent-Source-Sink-mit-Restart-Pin könnte einen seltenen
  Session-Lock-Race aufdecken.** Das wäre ein **production**-Finding,
  kein Test-Bug — der Pin existiert genau, um diese Klasse zu
  probieren. Mitigation: wenn der Pin failt, wird ein OpcUaClient-
  Concurrency-Issue auf Sub-Slice-Ebene als Review-Fix-Commit
  ergänzt, NICHT der Pin entschärft. Die `_connectGate`-`_stateGate`-
  Interaktion in `OpcUaClient.cs` ist dann der Untersuchungsort.
- **Per-class Fixture verdoppelt Server-Startup-Zeit auf ~12s
  insgesamt.** Das ist akzeptabel im `make test-hil-opcua`-Budget
  (heute ~13s, mit M4-08-A erwartet ~25-30s). Mitigation: kein
  Mitigation-Bedarf; Trade-off ist State-Isolation gegen Startup-
  Zeit, und die Klarheit überwiegt.
- **Failover-Replay-via-Reconnect-Obligation hängt jetzt an F-09 —
  F-09 zündet möglicherweise nie.** Mitigation: F-09's Trigger ist
  konkret („erste TSO-Spec mit OPC-UA-Aktivierungsendpoint, oder
  explizite Operator-Anforderung nach Replay-Verifikation"). Das
  Item ist in `note-RM-M4-followups.md` mit eigenem `## Item F-09:`-
  Header sichtbar — keine versteckte Verschiebung. Wenn der Trigger
  nie zündet, ist der Pin auch nie operational nötig.
- **`make ci` und `make gates` werden durch `test-hil-opcua` ~25-30s
  länger** (vorher: nur unit + arch + native + test-integration;
  jetzt zusätzlich Embedded TestServer + 7 Pins). Kein Show-Stopper
  auf CI-Time-Budget; Reviewer-Hinweis: falls ein zukünftiger Slice
  weitere `make test-hil-*`-Targets in `gates`/`ci` zieht, lohnt sich
  ein Time-Budget-Audit.
- **DoD-Wortlaut-Cleanup könnte als „Aufweichen" gelesen werden.**
  Mitigation per Review-Pass-Antwort: Failover-Replay-via-Reconnect
  wandert sichtbar zu F-09 (eigenes Item, eigener Trigger), nicht
  silent unter „Aktivierungsjitter". Reviewer-Hinweis: die
  korrigierte M4-08-Zeile **muss** den F-09-Sub-Bullet namentlich
  tragen, sonst riskiert sie als Slice-Schwund interpretiert zu
  werden.

---

## 8. Sequenz

**Schritt 1: Plan reviewen (zweiter Pass).** Der erste Review-Pass hat
H1-H4 + M1-M4 + L1-L5 ergeben; der überarbeitete Plan hier adressiert
sie. Ein zweiter Review-Pass prüft (a) ob die zwei Pins gegen
existierende Tests wirklich orthogonal sind, (b) ob die F-09-Sub-
Bullet-Verlinkung in D-03 die Failover-Replay-Obligation sauber
trägt, (c) ob die `make ci`-Promotion (D-02) gegen die heutige
Container-Time-Budget-Annahme aufgeht, (d) ob die client-seitige
`_subscriptions.Count`-Assertion (D-05) tatsächlich die einzig
existierende Bug-Klasse abdeckt.

**Schritt 2: Sub-Slice RM-M4-08-A umsetzen.** Eine Implementierungs-
Phase. Reihenfolge:

1. `Defaults.ForHilSimulator` ergänzen um `KeepAliveInterval=2s`
   (D-07).
2. `OpcUaTestServerFixture.ResetNodeBaseline()`-Helper extrahieren;
   `OpcUaRoundtripTests.InitializeAsync` migrieren auf den Helper
   (Vorbereitung für Sharing).
3. `OpcUaTestServerFixture` per-class statt collection-level — die
   `[Collection]`-Annotation auf `OpcUaRoundtripTests` bleibt;
   Visibility-Check. **Regression-Guard-Comment in
   `OpcUaIntegrationCollection`** (heute leerer Marker-Type):
   ```csharp
   // M4-08-A D-06: bewusst KEIN ICollectionFixture<OpcUaTestServerFixture>
   // hier — per-Test-Class-Fixture-Instanz ist gefordert, damit
   // Multi-Cycle-Reconnect-State zwischen den Klassen isoliert bleibt.
   ```
   Verhindert silent regression durch ein-Liner-Refactor.
4. Visibility-Anpassung in `OpcUaClient.cs`: ein `internal int
   SubscriptionCount => _subscriptions.Count`-Property (einziger
   Test-Hook; Wrapper-Typ und Dictionary bleiben `private`).
5. `OpcUaNegativeTests.cs` mit den zwei Pins; pro Pin gegen `make
   test-hil-opcua` verifizieren.
6. `Makefile:gates`- UND `Makefile:ci`-Targets um `test-hil-opcua`
   erweitern (analog zu `test-native-{interop,parity}`).
7. `note-RM-M4-followups.md` Item F-09 anlegen mit Sub-Bullets +
   Trigger.
8. `docs/user/quality.md` Update.
9. Master-Plan-Zeile RM-M4-08 ✅ + DoD-Wortlaut-Cleanup; Slice-Plan
   nach `done/`.

**Schritt 3: Closure-Commit.** Pattern wie M4-04-D — ein Commit für
die Implementierung, dann externes Review (dritter Pass), dann
optional Review-Fix-Commit, dann Master-Plan-Move.

---

## 9. Folgearbeiten (gehen in `note-RM-M4-followups.md`)

**Neu von M4-08-A explizit angelegt** (existierte vorher nur als
Cross-Reference-Tag in den Slice-Plänen):

- **F-09 OPC-UA-Activation-Source-Adapter (incl. Failover-Replay-via-
  Reconnect).** Trigger: konkrete TSO-Spec mit OPC-UA-Aktivierungs-
  endpoint, oder Operator-Anforderung nach Mid-Stream-Reconnect-
  Replay-Verifikation. Sub-Bullets:
  - (a) **Source-Wire-Adapter selbst** — Implementiert `IRegelleistung-
    ActivationSource` über OPC-UA-Subscription, hebelt Aktivierungen
    auf den Use-Case-Driving-Port `IRegelleistungActivationUseCase.
    ReceiveAsync`.
  - (b) **Failover-Replay-Pin via OPC-UA-Reconnect** — Server kappt
    die Session, Source reconnectet, Server liefert die letzte
    Aktivierung erneut, der Use-Case sieht sie als Replay-Idempotent
    (gleicher `payload_hash` → `Accepted`-Outcome `Replay_Idempotent`).
    Pin-Test gegen die Embedded TestServer-Fixture aus M4-04-D mit
    dem F-09-Source als Driving-Port-Wrapper. Diese Sub-Obligation
    erbt M4-08-A's eingeklemmten Master-DoD-Begriff „Failover-Replay".
  - (c) **Aktivierungsjitter-Profile-Pins** via OPC-UA-Wire — pin-
    getestet mit verschiedenen `valid_from`/`valid_until`-Skews,
    Timestamp-Drift, und Subscription-Buffering-Reorder.

**Bestehend, unverändert** (kein neues F-Item):

- **F-13 OPC-UA-Multi-Server / Endpoint-Failover** (für Failover-
  Replay-Pins über mehrere Endpoints) — Trigger: dual-Verteilnetz-
  betreiber-Spec.
- **F-14 OPC-UA-Method-Calls / HistoricalAccess / Events** —
  Trigger: Vendor-/TSO-Spec mit Method/History-Pflicht.
- **F-15 OPC-UA-Type-System-Erweiterung** (Strukturen, Arrays, Enums)
  — Trigger: Vendor-NodeSet mit Cell-Voltage-Array oder Enum-typ.
- **F-16 Compose-Sidecar-Fallback** (falls Embedded TestServer
  nicht mehr trägt) — heute nicht zündend.
