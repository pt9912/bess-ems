# Plan RM-M4-06-FUP ExactlyOnce-Acknowledgement-Gate

Status: abgeschlossen am 2026-05-13. Dieser Follow-up-Slice schliesst
`note-RM-M4-followups.md` Item F-06.

## Ziel

MQTT QoS 2 (`ExactlyOnce`) darf nicht versehentlich aktiviert werden.
Der Adapter bleibt bei den RM-M4-06-Defaults (`AtLeastOnce` fuer
Command-Publish/ACK-Subscribe, `AtMostOnce` fuer Telemetrie) und verlangt
fuer jeden `ExactlyOnce`-Slot ein explizites Operator-Acknowledgement.

## Ergebnis

- `MqttAdapterOptions` traegt `AllowExactlyOnce` und
  `AllowExactlyOnceReason`.
- `EnsureValid(...)` prueft alle drei QoS-Slots
  (`CommandPublish`, `CommandAckSubscribe`, `TelemetrySubscribe`) und
  bricht mit `mqtt-exactly-once-not-acknowledged` ab, wenn ein Slot auf
  `ExactlyOnce` steht ohne `AllowExactlyOnce=true` plus Reason.
- `BessHostOptions` expose't bindbare QoS-Overrides fuer
  Command-Publish, ACK-Subscribe und Telemetrie sowie die
  `AllowExactlyOnce`-Acknowledgement-Felder.
- Host-Composition und Adapter-Tests pinnen Reject und Positivpfad.

## Nachweise

- `make test`

## Bewusst Draussen

- Persistentes ACK-Tracking ueber Reconnect bleibt F-03.
- MQTTv5-Properties-Adoption bleibt F-05.
