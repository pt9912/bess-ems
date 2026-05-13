# ADR 0007 — Multi-Asset Hosting Strategy

**Status:** Accepted — shared Worker mit per-Asset fan-out ist der
Default fuer M6. Worker-pro-Asset bleibt ein Deployment-/Isolation-
Pattern fuer harte Fault-Domain-, Mandanten- oder Edge-Anforderungen.
Schliesst `AR-OPEN-006`.
**Datum:** 2026-05-13
**Bezug:**
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§18 (`AR-OPEN-006`),
[`../planning/in-progress/plan-RM-M6.md`](../planning/in-progress/plan-RM-M6.md)
(RM-M6-02),
[`../planning/in-progress/plan-RM-M6-02.md`](../planning/in-progress/plan-RM-M6-02.md)
(Detail-Slice),
[`0005-optimization-core-sidecar-transport.md`](0005-optimization-core-sidecar-transport.md)
(Sidecar-Topologie),
[`0006-mpc-kernel-backend-and-solver.md`](0006-mpc-kernel-backend-and-solver.md)
(MPC-Backend-Wahl)

---

## 1. Kontext

`AR-OPEN-006` fragt, ob Multi-Asset-Hosting per Worker-pro-Asset oder
shared Worker laufen soll. Die bestehende Implementierung ist bereits
naeher am shared-Worker-Modell:

- `ControlCycleHostedService` iteriert pro Tick ueber
  `IBatteryAssetRegistry.GetAll()` und fuehrt den Control-Cycle pro
  `assetId` aus.
- `IBatteryAssetRegistry` liefert ein Snapshot aller registrierten
  Assets; `InMemoryBatteryAssetRegistry` speichert per `assetId`.
- Domain-, Schedule-, Optimization-, MpcRun-, Command-, Metrik- und
  Trace-Pfade tragen bereits `assetId` als fachliche Achse.
- M5-Sidecar und MPC sind pro Request asset-bezogen; eine per-Asset-
  Sidecar-Topologie ist als Trigger-Watch dokumentiert, aber nicht der
  heutige Default.

Damit ist die Frage nicht, ob ein einzelner Host mehrere Assets technisch
sehen kann, sondern welches **Betriebsmodell** normativ wird: ein Host
mit kontrolliertem per-Asset fan-out, oder ein Prozess/Pod pro Asset.

---

## 2. Entscheidung

M6 nimmt **shared Worker mit per-Asset fan-out** als Default an.

| Achse | Entscheidung |
| ----- | ------------ |
| Default-Topologie | Ein Worker/API-Host kann mehrere Assets verwalten und pro Tick ueber die registrierten Assets fan-outen. |
| Isolation | Fehler eines Assets duerfen den Tick anderer Assets nicht stoppen; per-Asset Fehler werden geloggt, metrisiert und isoliert behandelt. |
| Ausfuehrung | Initial sequenzieller fan-out bleibt erlaubt. Parallelisierung ist ein eigener Performance-Slice, sobald Tick-Dauer oder Asset-Anzahl es erzwingt. |
| Konfiguration | Asset-Liste bleibt explizite Konfiguration; M6 muss Multi-Asset-Config validieren und eindeutige `asset_id`s erzwingen. |
| Persistenz/Observability | `asset_id` bleibt Pflichtachse in Commands, Schedules, Telemetrie, Runs, Logs, Metriken und Traces. |
| Sidecars | Ein Sidecar kann shared genutzt werden, solange Requests asset-bezogen und idempotent bleiben. Per-Asset-Sidecar wird erst bei Isolation-/Performance-Trigger aktiviert. |
| Worker-pro-Asset | Zulaessiges Deployment-Pattern fuer Mandantenisolation, harte Fault Domains, Edge-Controller-Naehe oder regulatorische Trennung, aber nicht M6-Default. |

---

## 3. Begruendung

- **Passt zum Code.** Der Worker besitzt bereits einen per-Asset fan-out
  und zentrale Ports sind `assetId`-faehig. Worker-pro-Asset als Default
  wuerde die vorhandene Struktur umgehen statt sie zu haerten.
- **Einfachere Operator-UI.** Ein shared Worker bietet eine einheitliche
  Flottenansicht und verhindert, dass die UI mehrere Host-Instanzen als
  primaere Fachstruktur modellieren muss.
- **Bessere lokale Optimierungsbasis.** Flotten- und Netzpunktlogik
  braucht spaeter Cross-Asset-Kontext. Ein Prozess pro Asset macht diese
  Koordination frueh teurer.
- **Deployment bleibt flexibel.** Kubernetes kann shared Worker skalieren
  oder Worker-pro-Asset deployen, ohne das Domain-Modell zu aendern.
- **Keine harte Echtzeitbehauptung.** Sequenzieller fan-out ist fuer den
  M6-Default akzeptabel; Performance- oder Edge-Trigger duerfen spaeter
  parallelisieren oder auslagern.

---

## 4. Konsequenzen

- RM-M6-02 muss den bestehenden shared fan-out haerten statt eine neue
  Prozess-pro-Asset-Architektur einzufuehren.
- Multi-Asset-Konfiguration braucht Validierung fuer eindeutige IDs,
  leere Asset-Listen, doppelte Adapterbindungen und unklare
  Kommando-Sink-Zuordnung.
- Control-Cycle-Fehlerpfade bleiben per Asset isoliert; ein Fehler darf
  nicht den kompletten Tick abbrechen.
- Metrik-, Log-, Trace- und Persistenzabfragen muessen `asset_id` als
  erste Filterachse behalten.
- Kubernetes/Helm modelliert shared Worker als Default-Chart; Worker-
  pro-Asset kann ueber Values/Release-Topologie abgebildet werden.
- Per-Asset-Sidecar oder Sidecar-Pool wird nicht automatisch aktiviert;
  Trigger bleiben Performance, Fault-Isolation, Mandantentrennung oder
  Asset-spezifische Backend-Wahl.

---

## 5. Trigger fuer Abweichung

Worker-pro-Asset oder paralleler fan-out wird neu bewertet, wenn eines
dieser Signale eintritt:

- Ein Asset-Fehler kann trotz lokaler Fehlerbehandlung andere Assets
  messbar stoeren.
- Tick-Dauer ueberschreitet mit mehreren Assets reproduzierbar den
  konfigurierten `CycleInterval`-Budgetrahmen.
- Mandanten-, Betreiber- oder Zertifizierungsgrenzen verlangen getrennte
  Prozesse, Pods, Secrets oder Audit-Domaenen.
- Ein Edge-Controller-Pfad verlangt Asset-nahe Ausfuehrung mit eigener
  Lifecycle- und Restart-Domaene.
- Asset-spezifische MPC-/Sidecar-Backends werden produktiv notwendig.

---

## 6. Umsetzungspfad

1. RM-M6-02 dokumentiert diese Entscheidung im Slice-Plan und schliesst
   `AR-OPEN-006` in `spec/architecture.md`.
2. Multi-Asset-Konfigurationsvalidierung und ein Orchestrierungs-Pin
   beweisen den shared-Worker-Default.
3. RM-M6-03 baut Kubernetes/Helm auf dem shared-Worker-Default auf.
4. RM-M6-01 nutzt die Asset-Liste als UI-Auswahl-/Filterachse.
5. Abweichungen laufen ueber eigene Folge-ADR oder Slice-Plan-Aenderung.
