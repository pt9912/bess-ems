# Plan: Co-Location- und Hybrid-BESS-Modell

**Dokumenttyp:** MVP-Spec / offen
**Status:** Open - wartet auf Produkt-/Standorttrigger
**Datum:** 2026-05-24
**Quelle:** [`note-market-and-colocation-followups.md`](note-market-and-colocation-followups.md)
**Bezug:**
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md),
[`../done/plan-RM-M4.md`](../done/plan-RM-M4.md),
[`../done/plan-RM-M5.md`](../done/plan-RM-M5.md)

---

## Ziel

`bess-ems` soll Standalone- und Co-Located-Batteriespeicher fachlich
unterscheiden können, ohne die bestehende Safety-First-Regelpipeline
zu ändern.

Der Slice führt ein explizites Markt-/Standortmodell für Co-Location
ein und erweitert die Optimierungsinputs um lokale Erzeugung,
Netzanschlussgrenzen und Vermarktungsrestriktionen.

Der technische Regelkreis bleibt unverändert:

```text
Optimierung -> Fahrplan -> State Machine -> Prioritäten -> Limiter -> Command
```

---

## Arbeitsmodell

### Architektur- und Solver-Scope für LP/MILP

Der operative Optimierungsrequest enthält genau eine Solver-Scope-Ausprägung.
`solver_scope` wird Bestandteil der bestehenden `ScheduleOptimizationRequest`-
Erweiterung bzw. des daraus erzeugten Optimization-Core-Requests, nicht von
`OptimizationRun`; der Run persistiert nur Ergebnisstatus, Terminierung und
`CanExecute`. Für Audit/Replays ist der immutable Request-Snapshot die kanonische
Ablage für den gewählten `solver_scope` (`LP`/`MILP`) und eine mögliche
Partitionierungsentscheidung. Produktive Replays ohne Request-Snapshot sind
vorerst nicht freigegeben; falls sie später erlaubt werden sollen, ist vorher
ein eigener Pre-Slice `OptimizationRun.SolverScopeAudit` erforderlich.
`TerminationDetail` ist nicht die kanonische Ablage für diesen Scope.

Entscheidungsreihenfolge je Request:

1. Wenn **keine** aktivierte Co-Location-/Herkunftsrestriktion betroffen ist => `solver_scope=LP`.
2. Sonst (produktiver MVP mit Co-Location-/Herkunftsrestriktion) => `solver_scope=MILP`,
   außer ein ADR hat den unten beschriebenen LP-Sonderfall explizit freigegeben.
3. Gemischte Requests aus LP- und MILP-relevanten Assets dürfen nur dann partitioniert werden,
   wenn ein Folge-ADR für homogene Solver-Scope-Partitionierung explizit aktiviert ist und
   die Partitionierung vollständig isolierbar sowie deterministisch reaggregierbar ist.

Die Umschaltung erfolgt per Request und ist nicht global. Ein Request mit mindestens einem betroffenen
Asset (`ClassicalCoLocation`, `HybridWithGridImport`, `GreenStorageRestricted`
im Validierungsmodus oder aktivierter `OriginConstraint`)
wird standardmäßig als `MILP` ausgeführt.
`GreenStorageRestricted` bleibt explizit außerhalb des produktiven Routine-Betriebs:
- im produktiven Scope ist `GreenStorageRestricted` nur mit explizitem
  `green_storage_validation_mode=true` zulässig;
- bei `green_storage_validation_mode=true` gilt der Request explizit als
  MILP-relevant;
- bei produktivem Routine-Run ohne dieses Flag gilt hart `CONFIG_INVALID`
  (`GREEN_STORAGE_RESTRICTED_PRODUCTIVE_BLOCKED`).

Kompatibilitätsprinzip:

- Bestehende Standalone-only-Requests bleiben LP.
- Bei nicht kompatibler `solver_scope`-Zuordnung endet der Lauf mit `CONFIG_INVALID`, nicht mit stiller Downgrade-Logik.
- Für gemischte Multi-Site-/Multi-Asset-Fälle gilt:
  - deterministische Default-Regel: vollständiger Request wird als MILP-Request ausgeführt.
  - optional (explizit freigeschalteter ADR-Pfad): Request-Partitionierung in homogene Solver-Scope-Gruppen vor dem Optimierer.
- Der Scope bleibt pro Request eindeutig; innerhalb einer einzelnen Anfrage gibt es keinen partiellen LP/MILP-Mix.
- Für den ersten produktiven Slice ist die Partitionierungsentscheidung im ADR festzuhalten.
- Partitionierung ist nur zulässig, wenn der Request-Builder alle Unterrequests vollständig
  isoliert und im Anschluss deterministisch reaggregiert. Andernfalls ist bei gemischten
  Scope-Anforderungen die fachliche Ursache `CONFIG_INCONSISTENT` zu setzen; das
  konkrete `TerminationCode`-/`CanExecute`-Mapping bleibt der gemeinsamen Matrix
  vorbehalten.

Semantik der fachlichen Coderäume (für deterministische Fehlerauswertung):

- `CONFIG_INVALID`: globale Konfiguration/Request-Kontraktfehler (z. B. ungültige Kombinationen
  der Solver-Scope-Aktivierungen, produktiv gesperrte Modi oder fehlende Muss-Felder auf
  dem globalen Solver-Scope). Standortbezogene Migrations-/Ableitungsfälle wie fehlende
  Sign-Konventionen und standortbezogene Aktivitäts-/Muss-Feld-Lücken wie
  `can_dispatch` werden nicht hier, sondern als `CONFIG_INCONSISTENT`
  klassifiziert.
- `CONFIG_INCONSISTENT`: fachlich konsistenzrelevante, aber konfigurationsnahe Inkonsistenzen
  im Request (z. B. nicht auflösbare/inhomogene Modellierung über aktivierte Co-Location-Modi
  oder nicht deterministisch ableitbare Sign-Konvention).
- `SCHEMA_INCONSISTENT`: reine Schemata- bzw. Datenvertragsverletzungen (z. B. unzulässiger
  Datensatzzustand, inkonsistente Zeitachsen oder unerlaubte Vorverarbeitungszustände).

### Run- und Fehlerkodierung (Kompatibilität zum bestehenden Solver-Laufmodell)

Die Co-Location-/Migrationserkennung verwendet die fachlichen Coderäume `CONFIG_*`,
`SCHEMA_INCONSISTENT` und `MODEL_INFEASIBLE`. Die Persistenz auf
`OptimizationRun` erfolgt ausschließlich über den gemeinsamen Ausführungs-/
Fehlervertrag in
[`plan-ler-fcr-reserve-robustness.md`](plan-ler-fcr-reserve-robustness.md).
Dieser Plan definiert nur die Co-Location-spezifischen Ursachen und Beispiele;
die autoritative Matrix aus `OptimizationSolverStatus`, `TerminationCode` und
`CanExecute` wird hier nicht gespiegelt.

Alle fachlichen Gründe werden zusätzlich in `TerminationDetail` geführt, damit
Operator-/Replay-Pfade die Unterscheidung ohne Hilfskontext nachziehen können.
Neue Co-Location-Details verwenden den gemeinsamen Detailvertrag
`format=kv1;reason=<GROUND_CODE>` aus
[`plan-domain-migration-optimization-run-can-execute.md`](plan-domain-migration-optimization-run-can-execute.md),
z. B. `format=kv1;reason=SITE_GRID_POWER_SIGN_MISSING` oder
`format=kv1;reason=SITE_CAN_DISPATCH_MISSING`.

### Projekt-/Standorttypen

Der Slice modelliert mindestens diese Betriebsarten:

- `StandaloneBess`
  - Batterie handelt unabhängig am Markt.
  - Netzbezug und Netzeinspeisung sind technisch und marktlich erlaubt,
    soweit Asset- und Netzlimits eingehalten werden.

- `ClassicalCoLocation`
  - Batterie sitzt hinter einem gemeinsamen Netzanschlusspunkt mit
    PV/Wind.
  - Lokale Erzeugung, Einspeiselimit und Netzanschlussleistung begrenzen
    den Gesamtfahrplan.

- `HybridWithGridImport`
  - Batterie darf lokale Erzeugung und Netzbezug kombinieren.
  - Optimierung muss Herkunfts-/Kostenunterschiede transparent halten.

- `GreenStorageRestricted`
  - Batterie darf nur unter definierten Herkunfts- oder Förderregeln
    laden/entladen.
  - Dieser Modus ist zunächst Modell- und Validierungs-Scope; produktive
    Förderlogik braucht eigene Rechts-/Compliance-Freigabe.
  - Validierungsmodus wird strukturell durch
    `green_storage_validation_mode=true` im Optimierungsrequest markiert. Ohne
    dieses Flag ist der Modus im produktiven Routinepfad blockiert.
  - Validierungsmodus bedeutet vollständige Solver-Formulierung mit harten
    Herkunfts-/Netzbezugs-Constraints, aber ohne automatische produktive
    Förder-/Marktentscheidung im Regelkreis.

### Neue fachliche Konzepte

Mögliche Domain-/Application-Erweiterungen:

- `SiteConstraint`
  - `site_id`
  - `max_import_kw`
  - `max_export_kw`
  - optional `grid_connection_power_kw`
  - `can_dispatch` (required bei aktiver Request-Beteiligung im produktiven Scope)
    - Semantik: `true` = aktive Teilnahme am Request, `false` = bewusst ausgeschaltet.
    - Produktiver Scope:
      - `true`/`false` ist bindend.
      - Bei expliziter Site-Beteiligung in einem produktiven Co-Location-Request muss `can_dispatch=true` gelten.
      - Bei fehlendem `can_dispatch` wird der betroffene Site-Scope im produktiven Lauf als Konfigurationsfehler abgelehnt:
        `CONFIG_INCONSISTENT` mit `SITE_CAN_DISPATCH_MISSING`.
    - Legacy-Migrationsfenster:
      - Im Migrations-Dry-Run darf das Feld als `can_dispatch=false` modelliert werden,
        aber nur mit explizitem Migrations-Audit-Flag und sichtbar im Audit-Report.
    - Fehlendes `can_dispatch` wird nach Modus eindeutig behandelt; diese Matrix
      ist die kanonische Aktivitäts-/Migrationsmatrix für `can_dispatch`.
      `migration_dry_run=true` überschreibt `migration_strict` ausschließlich für
      Run-Blockaden und erzeugt stattdessen ein Audit-Bundle.
      | Modus | Site im aktuellen Request aktiv? | Ergebnis |
      | --- | --- | --- |
      | `migration_dry_run=true` | ja oder unklar | keine Laufblockade; Audit-Eintrag `SITE_CAN_DISPATCH_MISSING`, Site gilt nicht als aktiv ausführbar |
      | `migration_strict=true` produktiv | ja oder unklar | harte Blockade mit `CONFIG_INCONSISTENT` / `SITE_CAN_DISPATCH_MISSING` |
      | `migration_strict=true` produktiv | nein | harte Blockade bis explizit `can_dispatch=false` modelliert oder im Dry-Run auditierbar bereinigt |
      | `migration_strict=false` produktiv | ja | harte Blockade mit `CONFIG_INCONSISTENT` / `SITE_CAN_DISPATCH_MISSING` |
      | `migration_strict=false` produktiv | nein | keine Laufblockade; Datensatz bleibt im Audit-Bundle bis zur Bereinigung |
      Der `migration_strict`-Schalter wirkt damit ausschließlich für nicht aktive
      Sites; aktive Sites bleiben in beiden Produktivmodi hart blockiert.
    - Produktiver Zugriff außerhalb eines Migrationsfensters:
      - `can_dispatch` muss gesetzt sein.
      - `can_dispatch=false` markiert die Site als inaktiv; inaktive Sites werden bei aktiven Requests nicht ausgewertet.
      - `can_dispatch=false` ohne aktive Request-Beteiligung ist zulässig und blockiert nicht per se produktive Co-Location-Runs anderer aktiver Sites.
      - `can_dispatch=true` und `site_grid_power_sign` nicht gesetzt bleibt weiterhin hart blockiert (`CONFIG_INCONSISTENT`).
  - `site_grid_power_sign` (verpflichtend): `export_pos` oder `import_pos`
    - `export_pos`: positive Leistung bedeutet Export ans Netz
    - `import_pos`: positive Leistung bedeutet Import aus dem Netz
- `LocalGenerationSeries`
  - Zeitreihe für PV/Wind-Erzeugung oder Forecast.
  - Produktive `LocalGenerationSeries`-Eingänge laufen über
    `IForecastSeriesSource`/`SeriesEnvelope` aus
    [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md) und tragen
    `series_status`, `source_eval_status` und `status_flags`.
  - Nur explizite Validierungs-/Migrationspfade dürfen den Adapterpfad umgehen; dann
    muss der Lauf als nicht-produktiver Validierungslauf auditierbar sein.
  - Pflichtfelder:
    - `site_id`
      - bei produktiver Co-Location-Nutzung muss die `site_id` eindeutig einem
        `SiteConstraint` zugeordnet sein; andernfalls `SCHEMA_INCONSISTENT`.
    - `timestamp_utc`
    - `resolution_minutes`
  - Die normative Definition der Alignment-Felder
    (`alignment_mode`, `alignment_prepared`, `alignment_prepared_by`,
    `alignment_prepared_horizon_start_utc`, `alignment_prepared_horizon_end_utc`)
    lebt ausschließlich in
    [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md). Dieser
    Plan beschreibt nur die Co-Location-Verbrauchsregeln und dupliziert keine
    Feldsemantik.
  - Weitere Pflichtfelder:
    - `value_kw`
    - `value_type` (`forecast` | `actual`)
    - `source_metadata.provider_id`
    - `series_product`
    - `unit` (z. B. `kW`)
    - `source_metadata.retrieved_at_utc`
    - `series_version`
  - Validierung:
    - gleiche Zeitachse wie `PriceSeries` (UTC, step-genau, gleiche Horizon-Länge) bei produktiver Nutzung.
      Abweichende Quellauflösungen sind im produktiven Co-Location-Pfad nur
      zulässig, wenn der externe Provider bereits in der Zielauflösung liefert
      oder ein eigener, versionierter Preprocessing-Slice einen
      `alignment_mode=resample`-Pfad mit deterministischer Aggregations-/
      Interpolationsregel freigibt. Dieser Plan definiert noch keinen
      produktiven Resampling-Pfad.
    - Für Alignment gilt ausschließlich der Vertrag aus
      [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md):
      produktiver Default ist `alignment_mode=reject`; `trim-to-common` ist nur
      über den dort spezifizierten versionierten Vorverarbeitungspfad zulässig.
      - Wenn die Vorverarbeitung Werte auffüllt oder schätzt, muss das
        resultierende `SeriesEnvelope` das Flag `SOURCE_BACKFILL` behalten. Solche
        Serien sind im produktiven `quality_mode=strict` nicht zulässig und dürfen
        nur mit explizit freigegebenem `quality_mode=degraded_ok` in Co-Location
        eingehen. Reines deterministisches Trimming ohne aufgefüllte Werte setzt
        kein `SOURCE_BACKFILL`-Flag.
    - `value_kw` ist eine Produktionsleistung und für diese Serie standardmäßig nicht negativ.
    - Negative Werte sind in `LocalGenerationSeries` nicht zulässig. Signierte
      Nettoeinspeise- oder Lastreihen müssen als eigene `ForecastSeries` über den
      Forecast-Adaptervertrag mit explizitem `series_product` und dokumentierter
      Vorzeichenkonvention modelliert werden.
    - Last-Forecasts sind keine `LocalGenerationSeries`; sie laufen als
      `ForecastSeries` mit `series_product=load` über den Forecast-Adaptervertrag.
    - Metadatenpflicht gemäß `SeriesEnvelope`: `source_metadata.provider_id`,
      `series_product`, `source_metadata.retrieved_at_utc`, `series_version`.

- `LocalOriginState`
  - virtuelle Zwischenbilanz für herkunftsgebundene Energie (kWh)
  - `e_local_t` je Zeitschritt
  - `eta_charge` (optional, default `1.0`)
  - `eta_discharge` (optional, default `1.0`)
  - Begrenzung via `local_origin_capacity_kwh`
  - Für `GreenStorageRestricted` muss `local_origin_capacity_kwh > 0` gelten.
    `local_origin_capacity_kwh=0` ist keine nutzbare Herkunftsbilanz und führt zu
    `CONFIG_INCONSISTENT` mit `format=kv1;reason=GREEN_STORAGE_ORIGIN_CAPACITY_ZERO`.
  - Validierung (nur wenn von `1.0` abweichend): `eta_min <= eta_charge <= 1`
    und `eta_min <= eta_discharge <= 1`; `eta_min = 1e-6` ist dieselbe
    gemeinsame Assetmodell-Invariante wie im Robustheitspfad. Die Umsetzung darf
    diesen Wert nicht planlokal duplizieren, sondern muss ihn aus einer zentralen
    Assetmodell-Konstante bzw. einem gemeinsamen Validierungshelfer beziehen.

- `CoLocationMode`
  - Betriebsart nach obigem Arbeitsmodell
  - `GreenStorageRestricted` ist Sonderfall im selben Co-Location-Kontext und nutzt den gleichen Basis-Constraint-Stack.

- `CurtailmentCost`
  - Strafkosten für Abregelung lokaler Erzeugung

- `OriginConstraint`
  - optionale Restriktion, ob Batterieenergie aus lokaler Erzeugung,
    Netzbezug oder beidem stammen darf

### Netzanschlusspunkt-Konvention (verbindlich)

Für jede Site gilt folgendes Basis-Modell (LP) mit separater MILP-Kontrolllogik für Richtungen:

- `b_t` = Batterieleistung (kW), Batterie-Vorzeichen bleibt unverändert:
  - `b_t > 0`: Entladen
  - `b_t < 0`: Laden
- `g_t` = lokale Erzeugung vor Abregelung (kW), standardmäßig positiv bei Einspeise-Richtung
- `c_t` = Abregelung der lokalen Erzeugung (kW), `0 <= c_t <= g_t`
- `p_grid_import_t`, `p_grid_export_t` = Leistung am Netzknoten (kW), beide `>= 0`
- `site_power_t` = Netzleistung nach Konvention (kW)
- `s_site` = Signierungsfaktor der Site-Konvention:
  - `s_site = +1`, wenn `site_grid_power_sign = export_pos`
  - `s_site = -1`, wenn `site_grid_power_sign = import_pos`
- `d_t` = Richtungsbinarvariable je Zeitschritt (`MILP`)

Gleichungen:

- `site_power_t = s_site * ((g_t - c_t) + b_t)`
- bei Konvention `site_grid_power_sign=export_pos` (`s_site=+1`): `site_power_t = p_grid_export_t - p_grid_import_t`
- bei Konvention `site_grid_power_sign=import_pos` (`s_site=-1`): `site_power_t = p_grid_import_t - p_grid_export_t`
- `p_grid_import_t <= site.max_import_kw`
- `p_grid_export_t <= site.max_export_kw`
- simultane Netznutzung ist ausgeschlossen:
  - Formulierung: `p_grid_import_t * p_grid_export_t = 0`
  - MILP-Linearisation (`d_t` ist Richtungsbit je Zeitschritt:
    - `d_t = 1` bedeutet Fluss in positiver Richtung der gewählten Site-Konvention (`site_power_t > 0`)
    - `d_t = 0` bedeutet Gegenrichtung):
    - bei Konvention `site_grid_power_sign=export_pos`:
      - `p_grid_import_t <= site.max_import_kw * (1 - d_t)`
      - `p_grid_export_t <= site.max_export_kw * d_t`
    - bei Konvention `site_grid_power_sign=import_pos`:
      - `p_grid_import_t <= site.max_import_kw * d_t`
      - `p_grid_export_t <= site.max_export_kw * (1 - d_t)`
    - `d_t ∈ {0,1}`
- `d_t` ist für beide Sign-Konventionen identisch als Richtungs-Binärvariable definiert;
  nur die `site_power_t`-Vorzeichenkonvention ändert die Bedeutung von `+`/`-`.
- optional, falls `grid_connection_power_kw` gesetzt:
  - `p_grid_import_t + p_grid_export_t <= site.grid_connection_power_kw`

Verbindliche Defaults (Phase-1-ADR):

- `site_grid_power_sign` ist ein Pflichtfeld für produktive `SiteConstraint`-Konfigurationen.
  - Bei einem produktiven Lauf ohne `site_grid_power_sign` endet der Lauf mit `CONFIG_INCONSISTENT`.
  - Bei vorhandenen Legacy-Daten ohne `site_grid_power_sign` ist ein strukturierter Migrationspfad zwingend:
    - Migrations-Release: bestehende `SiteConstraint`-Datensätze, deren Vorzeichenkonvention aus vorhandenen Feldern eindeutig bestimmt werden kann, werden deterministisch auf den abgeleiteten Wert normalisiert und mit `site_grid_power_sign_normalized=true` markiert.
    - Folge-Release: unmarkierte `site_grid_power_sign`-freie Datensätze werden als `CONFIG_INCONSISTENT` abgewiesen.
    - Nach der Migrationsphase ist `site_grid_power_sign` ohne Ausnahmekodex Pflichtfeld.
  - Es gilt kein impliziter Default auf `export_pos` oder `import_pos`.
  - Nicht-aktive Sites (`can_dispatch=false`) dürfen im `migration_strict=false`-Modus als betrieblich freigestellt betrachtet werden, bis der Betreiber die Sign-Konvention aktiv bestätigt.
  - `max_import_kw` und `max_export_kw` sind harte Pflichtfelder (`>= 0`) je Site.
- `grid_connection_power_kw` ist optional; wenn nicht gesetzt, ist nur die Einzelgrenze über `max_import_kw`/`max_export_kw` aktiv.
- `d_t` ist Richtungs-Binärvariable entsprechend der Site-Konvention; für beide
  Sign-Konventionen identisch definiert, nur die Vorzeichenzuordnung in
  `site_power_t` wird umgeschaltet.
- Die Ziellogik ist zeitscheibenweise identisch in beiden Konventionen; es wird ausschließlich die Vorzeichenzuordnung von `site_power_t` gewechselt.

Interpretation:

- `b_t` und `(g_t - c_t)` wirken auf denselben Punkt.
- Die Export-/Importgrenzen gelten explizit für jede Zeitscheibe.
- Die Kombination aus Import/Export- und Gesamtanschlussgrenze vermeidet Simultanfehler bei der Berechnung.
 - Für den produktiven MVP gilt als konservativer ADR-Default:
   `CoLocationMode != StandaloneBess` => `solver_scope=MILP`.
   Hintergrund: Der Import/Export-Mutex (`p_grid_import_t * p_grid_export_t = 0`) ist
   bei jeder Site mit `max_import_kw > 0` und `max_export_kw > 0` MILP-gebunden und
   wird über Richtungs-Binärisierung erzwungen.
   Eine LP-Ausnahme darf ein ADR nur für konkret belegte Sonderfälle freigeben, in denen
   der Mutex strukturell entfällt (z. B. `max_import_kw=0` oder `max_export_kw=0`) und
   keine Herkunfts-/Richtungsbinarisierung aktiv ist.

### Legacy-Daten-Migrationslauf und Backout (verbindlich)

Für produktive Freischaltung ist ein deterministischer Migrationspfad für bestehende
`SiteConstraint`-Datensätze vorab definiert:

`migration_strict` steuert ausschließlich die Toleranz für Co-Location-
Konfigurationsmigrationen; `quality_mode` steuert ausschließlich
Preis-/Forecast-Qualität. Beide Schalter bleiben getrennt und müssen in
Operator-/Runbook-Sichten nebeneinander ausgewiesen werden.

- Migrationsfenster ist zweistufig:
  - `migration_dry_run=true`: vollständige Klassifikation mit vollständigem Audit-Bundle, aber ohne Laufblockade.
  - `migration_dry_run=false` (Standard für produktive Freischaltung): keine Änderung der Runtime-Lauflogik, nur deterministische Ergebnisklassen.
  - `migration_strict=true` (Standard): unklare oder inkompatible Datensätze werden in produktiven Co-Location-Requests blockiert.
  - `migration_strict=false` (nur mit explizitem Release-Governance): erlaubt die Koexistenz bis zur Bereinigung nicht-aktiver Sites; aktive Sites bleiben weiterhin blockiert.
  - Aktivitätsdeterministik:
    - Die kanonische Bewertung fehlender `can_dispatch`-Werte steht in der
      Matrix im Abschnitt `SiteConstraint`.
    - `migration_dry_run=true` blockiert keinen Lauf, unabhängig vom Wert von
      `migration_strict`; produktive Läufe folgen der Matrix.
    - Nicht aktive Sites werden explizit mit `can_dispatch=false` markiert oder
      im Migrationsaudit ohne harte Blockade geführt, bis sie aktiv in Betrieb
      genommen werden.
- Migrationsfenster (vor Aktivierung des Co-Location-MVP, nicht inline im operativen Lauf).
- Kandidatenklassifikation je Datensatz:
  - `normalized`: `site_grid_power_sign` ist gesetzt oder eindeutig aus vorhandenen
    Feldern deterministisch ableitbar.
  - `repairable`: Signatur kann aus deterministischen und konsistenten
    Legacy-Metadaten eindeutig und deterministisch ermittelt werden (`normalized=false`, aber ableitbar).
  - `unclear`: keine sichere Ableitung, keine gültige Signatur.
  - `incompatible`: harte Daten- oder Grenzverletzung (z. B. negative Limits, fehlende
    Pflichtwerte, inkonsistente Grenzdefinitionen).
- Deterministische Ableitungsregeln für `repairable`:
  - Die folgende Aliasliste ist ein Migrationskandidat und vor Slice-Aktivierung
    gegen das reale Legacy-Schema zu verifizieren. Nicht vorhandene Felder werden
    aus Fixtures und Migrationslogik entfernt; neue Felder dürfen nur mit
    dokumentierter Schema-Herkunft ergänzt werden.
  - Es werden nur explizite Aliasfelder ausgewertet, die nach dieser Verifikation
    in historischen Datensätzen vorhanden und eindeutig gesetzt sind:
    - `legacy_grid_power_sign`
    - `legacy_site_grid_power_sign`
    - `grid_power_sign`
    - `grid_connection_direction`
    - `site_network_power_sign`
    - `site_grid_power_sign_raw`
  - Die Werte werden auf die Zielwerte normalisiert:
    - `export_pos` oder `import_pos` (case-insensitiv akzeptiert; alle anderen Werte
      sind inkonsistent).
  - Mindestens ein Aliasfeld muss gültig gesetzt sein.
  - Sind mehrere Aliasfelder gesetzt, gilt:
    - sind die normalisierten Werte konsistent, ist der Datensatz `repairable` (`site_grid_power_sign` kann deterministisch gesetzt werden).
    - sind die normalisierten Werte widersprüchlich, wird der Datensatz als `unclear` eingestuft
      (`SITE_GRID_POWER_SIGN_MISSING`).
- Bei `repairable` Datensätzen wird automatisch eine Normalisierung durchgeführt:
  - `site_grid_power_sign_normalized=true`
  - `site_grid_power_sign_normalized_from=<ableitungsregel>`
  - `site_grid_power_sign_normalized_at=<timestamp>`
- `unclear` Datensätze müssen mit `CONFIG_INCONSISTENT` abgewiesen werden und enthalten
  einen operatorfähigen Grundcode (z. B. `SITE_GRID_POWER_SIGN_MISSING`).
- `incompatible` Datensätze werden mit `SCHEMA_INCONSISTENT` und spezifischem
  `limiting_reason` abgelehnt.
- Unklare Datensätze dürfen nicht implizit in einen Default übernommen werden.
- Bei `migration_strict=false` gilt:
  - `unclear`-/`incompatible`-Datensätze blockieren produktive Runs weiterhin nur, wenn sie im aktuellen aktiven Co-Location-Request-Scope liegen.
  - `unclear`-/`incompatible`-Datensätze mit `can_dispatch=false` werden im Audit-Bundle erfasst, aber operativ nicht hart blockiert.
- Alle Migrationsergebnisse sind in einem Audit-Bundle je Lauf zusammengefasst und
  für Operator-Replay verfügbar.

Rollback-/Backout-Verhalten:

- Bei `migration_strict=true` ist im produktiven Rollout der Slice bei offenem Anteil
  `unclear`/`incompatible` nicht freizuschalten.
- Für Ausnahmeszenarien kann ein Release-Block oder eine kontrollierte Suspendierung gelten:
  - Release-Block: Freigabe bis Daten bereinigt sind.
  - Notfallmodus: optionaler Operator-Override nur für nicht-aktive Sites im Rahmen einer
    dokumentierten Wartungsfreigabe.
  - Nicht-aktive Sites sind im Notfallmodus mit `can_dispatch=false` explizit zu markieren;
    aktive Sites bleiben weiterhin hart blockiert.
- Keine stillschweigende „Best-Effort“-Auto-Ableitung in produktivem Requestbetrieb.

### GreenStorageRestricted-Regeln (MVP)

`GreenStorageRestricted` ist im ersten Umsetzungsumfang als harte Validierung spezifiziert:

- Erlaubte Ladequelle: ausschließlich lokale Erzeugung (`p_grid_import_t == 0`).
- Einführung der herkunftsbezogenen Zwischengröße `e_local_t` (kWh), mit
  `Δt = resolution_minutes / 60`:
  - `e_local_{t+1} = e_local_t + eta_charge * max(0, -b_t) * Δt - max(0, b_t) * Δt / eta_discharge`
    (bei Vorzeichenkonvention `b_t>0` Entladen, `b_t<0` Laden; für `eta_charge = eta_discharge = 1` vereinfacht sich dies zu `e_local_t - b_t * Δt`)
  - `0 <= e_local_t <= local_origin_capacity_kwh`
  - `local_origin_capacity_kwh > 0`; `0` ist `CONFIG_INCONSISTENT` mit
    `format=kv1;reason=GREEN_STORAGE_ORIGIN_CAPACITY_ZERO`.
  - `e_local_0` ist konfigurierbar (meist 0).
- Harte Koppelregeln:
  - `p_grid_import_t == 0` (für beide Site-Konventionen) – vollständiges Netzbezugsverbot.
  - `-b_t <= (g_t - c_t)` (Laden nur solange lokaler Überschuss vorliegt)
  - `b_t <= e_local_t * eta_discharge / Δt` (Entladen nur aus vorhandener lokaler Herkunftsmasse)
  - Ist kein Herkunftsnachweis oder keine ausreichende lokale Quelle vorhanden, gilt `CONFIG_INCONSISTENT`.
- Abregelung `c_t` ist im Modus erlaubt und mindert damit lokal verfügbaren Strom.
- Bei fehlender Herkunftstransparenz darf kein produktiver Lauf gestartet werden.

Fehlerklassifikation für `GreenStorageRestricted`:

| Erkennungsschicht | Auslöser | Coderraum | Grundcode |
| --- | --- | --- | --- |
| Produktiv-Precheck | Produktiver Routine-Run ohne `green_storage_validation_mode=true` | `CONFIG_INVALID` | `GREEN_STORAGE_RESTRICTED_PRODUCTIVE_BLOCKED` |
| Input-/Pre-Solver-Validierung | Fehlender oder nicht auditierbarer Herkunftsnachweis | `CONFIG_INCONSISTENT` | `GREEN_STORAGE_ORIGIN_PROOF_MISSING` |
| Input-/Pre-Solver-Validierung | Request fordert Netzladung oder modelliert Netzbezug als zulässige Ladequelle | `CONFIG_INCONSISTENT` | `GREEN_STORAGE_GRID_CHARGE_BLOCKED` |
| Solver-Ergebnis | Harte Constraints (`p_grid_import_t == 0`, lokale Quelle, `e_local_t`) machen den angefragten Fahrplan mathematisch unerfüllbar | `MODEL_INFEASIBLE` | `GREEN_STORAGE_MODEL_INFEASIBLE` |

`GREEN_STORAGE_MODEL_INFEASIBLE` ist ein Domain-Grundcode im
`TerminationDetail` (`format=kv1;reason=GREEN_STORAGE_MODEL_INFEASIBLE`), kein
eigener `TerminationCode`. Das Run-Mapping nutzt die gemeinsame Matrix:
solver-seitige Infeasibility bleibt `OptimizationSolverStatus.Infeasible` mit
bestehendem Solver-`TerminationCode` (z. B. `or-tools-infeasible`).

### Optimierungswirkung

Der Horizon-Optimierer muss zusätzlich berücksichtigen können:

- Netzanschlusspunkt-Grenzen für Import und Export
- lokale Erzeugungsprognose pro Zeitschritt
- optionales Herkunftssourcing bei Lade-/Entladungspfaden (`OriginConstraint`)
- optionale Abregelung lokaler Erzeugung
- Batterie-Ladefenster aus lokalem Überschuss
- Reservebänder aus `ReserveBand`
- bestehende Day-Ahead-/Intraday-Fahrpläne
- Degradation- und SOC-Zielkosten wie heute

Die bestehende Vorzeichenkonvention bleibt unverändert:

- Batterie `> 0 kW` = Entladen
- Batterie `< 0 kW` = Laden

Für den Netzanschlusspunkt ist die Konvention in diesem Slice vollständig festgelegt.
`site_grid_power_sign` ist je Site explizit zu konfigurieren; `export_pos` oder `import_pos` sind beide zulässig.

---

## Nicht-Ziele

- Kein Ersatz für BMS-/PCS-Schutzfunktionen.
- Keine direkte Feldgeräte-Ansteuerung aus der Optimierung.
- Keine Zertifizierung oder Rechtsauslegung für EEG-/Fördermodelle.
- Kein automatischer externer Forecast-Abruf; das ist
  [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).
- Ein erster produktiver Co-Location-Slice kann mit lokalen Erzeugungs-/Lastreihen starten;
  forecast-basierte Optimierung bleibt bis zur Aktivierung von
  `plan-price-forecast-adapters.md` in einem expliziten
  `quality_mode=degraded_ok`-Übergangspfad.
- Übergangsadapter im `quality_mode=degraded_ok`-Übergangspfad:
  - Bis `plan-price-forecast-adapters.md` produktiv aktiviert ist, dürfen
    `LocalGenerationSeries` nur über einen expliziten manuellen Import-/API-Push
    oder CSV/Fixture-Import in einen nicht-forecastenden Übergangsadapter geladen
    werden.
  - Dieser Übergangsadapter darf keine automatischen externen Abrufe durchführen,
    muss `series_status=SOURCE_DEGRADED` und einen Audit-Hinweis
    `format=kv1;reason=LOCAL_GENERATION_TRANSITIONAL_INPUT` setzen und bleibt
    auf `quality_mode=degraded_ok` beschränkt.
  - Der Übergangsadapter ist mit Aktivierung von F-MKT-02 abzulösen oder als
    eigener Legacy-Sunset-Slice weiterzuführen.
- Keine Multi-Asset-Fleet-Optimierung über mehrere Standorte; das bleibt
  M6-Folgearbeit.

## LP-/MILP-Kompatibilitätsstrategie

Die Umstellung auf Co-Location-MVP ist feature-gesteuert und darf bestehende LP-basierte
Bestandsrouten nicht brechen:

- Default bleibt der bestehende LP-Standard für `StandaloneBess` und Szenarien ohne
  aktivierte Co-Location-/Herkunftsrestriktionen.
- `ClassicalCoLocation`, `HybridWithGridImport`, `GreenStorageRestricted` und damit verbundene
  Vorzeichen- und Herkunftsrestriktionen werden über explizit aktivierte
  `CoLocationMode`-/Modellschalter auf MILP-Profil gestellt.
- Für gemischte Szenarien ist der komplette Request auf MILP zu setzen oder
  vor dem Solver fachlich zu partitionieren; ein partieller Scope-Mix
  innerhalb eines Requests ist nicht erlaubt. Default bleibt dabei ein
  einheitlicher MILP-Pfad.
- Regressionstest ist als Matrix verbindlich:
  - LP-basierter Standalone-Fall bleibt bit-kompatibel.
  - Co-Location mit aktivem Import/Export-/Herkunftsmodell muss auf MILP laufen.
  - `quality_mode=degraded_ok`-Übergangsfälle im `GreenStorageRestricted`
    folgen demselben LP/MILP-Wechselpfad.

---

## Liefergegenstände bei Aktivierung

1. Folge-ADR oder ADR-Schärfung für Co-Location-Modell und
   Netzanschlusspunkt-Vorzeichen.
2. Domain-Modelle für Standorttyp, Netzanschlussgrenzen und lokale
   Erzeugungs-/Forecast-Zeitreihen inkl. verbindlicher Site-Netzflussdefinition.
3. Application-Port-Erweiterung für Optimierungsrequests.
4. OR-Tools-Modellerweiterung oder neuer Solver-Pfad für
   Co-Location-Constraints.
5. Tests:
   - Kein simultaner Netzimport/Netzeinspeisung im gleichen Zeitschritt.
   - Standalone bleibt bit-kompatibel zum heutigen Pfad.
   - PV-Überschuss kann Batterie laden, ohne Exportlimit zu verletzen.
   - `site_grid_power_sign=import_pos`: Vorzeichen-/Grenzlogik bleibt konsistent
     zur Export-Variante bei identischem physischem Leistungsfluss.
   - Netzexportlimit begrenzt Batterieentladung plus lokale Erzeugung.
   - `GreenStorageRestricted`: `e_local_0`-Randfall (`0` und >0) und
     `local_origin_capacity_kwh`-Randfall (`0`, Minimalreserve) werden explizit geprüft;
     `local_origin_capacity_kwh=0` blockiert mit `CONFIG_INCONSISTENT` und
     `format=kv1;reason=GREEN_STORAGE_ORIGIN_CAPACITY_ZERO`.
   - `GreenStorageRestricted`: Lauf wird mit `CONFIG_INCONSISTENT` und
     `GREEN_STORAGE_GRID_CHARGE_BLOCKED` geblockt, wenn eine Netzladung
     (`p_grid_import_t > 0`) versucht wird.
   - Co-Location-Konfigurations- und Schemafehler setzen `CanExecute=false`
     gemäß Pre-Slice-Matrix.
   - Reservebänder reduzieren weiterhin verfügbare Lade-/Entladeleistung.
   - `migration_strict=false` erlaubt nicht-aktive `can_dispatch=false`-Sites mit
     `unclear`/`incompatible` im Migrationsfenster, blockiert aber aktive Sites
     mit `can_dispatch=true` konsistent auf `CONFIG_INCONSISTENT`.
6. Operator-/API-Doku für die neuen Eingaben und Fehlermodi.

## Gemeinsamer Ausführungs-/Fehlermodus-Vertrag

Die Ausführungsregeln für Status/Mappings (`OptimizationSolverStatus`,
`TerminationCode`, `CanExecute`, `TerminationDetail`) sind in diesem Slice und im
LER/FCR-Robustheitsslice **kompatibel und semantisch konsistent** zu halten.

- Autoritative Quelle für den gemeinsamen Vertrag ist
  [`plan-domain-migration-optimization-run-can-execute.md`](plan-domain-migration-optimization-run-can-execute.md).
- Die vollständige `CanExecute`-/`OptimizationSolverStatus`/`TerminationCode`-Matrix ist dort autoritativ festgelegt und wird hier nicht dupliziert.
- Änderungen an dieser Matrix sind Release-blocking, wenn nicht im Pre-Slice und in allen konsumierenden Plänen umgesetzt.
- Preis-/Forecast-Serienidentität ist mit
  [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md)
  semantisch deckungsgleich definiert:
  `series_id`, `source.provider_id`, `series_type`, `series_product`,
  `market_bid_area` (falls gesetzt), `site_id` (falls gesetzt), `unit`,
  `resolution_minutes`, `series_version`.
- Produktive Nutzung externer lokaler Erzeugungs-/Forecast-Serien setzt den
  Pre-Slice
  [`Domain-Migration PriceSeries.Identity`](plan-domain-migration-price-series-identity.md)
  voraus.
- Änderungen an `CanExecute`, Laufkodierung oder Terminierungsdetails sind nur
  gemeinsam und im selben Release-Commit umzusetzen.
- Cross-Slice-Contract ist verbindlich und prüfpflichtig:
  - Die Mapping-Matrix (`OptimizationSolverStatus` + `TerminationCode` + `CanExecute`)
    wird ausschließlich in [`plan-domain-migration-optimization-run-can-execute.md`](plan-domain-migration-optimization-run-can-execute.md)
    gepflegt; dieser Plan darf nur auf sie referenzieren.
  - Abweichung ist ein hartes Release-Blocking; kein Slice darf unabhängig freigegeben werden.
- Implementierungsvorgabe:
  - Vor Aktivierung dieses Slices ist ein eigener Pre-Slice
    [`Domain-Migration OptimizationRun.CanExecute`](plan-domain-migration-optimization-run-can-execute.md)
    abzuschließen.
  - Domain-Constructor, Store-/Wire-/API-Migration, Dispatch-Gate und
    ProducedSchedule-Invarianten werden dort autoritativ definiert und hier
    nicht wiederholt.
- Ein Slice darf erst freigegeben werden, wenn beide Dokumente semantisch
  übereinstimmen und die Cross-Checks bestanden sind.
- Beitrag zum `CanExecute`-Combiner:
  - Co-Location führt keinen eigenen `co_location_ok`-Beitrag ein.
  - `CONFIG_INVALID` und `CONFIG_INCONSISTENT` setzen den bestehenden
    Combiner-Beitrag `config_ok=false`.
  - `SCHEMA_INCONSISTENT` setzt `schema_ok=false`.
  - Preis-/Forecast-Qualität aus `SeriesEnvelope` setzt `source_ok` gemäß
    [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).
  - `MODEL_INFEASIBLE` ist kein Guard-Beitrag, sondern ein Solver-/Modellergebnis;
    es führt über `HasUsableSolution=false` zur Nichtausführbarkeit.
  - Alle daraus folgenden `CanExecute`-, `TerminationCode`- und Dispatch-
    Entscheidungen ergeben sich ausschließlich aus dem Pre-Slice-Vertrag.

---

## Akzeptanzkriterien

- Bestehende Day-Ahead-/Intraday-Optimierung ohne Co-Location-Input bleibt
  unverändert.
- Co-Location-Constraints werden im Run-Ergebnis als eigene Objective- oder
  Constraint-Komponenten sichtbar.
- Ein Setup wird so gemappt, dass Operator- und Replay-Sichten klar trennbar bleiben:
  - Die fachlichen Co-Location-Klassen `CONFIG_INVALID`, `CONFIG_INCONSISTENT`,
    `SCHEMA_INCONSISTENT` und `MODEL_INFEASIBLE` bleiben im Run-Audit eindeutig
    erkennbar.
  - Das konkrete Mapping auf `OptimizationSolverStatus`, `TerminationCode` und
    `CanExecute` entspricht ohne lokale Abweichung der autoritativen Matrix in
    [`plan-domain-migration-optimization-run-can-execute.md`](plan-domain-migration-optimization-run-can-execute.md).
  - Replay-/Golden-Fixtures decken mindestens ein Standalone- und ein
     Co-Location-Szenario ab.
  - Der technische Dispatch-Pfad bleibt Safety-First und kennt keine
  externen Marktdetails.
  - Scheduler/Dispatcher wird integrationstauglich getestet mit
    `OptimizationSolverStatus.(Optimal|Feasible)` bei `CanExecute=false`:
    es darf kein Lauf in den ausführbaren Pfad gehen.

## Definition of Done (DoD)

- [ ] LP/MILP-Switching ist deterministisch und produktiv dokumentiert:
  - Standalone bleibt LP,
  - Co-Location-/Herkunftsfälle laufen in MILP,
  - kein partieller LP/MILP-Mix innerhalb eines Requests.
- [ ] Neue Domänen-/Anwendungsobjekte sind eingeführt und validiert (`SiteConstraint`, `LocalGenerationSeries`, `LocalOriginState`, `CoLocationMode`, `OriginConstraint`, `CurtailmentCost`).
- [ ] Migrationsvertrag ist in beiden Modi abgeschlossen:
  - Deterministische Klassifikation (`normalized`/`repairable`/`unclear`/`incompatible`) ist implementiert,
  - `migration_strict=true` blockiert harte Fehlerfälle im produktiven Scope,
  - `migration_strict=false` ist ein dokumentierter Sonderbetrieb mit aktivitätsbasierter Isolation (`can_dispatch`).
- [ ] Netzanbindung ist für beide `site_grid_power_sign`-Varianten verbindlich spezifiziert (`export_pos`, `import_pos`) inkl. Sign-Konventionstests.
- [ ] `GreenStorageRestricted`-Regeln sind als harte Validierung umgesetzt (`p_grid_import_t == 0`, `e_local`-Kopplung, Konfigurationsgrenzen).
- [ ] Gemeinsamer Fehler-/Ausführungsvertrag zu [plan-domain-migration-optimization-run-can-execute.md](plan-domain-migration-optimization-run-can-execute.md) und [plan-ler-fcr-reserve-robustness.md](plan-ler-fcr-reserve-robustness.md) ist abgeglichen (unter anderem `CanExecute`, `TerminationCode`, `TerminationDetail`, Cross-Checks, Mapping-Matrix) und durch mindestens einen Golden-Fixture-Test abgesichert.
- [ ] Vor produktiver Nutzung von `alignment_mode=trim-to-common` ist der
  versionierte Vorverarbeitungspfad aus
  [plan-price-forecast-adapters.md](plan-price-forecast-adapters.md)
  abgeschlossen; ohne diesen Pfad bleibt Co-Location produktiv auf
  `alignment_mode=reject` beschränkt.
- [ ] Liefergegenstände bei Aktivierung sind vollständig umgesetzt:
  - ADR/ADR-Schärfung,
  - Domain-/Application-Erweiterungen,
  - Solver/Modellerweiterung,
  - Regressionsmatrix + Migrationstestfälle,
  - Operator-/API-Dokumentation.

---

## Abschlussentscheidungen

- Reicht ein LP-Modell oder braucht der erste produktive Co-Location-Scope
  MILP-Binaervariablen für Lade-/Entlade-/Herkunftsentscheidungen?
  - Entschieden: Für den ersten produktiven Co-Location-Scope wird MILP genutzt, um
    Simultanfluss- und Herkunftskontrollen formal erzwingbar zu machen.
- Ist `LocalGenerationSeries` eigener Application-Typ oder spezialisierte Preiszeitreihe?
  - Entschieden: `LocalGenerationSeries` als eigener Application-Typ im Co-Location-Scope.
- Soll Abregelung als Kostenkomponente, Constraint-Violation oder eigene
  Fahrplanzeitreihe materialisiert werden?
  - Entschieden: Abregelung wird als Constraint (`c_t`) mit Kostenmodell (`CurtailmentCost`) umgesetzt; eigene Fahrplanzeitreihe ist für den ersten Scope nicht erforderlich.
- Welche Netzanschlusspunkt-Vorzeichenkonvention wird für Site-Level-
  Leistung normativ?
  - Entschieden: `site_grid_power_sign` ist der normative Site-Parameter (`export_pos` oder `import_pos`), ohne globalen Default und ohne impliziten Fallback.
