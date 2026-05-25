# Notiz: Markt-, Co-Location- und Forecast-Folgearbeiten

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen - Folgearbeiten aus externem Fachmaterial
**Datum:** 2026-05-24
**Quelle:** Öffentlich benanntes Referenzmaterial – externe Orientierung,
keine Code-Übernahme; Namen dienen nur der Nachvollziehbarkeit der
fachlichen Einordnung.
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md),
[`../../../user/bess-ems-function.md`](../../../user/bess-ems-function.md),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)

---

## Zweck

Diese Notiz sammelt fachliche Folgearbeiten aus der Sichtung externer
Referenzdokumente (siehe **Quelle** oben), die als fachlicher Input dienen,
aber keine direkte Codeübernahme darstellen.
Sie ist kein aktiver Implementierungsplan, sondern ein Trigger-Watch-Artefakt
für Marktmodell, Co-Location, Preisquellen und Forecast-Inputs.
Die Readiness-Checkboxen in dieser Notiz bedeuten deshalb: Die
Trigger-Spezifikation ist so präzise, dass bei Auslösung ein Slice ohne weitere
Begriffs- oder Abnahmeklärung gestartet werden kann.

Die vorhandene `bess-ems`-Basis deckt bereits zentrale Bausteine ab:

- Day-Ahead- und Intraday-Fahrpläne
- Horizon-Optimierung mit OR-Tools/GLOP
- Regelleistungsreserve über `ReserveBand`
- Aktivierungspfad für Regelleistung
- source-neutraler `PriceSeries`-/`IPriceSeriesSource`-Pfad
- Operator-UI-Grundlagen

Die Dokumente sind daher nicht als Codequelle zu verwenden, sondern als
Fachvalidierung und als Input für produktnahe Folge-Slices.

---

## Bewertete Quellen

### Hohe Relevanz

- Frontier-Economics-/DFBEW-Referenzmaterial zu Batteriespeicher-
  Geschäftsmodellen in Deutschland/Frankreich:
  - Geschäftsmodelle, regulatorischer Rahmen, Co-Location, Day-Ahead,
    Intraday, Systemdienstleistungen und Risiken.
  - Nutzen: Validierung von `LH-MKT-*`, Regelleistungsfolgearbeiten,
    Produkt-/Compliance-Gates und Betreiber-Risiken.

- Co-Location-Whitepaper eines BESS-Optimierungsanbieters:
  - Co-Located-Speicher, Hybridmodelle mit Netzbezug, Speicher-
    Überbauung, Grünstromspeicher, Multi-Market-Optimierung und
    Netzrestriktionen.
  - Nutzen: Fachliche Vorlage für einen Co-Location-/Hybrid-BESS-Slice.

- Produkt-/Strategiematerial zu BESS-Forecasting und Trading:
  - Revenue Streams, Day-Ahead-only-Grenzen, Intraday-/Balancing-
    Opportunitäten, Multi-Day-Planning, negative Preise, RL-Ausblick.
  - Nutzen: Produkt-/Roadmap-Input für Marktlogik, Operator-UI und
    Forecast-/Optimierungsgrenzen.

### Mittlere Relevanz

- Quellenkatalog für Markt- und Wetterdaten:
  - EPEX, TSO-Daten, Wetter, Fuel, CO2, OPSD.
  - Nutzen: Quellenkatalog für Preis- und Forecast-Adapter.

- Forecast-Feature-Selection-Material:
  - Lag-Features, residual load, Wind-/Solar-Forecast, Load-Forecast,
    Wetter, Gas, Coal, CO2, Kalenderfeatures.
  - Nutzen: Input-Vertrag für Forecast-Sidecar oder externe
    Forecast-Provider, nicht für den technischen Regelkreis.

- Marktüberblick zu europäischen BESS-Optimierern:
  - Wettbewerbs-/Produktpositionierung.
  - Nutzen: Benchmarking für Feature-Scope und UI-Sprache; Aussagen zu
    Firmen, Funding und Uplift müssen vor externer Verwendung
    verifiziert werden.

### Hintergrund

- Forecasting-Papers zu Day-Ahead, Intraday, probabilistischen Forecasts,
  SHAP und Deep Learning.
  - Nutzen: spätere Forecast- oder Sidecar-Architektur, nicht direkter
    EMS-Core-Scope.

---

## Item F-MKT-01: Co-Location-/Hybrid-BESS-Modell

**Quelle:** DFBEW/Frontier-Referenzmaterial, Co-Location-Whitepaper,
BESS-Forecasting-/Trading-Strategiematerial

**Problem heute:** `bess-ems` optimiert und dispatcht Batterie-Assets,
aber das Marktmodell unterscheidet noch nicht explizit zwischen
Standalone-BESS, `ClassicalCoLocation`, Hybridmodell mit Netzbezug und
förder-/herkunftsgebundenem Grünstromspeicher.

**Trigger** (eines reicht):

- Ein Standort kombiniert PV/Wind und BESS hinter einem gemeinsamen
  Netzanschlusspunkt.
- Ein Betreiber verlangt Optimierung unter Netzanschlusslimit,
  Einspeiselimit, lokalen Erzeugungsprognosen und Speicherüberbauung.
- Ein Vermarktungsmodell verlangt getrennte Behandlung von Standalone,
  Hybrid mit Netzbezug und fördergebundenem Grünstromspeicher.

**Aktivierungs-Pfad:** eigener Slice-Plan
[`plan-market-colocation-model.md`](plan-market-colocation-model.md).
Für den ersten Slice ist ein produktiver Forecast-Adapter keine zwingende
Voraussetzung; fehlender externer Forecast-Zugriff schaltet die
forecast-basierte Co-Location-Nutzung in einen kontrollierten
`quality_mode=degraded_ok`-Übergangspfad. Dieser Pfad ist ein
Architektur-/Aktivierungszugeständnis, kein eigener Trigger.

**Abnahmekriterien:**

- Marktmodell unterscheidet klar mindestens die drei produktiven Betriebsformen
  (`StandaloneBess`, `ClassicalCoLocation`, `HybridWithGridImport`) und behandelt
  `GreenStorageRestricted` als optionalen Sondermodus.
- Für jede Form ist ein Constraint-Satz definiert
  (Netzanschlussleistung, Einspeisung, lokale Erzeugung, Speicher
  Überbauung).
- Für den ersten Slice existieren mindestens je ein validierter Testfall für die
  drei produktiven Betriebsformen (`StandaloneBess`, `ClassicalCoLocation`, `HybridWithGridImport`) inkl. Rechen- und Ergebnisbericht.
- Für `GreenStorageRestricted` sind im ersten Slice eigene Validierungsfälle im
  selben Slice erforderlich. Validierungsmodus bedeutet vollständige
  Solver-Formulierung mit harten Constraints, aber ohne produktive
  Förder-/Marktautomatik im Regelkreis.

---

## Item F-MKT-02: Preisquellen- und Forecast-Adapter

**Quelle:** Data-sources- und Feature-selection-Referenzmaterial,
Forecasting-Papers, BESS-Forecasting-/Trading-Strategiematerial

**Problem heute:** `bess-ems` besitzt einen source-neutralen
`PriceSeries`-Pfad und einen Import-Endpunkt, aber keine produktiven
externen Quellenadapter für EPEX/OPSD (Preis) oder ENTSO-E/Open-Meteo (Forecast),
sowie Forecast-Serien.

**Trigger** (eines reicht):

- Produktiver Day-Ahead-/Intraday-Workflow soll Preise nicht mehr
  manuell importieren.
- Ein Co-Location-Slice braucht PV-/Wind-/Load-Forecasts für den
  Optimierungshorizont.
- Ein Betreiber verlangt auditierbare Quellenmetadaten und Refresh-
  Status für Markt- und Forecast-Daten.

**Aktivierungs-Pfad:** eigener Slice-Plan
[`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).

**Abnahmekriterien:**

- Mindestens zwei produktive Preis-Adapter (z. B. EPEX + OPSD
  Open-Power-System-Data) mit
  dokumentiertem Fallback-Verhalten, Quellenmetadaten und Refresh-Status.
  Falls EPEX beim Start nicht als `primary` freigegeben ist, darf OPSD oder eine
  Replay-/Basisquelle temporär `primary` sein; das zählt nicht als fehlender
  Adapter, solange ein fallbackfähiger zweiter Pfad dokumentiert ist.
- Mindestens zwei Forecast-Adapter für die produktiv verpflichtenden
  Forecast-Familien `load` und `weather-temp`
  (je 1 Primär + 1 fallbackfähiger Adapter) mit
  Zeitstempelung, Versionskennzeichen und Qualitätskennzahlen.
- Für produktive forecast-basierte Co-Location mit PV-/Wind-Erzeugungsreihen
  sind zusätzlich je aktivierter lokaler Erzeugungsfamilie (`pv`, `wind`) ein
  Primär- und ein fallbackfähiger Adapter oder ein explizit dokumentierter
  `quality_mode=degraded_ok`-Übergangspfad erforderlich.
- Operativer Importpfad mit automatischer Aktualisierung, Toleranzgrenzen
  und Alarmierung bei Datenlücken.
- Qualitätsschema enthält kombinierte Status-Sichtbarkeit (Fallback + Degradation)
  ohne Informationsverlust für Operator/Rückverfolgbarkeit.

## Item F-LER-01: Aktivierungsdauer als Steuerparameter

**Quelle:** Regelleistungs-/LER-Robustheitsfolge aus
[`plan-ler-fcr-reserve-robustness.md`](plan-ler-fcr-reserve-robustness.md)

**Problem heute:** `required_activation_minutes` ist im ersten
LER/FCR-Slice nur Operatorkontext und trägt `audit_only=true`. Kein
Laufverhalten darf davon abhängen.

**Trigger** (eines reicht):

- Ein Produkt- oder Regelleistungsfall verlangt
  `required_activation_minutes` als harte Optimierungs- oder
  Dispatch-Constraint.
- Operator-/Replay-Auswertung reicht nicht mehr aus, weil Aktivierungsdauer
  direkt in Restore-, Reserve- oder Fahrplanentscheidungen eingehen muss.

**Aktivierungs-Pfad:** eigener Migrations-/Folgeslice auf Basis von
[`plan-ler-fcr-reserve-robustness.md`](plan-ler-fcr-reserve-robustness.md).

**Abnahmekriterien:**

- Feldvalidierung, Persistenz-/API-Ausgabe und alle Konsumenten werden gemeinsam
  migriert.
- Der Wechsel von `audit_only=true` zu steuerndem Verhalten ist explizit
  versioniert und replaybar.

## Trigger-Readiness-Checkliste

Legende: `[T]` bedeutet "Trigger-Spezifikation scharf genug". Es bedeutet
nicht "Implementierung abgeschlossen"; offene DoD-Items bleiben in den
verlinkten Plänen maßgeblich.

- [T] Externe Quellenanalyse ist als Trigger-Spezifikation abgeschlossen (DFBEW,
  Co-Location, Forecast-Trading, zusätzliche Medium-Quellen) und fachlich
  klassifiziert; interne Pflicht-Gates wie
  [`Domain-Migration OptimizationRun.CanExecute`](plan-domain-migration-optimization-run-can-execute.md)
  bleiben davon unberührt und müssen vor Slice-Aktivierung erfüllt sein.
- [T] F-MKT-01-Abnahmebedingungen sind als Slice-Startkriterien dokumentiert und
  für mindestens diese drei produktiven Betriebsformen (`StandaloneBess`,
  `ClassicalCoLocation`, `HybridWithGridImport`) definiert.
- [T] `GreenStorageRestricted` läuft im ersten Slice als Validierungsmodus mit
  harten Constraints; produktive Förderautomatik bleibt einem späteren Slice
  vorbehalten.
- [T] F-MKT-02-Abnahmebedingungen sind als Slice-Startkriterien dokumentiert
  (Preis: 1 Primär + 1 Fallback; Forecast: je 2 Adapter für `load` und
  `weather-temp`; bei forecast-basierter Co-Location zusätzlich `pv`/`wind`
  je aktivierter lokaler Erzeugungsfamilie oder ein expliziter
  `degraded_ok`-Übergangspfad; jeweils Fallback-/Degradation-Modell,
  Aktualisierung + Alarmierung).
- [T] F-LER-01 ist als Trigger für den späteren Wechsel von
  `required_activation_minutes` aus reinem Audit-Kontext in steuerndes
  Laufverhalten dokumentiert.
- [T] Trigger-Koordination bei parallelen Auslösern verweist auf kanonische
  Aktivierungsquellen; Übersicht steht im folgenden Abschnitt
  [Trigger-Koordination bei gleichzeitigen Ausloesern](#trigger-koordination-bei-gleichzeitigen-ausloesern).
- [T] Copyright- und Nutzungsgrenzen sind als Prüfpunkte für spätere Slices
  explizit dokumentiert.

## Trigger-Koordination bei gleichzeitigen Ausloesern

Diese Note ist kein normativer Gate-Vertrag. Sie verweist nur auf die
kanonischen Quellen, wenn mehrere Trigger gleichzeitig aktiv werden:

- Gemeinsame Ausführungsentscheidung und `*_ok`-Combiner:
  [`Domain-Migration OptimizationRun.CanExecute`](plan-domain-migration-optimization-run-can-execute.md)
- Gemeinsame Assetmodell-Grenze `eta_min`:
  [`Domain-Migration AssetModel.ValidationHelper`](plan-domain-migration-asset-model-validation-helper.md)
- Serienidentität und Store-/Mapper-Migration:
  [`Domain-Migration PriceSeries.Identity`](plan-domain-migration-price-series-identity.md)
- `solver_scope`-Audit für produktive Replays ohne immutable Request-Snapshot:
  [`OptimizationRun.SolverScopeAudit`](plan-domain-migration-optimization-run-solver-scope-audit.md)
- Forecast-/Adapter-Aktivierung, `source_ok`, Transitional-Input und
  Legacy-Sunset:
  [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md)
- Co-Location-Produktivpfade und Übergangsadapter-Verbrauch:
  [`plan-market-colocation-model.md`](plan-market-colocation-model.md)

---

## Copyright- und Nutzungsgrenze

Die Dokumente werden fachlich ausgewertet, aber nicht textlich
übernommen. Für externe Dokumente, Produktmaterial oder öffentliche
Claims müssen Quellenstatus, Nutzungsrechte und Aktualität separat
überprüft werden.
