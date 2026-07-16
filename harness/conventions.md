# Harness-Konventionen

Repo-lokale Strukturregeln und Adaptionen von bess-ems gegenüber der adoptierten
Baseline (AI-Harness-Betriebsregelwerk). Bei Konflikt zwischen dieser Datei und
einer kanonischen Quelle gilt die kanonische Quelle (Source Precedence, siehe
[`README.md`](README.md)). Diese Datei ist konformitätsbringend für *Form*-Fragen,
nicht autoritativ über Inhalt.

## Purpose

Default-Ort für: Adaptionen ggü. der Baseline (mit Begründung und
Auflösungs-Trigger), die ID-Schema-Deklaration dieses Repos, Zusatzklassen für die
Sensors-Bindung und die Modus-Deklaration pro Sub-Area.

## Baseline

- **Konvention:** AI-Harness-Betriebsregelwerk (`ai-harness-course`)
- **Stand:** v1.4.0 (Regelwerk- und Template-Set)
- **Datum der Adoption:** 2026-07-16 ([ADR 0014](../docs/plan/adr/0014-operations-baseline-adoption.md))

Der Tag in der `**Stand:**`-Zeile ist die Single Source of Truth für
`tools/harness/fetch-baseline-cache.sh` (Materialisierung/Verifikation des
vendored Regelwerks).

## Adoptierte Konventions-Quellen

Pointer, keine Wiederholung des Inhalts.

- **Extern (Lehrmaterial):** `github.com/pt9912/ai-harness-course`, Tag `v1.4.0`.
- **In-Repo (committet vendored, `MR-004`):** das nach Modulen und
  Grundlagen-Abschnitten aufgeteilte Regelwerk liegt netzlos unter
  `.harness/baseline/v1.4.0/regelwerk/` (dortige `README.md` = Index), samt
  `SHA256SUMS`-Integritätsmanifest; offline verifizierbar via
  `tools/harness/fetch-baseline-cache.sh --verify`.

## Adaptions-Block

ADR-artige Liste der Abweichungen ggü. Baseline. Chronologisch nummeriert, keine
nachträglichen inhaltlichen Änderungen an akzeptierten Einträgen — nur neue
Einträge oder explizite Aufhebungen via neuem `MR`.

### MR-000 — Baseline-Aussage

- **Datum:** 2026-07-16
- **Geltungsbereich:** gesamtes Repo
- **Adaption:** Adoption der Baseline v1.4.0. Der Baseline-Default für
  Verzeichniskonvention, Lifecycle-Regeln und Carveout-Disziplin gilt; das
  ID-Schema weicht ab (siehe `MR-002`/`MR-003`). Verwendete ID-Familien:
  `LH-<KAT>-<NN>` (Anforderungen), `ADR NNNN` (Leerzeichen-Form, chronologisch),
  `RM-M<n>-<NN>` (Bestandsplanung), `MR-<NNN>` (diese Adaptionen).
- **Begründung:** Initial-Setzung. Spätere Adaptionen werden als `MR-<NNN>`
  nachgetragen.
- **Auflösungs-Trigger:** permanent.

### MR-001 — Doc-Gate auf eigener, fortgeschrittener d-check-Basis

- **Datum:** 2026-07-16
- **Geltungsbereich:** `.d-check.yml`, `d-check.mk`, `make docs-check`
- **Adaption:** bess-ems nutzt ein eigenes, tool-generiertes `d-check.mk`
  (d-check v0.42.0, `--network none`) mit vollem Modulsatz
  (`links, anchors, hostpaths, spans, codepaths, matrix, ids`) plus die
  git-basierten Range-Targets — statt des Baseline-`harness.mk` (ältere
  d-check-Version, simpler Modulsatz). Die `matrix`-Layering-Regeln
  (`spec↛adr/plan/outside`, `adr↛plan`), die Kennungs-Linkpflicht (`ids`) und die
  Inline-Pfad-Prüfung (`codepaths`) sind Schärfungen über den Baseline-Default
  hinaus.
- **Begründung:** Der Doc-Gate ist organisch über den Baseline-Stand hinaus
  gereift; ein Rückbau auf den simpleren Baseline-`harness.mk` wäre eine
  Verschlechterung.
- **Auflösungs-Trigger:** permanent (Bump folgt der d-check-Release-Linie).

### MR-002 — Kategoriebasiertes Anforderungs-ID-Schema

- **Datum:** 2026-07-16
- **Geltungsbereich:** `spec/lastenheft.md`, alle aufwärts referenzierenden Artefakte
- **Adaption:** Anforderungen tragen `LH-<KAT>-<NN>` (KAT ∈ API, ARCH, CONF, CTRL,
  DEPLOY, DOM, MKT, MODB, MON, MQTT, NF, OPS, OPT, PERSIST, PROT, RISK, RT, SAFE,
  SM, TEST, ZIEL, …) statt des Baseline-`LH-FA-<NN>`/`LH-QA-<NN>`.
- **Begründung:** Das gewachsene Lastenheft ist kategorial gegliedert; die Kategorie
  im Kürzel trägt Trace-Information, die `FA/QA` nicht hat.
- **Auflösungs-Trigger:** permanent.

### MR-003 — Bestand meilenstein-gebunden; Welle/Slice-Lifecycle vorwärts

- **Datum:** 2026-07-16
- **Geltungsbereich:** `docs/plan/planning/`
- **Adaption:** Der Bestand plant meilenstein-gebunden (`M<n>` mit unter dem
  Meilenstein eingebetteten Liefergegenständen `RM-M<n>-<NN>`), nicht als
  Welle/Slice-Lifecycle. Der Baseline-Lifecycle (`open/next/in-progress/done`,
  Slices als eigene Dateien, Wellen als Roadmap-Bündel, Meilenstein *neben* der
  Welle) wird **vorwärts** adoptiert; der bestehende Planungskorpus bleibt
  grandfathered.
- **Begründung:** Retroaktiver Umbau des reifen, abgeschlossenen Planungsbestands
  hätte hohes Risiko bei geringem Sofortnutzen (alle M1–M6 sind `done`).
- **Auflösungs-Trigger:** [ADR 0014](../docs/plan/adr/0014-operations-baseline-adoption.md)
  §8 — die erste neue, post-Adoption aktiv geplante Arbeitswelle.

### MR-004 — Regelwerk-Lese-Form committet vendored, ohne Enforcement-Hooks

- **Datum:** 2026-07-16
- **Geltungsbereich:** `.harness/baseline/`, `tools/harness/fetch-baseline-cache.sh`,
  `AGENTS.md` §1, `.d-check.yml` (`scan.ignore`), `.gitignore`
- **Adaption:** Das Regelwerk wird committet-vendored gelesen
  (`.harness/baseline/v1.4.0/regelwerk/` + `SHA256SUMS`, netzlos, offline
  verifizierbar) statt pro Session aus dem Remote-ZIP. „Ein Modul pro Session
  lesen" ist Prosa-Regel in `AGENTS.md`; es gibt bewusst **keine** `.claude`-Hooks
  (SessionStart-Injektor / Command-Guard) und kein `harness.mk`.
- **Begründung:** Netzloser, integritätsgeprüfter Bestand auf jedem Checkout;
  Parität mit dem erprobten committet-vendored-Muster. Automatisierte
  Injektion/Guarding wäre net-new und ein eigener ADR.
- **Auflösungs-Trigger:** Enforcement-Hooks bei Bedarf via eigenem ADR
  ([ADR 0014](../docs/plan/adr/0014-operations-baseline-adoption.md) §8);
  Vendoring permanent.

### MR-005 — Source Precedence mit eigener Spezifikations-Schicht (3 Straten)

- **Datum:** 2026-07-16
- **Geltungsbereich:** `spec/`, Source Precedence (`AGENTS.md`, `harness/README.md`), `.d-check.yml` matrix `spec`-Klasse
- **Adaption:** Einführung des optionalen **Technik-Stratums** `spec/spezifikation.md` als eigener **Rang 2** zwischen Lastenheft (Vertrag, Rang 1) und Architektur (Sicht, Rang 3) — Erweiterung des in `MR-000` gehaltenen 2-Straten-Defaults (nur Vertrag + Sicht obligatorisch, Technik optional) um das Technik-Stratum. Die technischen Festlegungen (State Machine, Fail-Closed, Persistenz, Konfiguration, Native-Core inkl. ABI-Regeln) sind aus `architecture.md` (Sicht) extrahiert; die Architektur trägt Pointer-Stubs (Sicht → Technik, aufwärts). `.d-check.yml` matrix `spec`-Klasse: `order = [lastenheft, spezifikation, architecture]`, `no-downward`.
- **Begründung:** genug Technik-Substanz (Algorithmen, Defaults, Protokolle, ABI), die weder in den Vertrag (Anforderungen) noch in die Sicht (Diagramme) sauber passt. ADRs schärfen jetzt die Spezifikation (nicht das Lastenheft, nie die Architektur-Sicht).
- **ID-Schema:** Schärfung einer Anforderung trägt `LH-<KAT>-<NN>.<a>` (Buchstaben-Suffix an der geschärften Anforderung).
- **Auflösungs-Trigger:** permanent.

## Zusatzklassen-Deklaration für Sensors-Bindung

Über die vier kanonischen Bindung-Klassen (ADR, Carveout, Kalibrierung,
Reproduzierbarkeit) hinaus verwendet bess-ems:

| Klasse | Form | Bedeutung | Beispiel |
|---|---|---|---|
| Anforderungs-Bindung | `LH-<KAT>-<NN>` | Gate setzt eine bestimmte Lastenheft-Anforderung durch | Safety-Tests binden die Safety-Anforderungen |
| Feldvertrag-Bindung | ADR-referenziert | Golden-Vector-/SUT-Gates setzen den publizierten Feldvertrag durch | `make field-vectors-check` |

## Modus-Deklaration pro Sub-Area

| Sub-Area (Pfad / Modul) | Modus | Begründung | Graduation-Bedingung |
|---|---|---|---|
| `*` (Default gesamtes Repo) | Greenfield | Reifer, doc-first Bestand: `spec/` und ADRs führen, Architektur-Boundary-Tests und Gates erzwingen Konformität (Steady State) | n/a (GF) |
