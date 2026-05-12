# Plan RM-M5-07 Preisreihen-Port und quellenneutraler Import

Status: abgeschlossen am 2026-05-12. Dieser Slice liefert den
source-agnostic Preisreihen-Port fuer Optimierungsrequests.

## Ziel

Optimierung darf nicht direkt an einen externen Markt-/Datenanbieter
gekoppelt sein. Preisreihen werden normalisiert importiert, tragen eine
explizite Quelle und werden erst danach ueber einen Application-Port in
Optimierungsrequests aufgeloest.

## Ergebnis

- `IPriceSeriesSource` und `IPriceSeriesImportSink` sind Application-Ports
  unter `BatteryEms.Application.Markets`.
- `PriceSeries` normalisiert Marktgebiet, Produkt, Preisart, Einheit,
  Zeitraster, Werte und Quelle; Konstruktion validiert Horizon-Alignment,
  Schrittanzahl und finite Preise.
- `InMemoryPriceSeriesStore` ist der Default fuer API/Test-Hosts und
  resolved exact-match Referenzen ueber Marktgebiet, Produkt, Preisart,
  Quelle, Horizon und Zeitraster.
- `POST /markets/price-series/import` nimmt synthetische oder anderweitig
  frei nutzbare Preisreihen provider-neutral entgegen.
- `POST /markets/day-ahead/optimize` und
  `POST /markets/intraday/reoptimize` akzeptieren alternativ zu
  `prices_per_step` eine `price_series`-Referenz und reichen die geladenen
  Werte plus Einheit als Optimierungsrequest weiter.
- Externe Anbieteradapter bleiben bewusst draussen, bis Lizenz/Nutzung,
  Auth, Rate-Limits und Caching dokumentiert sind.

## Nachweise

- `make test`
- `make arch-check`
