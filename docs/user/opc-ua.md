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
- `IBatteryTelemetrySource` (nur Lese-/Abonnement-Pfad; `IBatteryCommandSink` ist erst nach vollständiger OPC-UA-Write-Integration aktiv)

Die zentrale Idee ist ein Mapping:

- JSON-konfiguriertes Node-Mapping von OPC-UA-Nodes auf Domänenfelder
- Richtung `read` / `subscribe` / `write`
- optionale Skalierung (`scaleFactor`) und Schreibbarkeit (`writable`)

## Gekappter Funktionsumfang
Der OPC-UA-Adapter deckt bewusst nur die Kernfunktionen ab:

- ✅ `Read` (Polling konfigurierter `read`-Nodes)
- ✅ `Subscribe` (Push über `subscribe`-Nodes)
- ⏳ `Write` (Befehlswrites auf `write`/`writable` Nodes) – im aktuellen Host-Compose-Wiring noch nicht verdrahtet
- ✅ `StatusCode`-Abbildung auf interne Qualitätsinformationen

Nicht Gegenstand dieses Adapter-Scope:

- Methodenaufrufe (Methods)
- Historische Werte (History)
- Events/Alarme
- Server-seitige Security-Härtung (separater Slice RM-M4-05)

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
Im aktuellen Adapter-Scope ist das Basisverhalten funktional, aber explizit nicht die End-to-end-Härtung.

- `SecurityMode=None` ist als Basismodell im Slice vorgesehen (Simulator-/Test-Kontext).
  Der dazugehörige Startup-Guard steckt in `OpcUaAdapterOptions`; die aktuellen Host-Optionen (`Bess`-Konfig) besitzen noch keine eigenen `OpcUa*`-Felder zum direkten Weiterreichen.

- Während direkter Konstruktion des `OpcUaAdapterOptions` verlangt der Guard bei ungesicherter Verbindung ein explizites Opt-in:
  - `AllowUnsecured = true`
  - `AllowUnsecuredReason` nicht leer
- Ohne dieses Opt-in schlägt die Konfiguration bewusst fehl (`opcua-security-not-hardened`), statt still weiterzulaufen.
- Vollständige Security-Härtung (Policies, Zertifikate etc.) erfolgt in RM-M4-05.

## Weiterführend
- Mapping-Konzept: `OpcUaMappingConfiguration` (aus der Mapping-Konfigurationsarbeit)

## Links

- [OPC Foundation GitHub-Organisation](https://github.com/OPCFoundation)
- [OPC Foundation (offizielle Website)](https://opcfoundation.org/)
- [OPC Unified Architecture (Spezifikationsportal)](https://reference.opcfoundation.org/)
- [OPC Foundation OPC-UA .NET Standard SDK (GitHub)](https://github.com/OPCFoundation/UA-.NETStandard)
