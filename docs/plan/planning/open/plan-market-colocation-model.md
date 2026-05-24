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

- `LocalGenerationSeries`
  - Zeitreihe fuer PV/Wind-Erzeugung oder Forecast
  - gleiche Zeitraster-Regeln wie `PriceSeries`
  - Quelle, Produkt, Einheit und Forecast-/Actual-Kennzeichnung

- `CoLocationMode`
  - Betriebsart nach obigem Arbeitsmodell

- `CurtailmentCost`
  - Strafkosten fuer Abregelung lokaler Erzeugung

- `OriginConstraint`
  - optionale Restriktion, ob Batterieenergie aus lokaler Erzeugung,
    Netzbezug oder beidem stammen darf

### Optimierungswirkung

Der Horizon-Optimierer muss zusaetzlich beruecksichtigen koennen:

- Netzanschlusspunkt-Grenzen fuer Import und Export
- lokale Erzeugungsprognose pro Zeitschritt
- optionale Abregelung lokaler Erzeugung
- Batterie-Ladefenster aus lokalem Ueberschuss
- Reservebaender aus `ReserveBand`
- bestehende Day-Ahead-/Intraday-Fahrplaene
- Degradation- und SOC-Zielkosten wie heute

Die bestehende Vorzeichenkonvention bleibt unveraendert:

- Batterie `> 0 kW` = Entladen
- Batterie `< 0 kW` = Laden

Fuer den Netzanschlusspunkt muss der Slice eine eigene, explizite
Konvention festlegen, bevor Code umgesetzt wird.

---

## Nicht-Ziele

- Kein Ersatz fuer BMS-/PCS-Schutzfunktionen.
- Keine direkte Feldgeraete-Ansteuerung aus der Optimierung.
- Keine Zertifizierung oder Rechtsauslegung fuer EEG-/Foerdermodelle.
- Kein automatischer externer Forecast-Abruf; das ist
  [`plan-price-forecast-adapters.md`](plan-price-forecast-adapters.md).
- Keine Multi-Asset-Fleet-Optimierung ueber mehrere Standorte; das bleibt
  M6-Folgearbeit.

---

## Liefergegenstaende bei Aktivierung

1. Folge-ADR oder ADR-Schaerfung fuer Co-Location-Modell und
   Netzanschlusspunkt-Vorzeichen.
2. Domain-Modelle fuer Standorttyp, Netzanschlussgrenzen und lokale
   Erzeugungs-/Forecast-Zeitreihen.
3. Application-Port-Erweiterung fuer Optimierungsrequests.
4. OR-Tools-Modellerweiterung oder neuer Solver-Pfad fuer
   Co-Location-Constraints.
5. Tests:
   - Standalone bleibt bit-kompatibel zum heutigen Pfad.
   - PV-Ueberschuss kann Batterie laden, ohne Exportlimit zu verletzen.
   - Netzexportlimit begrenzt Batterieentladung plus lokale Erzeugung.
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
- Ein infeasibles Setup liefert einen operatorfaehigen Termination-Code,
  nicht nur `infeasible`.
- Replay-/Golden-Fixtures decken mindestens ein Standalone- und ein
  Co-Location-Szenario ab.
- Der technische Dispatch-Pfad bleibt Safety-First und kennt keine
  externen Marktdetails.

---

## Offene Entscheidungen

- Reicht ein LP-Modell oder braucht der erste produktive Co-Location-Scope
  MILP-Binaervariablen fuer Lade-/Entlade-/Herkunftsentscheidungen?
- Wird `LocalGenerationSeries` als eigener Application-Typ eingefuehrt oder
  als spezialisierte `PriceSeries`-aehnliche Zeitreihe modelliert?
- Soll Abregelung als Kostenkomponente, Constraint-Violation oder eigene
  Fahrplanzeitreihe materialisiert werden?
- Welche Netzanschlusspunkt-Vorzeichenkonvention wird fuer Site-Level-
  Leistung normativ?

