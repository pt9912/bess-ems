# Plan: Preisquellen- und Forecast-Adapter

**Dokumenttyp:** Slice-Skizze / offen
**Status:** Open - wartet auf Quellen-/Produkttrigger
**Datum:** 2026-05-24
**Quelle:** [`note-market-and-colocation-followups.md`](note-market-and-colocation-followups.md)
**Bezug:**
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md),
[`../done/plan-RM-M5-07.md`](../done/plan-RM-M5-07.md)

---

## Ziel

`bess-ems` soll Preis- und Forecast-Daten nicht nur manuell importieren,
sondern ueber austauschbare Quellenadapter beziehen koennen.

Der bestehende `PriceSeries`-/`IPriceSeriesSource`-Pfad bleibt die
Application-Grenze. Externe Providerlogik gehoert in Adapter, nicht in
Domain, Optimierung oder Regelkreis.

---

## Ausgangslage

Heute vorhanden:

- `PriceSeries`
  - `MarketBidArea`
  - `Product`
  - `PriceKind`
  - `Unit`
  - `Source`
  - Horizont, Zeitschritt und Werte
- `IPriceSeriesSource`
- `IPriceSeriesImportSink`
- `POST /markets/price-series/import`
- Nutzung von Preisreihen in Day-Ahead-/Intraday-Optimierung

Nicht vorhanden:

- produktive externe Preisquellenadapter
- Forecast-Zeitreihen fuer Load, PV, Wind oder Wetter
- Quellenstatus, Refresh-Status, Rate-Limit-/Cache-Regeln
- provider-spezifische Authentisierung

---

## Quellenkandidaten

### Marktpreise

- EPEX SPOT
  - Day-Ahead-Preise
  - Intraday- oder Indexdaten, sofern lizenziert und verfuegbar
  - Erwarteter Nutzen: produktive Preisbasis fuer Optimierung

- Open Power System Data oder andere offene historische Datasets
  - Benchmark-/Replay-Daten
  - Erwarteter Nutzen: Tests, Demo, Regression

### Systemdaten und Forecasts

- ENTSO-E Transparency Platform
  - Load forecast
  - Wind/solar generation forecast
  - Cross-border flows und Generation by type, soweit relevant
  - Erwarteter Nutzen: Co-Location, Forecast-Sidecar, Plausibilisierung

- Deutsche TSO-Daten
  - 50Hertz, Amprion, TenneT, TransnetBW
  - Erwarteter Nutzen: hochaufgeloeste deutsche Grid-/Renewables-Daten

- Open-Meteo / Copernicus / ECMWF
  - Temperatur, Wind, Einstrahlung, Wolkenbedeckung
  - Erwarteter Nutzen: Forecast-Features fuer PV/Wind/Load

- Fuel-/CO2-Quellen
  - Gas, Coal, CO2
  - Erwarteter Nutzen: Preisprognose-Features, nicht kurzfristig fuer den
    technischen Dispatch noetig

---

## Scope bei Aktivierung

### Phase 1: Adapter-Vertrag und Import-Hardening

- Quellenneutrales Adapterinterface oberhalb externer Provider:
  - `LoadPriceSeriesAsync`
  - `LoadForecastSeriesAsync` oder separater Forecast-Port
  - explizite `Source`, `RetrievedAt`, `ValidFrom`, `ValidTo`
- Cache-/Refresh-Vertrag:
  - TTL
  - max stale age
  - provider rate limit
  - operatorfaehige Fehlercodes
- API-/Operator-Status fuer Quellen:
  - letzter erfolgreicher Abruf
  - letzter Fehler
  - Datenhorizont
  - Quelle und Produkt

### Phase 2: Erste produktive Quelle

Eine Quelle wird als erster produktiver Adapter gewaehlt. Kandidaten:

- ENTSO-E fuer Forecast-/Systemdaten
- Open-Meteo fuer Wetterfeatures
- EPEX nur, wenn Zugriff, Lizenz und Nutzungsbedingungen geklaert sind

Die erste Quelle sollte vorzugsweise eine freie oder testbar mockbare
Quelle sein, damit CI-/Replay-Gates ohne externe Credentials gruen
bleiben.

### Phase 3: Forecast-Sidecar-Input

Wenn Forecasting nicht im EMS laufen soll, definiert dieser Slice nur den
Input-/Output-Vertrag fuer einen Forecast-Sidecar:

- Input:
  - Preis-Historie
  - Load forecast
  - Wind forecast
  - Solar forecast
  - Wetter
  - Kalenderfeatures
- Output:
  - `PriceSeries` fuer Punktforecast
  - optional spaeter: Quantile oder Szenariopfade

Probabilistische Forecasts sind ein Folgeslice; der heutige Optimierer
arbeitet mit deterministischen Preiswerten.

---

## Nicht-Ziele

- Kein Scraping ohne geklaerte Nutzungsrechte.
- Kein Forecasting-Modell im Domain- oder Regelkreis.
- Kein Vendor-/Credential-Secret im Repository.
- Kein harter Netzaufruf in Unit Tests.
- Keine produktive Intraday-Continuous-Orderbook-Strategie im ersten Slice.

---

## Liefergegenstaende bei Aktivierung

1. Folge-ADR oder Architektur-Schaerfung fuer externe Datenquellen.
2. Adapter-Port fuer Preis-/Forecast-Quellen oder Erweiterung des
   bestehenden `IPriceSeriesSource`-Umfelds.
3. Quellenstatusmodell inklusive Refresh-/Fehlerstatus.
4. Mindestens ein Adapter mit Mock-/Replay-faehigem Testpfad.
5. API- oder Operator-UI-Status fuer Datenquellen.
6. Tests:
   - erfolgreiche Serienladung
   - fehlende Credentials
   - Rate-Limit-/Providerfehler
   - stale Daten werden markiert oder abgelehnt
   - Zeitzone/DST bleibt konsistent
   - Optimierung bekommt exakt die erwartete Schrittzahl
7. Runbook fuer Credentials, Rate Limits und Provider-Ausfall.

---

## Akzeptanzkriterien

- Optimierung kann eine importierte oder extern geladene Preisreihe
  ohne Codepfad-Unterschied nutzen.
- Fehlende oder veraltete Quelldaten fuehren zu einem klaren
  operatorfaehigen Fehler, nicht zu implizitem Fallback auf falsche Werte.
- Externe Datenquellen sind in Tests durch Replay-/Fixture-Daten ersetzbar.
- Quellenadapter verletzen keine Architekturregel: keine Markt- oder
  Regelentscheidung im Adapter.
- Provider-Lizenz, Authentisierung und Nutzungsbedingungen sind vor
  produktiver Aktivierung dokumentiert.

---

## Offene Entscheidungen

- Wird eine generische `ForecastSeries` neben `PriceSeries` eingefuehrt?
- Welche Quelle ist der erste produktive Adapter?
- Sollen Forecast-Szenarien/Quantile direkt modelliert oder erst in einem
  separaten probabilistischen Optimierungsslice behandelt werden?
- Wo lebt langfristiger Quellen-Cache: In-Memory, Postgres/Timescale oder
  externer Datenservice?

