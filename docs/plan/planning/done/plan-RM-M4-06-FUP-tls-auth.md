# Plan RM-M4-06-FUP TLS/Auth-Haertung fuer MQTT

Status: abgeschlossen am 2026-05-13. Dieser Follow-up-Slice schliesst
`note-RM-M4-followups.md` Item F-04.

## Ziel

Der MQTT-Adapter darf in Production nicht mehr versehentlich ueber
Plaintext-TCP oder ohne Broker-Authentisierung starten. Simulator- und
Development-Profile behalten Plaintext nur ueber ein explizites
Operator-Acknowledgement.

## Ergebnis

- `MqttRuntimeProfile` fuehrt `Production`, `HilSimulator` und
  `Development` als harte Startup-Achse ein; Default ist `Production`.
- `MqttTlsOptions` verlangt bei TLS einen konfigurierten CA-Pfad und
  validiert fehlende CA-/Client-Zertifikat-Dateien fail-closed.
- `MqttCredentialOptions` unterstuetzt Username/Password sowie
  secret-gemountete Passwortdateien; Production verbietet Inline-
  Passwoerter und Inline-Client-Cert-Passwoerter.
- `MqttAdapterOptions.EnsureValid(...)` blockiert Production-Plaintext
  mit `mqtt-security-not-hardened-in-production` und verlangt fuer
  nicht-produktives Plaintext `AllowPlaintext=true` plus Reason.
- `MqttNetClient` verdrahtet MQTTnet-TLS inklusive Custom-CA-Trust,
  Hostname-Check, optionalem Client-Zertifikat und Broker-Credentials.
- Host-, Compose- und Helm-Konfiguration reichen RuntimeProfile,
  TLS-Pfade, Secret-Pfade und Plaintext-Acknowledgement durch.
- Simulator-/Integrationstest-Konfiguration ist explizit als
  `HilSimulator` mit dokumentiertem Plaintext-Reason markiert.
- `docs/user/quality.md` dokumentiert die MQTT-Security-Gates und die
  Operator-Konfiguration fuer Production.

## Nachweise

- `make lock-refresh`
- `make test`
- `make helm-lint`
- `make test-integration`

## Bewusst Draussen

- Persistentes ACK-Tracking ueber Reconnect bleibt F-03.
- ExactlyOnce-Acknowledgement-Gating bleibt F-06.
- MQTTv5-Properties-Adoption bleibt F-05.
