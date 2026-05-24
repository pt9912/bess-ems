# Plan: Co-Location- und Hybrid-BESS-Modell

**Dokumenttyp:** Slice-Skizze / offen
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
unterscheiden koennen, ohne die bestehende Safety-First-Regelpipeline
zu veraendern.

Der Slice fuehrt ein explizites Markt-/Standortmodell fuer Co-Location
ein und erweitert die Optimierungsinputs um lokale Erzeugung,
Netzanschlussgrenzen und Vermarktungsrestriktionen.

Der technische Regelkreis bleibt unveraendert:

```text
Optimierung -> Fahrplan -> State Machine -> Prioritaeten -> Limiter -> Command
```

---

## Arbeitsmodell

### Architektur- und Solver-Scope für LP/MILP

Der operative Optimierungsrequest enthält genau eine Solver-Scope-Ausprägung.

Entscheidungsreihenfolge je Request:

1. Wenn **keine** aktivierte Co-Location-/Herkunftsrestriktion betroffen ist => `solver_scope=LP`.
2. Sonst (zumindest eine nicht LP-abbildbare Restriktion vorhanden) => `solver_scope=MILP`.
3. Gemischte Requests aus LP- und MILP-relevanten Assets dürfen nur dann partitioniert werden,
   wenn die ADR-seitige Sonderlogik explizit aktiviert ist und die Partitionierung vollständig
   isolierbar sowie deterministisch reaggregierbar ist.

Die Umschaltung erfolgt per Request und ist nicht global. Ein Request mit mindestens einem betroffenen
Asset (`ClassicalCoLocation`, `HybridWithGridImport`, `GreenStorageRestricted` oder aktivierter `OriginConstraint`)
wird standardmäßig als `MILP` ausgeführt.

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
  Scope-Anforderungen ein `CONFIG_INVALID` mit Grundcode `SCHEMA_INCONSISTENT` zu werfen.

### Projekt-/Standorttypen

Der Slice modelliert mindestens diese Betriebsarten:

- `StandaloneBess`
  - Batterie handelt unabhaengig am Markt.
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
  - Batterie darf nur unter definierten Herkunfts- oder Foerderregeln
    laden/entladen.
  - Dieser Modus ist zunaechst Modell- und Validierungs-Scope; produktive
    Foerderlogik braucht eigene Rechts-/Compliance-Freigabe.

### Neue fachliche Konzepte

Moegliche Domain-/Application-Erweiterungen:

- `SiteConstraint`
  - `site_id`
  - `max_import_kw`
  - `max_export_kw`
  - optional `grid_connection_power_kw`
  - `site_grid_power_sign` (verpflichtend): `export_pos` oder `import_pos`
    - `export_pos`: positive Leistung bedeutet Export ans Netz
    - `import_pos`: positive Leistung bedeutet Import aus dem Netz

  - `LocalGenerationSeries`
  - Zeitreihe fuer PV/Wind-Erzeugung oder Forecast
  - Pflichtfelder pro Timestamp:
    - `site_id`
      - bei produktiver Co-Location-Nutzung muss die `site_id` eindeutig einem
        `SiteConstraint` zugeordnet sein; andernfalls `SCHEMA_INCONSISTENT`.
    - `timestamp_utc`
    - `resolution_minutes`
  - Die Felder `alignment_mode`, `alignment_prepared`, `alignment_prepared_by`,
    `alignment_prepared_horizon_start_utc`, `alignment_prepared_horizon_end_utc`
    entsprechen der Co-Location-Erweiterung von `SeriesEnvelope` aus
    [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).
  - `alignment_mode` (`reject` | `trim-to-common`)  
      - `reject`: harte Ablehnung bei Zeitachsenabweichung (Default im produktiven ADR)
      - `trim-to-common`: kontrollierte Trimmung auf gemeinsame Schnittmenge für Vorverarbeitung;
        nur zulässig bei abgeschlossenem Forecast-Preprocessing und nur dann als
        vorbereiteter Input (`alignment_prepared=true`).
    - `alignment_prepared` (verpflichtend bei `trim-to-common`):
      Kennzeichen, dass die Trimmung bereits vollständig deterministisch
      durchgeführt wurde (inkl. Reihenfolge- und Segmentierungsvorschrift).
    - `alignment_prepared_by` (verpflichtend bei `alignment_prepared=true`):
      Präfix/Komponente, die den Vorverarbeitungslauf ausgeführt hat (`forecast-preprocessor/v1`, `batch-trimmer/...`).
    - `alignment_prepared_horizon_start_utc` und `alignment_prepared_horizon_end_utc` (verpflichtend bei `alignment_prepared=true`):
      Resultierende harte Zeitschnittgrenzen nach Trimming.
    - `value_kw`
    - `value_type` (`forecast` | `actual`)
    - `source`
    - `product`
    - `unit` (z. B. `kW`)
    - `updated_at_utc`
    - `value_version`
- Validierung:
- gleiche Zeitachse wie `PriceSeries` (UTC, step-genau, gleiche Horizon-Länge) bei produktiver Nutzung
- Ist `alignment_mode=reject` (Default im produktiven ADR): die Zeitachse muss hart identisch sein (gleiches Horizon, gleiche Schrittweite, gleiche Startzeit).
  - Ist `alignment_mode=trim-to-common` gesetzt:
  - `alignment_prepared` muss `true` sein; ohne abgeschlossene Vorverarbeitung führt der produktive Lauf zu `SCHEMA_INCONSISTENT`.
  - `alignment_prepared` darf nur zusammen mit `alignment_mode=trim-to-common` verwendet werden.
  - produktive Optimierung darf `trim-to-common` nur mit explizit abgeschlossenem
    Vorverarbeitungs-Pfad und nachweislicher Versionierung in den zugehörigen
    Vorverarbeitungsfeldern starten; ohne diese Voraussetzungen führt der Lauf zu
    `SCHEMA_INCONSISTENT`.
  - Schnittmenge auf den gemeinsamen Zeithorizont
  - `alignment_prepared_by` und die vorbereiteten Horizon-Grenzen (`alignment_prepared_horizon_start_utc`, `alignment_prepared_horizon_end_utc`) müssen gesetzt sein.
  - deterministische Konvention bei Zeitachsen-Verschiebung
  - lückenbehaftete Schritte müssen im Anschluss vollständig abgearbeitet werden
  - nur im Forecast-/Preprocessing-Pfad, danach muss der Standardpfad (`reject`) mit lückenloser Zeitachse erhalten bleiben.
  - keine offenen Zeitlücken; zulässig: kontrollierte Backfill-Regel (max. 2 Intervalle)
    - `value_kw` ist eine Produktionsleistung und für diese Serie standardmässig nicht negativ.
    - Negative Werte sind nur über einen separaten signierten Netzausgangs-Datensatz zulässig.
    - Metadatenpflicht fuer `source`, `product`, `updated_at_utc`, `value_version`

- `LocalOriginState`
  - virtuelle Zwischenbilanz für herkunftsgebundene Energie (kWh)
  - `e_local_t` je Zeitschritt
  - `eta_charge` (optional, default `1.0`)
  - `eta_discharge` (optional, default `1.0`)
  - Begrenzung via `local_origin_capacity_kwh`
  - Validierung (nur wenn von `1.0` abweichend): `0 < eta_charge <= 1` und `0 < eta_discharge <= 1`

- `CoLocationMode`
  - Betriebsart nach obigem Arbeitsmodell
  - `GreenStorageRestricted` ist Sonderfall im selben Co-Location-Kontext und nutzt den gleichen Basis-Constraint-Stack.

- `CurtailmentCost`
  - Strafkosten fuer Abregelung lokaler Erzeugung

- `OriginConstraint`
  - optionale Restriktion, ob Batterieenergie aus lokaler Erzeugung,
    Netzbezug oder beidem stammen darf

### Netzanschlusspunkt-Konvention (verbindlich)

Fuer jede Site gilt folgendes Basis-Modell (LP) mit separater MILP-Kontrolllogik fuer Richtungen:

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
- Für den produktiven MVP wird für Co-Location mindestens MILP angenommen; bei reiner LP ist diese Ausschlussregel nicht exakt abbildbar.

### Legacy-Daten-Migrationslauf und Backout (verbindlich)

Für produktive Freischaltung ist ein deterministischer Migrationspfad für bestehende
`SiteConstraint`-Datensätze vorab definiert:

- Migrationsfenster: vor Aktivierung des Co-Location-MVP, nicht inline im operativen Lauf.
- Kandidatenklassifikation je Datensatz:
  - `normalized`: `site_grid_power_sign` ist gesetzt oder eindeutig aus vorhandenen
    Feldern deterministisch ableitbar.
  - `repairable`: Signatur kann aus deterministischen und konsistenten
    Legacy-Metadaten eindeutig und deterministisch ermittelt werden (`normalized=false`, aber ableitbar).
  - `unclear`: keine sichere Ableitung, keine gültige Signatur.
  - `incompatible`: harte Daten- oder Grenzverletzung (z. B. negative Limits, fehlende
    Pflichtwerte, inkonsistente Grenzdefinitionen).
- Deterministische Ableitungsregeln für `repairable`:
  - Es werden nur explizite Aliasfelder ausgewertet, die bereits in historischen
    Datensätzen vorhanden und eindeutig gesetzt sind:
    - `legacy_grid_power_sign`
    - `legacy_site_grid_power_sign`
    - `grid_power_sign`
    - `grid_connection_direction`
    - `site_network_power_sign`
    - `site_grid_power_sign_raw`
  - Die Werte werden auf die Zielwerte normalisiert:
    - `export_pos` oder `import_pos` (case-insensitiv akzeptiert; alle anderen Werte
      sind inkonsistent).
  - Genau eines der Aliasfelder muss gültig gesetzt sein; fehlt es oder sind mehrere
    widersprüchliche Aliaswerte vorhanden, wird der Satz als `unclear` eingestuft
    (`SITE_GRID_POWER_SIGN_MISSING`).
- Bei `repairable` Datensätzen wird automatisch eine Normalisierung durchgeführt:
  - `site_grid_power_sign_normalized=true`
  - `site_grid_power_sign_normalized_from=<ableitungsregel>`
  - `site_grid_power_sign_normalized_at=<timestamp>`
- `unclear` Datensätze müssen mit `CONFIG_INCONSISTENT` abgewiesen werden und enthalten
  einen operatorfähigen Grundcode (z. B. `SITE_GRID_POWER_SIGN_MISSING`).
- `incompatible` Datensätze werden mit `CONFIG_INCONSISTENT` und spezifischem
  `limiting_reason` abgelehnt.
- Unklare Datensätze dürfen nicht implizit in einen Default übernommen werden.
- Alle Migrationsergebnisse sind in einem Audit-Bundle je Lauf zusammengefasst und
  für Operator-Replay verfügbar.

Rollback-/Backout-Verhalten:

- Ist im produktiven Rollout noch ein Anteil `unclear`/`incompatible`, ist der Slice nicht freizuschalten.
- Für Ausnahmeszenarien kann ein Release-Block oder eine kontrollierte Suspendierung gelten:
  - Release-Block: Freigabe bis Daten bereinigt sind.
  - Notfallmodus: optionaler Operator-Override nur für nicht-aktive Sites im Rahmen einer
    dokumentierten Wartungsfreigabe.
- Keine stillschweigende „Best-Effort“-Auto-Ableitung in produktivem Requestbetrieb.

### GreenStorageRestricted-Regeln (MVP)

`GreenStorageRestricted` ist im ersten Umsetzungsumfang als harte Validierung spezifiziert:

- Erlaubte Ladequelle: ausschliesslich lokale Erzeugung (`p_grid_import_t == 0`).
- Einführung der herkunftsbezogenen Zwischengröße `e_local_t` (kWh), mit
  `Δt = resolution_minutes / 60`:
  - `e_local_{t+1} = e_local_t + eta_charge * max(0, -b_t) * Δt - max(0, b_t) * Δt / eta_discharge`
    (bei Vorzeichenkonvention `b_t>0` Entladen, `b_t<0` Laden; für `eta_charge = eta_discharge = 1` vereinfacht sich dies zu `e_local_t - b_t * Δt`)
  - `0 <= e_local_t <= local_origin_capacity_kwh`
  - `e_local_0` ist konfigurierbar (meist 0).
- Harte Koppelregeln:
  - `p_grid_import_t == 0` (für beide Site-Konventionen) – vollständiges Netzbezugsverbot.
  - `-b_t <= (g_t - c_t)` (Laden nur solange lokaler Überschuss vorliegt)
  - `b_t <= e_local_t * eta_discharge / Δt` (Entladen nur aus vorhandener lokaler Herkunftsmasse)
  - Ist kein Herkunftsnachweis oder keine ausreichende lokale Quelle vorhanden, gilt `CONFIG_INCONSISTENT`.
- Abregelung `c_t` ist im Modus erlaubt und mindert damit lokal verfügbaren Strom.
- Bei fehlender Herkunftstransparenz darf kein produktiver Lauf gestartet werden.

### Optimierungswirkung

Der Horizon-Optimierer muss zusaetzlich beruecksichtigen koennen:

- Netzanschlusspunkt-Grenzen fuer Import und Export
- lokale Erzeugungsprognose pro Zeitschritt
- optionales Herkunftssourcing bei Lade-/Entladungspfaden (`OriginConstraint`)
- optionale Abregelung lokaler Erzeugung
- Batterie-Ladefenster aus lokalem Ueberschuss
- Reservebaender aus `ReserveBand`
- bestehende Day-Ahead-/Intraday-Fahrplaene
- Degradation- und SOC-Zielkosten wie heute

Die bestehende Vorzeichenkonvention bleibt unveraendert:

- Batterie `> 0 kW` = Entladen
- Batterie `< 0 kW` = Laden

Für den Netzanschlusspunkt ist die Konvention in diesem Slice vollständig festgelegt.
`site_grid_power_sign` ist je Site explizit zu konfigurieren; `export_pos` oder `import_pos` sind beide zulässig.

---

## Nicht-Ziele

- Kein Ersatz fuer BMS-/PCS-Schutzfunktionen.
- Keine direkte Feldgeraete-Ansteuerung aus der Optimierung.
- Keine Zertifizierung oder Rechtsauslegung fuer EEG-/Foerdermodelle.
- Kein automatischer externer Forecast-Abruf; das ist
  [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).
- Ein erster produktiver Co-Location-Slice kann mit lokalen Erzeugungs-/Lastreihen starten;
  forecast-basierte Optimierung bleibt bis zur Aktivierung von
  `plan-price-forecast-adapters.md` im degraded/fallback-Modus.
- Keine Multi-Asset-Fleet-Optimierung ueber mehrere Standorte; das bleibt
  M6-Folgearbeit.

## LP-/MILP-Kompatibilitaetsstrategie

Die Umstellung auf Co-Location-MVP ist feature-gesteuert und darf bestehende LP-basierte
Bestandsrouten nicht brechen:

- Default bleibt der bestehende LP-Standard fuer `StandaloneBess` und Szenarien ohne
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
  - Degraded/Fallback-Fall im `GreenStorageRestricted` folgt dem selben LP/MILP-Wechselpfad.

---

## Liefergegenstaende bei Aktivierung

1. Folge-ADR oder ADR-Schaerfung fuer Co-Location-Modell und
   Netzanschlusspunkt-Vorzeichen.
2. Domain-Modelle fuer Standorttyp, Netzanschlussgrenzen und lokale
   Erzeugungs-/Forecast-Zeitreihen inkl. verbindlicher Site-Netzflussdefinition.
3. Application-Port-Erweiterung fuer Optimierungsrequests.
4. OR-Tools-Modellerweiterung oder neuer Solver-Pfad fuer
   Co-Location-Constraints.
5. Tests:
   - Kein simultaner Netzimport/Netzeinspeisung im gleichen Zeitschritt.
   - Standalone bleibt bit-kompatibel zum heutigen Pfad.
   - PV-Ueberschuss kann Batterie laden, ohne Exportlimit zu verletzen.
   - `site_grid_power_sign=import_pos`: Vorzeichen-/Grenzlogik bleibt konsistent
     zur Export-Variante bei identischem physischem Leistungsfluss.
   - Netzexportlimit begrenzt Batterieentladung plus lokale Erzeugung.
   - `GreenStorageRestricted`: `e_local_0`-Randfall (`0` und >0) und `local_origin_capacity_kwh`-Randfall (`0`, Minimalreserve) werden explizit geprüft.
   - `GreenStorageRestricted`: Lauf wird mit `CONFIG_INCONSISTENT` geblockt, wenn eine Netzladung (`p_grid_import_t > 0`) versucht wird.
   - Reservebaender reduzieren weiterhin verfuegbare Lade-/Entladeleistung.
   - `GreenStorageRestricted` lehnt unzulaessige Netzladung ab oder markiert
     den Run als unzulaessig.
6. Operator-/API-Doku fuer die neuen Eingaben und Fehlermodi.

---

## Akzeptanzkriterien

- Bestehende Day-Ahead-/Intraday-Optimierung ohne Co-Location-Input bleibt
  unveraendert.
- Co-Location-Constraints werden im Run-Ergebnis als eigene Objective- oder
  Constraint-Komponenten sichtbar.
- Ein infeasibles Setup liefert einen klaren, operatorfaehigen Termination-Code:
  - `OK`: gueltiger Plan berechnet
- `CONFIG_INVALID`: Eingabedaten ungueltig/fehlerhaft (Schema oder Constraints)
- `CONFIG_INCONSISTENT`: regelwerkskonflikt (z. B. `GreenStorageRestricted` ohne Herkunftsnachweis)
- `MODEL_INFEASIBLE`: Optimierungsproblem ohne Loesung bei gueltigen Daten
- `SOLVER_ERROR`: Timeout/technisches Solverproblem
- `SCHEMA_INCONSISTENT`: Scope-/Datenkonflikt (z. B. Request-Konfiguration mit unzulässiger `alignment_mode=trim-to-common` im produktiven Lauf).
- Replay-/Golden-Fixtures decken mindestens ein Standalone- und ein
  Co-Location-Szenario ab.
- Der technische Dispatch-Pfad bleibt Safety-First und kennt keine
  externen Marktdetails.

---

## Offene Entscheidungen

- Reicht ein LP-Modell oder braucht der erste produktive Co-Location-Scope
  MILP-Binaervariablen fuer Lade-/Entlade-/Herkunftsentscheidungen?
  - Entschieden: Für den ersten produktiven Co-Location-Scope wird MILP genutzt, um
    Simultanfluss- und Herkunftskontrollen formal erzwingbar zu machen.
- Wird `LocalGenerationSeries` als eigener Application-Typ eingefuehrt oder
  als spezialisierte `PriceSeries`-aehnliche Zeitreihe modelliert?
- Soll Abregelung als Kostenkomponente, Constraint-Violation oder eigene
  Fahrplanzeitreihe materialisiert werden?
- Welche Netzanschlusspunkt-Vorzeichenkonvention wird fuer Site-Level-
  Leistung normativ?
