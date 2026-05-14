# ADR 0011 - BatteryEms.Application bleibt monolithisches Modul

**Status:** Accepted - `BatteryEms.Application` bleibt ein einzelnes
`csproj` mit Sub-Namespaces fuer Realtime, Control, Markets,
Optimization, Mpc und die uebrigen Application-Verantwortungen. Ein
Split in mehrere Projekte ist trigger-basiert und kein impliziter
Wachstumsschritt. Schliesst `AR-OPEN-008`.
**Datum:** 2026-05-14
**Bezug:**
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§4 (Hexagonale Sicht), §18 (`AR-OPEN-008`),
[`../../../src/hexagon/BatteryEms.Application/`](../../../src/hexagon/BatteryEms.Application/)
(produktives Modul),
[`../../../tests/hexagon/BatteryEms.Application.Tests/`](../../../tests/hexagon/BatteryEms.Application.Tests/)
(zugehoerige Test-Suite),
[`0009-api-service-extraction-criteria.md`](0009-api-service-extraction-criteria.md)
(gleiche Struktur fuer den API-Auskopplungsfall),
[`0010-boundary-test-tooling.md`](0010-boundary-test-tooling.md)
(NetArchTest erzwingt die Hexagonal-Schichten projektuebergreifend
und auf Namespace-Ebene innerhalb von Application)

---

## 1. Kontext

`AR-OPEN-008` aus dem Architekturentwurf stand seit Projektstart offen:
"Wann wird `BatteryEms.Application` in eigene Projekte (Realtime,
Control, Markets, Optimization) gesplittet, oder bleibt es ein Modul
mit Namespaces?".

Stand 2026-05-14:

| Metrik                              | Wert                                                                                                          |
| ----------------------------------- | ------------------------------------------------------------------------------------------------------------- |
| Quelldateien                        | 116                                                                                                           |
| Lines of Code                       | 6.841                                                                                                         |
| Sub-Namespaces                      | 12 — `Api`, `Assets`, `Configuration`, `Control`, `IO`, `Markets`, `Mpc`, `Observability`, `Optimization`, `Persistence`, `Realtime`, `Time` |
| Test-Projekt                        | `tests/hexagon/BatteryEms.Application.Tests` (eines)                                                          |
| Boundary-Test-Enforcement           | `BatteryEms.ArchitectureTests` per NetArchTest (siehe ADR 0010) prueft Domain↔Application↔Adapter und Tabus  |
| Anteil am Gesamt-Code               | ein Modul von 14 Produktiv-Projekten; Application liegt in der gleichen Groessenordnung wie Domain + Host    |

Sub-Namespaces innerhalb von Application reflektieren die fachlichen
Verantwortungsbereiche des Hexagonal-Application-Layers: Markets +
Optimization steuern die Marktlogik, Control + Realtime den Regelkreis,
Mpc die MPC-Linie, Configuration/Time/Observability/IO/Persistence/Api
sind Querschnittsverantwortungen.

Heute spuerbare Friction-Punkte: **keine.**

- `make test` (Application-Unit-Tests inkl.) ist Teil der M1-Pflicht-
  Gates und laeuft im erwarteten CI-Budget.
- `make arch-check` (NetArchTest) erzwingt die Boundary-Regeln stabil
  und macht Disziplinverstoesse innerhalb von Application sichtbar.
- Es gibt keine konkurrierenden Owner pro Sub-Namespace mit Merge-
  Konflikt-Historie.
- Inkrementelle Builds einzelner Sub-Namespaces sind nicht zu langsam
  geworden.

---

## 2. Entscheidung

| Achse                | Entscheidung                                                                                                |
| -------------------- | ----------------------------------------------------------------------------------------------------------- |
| Projektstruktur      | `BatteryEms.Application` bleibt ein einziges `csproj` mit Sub-Namespaces.                                   |
| Modularitaet         | Modularitaet wird auf Namespace-Ebene gepflegt; Cross-Namespace-Tabus koennen bei Bedarf in NetArchTest hinzugefuegt werden. |
| Test-Suite           | `BatteryEms.Application.Tests` bleibt ein Test-Projekt; sollte sich die Laufzeit als problematisch erweisen, ist ein Test-Sub-Set per `Category=...` der erste Hebel, nicht ein Test-Projekt-Split. |
| Verworfen fuer jetzt | Praeventiver Split in `BatteryEms.Application.Realtime`, `.Control`, `.Markets`, `.Optimization`, `.Mpc` o. ae. |

Begruendung: Diese ADR folgt derselben Logik wie
[ADR 0009](0009-api-service-extraction-criteria.md) - eine vorzeitige
Strukturentscheidung erkauft Komplexitaet (mehr `csproj`, mehr
NuGet-Lock-Dateien, mehr `Directory.Build.props`-Disziplin, mehr
Project-References, mehr Boundary-Test-Surface) ohne erkennbaren
Nutzen. Der heutige Application-Layer ist gross genug, dass eine
Modularisierung *vorstellbar* ist, aber klein genug, dass sie noch
keine messbare Reibung verursacht.

---

## 3. Trigger fuer einen Split

Ein Split soll geprueft werden, sobald **mindestens einer** der
folgenden Punkte messbar eintritt. Trigger sind absichtlich
quantitativ formuliert, damit "es fuehlt sich gross an" kein Trigger
ist:

- **Build-Zeit-Trigger:** `dotnet build src/hexagon/BatteryEms.Application/`
  (warm, ohne Restore) dauert reproduzierbar > 10 s.
- **Test-Zeit-Trigger:** `make test` braucht reproduzierbar > 2 min,
  und die Mehrkosten gehen nachweisbar auf Application.Tests zurueck
  (nicht auf die Driven-Adapter-Tests oder HIL-Gates).
- **Ownership-Trigger:** Zwei Verantwortungsbereiche
  (z. B. Markets vs. Mpc) haben in einem Quartal mehrfach
  Merge-Konflikte innerhalb von Application, weil sie unabhaengig
  voneinander entwickelt werden.
- **Boundary-Trigger:** NetArchTest-Regeln werden so detailliert, dass
  sie Cross-Namespace-Tabus innerhalb von Application abbilden muessen
  und dabei unuebersichtlich werden (Faustregel: > 5 Tabu-Regeln
  zwischen Application-Sub-Namespaces).
- **API-Surface-Trigger:** Externer Konsument
  (z. B. Cross-Repo-Konsument der `IControlKernel`- oder
  `IMpcModelSolver`-Interfaces) braucht einen schlankeren
  NuGet-Footprint, der nur den jeweiligen Sub-Namespace bringt.
- **Cycle-Trigger:** Eine Aenderung an Application zwingt
  reproduzierbar zum Re-Compile von >= 8 anderen Projekten,
  obwohl der fachliche Touch nur ein Sub-Namespace ist.

---

## 4. Mindestvoraussetzungen fuer einen Split

Falls ein Trigger zuendet, gelten dieselben Disziplin-Anforderungen
wie bei ADR 0009 §4:

- Eigene Folge-ADR mit Begruendung des Triggers, Schnittstellen-
  Inventar pro neuem `csproj` und Anpassungs-Plan fuer
  `BatteryEms.ArchitectureTests`.
- `BatteryEms.Application` darf nicht weiterexistieren als Bibliothek
  mit nur einer Klasse; entweder echter Modul-Split mit klar
  zuordenbaren Verantwortungen oder kein Split.
- NetArchTest-Regeln werden in der gleichen ADR-Welle aktualisiert,
  damit Boundary-Enforcement nicht zwischenzeitlich kippt.
- `Directory.Packages.props` und `packages.lock.json` werden konsistent
  fuer alle neuen Projekte angelegt; kein Wildwuchs an Versions-Pins.
- Single-Image-Deployment bleibt der Default-Pfad
  (analog zu ADR 0009): Split ist intern, der Host bleibt ein OCI-Image.

---

## 5. Konsequenzen

- `AR-OPEN-008` wechselt im Architekturentwurf §18 auf "Geschlossen
  mit ADR 0011 - monolithisches Application-Modul mit
  Namespace-Modularitaet; Split ist trigger-basiert."
- Neue fachliche Verantwortungen werden als neuer Sub-Namespace
  innerhalb von `BatteryEms.Application` angelegt, nicht als neues
  `csproj` "weil es sich groesser anfuehlt".
- Sollte sich ein Sub-Namespace als Architektur-Tabu-Verletzungs-
  Magnet entwickeln, wird zuerst die NetArchTest-Regel verschaerft -
  ein Split ist die letzte Massnahme, nicht die erste.
- Diese ADR ist kein Bekenntnis "niemals splitten"; sie ist ein
  Bekenntnis "splitten erst, wenn messbarer Schmerz da ist".
