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
| [`plan-field-contract-sut-docs.md`](plan-field-contract-sut-docs.md) | ADR 0013 §5.3 — SUT-Doku + config-only-Pfad + Compose-SUT-Variante (zweifach owner-reviewt, Befunde eingearbeitet) | Owner-Go nach Plan-Review |
| [`note-v2.2.0-scope.md`](note-v2.2.0-scope.md) | Internal-Refinement-Scope (Lock-Eviction + Cluster-Smoke), zweifach umgewidmet (v1.1.0 → v2.1.0 → v2.2.0) | Scope-Bestätigung, sobald keine Feldvertrags-Arbeit vorbeizieht |
