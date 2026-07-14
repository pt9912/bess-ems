# Next

Plan- und Slice-Notizen für **konkret geplante, aber noch nicht
aktive** Arbeit nach `v1.0.0`. Abgrenzung zu den anderen
`planning/`-Unterverzeichnissen:

| Verzeichnis    | Inhalt                                                                  |
| -------------- | ----------------------------------------------------------------------- |
| `next/`        | Geplante v1.x/v2.x-Arbeit mit Scope-Skizze, aber kein laufender Slice.  |
| `in-progress/` | Lebende Roadmap und aktive Slice-Pläne, an denen gearbeitet wird.       |
| `open/`        | Trigger-Watch-Notizen (Follow-up-Items, warten auf konkreten Anlass).   |
| `done/`        | Abgeschlossene Slices und Meilensteinpläne (eingefroren, nur Referenz). |

Ein Eintrag wechselt typischerweise:
`open/` (Trigger entsteht) → `next/` (Scope skizziert) → `in-progress/`
(Slice-Plan aktiv) → `done/` (geliefert).

## Bestand

| Datei | Inhalt | Aktivierung |
| ----- | ------ | ----------- |
| [`note-internal-refinement-scope.md`](note-internal-refinement-scope.md) | Internal-Refinement-Scope (Lock-Eviction + Cluster-Smoke), nach zweifacher Nachnummerierung versions-agnostisch benannt — Ziel: nächste freie Minor | Scope-Bestätigung, sobald keine Feldvertrags-Arbeit vorbeizieht |

*([ADR 0013 §5.3](../../adr/0013-device-mapping-field-contract.md#5-umsetzung-reihenfolge) (`plan-field-contract-sut-docs.md`) ist am 2026-07-13
nach dreifachem Owner-Review promotet und am selben Tag nach `done/` abgeschlossen;
[ADR 0013 §5.4](../../adr/0013-device-mapping-field-contract.md#5-umsetzung-reihenfolge) (`plan-field-contract-modbus-vectors.md`) ist am 2026-07-13
aktiviert und am selben Tag nach `done/` abgeschlossen — [ADR 0013 §5](../../adr/0013-device-mapping-field-contract.md#5-umsetzung-reihenfolge) ist
damit vollständig.)*
