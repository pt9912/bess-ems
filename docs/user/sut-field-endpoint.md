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
ohne Command-Unterstützung erzeugt deshalb pro Zyklus eine Warning
`Command-sink dispatch failed asset_id=… reason=ack-timeout`
(EventId 1903). Das ist für die read-only-Kopplung erwartbar und
unschädlich; der Regelzyklus läuft weiter (das Gutfall-Signal aus §5
erscheint vor dem Dispatch).

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
`bess-field-sim`); `make sut-smoke` legt es an, wenn es fehlt, und
räumt es nur dann ab, wenn dieser Lauf es angelegt hat — ein
vorbestehendes Netz wird mit Warnung weiterverwendet und belassen.
Der Feld-Stack spielt das committete Fixture
`simulators/bess-field-sim/testdata/scenarios/sut-smoke-cadence.json`
mit **kontinuierlicher 1-s-Kadenz** ab (der Simulator spielt Ticks
einmal ab, kein Loop) — mit dem Standard-Integrations-Szenario, das
nach Tick 0 stillhält, liefe die SUT-Variante korrekt in Safe-Stop:
genau die Kadenz-Regel aus §1. Für einen echten externen Endpoint ist die Netz-Topologie
Betreibersache — `BESS_SUT_BROKER_HOST` muss aus dem Container heraus
routbar sein.

## 5. Verifikation

1. **Health:** `GET /health` liefert 200, wenn **alle** registrierten
   Komponenten healthy sind, und 503, sobald eine kritische Komponente
   (z. B. Postgres) unhealthy ist — ein 503 heißt also nicht „Host
   läuft nicht". Über die Feldanbindung sagt der Endpoint nichts aus.
   Die SUT-Variante publisht bewusst keine Host-Ports; von außen also
   per `docker compose -f deploy/compose.sut.yml exec bess-ems \
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
Stand-in-Stack. Grün heißt: das Gutfall-Signal (JSON-Anker
`"EventId":1701`) erscheint binnen Frist (Default 90 s), und ab dem
Signal kommt im 20-s-Beobachtungsfenster **keine neue**
Safe-Stop-Zeile (`"EventId":1702`) hinzu — Anlauf-Safe-Stops **vor**
dem ersten Gutfall-Signal sind erwartbar (der Zyklus läuft, bevor die
erste Telemetrie eintrifft) und zählen nicht.

## 6. Verifikation gegen eine reale externe Feld-Umgebung — ERFÜLLT

> **Status: verifiziert (2026-07-13).** Die Schwester-Simulationsplattform
> liefert seit ihrem jüngsten Release einen bess-ems-konformen
> Feld-Publisher (breiter 10-Feld-Envelope je Tick, gegen das
> publizierte Schema-Bundle und die Golden-Vektoren dieses Vertrags
> abgenommen; Vorzeichen-Konvention und ID-Mapping dokumentiert dort).
> Der E2E wurde **beidseitig** gefahren: die Gegenseite nahm das
> offizielle, unveränderte bess-ems-Image in ihrem Abnahme-Stack ab,
> und von dieser Seite lief `deploy/compose.sut.yml` **unverändert**
> (Default `BESS_SUT_BROKER_HOST=field-mosquitto`) gegen das
> digest-gepinnte, publizierte Image der Gegenseite über das in §4
> beschriebene external Network: Gutfall-Signal (`"EventId":1701`)
> binnen Frist, genau ein Anlauf-Safe-Stop und im Beobachtungsfenster
> **kein** neuer (`"EventId":1702`-Baseline stabil), Draht-Frames als
> exakter 10-Feld-Envelope inklusive Fault-Semantik
> (`fault_status`/`available` aus der Fault-Surface der Gegenseite).
> Die Gegenseite beantwortet zusätzlich `command` mit einem
> Always-Accept-`command/ack`-Echo — der Warnstrom aus §1
> (EventId 1903) entfällt dort sogar; der Sollwert-**Effekt** bleibt
> weiterhin dem Modbus-Pfad bzw. dem deferred Command-Closed-Loop
> vorbehalten (ADR 0013 §6).

Der Stand-in-Smoke (`make sut-smoke`) bleibt als repo-internes,
fortlaufendes Gate in `fullbuild` bestehen — er braucht keine externe
Komponente.

## Bezug

- [ADR 0013](../plan/adr/0013-device-mapping-field-contract.md) —
  Feldvertrag, SUT-Modus (§2), Kadenz (§1), Golden-Vektoren (§5.2)
- [`quality.md`](quality.md) §2.2.1 — MQTT-Security-Profil
  (TLS/Auth/Plaintext-/QoS-2-Gates)
- [`releasing.md`](releasing.md) — Release-Assets inkl. Schema-Bundle
- `deploy/compose.sut.yml` / `deploy/compose.field.yml` — SUT-Variante
  und Stand-in-Feld-Stack
