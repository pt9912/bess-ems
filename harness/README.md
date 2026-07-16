# Harness — Repo-Einstieg

Einstiegspunkt für AI-Coding-Sessions in bess-ems: Source Precedence, Guides,
Sensors (Feedback-Gates) und Safety. Die maschinenlesbaren Hard Rules stehen in
[`../AGENTS.md`](../AGENTS.md); repo-lokale Strukturregeln und Adaptionen in
[`conventions.md`](conventions.md).

## Purpose

bess-ems ist ein sicherheitsgerichtetes Batterie-EMS. Dieser Harness macht den
Prozess-Kanon explizit: welche Quelle bei Konflikt gewinnt, welche Gates den
Vertrag durchsetzen, und wo die adoptierte Baseline liegt.

## Source Precedence

Bei Konflikt gilt die höherrangige Quelle:

1. [`../spec/lastenheft.md`](../spec/lastenheft.md) — vertraglich abnahmebindend.
2. [`../spec/spezifikation.md`](../spec/spezifikation.md) — technisch verbindlich,
   fortschreibbar (Technik-Stratum); ADRs dürfen es schärfen.
3. [`../spec/architecture.md`](../spec/architecture.md) — Komponenten- und
   Sequenzsicht, sprach- und meilensteinfrei.
4. [`../docs/plan/adr/`](../docs/plan/adr/) — Architektur-Entscheidungen (ADRs).
5. [`../docs/plan/planning/in-progress/roadmap.md`](../docs/plan/planning/in-progress/roadmap.md)
   — Meilensteine / aktuelle Planung.
6. [`../README.md`](../README.md) — Projekt-Überblick.
7. [`../AGENTS.md`](../AGENTS.md) — Hard Rules und Pointer.
8. Diese Datei (`harness/README.md`).

## Guides

| Quelle | Inhalt |
|---|---|
| [`conventions.md`](conventions.md) | repo-lokale Strukturregeln, Adaptions-Block (`MR-*`), Modus-Deklaration |
| [Betriebsregelwerk v1.4.0 (committet vendored)](../.harness/baseline/v1.4.0/regelwerk/README.md) | adoptiertes Betriebsregelwerk, ein Modul pro Session; netzlos unter `.harness/baseline/v1.4.0/regelwerk/`, offline verifizierbar via `tools/harness/fetch-baseline-cache.sh --verify` (`MR-004`); derivativ — bei Konflikt gilt die kanonische Quelle, Stand siehe [`conventions.md`](conventions.md) §Baseline |
| [`../docs/user/quality.md`](../docs/user/quality.md) | Definitionen der Quality-Gates (Stages, Schwellen) |

## Sensors (Feedback-Gates)

Nur real existierende `make`-Targets. Ein behaupteter, aber nicht verdrahteter
Gate wäre eine Harness-Lüge.

| Target | Vertrag | Bindung |
|---|---|---|
| `make docs-check` | Doku-Referenzen: Links/Anker/Pfade/IDs, ADR-Layering, Getrackt-Status (d-check `links/anchors/hostpaths/spans/codepaths/matrix/ids` + `tracked`) | `MR-001` |
| `make lint` | Statische Analyse / Format | — |
| `make arch-check` | Hexagonale Grenzen: Domain frameworkfrei, Application kein Adapter, Adapter zitieren keine anderen Adapter | Architektur §4.2 |
| `make test` | Unit-/Integrations-Testsuite | — |
| `make test-safety` | Safety-Pfad-Subset (Category=Safety) | Anforderungs-Bindung (Safety) |
| `make coverage-gate` | Line-Coverage-Gate (90 % pro M1-Production-Assembly) | Kalibrierung |
| `make native-coverage-gate` | 100 % Line-Coverage des nativen Control-Core | Reproduzierbarkeit |
| `make field-vectors-check` | Golden-Vector-Drift gegen den publizierten Feldvertrag | Feldvertrag-Bindung |
| `make gates` | bündelt die mandatorischen Gates (M1 + M3 native) | — |
| `make ci` | sequenzieller CI-Lauf aller mandatorischen Gates inkl. Schema + Integration | — |
| `make fullbuild` | volle Closure: `ci` + `build` + `runtime` | — |

## Safety

bess-ems ist sicherheitsgerichtet. Die Safety-Funktion ist die Schreibbegrenzung
unmittelbar vor der Feldkommunikation und der Fail-Closed-Fallback bei
ungültigem/veraltetem Snapshot. Production-Profile sind fail-closed. Details:
[`../AGENTS.md`](../AGENTS.md) §3 (Hard Rules) und
[`../spec/architecture.md`](../spec/architecture.md).

## Minimal Agent Workflow

Pro Änderung: (1) diese Datei + die relevante kanonische Quelle lesen; (2) das
task-relevante Regelwerk-Modul lesen; (3) betroffene IDs identifizieren; (4)
kleinste sinnvolle Änderung; (5) engsten Sensor laufen lassen; (6) `make gates`
vor Handoff; (7) Doku/Indizes aktualisieren, falls ein öffentlicher Vertrag
berührt ist; (8) ausgeführte Sensors und Restrisiken berichten — keine
Erfolgsmeldung ohne Gate-Ausführung.
