# Plan: Preisquellen- und Forecast-Adapter

**Dokumenttyp:** MVP-Spec / offen
**Status:** Open - wartet auf Quellen-/Produkttrigger
**Datum:** 2026-05-24
**Quelle:** [`note-market-and-colocation-followups.md`](note-market-and-colocation-followups.md)
**Bezug:**
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md),
[`../done/plan-RM-M5-07.md`](../done/plan-RM-M5-07.md),
[`plan-domain-migration-price-series-identity.md`](plan-domain-migration-price-series-identity.md)

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
  - Preisreihen werden vor dem Optimierer auf die aktuelle Domäne projiziert;
    Forecastreihen bleiben in diesem Slice Contract-/DTO-Daten, bis ein produktiver
    Forecast-Domaintyp aktiviert wird.
- Vor produktiver Aktivierung dieses Slices ist der Pre-Slice
  [`Domain-Migration PriceSeries.Identity`](plan-domain-migration-price-series-identity.md)
  abzuschließen.
- Die bestehende `PriceSeries`-/Persistenzkette wird ausschließlich im
  Identity-Pre-Slice migriert. Dieser Adapter-Slice konsumiert den dort
  abgeschlossenen Identitätsvertrag; ohne diese Erweiterung bleibt F-MKT-02 im
  produktiven Modus auf den bisherigen Altpfad beschränkt, bis ein dedizierter
  Speicher-Migrationspfad freigegeben wurde.
- In der Einführungsphase gilt "Dual-Path":
  - Altpfad unverändert lauffähig,
  - Neupfade können aktiv/inaktiv geschaltet werden,
  - späterer Wechsel auf den neuen Pfad ohne Schnittstellenbruch durch einheitlichen Mapper.

## Verbindlicher Daten- und Fehlervertrag

Einheitliche Adaptervertraege für Preis- und Forecastdaten:

### Gemeinsame Serien-Typsignatur (verbindlich)

- Provider-Adapter liefern intern ein einheitliches `SeriesEnvelope`.
- Der bestehende `PriceSeries`-/`IPriceSeriesSource`-Pfad bleibt mit seiner
  heutigen Signatur die Application-Grenze; `LoadAsync` gibt weiterhin
  `PriceSeries` zurück. Für Preisreihen mappt eine Adapter-Bridge
  `SeriesEnvelope` deterministisch auf `PriceSeries`, bevor der bestehende Port
  bedient wird.
- Forecasts laufen über den neuen `IForecastSeriesSource`-Port
  (`LoadForecastAsync`) und verwenden den `SeriesEnvelope`-/Forecast-Contract
  direkt; kein paralleler `LoadPriceSeriesAsync`-Name ohne explizite
  Migrationsphase.
- Der minimale `SeriesEnvelope` enthält:
  - `series_id` (stabil)
  - `series_version` (stabil, monoton wachsend oder semantische Versionskennung)
    - Erforderlich und vergleichbar pro kanonischer Serien-Signatur; die
      Signatur ist unten im Abschnitt `Repräsentationsstabilität` verbindlich
      definiert und darf hier nicht als Teilmenge dupliziert werden.
    - Akzeptiert werden:
      - streng monoton wachsende numerische Versionen (int-kompatibel),
      - oder Zeitstempel-basierte semantische Versionen (z. B. `2026-05-24T12:00:00Z-v3`).
    - Andere Formate sind unzulässig und führen auf dem Importpfad zu
      `source_eval_status=SOURCE_SCHEMA_MISMATCH` und `series_status=SOURCE_REJECTED`.
  - `series_family_alias`: optional bei stabiler Serienfamilie; Pflichtfeld beim
    kontrollierten Family-Wechsel ohne neue `series_id`. Prüfbare Write-Regel:
    Wird derselbe Serienidentitäts-Sub-Key ohne `series_version`
    (`series_id`, `source.provider_id`, `series_type`, `series_product`,
    normalisiertes `market_bid_area`, normalisierte `site_id`) mit anderer
    `series_version_family` geliefert als die bisher persistierte
    Referenzfamilie und bleibt `series_id` unverändert, muss
    `series_family_alias` gesetzt sein. Fehlt der Alias, ist der Import mit
    `source_eval_status=SOURCE_SCHEMA_MISMATCH` und
    `series_status=SOURCE_REJECTED` abzulehnen. Ein Wechsel der
    Familienkennung erfolgt nur über diesen Alias bzw. durch neue
    Serienkennung, nicht per stillschweigendem Wechsel derselben
    `series_id`. Beim ersten gültigen Write existiert noch keine
    Referenzfamilie; dort ist `series_family_alias` nicht erforderlich. Ab dem
    ersten akzeptierten Write ist ein Familienwechsel ohne neue `series_id`
    aliaspflichtig.
  - `site_id` (optional)
    - erforderlich für standortgebundene Forecast-/Erzeugungs-/Last-/Wetter-Reihen
    - optional oder leer für produktweite Forecast-Daten ohne Standortkontext
  - `market_bid_area` (optional)
    - erforderlich für `series_type=price`, damit die bestehende `PriceSeries`-Schiene
      den Marktbereich eindeutig identifizieren kann
    - empfohlen für gebietsgebundene Forecasts (`series_type=forecast` und
      `series_product` in `{load, pv, wind}`), sofern die Reihe nicht eindeutig
      standortgebunden über `site_id` ist. Operator-/Replay-Sichten dürfen den
      Gebietsbezug nicht aus Provider-Konventionen erraten müssen.
  - `series_type` (`price` oder `forecast`)
  - `series_product` (z. B. `day_ahead`, `intraday`, `load`, `pv`, `wind`, `weather-temp`)
  - `unit` (z. B. `EUR/MWh`, `kW`, `kWh`, etc.)
  - `resolution_minutes` (`> 0`, ganzzahlig)
  - `timezone` (muss `UTC` sein)
  - `horizon_start_utc`, `horizon_end_utc`
  - optionale Co-Location-Vorverarbeitungsmetadaten:
    - `alignment_mode` (`reject` | `trim-to-common` | `resample`)
      - `resample` ist reserviert und im ersten Slice hart gesperrt, bis ein
        eigener, versionierter Preprocessing-Slice die Aggregations-/
        Interpolationsregel freigibt.
    - `alignment_prepared` (bool, nur bei `alignment_mode=trim-to-common` sinnvoll)
    - `alignment_prepared_by` (string, z. B. `forecast-preprocessor/v1`, `batch-trimmer/...`);
      gesetzt durch den versionierten Preprocessor, nicht durch einen
      nachgelagerten Optimierungsconsumer.
    - `alignment_prepared_horizon_start_utc`: inklusiver Start des nach
      Vorverarbeitung tatsächlich konsumierbaren Zielhorizonts. Bei
      `trim-to-common` ist dies der getrimmte Common-Horizon, nicht der
      ursprüngliche Quellhorizont.
    - `alignment_prepared_horizon_end_utc`: exklusives Ende desselben
      konsumierbaren Zielhorizonts.
      Beide Horizon-Felder sind Pflicht, wenn `alignment_prepared=true`, müssen
      innerhalb des ursprünglichen `horizon_start_utc`/`horizon_end_utc` liegen
      und gehen in `value_hash`/Idempotenz ein, damit Re-Loads denselben
      Vorverarbeitungsschritt bytegleich auditieren können.
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
      - reserviert für spätere Quellen-Gültigkeitsfenster; im ersten Slice sind
        sie reine Audit-Metadaten und kein Bestandteil von Idempotenz,
        Family-Cutover, Status-Mapping, Validierung oder `value_hash`.
    - `provider_request_id`
  - `series_status` (finaler Endstatus je Serie): `SOURCE_OK`, `SOURCE_DEGRADED`, `SOURCE_FALLBACK_USED`, `SOURCE_REJECTED`
  - `source_eval_status` (Rohstatus je Providerlauf, vor Fallback-/Degradationsentscheidung): `SOURCE_OK`, `SOURCE_AUTH_ERROR`, `SOURCE_RATE_LIMIT`, `SOURCE_UNAVAILABLE`, `SOURCE_EMPTY`, `SOURCE_STALE`, `SOURCE_GAP`, `SOURCE_SCHEMA_MISMATCH`, `SOURCE_RETRY_EXHAUSTED`, `SOURCE_TRANSITIONAL_INPUT`
  - `status_flags` (array, required bei `series_status != SOURCE_OK`; optional bei `SOURCE_OK`): `SOURCE_FALLBACK_USED`, `SOURCE_BACKFILL`, `SOURCE_RATE_LIMIT`, ...
  - `status_message` (optional): menschenlesbar
  - `status_detail` (optional): strukturierte Zusatzinfo (z. B. `{ "source_code": "SOURCE_STALE", "backfill_intervals_closed": 2, "effective_source_id": "opsd-..." }`)
  - `value_hash`: optional für reine Einmalimporte ohne Idempotenz-/Cutover-Vertrag;
    verpflichtend, sobald Versions-Idempotenz, Re-Load-Vergleich,
    Family-Cutover oder Rollback aktiviert ist. In der Erstaktivierung von Phase
    1/2 ist `value_hash` Pflicht, außer der Adapter ist explizit als
    Einmalimport ohne Re-Load-Vertrag deklariert. Der Hash stabilisiert den
    Payload-Vergleich.

### Kanonische `value_hash`-Berechnung (verbindlich)

`value_hash` ist `sha256(canonical_bytes)` als lower-case Hex-String ohne
Prefix. `canonical_bytes` sind UTF-8-Bytes eines kanonischen JSON-Objekts mit
lexikografisch sortierten Property-Namen; Arrays behalten die unten definierte
Reihenfolge. Es gibt keine weitere Byte-Order-/Endian-Interpretation.

Eingabemenge:

- `series_type`, `series_product`, `unit`, `resolution_minutes`
- `horizon_start_utc`, `horizon_end_utc`
- `alignment_mode`
- falls gesetzt: `alignment_prepared`, `alignment_prepared_by`,
  `alignment_prepared_horizon_start_utc`, `alignment_prepared_horizon_end_utc`
- `values`

Normalisierung:

- Zeitstempel werden in UTC im Roundtrip-ISO-8601-Format mit `Z` serialisiert;
  lokale Zeitzonen und Offset-Varianten sind vor dem Hash zu normalisieren.
- `values` werden strikt aufsteigend nach `timestamp_utc` sortiert. Doppelte
  Zeitstempel innerhalb derselben Serie sind `SOURCE_SCHEMA_MISMATCH`.
- Jeder Wertpunkt enthält `timestamp_utc`, `value` und `value_type`.
  `value_type` wird lower-case (`actual` oder `forecast`) serialisiert.
- `value` wird als JSON-Zahl in invariant-culture Roundtrip-Repräsentation
  serialisiert; `NaN`, `Infinity`, lokalisierte Dezimaltrennzeichen und
  stringifizierte Zahlen sind unzulässig.
- `confidence` ist im ersten Slice kein produktiver Hash-Bestandteil. Wird eine
  spätere Extension dafür freigegeben, wird `confidence` genau dann je Punkt in
  die kanonische Nutzlast aufgenommen, wenn es gesetzt ist, und nutzt dieselbe
  numerische Repräsentation wie `value`.
- `retrieved_at_utc`, `valid_from_utc`, `valid_to_utc`, `status_message`,
  `status_detail`, `series_status`, `source_eval_status`, `status_flags`,
  Credentials, Provider-Request-Metadaten und Lizenzmetadaten gehen nicht in den
  Hash ein.

Der Pre-Slice
[`Domain-Migration PriceSeries.Identity`](plan-domain-migration-price-series-identity.md)
implementiert denselben Algorithmus in Store-/Mapper-Tests; dieser Adapterplan
ist die fachliche Eingabequelle für die Hash-Nutzlast.
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
      2. **Dual-Active-Wahrnehmung**: Alte und neue Family werden parallel beobachtet.
         Der Beobachtungszeitraum startet beim späteren der beiden Zeitpunkte
         `dual_active_started_at` aus dem Release-Runbook und erstem
         akzeptiertem Dual-Active-Import, in dem alte und neue Family im selben
         Release-Fenster mindestens `SOURCE_OK` oder ausdrücklich zugelassenes
         `SOURCE_DEGRADED` liefern. Fehlt `dual_active_started_at` im Runbook,
         gilt `first_accepted_dual_active_import_time` als Anker und wird im
         Audit-Bundle festgehalten. Der Default verlangt ab diesem Anker
         mindestens zwei vollständige Release-Fenster. Ein Release-Fenster ist
         mindestens ein produktiver Import-/Refresh-Zyklus und mindestens
         7 Kalendertage.
         Damit braucht der Cutover im Default mindestens zwei vollständige
         Zyklen und mindestens 14 Kalendertage Beobachtung.
         Der 14-Tage-Default ist ein serienübergreifendes Wartezeitfenster,
         keine Stop-Condition: auch durchgehend idempotente `value_hash`-
         Vergleiche nach weniger als 14 Kalendertagen erlauben keinen
         Early-Cutover ohne expliziten Release-Runbook-Override.
         Die Zahl ist ein konservativer Produkt-Default, kein stiller
         Code-Default. `value_hash`-Abweichungen werden in `status_detail`
         strukturiert auditierbar gemacht; `status_flags` erhält nur einen
         eigenen Flag-Wert, falls dieser später explizit eingeführt wird.
      3. **Cutover freigeben**: Neue Family wird produktiv als `primary` markiert und auf mindestens `SOURCE_OK` oder ausdrücklich zugelassene `SOURCE_DEGRADED` gesetzt.
      4. **Produktiver Umschaltpunkt**: Alte Family in produktiven Runs auf `SOURCE_REJECTED`/`CanExecute=false` halten; alte Werte sind nur noch Replay/Diagnose sichtbar.
      5. **Rollback-Fenster**: Rückkehr auf alte Family nur mit explizitem
         Release-Block möglich. Rollback ist nur zulässig, wenn die neue Family
         im Beobachtungsfenster die Lastserie nicht ohne harten Fallback
         akzeptierbar liefern konnte. Die Runbook-Marker `release_block` und
         `controlled_switchover` werden im Aktivierungs-Runbook definiert,
         inklusive Ablageort, Berechtigung und Löschregel.
      6. **Abschluss**: Alter Family-Schlüssel wird auf `ARCHIVED` gesetzt; neue Family läuft ohne Alias-Wechsel weiter.
  - Für einen stabilen Tie-Break werden bei gleicher Klasse numerisch zuerst
    `series_version` (wie definiert), danach nur bei unterschiedlichen, gültigen
    Serienfamilien der hash-basierte Payload-Fingerprint (`value_hash`) als
    auditierbarer Sortierschlüssel verwendet.
    - „gleiche Klasse“ meint die normalisierte Vergleichsschicht der festen Versionsfamilie pro Serie/Provider.
    - Innerhalb derselben Serien-Signatur und derselben `series_version` ist
      `value_hash` kein Tie-Break: ein abweichender Hash ist dort immer ein harter
      Idempotenzfehler.

Kontrollierte Abnahmetests für Family-/Versionswechsel:
- **Dual-Active-Fähigkeit**: Alte und neue Family werden gleichzeitig geladen; beide dürfen im Beobachtungsfenster aktiv sein, solange beide dieselbe Semantik (`series_type`, `series_product`, `market_bid_area`, `site_id`, `resolution_minutes`) liefern.
- **Unsichtbarer Familienwechsel wird blockiert**: Derselbe `series_id`/`series_product` ohne neue Alias-/Schlüsselstruktur und ohne Release-Step darf nie von `numeric` auf `timestamp` (oder umgekehrt) springen.
- **Harsh-Case bei Alias-Ungleichheit**: `series_id`/`series_version`/`source.provider_id` gleich, aber Family-Wechsel ohne Revisionsbruch, liefern unterschiedliche `value_hash` -> harte `SOURCE_REJECTED`.
- **Regressionstest `value_hash`**: Gleiche Vollsignatur + gleicher Hash ist idempotent; gleicher Hash plus gleicher `series_version`/Provider muss ohne Seiteneffekte mehrfach akzeptiert werden.
- **Rollback-Sichtbarkeit**: Im kontrollierten Rollback werden alte und neue `series_family_alias` mindestens bis zum Ende eines Validation-Fensters in Operator-/Replay-Sicht weiterhin explizit aufgelöst.

Hinweis zur Semantik:
- `source_eval_status` ist der interne Rohstatus pro Providerlauf (inkl. Primär- und Fallback-Pfad, vor Qualitätsentscheidung).
- `series_status` ist der externe Endstatus für Operator/API-Verträge (single-value).
- Kombinierte Ereignisse werden weiterhin nur in `status_flags` kodiert.
- `SOURCE_DEGRADED` ist ausschließlich ein finaler `series_status` und nicht als `source_eval_status` vorgesehen.
  Operator-Audit muss degradierte Endstatus über `source_eval_status`,
  `status_flags` und `status_detail.reason` unterscheiden können (z. B.
  Backfill, Stale-Weiterbetrieb oder transitional input).
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
    - fehlt `value_hash`, wird keine Idempotenzprüfung durchgeführt; ein Re-Load
      muss deterministisch eine neue `series_version` erzeugen oder als
      Einmalimport ohne Re-Load-Vertrag behandelt werden.
    - `value_hash` darf nur bei explizit deklarierten Einmalimporten fehlen; in
      allen Phase-1/2-Produktivpfaden mit Re-Load-, Idempotenz-, Cutover- oder
      Rollback-Vertrag ist fehlender Hash ein harter Vertragsfehler.
  - Für inhaltliche Korrekturen ist ein Revisionswechsel zwingend:
    entweder höhere `series_version` derselben Familie oder ein kontrollierter
    Migrationspfad über `series_family_alias` (oder neue `series_id`).
    Ohne expliziten Revisionswechsel ist keine In-Place-Korrektur derselben Version im produktiven Lauf erlaubt.
  - Eine eingehende Serie mit älterer Version als die letzte akzeptierte Referenzversion für dieselbe
    Serien-Signatur und dieselbe `series_version_family` wird nach dem
    stabilisierten Vergleich als harte Versionsabweichung
    (`series_status=SOURCE_REJECTED`) abgewiesen.
    - Der stabile Vergleich nutzt die feste Versionsfamilie (`int` oder
      `timestamp`) und danach den normalisierten Fortschritt in dieser Familie.
      Während eines Dual-Active-Cutovers werden alte und neue Family getrennt
      bewertet; Korrekturen der alten Family im Beobachtungsfenster gelten nicht
      automatisch als Regression der neuen Family.
  - Signaturänderungen bei optionalen Feldern sind harte Inkompatibilitäten:
    Wenn `market_bid_area`, `site_id`, `unit` oder `resolution_minutes` für dieselbe
    `series_id`/`source.provider_id`/`series_type`/`series_product`-Kombination variieren,
    ergibt sich `source_eval_status=SOURCE_SCHEMA_MISMATCH` / `series_status=SOURCE_REJECTED`.
- Mapping-Regel:
  - `series_type=price` wird auf bestehende Preis-Produkte in `PriceSeries` abgebildet.
  - dafür ist `market_bid_area` verpflichtend und wird auf das entsprechende
    `PriceSeries`-Feld gemappt.
  - `series_type=forecast` dient in diesem Slice als Adapter-/Sidecar-Vertrag für
    deterministische Point-Forecasts. Das hier eingeführte `ForecastSeries`-Schema ist
    ein DTO-/Contract-Typ an der Application-Grenze, noch kein produktiver
    EMS-Domaintyp für Optimierungsentscheidungen.
  - Co-Location-Verbraucher projizieren `SeriesEnvelope` deterministisch in ihren
    eigenen Domaintyp `LocalGenerationSeries`; die Optimierung konsumiert diesen
    Domaintyp, nicht `ForecastSeries` direkt. Ein späterer Slice kann denselben
    Contract in ein produktives Forecast-Domainmodell überführen.

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
- `source_eval_status=SOURCE_EMPTY`:
  - Bei erfolgreicher kompatibler Fallback-Quelle: `series_status=SOURCE_FALLBACK_USED`.
  - Ohne erfolgreichen kompatiblen Fallback: `series_status=SOURCE_REJECTED`.
  - `quality_mode=degraded_ok` darf eine leere Primärantwort nie allein in
    `SOURCE_DEGRADED` umwerten.
- `source_eval_status` aus `{SOURCE_STALE, SOURCE_RATE_LIMIT, SOURCE_UNAVAILABLE, SOURCE_RETRY_EXHAUSTED}`:
  - Bei vorhandenem kompatiblem Fallback folgt die Fallback-Evaluierung.
  - Bei erfolgreichem Fallback: `series_status=SOURCE_FALLBACK_USED` oder
    `SOURCE_DEGRADED` je nach Backfill/Qualitätsminderung.
    Präzedenz bei kombinierten Ereignissen ist verbindlich:
    Qualitätsminderung schlägt Fallback. Wenn Fallback und Backfill bzw. andere
    Degradation zugleich auftreten, ist `series_status=SOURCE_DEGRADED` und
    `status_flags` enthält mindestens `SOURCE_FALLBACK_USED` und den
    Degradationsflag, z. B. `SOURCE_BACKFILL`.
  - Bei fehlendem kompatiblen Fallback: harte Abweisung außer im `degraded_ok`-Modus
    für `SOURCE_STALE` (-> `SOURCE_DEGRADED`).
- `source_eval_status=SOURCE_TRANSITIONAL_INPUT`:
  - nur für explizit freigegebene manuelle Import-/API-Push- oder
    CSV/Fixture-Übergangspfade zulässig.
  - bei `quality_mode=degraded_ok`: `series_status=SOURCE_DEGRADED`,
    `status_detail` enthält mindestens
    `format=kv1;reason=LOCAL_GENERATION_TRANSITIONAL_INPUT`.
  - bei `quality_mode=strict`: `series_status=SOURCE_REJECTED`.
  - automatische externe Abrufe dürfen diesen Rohstatus nicht setzen.
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
- `SOURCE_EMPTY` bleibt hart als `SOURCE_REJECTED`, wenn keine kompatible
  Fallback-Quelle erfolgreich eine nicht-leere Serie liefert. Ein erfolgreicher
  Fallback darf daraus `SOURCE_FALLBACK_USED` machen; `SOURCE_EMPTY` darf nie
  durch `quality_mode=degraded_ok` allein zu `SOURCE_DEGRADED` werden.

- Konsistenz-Regel:
  - Wenn `series_type=price`, ist `market_bid_area` Pflichtfeld; `site_id` optional.
  - Wenn `series_type=forecast` in einem Co-Location- oder standortgetrennten
    Optimierungsscope genutzt wird, ist `site_id` Pflicht. Reine systemweite
    Wetter-/Marktfeatures ohne Standortbezug dürfen `site_id` leer lassen.
  - Co-Location-Vorverarbeitung:
    - `alignment_mode=reject` ist produktiv der Default.
    - `alignment_mode=trim-to-common` ist nur zulässig, wenn `alignment_prepared=true` gesetzt ist, die Horizon-Metadaten vorhanden sind und ein vorbereiteter, deterministisch versionierter Vorverarbeitungspfad dokumentiert wurde.
- Statusmodell:
  - Die finale Serienqualität bleibt ein einzelner Wert in `series_status` (`SOURCE_OK`, `SOURCE_DEGRADED`, `SOURCE_FALLBACK_USED`, `SOURCE_REJECTED`).
  - Kombinierte Ereignisse (z. B. Fallback **und** Backfill) werden nur durch `status_flags` abgebildet; `series_status` bleibt ein `single-value`.
- `status_flags` ist verpflichtend, wenn `series_status != SOURCE_OK`.
- Combiner-Beitrag:
  - Dieser Slice ist Eigentümer des gemeinsamen `CanExecute`-Combiner-Beitrags
    `source_ok` aus
    [`plan-domain-migration-optimization-run-can-execute.md`](plan-domain-migration-optimization-run-can-execute.md).
  - Für jede im Request verpflichtende Serie gilt `source_ok=false`, wenn die
    Serie fehlt, `series_status=SOURCE_REJECTED` ist oder ein vorhandener
    Qualitätsgrad im konsumierenden Slice nicht zugelassen ist.
  - `series_status=SOURCE_OK` setzt für diese Serie `source_ok=true`.
  - `series_status=SOURCE_FALLBACK_USED` und `SOURCE_DEGRADED` setzen
    `source_ok=true` nur, wenn der konsumierende Slice den Qualitätsgrad
    ausdrücklich erlaubt (`quality_mode=degraded_ok` bzw. explizit zugelassener
    Fallback); sonst ziehen sie `source_ok=false`.
  - Mehrere Pflichtserien aggregieren all-or-nothing: ein einzelner
    `source_ok=false`-Beitrag zieht den aggregierten Beitrag auf `false`.

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
  - lebt am Serien-/Adapterlauf und muss in Source-Audit sowie im daraus
    erzeugten Optimierungsrequest sichtbar sein; er ist kein globaler
    Deployment-Schalter.
  - `strict` (Default): Keine Degradation erlaubt. Fallback ist nur zulässig,
    wenn daraus `SOURCE_OK` oder `SOURCE_FALLBACK_USED` resultiert und kein
    zusätzliches Qualitätsflag (`SOURCE_BACKFILL`) gesetzt ist.
  - `degraded_ok`: Degradierte Nutzung erlaubt, Ergebnis muss als `SOURCE_DEGRADED` gekennzeichnet werden.
- `min_coverage_ratio` Pflicht:
  - Produkt-Default `0.995` (99,5 %), wenn keine explizite Policy gesetzt ist.
    Das ist kein universelles Mindestlimit für alle Sonderpfade: niedrigere
    konfigurierte Werte sind nur mit explizitem Feature-/Runbook-Override im
    `quality_mode=degraded_ok` zulässig und müssen im Audit sichtbar sein.
  - Bezug auf Rohdaten vor Backfill (`raw_values_coverage`).
  - `n_intervals` ist die erwartete Schrittzahl im Zielhorizont.
  - Es gibt keine separate harte `48`-Intervalle-Schwelle.
  - Zulässige Rohdatenlücken werden deterministisch berechnet:
    `max_missing_raw_intervals = floor(n_intervals * (1 - min_coverage_ratio))`.
    Bei 96 Intervallen sind damit 0 Rohdatenlücken zulässig; bei 200 Intervallen
    genau 1. Diese Rundungsregel ist bewusst konservativ und ersetzt
    horizon-spezifische Magic Numbers.
    Operative Folge: Bei typischen 24-h-/15-min-Horizonten greift Backfill wegen
    dieser Mindestabdeckung praktisch nicht. Das ist im Default gewollt:
    Backfill ist nur bei längeren Horizonten oder explizit konfigurierter
    niedrigerer Mindestabdeckung im `quality_mode=degraded_ok` aktiv. Tests für
    `SOURCE_BACKFILL` müssen deshalb entweder einen längeren Horizon oder eine
    explizit abgesenkte `min_coverage_ratio` verwenden; ein typischer
    24-h-/15-min-Defaulttest erwartet keinen Backfill.
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
  - Operator-Hinweis (Begründung, keine zweite Regel): Behebbare Gaps bleiben in
    `strict` abgelehnt, weil Backfill einen geänderten Datenbestand erzeugt.
    Stale-Daten dürfen in `degraded_ok` nur kontrolliert weiterlaufen, weil ihre
    Zeitachse vollständig ist und die Qualitätsminderung sichtbar markiert wird.

### Qualitätsentscheidungen bei `SOURCE_*` (Ablauf)

Die Entscheidungslogik ist zweistufig:
- `source_eval_status` wird zuerst berechnet (`SOURCE_*` inklusive `SOURCE_OK`, harte oder degradierende Rohcodes).
- Daraus wird deterministisch ein externer `series_status` abgeleitet (`SOURCE_OK`, `SOURCE_DEGRADED`, `SOURCE_FALLBACK_USED`, `SOURCE_REJECTED`).

Die `SOURCE_*`-Auswertung ist für jede Serie deterministisch:
- Die folgenden Punkte sind ein Verweis auf die oben genannten **Kanonischen Ableitungsregeln** und keine zweite, abweichende Normdefinition.

1) Vorvalidierung
- bei Schema-, Zeitachsen- oder Horizonabweichungen: sofort `source_eval_status=SOURCE_SCHEMA_MISMATCH` → `series_status=SOURCE_REJECTED` (kein Fallback).

2) Rohcode-Auswertung
- Rohcodes werden ausschließlich nach den **Kanonischen Ableitungsregeln** oben
  klassifiziert. Diese Flow-Sektion wiederholt die Matrix nicht normativ.
- `SOURCE_OK` geht direkt in die Endstatuszuordnung.
- harte Rohcodes (`SOURCE_AUTH_ERROR`, `SOURCE_SCHEMA_MISMATCH`, nicht behebbare
  `SOURCE_GAP`) enden ohne Retry/Fallback in `SOURCE_REJECTED`.
- `SOURCE_EMPTY` darf nur über einen erfolgreichen kompatiblen Fallback in
  `SOURCE_FALLBACK_USED` wechseln; ohne Fallback bleibt es `SOURCE_REJECTED`.
- fallback-fähige Rohcodes (`SOURCE_EMPTY`, `SOURCE_STALE`,
  `SOURCE_RATE_LIMIT`, `SOURCE_UNAVAILABLE`, `SOURCE_RETRY_EXHAUSTED`) gehen in
  Schritt 3.

3) Fallback- und Qualitätsmoduslogik
- Fallback wird nur für die in den kanonischen Ableitungsregeln genannten
  fallback-fähigen Rohcodes versucht.
- Der Fallback-Pfad darf keine eigenen Endstatusregeln formulieren; er übergibt
  Rohcode, Fallback-Ergebnis und Qualitätsflags an die kanonische Ableitung.

4) Endstatuszuordnung bei akzeptierter Serie
- Die Endstatuszuordnung ist ausschließlich die kanonische Ableitung oben.
  Diese Flow-Sektion beschreibt nur die Reihenfolge der Verarbeitung und darf
  keine abweichende Statusmatrix enthalten.

Hinweis:
Endstatus ist ein einzelner Wert (`single-value`) je Serie (`series_status`).
Fallback-/Degradationsdetails werden zusätzlich in `status_flags` und optionalem `status_detail` erfasst.

### Fehler-/Rohcodes und Endstatus (Vokabular)

- `SOURCE_AUTH_ERROR` – Authentifizierung fehlt/fehlerhaft
- `SOURCE_RATE_LIMIT` – Rate-Limit erreicht / Retry empfohlen
- `SOURCE_UNAVAILABLE` – Provider temporär nicht erreichbar
- `SOURCE_EMPTY` – leere Provider-Antwort
- `SOURCE_STALE` – Daten jenseits `max_stale_age_minutes`
- `SOURCE_GAP` – nicht behebbare Zeitlücken
- `SOURCE_SCHEMA_MISMATCH` – Zeitachse/Einheit/Schemafehler
- `SOURCE_RETRY_EXHAUSTED` – Retries erfolglos
- `SOURCE_TRANSITIONAL_INPUT` – explizit freigegebener manueller/Fixture-
  Übergangsinput ohne automatische externe Providerabfrage

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
  - bestehendes `LoadAsync` (`IPriceSeriesSource`) bleibt `PriceSeries`-basiert;
    `SeriesEnvelope` wird davor in der Adapter-Bridge gemappt
  - `LoadForecastAsync` (`IForecastSeriesSource`)
  - Adapter-Bridge zwischen `SeriesEnvelope`, produktiven Preis-Domainmodellen und
    Forecast-Contract-Daten
  - `IPriceSeriesImportSink` bleibt der manuelle/Fixture-Import-Einstieg, darf
    aber keine eigene Status- oder Identitätslogik behalten: Importe erzeugen
    vor Persistenz denselben `SeriesEnvelope` bzw. laufen über denselben Mapper
    wie externe Adapter.
  - verbindliches `SeriesEnvelope` gemäß obigem Datenvertrag
  - deterministisches Mapping in die bestehende Import-Pipeline (`IPriceSeriesSource`).
  - deterministische Projektion von Forecast-`SeriesEnvelope` in konsumierende
    Domaintypen wie `LocalGenerationSeries`; `ForecastSeries` selbst bleibt
    Contract-/DTO-Daten.
- Persistenz-/Schema-Migration als Phase-1-Voraussetzung:
  - `PriceSeries`/Import-Request erhalten die Serienidentitätsfelder oder einen
    deterministisch äquivalenten Ersatzschlüssel.
  - Der Identity-Pre-Slice stellt den neuen Store-/Mapper-Schlüsselvertrag für
    InMemory und dauerhafte Stores bereit.
  - Ohne diese Migration dürfen neue Serienkennungen nicht produktiv aufgenommen werden.
  - Die Umsetzung liegt im Pre-Slice
    [`Domain-Migration PriceSeries.Identity`](plan-domain-migration-price-series-identity.md);
    dieser Adapter-Slice konsumiert nur den abgeschlossenen Identitätsvertrag.
- Cache-/Refresh-Vertrag:
  - TTL
  - `max_stale_age_minutes`
  - provider rate limit + Retry-Backoff
- operatorfähige Fehlercodes inkl. `SOURCE_*`
- `value_hash` ist für produktive Phase-1-Adapter mit Re-Load- oder
  Idempotenzvertrag verpflichtend und wird in Tests/Audit sichtbar gemacht;
  Einmalimporte ohne Re-Load-Vertrag müssen explizit deklariert sein.
- API-/Operator-Status für Quellen:
  - letzter erfolgreicher Abruf + aktiver Statuscode
  - letzter Fehler
  - letzter Fehlercode (`SOURCE_*`)
  - Datenhorizont
  - Quelle und Produkt
  - Serien-ID plus `series_version` des zuletzt geladenen Satzes
- Deterministische Reaktionsregeln:
  - Die Source-Auswahl, Retry-/Fallback-Versuche und Operator-Ausgaben folgen
    den kanonischen Ableitungsregeln aus dem Abschnitt
    [Verbindlicher Daten- und Fehlervertrag](#verbindlicher-daten--und-fehlervertrag).
  - Dieser Phase-1-Block definiert keine zweite Statusmatrix. Er ergänzt nur,
    dass Fallback-Quellen `SeriesEnvelope`, Einheit und Horizon exakt kompatibel
    liefern müssen, bevor die kanonische Ableitung angewendet wird.

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
  - Produktiv verpflichtende Forecast-Familien für Phase 2: `load` und
    `weather-temp`.
  - `load`: Primär ENTSO-E, Fallback Replay-/historische Lastquelle mit
    identischem `SeriesEnvelope`-Vertrag.
  - `weather-temp`: Primär Open-Meteo, Fallback Copernicus.
  - Für forecast-basierte Co-Location werden `pv` und `wind` je aktivierter
    lokaler Erzeugungsfamilie zusätzlich produktiv verpflichtend
    (je 1 Primär + 1 fallbackfähiger Adapter), sofern der Co-Location-Slice
    nicht ausdrücklich im `quality_mode=degraded_ok`-Übergangspfad bleibt.

Aktivierungslogik:
- `SOURCE_EMPTY` folgt ausschließlich der kanonischen Regel oben: nur ein
  erfolgreicher kompatibler Fallback kann die leere Primärantwort retten; ohne
  Fallback bleibt sie `SOURCE_REJECTED` und wird nie nur wegen
  `quality_mode=degraded_ok` akzeptiert.
- Primärquelle wird standardmäßig genutzt.
- Bei Ausfall oder Qualitätsfehlern der Primärquelle wird nur auf eine explizit als
  `fallback` konfigurierte, kompatible Quelle gewechselt.
- Qualitäts- und Fallback-Entscheide inkl. `SOURCE_*`-Matrix, `quality_mode`,
  `fallback`-Kompatibilität, harte Ablehnungen und `status_flags` sind
  verbindlich im Abschnitt
  [Qualitätsentscheidungen bei `SOURCE_*` (Ablauf)](#qualitätsentscheidungen-bei-source_-ablauf)
  festgelegt.
- Diese Phase beschreibt nur den Source-Auswahl- und Aktivierungspfad; die
  Status-/Fallback-Matrix wird nicht hier dupliziert.
- Ohne genehmigte Fallback-Quelle bleibt der produktive Pfad hart blockiert (kein
  Übergang über implizite Qualitätsdegradation).
- Vollaktivierung eines Forecast-/Adapterpfads ist erst zulässig, wenn alle DoD-
  Items dieses Plans erfüllt sind und mindestens ein produktiver Refresh-Zyklus
  je aktivierter Serie ohne `SOURCE_REJECTED` abgeschlossen wurde.
- Neue Betriebslogik darf nicht ohne definierte `series_status`-Entscheidung in
  Markt- oder Optimierungsworkflows starten.
- Nicht adapter-getragene Serien im Altpfad nutzen ausschließlich den
  Transitional-Input-Vertrag dieses Plans. Dieser Altpfad ist kein
  `quality_mode=strict`-Pfad und muss spätestens mit produktiver F-MKT-02-
  Aktivierung abgelöst oder als eigener Legacy-Sunset-Slice geführt werden.

Zusätzlich:

- Falls EPEX Lizenz nicht geklärt ist, darf EPEX nicht als `primary` markiert
  werden. In diesem Fall startet Phase 2 mit OPSD/Replay-konformer Datenquelle
  als temporärer `primary`; die EPEX-Aktivierung bleibt ein Lizenz-Runbook- und
  Cutover-Schritt. Der Begriff `fallback` bleibt ausschließlich Quellen
  vorbehalten, die hinter einer aktiven Primärquelle als Ersatzpfad konfiguriert
  sind.

### Phase 3: Forecast-Sidecar-Input

Für Forecasting außerhalb des EMS definiert dieser Slice den
Input-/Output-Vertrag für einen Forecast-Sidecar. Für EMS-internen Verbrauch
bleibt `ForecastSeries` in diesem Slice ein Contract-/DTO-Typ, kein produktiver
Domain-Typ für Optimierungsentscheidungen:

- Input:
  - Preis-Historie
  - Load forecast
  - Wind forecast
  - Solar forecast
  - Wetter
  - Kalenderfeatures
- Output:
  - `PriceSeries` für Punktforecast
  - `ForecastSeries`-Contract-Schema für deterministische Nicht-Preis-Forecasts
    (Load/PV/Wind/Wetter)
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

## Liefergegenstände bei Aktivierung

1. Folge-ADR oder Architektur-Schärfung für externe Datenquellen.
2. Adapter-Port für Preis-/Forecast-Quellen oder Erweiterung des
   bestehenden `IPriceSeriesSource`-Umfelds; Forecast-Ergebnisse bleiben in diesem
   Slice Contract-/DTO-Daten. Konsumierende Slices wie Co-Location projizieren
   sie deterministisch in eigene Domaintypen (`LocalGenerationSeries`), bis ein
   produktiver Forecast-Domaintyp aktiviert wird.
3. Abgeschlossene Schema-/Store-Migration aus
   [`Domain-Migration PriceSeries.Identity`](plan-domain-migration-price-series-identity.md);
   dieser Adapter-Slice konsumiert deren Serienidentitäts-, Store- und
   Mapper-Vertrag nur als Voraussetzung.
4. Quellenstatusmodell inklusive Refresh-/Fehlerstatus.
5. Mindestens ein Adapter mit Mock-/Replay-fähigem Testpfad.
6. API- oder Operator-UI-Status für Datenquellen.
7. Tests:
   - erfolgreiche Serienladung
   - fehlende Credentials
   - identische kanonische Serien-Signatur plus `series_version` mehrfach laden
     mit identischem Payload bleibt idempotent
   - identische kanonische Serien-Signatur plus `series_version` mit anderem
     `value_hash` führt deterministisch zu `SOURCE_REJECTED`
   - `value_hash`-Kanonisierung verwendet die Eingabemenge, Sortier-/
     Normalisierungsregel und `sha256(canonical_bytes)` aus
     [Kanonische `value_hash`-Berechnung](#kanonische-value_hash-berechnung-verbindlich).
   - gleiche `series_id`/`series_version` mit anderem `provider_id` wird als
     separater Provider-Kontext innerhalb der kanonischen Signatur bewertet
   - Primary-Validierung gegen Secondary-Adapter bei Primary-Ausfall
   - Rate-Limit-/Providerfehler
   - stale Daten werden je nach `quality_mode` korrekt geführt (`SOURCE_DEGRADED` vs `SOURCE_REJECTED`)
   - Zeitzone/DST bleibt konsistent
   - `valid_from_utc`/`valid_to_utc` werden, falls geliefert, als reine
     Audit-Metadaten persistiert bzw. ausgegeben und verändern weder Mapper-
     Idempotenz noch `value_hash`.
   - UTC-Zeitachse, Schrittweite und Lückenbehandlung (99,5 % Mindestabdeckung)
   - Family-Cutover Dual-Active, harter Familienwechsel ohne Alias
     (`SOURCE_REJECTED`) und Rollback-Sichtbarkeit
   - Optimierung bekommt exakt die erwartete Schrittzahl
   - operatorfähige Laufcodes werden bei Fehlerpfaden gesetzt (`SOURCE_*`)
   - `value_hash` ist in produktiven Phase-1/2-Re-Load-Pfaden vorhanden und
     wird bei fehlendem Wert außerhalb expliziter Einmalimporte hart
     abgewiesen.
8. Runbook für Credentials, Rate Limits, Provider-Ausfall und Cutover-Marker
   (`release_block`, `controlled_switchover` inkl. Ablageort und Verantwortlichkeit);
   Runbook-Overrides für Early-Cutover müssen als Audit-Marker roundtrippen,
   damit kein impliziter Code-Default entsteht.

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
  - der Pre-Slice
    [`Domain-Migration PriceSeries.Identity`](plan-domain-migration-price-series-identity.md)
    ist abgeschlossen.
  - Der Identity-Pre-Slice deckt `PriceSeries`, Store-Key, Import-Request-
    Mapping und dauerhafte Store-Pfade ab; reine Adapter-Vertragsänderungen
    reichen nicht.
  - ohne diese Erweiterung bleibt eine produktive Aufnahme neuer Serienkennungen im Slice gesperrt (Fallback-Pfad definiert).
  - Produktive Familie-/Versionswechsel sind nur per dokumentiertem Cutover erlaubt; ein stiller `series_version_family`- oder Revisionsfamilienwechsel ohne Release-Freigabe führt zu harter Ablehnung.
- [ ] Import-Adapter sind bereit für produktive Nutzung:
  - je produktiv aktivierter Serie bzw. Feature-Familie mindestens zwei Adapter
    (1 Primär + 1 fallbackfähiger Adapter): Preis und Forecast,
  - je Typ klar definierte Qualitäts- und Fallback-Pfade inkl. `quality_mode`.
  - klare Refresh-/Staleness-/Rate-Limit-Politik.
- [ ] Fallback-/Degradationslogik ist deterministisch umgesetzt:
  - harte Fehler (`SOURCE_REJECTED`) vs. degradierte Nutzung (`SOURCE_DEGRADED`) klar getrennt,
  - kombinierte Qualitätsereignisse via `status_flags`,
  - `SOURCE_EMPTY` bleibt ohne erfolgreichen kompatiblen Fallback hart
    abweisend; `quality_mode=degraded_ok` allein darf keine leere Serie
    akzeptieren.
- [ ] Versions- und Idempotenzregeln umgesetzt:
  - eindeutiger Revisionsvergleich pro `(series_id, series_version, source.provider_id)`,
  - `value_hash` ist für produktive Phase-1/2-Adapter vorhanden, außer bei
    explizit deklarierten Einmalimporten ohne Re-Load-Vertrag,
  - payload-identisches Re-Load ist idempotent,
  - Provider-Kontexte trennscharf unabhängig von identischem `series_id`/`series_version`.
  - kontrollierter Cutover/Rollback für `series_version_family` ist dokumentiert und mit den obigen Family-Tests verifiziert.
- [ ] Liefergegenstände bei Aktivierung umgesetzt:
  - ADR/Architektur-Spezifikation,
  - Source-Port- und Statusmodell,
  - Replay-/Mock-Testpfad,
  - API-/Operator-Status,
  - Runbook (Credentials, Limits, Ausfälle). Das Runbook übernimmt den
    14-Kalendertage-/zwei-Release-Fenster-Cutover-Default aus diesem Plan und
    definiert nur Override-Verfahren, Berechtigung und Auditablage; es darf
    keinen abweichenden stillen Default einführen.
- [ ] Akzeptanzkriterien aus diesem Dokument vollständig erfüllt.

---

## Abschlussentscheidungen

- Wird in einem ersten produktiven Slice zusätzlich `ForecastSeries`-Schema eingeführt?
  - Ja, aber nur als deterministisches Point-Forecast-Contract-Schema an der
    Application-/Sidecar-Grenze. Es ist in diesem Slice kein produktiver
    EMS-Domaintyp für Optimierungsentscheidungen; Quantile, Szenariopfade und die
    produktive Domain-Aktivierung bleiben Folgeslices.
- Sollen Forecast-Szenarien/Quantile direkt modelliert oder erst in einem
  separaten probabilistischen Optimierungsslice behandelt werden?
  - Entscheidung: Erst im ersten Slice sind nur point-forecast-Pfade relevant; Quantile/Probabilistik geht in ein Folge-Slice.
- Wo lebt langfristiger Quellen-Cache: In-Memory, Postgres/Timescale oder
  externer Datenservice?
  - Entscheidung für produktiv: bestehende Persistenz/Store des Kernsystems wird genutzt, ergänzt durch optionales dediziertes Forecast-Cache-Modul im zweiten Phase.
