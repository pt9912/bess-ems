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
   - Legacy-`PriceSeries` ohne explizite Identität werden beim Lesen über einen
     deterministischen Adapter in die neue Identität projiziert; neue Producer
     dürfen keine unversionierte Serie mehr schreiben.

2. Store-Key
   - Der private `InMemoryPriceSeriesStore.PriceSeriesKey` wird durch einen
     expliziten, testbaren Serienidentitätstyp ersetzt oder vollständig auf die
     neue Identität umgestellt.
   - Dauerhafte Stores, soweit vorhanden, verwenden denselben Schlüsselvertrag.
   - Der heutige Legacy-Key
     `(MarketBidArea, Product, PriceKind, Source, HorizonStart, HorizonEnd, TimeStep)`
     wird nur noch als Migrationsinput akzeptiert. Der neue Schlüssel ist die
     explizite Serienidentität plus Provider-Kontext; Horizon-Grenzen sind
     Payload-/Coverage-Metadaten, kein Identitätsersatz.

3. Persistenz und Migration
   - `schema/schema.yaml` und d-migrate/RunOnce-Migrationen werden erweitert,
     sobald ein dauerhafter Store beteiligt ist. Diese Erweiterung gehört nicht
     zu `optimization_runs`; sie liegt in einem eigenen Preis-/Serien-Store
     (z. B. Tabelle `price_series` oder `series_envelopes`) mit mindestens:
     - `series_id` (`text`, required)
     - `series_version` (`text`, required)
     - `provider_id` (`text`, required; entspricht `source.provider_id`)
     - `series_type` (`text`, required)
     - `series_product` (`text`, required)
     - `market_bid_area` (`text`, nullable)
     - `site_id` (`text`, nullable)
     - `unit` (`text`, required)
     - `resolution_minutes` (`integer`, required)
     - `value_hash` (`text`, required für produktive Re-Load-/Cutover-Pfade)
     - `series_version_family` (`text`, nullable)
     - `horizon_start_utc` / `horizon_end_utc` (`datetime`, timezone, required)
     - `source_metadata_json` (`text`, required)
   - Für eindeutige Keys werden nullable Scope-Felder normalisiert
     (`market_bid_area`/`site_id` als leerer Scope-Key oder äquivalenter
     persisted generated key). Der eindeutige Store-Key umfasst mindestens
     `(series_id, series_version, provider_id, series_type, series_product,
     normalized_market_bid_area, normalized_site_id)`.
     `unit` und `resolution_minutes` bleiben verpflichtende
     Verträglichkeitsfelder und sind Bestandteil von `value_hash` sowie
     Schema-/Signaturprüfungen; sie sind nicht Teil des eindeutigen Store-Keys,
     weil ein Wechsel unter gleicher Serienidentität als harter
     Schemafehler statt als zweite Serie behandelt wird.
   - Bestehender Schlüsselaspekt in
     `InMemoryPriceSeriesStore.PriceSeriesKey`
     (Quelle:
     `src/hexagon/BatteryEms.Application/Markets/InMemoryPriceSeriesStore.cs`)
     darf nicht nur auf `MarketBidArea`/`Product`/`PriceKind`/`Source`/
     `HorizonStart`/`HorizonEnd`/`TimeStep` basieren. Der heutige Typ ist ein
     privater Store-Record; die Migration muss ihn entweder durch einen
     expliziten, testbaren Serienidentitätstyp ersetzen oder den Store-Key
     vollständig neu schneiden.
   - Liefergegenstand dieses Pre-Slices ist eine explizite Schema-Migration für
     `PriceSeries` selbst: neue Serienidentitätsfelder am Domain-/
     Application-Record, neuer Store-Key, Mapping zwischen `SeriesEnvelope` und
     `PriceSeriesRequest`, Import-/API-Wire-Kompatibilität sowie Anpassung aller
     dauerhaften Stores, soweit im Produktpfad vorhanden.
   - Altimporte bleiben über einen Dual-Path lauffähig.
   - Bestehende In-Memory-/Legacy-Datensätze werden nicht still umgedeutet:
     - `series_id = legacy:<market_bid_area>:<product>:<price_kind>:<source>`
       oder ein äquivalent dokumentierter stabiler Legacy-Alias,
     - `series_version` wird deterministisch aus
       `(horizon_start_utc, horizon_end_utc, resolution_minutes, source)` gebildet,
     - `provider_id` wird aus dem bisherigen `Source`-Feld abgeleitet,
     - `series_type`/`series_product` werden über die Mapping-Tabelle im
       Adapterplan normalisiert.
   - Neue Serienkennungen dürfen erst produktiv aufgenommen werden, wenn der
     neue Identitätsschlüssel in allen aktiven Store-Pfaden verfügbar ist.

4. Mapping
   - `SeriesEnvelope` wird deterministisch auf `PriceSeries` bzw.
     `ForecastSeries`-Contract-Daten abgebildet.
   - Provider-ID, Version, Family und `value_hash` dürfen nicht im
     Mapping-Verlauf verloren gehen.
   - `value_hash` wird kanonisch nach
     [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md#kanonische-value_hash-berechnung-verbindlich)
     berechnet: `sha256(canonical_bytes)` über UTF-8-kodiertes kanonisches JSON
     mit sortierten Properties, normalisierter UTC-Zeitachse, deterministischer
     Zahlenrepräsentation und Alignment-Metadaten, sofern gesetzt.
   - Ein Mapping-Test fixiert Byte-für-Byte, dass identische Eingangszeitreihen
     unabhängig von Dictionary-/JSON-Feldreihenfolge denselben `value_hash`
     erzeugen.

5. Tests
   - Idempotenter Re-Load bei gleicher Identität und gleichem `value_hash`.
   - Harte Ablehnung bei gleicher Identität/Version und anderem `value_hash`.
   - Provider-Kontexte bleiben getrennt.
   - Family-Cutover und Rollback sind auditierbar.
   - `alignment_prepared_*`-Metadaten ändern bei sonst identischem Payload den
     `value_hash` deterministisch.
   - Altpfad bleibt lauffähig, solange Dual-Path aktiv ist.
   - Dual-Path-Fixtures:
     - Legacy-`PriceSeriesKey`-Datensatz wird in eine stabile Serienidentität
       projiziert und bleibt lesbar.
     - Neuer `SeriesEnvelope`-Datensatz roundtript über den Store-Key ohne
       Verlust von `provider_id`, `series_version`, `series_version_family` und
       `value_hash`.
     - Gleiche Identität + gleiche Version + anderer `value_hash` wird
       deterministisch abgelehnt.
     - Gleiche Identität + gleicher `value_hash` wird idempotent akzeptiert.

---

## Abhängigkeiten

Dieser Pre-Slice muss vor produktiver Aktivierung von
[`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md) abgeschlossen
sein. Co-Location darf lokale Serien nur dann produktiv über den Adapterpfad
nutzen, wenn diese Identität verfügbar ist.

---

## Definition of Done (DoD)

- [ ] Serienidentität ist als expliziter Application-/Domain-Vertrag eingeführt.
- [ ] InMemory- und dauerhafte Store-Keys verwenden dieselbe Identität; der
  Legacy-`PriceSeriesKey` ist nur noch Migrationsinput.
- [ ] `schema/schema.yaml` und d-migrate-Migrationen führen den dauerhaften
  Preis-/Serien-Store mit den oben genannten Identitäts-, Provider-,
  Horizon- und Hash-Feldern, sobald Persistenz produktiv genutzt wird.
- [ ] Import-/Request-/API-/Wire-Mappings transportieren die Identitätsfelder
  ohne Verlust von `provider_id`, `series_version_family` und `value_hash`.
- [ ] Idempotenz-, `value_hash`-Kanonisierung- und Dual-Path-Fixtures sind grün.
- [ ] Dual-Path für Altimporte ist dokumentiert und getestet.
- [ ] Preis-/Forecast-Adapterplan referenziert diesen Pre-Slice als
  abgeschlossene Voraussetzung.
