# OPC Unified Architecture (OPC UA)

## Kurzüberblick
OPC UA (Open Platform Communications Unified Architecture) ist ein standardisiertes Industrieprotokoll für den sicheren Austausch von Maschinendaten.

Typische Ziele:

- Geräte- und Systemknoten mit einheitlichem Datenmodell auslesen/schreiben
- Ereignis- und Push-Updates empfangen
- Zugriff absichern (Authentisierung, Integrität, optional Verschlüsselung)

Für BESS-EMS nutzen wir OPC UA, um Batteriesteuerung und -telemetrie adapterbasiert auszulagern.

## Warum es hier relevant ist
Der OPC-UA-Adapter verbindet die Domänenports mit:

- `IBatteryTelemetrySource` (Lesen + Push-Updates)
- `IBatteryCommandSink` (Befehlswrites auf als `write`/`writable`
  gemappte Nodes)

Die zentrale Idee ist ein Mapping:

- JSON-konfiguriertes Node-Mapping von OPC-UA-Nodes auf Domänenfelder
- Richtung `read` / `subscribe` / `write`
- optionale Skalierung (`scaleFactor`) und Schreibbarkeit (`writable`)

## Gekappter Funktionsumfang
Der OPC-UA-Adapter deckt bewusst nur die Kernfunktionen ab:

- ✅ `Read` (Polling konfigurierter `read`-Nodes)
- ✅ `Subscribe` (Push über `subscribe`-Nodes)
- ✅ `Write` (Befehlswrites auf `write`/`writable` Nodes)
- ✅ `StatusCode`-Abbildung auf interne Qualitätsinformationen

Der Adapter und der kombinierte Host können den Write-Pfad registrieren,
wenn `Bess:OpcUaMappingPath` und `Bess:OpcUaEndpointUrl` gesetzt sind.
Die aktuellen Referenz-Compose-Stacks aktivieren OPC-UA jedoch nicht als
produktiven Compose-Pfad; sie bleiben auf die bestehenden Simulator-/
Mosquitto-Pfade ausgelegt. Kurz: **Adapter kann schreiben, produktives
Compose-Wiring für OPC-UA ist nicht der aktive Referenzpfad.**

Nicht Gegenstand dieses Adapter-Scope:

- Methodenaufrufe (Methods)
- Historische Werte (History)
- Events/Alarme
- Server-seitige Security-Härtung (separater Slice [RM-M4-05](../plan/planning/done/plan-RM-M4-05.md))

## Begriffe (praktisch)
- **NodeId**: Eindeutiger OPC-UA-Node-Pfad für einen Wert (z. B. SOC, Power, Temperature).
- **Subscription**: Server-seitiger Update-Kanal mit konfigurierter Publish-Rate.
- **MonitoredItem**: Abonniertes einzelnes Feld innerhalb einer Subscription mit eigenem Sampling-Intervall.
- **StatusCode**: Ergebniscode jeder OPC-UA-Operation.
- **DataQuality**: Interner Qualitätsstatus im Domänenmodell für `BatteryTelemetry`.

## StatusCode → DataQuality (konkret)
Im Adapter wird der OPC-UA-StatusCode direkt als Qualitätssignal weitergereicht:

- `Good` → `DataQuality.Valid`
- `Uncertain` → `DataQuality.Stale("opcua-uncertain-<status-name>")`
- `Bad` → `DataQuality.ProtocolError("opcua-bad-<status-name>")`

Pro Telemetrie-Sample gilt: eine schlechte Qualität dominiert.  
Ein einzelner `Bad`-Status macht die Probe ungültig (`ProtocolError`), bei `Uncertain` ohne `Bad` wird `Stale` verwendet.

## Sicherheit in diesem Scope
Seit [RM-M4-05](../plan/planning/done/plan-RM-M4-05.md) ist OPC-UA production-fail-closed gehärtet:

- Default ist `RuntimeProfile=Production` mit
  `SecurityMode=SignAndEncrypt` und allowlisteter Security-Policy.
- `SecurityMode=None` ist nur für `HilSimulator`/`Development` mit
  explizitem `OpcUaAllowUnsecured=true` plus
  `OpcUaAllowUnsecuredReason` zulässig.
- Die Host-Konfiguration reicht die `Bess:OpcUa*`-Security-Felder in
  `OpcUaAdapterOptions` durch; eine unsichere Production-Konfiguration
  schlägt beim Start bewusst fehl.

## Weiterführend
- Mapping-Konzept: `OpcUaMappingConfiguration` (aus der Mapping-Konfigurationsarbeit)

## Links

- [OPC Foundation GitHub-Organisation](https://github.com/OPCFoundation)
- [OPC Foundation (offizielle Website)](https://opcfoundation.org/)
- [OPC Unified Architecture (Spezifikationsportal)](https://reference.opcfoundation.org/)
- [OPC Foundation OPC-UA .NET Standard SDK (GitHub)](https://github.com/OPCFoundation/UA-.NETStandard)
