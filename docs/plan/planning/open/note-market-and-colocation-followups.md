# Notiz: Markt-, Co-Location- und Forecast-Folgearbeiten

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen - Folgearbeiten aus externem Fachmaterial
**Datum:** 2026-05-24
**Quelle-Repo:** [`BESSelligence`](https://github.com/Ati-PK/BESSelligence) (Ordner `Docs`)
**Bezug:**
[`../in-progress/roadmap.md`](../in-progress/roadmap.md),
[`../../../user/bess-ems-function.md`](../../../user/bess-ems-function.md),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)

---

## Zweck

Diese Notiz sammelt fachliche Folgearbeiten, die aus der Sichtung der
Dokumente aus dem Git-Repository `BESSelligence` (siehe **Quelle-Repo** oben) im Ordner `Docs`
relevant sind. Sie ist kein aktiver Implementierungsplan, sondern ein
Trigger-Watch-Artefakt für Marktmodell, Co-Location, Preisquellen und
Forecast-Inputs.

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

- `DFBEW_HP_extern_Batteriespeicher_Frontier_Economics_2602.pdf` (liegt im Repo `BESSelligence`, Ordner `Docs`)
  - Geschäftsmodelle, regulatorischer Rahmen, Deutschland/Frankreich,
    Co-Location, Day-Ahead, Intraday, Systemdienstleistungen und Risiken.
  - Nutzen: Validierung von `LH-MKT-*`, Regelleistungsfolgearbeiten,
    Produkt-/Compliance-Gates und Betreiber-Risiken.

- `suena_energy_Whitepaper_Co-Location.pdf` (liegt im Repo `BESSelligence`, Ordner `Docs`)
  - Co-Located-Speicher, Hybridmodelle mit Netzbezug, Speicher-
    Überbauung, Grünstromspeicher, Multi-Market-Optimierung und
    Netzrestriktionen.
  - Nutzen: Fachliche Vorlage für einen Co-Location-/Hybrid-BESS-Slice.

- `BESS_Forecasting_Trading_Strategies.pptx` (liegt im Repo `BESSelligence`, Ordner `Docs`)
  - Revenue Streams, Day-Ahead-only-Grenzen, Intraday-/Balancing-
    Opportunitäten, Multi-Day-Planning, negative Preise, RL-Ausblick.
  - Nutzen: Produkt-/Roadmap-Input für Marktlogik, Operator-UI und
    Forecast-/Optimierungsgrenzen.

### Mittlere Relevanz

- `Data_sources_list.docx` (liegt im Repo `BESSelligence`, Ordner `Docs`)
  - EPEX, ENTSO-E, TSO-Daten, Wetter, Fuel, CO2, OPSD.
  - Nutzen: Quellenkatalog für Preis- und Forecast-Adapter.

- `Feature_selection.docx` und
  `WattWise Feature Selection_20260310.docx` (liegen im Repo `BESSelligence`, Ordner `Docs`)
  - Lag-Features, residual load, Wind-/Solar-Forecast, Load-Forecast,
    Wetter, Gas, Coal, CO2, Kalenderfeatures.
  - Nutzen: Input-Vertrag für Forecast-Sidecar oder externe
    Forecast-Provider, nicht für den technischen Regelkreis.

- `European_BESS_Optimizers_Landscape.pptx` (liegt im Repo `BESSelligence`, Ordner `Docs`)
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
Standalone-BESS, klassischer Co-Location, Hybridmodell mit Netzbezug und
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

- Marktmodell unterscheidet klar die drei Betriebsformen
  (Standalone, klassische Co-Location, Hybrid mit Netzbezug).
- Für jede Form ist ein Constraint-Satz definiert
  (Netzanschlussleistung, Einspeisung, lokale Erzeugung, Speicher
  Überbauung).
- Für den ersten Slice existiert mindestens ein validierter Testfall je
  Betriebsform inkl. Rechen- und Ergebnisbericht.
- `GreenStorageRestricted` ist im gleichen Slice nur im **Validierungsmodus**
  erlaubt und dient zunächst nur zur Eingangskonformität (kein produktiver
  Förderlogik-Umfang im Regelkreis).

---

## Item F-MKT-02: Preisquellen- und Forecast-Adapter

**Quelle:** Data-sources- und Feature-selection-Dokumente, Forecasting-
Papers, `BESS_Forecasting_Trading_Strategies.pptx`

**Problem heute:** `bess-ems` besitzt einen source-neutralen
`PriceSeries`-Pfad und einen Import-Endpunkt, aber keine produktiven
externen Quellenadapter für EPEX/ENTSO-E/Open-Meteo oder für
Forecast-Serien.

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

- Mindestens zwei produktive Preis-Adapter (z. B. EPEX + ENTSO-E) mit
  dokumentiertem Fallback-Verhalten, Quellenmetadaten und Refresh-Status.
- Mindestens zwei Forecast-Adapter (z. B. Wetter + Lastprognose) mit
  Zeitstempelung, Versionskennzeichen und Qualitätskennzahlen.
- Operativer Importpfad mit automatischer Aktualisierung, Toleranzgrenzen
  und Alarmierung bei Datenlücken.

---

## Copyright- und Nutzungsgrenze

Die Dokumente werden fachlich ausgewertet, aber nicht textlich
übernommen. Für externe Dokumente, Produktmaterial oder öffentliche
Claims müssen Quellenstatus, Nutzungsrechte und Aktualität separat
überprüft werden.
