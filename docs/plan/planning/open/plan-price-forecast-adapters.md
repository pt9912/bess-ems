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
sondern über austauschbare Quellenadapter beziehen können.

Der bestehende `PriceSeries`-/`IPriceSeriesSource`-Pfad bleibt die
Application-Grenze. Externe Providerlogik gehört in Adapter, nicht in
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
- Forecast-Zeitreihen für Load, PV, Wind oder Wetter
- Quellenstatus, Refresh-Status, Rate-Limit-/Cache-Regeln
- provider-spezifische Authentisierung
- verbindlicher Serienvertrag für Zeitachse, Freshness, Gap-Handling und Fehlercodes

Kompatibilitäts-/Migrationsprinzip:

- Das bestehende `PriceSeries`-Import-Subsystem bleibt vollständig funktionsfähig.
- `IForecastSeriesSource` wird als zusätzliche, kompatible Schnittstelle eingeführt.
- `SeriesEnvelope` dient als einheitlicher Zwischenvertrag:
  - bestehende Preisimporte nutzen weiterhin `IPriceSeriesSource`,
  - Forecast/Forecast-Adapter nutzen `IForecastSeriesSource`,
  - beide werden vor dem Optimierer über denselben Mapping-Layer auf die aktuelle Domäne projiziert.
- Für den produktiven Slice ist die bestehende `PriceSeries`/Persistenzkette kompatibel zu erweitern:
  - bestehender Schlüsselaspekt in `InMemoryPriceSeriesStore.PriceSeriesKey`
    (Quelle: `src/hexagon/BatteryEms.Application/Markets/InMemoryPriceSeriesStore.cs`)
    darf nicht nur auf Markt/Produkt/Bereich/Quelle/Timestep basieren. Der heutige
    Typ ist ein privater Store-Record; die Migration muss ihn entweder durch einen
    expliziten, testbaren Serienidentitätstyp ersetzen oder den Store-Key vollständig
    neu schneiden.
  - Die Persistenz muss mindestens `series_id`, `series_version` **und**
    `source.provider_id` als Teil der Identität tragen (oder einen deterministischen
    Ersatzschlüssel aus genau diesen Werten plus
    `series_type/product/site_id/market_bid_area`).
  - Liefergegenstand des Slices ist eine explizite Schema-Migration für `PriceSeries`
    selbst: neue Serienidentitätsfelder am Domain-/Application-Record, neuer
    Store-Key, Mapping zwischen `SeriesEnvelope` und `PriceSeriesRequest`,
    Import-/API-Wire-Kompatibilität sowie Anpassung aller dauerhaften Stores, soweit
    im Produktpfad vorhanden.
  - Ohne diese Erweiterung bleibt F-MKT-02 im produktiven Modus auf den bisherigen Altpfad beschränkt, bis ein dedizierter Speicher-Migrationspfad freigegeben wurde.
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
  - `series_family_alias`: optional bei stabiler Serienfamilie, verpflichtend bei
    kontrolliertem Family-Wechsel ohne neue `series_id`; stabiler Alias für
    kontrollierte Migrationspfade. Ein Wechsel der Familienkennung erfolgt nur über
    diesen Alias bzw. durch neue Serienkennung, nicht per stillschweigendem Wechsel
    derselben `series_id`.
  - Erforderlich und vergleichbar pro `(series_id, site_id?, market_bid_area?, series_product, series_type, source.provider_id)`.
  - Akzeptiert werden:
    - streng monoton wachsende numerische Versionen (int-kompatibel),
    - oder Zeitstempel-basierte semantische Versionen (z. B. `2026-05-24T12:00:00Z-v3`).
  - Andere Formate sind unzulässig und führen auf dem Importpfad zu
    `source_eval_status=SOURCE_SCHEMA_MISMATCH` und `series_status=SOURCE_REJECTED`.
- Repräsentationsstabilität ist für die vollständige Serien-Signatur verpflichtend.
  - Die Vergleichbarkeit verwendet die Signatur:
    `series_id`, `source.provider_id`, `series_type`, `series_product`,
    `market_bid_area` (falls gesetzt), `site_id` (falls gesetzt), `unit`,
    `resolution_minutes`.
  - `series_version_family` (optional, empfohlen): `numeric` oder `timestamp`, zur
    verbindlichen Normalisierung der `series_version`.
    - Bei ausbleibender Angabe wird die Familie aus dem Versionsformat deterministisch abgeleitet.
    - Die Familie wird beim ersten gültigen Write bestimmt (`int` oder `timestamp`-basiert).
    - Die Familie ist Teil der persistierbaren Serienidentität; der produktive
      Wechsel der Familie ist **nur** über gesteuerten Migrationsschnittpunkt zulässig
      (kein stiller Wechsel derselben `series_id`/`series_product`-Paarung).
  - Ein späterer unkontrollierter Wechsel der Versionsfamilie im produktiven Modus wird als harte
    Schemaabweichung (`source_eval_status=SOURCE_SCHEMA_MISMATCH`, `series_status=SOURCE_REJECTED`) bewertet.
  - Migrationsprinzip bei `series_version_family`-Wechsel:
    - Produktiv ist ein Wechsel nur mit geplanter Migrationsfolge zulässig.
    - Die Umstellung muss explizit über neue Serienkennung/Alias erfolgen (z. B. neues `series_id`/`series_product`-Paar), nicht per stillschweigendem Familienwechsel derselben `series_id`.
    - Während der Migrationsphase können beide Serienfolgen parallel aktiv sein; nach erfolgreichem Cutover wird die alte Folge deaktiviert.
    - Die Migrationsfreigabe ist an einen expliziten Release-Hinweis gebunden.
    - Empfohlener kontrollierter Cutover:
      1. Neue Family entweder unter expliziter Alias-Kennung (`series_family_alias`) oder
         als neue Serienkennung (`series_id`/`series_product`) freigeben; beide Varianten
         starten mit paralleler Beobachtung des alten und neuen Streams.
      2. Während der Übernahmephase auf Alias- bzw. neue Serienkennzeichnung in
         Operator-/Replay-Berichten prüfen; beide Familien müssen bis zum Cutover
         denselben fachlichen Wertbereich liefern.
      3. Nach Freigabe wird die alte Family für den produktiven Pfad abgeschaltet, neue Family wird normativ aktiv.
    - Verbindlicher Cutover-/Rollback-SOP:
      1. **Vorbereitung**: Neue Family mit eigenem `series_family_alias` oder neuem (`series_id`, `series_product`) freigeben.
      2. **Dual-Active-Wahrnehmung**: Alte und neue Family mindestens in zwei vollständigen Release-Fenstern parallel beobachten; `value_hash`-Abweichungen sind als `status_flags` mit `status_message` auditierbar.
      3. **Cutover freigeben**: Neue Family wird produktiv als `primary` markiert und auf mindestens `SOURCE_OK` oder ausdrücklich zugelassene `SOURCE_DEGRADED` gesetzt.
      4. **Produktiver Umschaltpunkt**: Alte Family in produktiven Runs auf `SOURCE_REJECTED`/`CanExecute=false` halten; alte Werte sind nur noch Replay/Diagnose sichtbar.
      5. **Rollback-Fenster**: Rückkehr auf alte Family nur mit explizitem Release-Block (`release_block` oder `controlled_switchover`) möglich; Rollback ist nur dann erlaubt, wenn die neue Family in einem vollständigen Beobachtungsfenster keine vollständige akzeptierbare Lastserie (ohne harte Fallback) geliefert hat.
      6. **Abschluss**: Alter Family-Schlüssel wird auf `ARCHIVED` gesetzt; neue Family läuft ohne Alias-Wechsel weiter.

Kontrollierte Abnahmetests für Family-/Versionswechsel:
- **Dual-Active-Fähigkeit**: Alte und neue Family werden gleichzeitig geladen; beide dürfen im Beobachtungsfenster aktiv sein, solange beide dieselbe Semantik (`series_type`, `series_product`, `market_bid_area`, `site_id`, `resolution_minutes`) liefern.
- **Unsichtbarer Familienwechsel wird blockiert**: Derselbe `series_id`/`series_product` ohne neue Alias-/Schlüsselstruktur und ohne Release-Step darf nie von `numeric` auf `timestamp` (oder umgekehrt) springen.
- **Harsh-Case bei Alias-Ungleichheit**: `series_id`/`series_version`/`source.provider_id` gleich, aber Family-Wechsel ohne Revisionsbruch, liefern unterschiedliche `value_hash` -> harte `SOURCE_REJECTED`.
- **Regressionstest `value_hash`**: Gleiche Vollsignatur + gleicher Hash ist idempotent; gleicher Hash plus gleicher `series_version`/Provider muss ohne Seiteneffekte mehrfach akzeptiert werden.
- **Rollback-Sichtbarkeit**: Im kontrollierten Rollback werden alte und neue `series_family_alias` mindestens bis zum Ende eines Validation-Fensters in Operator-/Replay-Sicht weiterhin explizit aufgelöst.
  - Für einen stabilen Tie-Break werden bei gleicher Klasse numerisch zuerst `series_version` (wie definiert),
    dann bei gleicher Version der hash-basierte Payload-Fingerprint (`value_hash`) verwendet.
    - „gleiche Klasse“ meint die normalisierte Vergleichsschicht der festen Versionsfamilie pro Serie/Provider.
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
- `value_hash`: optional für reine Einmalimporte ohne Idempotenz-/Cutover-Vertrag;
  verpflichtend, sobald Versions-Idempotenz, Re-Load-Vergleich, Family-Cutover oder
  Rollback aktiviert ist. Der Hash stabilisiert den Payload-Vergleich.
- Validierungspflicht:
  - strikte UTC-Zeitachse
  - Schrittweite exakt `resolution_minutes`
  - keine Duplikate in `timestamp_utc`
  - keine NaN/Inf-Werte
  - gleiche Horizontlänge/Range je Request
  - Preisreihen haben konsistente Preis-Einheit
  - Forecastreihen haben konsistente physikalische Einheit
- Replay-/Idempotenz- und Versionierungsregeln:
  - Die Idempotenzprüfung verwendet die komplette Serien-Signatur plus `series_version`:
    `series_id`, `source.provider_id`, `series_type`, `series_product`,
    `market_bid_area` (falls gesetzt), `site_id` (falls gesetzt),
    `unit`, `resolution_minutes`, `series_version`.
  - Wird dieselbe Signatur-Vollmenge mehrfach geladen, wird vollständige Payload-Identität über `value_hash` erwartet:
    - identische `value_hash`-Werte sind idempotent,
    - unterschiedliche `value_hash` bei identischer Signatur und identischem `series_version` führen als harter Fehler zu  
      (`source_eval_status=SOURCE_SCHEMA_MISMATCH`, `series_status=SOURCE_REJECTED`).
  - Für inhaltliche Korrekturen ist ein Revisionswechsel zwingend:
    entweder höhere `series_version` derselben Familie oder ein kontrollierter
    Migrationspfad über `series_family_alias` (oder neue `series_id`).
    Ohne expliziten Revisionswechsel ist keine In-Place-Korrektur derselben Version im produktiven Lauf erlaubt.
  - Eine eingehende Serie mit älterer Version als die letzte akzeptierte Referenzversion für dieselbe
    Serien-Signatur wird nach dem stabilisierten Vergleich als harte Versionsabweichung
    (`series_status=SOURCE_REJECTED`) abgewiesen.
    - Der stabile Vergleich nutzt zuerst die Versionsfamilie (`int` oder `timestamp`), danach den normalisierten Fortschritt in dieser Familie.
  - Signaturänderungen bei optionalen Feldern sind harte Inkompatibilitäten:
    Wenn `market_bid_area`, `site_id`, `unit` oder `resolution_minutes` für dieselbe
    `series_id`/`source.provider_id`/`series_type`/`series_product`-Kombination variieren,
    ergibt sich `source_eval_status=SOURCE_SCHEMA_MISMATCH` / `series_status=SOURCE_REJECTED`.
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
  - bei vollständig behebbaren Gaps:
    - bei `quality_mode=degraded_ok`: `series_status=SOURCE_DEGRADED` mit `status_flags=[SOURCE_BACKFILL]`
    - bei `quality_mode=strict`: harte Ablehnung als `series_status=SOURCE_REJECTED`
  - ist eine vollständige Schließung nicht möglich oder überschreitet
    `missing_raw_intervals` die berechnete Grenze `max_missing_raw_intervals`, wird
    `series_status=SOURCE_REJECTED`.
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
  - in `quality_mode=strict` kein gültiger Endstatus.
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
    wenn daraus `SOURCE_OK` oder `SOURCE_FALLBACK_USED` resultiert und kein
    zusätzliches Qualitätsflag (`SOURCE_BACKFILL`) gesetzt ist.
  - `degraded_ok`: Degradierte Nutzung erlaubt, Ergebnis muss als `SOURCE_DEGRADED` gekennzeichnet werden.
- `min_coverage_ratio` Pflicht:
  - mindestens 99,5 %
  - Bezug auf Rohdaten vor Backfill (`raw_values_coverage`).
  - `n_intervals` ist die erwartete Schrittzahl im Zielhorizont.
  - Es gibt keine separate harte `48`-Intervalle-Schwelle.
  - Zulässige Rohdatenlücken werden deterministisch berechnet:
    `max_missing_raw_intervals = floor(n_intervals * (1 - min_coverage_ratio))`.
    Bei 96 Intervallen sind damit 0 Rohdatenlücken zulässig; bei 200 Intervallen
    genau 1. Diese Rundungsregel ist bewusst konservativ und ersetzt
    horizon-spezifische Magic Numbers.
  - Die konsolidierte konsumierbare Serie muss nach Backfill keine offenen Lücken mehr enthalten.
- Lückenregime:
  - Rohdaten mit Lücken vor Backfill sind nur zulässig, solange
    `raw_values_coverage` die Mindestquote erreicht und
    `missing_raw_intervals <= max_missing_raw_intervals` gilt.
  - kontrollierter Backfill darf maximal 2 aufeinanderfolgende Intervalle pro Lücke schließen.
  - bei Backfill gilt der finale Datenvertrag weiterhin (keine offenen Restlücken).
  - Vollständig behobene Lücken werden als qualitätsgemindert markiert:
    - `series_status=SOURCE_DEGRADED` mit `status_flags=[SOURCE_BACKFILL]`.
  - In `quality_mode=strict` werden diese Serien nicht akzeptiert.
  - `SOURCE_GAP` gilt in `quality_mode=strict` immer als harte Ablehnung, auch wenn
    die Lücke technisch per Backfill vollständig behebbar wäre.
  - In `quality_mode=degraded_ok` gilt `SOURCE_GAP` als harte Ablehnung nur für nicht
    vollständig behebbare oder noch offene Restlücken nach abgeschlossener
    Backfill-Behandlung.

### Qualitätsentscheidungen bei `SOURCE_*` (verbindlich)

Die Entscheidungslogik ist zweistufig:
- `source_eval_status` wird zuerst berechnet (`SOURCE_*` inklusive `SOURCE_OK`, harte oder degradierende Rohcodes).
- Daraus wird deterministisch ein externer `series_status` abgeleitet (`SOURCE_OK`, `SOURCE_DEGRADED`, `SOURCE_FALLBACK_USED`, `SOURCE_REJECTED`).

Die `SOURCE_*`-Auswertung ist für jede Serie deterministisch:
- Die folgenden Punkte sind ein Verweis auf die oben genannten **Kanonischen Ableitungsregeln** und keine zweite, abweichende Normdefinition.

1) Vorvalidierung
- bei Schema-, Zeitachsen- oder Horizonabweichungen: sofort `source_eval_status=SOURCE_SCHEMA_MISMATCH` → `series_status=SOURCE_REJECTED` (kein Fallback).

2) Rohcode-Auswertung
- `source_eval_status=SOURCE_OK` → direkt in Schritt 3.
- `source_eval_status=SOURCE_AUTH_ERROR` → harte Ablehnung (`series_status=SOURCE_REJECTED`), kein Retry/Fallback.
- `source_eval_status=SOURCE_GAP`:
  - bei vollständig behebbaren Gaps innerhalb der Coverage-Grenze
    (`missing_raw_intervals <= max_missing_raw_intervals`):
    - bei `quality_mode=degraded_ok`: Ergebnis wie bisher (`series_status=SOURCE_DEGRADED`, `status_flags=[SOURCE_BACKFILL]`).
    - bei `quality_mode=strict`: harte Ablehnung als `series_status=SOURCE_REJECTED`.
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
    - explizit mit der Ausnahme: `SOURCE_FALLBACK_USED` ist in `strict` nur ohne
      `SOURCE_BACKFILL` erlaubt.
    - Zusätzliche Qualitätsminderung führt in `strict` auf `SOURCE_REJECTED`; das
      Statusvokabular oben bleibt normativ.
  - bei `quality_mode=degraded_ok`: finaler Zustand darf `series_status=SOURCE_DEGRADED` oder `series_status=SOURCE_FALLBACK_USED` sein; kombinierte Ereignisse werden in `status_flags` gehalten.
    - Wenn Fallback erfolgreich und keine zusätzliche Qualitätsminderung vorliegt: `series_status=SOURCE_FALLBACK_USED`, `status_flags=[SOURCE_FALLBACK_USED]`.
    - Wenn Fallback erfolgreich mit Backfill/Degradation: `series_status=SOURCE_DEGRADED`, `status_flags=[SOURCE_FALLBACK_USED, SOURCE_BACKFILL]`.
  - Ist Fallback fehlgeschlagen:
    - `strict`: harte Ablehnung (`series_status=SOURCE_REJECTED`).
    - `degraded_ok`: nur akzeptierbare Backfill/Degradation zulassen (`series_status=SOURCE_DEGRADED`), sonst `series_status=SOURCE_REJECTED`.
- Kein kompatibler Fallback:
  - `strict`: harte Ablehnung bei `source_eval_status in {SOURCE_STALE, SOURCE_RATE_LIMIT, SOURCE_UNAVAILABLE, SOURCE_RETRY_EXHAUSTED}`.
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
  - Intraday- oder Indexdaten, sofern lizenziert und verfügbar
  - Erwarteter Nutzen: produktive Preisbasis für Optimierung

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
  - Erwarteter Nutzen: Forecast-Features für PV/Wind/Load

- Fuel-/CO2-Quellen
  - Gas, Coal, CO2
  - Erwarteter Nutzen: Preisprognose-Features, nicht kurzfristig für den
    technischen Dispatch nötig

---

## Scope bei Aktivierung

### Phase 1: Adapter-Vertrag und Import-Hardening

- Quellenneutrales Adapterinterface oberhalb externer Provider:
  - `LoadAsync` (`IPriceSeriesSource`)
  - `LoadForecastAsync` (`IForecastSeriesSource`)
  - Adapter-Bridge zwischen `SeriesEnvelope` und produktiven Forecast-/Price-Domainmodellen
  - verbindliches `SeriesEnvelope` gemäß obigem Datenvertrag
  - deterministisches Mapping in die bestehende Import-Pipeline (`IPriceSeriesSource`).
- Persistenz-/Schema-Migration als Phase-1-Voraussetzung:
  - `PriceSeries`/Import-Request erhalten die Serienidentitätsfelder oder einen
    deterministisch äquivalenten Ersatzschlüssel.
  - `InMemoryPriceSeriesStore` ersetzt den privaten Alt-Key durch die neue
    Serienidentität; dauerhafte Stores folgen demselben Schlüsselvertrag.
  - Ohne diese Migration dürfen neue Serienkennungen nicht produktiv aufgenommen werden.
- Cache-/Refresh-Vertrag:
  - TTL
  - `max_stale_age_minutes`
  - provider rate limit + Retry-Backoff
- operatorfähige Fehlercodes inkl. `SOURCE_*`
- API-/Operator-Status für Quellen:
  - letzter erfolgreicher Abruf + aktiver Statuscode
  - letzter Fehler
  - letzter Fehlercode (`SOURCE_*`)
  - Datenhorizont
  - Quelle und Produkt
  - Serien-ID plus `series_version` des zuletzt geladenen Satzes
- Deterministische Reaktionsregeln:
  - `SOURCE_OK`: normaler Betrieb
  - `SOURCE_AUTH_ERROR`: harte Ablehnung (`SOURCE_REJECTED`), kein Retry/Fallback.
  - `SOURCE_SCHEMA_MISMATCH`: harte Ablehnung (`SOURCE_REJECTED`), kein Retry/Fallback.
  - `SOURCE_GAP` wird gemäß den im vorangehenden Abschnitt definierten globalen Ableitungsregeln entschieden
    (`quality_mode=strict` => `SOURCE_REJECTED`, `quality_mode=degraded_ok` =>
    Backfill/`SOURCE_DEGRADED` oder harte Ablehnung bei nicht behebbaren Restlücken).
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

- Je `series_type`/`series_product` wird genau eine Quelle als `primary` markiert;
  weitere Quellen sind `fallback` oder `disabled`.
- Diese Kennzeichnung ist Source-Auswahllogik und gehört zu Phase 2, nicht zum
  reinen Adapter-/Importvertrag aus Phase 1.
- Preis:
  - Primär: EPEX (nur wenn Zugriff, Lizenz und Nutzungsbedingungen geklärt sind)
  - Fallback: Open Power System Data / Replay-konforme Datenquelle
- Forecast:
  - Primär: ENTSO-E
  - Fallback: Open-Meteo oder Copernicus für Wetter, je Featuretyp

Aktivierungslogik:
- `SOURCE_EMPTY` wird ohne zugelassenen, kompatiblen Fallback nur als harte
  Fehlerklasse `SOURCE_REJECTED` behandelt.
- Primärquelle wird standardmäßig genutzt.
- Bei Ausfall oder Qualitätsfehlern der Primärquelle wird nur auf eine explizit als
  `fallback` konfigurierte, kompatible Quelle gewechselt.
- Qualitäts- und Fallback-Entscheide inkl. `SOURCE_*`-Matrix, `quality_mode`,
  `fallback`-Kompatibilität, harte Ablehnungen und `status_flags` sind
  verbindlich im Abschnitt
  [Qualitätsentscheidungen bei `SOURCE_*` (verbindlich)](#qualitätsentscheidungen-bei-source_-verbindlich)
  festgelegt.
- Diese Phase beschreibt nur den Source-Auswahl- und Aktivierungspfad; die
  Status-/Fallback-Matrix wird nicht hier dupliziert.
- Ohne genehmigte Fallback-Quelle bleibt der produktive Pfad hart blockiert (kein
  Übergang über implizite Qualitätsdegradation).

Zusätzlich:

- Falls EPEX Lizenz nicht geklärt ist, startet Phase 2 direkt mit Fallback-Basisquelle
  und dokumentiert den Lizenzaufhebungsplan im Runbook.

### Phase 3: Forecast-Sidecar-Input

Wenn Forecasting nicht im EMS laufen soll, definiert dieser Slice nur den
Input-/Output-Vertrag für einen Forecast-Sidecar:

- Input:
  - Preis-Historie
  - Load forecast
  - Wind forecast
  - Solar forecast
  - Wetter
  - Kalenderfeatures
- Output:
  - `PriceSeries` für Punktforecast
  - `ForecastSeries`-Schema für deterministische Nicht-Preis-Forecasts (Load/PV/Wind/Wetter)
- optional später: Quantile oder Szenariopfade in separatem Slice

Vertraglich gilt der gleiche `SeriesEnvelope` für den Sidecar-Output (Zeitachse, Einheit, Source-Metadaten, Qualitätscode).

Probabilistische Forecasts sind ein Folgeslice; der heutige Optimierer
arbeitet mit deterministischen Preiswerten.

---

## Nicht-Ziele

- Kein Scraping ohne geklärte Nutzungsrechte.
- Kein Forecasting-Modell im Domain- oder Regelkreis.
- Kein Vendor-/Credential-Secret im Repository.
- Kein harter Netzaufruf in Unit Tests.
- Keine produktive Intraday-Continuous-Orderbook-Strategie im ersten Slice.

---

## Liefergegenstaende bei Aktivierung

1. Folge-ADR oder Architektur-Schräftigung für externe Datenquellen.
2. Adapter-Port für Preis-/Forecast-Quellen oder Erweiterung des
   bestehenden `IPriceSeriesSource`-Umfelds.
3. Schema-Migration `PriceSeries`/Store-Identität:
   - `series_id`, `series_version`, `source.provider_id` und Serien-Signaturfelder am
     produktiven Serienmodell oder deterministischem Ersatzschlüssel,
   - neuer `PriceSeriesKey`/Store-Key für InMemory und dauerhafte Stores,
   - Mapper `SeriesEnvelope` -> `PriceSeriesRequest`/Domain-Serie,
   - Migrations-/Fallback-Verhalten für Altimporte.
4. Quellenstatusmodell inklusive Refresh-/Fehlerstatus.
5. Mindestens ein Adapter mit Mock-/Replay-fähigem Testpfad.
6. API- oder Operator-UI-Status für Datenquellen.
7. Tests:
   - erfolgreiche Serienladung
   - fehlende Credentials
   - identische `(series_id, series_version, source.provider_id)` mehrfach laden mit identischem Payload bleibt idempotent
   - identische `(series_id, series_version, source.provider_id)` mit anderem `value_hash` führt deterministisch zu `SOURCE_REJECTED`
   - gleicher `(series_id, series_version)` mit anderem `provider_id` wird als separater Provider-Kontext bewertet
   - Primary-Validierung gegen Secondary-Adapter bei Primary-Ausfall
   - Rate-Limit-/Providerfehler
   - stale Daten werden je nach `quality_mode` korrekt geführt (`SOURCE_DEGRADED` vs `SOURCE_REJECTED`)
   - Zeitzone/DST bleibt konsistent
   - UTC-Zeitachse, Schrittweite und Lückenbehandlung (99,5 % Mindestabdeckung)
   - Optimierung bekommt exakt die erwartete Schrittzahl
   - operatorfähige Laufcodes werden bei Fehlerpfaden gesetzt (`SOURCE_*`)
8. Runbook für Credentials, Rate Limits und Provider-Ausfall.

---

## Akzeptanzkriterien

- Optimierung kann eine importierte oder extern geladene Preisreihe
  ohne Codepfad-Unterschied nutzen.
- Fehlende oder veraltete Quelldaten führen zu einem klaren
  operatorfähigen Fehler, nicht zu implizitem Fallback auf falsche Werte.
- Externe Datenquellen sind in Tests durch Replay-/Fixture-Daten ersetzbar.
- Quellenadapter verletzen keine Architekturregel: keine Markt- oder
  Regelentscheidung im Adapter.
- Jede Quelle-Fehlerklasse aus `SOURCE_*` ist im Run-Status und Operator-UI
  explizit sichtbar.
- Provider-Lizenz, Authentisierung und Nutzungsbedingungen sind vor
  produktiver Aktivierung dokumentiert.
- Fallback-Verhalten ist vor Aktivierung je Serie konfiguriert (erlaubt/unterbunden)
  und dokumentiert.

## Definition of Done (DoD)

- [ ] Daten-/Fehlervertrag ist vollständig definiert:
  - `SeriesEnvelope` inkl. `series_status`, `source_eval_status`, `status_flags`, `status_detail`,
  - `source_metadata` mit Versions-/Zeit-/Provider-Informationen,
  - klare Trennschärfe Producer- und Operator-Sichten.
- [ ] Persistenzkompatibilität hergestellt:
  - bestehende Serie- und Import-Speicher (InMemory + dauerhafte Stores, soweit vorhanden)
    führen `series_id`/`series_version`/`source.provider_id` in der Serien-Identität
    oder einem deterministisch äquivalenten Ersatzschlüssel.
  - `PriceSeries` selbst, `PriceSeriesKey`, Import-Request-Mapping und alle Store-Pfade
    sind Teil derselben Migration; reine Adapter-Vertragsänderungen reichen nicht.
  - ohne diese Erweiterung bleibt eine produktive Aufnahme neuer Serienkennungen im Slice gesperrt (Fallback-Pfad definiert).
  - Produktive Familie-/Versionswechsel sind nur per dokumentiertem Cutover erlaubt; ein stiller `series_version_family`- oder Revisionsfamilienwechsel ohne Release-Freigabe führt zu harter Ablehnung.
- [ ] Import-Adapter sind bereit für produktive Nutzung:
  - je Typ mindestens zwei Adapter (1 Primär + 1 Fallbackfähiger): Preis und Forecast,
  - je Typ klar definierte Qualitäts- und Fallback-Pfade inkl. `quality_mode`.
  - klare Refresh-/Staleness-/Rate-Limit-Politik.
- [ ] Fallback-/Degradationslogik ist deterministisch umgesetzt:
  - harte Fehler (`SOURCE_REJECTED`) vs. degradierte Nutzung (`SOURCE_DEGRADED`) klar getrennt,
  - kombinierte Qualitätsereignisse via `status_flags`,
  - `SOURCE_EMPTY` ohne expliziten degraded-Zulassungsweg bleibt hart abweisend.
- [ ] Versions- und Idempotenzregeln umgesetzt:
  - eindeutiger Revisionsvergleich pro `(series_id, series_version, source.provider_id)`,
  - payload-identisches Re-Load ist idempotent,
  - Provider-Kontexte trennscharf unabhängig von identischem `series_id`/`series_version`.
  - kontrollierter Cutover/Rollback für `series_version_family` ist dokumentiert und mit den obigen Family-Tests verifiziert.
- [ ] Liefergegenstände bei Aktivierung umgesetzt:
  - ADR/Architektur-Spezifikation,
  - Source-Port- und Statusmodell,
  - Replay-/Mock-Testpfad,
  - API-/Operator-Status,
  - Runbook (Credentials, Limits, Ausfälle).
- [ ] Akzeptanzkriterien aus diesem Dokument vollständig erfüllt.

---

## Abschlussentscheidungen

- Wird in einem ersten produktiven Slice zusätzlich `ForecastSeries`-Schema eingeführt?
  - Ja, als deterministisches Point-Forecast-Schema und erweiterter Liefervertrag,
    solange es ein kompatibles `SeriesEnvelope` bleibt; Quantile und Szenariopfade
    bleiben Folgeslice.
- Sollen Forecast-Szenarien/Quantile direkt modelliert oder erst in einem
  separaten probabilistischen Optimierungsslice behandelt werden?
  - Entscheidung: Erst im ersten Slice sind nur point-forecast-Pfade relevant; Quantile/Probabilistik geht in ein Folge-Slice.
- Wo lebt langfristiger Quellen-Cache: In-Memory, Postgres/Timescale oder
  externer Datenservice?
  - Entscheidung für produktiv: bestehende Persistenz/Store des Kernsystems wird genutzt, ergänzt durch optionales dediziertes Forecast-Cache-Modul im zweiten Phase.
