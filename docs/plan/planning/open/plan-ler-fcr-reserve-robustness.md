# Plan: LER/FCR Reserve Robustness

**Dokumenttyp:** Slice-Skizze / offen
**Status:** Open - wartet auf Regelleistungs-/FCR-Produkttrigger
**Datum:** 2026-05-24
**Quelle-Repo:** Fachliche Sichtung von [`BESS-Simulation`](https://github.com/flpp-signature/BESS-Simulation)
Baltputnis et al. (2024), Journal of Energy Storage 102, 114082
**Bezug:**
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md),
[`../done/plan-RM-M4.md`](../done/plan-RM-M4.md),
[`../done/plan-RM-M5.md`](../done/plan-RM-M5.md),
[`../open/plan-price-forecast-adapters.md`](../open/plan-price-forecast-adapters.md)

---

## Ziel

`bess-ems` soll fuer FCR-/aFRR-Reservebereitstellung explizit pruefen
koennen, ob ein Asset oder Asset-Verbund unter Worst-Case-Aktivierung
die zugesagte Reserve ohne SOC-Verletzung liefern kann.

Der Slice schliesst die fachliche Luecke zwischen:

- heute vorhandenen `ReserveBand`-Kapazitaetsrestriktionen,
- Regelleistungsaktivierungen im Regelkreis,
- Day-Ahead-/Intraday-Optimierung,
- und dem robusten Betriebsfall fuer Limited Energy Reservoirs (LER).

Der technische Regelkreis bleibt Safety-first:

```text
Regelleistungsaktivierung -> Fahrplanauflösung -> State Machine -> Limiter -> Command
```

Die Robustheitspruefung darf Markt- und Optimierungsentscheidungen
vorbereiten oder ablehnen, aber keine BMS-/PCS-Schutzfunktion ersetzen.

---

## Ausgangslage

Heute in `bess-ems` vorhanden:

- `ReserveBand` fuer FCR, aFRR und mFRR.
- FCR als symmetrisches Reserveprodukt.
- aFRR/mFRR als Up-/Down-Reserve.
- Regelleistungsaktivierung als priorisierte Eingangsquelle im
  Regelkreis.
- Optimierer, der Reservebaender von Lade-/Entladeleistung abzieht.
- Intraday-Reoptimierung und `PriceSeries`-basierte Fahrplanoptimierung.

Nicht explizit modelliert:

- LER-Status eines Assets.
- FCR-Alert-State-Erkennung und Reserve-Mode-Transitions.
- `t_min_FCR`, Full Activation Time und Recovery-Zeit als fachliche
  Constraints.
- Worst-Case-Energiebedarf fuer FCR/aFRR ueber einen Horizont.
- Intraday-SOC-Restauration als gezielte Massnahme zur
  Reservefaehigkeit.
- Operator-faehige Fehlermodi fuer "Reserve zugesagt, aber robust nicht
  lieferbar".

---

## Arbeitsmodell

### Begriffe

- `LimitedEnergyReservoir`
  - Asset-Eigenschaft fuer Batterien, deren FCR-Lieferfaehigkeit durch
    Energieinhalt und Recovery-Regeln begrenzt ist.

- `ReserveRobustnessPolicy`
  - Produkt- und Standortregel fuer robuste Reservepruefung.
  - Mindestfelder:
    - `asset_id`
    - `reserve_product`
    - `is_ler`
    - `soc_strategy` (`active` | `conservative`)
    - `t_min_fcr`
    - `full_activation_time`
    - `max_recovery_time`
    - `intraday_gate_closure`
    - `intraday_preparation_time`
    - `market_time_unit`

- `ReserveEnergyEnvelope`
  - Zeitschrittweise Worst-Case-Energiehuelle fuer Up- und Down-Richtung.
  - Mindestfelder pro Schritt:
    - `timestamp_utc`
    - `resolution_minutes` (int, muss mit Policy/Horizont konsistent sein)
    - `worst_up_kwh`
    - `worst_down_kwh`
    - `available_up_kwh`
    - `available_down_kwh`
    - `status`
    - `limiting_reason`
  - Optional:
    - `required_activation_kw`
    - `required_activation_minutes`

- `AlertStateTimeline`
  - Zeitreihe fuer FCR-Alert-State-Status, Reserve Mode,
    Recovery-Status und `t_min_fcr`-Fortschritt.

- `ReserveRobustnessResult`
  - Ergebnis einer Pruefung:
  - `status`:
    - `ROBUST_OK`
    - `ROBUST_NEEDS_INTRADAY_RESTORE`
    - `ROBUST_INFEASIBLE`
    - `ROBUST_SOURCE_DATA_MISSING`
    - `ROBUST_POLICY_UNSUPPORTED`
  - `limiting_reason_code`:
    - `SOC_LIMIT`, `RESERVE_CAPACITY`, `RECOVERY_TIMEOUT`, `POLICY_MISMATCH`,
      `SOURCE_DATA_MISSING`, `NO_RECOVERY_PATH`
  - `status_description` (optional, menschenlesbar)

### Worst-Case-Energie

Der Slice uebernimmt die fachliche Idee aus `BESS-Simulation`, nicht den
Python-Code:

- verfuegbare Energie fuer positive Reserve:
  - SOC oberhalb Mindest-SOC, inklusive Entladeeffizienz
- verfuegbare Energie fuer negative Reserve:
  - freier SOC-Raum bis Max-SOC, inklusive Ladeeffizienz
- Worst-Case-Up:
  - FCR-Energiebedarf
  - aFRR-/mFRR-Up-Kapazitaet und Aktivierungsenergie
  - bereits geplante Intraday-/Fahrplanpositionen
  - Selbstentladung, falls konfiguriert
- Worst-Case-Down:
  - FCR-Energiebedarf
  - aFRR-/mFRR-Down-Kapazitaet und Aktivierungsenergie
  - Gegenrichtung der Intraday-/Fahrplanpositionen

LER darf im konservativen Modus eine andere FCR-Energiehuelle nutzen als
ein nicht-LER-Asset. Diese Abweichung muss in Tests und Operator-Ausgabe
sichtbar sein.

Verbindliche Zeitscheiben-Definition:

- `Δt = resolution_minutes / 60` (in Stunden) je Schritt.
- `worst_up_kwh_t`/`worst_down_kwh_t` berechnen:
  - FCR-Teil: `P_FCR * min(Δt, t_min_fcr / 60)`.
  - aFRR-/mFRR-Aufrufteil: Aktivierungsleistung multipliziert mit
    `min(Δt, full_activation_time / 60)`.
- `worst_*_kwh` muss stets auf dieselbe Zeiteinheit wie `worst_*_kw`/
  Leistungskomponenten normiert werden.
- `ReserveEnergyEnvelope.resolution_minutes` und `ReserveRobustnessPolicy.market_time_unit`
  müssen pro Check konsistent sein, sonst `ROBUST_POLICY_UNSUPPORTED`.

### Alert State und Reserve Mode

Der erste produktive Slice muss mindestens diese Zustandsmaschine
beschreiben und testen:

```text
Normal
  -> AlertTracking
  -> ReserveModeTransition
  -> ReserveMode
  -> NormalModeTransition
  -> Recovery
  -> Normal
```

Pflichtregeln:

- Alert State wird aus Frequenzabweichung oder externem
  Regelleistungsstatus abgeleitet.
- `t_min_fcr` zaehlt nur in zulaessigen Betriebsfenstern.
- `full_activation_time` ist in derselben Zeiteinheit wie `t_min_fcr` anzugeben.
- Reserve Mode darf nur aktiviert werden, wenn der resultierende
  Robustheitszustand operator-faehig erklaert wird.
- Recovery endet erst, wenn Worst-Case-Lieferfaehigkeit wieder
  hergestellt ist.
- Wird `max_recovery_time` erreicht ohne Wiederherstellung, endet die
  Recovery mit `ROBUST_INFEASIBLE` und `limiting_reason_code=RECOVERY_TIMEOUT`.
- Infeasible Recovery fuehrt zu einem klaren Status, nicht zu stiller
  Fahrplanfortsetzung.

### Intraday-SOC-Restauration

Wenn `ReserveRobustnessResult` auf `ROBUST_NEEDS_INTRADAY_RESTORE`
steht, darf der Optimierer einen Intraday-Restaurationsfahrplan
vorschlagen:

- Laden, wenn Worst-Case-Up nicht gedeckt ist.
- Entladen, wenn Worst-Case-Down nicht gedeckt ist.
- harte Begrenzung durch Asset-Leistung, Reservebaender,
  SOC-Grenzen und Gate-Closure-Zeit.
- keine automatische Marktorder im Domain- oder Regelkreis.

Der Slice definiert nur EMS-seitig den Bedarf und einen Fahrplanvorschlag;
Order-Routing oder Boersenanbindung bleibt ausserhalb.

---

## Scope bei Aktivierung

### Phase 1: Fachvertrag und Golden Fixtures

- Fachliche Portierung der relevanten Formeln als Spezifikation:
  - verfuegbare Up-/Down-Energie
  - FCR-Worst-Case-Huelle
  - kombinierte FCR/aFRR-Energiehuelle
  - LER-Alert-/Recovery-Regeln
- Replay-/Golden-Fixtures aus synthetischen Szenarien:
  - 6h volle FCR-Aktivierung
  - 30min `t_min_fcr`-Erfuellung
  - Alert-State-Ende mit Recovery
  - zu hohe aFRR-Up-Reserve bei niedrigem SOC
  - zu hohe aFRR-Down-Reserve bei hohem SOC
- Keine Abhaengigkeit auf Python oder Excel im Produktpfad.

### Phase 2: Domain- und Application-Modell

- Domain-/Application-Typen:
  - `ReserveRobustnessPolicy`
  - `ReserveEnergyEnvelope`
  - `AlertStateTimeline`
  - `ReserveRobustnessResult`
- Application-Port:
  - `IReserveRobustnessCheck`
  - optional spaeter `IReserveRestorationPlanner`
- Fehlercodes und Metriken:
  - `reserve_robustness_status`
  - `reserve_robustness_limiting_reason`
  - `reserve_recovery_state`
  - `reserve_restore_energy_kwh`

### Phase 3: Optimierungsintegration

- Precheck fuer Schedule-Optimierung:
  - Reserveband darf nicht nur leistungsmässig passen, sondern muss
    energie-/SOC-robust sein.
- Intraday-Restauration:
  - optionaler Optimierungsinput fuer benötigte Up-/Down-Energiekorrektur.
- Ergebnisverhalten:
  - Robustheitsverletzung beendet den Run mit operator-faehigem Status.
  - Keine impliziten Reserveverletzungen im produzierten Fahrplan.
  - Bestehender Pfad ohne LER/FCR-Robustheit bleibt unveraendert.
- Ergebnis-/Run-Mapping (verbindlich):
  - `reserve_robustness_status = ReserveRobustnessResult.status`.
  - `reserve_robustness_limiting_reason = ReserveRobustnessResult.limiting_reason_code`.
  - `ROBUST_OK` => `run_status=OK`, `can_execute=true`.
  - `ROBUST_NEEDS_INTRADAY_RESTORE` => `run_status=DEGRADED`, `can_execute=true`,
    Operator-Hinweis `action=intraday_restore_required`.
  - `ROBUST_INFEASIBLE` / `ROBUST_SOURCE_DATA_MISSING` => `run_status=FAILED`,
    `can_execute=false`.
  - `RECOVERY_TIMEOUT` / `ROBUST_POLICY_UNSUPPORTED` => `run_status=BLOCKED`,
    `can_execute=false`, Eingriff notwendig.

### Phase 4: Operator- und Replay-Sicht

- API-/UI-Ausgabe fuer:
  - aktuelle Reservefaehigkeit
  - Alert/Reserve/Recovery-State
  - erwartete Recovery-Endzeit
  - benoetigte Intraday-Restauration
  - nicht lieferbare Reserveanteile
- Replay-Berichte:
  - SOC-Kurve
  - FCR-/aFRR-Anforderung
  - gelieferte vs. nicht gelieferte Energie
  - Robustheitsstatus pro Zeitschritt

---

## Nicht-Ziele

- Kein direkter Python-Port aus `BESS-Simulation`.
- Keine Excel-Konfiguration im Produktpfad.
- Keine Boersenorder-Ausfuehrung.
- Keine Zertifizierung als FCR-/aFRR-Praequalifikationsnachweis.
- Kein Ersatz fuer BMS-/PCS-Schutzfunktionen.
- Keine Aenderung der bestehenden Vorzeichenkonvention.
- Kein Forecast-Adapter; Quellenbeschaffung bleibt
  [`plan-price-forecast-adapters.md`](../open/plan-price-forecast-adapters.md).

---

## Liefergegenstaende bei Aktivierung

1. ADR oder Architektur-Schaerfung fuer LER/FCR-Robustheitsmodell.
2. Domain-/Application-Typen fuer Robustheit, Alert State und Recovery.
3. Deterministische Golden-Fixtures fuer Worst-Case-Energiehuellen.
4. Application-Port `IReserveRobustnessCheck`.
5. Optimierungs-Precheck fuer energie-robuste Reservebaender.
6. Intraday-Restaurationsbedarf als strukturierter Optimierungsinput.
7. Operator-faehige Fehlercodes, Metriken und API-Ausgabe.
8. Dokumentation der Grenzen: keine Praequalifikation, keine
   automatische Marktorder.

---

## Akzeptanzkriterien

- Bestehende Optimierung ohne aktivierte LER/FCR-Robustheit bleibt
  verhaltensgleich.
- Ein FCR-Reserveband mit ausreichender Leistung, aber zu wenig Energie
  wird als `ROBUST_INFEASIBLE` erkannt.
- Ein niedriger SOC mit aFRR-Up-Verpflichtung erzeugt entweder einen
  Intraday-Restaurationsbedarf oder einen klaren Infeasible-Status.
- Ein hoher SOC mit aFRR-Down-Verpflichtung erzeugt entsprechend einen
  Entladebedarf oder einen klaren Infeasible-Status.
- `t_min_fcr`, `full_activation_time` und `max_recovery_time` sind in
  Tests sichtbar und nicht nur Konfigurationsfelder.
- Alert-State-/Recovery-Transitions sind replaybar und deterministisch.
- Keine Reserveverletzung wird durch stilles Clamping versteckt.
- Operator-Ausgabe nennt limitierende Ursache, Zeitschritt und Richtung.
- Alle Golden-Fixtures laufen ohne Netzwerk, ohne Python-Runtime und ohne
  Excel-Abhaengigkeit im `bess-ems`-Testpfad.

---

## Testideen

- `E_avail_up/down` bei SOC-Min/Max und Effizienz < 1.
- FCR-Worst-Case fuer nicht-LER: volle FCR-Leistung ueber Horizont.
- LER-konservative FCR-Huelle mit `t_min_fcr`.
- Alert State startet bei definierter Frequenzabweichung und endet erst
  bei Rueckkehr in den Normalbereich.
- Recovery scheitert nach `max_recovery_time` mit operator-faehigem
  Status.
- Voluntary-aFRR-Bid wird reduziert, bis Worst-Case-Pruefung besteht.
- Intraday-Restauration respektiert Gate Closure und MTU.
- Replay-Fall "Reserveleistung passt, Energie reicht nicht" bricht vor
  Schedule-Aktivierung ab.

---

## Offene Entscheidungen

- Wird Alert State intern aus Frequenzdaten abgeleitet oder als externer
  Status importiert?
- Liegt `IReserveRobustnessCheck` im Optimization-Umfeld oder in einem
  eigenen Markets-/Reserve-Modul?
- Soll LER-Robustheit nur Precheck sein oder als harte Constraint in den
  Horizon-Optimierer eingehen?
- Wie detailliert muss aFRR-/mFRR-Aktivierungsenergie im ersten Slice
  modelliert werden?
- Welche Operator-Sicht ist zuerst relevant: API-only, UI oder Replay-
  Bericht?
