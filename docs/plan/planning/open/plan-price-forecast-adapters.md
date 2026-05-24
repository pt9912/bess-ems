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

Kompatibilitäts-/Migrationsprinzip:

- Das bestehende `PriceSeries`-Import-Subsystem bleibt vollständig funktionsfähig.
- `IForecastSeriesSource` wird als zusätzliche, kompatible Schnittstelle eingeführt.
- `SeriesEnvelope` dient als einheitlicher Zwischenvertrag:
  - bestehende Preisimporte nutzen weiterhin `IPriceSeriesSource`,
  - Forecast/Forecast-Adapter nutzen `IForecastSeriesSource`,
  - beide werden vor dem Optimierer über denselben Mapping-Layer auf die aktuelle Domäne projiziert.
- In der Einführungsphase gilt "Dual-Path":
  - Altpfad unverändert lauffähig,
  - Neupfade können aktiv/inaktiv geschaltet werden,
  - späterer Wechsel auf den neuen Pfad ohne Schnittstellenbruch durch einheitlichen Mapper.

## Verbindlicher Daten- und Fehlervertrag

Einheitliche Adaptervertraege für Preis- und Forecastdaten:

### Gemeinsame Serien-Typsignatur (verbindlich)

- Provider-Ports liefern ein einheitliches `SeriesEnvelope`:
  - `LoadAsync` auf dem bestehenden `IPriceSeriesSource`-Port
  - `LoadForecastAsync` auf neuem `IForecastSeriesSource`-Port
    (separate Schnittstelle zur Preis-/Forecast-Domain, kein paralleler `LoadPriceSeriesAsync`-Name ohne explizite Migrationsphase)
- Der bestehende `PriceSeries`-/`IPriceSeriesSource`-Pfad bleibt die
  Application-Grenze; `SeriesEnvelope`-Objekte müssen in bestehende Domain-Serien
  überführt werden.
- Der minimale `SeriesEnvelope` enthält:
  - `series_id` (stabil)
  - `series_version` (stabil, monoton wachsend oder semantische Versionskennung)
  - Erforderlich und vergleichbar pro `(series_id, site_id?, market_bid_area?, series_product, series_type, source.provider_id)`.
  - Akzeptiert werden:
    - streng monoton wachsende numerische Versionen (int-kompatibel),
    - oder Zeitstempel-basierte semantische Versionen (z. B. `2026-05-24T12:00:00Z-v3`).
  - Für einen stabilen Tie-Break werden bei gleicher Klasse numerisch zuerst `series_version` (wie definiert),
    dann bei gleicher Version der hash-basierte Payload-Fingerprint (`value_hash`) verwendet.
  - `site_id` (optional)
    - erforderlich für standortgebundene Forecast-/Erzeugungs-/Last-/Wetter-Reihen
    - optional oder leer für produktweite Forecast-Daten ohne Standortkontext
  - `market_bid_area` (optional)
    - erforderlich für `series_type=price`, damit die bestehende `PriceSeries`-Schiene
      den Marktbereich eindeutig identifizieren kann
  - `series_type` (`price` oder `forecast`)
  - `series_product` (z. B. `day_ahead`, `intraday`, `load`, `pv`, `wind`, `weather-temp`)
  - `unit` (z. B. `EUR/MWh`, `kW`, `kWh`, etc.)
- `resolution_minutes` (`> 0`, ganzzahlig)
  - `timezone` (muss `UTC` sein)
  - `horizon_start_utc`, `horizon_end_utc`
  - optionale Co-Location-Vorverarbeitungsmetadaten:
    - `alignment_mode` (`reject` | `trim-to-common`)
    - `alignment_prepared` (bool, nur bei `alignment_mode=trim-to-common` sinnvoll)
    - `alignment_prepared_by` (string, z. B. `forecast-preprocessor/v1`, `batch-trimmer/...`)
    - `alignment_prepared_horizon_start_utc`
    - `alignment_prepared_horizon_end_utc`
  - `values` als geordnete Punkte:
    - `timestamp_utc`
    - `value`
    - `value_type` (`actual` | `forecast`)
    - optional `confidence` (nur spätere Extensions)
  - `source_metadata`:
    - `provider_id`
    - `license_id` (optional, falls lizenzrelevant)
    - `retrieved_at_utc`
    - `valid_from_utc`, `valid_to_utc`
    - `provider_request_id`
- `series_status` (finaler Endstatus je Serie): `SOURCE_OK`, `SOURCE_DEGRADED`, `SOURCE_FALLBACK_USED`, `SOURCE_REJECTED`
- `source_eval_status` (Rohstatus je Providerlauf, vor Fallback-/Degradationsentscheidung): `SOURCE_OK`, `SOURCE_AUTH_ERROR`, `SOURCE_RATE_LIMIT`, `SOURCE_UNAVAILABLE`, `SOURCE_EMPTY`, `SOURCE_STALE`, `SOURCE_GAP`, `SOURCE_SCHEMA_MISMATCH`, `SOURCE_RETRY_EXHAUSTED`
- `status_flags` (array, required bei `series_status != SOURCE_OK`; optional bei `SOURCE_OK`): `SOURCE_FALLBACK_USED`, `SOURCE_BACKFILL`, `SOURCE_RATE_LIMIT`, ...

Hinweis zur Semantik:
- `source_eval_status` ist der interne Rohstatus pro Providerlauf (inkl. Primär- und Fallback-Pfad, vor Qualitätsentscheidung).
- `series_status` ist der externe Endstatus für Operator/API-Verträge (single-value).
- Kombinierte Ereignisse werden weiterhin nur in `status_flags` kodiert.
- `SOURCE_DEGRADED` ist ausschließlich ein finaler `series_status` und nicht als `source_eval_status` vorgesehen.
- `status_message` (optional): menschenlesbar
- `status_detail` (optional): strukturierte Zusatzinfo (z. B. `{ "source_code": "SOURCE_STALE", "backfill_intervals_closed": 2, "effective_source_id": "opsd-..." }`)
- `value_hash` (optional, empfohlen): Repräsentations-Stabilisierung des payload für Idempotenzvergleiche.
- Validierungspflicht:
  - strikte UTC-Zeitachse
  - Schrittweite exakt `resolution_minutes`
  - keine Duplikate in `timestamp_utc`
  - keine NaN/Inf-Werte
  - gleiche Horizontlänge/Range je Request
  - Preisreihen haben konsistente Preis-Einheit
  - Forecastreihen haben konsistente physikalische Einheit
- Replay-/Idempotenz- und Versionierungsregeln:
  - Wird dieselbe Kombination aus `series_id` und `series_version` mehrfach geladen, muss der
    vollständig gehashte Payload-identisch sein; andernfalls wird der Lauf als harten Fehler
    (`source_eval_status=SOURCE_SCHEMA_MISMATCH`, `series_status=SOURCE_REJECTED`) geführt.
  - Eine eingehende Serie mit älterer Version als der zuletzt akzeptierten Referenzversion für dieselbe
    Signaturkombination wird als harte Versionsabweichung (`series_status=SOURCE_REJECTED`) abgewiesen.
  - Für identische Serienversionen ist Verhalten idempotent: bestehende gültige Daten dürfen nur dann
    aktualisiert werden, wenn sich die `series_version` oder der `value_hash` geändert hat.
- Mapping-Regel:
- `series_type=price` wird auf bestehende Preis-Produkte in `PriceSeries` abgebildet.
  - dafür ist `market_bid_area` verpflichtend und wird auf das entsprechende
    `PriceSeries`-Feld gemappt.
- `series_type=forecast` dient in diesem Slice der Sidecar-/Inputlogik und darf bis zur Aktivierung
  eines produktiven Forecast-Domaintypen im EMS ausschließlich über den Sidecar-Integrationspfad
  weiterverarbeitet werden. In späteren Slices kann derselbe Contract in ein produktives
  Forecast-Domain-Format überführt werden.

### Normatives Status-Mapping (verbindlich)

- `source_eval_status` ist **interner Rohstatus pro Providerlauf** und darf direkt an einem
  externen Lauf nicht als End-Entscheidung gebunden werden.
- `series_status` ist der **einzige externe Endstatus je Serie** im Operator/API-Vertrag.
- `status_flags` ergänzt `series_status` um kombinierte Qualitätsereignisse.

Kanonische Ableitungsregeln (deterministisch):

- `source_eval_status=SOURCE_AUTH_ERROR` -> `series_status=SOURCE_REJECTED`, harte Abweisung.
- `source_eval_status=SOURCE_SCHEMA_MISMATCH` -> `series_status=SOURCE_REJECTED`, harte Abweisung.
- `source_eval_status=SOURCE_GAP`:
  - falls Lücken mit kompatiblem Backfill vollständig geschlossen werden können, resultiert der Zustand als `series_status=SOURCE_DEGRADED` mit `status_flags=[SOURCE_BACKFILL]`.
  - ist eine vollständige Schließung nicht möglich (oder `n_intervals <= 48`), wird `series_status=SOURCE_REJECTED`.
- `source_eval_status=SOURCE_EMPTY` -> `series_status=SOURCE_REJECTED`, harte Abweisung.
- `source_eval_status` aus `{SOURCE_STALE, SOURCE_RATE_LIMIT, SOURCE_UNAVAILABLE, SOURCE_RETRY_EXHAUSTED}`:
  - Bei vorhandenem kompatiblem Fallback folgt die Fallback-Evaluierung.
  - Bei erfolgreichem Fallback: `series_status=SOURCE_FALLBACK_USED` oder
    `SOURCE_DEGRADED` je nach Backfill/Qualitätsminderung.
  - Bei fehlendem kompatiblen Fallback: harte Abweisung außer im `degraded_ok`-Modus
    für `SOURCE_STALE` (-> `SOURCE_DEGRADED`).
- `source_eval_status=SOURCE_OK` bleibt `series_status=SOURCE_OK`, sofern weitere
  Daten- und Zeitachsenvalidierung bestanden ist.

Statusvokabular ist für alle Markt-/Forecast-Slices verbindlich:

- `SOURCE_OK`
- `SOURCE_DEGRADED`
- `SOURCE_FALLBACK_USED`
- `SOURCE_REJECTED`

Interoperabilitätsregel (über Pläne hinweg):

- Im Optimierungsrequest wird nur über den konkreten `series_status` und die
  Slice-konfigurierten Qualitätsregeln entschieden (`strict`/`degraded_ok`).
- `source_eval_status` darf nicht ohne Mapping in Operator-Sichten als Entscheidungsstatus
  verwendet werden.
- Wenn eine Pflichtserie auf `SOURCE_REJECTED` steht, ist der zugehörige Request im
  produktiven Pfad als hartes Verarbeitungsfehlerbild zu behandeln.
- Bei `SOURCE_DEGRADED`/`SOURCE_FALLBACK_USED` ist die Ausführung nur erlaubt,
  wenn der Slice diese Qualitätsgrade explizit erlaubt.
- `SOURCE_EMPTY` bleibt **in beiden Qualitätsmodi** hart als `SOURCE_REJECTED`
  und darf nicht implizit in `degraded_ok` umgewertet werden.

- Konsistenz-Regel:
  - Wenn `series_type=price`, ist `market_bid_area` Pflichtfeld; `site_id` optional.
  - Wenn `series_type=forecast` und Standorttrennung aktiv ist, ist `site_id` Pflicht.
  - Co-Location-Vorverarbeitung:
    - `alignment_mode=reject` ist produktiv der Default.
    - `alignment_mode=trim-to-common` ist nur zulässig, wenn `alignment_prepared=true` gesetzt ist, die Horizon-Metadaten vorhanden sind und ein vorbereiteter, deterministisch versionierter Vorverarbeitungspfad dokumentiert wurde.
- Statusmodell:
  - Die finale Serienqualität bleibt ein einzelner Wert in `series_status` (`SOURCE_OK`, `SOURCE_DEGRADED`, `SOURCE_FALLBACK_USED`, `SOURCE_REJECTED`).
  - Kombinierte Ereignisse (z. B. Fallback **und** Backfill) werden nur durch `status_flags` abgebildet; `series_status` bleibt ein `single-value`.
- `status_flags` ist verpflichtend, wenn `series_status != SOURCE_OK`.

Empfohlene Integrationskonvention (API/Operator):

- Laufzustände sollten die folgende Entkopplung nutzen:
  - Datenqualität: `series_status` + `status_flags` + `status_detail`
  - Operative Verfügbarkeit: zusätzlicher Dispatch-Guard (z. B. `CanExecute`)
- `series_status=SOURCE_REJECTED` muss in Operator-/Replay-Ausgaben als harte
  Datenblockade sichtbar sein.
- `series_status=SOURCE_DEGRADED`/`SOURCE_FALLBACK_USED` bleibt aktiv nutzbar, aber
  mit expliziter Anzeige von Qualitätsdegradation und Wiederherstellungspfad.

### Freshness- und Gap-Policy (verbindlich)

- `max_stale_age_minutes`:
  - Preisreihen: 90 Minuten (default, pro Serie konfigurierbar)
  - Forecastreihen: 720 Minuten (default, pro Feature konfigurierbar)
- `quality_mode`:
  - `strict` (Default): Keine Degradation erlaubt. Fallback ist nur zulässig,
    wenn daraus `SOURCE_OK` oder `SOURCE_FALLBACK_USED` resultiert.
  - `degraded_ok`: Degradierte Nutzung erlaubt, Ergebnis muss als `SOURCE_DEGRADED` gekennzeichnet werden.
- `min_coverage_ratio` Pflicht:
  - mindestens 99,5 %
  - Bezug auf Rohdaten vor Backfill (`raw_values_coverage`).
  - `n_intervals` ist die erwartete Schrittzahl im Zielhorizont.
  - Für sehr kurze Horizonte (`n_intervals <= 48`) wird diese Quote intern auf
    `100 %` gesetzt.
  - Für diese kurzen Horizonte gilt harte Volldeckung der Rohdaten: Jede
    Rohdatenlücke führt zu einem harten Qualitätsfehler (`SOURCE_GAP`) vor
    Backfill.
  - Die konsolidierte konsumierbare Serie muss nach Backfill keine offenen Lücken mehr enthalten.
- Lückenregime:
  - Für `n_intervals > 48` sind Rohdaten mit Lücken vor Backfill zulässig, solange `raw_values_coverage` die Mindestquote erreicht.
  - kontrollierter Backfill darf maximal 2 aufeinanderfolgende Intervalle pro Lücke schließen.
  - bei Backfill gilt der finale Datenvertrag weiterhin (keine offenen Restlücken).
  - Vollständig behobene Lücken werden als qualitätsgemindert markiert:
    - `series_status=SOURCE_DEGRADED` mit `status_flags=[SOURCE_BACKFILL]`.
  - In `quality_mode=strict` werden diese Serien nicht akzeptiert.
  - `SOURCE_GAP` gilt als harte Ablehnung nur für nicht vollständig behebbaren oder noch offenen Restlücken nach abgeschlossener Backfill-Behandlung.

### Qualitätsentscheidungen bei `SOURCE_*` (verbindlich)

Die Entscheidungslogik ist zweistufig:
- `source_eval_status` wird zuerst berechnet (`SOURCE_*` inklusive `SOURCE_OK`, harte oder degradierende Rohcodes).
- Daraus wird deterministisch ein externer `series_status` abgeleitet (`SOURCE_OK`, `SOURCE_DEGRADED`, `SOURCE_FALLBACK_USED`, `SOURCE_REJECTED`).

Die `SOURCE_*`-Auswertung ist für jede Serie deterministisch:

1) Vorvalidierung
- bei Schema-, Zeitachsen- oder Horizonabweichungen: sofort `source_eval_status=SOURCE_SCHEMA_MISMATCH` → `series_status=SOURCE_REJECTED` (kein Fallback).

2) Rohcode-Auswertung
- `source_eval_status=SOURCE_OK` → direkt in Schritt 3.
- `source_eval_status=SOURCE_AUTH_ERROR` → harte Ablehnung (`series_status=SOURCE_REJECTED`), kein Retry/Fallback.
- `source_eval_status=SOURCE_GAP`:
  - bei vollständig behebbaren Gaps (`n_intervals > 48` und Backfill-Quote innerhalb Limit): Übergang in Qualitätsmodus mit `series_status=SOURCE_DEGRADED` und `status_flags=[SOURCE_BACKFILL]`.
  - bei nicht behebbaren Gaps: harte Ablehnung (`series_status=SOURCE_REJECTED`), kein Retry/Fallback.
- `source_eval_status=SOURCE_EMPTY` → harte Ablehnung (`series_status=SOURCE_REJECTED`), kein Retry/Fallback.
- `source_eval_status` in `{SOURCE_STALE, SOURCE_RATE_LIMIT, SOURCE_UNAVAILABLE, SOURCE_RETRY_EXHAUSTED}` → Schritt 3.

3) Fallback- und Qualitätsmoduslogik
- Fallback ist versuchsweise nur für diese Codes:
  - `SOURCE_STALE`, `SOURCE_RATE_LIMIT`, `SOURCE_UNAVAILABLE`, `SOURCE_RETRY_EXHAUSTED`
- `SOURCE_AUTH_ERROR`, `SOURCE_SCHEMA_MISMATCH` sind nicht fallback-fähig.
- Sind Primärcode + kompatible Fallback-Quelle vorhanden:
  - Der Fallback wird synchron ausgewertet.
  - Ist Fallback erfolgreich:
  - bei `quality_mode=strict`:
    - finaler Zustand darf nur `series_status=SOURCE_OK` oder `series_status=SOURCE_FALLBACK_USED` sein.
    - führt der Fallback zusätzliche Qualitätsminderung (`SOURCE_BACKFILL` o. ä.), ist das Ergebnis `SOURCE_REJECTED`.
  - bei `quality_mode=degraded_ok`: finaler Zustand darf `series_status=SOURCE_DEGRADED` oder `series_status=SOURCE_FALLBACK_USED` sein; kombinierte Ereignisse werden in `status_flags` gehalten.
    - Wenn Fallback erfolgreich und keine zusätzliche Qualitätsminderung vorliegt: `series_status=SOURCE_FALLBACK_USED`, `status_flags=[SOURCE_FALLBACK_USED]`.
    - Wenn Fallback erfolgreich mit Backfill/Degradation: `series_status=SOURCE_DEGRADED`, `status_flags=[SOURCE_FALLBACK_USED, SOURCE_BACKFILL]`.
  - Ist Fallback fehlgeschlagen:
    - `strict`: harte Ablehnung (`series_status=SOURCE_REJECTED`).
    - `degraded_ok`: nur akzeptierbare Backfill/Degradation zulassen (`series_status=SOURCE_DEGRADED`), sonst `series_status=SOURCE_REJECTED`.
- Kein kompatibler Fallback:
  - `strict`: harte Ablehnung bei `source_eval_status in {SOURCE_STALE, SOURCE_RETRY_EXHAUSTED, SOURCE_RATE_LIMIT, SOURCE_UNAVAILABLE}`.
  - `degraded_ok`: `source_eval_status=SOURCE_STALE` kann als `series_status=SOURCE_DEGRADED` akzeptiert werden; `SOURCE_EMPTY` bleibt `series_status=SOURCE_REJECTED`.

4) Endstatuszuordnung bei akzeptierter Serie
- Primär: `SOURCE_OK`.
  - Mit Ersatzquelle: `series_status=SOURCE_FALLBACK_USED`.
  - Qualitätsminderung: `series_status=SOURCE_DEGRADED`.
- Kombinationsregel: Ist Fallback erfolgreich **und** Backfill aktiv, ist der `series_status` `SOURCE_DEGRADED` mit `status_flags` inklusive `SOURCE_FALLBACK_USED` und `SOURCE_BACKFILL`.
  - Diese Kombination ist in `quality_mode=strict` nicht zulässig.
- harte Ablehnung: `series_status=SOURCE_REJECTED`.

Hinweis:
Endstatus ist ein einzelner Wert (`single-value`) je Serie (`series_status`).
Fallback-/Degradationsdetails werden zusätzlich in `status_flags` und optionalem `status_detail` erfasst.

### Fehler-/Rohcodes und Endstatus (verbindlich)

- `SOURCE_AUTH_ERROR` – Authentifizierung fehlt/fehlerhaft
- `SOURCE_RATE_LIMIT` – Rate-Limit erreicht / Retry empfohlen
- `SOURCE_UNAVAILABLE` – Provider temporär nicht erreichbar
- `SOURCE_EMPTY` – leere Provider-Antwort
- `SOURCE_STALE` – Daten jenseits `max_stale_age_minutes`
- `SOURCE_GAP` – nicht behebbare Zeitlücken
- `SOURCE_SCHEMA_MISMATCH` – Zeitachse/Einheit/Schemafehler
- `SOURCE_RETRY_EXHAUSTED` – Retries erfolglos

- `SOURCE_REJECTED` ist ausschließlich Endstatus (`series_status`), kein Rohstatus aus
  `source_eval_status`.

Endgültige Serienendstatus sind:

- `SOURCE_OK`
- `SOURCE_DEGRADED`
- `SOURCE_FALLBACK_USED`
- `SOURCE_REJECTED`

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
  - `LoadAsync` (`IPriceSeriesSource`)
  - `LoadForecastAsync` (`IForecastSeriesSource`)
  - Adapter-Bridge zwischen `SeriesEnvelope` und produktiven Forecast-/Price-Domainmodellen
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
  - Serien-ID plus `series_version` des zuletzt geladenen Satzes
- Primärkennzeichnung je `series_type` (`primary`, `fallback`, `disabled`)
- Deterministische Reaktionsregeln:
  - `SOURCE_OK`: normaler Betrieb
  - `SOURCE_AUTH_ERROR`: harte Ablehnung (`SOURCE_REJECTED`), kein Retry/Fallback.
  - `SOURCE_SCHEMA_MISMATCH`, `SOURCE_GAP`: harte Ablehnung (`SOURCE_REJECTED`), kein Fallback.
- `SOURCE_RATE_LIMIT`, `SOURCE_STALE`, `SOURCE_UNAVAILABLE`, `SOURCE_RETRY_EXHAUSTED`:
    - Primär wird kontrollierter Fallback auf `fallback`-Quelle versucht, sofern vorhanden und kompatibel.
    - Fallback nur akzeptieren, wenn `SeriesEnvelope`, Einheit und Horizon exakt kompatibel sind.
    - Bei Fallback Erfolg gilt Fallback-Ergebnis nach `quality_mode`:
      - `strict`: nur akzeptierte Daten ohne Backfill/Verfallung (`SOURCE_OK`, `SOURCE_FALLBACK_USED`)
      - `degraded_ok`: Fallback kann als `SOURCE_DEGRADED` geführt werden.
      - in strict ist Fallback mit Backfill weiterhin `SOURCE_REJECTED`.
    - Bei Ausfall / Schemakonflikt des Fallbacks:
      - `quality_mode=degraded_ok` und Primärcode `SOURCE_STALE`: degradierte Fortsetzung als `SOURCE_DEGRADED` möglich
      - sonst harte Ablehnung (`SOURCE_REJECTED`)
  - Fallback nicht verfügbar:
- `strict`: harte Ablehnung für `SOURCE_STALE`, `SOURCE_RATE_LIMIT`, `SOURCE_UNAVAILABLE`, `SOURCE_RETRY_EXHAUSTED`
    - `degraded_ok`: nur `SOURCE_STALE` als `SOURCE_DEGRADED` möglich; `SOURCE_EMPTY` bleibt `SOURCE_REJECTED`.

### Phase 2: Erste produktive Quelle

Primär-/Fallback-Regel ist verbindlich:

- Preis:
  - Primär: EPEX (nur wenn Zugriff, Lizenz und Nutzungsbedingungen geklaert sind)
  - Fallback: Open Power System Data / Replay-konforme Datenquelle
- Forecast:
  - Primär: ENTSO-E
  - Fallback: Open-Meteo oder Copernicus für Wetter, je Featuretyp

Aktivierungslogik:
- `SOURCE_EMPTY` wird ohne zugelassenen, kompatiblen Fallback nur als harte
  Fehlerklasse `SOURCE_REJECTED` behandelt.

- Primärquelle wird standardmaessig genutzt.
- Bei Qualitätsfehler (`SOURCE_STALE`, `SOURCE_RATE_LIMIT`, `SOURCE_UNAVAILABLE`, `SOURCE_RETRY_EXHAUSTED`)
    wird der Fallbackzugriff nur genutzt, wenn:
   - derselbe `SeriesEnvelope`-Vertrag eingehalten wird
   - Lücke/Abweichung im Fallback innerhalb definierter Konfigurationsgrenzen bleibt
   - Operator den Fallbackstatus explizit akzeptiert
  - `SOURCE_GAP` wird nur als harte Ablehnung (`SOURCE_REJECTED`) geführt, wenn die Gaps nicht vollständig durch Backfill geschlossen werden konnten oder wenn `n_intervals <= 48`.
  - Ohne akzeptablen Fallback gilt:
    - `SOURCE_STALE`:
      - bei `quality_mode=degraded_ok`: `SOURCE_DEGRADED`
      - sonst `SOURCE_REJECTED`
    - `SOURCE_RATE_LIMIT` / `SOURCE_UNAVAILABLE` / `SOURCE_SCHEMA_MISMATCH`: `SOURCE_REJECTED`
  - `SOURCE_RETRY_EXHAUSTED`: `SOURCE_REJECTED`
    - `SOURCE_GAP`: `SOURCE_REJECTED`
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
