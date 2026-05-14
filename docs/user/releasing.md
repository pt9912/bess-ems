# Releasing

## Zweck

Dieses Dokument beschreibt den **reproduzierbaren Release-Pfad** für
`bess-ems`. Es legt fest, welche Versions-Identitäten gepflegt werden,
welche Artefakte ein Release publiziert, wie ein Release ausgelöst wird,
welche manuellen Schritte verbleiben und wie ein Rollback aussieht.

Aktuelle Versionsstände leben nicht in diesem Dokument; sie ändern sich
pro Release. Dieses Doc fixiert *wie* releast wird, nicht *was die
letzte Version war*.

Bezug:
- [`CHANGELOG.md`](../../CHANGELOG.md) (Keep-a-Changelog 1.1.0)
- [`docs/user/quality.md`](quality.md) §1 (Lint/Restore-Lock-Disziplin),
  §6 (Native-Quality-Gates)
- [`.github/workflows/release.yml`](../../.github/workflows/release.yml)
  (Tag-getriebene Release-Pipeline)
- [`.github/workflows/build.yml`](../../.github/workflows/build.yml)
  (PR/Main-CI-Gates)
- [`native/battery_control_core/include/battery_control_core.h`](../../native/battery_control_core/include/battery_control_core.h)
  (`BCC_ABI_VERSION_*` — eigenständige SemVer-Linie für die `.so`)

---

## 1. Versions-Identitäten

`bess-ems` pflegt **drei voneinander unabhängige** SemVer-Linien. Sie
werden bewusst getrennt gehalten, weil sie unterschiedliche Konsumenten
und unterschiedliche Bruchsemantiken haben.

| Linie                | Quelle der Wahrheit                                                | Konsument                          | Bruchsemantik                                                                          |
| -------------------- | ------------------------------------------------------------------ | ---------------------------------- | -------------------------------------------------------------------------------------- |
| **App-Version**      | Git-Tag `vMAJOR.MINOR.PATCH[-PRERELEASE]`                          | Operator, Betrieb, Container-Pull  | Standard-SemVer auf der Anwendung als Ganzes (API, Konfig, Compose/Helm-Vertrag).      |
| **Helm-Chart**       | `deploy/helm/bess-ems/Chart.yaml` Felder `version` + `appVersion`  | Cluster-Operator, GitOps           | `appVersion` folgt der App-Version; `version` darf ab v1.1.0 unabhängig erhöht werden. |
| **Native-Kernel-ABI** | `BCC_ABI_VERSION_MAJOR/MINOR/PATCH` im `.h`                       | P/Invoke-Adapter, Out-of-Tree-Konsumenten | Eigene SemVer-Linie; Bump-Regeln stehen im Header-Kommentar (`ABI guarantees`).        |

**Konsequenz:** ein App-Major-Bump (`v1 → v2`) zwingt **nicht** zu
einem Native-Major-Bump. Umgekehrt kann ein Native-Major-Bump (z. B.
Struct-Layout-Bruch) in einem App-Minor stecken — der Adapter ist die
Pufferzone (`abi-mismatch`-Fallback, siehe `docs/user/quality.md` §6).

Für die erste Major (`v1.0.0`) sind alle drei Linien zu Klarheits­
zwecken synchronisiert: App = `1.0.0`, Chart = `1.0.0/1.0.0`, Native
bleibt auf seiner eigenen Linie `0.3.0`.

---

## 2. Voraussetzungen vor dem Tag

Vor `git tag` müssen folgende Punkte erfüllt sein. Jede Verletzung ist
ein **Stop** — kein Tag ohne grünes Set.

1. **Main ist clean.** `git status` ist leer, kein lokales Working-Set,
   das nicht im Tag landen soll.
2. **`make fullbuild` ist lokal grün.** Inkl. aller M1–M6-Gates und
   Compose-Smoke. Der CI-Lauf auf dem Tag wiederholt das, aber ein
   lokaler grüner Lauf vermeidet einen kaputten Tag, der sich nur durch
   Force-Löschen wieder einfangen lässt.
3. **`CHANGELOG.md`** hat einen versionierten Eintrag `## [X.Y.Z] -
   YYYY-MM-DD` (ISO-Datum). Der `[Unreleased]`-Block ist auf leere
   Sektions-Stubs zurückgesetzt. Inhalt orientiert sich an
   Keep-a-Changelog-Sektionen (`Added`/`Changed`/`Fixed`/
   `Deprecated`/`Removed`/`Security`).
4. **Helm-Chart-Versionen** in `deploy/helm/bess-ems/Chart.yaml`
   spiegeln die neue App-Version (`appVersion`) und sind im
   Chart-Versions-Feld (`version`) konsistent erhöht. `make helm-lint`
   ist grün.
5. **Native-ABI-Bump-Pflicht prüfen.** Wenn `native/battery_control_core/`
   in diesem Release verändert wurde, ist `BCC_ABI_VERSION_*` gemäß den
   im Header dokumentierten Regeln zu erhöhen. Kein App-Tag mit
   unverändertem ABI-Version-Triple bei verändertem ABI-Surface.
6. **Lock-Dateien** sind aktuell. `make lock-refresh` produziert
   zero-diff. (Siehe `docs/user/quality.md` §1.4.)
7. **Offene Follow-up-Notes geprüft.** `docs/plan/planning/open/note-RM-M*-followups.md`
   enthält keinen Eintrag, der durch die Releasebahn jetzt zwingend
   getriggert wäre (z. B. Production-Profile-Drift bei Operations-
   Anlass).

---

## 3. Tag setzen

Der Workflow triggert ausschließlich auf annotierte Tags der Form
`vMAJOR.MINOR.PATCH[-PRERELEASE]` (keine Build-Metadaten, keine
leeren Prerelease-Identifier — siehe `scripts/validate-release-version.sh`).
Lightweight-Tags (`git tag vX.Y.Z` ohne `-a`) werden vom Workflow
explizit abgelehnt (`git cat-file -t` muss `tag` liefern, nicht
`commit`).

```bash
# Beispiel für eine stabile Major
git tag -a v1.0.0 -m "bess-ems v1.0.0 — M1–M6 closure"
git push origin v1.0.0

# Beispiel für einen Release-Candidate
git tag -a v1.1.0-rc1 -m "bess-ems v1.1.0-rc1"
git push origin v1.1.0-rc1
```

Prerelease-Tags (`-rc1`, `-beta2`, `-alpha.3`) sind erlaubt und werden
vom Workflow erkannt: sie erhalten **keinen** `:latest`-Tag auf der
Registry und werden als `prerelease: true` auf GitHub publiziert. Alle
übrigen Artefakte werden identisch produziert.

---

## 4. Was der Workflow tut

Der Tag-Push triggert
[`release.yml`](../../.github/workflows/release.yml). Der Workflow
läuft auf `ubuntu-24.04` und führt folgende Schritte in dieser
Reihenfolge aus:

1. **Tag validieren** via `scripts/validate-release-version.sh`
   (`vMAJOR.MINOR.PATCH[-PRERELEASE]`, keine Build-Metadaten, keine
   leeren Prerelease-Identifier). Derselbe Validator wird auch von
   `make release-assets` lokal verwendet — ein Pfad, eine Regel.
2. **Annotierten Tag erzwingen** via `git cat-file -t refs/tags/$tag`.
   Lightweight-Tags werden hart abgelehnt.
3. **Tag-Commit muss auf `origin/main` liegen.** Pflicht-Gate vor
   allen Push-Schritten via `git merge-base --is-ancestor`. Tags auf
   Side-Branches oder umgeschriebener Historie werden hier blockiert,
   bevor irgendetwas nach GHCR oder GitHub Releases läuft.
4. **Release-Notes extrahieren** aus dem `## [X.Y.Z]`-Block der
   `CHANGELOG.md`. Bewusst **vor** Build/Push/Sign/Release —
   ein fehlender oder leerer CHANGELOG-Block failt in Sekunden, nicht
   nach 90 Minuten Build mit verwaisten GHCR-Tags.
5. **`make fullbuild` mit `SOURCE_DATE_EPOCH`** aus dem Tag-Commit-
   Timestamp — alle M1–M6-Gates plus Compose-Smoke. Reproduzierbarkeits-
   Voraussetzung für SBOM und Image-Diffs.
6. **Runtime-Image** bauen mit OCI-Labels (`version`, `revision`,
   `source`), Tag `bess-ems:vX.Y.Z`.
7. **Image-Labels und Native-Library** verifizieren (`ldd` darf kein
   `not found` zeigen).
8. **Helm-Chart packen** via `helm package` mit `--version` und
   `--app-version` aus dem Tag.
9. **Source-Tarball** via `git archive --format=tar.gz
   --prefix=bess-ems-X.Y.Z/`.
10. **Native `.so` und Header** aus dem fertigen Runtime-Image
    extrahieren (`libbattery_control_core-vX.Y.Z-linux-x86_64.so` plus
    `battery_control_core.h`).
11. **SBOM** generieren (SPDX-JSON, Anchore Syft).
12. **GHCR-Login** mit dem workflow-eigenen `GITHUB_TOKEN`
    (Permission `packages: write`).
13. **Image-Push** nach `ghcr.io/pt9912/bess-ems` mit drei Tags:
    `:vX.Y.Z`, `:X.Y.Z` und — nur bei stabiler Version — `:latest`.
14. **Cosign keyless** signiert das Image über sigstore-OIDC
    (Permission `id-token: write`, kein Secret erforderlich). Die
    SBOM wird als Attestation an das Image gebunden.
15. **`SHA256SUMS`** über alle Release-Assets erzeugen.
16. **GitHub Release** anlegen via `softprops/action-gh-release`:
    Notes aus dem CHANGELOG-Block extrahiert, Assets angehängt
    (Image-Inspect-JSON, Helm-Chart-Tarball, Source-Tarball,
    Native `.so` + Header, SBOM, `SHA256SUMS`), `prerelease: true`
    bei `-PRERELEASE`-Suffix.

---

## 5. Manuelle Schritte nach dem Workflow

1. **Release-Notes review.** Der Workflow extrahiert die
   CHANGELOG-Sektion 1:1. Wenn Begleittext nötig ist (Migrations­
   hinweise, Operations-Warnungen), in der GitHub-UI nachtragen —
   nicht den Tag neu setzen.
2. **GHCR-Sichtbarkeit prüfen.** Das Paket steht nach dem ersten Push
   auf `private`; falls public gewünscht, einmalig in den GHCR-Paket-
   Einstellungen umstellen.
3. **Helm-Chart-Index aktualisieren.** Wenn ein `helm/charts`-Repo
   gepflegt wird (z. B. via `gh-pages`-Branch mit `index.yaml`), den
   Chart-Tarball dort einbinden — dieser Schritt ist **bewusst nicht
   im Workflow**, weil er repo-extern lebt.
4. **Cosign-Verifikation dokumentieren.** Für Konsumenten:
   `cosign verify ghcr.io/pt9912/bess-ems:vX.Y.Z --certificate-identity-regexp '^https://github.com/pt9912/bess-ems/.*' --certificate-oidc-issuer 'https://token.actions.githubusercontent.com'`.

---

## 6. Rollback / Yank

SemVer-Tags sind **immutable** — ein einmal publizierter Tag wird
nicht überschrieben. Rollback-Optionen, in aufsteigender Eingriffstiefe:

1. **Operator-Rollback.** Konsumenten pinnen auf den letzten bekannten
   guten Tag (`:vX.Y.Z-1`). Kein Repo-Eingriff nötig.
2. **Patch-Release.** Der schnellste sichere Pfad: Fix auf main,
   neuer Tag `vX.Y.(Z+1)`, normaler Release-Lauf.
3. **GitHub Release als Prerelease markieren.** Signal an Konsumenten
   per UI-Toggle — der Tag bleibt, aber das Release verschwindet aus
   der "latest"-Anzeige.
4. **Registry-Tag depublizieren.** Über die GHCR-Paket-Einstellungen
   die `:vX.Y.Z`/`:X.Y.Z`-Tags entfernen. `:latest` wird beim nächsten
   stabilen Release ohnehin überschrieben.
5. **Git-Tag löschen.** Nur im Notfall (z. B. Secret-Leak im Tag).
   `git push --delete origin vX.Y.Z` plus Re-Push nach Fix; die
   bisherige Release-Identität ist damit verbrannt, ein neuer Tag
   `vX.Y.(Z+1)` ist der saubere Pfad.

---

## 7. Lokale Trockenübung

`make release-assets VERSION=vX.Y.Z` produziert die gleichen Datei-
Artefakte lokal unter `artifacts/release-local/`, **ohne** Push, ohne
GHCR-Tags, ohne Cosign-Signatur und ohne GitHub-Release. Pflicht-Schritt
vor einem ersten Tag in einem neuen Major-/Minor-Zweig.

Schutz-Checks vor dem `rm -rf`:
- **RELEASE_DIR-Whitelist:** muss mit `artifacts/` beginnen, keine
  absoluten Pfade, kein `..`, nach Auflösung unter `${repo_root}/artifacts/`.
  Tippfehler wie `RELEASE_DIR=docs` werden hart abgewiesen.
- **Working-Tree-Sauberkeit:** `git status --porcelain` muss leer sein
  (sowohl tracked-modified als auch untracked). Sonst widersprächen
  `git archive HEAD` (Source-Tarball) und Working-Tree-Inputs für
  Helm/Docker/Header. Opt-out: `ALLOW_DIRTY=1` für dokumentierte
  Notfälle (typisch: Iteration am Asset-Pipeline-Script selbst).
- **Runtime-Image-Existenz:** `bess-ems-runtime:latest` muss vorhanden
  sein; das Target ruft `make build` als Vorbedingung selbst auf.

```bash
make release-assets VERSION=v1.0.0
ls artifacts/release-local/
```

Erwartete Dateien:
- `bess-ems-1.0.0.tgz` (Helm-Chart)
- `bess-ems-1.0.0-source.tar.gz`
- `libbattery_control_core-v1.0.0-linux-x86_64.so`
- `battery_control_core.h`
- `image-inspect.json`
- `sbom.spdx.json` (über `anchore/syft`-Container gegen das lokale
  Runtime-Image; setzt `/var/run/docker.sock`-Mount voraus)
- `SHA256SUMS`

Was die Trockenübung **nicht** abdeckt — bewusst, weil diese Schritte
gegen GitHub-/GHCR-/Sigstore-Infrastruktur sprechen:
- Image-Push nach GHCR
- Cosign keyless Sign und SBOM-Attestation
- GitHub Release-Erstellung
- Tag-auf-`origin/main`-Verifikation (lokal ist `HEAD` per Konstruktion
  der Stand, der gleich getaggt wird)
