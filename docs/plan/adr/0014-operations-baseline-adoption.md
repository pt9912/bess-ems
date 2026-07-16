# ADR 0014 — Adoption des AI-Harness-Betriebsregelwerks (Baseline v1.4.0, committed-vendored)

**Status:** Accepted — Owner-Sign-off 2026-07-16. bess-ems
adoptiert ein extern gepflegtes, versioniertes **Betriebsregelwerk** (Prozess-Kanon:
Planning-Lifecycle mit beobachtbaren Triggern, Slice-Größendisziplin, Carveout-Mechanik,
Welle-≠-Meilenstein-Trennung, Review-/Closure-Harness, Adaptions-Block) als
**committed-vendored** Baseline. Der Einbau ist **additiv** — die bereits schärfere
bess-ems-Gate- und Doku-Infrastruktur bleibt maßgeblich; Abweichungen von den
Baseline-Defaults werden als nummerierte `MR-*`-Einträge in `harness/conventions.md`
geführt. Der retroaktive Umbau der Meilenstein-Roadmap in die orthogonale
Welle/Meilenstein-Form ist **aufgeschoben mit Trigger** (§8).

**Datum:** 2026-07-16

**Bezug:** — (Prozess-/Werkzeug-ADR ohne Spec-Stratum). Präzedenz gleicher Klasse:
[ADR 0002](0002-release-pipeline-gates.md) (Adoption verbindlicher Gate-Disziplin),
[ADR 0010](0010-boundary-test-tooling.md) (Adoption externer Prüf-Werkzeuge).

---

## 1. Kontext

bess-ems hat organisch eine strenge Doku- und Gate-Disziplin entwickelt: ein
eigenes Doku-Referenz-Gate (`d-check.mk`, v0.42.0, voller Modulsatz, `--network
none`), hexagonale Architektur-Boundary-Tests, ein mehrstufiges Makefile-Gate-Bündel
(`make gates`/`ci`), einen ADR-Korpus (0001–0013) und eine Meilenstein-Roadmap
M1–M6.

Was fehlt, ist ein **explizit adoptierter, versionierter Prozess-Kanon**: die
Regeln, *wie* geplant, geschnitten, geschlossen und reviewt wird (beobachtbare
Trigger statt Termine, Slice = in einer Sitzung prüfbar, Carveout mit
Auflösungs-Trigger, Welle endet intern durch Closure vs. Meilenstein endet extern,
Lerneintrag als Closure-Pflicht). Diese Governance existiert extern als
**AI-Harness-Baseline** (`ai-harness-course`, Release v1.4.0) und ist in
Schwesterprojekten im **committed-vendored**-Muster erprobt (Regelwerk entpackt und
sha256-integritätsgeprüft im Repo, netzlos verifizierbar).

Zwei konkrete Drücke:

1. Das d-check-`planning`-Modul ist heute ein struktureller **No-op** — es prüft
   die Welle/Slice-Lifecycle-Invariante, die bess-ems' meilenstein-basierte Roadmap
   nicht abbildet. Ohne adoptierten Lifecycle bleibt der Check bedeutungslos.
2. Der Prozess-Kanon lebt implizit „im Kopf". Ihn versioniert, offline-verifizierbar
   und als gemeinsame Sprache mit den Schwesterprojekten ins Repo zu heben, macht
   ihn auditierbar und driftfest.

**Kippende Annahme:** die Baseline bleibt mit bess-ems' bereits schärferen Gates
kompatibel. bess-ems übertrifft die Baseline-Defaults in mehreren Achsen (d-check-Version
und Modulsatz, Gate-Tiefe), daher ist der Einbau additiv, nicht ersetzend. Kippt das —
etwa wenn eine Baseline-Version eine unserer Schärfen zurückdrehen wollte — wird diese
Entscheidung neu bewertet.

## 2. Entscheidung

Wir adoptieren die AI-Harness-Baseline **v1.4.0** im **committed-vendored**-Muster.
Das Regelwerk liegt entpackt unter `.harness/baseline/v1.4.0/regelwerk/` samt
`SHA256SUMS`-Integritätsmanifest, offline materialisier- und verifizierbar über
`tools/harness/fetch-baseline-cache.sh` (`--verify`). Der Einbau ist **additiv**:
bess-ems' bestehende Gate-/Doku-Infrastruktur bleibt autoritativ; jede Abweichung von
den Baseline-Defaults wird als nummerierter `MR-*`-Eintrag in `harness/conventions.md`
dokumentiert.

Der retroaktive **Umbau** der bestehenden Meilenstein-Roadmap (M1–M6, mit unter dem
Meilenstein eingebetteten Slices) in die orthogonale Form (Meilenstein-Tabelle *neben*
Wellen) wird **nicht** jetzt vorgenommen, sondern aufgeschoben mit Trigger (§8).

## 3. Umfang des Einbaus (jetzt)

- **Vendored Regelwerk:** `.harness/baseline/v1.4.0/regelwerk/*.md` (Split-Module +
  Grundlagen-Abschnitte + Index-`README.md`) und `SHA256SUMS`.
- **Materialisierung/Verifikation:** `tools/harness/fetch-baseline-cache.sh`
  (Default = re-vendor mit Netz; `--verify` = offline `sha256sum -c`; Tag aus
  `harness/conventions.md` §Baseline als Single Source of Truth).
- **Prozess-Pointer (Inhalt wird nicht dupliziert, nur verwiesen):** `AGENTS.md`
  (Hard Rules + „ein Modul pro Session lesen"), `harness/README.md` (Source Precedence,
  Sensors), `harness/conventions.md` (§Baseline + Adaptions-Block `MR-*`).
- **d-check-Anpassung:** `.d-check.yml` `scan.ignore` um `.harness/baseline/**` und
  `.harness/cache/**` erweitern (Fremdinhalt mit hunderten Upstream-Links aus dem Scan
  nehmen); `.gitignore` für `.harness/cache/` (die vendored `baseline/` bleibt getrackt).
- **Bewusst NICHT eingebaut** (Baseline-Parität mit dem erprobten Muster): keine
  `.claude`-Hooks (SessionStart-Injektor / Command-Guard), kein `harness.mk`, keine
  neuen Enforcement-Make-Targets. „Ein Modul pro Session" bleibt Prosa-Regel in
  `AGENTS.md`. Automatisierte Injektion/Guarding wäre net-new und ein eigener ADR (§8).

## 4. Dokumentierte Abweichungen (`MR-*`, Auszug)

Die Adaptionen leben normativ in `harness/conventions.md`; hier nur die
substanziellen für die Nachvollziehbarkeit der Entscheidung:

- **Doc-Gate:** eigenes `d-check.mk` (v0.42.0, voller Modulsatz, `--network none`)
  statt des Baseline-`harness.mk` (ältere d-check-Version, simpler Modulsatz).
- **Spec-Straten:** zwei (Lastenheft + Architektur) — Baseline-Default; keine
  dritte `spezifikation.md`-Schicht.
- **Anforderungs-ID-Schema:** kategoriebasiert `LH-<KAT>-<NN>` statt des
  Baseline-`LH-FA/QA-<NN>`.
- **Planning-IDs / Lifecycle:** Bestand nutzt ein meilenstein-gebundenes
  `M<n>`-Schema mit eingebetteten Slices. Der Baseline-Lifecycle
  (`open/next/in-progress/done`, Slices als eigene Dateien, Wellen als
  Roadmap-Bündel) wird **vorwärts** adoptiert; der bestehende Planungskorpus wird
  grandfathered, bis §8 greift.

## 5. Verglichene Alternativen

| Option | Pro | Contra |
|---|---|---|
| A — Status quo (nichts tun) | kein Aufwand; Gates schon streng | Prozess-Kanon bleibt implizit; `planning`-Check bleibt No-op; keine gemeinsame Governance |
| B — Baseline per URL referenzieren (nicht vendored) | minimal, kein Vendoring | netzabhängig pro Session; nicht offline-/audit-fest; Drift gegen bewegliches `main` |
| **C — Committed-vendored v1.4.0 (gewählt)** | netzlos auf jedem Checkout, sha256-geprüft, tag-gepinnt; additiv; erprobtes Muster | einmaliger Einbauaufwand; ~20 Fremd-Dateien im Repo (aus dem Scan genommen) |
| D — Eigenes Regelwerk from scratch | maximal passgenau | dupliziert erprobte Governance; kein gemeinsamer Kanon; hoher Pflegeaufwand |

## 6. Konsequenzen

- **Positiv:** versionierter, offline-verifizierbarer Prozess-Kanon im Repo;
  Planning-Lifecycle und Welle/Meilenstein-Trennung verfügbar; das d-check-`planning`-Modul
  wird sinnvoll aktivierbar, sobald eine Idle-Marker-Roadmap steht; gemeinsame Sprache
  mit den Schwesterprojekten; der Adaptions-Block macht bess-ems-Abweichungen explizit
  statt implizit.
- **Negativ / Schmerz:** zusätzlicher Fremdinhalt im Repo (mitigiert durch
  `scan.ignore` + Integritätsmanifest); ein Doppel-Namensschema (bestehendes
  `M<n>`/Slice-Bündel vs. Baseline-Slice/Welle vorwärts), bis der Umbau in §8 erfolgt;
  die „ein Modul pro Session"-Disziplin ist nur prosaisch, nicht maschinell erzwungen.
- **Folgepflicht:** `harness/conventions.md` Adaptions-Block pflegen; ein
  Baseline-Bump ist ein neuer `MR-*` plus `fetch-baseline-cache.sh`-re-vendor; der
  orthogonale Roadmap-Umbau ist ein eigener, getriggerter Folgeschritt (§8).

## 7. Fitness Function

| Tooling | Regel | Aufruf |
|---|---|---|
| `tools/harness/fetch-baseline-cache.sh --verify` | `.harness/baseline/v1.4.0/regelwerk/*` hasht identisch zu `SHA256SUMS` (netzlose Integrität) | manuell / CI |
| d-check `planning` | Idle-Marker „Keine aktive Welle" ⟺ kein aktiver Slice (nach Idle-Roadmap-Setup) | `make doc-planning` |
| d-check `matrix`/`ids` | ADR-Layering `adr↛plan`, Link-/Anker-/ID-Pflicht — gilt auch für dieses ADR | `make docs-check` |

## 8. Re-Evaluierungs-Trigger

- **Orthogonaler Roadmap-Umbau (aufgeschoben):** Trigger = die erste *neue*,
  post-Adoption aktiv geplante Arbeitswelle (d. h. sobald wieder geschnitten statt nur
  bestehende trigger-getriebene Follow-ups verwaltet werden). Dann M1–M6 in eine
  `## Meilensteine`-Tabelle *neben* Wellen überführen; bis dahin bleibt der Bestand
  grandfathered.
- **Baseline-Bump:** ein neues `ai-harness-course`-Release → Abwägung eines Bumps
  via neuem `MR-*`.
- **Enforcement-Hooks:** falls „ein Modul pro Session" prosaisch nicht trägt →
  SessionStart-Injektor / Command-Guard als eigener ADR (net-new gegenüber dem
  adoptierten Muster).

## 9. Geschichte

| Datum | Ereignis | Verweis |
|---|---|---|
| 2026-07-16 | Proposed | dieser Entwurf |
| 2026-07-16 | Accepted | Owner-Sign-off (pt9912) |
