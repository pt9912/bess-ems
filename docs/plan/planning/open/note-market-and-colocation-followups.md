# Notiz: Markt-, Co-Location- und Forecast-Folgearbeiten

**Dokumenttyp:** Vorabklaerung / Trigger-Watch
**Status:** Offen - Folgearbeiten aus externem Fachmaterial
**Datum:** 2026-05-24
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md),
[`../../../user/bess-ems-function.md`](../../../user/bess-ems-function.md),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)

---

## Zweck

Diese Notiz sammelt fachliche Folgearbeiten, die aus der Sichtung der
Dokumente unter `/Development/grid/BESSelligence/Docs` fuer `bess-ems`
relevant sind. Sie ist kein aktiver Implementierungsplan, sondern ein
Trigger-Watch-Artefakt fuer Marktmodell, Co-Location, Preisquellen und
Forecast-Inputs.

Die vorhandene `bess-ems`-Basis deckt bereits zentrale Bausteine ab:

- Day-Ahead- und Intraday-Fahrplaene
- Horizon-Optimierung mit OR-Tools/GLOP
- Regelleistungsreserve ueber `ReserveBand`
- Aktivierungspfad fuer Regelleistung
- source-neutraler `PriceSeries`-/`IPriceSeriesSource`-Pfad
- Operator-UI-Grundlagen

Die Dokumente sind daher nicht als Codequelle zu verwenden, sondern als
Fachvalidierung und als Input fuer produktnahe Folge-Slices.

---

## Bewertete Quellen

### Hohe Relevanz

- `/Development/grid/BESSelligence/Docs/DFBEW_HP_extern_Batteriespeicher_Frontier_Economics_2602.pdf`
  - Geschaeftsmodelle, regulatorischer Rahmen, Deutschland/Frankreich,
    Co-Location, Day-Ahead, Intraday, Systemdienstleistungen und Risiken.
  - Nutzen: Validierung von `LH-MKT-*`, Regelleistungsfolgearbeiten,
    Produkt-/Compliance-Gates und Betreiber-Risiken.

- `/Development/grid/BESSelligence/Docs/suena_energy_Whitepaper_Co-Location.pdf`
  - Co-Located-Speicher, Hybridmodelle mit Netzbezug, Speicher-
    Ueberbauung, Gruenstromspeicher, Multi-Market-Optimierung und
    Netzrestriktionen.
  - Nutzen: fachliche Vorlage fuer einen Co-Location-/Hybrid-BESS-Slice.

- `/Development/grid/BESSelligence/Docs/BESS_Forecasting_Trading_Strategies.pptx`
  - Revenue Streams, Day-Ahead-only-Grenzen, Intraday-/Balancing-
    Opportunitaeten, Multi-Day Planning, negative Preise, RL-Ausblick.
  - Nutzen: Produkt-/Roadmap-Input fuer Marktlogik, Operator-UI und
    Forecast-/Optimierungsgrenzen.

### Mittlere Relevanz

- `/Development/grid/BESSelligence/Docs/Data_sources_list.docx`
  - EPEX, ENTSO-E, TSO-Daten, Wetter, Fuel, CO2, OPSD.
  - Nutzen: Quellenkatalog fuer Preis- und Forecast-Adapter.

- `/Development/grid/BESSelligence/Docs/Feature_selection.docx`
  und `/Development/grid/BESSelligence/Docs/WattWise Feature Selection_20260310.docx`
  - Lag-Features, residual load, Wind-/Solar-Forecast, Load-Forecast,
    Wetter, Gas, Coal, CO2, Kalenderfeatures.
  - Nutzen: Input-Vertrag fuer Forecast-Sidecar oder externe
    Forecast-Provider, nicht fuer den technischen Regelkreis.

- `/Development/grid/BESSelligence/Docs/European_BESS_Optimizers_Landscape.pptx`
  - Wettbewerbs-/Produktpositionierung.
  - Nutzen: Benchmarking fuer Feature-Scope und UI-Sprache; Aussagen zu
    Firmen, Funding und Uplift muessen vor externer Verwendung
    verifiziert werden.

### Hintergrund

- Forecasting-Papers zu Day-Ahead, Intraday, probabilistischen Forecasts,
  SHAP und Deep Learning.
  - Nutzen: spaetere Forecast- oder Sidecar-Architektur, nicht direkter
    EMS-Core-Scope.

---

## Item F-MKT-01: Co-Location-/Hybrid-BESS-Modell

**Quelle:** DFBEW/Frontier, suena Co-Location Whitepaper,
`BESS_Forecasting_Trading_Strategies.pptx`

**Problem heute:** `bess-ems` optimiert und dispatcht Batterie-Assets,
aber das Marktmodell unterscheidet noch nicht explizit zwischen
Standalone-BESS, klassischer Co-Location, Hybridmodell mit Netzbezug und
foerder-/herkunftsgebundenem Gruenstromspeicher.

**Trigger** (eines reicht):

- Ein Standort kombiniert PV/Wind und BESS hinter einem gemeinsamen
  Netzanschlusspunkt.
- Ein Betreiber verlangt Optimierung unter Netzanschlusslimit,
  Einspeiselimit, lokalen Erzeugungsprognosen und Speicherueberbauung.
- Ein Vermarktungsmodell verlangt getrennte Behandlung von Standalone,
  Hybrid mit Netzbezug und foerdergebundenem Gruenstromspeicher.

**Aktivierungs-Pfad:** eigener Slice-Plan
[`plan-market-colocation-model.md`](plan-market-colocation-model.md).

---

## Item F-MKT-02: Preisquellen- und Forecast-Adapter

**Quelle:** Data-sources- und Feature-selection-Dokumente, Forecasting-
Papers, `BESS_Forecasting_Trading_Strategies.pptx`

**Problem heute:** `bess-ems` besitzt einen source-neutralen
`PriceSeries`-Pfad und einen Import-Endpunkt, aber keine produktiven
externen Quellenadapter fuer EPEX/ENTSO-E/Open-Meteo oder fuer
Forecast-Serien.

**Trigger** (eines reicht):

- Produktiver Day-Ahead-/Intraday-Workflow soll Preise nicht mehr
  manuell importieren.
- Ein Co-Location-Slice braucht PV-/Wind-/Load-Forecasts fuer den
  Optimierungshorizont.
- Ein Betreiber verlangt auditierbare Quellenmetadaten und Refresh-
  Status fuer Markt- und Forecast-Daten.

**Aktivierungs-Pfad:** eigener Slice-Plan
[`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).

---

## Copyright- und Nutzungsgrenze

Die Dokumente werden fachlich ausgewertet, aber nicht textlich
uebernommen. Fuer externe Doku, Produktmaterial oder oeffentliche
Claims muessen Quellenstatus, Nutzungsrechte und Aktualitaet separat
geprueft werden.

