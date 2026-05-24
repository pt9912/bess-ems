# Plan: Domain-Migration PriceSeries.Identity

**Dokumenttyp:** Pre-Slice / offen
**Status:** Open - Voraussetzung für Preis-/Forecast-Adapter und produktive Forecast-Serien
**Datum:** 2026-05-24
**Bezug:**
[`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md),
[`plan-market-colocation-model.md`](plan-market-colocation-model.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)

---

## Ziel

`PriceSeries` und die zugehörigen Import-/Store-Pfade erhalten eine stabile
Serienidentität. Der heutige In-Memory-Key basiert auf
`MarketBidArea`/`Product`/`PriceKind`/`Source`/`HorizonStart`/`HorizonEnd`/
`TimeStep` und reicht nicht für versionierte externe Quellen, Family-Cutover und
Provider-Kontexte.

Normativ soll die Serienidentität mindestens tragen:

- `series_id`
- `series_version`
- `source.provider_id`
- `series_type`
- `series_product`
- `market_bid_area` falls gesetzt
- `site_id` falls gesetzt
- `unit`
- `resolution_minutes`

---

## Scope

1. Domain-/Application-Vertrag
   - `PriceSeries` bzw. ein vorgelagerter Serien-Record erhält die
     Identitätsfelder.
   - `PriceSeriesRequest` oder ein neuer Request-Typ kann diese Identität
     vollständig adressieren.
   - `value_hash` ist verpflichtend, sobald Idempotenz, Re-Load-Vergleich,
     Family-Cutover oder Rollback aktiviert ist.

2. Store-Key
   - Der private `InMemoryPriceSeriesStore.PriceSeriesKey` wird durch einen
     expliziten, testbaren Serienidentitätstyp ersetzt oder vollständig auf die
     neue Identität umgestellt.
   - Dauerhafte Stores, soweit vorhanden, verwenden denselben Schlüsselvertrag.

3. Persistenz und Migration
   - `schema/schema.yaml` und d-migrate/RunOnce-Migrationen werden erweitert,
     sobald ein dauerhafter Store beteiligt ist.
   - Altimporte bleiben über einen Dual-Path lauffähig.
   - Neue Serienkennungen dürfen erst produktiv aufgenommen werden, wenn der
     neue Identitätsschlüssel in allen aktiven Store-Pfaden verfügbar ist.

4. Mapping
   - `SeriesEnvelope` wird deterministisch auf `PriceSeries` bzw.
     `ForecastSeries`-Contract-Daten abgebildet.
   - Provider-ID, Version, Family und `value_hash` dürfen nicht im
     Mapping-Verlauf verloren gehen.

5. Tests
   - Idempotenter Re-Load bei gleicher Identität und gleichem `value_hash`.
   - Harte Ablehnung bei gleicher Identität/Version und anderem `value_hash`.
   - Provider-Kontexte bleiben getrennt.
   - Family-Cutover und Rollback sind auditierbar.
   - Altpfad bleibt lauffähig, solange Dual-Path aktiv ist.

---

## Abhängigkeiten

Dieser Pre-Slice muss vor produktiver Aktivierung von
[`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md) abgeschlossen
sein. Co-Location darf lokale Serien nur dann produktiv über den Adapterpfad
nutzen, wenn diese Identität verfügbar ist.

---

## Definition of Done

- [ ] Serienidentität ist als expliziter Application-/Domain-Vertrag eingeführt.
- [ ] InMemory- und dauerhafte Store-Keys verwenden dieselbe Identität.
- [ ] Import-/Request-/API-/Wire-Mappings transportieren die Identitätsfelder.
- [ ] Idempotenz- und `value_hash`-Tests sind grün.
- [ ] Dual-Path für Altimporte ist dokumentiert und getestet.
- [ ] Preis-/Forecast-Adapterplan referenziert diesen Pre-Slice als
  abgeschlossene Voraussetzung.
