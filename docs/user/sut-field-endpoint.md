# bess-ems als SUT gegen einen externen Feld-Endpoint (MQTT)

## Zweck

bess-ems kann als System-under-Test (SUT) gegen einen **externen
Feld-Endpoint** laufen — eine Feld-/Simulationsumgebung, die den
publizierten MQTT-Feldvertrag spricht (z. B. eine externe
Simulationsplattform mit Push-Field-Publish-Surface). Die Anbindung ist
**config-only**: kein Code-Pfad, ausschließlich `Bess__*`-Env-Keys
([ADR 0013](../plan/adr/0013-device-mapping-field-contract.md) §2,
Zeile „SUT-Modus").

Dieses Dokument beschreibt den Vertrag, den der Endpoint erfüllen muss,
die vollständige Konfigurations-Tabelle, die Kadenz-Anforderung, die
Security-Posture und das Verifikations-Rezept.

## 1. Der Vertrag, den der Endpoint sprechen muss

Der Feldvertrag ist als versioniertes Release-Asset publiziert:
`bess-ems-schemas-<version>.tar.gz` (siehe
[`releasing.md`](releasing.md)) enthält

- die Geräte-Mapping-Schemas (`schema/*.schema.json`), darunter das
  **MQTT-Envelope-Schema** (`mqtt-telemetry-envelope.schema.json`) mit
  den verbindlichen Payload-Definitionen für `telemetry`, `command` und
  `command_ack`, und
- die **Golden-Vektoren** (`schema/vectors/mqtt-golden-vectors.
  {field,ems}.v1.json`) — das Abnahme-Geschirr: ein konformer Endpoint
  produziert Nachrichten, die **strukturell** (Feldnamen, Präsenz,
  Typen, Null-Weglassung — nicht Byte-Reihenfolge) zu den
  Feld-Vektoren passen.

Topic-Schema: `battery/{assetId}/telemetry` (retained) +
`battery/{assetId}/status` (retained) + `battery/{assetId}/fault`
(non-retained, nur bei Fault). bess-ems konsumiert im SUT-Modus nur
`telemetry`; `command`/`command/ack` sind für die
telemetry-read-only-Kopplung nicht erforderlich (ADR 0013 §6).
**Erwartbares Log-Bild dabei:** bess-ems publisht seine Kommandos
(auch Idle) trotzdem und wartet je Zyklus auf den Ack — ein Endpoint
ohne Command-Unterstützung erzeugt deshalb pro Zyklus eine
Warning `Command dispatch failed … ack-timeout` (EventId 1903). Das
ist für die read-only-Kopplung erwartbar und unschädlich; der
Regelzyklus läuft weiter (das Gutfall-Signal aus §5 erscheint vor dem
Dispatch).

**Kadenz:** bess-ems misst Telemetrie-Frische **beim Empfang**. Der
Endpoint muss kontinuierlich innerhalb des Freshness-Fensters
(`Bess__SnapshotMaxAge`, Default 10 s) publizieren — ein einzelner
retained Publish genügt nicht; bess-ems fällt sonst dauerhaft in den
Safety-Fallback.

## 2. Konfiguration

Aktivierungsregel: die vier Kern-Keys (`MqttMappingPath`,
`MqttBrokerHost`, `MqttBrokerPort`, `MqttClientId`) müssen **alle**
gesetzt sein, sonst bleibt der MQTT-Adapter ein NoOp und bess-ems
läuft ohne Feldanbindung weiter.

**`asset_id`-Korrespondenz (häufigster Stolperstein):** bess-ems
subscribt `battery/{assetId}/telemetry` mit der `asset_id` aus der
Asset-Config (`Bess__AssetConfigPath`, Feld `asset_id`). Publisht der
Endpoint unter einer anderen ID, empfängt bess-ems **still nichts**
und läuft in Dauer-Safe-Stop (Symptom-Unterscheidung: §5).

| Env-Key (`Bess__…`) | Pflicht | Default (unset) | Semantik |
| ------------------- | ------- | ---------------- | -------- |
| `AssetConfigPath` | ja | — | Asset-Config; deren `asset_id` muss dem `{assetId}` des Endpoints entsprechen. |
| `SnapshotMaxAge` | nein | `00:00:10` | Freshness-Fenster (`TimeSpan`, `hh:mm:ss`); wirkt in Host **und** Api. |
| `MqttMappingPath` | ja (Kern) | — | MQTT-Mapping-Profil (`schema_version: "v1"` erforderlich). |
| `MqttBrokerHost` | ja (Kern) | — | Broker-Hostname/-Adresse des Endpoints. |
| `MqttBrokerPort` | ja (Kern) | — | Broker-Port (Plaintext üblich 1883, TLS 8883). |
| `MqttClientId` | ja (Kern) | — | Client-ID von bess-ems am Broker. |
| `MqttRuntimeProfile` | nein | `Production` | `Development` \| `HilSimulator` \| `Production`. Unset ⇒ **Production, fail-closed** (verlangt TLS + Auth). |
| `MqttTlsEnabled` | nein | `false` | TLS an/aus; Pflicht in `Production`. Tiefe: [`quality.md`](quality.md) §2.2.1. |
| `MqttTlsTrustedCaCertificatePath` | bei TLS | — | CA-Bundle (kein stiller System-Root-Fallback). |
| `MqttTlsClientCertificatePath` | nein | — | Client-Zertifikat (mTLS / Auth-Variante). |
| `MqttTlsClientCertificatePassword` | nein | — | Inline-Passwort — in `Production` verboten. |
| `MqttTlsClientCertificatePasswordPath` | nein | — | Passwort per Datei-/Secret-Mount. |
| `MqttUsername` | nein | — | Broker-Auth (Variante Benutzer/Passwort). |
| `MqttPassword` | nein | — | Inline-Passwort — in `Production` verboten. |
| `MqttPasswordPath` | nein | — | Passwort per Datei-/Secret-Mount. |
| `MqttAllowPlaintext` | bei Plaintext | `false` | Plaintext-Gate; nur `Development`/`HilSimulator`. |
| `MqttAllowPlaintextReason` | bei Plaintext | — | Pflicht-Begründung zum Plaintext-Gate. |
| `MqttCommandPublishQos` | nein | `AtLeastOnce` | QoS für Command-Publish (`AtMostOnce` \| `AtLeastOnce` \| `ExactlyOnce`). |
| `MqttCommandAckSubscribeQos` | nein | `AtLeastOnce` | QoS für die Command-Ack-Subscription. |
| `MqttTelemetrySubscribeQos` | nein | `AtMostOnce` | QoS für die Telemetrie-Subscription — bei kontinuierlicher Kadenz ist Verlusttoleranz beabsichtigt; ein fremder Broker muss das angefragte QoS nur gewähren, nicht übertreffen. |
| `MqttAllowExactlyOnce` | bei QoS 2 | `false` | Gate für `ExactlyOnce` (gleiche Bauart wie das Plaintext-Gate); Details [`quality.md`](quality.md) §2.2.1. |
| `MqttAllowExactlyOnceReason` | bei QoS 2 | — | Pflicht-Begründung zum QoS-2-Gate. |

`Bess__SnapshotMaxAge` bindet nicht über die Host-Options-Klasse,
sondern direkt aus der `IConfiguration` beider Prozesse
(`BessHostBuilder`/Api-`Program`) — der Wert wirkt deshalb im vollen
Host **und** im Api-Read-Pfad.

Analoge Familien für andere Protokolle: `Bess__Modbus*`
(`ModbusMappingPath`/`ModbusHost`/`ModbusPort`) und `Bess__OpcUa*`
(siehe [`opc-ua.md`](opc-ua.md)). Dieses Dokument führt den
MQTT-Pfad aus; die Mechanik (alle Kern-Keys gesetzt ⇒ Adapter aktiv,
sonst NoOp) ist identisch.

## 3. Security-Posture

Plaintext-MQTT ist eine **Nur-Sim-Netz**-Annahme: erlaubt nur in
`Development`/`HilSimulator` und nur mit explizitem
`MqttAllowPlaintext=true` + `Reason`. `Production` ist fail-closed
(TLS + Broker-Auth Pflicht, Inline-Secrets verboten) — vollständige
Regeln in [`quality.md`](quality.md) §2.2.1 (MQTT-Security-Profil).

## 4. Compose-SUT-Variante

`deploy/compose.sut.yml` startet bess-ems (+ Postgres) **ohne** eigenen
Broker und **ohne** Feld-Simulator; die Broker-Adresse kommt per Env:

```bash
# Stand-in-Betrieb (lokal): shared external Network + Feld-Stack
make sut-smoke        # legt Netz an, fährt beide Stacks, prüft, räumt ab

# Gegen einen echten externen Endpoint: das externe Netz muss existieren
# (compose.sut.yml deklariert es als external; make sut-smoke legt es nur
# für den Stand-in-Lauf an):
docker network create bess-sut 2>/dev/null || true
BESS_SUT_BROKER_HOST=<routbare-adresse> BESS_SUT_BROKER_PORT=1883 \
  docker compose -f deploy/compose.sut.yml up
```

Im Stand-in-Betrieb koppelt ein shared external Docker-Network
(`bess-sut`) die SUT-Variante mit dem Feld-Stack
(`deploy/compose.field.yml`: mosquitto als `field-mosquitto` +
`bess-field-sim`); es wird von `make sut-smoke` angelegt und
abgeräumt. Der Smoke generiert sich ein Szenario mit **kontinuierlicher
1-s-Kadenz** (der Simulator spielt Ticks einmal ab, kein Loop) — mit dem
Standard-Integrations-Szenario, das nach Tick 0 stillhält, liefe die
SUT-Variante korrekt in Safe-Stop: genau die Kadenz-Regel aus §1. Für einen echten externen Endpoint ist die Netz-Topologie
Betreibersache — `BESS_SUT_BROKER_HOST` muss aus dem Container heraus
routbar sein.

## 5. Verifikation

1. **Health:** `GET /health` liefert 200, sobald der Host läuft —
   das sagt nichts über die Feldanbindung aus. Die SUT-Variante
   publisht bewusst keine Host-Ports; von außen also per
   `docker compose -f deploy/compose.sut.yml exec bess-ems \
   curl -fsS http://localhost:8080/health` (oder Port-Override).
2. **Gutfall-Signal:** im Log erscheint zyklisch
   `Control cycle emitted command` (EventId 1701) — der Regelzyklus
   verarbeitet frische Telemetrie und emittiert Kommandos (ohne
   gepostetes Schedule als `Idle`-Kommando; das ist der erwartete
   Leerlauf-Gutfall).
3. **Fehlerbild unterscheiden** — beide Ursachen enden im
   Safety-Fallback (`Control cycle safe-stop …`), unterscheiden sich
   aber im `decision=`-Feld:

| Log-Signal | Ursache | Prüfen |
| ---------- | ------- | ------ |
| `decision=no-snapshot` | **Nie** Telemetrie empfangen | `asset_id`↔`{assetId}`-Korrespondenz (§2), Topic-Schema, Broker-Adresse/Netz-Routbarkeit |
| `decision=snapshot-unusable` + `reason=snapshot-aged-<N>s` (N mit einer Nachkommastelle, z. B. `snapshot-aged-12.3s`) | Telemetrie **zu alt** | Publish-Kadenz des Endpoints vs. `Bess__SnapshotMaxAge` (§1 Kadenz) |

`make sut-smoke` automatisiert genau dieses Rezept gegen den
Stand-in-Stack (Grün = Gutfall-Signal binnen Frist, kein Safe-Stop
nach Warmup).

## 6. Verifikation gegen eine reale externe Feld-Umgebung — OFFEN

> **Status: offen.** Dieses Rezept ist gegen den Stand-in
> (`bess-field-sim`, der De-facto-Produzent des Vertrags) verifiziert —
> **nicht** gegen eine reale externe Feld-Umgebung. Die
> Schwester-Simulationsplattform besitzt seit kurzem eine
> Push-Field-Publish-Surface, publiziert dort aber noch ihr eigenes
> schmales Punkt-Format; ein bess-ems-konformer Publisher (breiter
> Snapshot je Tick, gegen die Golden-Vektoren abgenommen) ist dort in
> Arbeit. Sobald er liefert, wird dieser Abschnitt durch den
> E2E-Befund ersetzt (bess-ems verlässt den Safety-Fallback gegen die
> reale Surface). Bis dahin gilt: Stand-in-verifiziert.

## Bezug

- [ADR 0013](../plan/adr/0013-device-mapping-field-contract.md) —
  Feldvertrag, SUT-Modus (§2), Kadenz (§1), Golden-Vektoren (§5.2)
- [`quality.md`](quality.md) §2.2.1 — MQTT-Security-Profil
  (TLS/Auth/Plaintext-/QoS-2-Gates)
- [`releasing.md`](releasing.md) — Release-Assets inkl. Schema-Bundle
- `deploy/compose.sut.yml` / `deploy/compose.field.yml` — SUT-Variante
  und Stand-in-Feld-Stack
