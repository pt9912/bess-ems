# AGENTS.md — Briefing für AI-Coding-Agenten

## 1. Was diese Datei ist

Onboarding-Briefing für jede AI-Session, die in bess-ems Code oder Dokumentation
ändert. Sie trägt die Hard Rules und verweist auf die kanonischen Quellen — sie
dupliziert deren Inhalt nicht.

**Bei Konflikt zwischen dieser Datei und einer kanonischen Quelle gilt die
kanonische Quelle** (Source Precedence, siehe [`harness/README.md`](harness/README.md)).

Repo-lokale Strukturregeln (ID-Schema, Verzeichniskonvention, Adaptionen ggü.
Baseline, Modus-Deklarationen) leben in
[`harness/conventions.md`](harness/conventions.md).

Das **Betriebsregelwerk der adoptierten Baseline** ist **committet vendored**
(`MR-004`): das nach Modulen und Grundlagen-Abschnitten aufgeteilte Regelwerk
liegt entpackt unter `.harness/baseline/v1.4.0/regelwerk/` (dortige `README.md`
ist der Index), samt `SHA256SUMS`-Integritätsmanifest — netzlos auf jedem
Checkout präsent, offline verifizierbar via
`tools/harness/fetch-baseline-cache.sh --verify`. Pro Session **nur den
task-relevanten Abschnitt** lesen, bevor der Workflow (§6) startet — nicht das
gesamte Regelwerk im Kontext halten. Derivativ: bei Konflikt gelten die
kanonischen Quellen; adoptierter Stand in [`harness/conventions.md`](harness/conventions.md)
§Baseline. Grund und Umfang der Adoption:
[ADR 0014](docs/plan/adr/0014-operations-baseline-adoption.md).

## 2. Kanonische Quellen (Source Precedence)

1. [`spec/lastenheft.md`](spec/lastenheft.md) — vertraglich abnahmebindend.
2. [`spec/spezifikation.md`](spec/spezifikation.md) — technisch verbindlich, fortschreibbar (Technik; ADRs dürfen schärfen).
3. [`spec/architecture.md`](spec/architecture.md) — Komponenten-/Sequenzsicht.
4. [`docs/plan/adr/`](docs/plan/adr/) — Architektur-Entscheidungen.
5. [`docs/plan/planning/in-progress/roadmap.md`](docs/plan/planning/in-progress/roadmap.md)
   — Meilensteine / aktuelle Planung.
6. [`README.md`](README.md) — Projekt-Überblick.
7. **AGENTS.md (diese Datei).**
8. [`harness/README.md`](harness/README.md) — Harness-Einstieg.

## 3. Harte Regeln

### 3.1 Docker-only über `make`

Kein lokales SDK/Toolchain-Install (.NET, C-Toolchain, Go). Alles läuft über
`make` (das Docker nutzt). Host braucht nur Docker und GNU `make`.
**Begründung:** Toolchain-Reproduzierbarkeit + Supply-Chain-Defense.

### 3.2 Suppression-Verbot

Inline-Suppression bricht das jeweilige Gate: C# `#pragma warning disable` /
`[SuppressMessage]`, Go `//nolint`, native GCOVR-Exclusion-Marker. Ausnahmen leben
zentral in der jeweiligen Konfiguration mit Begründung, nie inline ad hoc.

### 3.3 `git mv` + Inhaltsänderung = zwei Commits

Erst reiner Move (`git mv`, eigener Commit), dann Inhalt umschreiben. Sonst fällt
die Rename-Detection unter die Similarity-Schwelle und `git log --follow` wird
unzuverlässig.

### 3.4 Architektur ist sprach- und meilensteinfrei

`spec/architecture.md` referenziert ADRs und Modul-Pfade, aber **keine** Wellen,
Slices, Commit-Hashes oder Closure-Daten. Die zeitliche Schicht lebt in
`docs/plan/planning/`.

### 3.5 ADRs sind nach `Accepted` immutable

Eine ADR mit Status `Accepted` wird nicht inhaltlich überschrieben. Korrekturen
entstehen als neue ADR mit `Supersedes ADR-NNNN`.

### 3.6 Gates dürfen nicht ohne ADR gelockert werden

Jede Schwellen-Senkung (Coverage, Linter-Strenge, Architekturregel) ist ein ADR,
kein PR-Kommentar.

### 3.7 Sicherheit ist fail-closed

Der Optimierer schreibt **nie** direkt aufs Gerät; die Schreibbegrenzung sitzt im
Adapter unmittelbar vor dem Versand; bei ungültigem/veraltetem Snapshot greift der
sichere Fallback. Production-Profile sind fail-closed.

### 3.8 Adapter tragen keine Domänen-/Marktentscheidungen

Protokoll-Adapter (Modbus, MQTT, OPC-UA) enthalten keine Markt- oder
Regelentscheidungen — die hexagonale Trennung ist über `make arch-check`
erzwungen.

## 4. Quality Gates

Nur Targets, die im Makefile existieren.

| Target | Zweck |
|---|---|
| `make lint` | statische Analyse / Format |
| `make arch-check` | hexagonale Grenzen (Boundary-Tests) |
| `make test` | Unit-/Integrations-Testsuite |
| `make test-safety` | Safety-Pfad-Subset |
| `make coverage-gate` | Line-Coverage-Gate (90 %) |
| `make docs-check` | Doku-Referenz-Gate (d-check) |
| `make gates` | alle mandatorischen Gates (M1 + M3 native) |
| `make ci` | CI-Äquivalent inkl. Schema + Integration |
| `make fullbuild` | volle Closure (`ci` + `build` + `runtime`) |

## 5. Dokumentations-Regeln

- Anforderungs- und ADR-IDs werden in PRs/Commits referenziert, nicht ad hoc
  erfunden. Das ID-Schema ist in [`harness/conventions.md`](harness/conventions.md)
  deklariert (`LH-<KAT>-<NN>` aus dem Lastenheft; ADR-Nummern chronologisch).
- Neue ADRs entstehen aus dem ADR-Template; es gibt keinen separaten ADR-Index.
- Roadmap/Status-Geschichte lebt in `docs/plan/planning/`, nicht in
  `spec/architecture.md`.
- Quality-Gate-Definitionen leben in [`docs/user/quality.md`](docs/user/quality.md).

## 6. Minimal Agent Workflow

Pro Slice/Änderung:

1. [`harness/README.md`](harness/README.md) lesen.
2. Relevante kanonische Quelle lesen (Source Precedence beachten).
3. Task-relevantes Regelwerk-Modul lesen (`.harness/baseline/v1.4.0/regelwerk/`).
4. Betroffene Anforderungs-/ADR-IDs identifizieren.
5. Kleinste sinnvolle Änderung planen.
6. Engsten nützlichen Sensor laufen lassen.
7. Repo-weiten Gate-Lauf vor Handoff (`make gates`).
8. Doku/Indizes aktualisieren, falls ein öffentlicher Vertrag berührt ist.
9. Ausgeführte Sensors und verbleibende Risiken berichten — keine Erfolgsmeldung
   ohne Gate-Ausführung.
