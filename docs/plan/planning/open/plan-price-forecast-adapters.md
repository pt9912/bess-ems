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
- verbindlicher Serienvertrag fuer Zeitachse, Freshness, Gap-Handling und Fehlercodes

## Verbindlicher Daten- und Fehlervertrag

Einheitliche Adaptervertraege für Preis- und Forecastdaten:

### Gemeinsame Serien-Typsignatur (verbindlich)

- Provider-Ports liefern ein einheitliches `SeriesEnvelope`:
  - `LoadPriceSeriesAsync`
  - `LoadForecastSeriesAsync` oder separater Forecast-Port
- Der bestehende `PriceSeries`-/`IPriceSeriesSource`-Pfad bleibt die
  Application-Grenze; `SeriesEnvelope`-Objekte müssen in bestehende Domain-Serien
  überführt werden.
- Der minimale `SeriesEnvelope` enthält:
  - `series_id` (stabil)
  - `site_id` (optional)
    - erforderlich für standortgebundene Forecast-/Erzeugungs-/Last-/Wetter-Reihen
    - optional oder leer für produktweite Preisreihen ohne Standortkontext
  - `series_type` (`price` oder `forecast`)
  - `series_product` (z. B. `day_ahead`, `intraday`, `load`, `pv`, `wind`, `weather-temp`)
  - `unit` (z. B. `EUR/MWh`, `kW`, `kWh`, etc.)
  - `resolution_minutes` (ganzzahlig)
  - `timezone` (muss `UTC` sein)
  - `horizon_start_utc`, `horizon_end_utc`
  - `values` als geordnete Punkte:
    - `timestamp_utc`
    - `value`
    - `value_type` (`actual` | `forecast`)
    - optional `confidence` (nur spätere Extensions)
  - `source_metadata`:
    - `provider_id`
    - `license_id`
    - `retrieved_at_utc`
    - `valid_from_utc`, `valid_to_utc`
    - `provider_request_id`
- Validierungspflicht:
  - strikte UTC-Zeitachse
  - Schrittweite exakt `resolution_minutes`
  - keine Duplikate in `timestamp_utc`
  - keine NaN/Inf-Werte
  - gleiche Horizontlänge/Range je Request
  - Preisreihen haben konsistente Preis-Einheit
  - Forecastreihen haben konsistente physikalische Einheit
- Mapping-Regel:
  - `series_type=price` wird auf bestehende Preis-Produkte in `PriceSeries` abgebildet.
  - `series_type=forecast` dient in diesem Slice der Seitcar-/Inputlogik und darf nur in
    der dort definierten Integrationslogik weiterverarbeitet werden, solange kein
    produktiver Forecast-Domaintyp im EMS aktiv ist.
- Konsistenz-Regel:
  - Wenn `series_type=price`, ist `site_id` optional.
  - Wenn `series_type=forecast` und Standorttrennung aktiv ist, ist `site_id` Pflicht.

### Freshness- und Gap-Policy (verbindlich)

- `max_stale_age_minutes`:
  - Preisreihen: 90 Minuten (default, pro Serie konfigurierbar)
  - Forecastreihen: 720 Minuten (default, pro Feature konfigurierbar)
- `quality_mode`:
  - `strict` (Default): Keine Degradation erlaubt.
  - `degraded_ok`: Degradierte Nutzung erlaubt, Ergebnis muss als `SOURCE_DEGRADED` gekennzeichnet werden.
- `min_coverage_ratio` Pflicht:
  - mindestens 99,5 %
  - Bezug auf Rohdaten vor Backfill (`raw_values_coverage`).
  - Die konsolidierte konsumierbare Serie muss nach Backfill keine offenen Lücken mehr enthalten.
- Lückenregime:
  - Rohdaten mit Lücken sind vor Backfill zulässig, solange `raw_values_coverage` die Mindestquote erreicht.
  - kontrollierter Backfill darf maximal 2 aufeinanderfolgende Intervalle pro Lücke schließen.
  - bei Backfill gilt der finale Datenvertrag weiterhin (keine offenen Restlücken).
  - Serien mit Backfill markieren Ergebnisqualität als `SOURCE_DEGRADED`.

### Qualitätsentscheidungen bei `SOURCE_*` (verbindlich)

- `SOURCE_GAP` bedeutet harte Ablehnung (`SOURCE_REJECTED`), weil die Zielserie im
  Endzustand lückenfrei sein muss.
- `SOURCE_AUTH_ERROR` ist harter Stopp (`SOURCE_REJECTED`) ohne automatischen Retry/Fallback,
  da eine explizite Operator-Intervention für Credentials nötig ist.
- `SOURCE_EMPTY` ist harte Ablehnung (`SOURCE_REJECTED`), sofern kein zugelassenes
  Fallback aktiv ist.
- `SOURCE_RETRY_EXHAUSTED` folgt denselben Regeln wie `SOURCE_UNAVAILABLE`:
  Fallback nur bei erlaubt konfiguriertem Ersatzpfad, sonst harte Ablehnung.
- Wenn `quality_mode=strict`:
  - `SOURCE_STALE` -> harte Ablehnung (`SOURCE_REJECTED`).
  - `SOURCE_DEGRADED` aus Backfill kann nicht akzeptiert werden (`SOURCE_REJECTED`).
- Wenn `quality_mode=degraded_ok`:
  - `SOURCE_DEGRADED` ist als degradeierter Zustand akzeptierbar.
  - `SOURCE_STALE` darf als degradeierter Zustand akzeptiert werden, wenn keine harten
    Datenkonflikte (Schema/Horizont/Fehlende Pflichtfelder) auftreten.
- In beiden Modi gilt:
  - `SOURCE_RATE_LIMIT`, `SOURCE_EMPTY`, `SOURCE_RETRY_EXHAUSTED`, `SOURCE_SCHEMA_MISMATCH`,
    `SOURCE_UNAVAILABLE` bleiben hart,
    sofern kein vertragskonformer Fallback aktiv wird.

### Fehler- und Ablaufcodes

- `SOURCE_OK` – erfolgreich und validiert
- `SOURCE_AUTH_ERROR` – Authentifizierung fehlt/fehlerhaft
- `SOURCE_RATE_LIMIT` – Rate-Limit erreicht / Retry empfohlen
- `SOURCE_UNAVAILABLE` – Provider temporär nicht erreichbar
- `SOURCE_EMPTY` – leere Provider-Antwort
- `SOURCE_STALE` – Daten jenseits `max_stale_age_minutes`
- `SOURCE_GAP` – nicht behebbare Zeitlücken
- `SOURCE_SCHEMA_MISMATCH` – Zeitachse/Einheit/Schemafehler
- `SOURCE_RETRY_EXHAUSTED` – Retries erfolglos
- `SOURCE_FALLBACK_USED` – kontrollierter Fallback aktiv
- `SOURCE_REJECTED` – harte Qualitätsprüfung fehlgeschlagen
- `SOURCE_DEGRADED` – kontrollierter Backfill oder andere Teilqualitätsminderung

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
  - verbindliches `SeriesEnvelope` gemäß obigem Datenvertrag
  - deterministisches Mapping in die bestehende Import-Pipeline (`IPriceSeriesSource`).
- Cache-/Refresh-Vertrag:
  - TTL
  - `max_stale_age_minutes`
  - provider rate limit + Retry-Backoff
  - operatorfahige Fehlercodes inkl. `SOURCE_*`
- API-/Operator-Status fuer Quellen:
  - letzter erfolgreicher Abruf + aktiver Statuscode
  - letzter Fehler
  - letzter Fehlercode (`SOURCE_*`)
  - Datenhorizont
  - Quelle und Produkt
- Primärkennzeichnung je `series_type` (`primary`, `fallback`, `disabled`)
- Deterministische Reaktionsregeln:
  - `SOURCE_OK`: normaler Betrieb
  - `SOURCE_AUTH_ERROR`: harte Ablehnung (`SOURCE_REJECTED`), kein Retry/Fallback.
  - `SOURCE_RATE_LIMIT` / `SOURCE_STALE` / `SOURCE_UNAVAILABLE` / `SOURCE_EMPTY` /
    `SOURCE_RETRY_EXHAUSTED`:
    - Primär wird kontrollierter Fallback auf `fallback`-Quelle versucht, sofern vorhanden.
    - Fallback nur akzeptieren, wenn `SeriesEnvelope`, Einheit und Horizon exakt kompatibel sind.
    - Bei Ausfall / Schemakonflikt des Fallbacks:
      - bei `quality_mode=degraded_ok` und Primärcode `SOURCE_STALE`: degradierte Fortsetzung als `SOURCE_DEGRADED` möglich
      - sonst harte Fehlklassifikation (`SOURCE_REJECTED`)
  - `SOURCE_STALE` ohne konfigurierte Fallback-Quelle:
    - bei `quality_mode=strict`: harte Fehlklassifikation (`SOURCE_REJECTED`)
    - bei `quality_mode=degraded_ok`: degradierte Fortsetzung (`SOURCE_DEGRADED`)
  - `SOURCE_EMPTY` / `SOURCE_RETRY_EXHAUSTED` ohne akzeptablen Fallback:
    - immer `SOURCE_REJECTED`

### Phase 2: Erste produktive Quelle

Primär-/Fallback-Regel ist verbindlich:

- Preis:
  - Primär: EPEX (nur wenn Zugriff, Lizenz und Nutzungsbedingungen geklaert sind)
  - Fallback: Open Power System Data / Replay-konforme Datenquelle
- Forecast:
  - Primär: ENTSO-E
  - Fallback: Open-Meteo oder Copernicus für Wetter, je Featuretyp

Aktivierungslogik:

- Primärquelle wird standardmaessig genutzt.
  - Bei Qualitätsfehler (`SOURCE_STALE`, `SOURCE_RATE_LIMIT`, `SOURCE_UNAVAILABLE`, `SOURCE_EMPTY`, `SOURCE_RETRY_EXHAUSTED`, `SOURCE_SCHEMA_MISMATCH`)
    wird der Fallbackzugriff nur genutzt, wenn:
   - derselbe `SeriesEnvelope`-Vertrag eingehalten wird
   - Lücke/Abweichung im Fallback innerhalb definierter Konfigurationsgrenzen bleibt
   - Operator den Fallbackstatus explizit akzeptiert
- Ohne akzeptablen Fallback gilt:
  - `SOURCE_STALE`:
    - bei `quality_mode=degraded_ok`: `SOURCE_DEGRADED`
    - sonst `SOURCE_REJECTED`
  - `SOURCE_RATE_LIMIT` / `SOURCE_UNAVAILABLE` / `SOURCE_SCHEMA_MISMATCH`: `SOURCE_REJECTED`
  - `SOURCE_EMPTY` / `SOURCE_RETRY_EXHAUSTED`: `SOURCE_REJECTED`
  - `SOURCE_AUTH_ERROR`: `SOURCE_REJECTED` (ohne Fallback)

Zusätzlich:

- Falls EPEX Lizenz nicht geklaert ist, startet Phase 2 direkt mit Fallback-Basisquelle
  und dokumentiert den Lizenzaufhebungsplan im Runbook.

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
  - optional spaeter: `ForecastSeries`/Quantile oder Szenariopfade in separatem Slice

Vertraglich gilt der gleiche `SeriesEnvelope` für den Sidecar-Output (Zeitachse, Einheit, Source-Metadaten, Qualitätscode).

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
   - Primary-Validierung gegen Secondary-Adapter bei Primary-Ausfall
   - Rate-Limit-/Providerfehler
   - stale Daten werden je nach `quality_mode` korrekt gefuehrt (`SOURCE_DEGRADED` vs `SOURCE_REJECTED`)
   - Zeitzone/DST bleibt konsistent
   - UTC-Zeitachse, Schrittweite und Lückenbehandlung (99,5 % Mindestabdeckung)
   - Optimierung bekommt exakt die erwartete Schrittzahl
   - operatorfahige Laufcodes werden bei Fehlerpfaden gesetzt (`SOURCE_*`)
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
- Jede Quelle-Fehlerklasse aus `SOURCE_*` ist im Run-Status und Operator-UI
  explizit sichtbar.
- Provider-Lizenz, Authentisierung und Nutzungsbedingungen sind vor
  produktiver Aktivierung dokumentiert.
- Fallback-Verhalten ist vor Aktivierung je Serie konfiguriert (erlaubt/unterbunden)
  und dokumentiert.

---

## Offene Entscheidungen

- Wird in einem ersten produktiven Slice zusätzlich `ForecastSeries`-Schema eingeführt?
  - Ja, als erweiterter Liefervertrag, solange es ein kompatibles `SeriesEnvelope` bleibt.
- Sollen Forecast-Szenarien/Quantile direkt modelliert oder erst in einem
  separaten probabilistischen Optimierungsslice behandelt werden?
- Wo lebt langfristiger Quellen-Cache: In-Memory, Postgres/Timescale oder
  externer Datenservice?
