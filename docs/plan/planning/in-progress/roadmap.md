# Roadmap: bess-ems

**Dokumenttyp:** Planung / Roadmap
**Status:** In Arbeit
**Bezug:** [`spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`spec/spezifikation.md`](../../../../spec/spezifikation.md)
([§5](../../../../spec/spezifikation.md#5-native-core-strategie) Native-Core-Phasenmodell),
[`spec/architecture.md`](../../../../spec/architecture.md), [`docs/user/quality.md`](../../../user/quality.md)
(Gate-Aktivierung pro Meilenstein)

---

## Zweck

Dieses Dokument beschreibt die geplante Umsetzungsreihenfolge für `bess-ems`
in Meilensteinen. Es ist die Brücke zwischen Lastenheft (was) und
Architektur (wie) hin zu konkreter Arbeit (wann, in welcher Reihenfolge).

Meilensteine erscheinen als Zeilen in der Meilenstein-Tabelle
(`## Wellen → Meilensteine → Releases`); Liefergegenstände, Lastenheft-Kennungen
und Abnahmekriterien je Meilenstein leben im zugehörigen Done-Plan. Kennungen
`RM-Mn-xx` ermöglichen die Verlinkung aus PRs, Issues und ADRs.

Diese Roadmap ist die **Statusseite** des Projekts. Sie duplikiert nicht
die Anforderungen (die stehen normativ im Lastenheft), sondern verfolgt
*wo wir stehen, was als nächstes kommt und welche Risiken offen sind*.
Detail-DoD-Tracking pro Meilenstein lebt in einem eigenen
`plan-RM-Mn.md`; offene Entwürfe liegen unter `open/`, aktive Pläne
unter `in-progress/`.

### Status-Legende

| Symbol | Bedeutung   |
| ------ | ----------- |
| ✅     | abgeschlossen |
| 🟡     | in Arbeit  |
| ⬜     | geplant    |
| ⬛     | obsolet / verworfen |

---

## Aktuelle Welle

**Status:** Keine aktive Welle.

Alle Meilensteine M1–M6 sind abgeschlossen; die laufende Arbeit besteht aus
trigger-getriebenen Follow-ups (siehe **Aktive Fäden** unter `## Offene Punkte
zur Roadmap` sowie die `open/`-Notizen). Derzeit ist **keine aktive Welle** geschnitten. Der
Welle/Slice-Lifecycle wird ab der ersten neuen, aktiv geplanten Arbeitswelle
geführt ([ADR 0014](../../adr/0014-operations-baseline-adoption.md) §8;
`harness/conventions.md` MR-003).

**Abgeschlossene Wellen:** `welle-01`…`welle-06` (⇒ Meilensteine M1–M6,
aufgeschlüsselt unter `## Wellen → Meilensteine → Releases`).

---

## Wellen → Meilensteine → Releases

Modell und Definitionen (Welle ≠ Meilenstein ≠ Release; orthogonal, Meilenstein
leitet sich aus Welle(n) ab, Release umfasst Wellen): **Regelwerk Modul 6,
§Welle ≠ Meilenstein ≠ Release** (`modul-06-roadmap.md`) — hier nicht wiederholt.
Diese Sektion trägt nur die bess-ems-spezifische Zuordnung.

**Meilensteine** (der Meilenstein referenziert seine Quell-Welle, aus der er sich ableitet):

| Meilenstein | Welle(n) | Trigger | Status |
|---|---|---|---|
| M1 — MVP: sichere Regelpipeline | `welle-01-mvp` | RM-M1-01..24 in `done/`; `make fullbuild` grün; Compose `/health = ok` | ✅ |
| M2 — Marktausbau und Optimierung | `welle-02-marktausbau` | RM-M2-01..10 in `done/` (Optimization, Migrations-Tooling, HIL) | ✅ |
| M3 — Native Control Core (Library) | `welle-03-native-control-core` | RM-M3-01..13 + M3-D2 in `done/`; vier Native-Gates grün; Replay-Parity grün | ✅ |
| M4 — Regelleistung und OPC-UA | `welle-04-regelleistung-opcua` | RM-M4-01..08 in `done/`; OPC-UA-HIL-Roundtrips grün | ✅ |
| M5 — MPC, Solver-Sidecar, Replay | `welle-05-mpc-replay` | RM-M5-01..07 in `done/`; MPC-Property- + Replay-Gates grün | ✅ |
| M6 — Skalierung, UI, Edge / Multi-Asset | `welle-06-skalierung-edge` | RM-M6-01..06 in `done/`; Helm-Lint + Zertifizierungsgate grün | ✅ |

**Releases (umfassen Wellen):**

| Release | Umfasste Wellen | Datum |
|---|---|---|
| `v1.0.0` | `welle-01`…`welle-06` (M1–M6 vollständig) | 2026-05-14 |
| `v2.0.0`…`v2.2.1` | Post-M6-Wellen (u. a. Feldvertrag, [ADR 0013](../../adr/0013-device-mapping-field-contract.md)) — außerhalb des M1–M6-Scopes | 2026-07-13 |

Slices (`RM-*`) und Abnahmekriterien je Welle stehen in den Done-Plänen (siehe
`## Abgeschlossene Wellen`), nicht in der Roadmap. Produktive Zertifizierungswellen
(nach M6) bleiben trigger-getrieben nach Produkt-/TSO-/Anlagenkonzept.

**Phase-Zuordnung** (bezieht sich auf [`spezifikation.md`](../../../../spec/spezifikation.md) [§5.1](../../../../spec/spezifikation.md#51-phasenmodell)): M1 Phase 1 · M2 Phase 1 → 2 · M3 Phase 2 · M4 Phase 2 · M5 Phase 3 · M6 Phase 4. Die frühere Native-Core-Ideenskizze liegt archiviert unter [`docs/archive/idea.md`](../../../archive/idea.md).

---

## Abgeschlossene Wellen

Form nach roadmap-Template der Baseline v1.4.0 (Template nicht vendored, als
Konvention übernommen). Die Welle↔Meilenstein-Zuordnung steht in
„Wellen → Meilensteine → Releases".

| Welle | Abschluss | Closure-Notiz / Detailplan |
|---|---|---|
| `welle-01-mvp` | v1.0.0 (2026-05-14) | [`plan-RM-M1`](../done/plan-RM-M1.md), [Simulator](../done/plan-RM-M1-simulator.md) |
| `welle-02-marktausbau` | v1.0.0 | [`plan-RM-M2-optimization`](../done/plan-RM-M2-optimization.md), [`-migration`](../done/plan-RM-M2-migration.md), [HIL](../done/HIL-simulator.md) |
| `welle-03-native-control-core` | v1.0.0 | [`plan-RM-M3`](../done/plan-RM-M3.md), [`M3-D2`](../done/plan-RM-M3-D2.md) |
| `welle-04-regelleistung-opcua` | v1.0.0 | [`plan-RM-M4`](../done/plan-RM-M4.md); [Follow-ups](../open/note-RM-M4-followups.md) |
| `welle-05-mpc-replay` | 2026-05-13 → v1.0.0 | [`plan-RM-M5`](../done/plan-RM-M5.md) |
| `welle-06-skalierung-edge` | 2026-05-13 → v1.0.0 | [`plan-RM-M6`](../done/plan-RM-M6.md) |

---

## Querschnittsthemen

| Thema                       | Anmerkung                                                        |
| --------------------------- | ---------------------------------------------------------------- |
| ADRs                        | Wichtige Entscheidungen unter `docs/plan/adr/` festhalten         |
| Sicherheitsregression       | Sicherheitsfall-Tests laufen ab M1 in jeder CI-Pipeline ([LH-TEST-006](../../../../spec/lastenheft.md#lh-test-006--sicherheitsfall-tests)) |
| Native-Reference-Parität    | .NET-Referenzregler bleibt parallel gepflegt zum Native Core     |
| Konfigurations-Schemata     | JSON-Schemata unter `config/schema/` + Validatoren mitwachsen lassen |
| Vorzeichenkonvention        | In jedem neuen Modul aktiv testen ([LH-DOM-007](../../../../spec/lastenheft.md#lh-dom-007--vorzeichenkonvention))                      |

---

## Offene Punkte zur Roadmap

| Kennung    | Frage                                                          | Status |
| ---------- | -------------------------------------------------------------- | ------ |
| RM-OPEN-01 | Konkrete Zeitachse / Kalenderwochen pro Meilenstein?           | Offen  |
| RM-OPEN-02 | Welche Hersteller-Integration zuerst (siehe LH-OPEN-001)?      | Geschlossen mit LH-OPEN-001 — SunSpec/Socomec zuerst, danach Victron und SMA; Sungrow bleibt bis Rechtsklärung zurückgestellt. |
| RM-OPEN-03 | Solver-Auswahl für M2 (HiGHS vs. OR-Tools default)?            | Geschlossen mit RM-M2/M5 — M2 nutzt OR-Tools/GLOP fuer den Schedule-Optimizer; M5 nutzt OSQP fuer MPC gemaess [ADR 0006](../../adr/0006-mpc-kernel-backend-and-solver.md). HiGHS ist kein Default. |
| RM-OPEN-04 | Authentifizierung in M1 (API-Token, OIDC)?                     | Geschlossen mit RM-M1-16 — API-Token + Operator-Rolle live; OIDC/mTLS bleiben Folge-ADR. |
| RM-OPEN-05 | Reihenfolge M3 vs. M4 — Native zuerst oder Markt-/RL zuerst?   | Geschlossen durch Umsetzung — M3 Native Core wurde vor M4 Regelleistung/OPC-UA abgeschlossen; beide Meilensteine sind erledigt. |
| RM-OPEN-06 | Kriterien für spätere API-Extraktion nach dem MVP (siehe AR-OPEN-001)? | Geschlossen mit [ADR 0009](../../adr/0009-api-service-extraction-criteria.md) — kombinierter Worker/API-Host bleibt Default; API-Auskopplung ist trigger-basiert und braucht explizite Topologie-, Security-, Ownership- und Testnachweise. |
| RM-OPEN-07 | Folge-ADR für Release-Pipeline-Gates; vor Abschluss von M1 und vor erstem Tag `v0.1.0` schließen? | Geschlossen mit [ADR 0002](../../adr/0002-release-pipeline-gates.md) — `.github/workflows/release.yml` ist Gate-only vor Publishing; kein freigegebener Tag ohne grünen Release-Workflow. **Publishing-Schicht mit `v1.0.0` (2026-05-14) nachgezogen** (GHCR, Cosign keyless, GitHub Release, Helm/Source/Native-Assets — siehe [ADR 0002 §5](../../adr/0002-release-pipeline-gates.md#5-update-publishing-aktivierung-mit-v100-2026-05-14), `docs/user/releasing.md`, `docs/user/quality.md` §8). v0.x wurde übersprungen, erstes Release ist `v1.0.0`. |

**Aktive Fäden (Einstieg für die nächste Session):**

1. **API-Casing-Befund** (aus dem Anwenderhandbuch-Review): `CommandView.Source`/`.Mode`, `TelemetryQualityView.Flag` und `ScheduleView.Type` serialisieren PascalCase (string-typisierte `ToString()`-Properties umgehen die globale SnakeCaseLower-Policy). Fix ist klein, aber ein Wire-Break auf öffentlichen Endpoints → Major-Bump-Material. **Entscheidung offen**, ob er in [`../next/note-internal-refinement-scope.md`](../next/note-internal-refinement-scope.md) aufgenommen wird (Trigger: nächstes ohnehin brechendes Release).
2. **Internal-Refinement-Paket** (Lock-Eviction + Cluster-Smoke, [`../next/note-internal-refinement-scope.md`](../next/note-internal-refinement-scope.md)): wartet auf das nächste freie Minor.
3. **Kommando-Closed-Loop-Wirkung** der Schwester-Plattform bleibt deferred-with-trigger ([ADR 0013 §6](../../adr/0013-device-mapping-field-contract.md#6-command-closed-loop-deferred-mit-trigger-0012-muster), [LH-SAFE-007](../../../../spec/lastenheft.md#lh-safe-007--schreibbegrenzung-vor-feldkommunikation)).
4. grid-gym-Digest-Bumps weiterhin bewusst via `GRID_GYM_IMAGE`, wenn dort neue Releases landen.
5. **Doku-Layering-Audit fortsetzen:** die quality.md-Prüfung auf die übrigen Anwender-Docs ausweiten (persistence, releasing, sut-field-endpoint, anwenderhandbuch, security) — dieselbe Trennung normativer Vertrag → Spec vs. Verifikation/Anleitung. Offene Grenzfälle in quality.md: §5.6 Architektur-Tabus-Tabelle, §2.3 Safety-Erwartungen, §5.1 Vorzeichen-Properties; sowie ob die Native-Wiring-Anleitung in [quality.md §5.2](../../../user/quality.md#52-native-abi) in ein Deployment-/Konfig-Doc wandert.

---

## Verlinkung

- Lastenheft-Anforderungen: [`spec/lastenheft.md`](../../../../spec/lastenheft.md)
- Architekturentwurf: [`spec/architecture.md`](../../../../spec/architecture.md)
- Qualitäts- und Messpfade: [`docs/user/quality.md`](../../../user/quality.md)
- Release-Prozess: [`docs/user/releasing.md`](../../../user/releasing.md)
- Post-v1.0.0-Planung: [`../next/`](../next/) (konkret geplante v1.x/v2.x-Arbeit
  vor Slice-Aktivierung), [`../open/`](../open/) (trigger-getriebene
  Follow-up-Items aus M3–M6)
- Archivierte Native-Core-Ideenskizze: [`docs/archive/idea.md`](../../../archive/idea.md)

---

## Wartung dieses Dokuments

Die Roadmap ist ein **Wellen-Index** (Aktuelle Welle · Meilensteine · Releases),
kein Detail-Log. Planungs-Lifecycle, Slice-Größe, Closure-Regeln und Carveouts
folgen dem adoptierten Betriebsregelwerk (Modul 5/6, [ADR 0014](../../adr/0014-operations-baseline-adoption.md)).

- **Detail gehört in die Done-Pläne, nicht in die Roadmap:** abgeschlossene
  Liefergegenstände (`RM-*`) und Abnahmekriterien leben in den Done-Plänen
  (`docs/plan/planning/done/plan-RM-Mn.md`); die Roadmap verweist nur darauf.
- **Neue Welle:** `## Aktuelle Welle` mit Welle-ID + Closure-Trigger füllen und
  den Idle-Marker „Keine aktive Welle" entfernen; Slices als `slice-*.md` im
  Lifecycle `open` → `next` → `in-progress` → `done`.
- **Welle abgeschlossen:** die Meilenstein-Zeile in „Wellen → Meilensteine →
  Releases" auf ✅ setzen und Closure-Notiz/Detailplan verlinken; `## Aktuelle
  Welle` zurück auf den Idle-Marker.
- Bei Inkonsistenz zwischen Lastenheft (`LH-*`) und Roadmap-Eintrag gewinnt das
  Lastenheft; die Roadmap wird angepasst.

### Slice-Plan-Konvention

Innerhalb eines Meilensteins sind die einzelnen Arbeitspakete
(`RM-Mn-XX`) **nicht symmetrisch** dokumentiert. Manche leben mit voller
DoD inline in der Master-Plan-Liefergegenstands-Tabelle, andere bekommen
einen eigenen Detail-Slice-Plan unter `docs/plan/planning/{open,in-progress,done}/plan-RM-Mn-XX.md`.
Die Asymmetrie ist absichtlich, skaliert mit dem Umfang des einzelnen
Slices — nicht jedes Arbeitspaket trägt die Kosten eines eigenen
Slice-Plan-Files.

**Eigener Slice-Plan** wenn mindestens einer dieser Auslöser zutrifft:

- Das Arbeitspaket bricht sich natürlich in mehrere Sub-Slices
  (`RM-Mn-XX-A..n`) auf, jeder mit eigenem Closure-Commit.
- Geschätzter Aufwand ≥ ~1 Woche zusammenhängender Arbeit.
- Fünf oder mehr Design-Entscheidungen (D-01..D-NN), die jeweils eine
  Alternative + Begründung tragen.
- Externer Review-Pass (ein oder mehrere Iterationen) ist eingeplant —
  das Slice-Doc ist der Lese-Anker.
- Querverweise auf andere Slice-Pläne, ADRs, Plan-Notes als
  Trigger-Watches oder Cross-Cutting-Entscheidungen sind so dicht, dass
  eine eigene Bezug-Sektion sich lohnt.

**Inline-DoD in der Master-Plan-Tabelle** ist die Default-Form für alles
andere. Die DoD-Zelle trägt selbst die volle Substanz:

- Implementierungs-Skizze, Test-Inventory, Persistenz-/Migrations-
  Berührungspunkte.
- Design-Entscheidungen `D-01..D-04` inline (selten mehr; wenn doch:
  Wechsel auf eigenen Slice-Plan).
- Trigger-getriebene Folgearbeiten als „**Bewusst draußen** mit
  konkretem Trigger" Aufzählung — entweder inline oder mit Verweis auf
  `note-RM-Mn-followups.md`.

**Folge für Lesende:** die Master-Plan-Tabelle ist die normative
DoD-Quelle. Wenn eine DoD-Zelle `Slice-Plan: [plan-RM-Mn-XX.md](...)`
nennt, lebt die volle Tiefe im verlinkten Slice-Doc; sonst ist die
Zelle selbst die Quelle.

**Beispielzuordnung in M4** (zur Kalibrierung künftiger Slices):

| Arbeitspaket | Form | Begründung |
| ------------ | ---- | ---------- |
| RM-M4-01/02/06/07 | Inline-DoD | Single-Shot-Arbeit, 4 Design-Entscheidungen, kein Sub-Slice-Breakdown |
| RM-M4-03/04/05/08 | Eigener Slice-Plan | Sub-Slice-A..n + Multi-Wochen-Aufwand + Review-Pass-Iterationen |
