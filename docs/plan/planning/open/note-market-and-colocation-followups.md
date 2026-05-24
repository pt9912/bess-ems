# Notiz: Markt-, Co-Location- und Forecast-Folgearbeiten

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen - Folgearbeiten aus externem Fachmaterial
**Datum:** 2026-05-24
**Quelle-Repo:** Öffentliches Referenzmaterial – externe Orientierung, keine Code-Übernahme.
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md),
[`../../../user/bess-ems-function.md`](../../../user/bess-ems-function.md),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)

---

## Zweck

Diese Notiz sammelt fachliche Folgearbeiten aus der Sichtung externer
Referenzdokumente (siehe **Quelle-Repo** oben), die als fachlicher Input dienen,
aber keine direkte Codeübernahme darstellen.
Sie ist kein aktiver Implementierungsplan, sondern ein Trigger-Watch-Artefakt
für Marktmodell, Co-Location, Preisquellen und Forecast-Inputs.

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

- `DFBEW_HP_extern_Batteriespeicher_Frontier_Economics_2602.pdf` (liegt in den gesichteten Referenzunterlagen)
  - Geschäftsmodelle, regulatorischer Rahmen, Deutschland/Frankreich,
    Co-Location, Day-Ahead, Intraday, Systemdienstleistungen und Risiken.
  - Nutzen: Validierung von `LH-MKT-*`, Regelleistungsfolgearbeiten,
    Produkt-/Compliance-Gates und Betreiber-Risiken.

- `suena_energy_Whitepaper_Co-Location.pdf` (liegt in den gesichteten Referenzunterlagen)
  - Co-Located-Speicher, Hybridmodelle mit Netzbezug, Speicher-
    Überbauung, Grünstromspeicher, Multi-Market-Optimierung und
    Netzrestriktionen.
  - Nutzen: Fachliche Vorlage für einen Co-Location-/Hybrid-BESS-Slice.

- `BESS_Forecasting_Trading_Strategies.pptx` (liegt in den gesichteten Referenzunterlagen)
  - Revenue Streams, Day-Ahead-only-Grenzen, Intraday-/Balancing-
    Opportunitäten, Multi-Day-Planning, negative Preise, RL-Ausblick.
  - Nutzen: Produkt-/Roadmap-Input für Marktlogik, Operator-UI und
    Forecast-/Optimierungsgrenzen.

### Mittlere Relevanz

- `Data_sources_list.docx` (liegt in den gesichteten Referenzunterlagen)
  - EPEX, TSO-Daten, Wetter, Fuel, CO2, OPSD.
  - Nutzen: Quellenkatalog für Preis- und Forecast-Adapter.

- `Feature_selection.docx` und
  `WattWise Feature Selection_20260310.docx` (liegen in den gesichteten Referenzunterlagen)
  - Lag-Features, residual load, Wind-/Solar-Forecast, Load-Forecast,
    Wetter, Gas, Coal, CO2, Kalenderfeatures.
  - Nutzen: Input-Vertrag für Forecast-Sidecar oder externe
    Forecast-Provider, nicht für den technischen Regelkreis.

- `European_BESS_Optimizers_Landscape.pptx` (liegt in den gesichteten Referenzunterlagen)
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

**Quelle:** DFBEW/Frontier, suena Co-Location Whitepaper,
`BESS_Forecasting_Trading_Strategies.pptx`

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
- Für den ersten Slice ist ein produktiver Forecast-Adapter keine zwingende
  Voraussetzung; fehlender externer Forecast-Zugriff schaltet die
  forecast-basierte Co-Location-Nutzung in einen kontrollierten degraded/fallback-Modus.

**Aktivierungs-Pfad:** eigener Slice-Plan
[`plan-market-colocation-model.md`](plan-market-colocation-model.md).

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
  selben Slice erforderlich (Validierungsmodus), ohne produktive Förderlogik im
  Regelkreis (Eingangskonformität nur).

---

## Item F-MKT-02: Preisquellen- und Forecast-Adapter

**Quelle:** Data-sources- und Feature-selection-Dokumente, Forecasting-
Papers, `BESS_Forecasting_Trading_Strategies.pptx`

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
- Mindestens zwei Forecast-Adapter je produktiv aktivierter Forecast-Familie
  (1 Primär + 1 fallbackfähiger Adapter; z. B. Last oder Wetter) mit
  Zeitstempelung, Versionskennzeichen und Qualitätskennzahlen.
- Operativer Importpfad mit automatischer Aktualisierung, Toleranzgrenzen
  und Alarmierung bei Datenlücken.
- Qualitätsschema enthält kombinierte Status-Sichtbarkeit (Fallback + Degradation)
  ohne Informationsverlust für Operator/Rückverfolgbarkeit.

## Definition of Done (DoD)

- [ ] Externe Quellenanalyse abgeschlossen (DFBEW, Co-Location, Forecast-Trading, zusätzliche Medium-Quellen) und fachlich klassifiziert.
- [ ] F-MKT-01-Abnahmebedingungen sind dokumentiert und für mindestens diese drei
  produktiven Betriebsformen (`StandaloneBess`, `ClassicalCoLocation`, `HybridWithGridImport`) definiert.
- [ ] `GreenStorageRestricted` ist als separater Sondermodus im selben Slice als
  Validierungsmodus dokumentiert.
- [ ] F-MKT-02-Abnahmebedingungen sind dokumentiert (je 2 Adapter je Typ: 1 Primär + 1 Fallback + Fallback-/Degradation-Modell, Aktualisierung + Alarmierung).
- [ ] Trigger-Koordination bei parallelen Auslösern ist definiert und konfliktfrei;
  autoritativ ist der folgende Abschnitt
  [Trigger-Koordination bei gleichzeitigen Auslösern](#trigger-koordination-bei-gleichzeitigen-auslösern).
- [ ] Copyright- und Nutzungsgrenzen sind explizit dokumentiert und für externe Claims verifiziert.

## Trigger-Koordination bei gleichzeitigen Auslösern

- Wenn beide Trigger (F-MKT-01 und F-MKT-02) im selben Release-Fenster ausgelöst werden:
  1. `F-MKT-01` (Markt-/Co-Location-Modell) wird zuerst im operativen Produktivpfad freigegeben.
  2. `F-MKT-02` kann parallel als Datenvertrags-Slice vorbereitet werden, darf Co-Location aber zunächst im degraded/fallback-Modus betreiben.
  3. Produktiv geht erst auf vollen Forecast-Fokus über, wenn `plan-price-forecast-adapters.md` im
    Betriebsmodus mit aktivierter Qualitätsakzeptanz konform ist (`series_status` in `{SOURCE_OK, SOURCE_FALLBACK_USED}` bei
    `quality_mode=strict`, oder `series_status` in `{SOURCE_OK, SOURCE_DEGRADED, SOURCE_FALLBACK_USED}` bei
    `quality_mode=degraded_ok`).
    Die genaue `SOURCE_FALLBACK_USED`-Zulässigkeit in `strict` folgt der
    Statusmatrix in [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).
    Für die Abgrenzung gelten die Serienbegriffe (`series_type`, `series_product`, `market_bid_area`) exakt nach
    [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).
  4. Parallelbetrieb ist erst freigegeben, wenn `CanExecute`-Semantik zwischen
     `plan-market-colocation-model.md` und
     `plan-ler-fcr-reserve-robustness.md` verbindlich abgeglichen ist.
  5. Bei Konflikten gilt als harte Regel: neue Betriebslogik darf nicht ohne definierte
     `series_status`-Entscheidung in den Markt- und Optimierungsworkflow starten.
     Für nicht adapter-getragene Serien im bestehenden Altpfad ist ein kontrollierter
     Übergang nur über den expliziten degradierten Serienzustand erlaubt
     (`series_status=SOURCE_DEGRADED`, optional mit `status_flags` wie
     `SOURCE_BACKFILL`).

---

## Copyright- und Nutzungsgrenze

Die Dokumente werden fachlich ausgewertet, aber nicht textlich
übernommen. Für externe Dokumente, Produktmaterial oder öffentliche
Claims müssen Quellenstatus, Nutzungsrechte und Aktualität separat
überprüft werden.
