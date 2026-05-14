# ADR 0002 — Release-Pipeline-Gates

**Status:** Accepted — schliesst `RM-OPEN-07`; Release-Tags sind ohne
erfolgreichen Release-Gate-Workflow nicht zulaessig.
Vollständig implementiert mit `v1.0.0` (2026-05-14, siehe §5).
**Datum:** 2026-05-09
**Bezug:**
[`../planning/done/plan-RM-M1.md`](../planning/done/plan-RM-M1.md)
(`RM-OPEN-07`),
[`../planning/in-progress/roadmap.md`](../planning/in-progress/roadmap.md)
(`RM-OPEN-07`),
[`../../user/quality.md`](../../user/quality.md) §7/§8,
[`../../../spec/lastenheft.md`](../../../spec/lastenheft.md)
(LH-DEPLOY-001..004, LH-TEST-001/006/007)

---

## 1. Kontext

`RM-OPEN-07` verlangte eine Entscheidung zu Release-Pipeline-Gates vor
Abschluss von M1 und vor dem ersten Tag `v0.1.0`. M1 ist fachlich
abgeschlossen, aber ohne formale Release-Entscheidung blieb der
Blocker offen.

Die normale PR-/Main-CI und die Release-Pipeline haben verschiedene
Aufgaben:

- PR-/Main-CI prueft jede Aenderung gegen die verbindlichen Build-,
  Test-, Coverage-, Schema-, Container- und Native-Gates.
- Release-CI prueft zusaetzlich, dass ein Git-Tag ein gueltiger
  Release-Kandidat ist und dass das daraus gebaute Runtime-Image die
  Release-Metadaten und Native-Library-Invarianten traegt.

---

## 2. Entscheidung

| Achse | Entscheidung |
| ----- | ------------ |
| PR-/Main-CI | `.github/workflows/build.yml` laeuft auf Pull Requests gegen `main` und Pushes nach `main`. |
| Release-CI | `.github/workflows/release.yml` laeuft auf Tags `v*.*.*`. |
| Tag-Format | Erlaubt ist `vMAJOR.MINOR.PATCH[-PRERELEASE]`; Build-Metadata mit `+...` ist verboten. |
| Release-Freigabe | Ein Release-Tag gilt nur als freigabefaehig, wenn der Release-Workflow gruen ist. |
| Image-Publishing | Der erste Workflow ist bewusst Gate-only; Registry-Push wird erst aktiviert, wenn Repository, Namensschema und Signaturziel feststehen. *(Mit v1.0.0 nachgezogen, siehe §5.)* |
| Artefakte | CI- und Release-Logs werden als Workflow-Artefakte hochgeladen. SBOM ist ab Major-Release Pflicht-Artefakt. *(Major-Gate mit v1.0.0 entfernt; SBOM ist jetzt unkonditional.)* |

Damit ist `RM-OPEN-07` geschlossen, ohne einen unfertigen
Registry-/Signaturprozess vorzutäuschen.

---

## 3. Release-Gates

Der Release-Workflow erzwingt:

1. Semver-Tag-Validierung: `vMAJOR.MINOR.PATCH[-PRERELEASE]`, kein
   Build-Metadata-Suffix.
2. `make fullbuild`: alle CI-Gates plus Runtime-Image und Compose-Smoke.
3. Runtime-Image-Build mit `SOURCE_DATE_EPOCH` aus dem getaggten Commit.
4. OCI-Labels fuer Version, Revision und Source.
5. Label-Check: Tag-Version entspricht dem Image-Label
   `org.opencontainers.image.version`, Revision entspricht `GITHUB_SHA`.
6. Native-Library-Check: `/app/native/libbattery_control_core.so`
   existiert und hat keine nicht aufloesbaren dynamischen Dependencies.
7. SBOM-Artefakt ab Major-Release (`v1.0.0` und hoeher).
   *(Mit v1.0.0 zu unkonditional umgestellt — siehe §5.)*

Cosign-Signatur und Registry-Push bleiben absichtlich noch nicht aktiv,
weil dafuer ein Ziel-Registry- und Schluessel-/OIDC-Modell entschieden
werden muss. Sie duerfen erst aktiviert werden, wenn die Release-Gates
weiterhin gruen bleiben und die Signaturpolitik dokumentiert ist.
*(Mit v1.0.0 aktiviert — siehe §5.)*

---

## 4. Konsequenzen

- `RM-OPEN-07` ist nicht mehr offen.
- Der erste `v0.1.0`-Tag darf erstellt werden, sobald der Release-Workflow
  fuer diesen Tag gruen ist. *(Skipped: erstes Release war `v1.0.0`.)*
- Ein Tag ohne gruenen Release-Workflow ist kein freigegebener Release.
- PR-/Main-CI bleibt unabhaengig von Release-Publishing und darf keine
  Secrets oder Package-Write-Rechte benoetigen.

---

## 5. Update: Publishing-Aktivierung mit v1.0.0 (2026-05-14)

Die in §2/§3 als „bewusst noch nicht aktiv" zurückgestellten
Publishing- und Signatur-Schritte wurden mit dem ersten Tag `v1.0.0`
nachgezogen. Die Entscheidung selbst (gate-getrieben, Tag-only
Trigger, semver-Vertrag) bleibt unverändert; ergänzt wurden:

| Achse                 | Implementierung                                                                                  |
| --------------------- | ------------------------------------------------------------------------------------------------ |
| Zielregistry          | `ghcr.io/pt9912/bess-ems` (Tags `:vX.Y.Z`, `:X.Y.Z`, `:latest` nur bei stabiler Version)         |
| Signaturmodell        | Cosign keyless via sigstore-OIDC (`id-token: write`, kein langlebiges Secret)                    |
| SBOM                  | Pflicht für jedes Release (Major-Gate entfernt); zusätzlich als Cosign-Attestation gebunden       |
| Asset-Set             | Container + Helm-Chart-Tarball + Source-Tarball + native `.so`+Header + `image-inspect` + SBOM + `SHA256SUMS` |
| Release-Identität     | GitHub Release mit Notes aus dem CHANGELOG-`## [X.Y.Z]`-Block                                    |
| Zusätzliche Pre-Gates | Annotierter-Tag-Check, Tag-auf-`origin/main`-Check, CHANGELOG-Block-Existenz                     |

Die produktive Pipeline ist in
[`docs/user/releasing.md`](../../user/releasing.md) prozessual
beschrieben und in
[`docs/user/quality.md`](../../user/quality.md) §8 als verbindliche
Gates dokumentiert. Diese ADR bleibt der Anker für die ursprüngliche
gate-only Entscheidung; die Publishing-Schicht wird durch die beiden
Live-Docs gepflegt.
