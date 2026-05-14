# ADR 0010 - Boundary-Test-Tooling: NetArchTest

**Status:** Accepted - retrospektive Formalisierung der seit M1
gelebten Praxis. Schliesst `AR-OPEN-009`. NetArchTest.Rules ist das
Tool fuer Dependency-Rule- und Architektur-Tabu-Tests; ArchUnitNET
bleibt explizit verworfen.
**Datum:** 2026-05-14
**Bezug:**
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§4 (Hexagonale Sicht / Boundary-Tests), §18 (`AR-OPEN-009`),
[`../../../tests/BatteryEms.ArchitectureTests/`](../../../tests/BatteryEms.ArchitectureTests/)
(produktive Test-Suite),
[`../../user/quality.md`](../../user/quality.md) §1 (Lint/Arch-Check-Disziplin)

---

## 1. Kontext

`AR-OPEN-009` aus dem Architekturentwurf stand seit Projektstart offen:
"Boundary-Test-Tooling: NetArchTest oder ArchUnitNET?". Beide sind
.NET-Bibliotheken, die Architektur-Invarianten als Unit-Tests gegen
die kompilierten Assemblies pruefen.

Die Frage wurde implizit mit der Erstaufsetzung der
`BatteryEms.ArchitectureTests` in M1 entschieden: das Projekt referenziert
`NetArchTest.Rules` 1.3.2 und nutzt es seither in vier produktiven
Test-Klassen:

- `DependencyRuleTests` - Hexagonal-Schichten-Pruefung (Domain darf
  nicht auf Application zugreifen, Application nicht auf Adapter,
  Driving Adapter nicht auf Driven Adapter).
- `ArchitectureTabusTests` - Tabus wie "keine direkten DB-Calls aus
  Domain", "keine Newtonsoft.Json-Referenz", "kein direkter HTTP-Client
  ausserhalb von Adaptern".
- `MultiAssetHostCompositionTests` - asset-id-zentrierte Komposition
  pro ADR 0007.
- `MpcBackendCompositionTests` - MPC-Backend-Trennung pro ADR 0006.

`make arch-check` ist Pflicht-Gate in `make ci` und `make fullbuild`
und seit M1 gruen.

Die ADR holt die de-facto-Entscheidung in den Architekturentwurf nach,
damit `AR-OPEN-009` formal geschlossen ist und ein zukuenftiger Beitragender
nicht erneut die Wahl trifft.

---

## 2. Entscheidung

| Achse                | Entscheidung                                                                                  |
| -------------------- | --------------------------------------------------------------------------------------------- |
| Boundary-Test-Tool   | `NetArchTest.Rules` 1.3.2 (Version zentral gepinnt in `Directory.Packages.props`)             |
| Test-Projekt         | `tests/BatteryEms.ArchitectureTests/`                                                         |
| Make-Target          | `make arch-check` - Dependency-Rules, Tabus und Komposition als ein Docker-Build-Stage        |
| Verworfene Alternative | `ArchUnitNET`                                                                               |

### Warum NetArchTest

- **Lesbare Fluent-API.** Regeln wie `Types.InAssembly(Domain).ShouldNot()
  .HaveDependencyOn("BatteryEms.Adapters")` sind selbsterklaerend.
- **Geringer Setup-Aufwand.** Eine einzige NuGet-Referenz, kein
  zusaetzliches Konfigurations-Modell.
- **Stabile Version.** 1.3.2 ist die im Projekt verwendete Linie und
  blieb waehrend M1-M6 unveraendert; keine Breaking-Change-Wartung
  noetig.
- **Existierende Test-Suite.** Vier produktive Test-Klassen plus
  `ArchitectureTestHelpers.FormatFailures` sind eingespielt.

### Warum nicht ArchUnitNET

- **Hoeherer Konfigurationsaufwand.** Eigenes Setup mit
  `Architecture`-Modell-Loading, das fuer den heutigen Umfang von
  `bess-ems` (14 Produktiv-Projekte) keinen messbaren Mehrwert hat.
- **Andere API-Lineage.** Die API ist ein Port der Java-`ArchUnit`-
  Bibliothek; die NetArchTest-Fluent-API ist naeher an der bestehenden
  C#-Test-Code-Konvention im Projekt.
- **Kein konkretes Feature-Defizit.** Es gibt heute keinen Boundary-Test
  in `bess-ems`, den NetArchTest nicht ausdruecken koennte.

---

## 3. Konsequenzen

- `AR-OPEN-009` wechselt im Architekturentwurf §18 auf "Geschlossen
  mit ADR 0010 - NetArchTest.Rules ist das Boundary-Test-Tooling."
- Neue Architektur-Invarianten werden in `BatteryEms.ArchitectureTests`
  als NetArchTest-Regeln hinzugefuegt, nicht als alternatives Tool
  oder als Reflection-basierter Eigenbau.
- Ein Wechsel auf ArchUnitNET (oder ein anderes Tool) ist nicht
  ausgeschlossen, aber braucht eine Folge-ADR mit konkretem Anlass
  (z. B. unausdrueckbare Regel, Performance-Problem, gravierende
  Bibliotheks-Risiken). Bis dahin bleibt NetArchTest die Single
  Choice.

---

## 4. Trigger fuer ein Re-Open

Eine erneute Pruefung lohnt sich, wenn einer dieser Punkte eintritt:

- NetArchTest.Rules wird ueber 12+ Monate nicht mehr gewartet (kein
  Release, offene Security-Issues unbeantwortet).
- Eine konkrete Architektur-Invariante laesst sich nicht mehr in
  NetArchTest ausdruecken (Beispiel: Cross-Assembly-Datenfluss-Analyse,
  die ueber simple Referenz-Graphen hinausgeht).
- Die Test-Suite skaliert nicht mehr (heute Teil der bestehenden
  CI-Laufzeit ohne messbaren Overhead; Schwelle waere ein spuerbarer
  Anteil am `make ci`-Gesamtlauf).
