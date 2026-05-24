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
      - `active`: Berechnung nutzt den planlaeufigen SOC-Rahmen (`soc_min_kwh`, `soc_max_kwh`) ohne Zusatzpuffer.
      - `conservative`: Berechnung nutzt Zusatzränder auf beiden SOC-Grenzen.
    - optional `conservative_soc_headroom_kwh` (default `0`, nur bei `soc_strategy=conservative`)
    - optional `conservative_soc_headroom_ratio` (default `0`, nur bei `soc_strategy=conservative`); Bereich `0..1`
    - Wenn kein `conservative_soc_headroom_kwh` gesetzt ist, wird bei `conservative` `conservative_soc_headroom_ratio` genutzt.
    - `t_min_fcr`
  - `full_activation_time` (Fallback-Wert; produkt-spezifische Werte empfohlen)
- optional `full_activation_time_afrr` (nur wenn sinnvoll gesetzt; bei gesetztem Wert `> 0`)
- optional `full_activation_time_mfrr` (nur wenn sinnvoll gesetzt; bei gesetztem Wert `> 0`)
  - optional `simultaneous_reserve_direction_allowed` (default `false`)
  - optional `eta_charge` (default: `1.0`, falls nicht aus dem Assetmodell übernommen)
  - optional `eta_discharge` (default: `1.0`, falls nicht aus dem Assetmodell übernommen)
  - optional `self_discharge_mode` (Werte: `absolute_kwh_per_hour` | `relative_soc_per_hour`, optional nur relevant wenn `is_ler=true`)
  - optional `self_discharge_kwh_per_hour` (gültig nur bei `self_discharge_mode=absolute_kwh_per_hour`, optional nur bei `is_ler=true`)
  - optional `self_discharge_soc_per_hour` (gültig nur bei `self_discharge_mode=relative_soc_per_hour`, optional nur bei `is_ler=true`)
  - `max_recovery_time` (harte obere Grenze für die Wiederherstellungsdauer in Minuten)
  - `intraday_gate_closure`
  - `intraday_preparation_time`
  - `minutes_until_next_restore_window_start` (optional, `>= 0`)
  - `minutes_until_next_restore_window_end` (optional, wenn ein Fenster bekannt ist)
  - `market_time_unit`
  - Zeiteinheiten (verbindlich, alle in Minuten):
    - `t_min_fcr`, `full_activation_time`, `full_activation_time_afrr`,
      `full_activation_time_mfrr`, `max_recovery_time`, `intraday_gate_closure`,
      `intraday_preparation_time`, `minutes_until_next_restore_window_start`,
      `minutes_until_next_restore_window_end`
    - `market_time_unit` muss `minute` sein.
    - `resolution_minutes` und diese Policy-Felder müssen kompatibel sein.
  - Wirkungsgrade:
    - Wird `eta_charge`/`eta_discharge` in der Policy nicht gesetzt, sind sie aus dem Assetmodell zu lesen.
    - Falls gesetzt, muss gelten: `eta_min <= eta_charge <= 1` und `eta_min <= eta_discharge <= 1`.
    - `eta_min` ist ein verbindlicher, kleiner positiver Toleranzwert: `eta_min = 1e-6`.
    - `eta_charge < eta_min` oder `eta_discharge < eta_min` führt zu `ROBUST_POLICY_UNSUPPORTED`.
  - Selbstentladung:
  - Für `is_ler=true` gilt: Exakt einer der beiden Verlusteinstellungen muss gesetzt sein (`self_discharge_kwh_per_hour` oder `self_discharge_soc_per_hour`).
  - Für `is_ler=false` können die Werte gesetzt sein oder entfallen; fehlt jeder Wert, wird kein zusätzlicher LER-spezifischer Verlustfaktor verwendet.
  - `self_discharge_kwh_per_hour` und `self_discharge_soc_per_hour` können bei Bedarf aus dem Assetmodell geerbt werden.
  - `self_discharge_mode=absolute_kwh_per_hour`:
      - für `is_ler=true`: `self_discharge_kwh_per_hour` ist Pflichtfeld (`>= 0`),
      - `self_discharge_soc_per_hour` wird ignoriert.
    - `self_discharge_mode=relative_soc_per_hour`:
      - für `is_ler=true`: `self_discharge_soc_per_hour` ist Pflichtfeld (`>= 0`, Anteil SOC-Verlust pro Stunde),
      - `self_discharge_kwh_per_hour` wird ignoriert.
  - Für `soc_strategy=conservative` gelten Zusatzregeln:
    - `conservative_soc_headroom_ratio` muss im Bereich `0..1` liegen.
    - `conservative_soc_headroom_kwh` muss `>= 0` sein.
    - `effective_soc_headroom_kwh = max(
      conservative_soc_headroom_kwh,
      conservative_soc_headroom_ratio * (soc_max_kwh - soc_min_kwh)
    )`
    - `soc_min_eff_kwh = soc_min_kwh + effective_soc_headroom_kwh`
    - `soc_max_eff_kwh = soc_max_kwh - effective_soc_headroom_kwh`
    - `soc_min_eff_kwh < soc_max_eff_kwh` muss gelten, sonst `ROBUST_POLICY_UNSUPPORTED`.
  - Effektivzeiten je Produkt (Fallback auf `full_activation_time`):
  - `full_activation_time_afrr_eff = coalesce(full_activation_time_afrr, full_activation_time)`
  - `full_activation_time_mfrr_eff = coalesce(full_activation_time_mfrr, full_activation_time)`
  - Harte Schema-Validierung:
  - `self_discharge_mode` darf, soweit gesetzt, **nur** `absolute_kwh_per_hour` oder `relative_soc_per_hour` sein; alles andere -> `ROBUST_POLICY_UNSUPPORTED`.
  - Für `is_ler=true`: ist keiner der beiden Werte gesetzt, ist der Fall ungültig (`ROBUST_POLICY_UNSUPPORTED`).
  - Für `is_ler=true`: sind beide Werte gesetzt, ist der Fall ungültig (`ROBUST_POLICY_UNSUPPORTED`).
  - `self_discharge_kwh_per_hour` darf nicht negativ sein.
  - `self_discharge_soc_per_hour` muss in `[0,1]` liegen (als Anteil pro Stunde).
  - `minutes_until_next_restore_window_start` ist optional, muss aber bei
    benötigtem Intraday-Restore `>= 0` sein.
  - Wenn `minutes_until_next_restore_window_start` gesetzt ist, muss
    `minutes_until_next_restore_window_end` gesetzt und
    `minutes_until_next_restore_window_end > minutes_until_next_restore_window_start` sein.
  - Wenn `minutes_until_next_restore_window_start` `null` ist, liegt zum Bewertungszeitpunkt
    kein gültiges Intraday-Restorefenster vor.
  - `market_time_unit` muss exakt `minute` sein und mit `resolution_minutes` konsistent sein.
  - `t_min_fcr`, `full_activation_time`, `max_recovery_time`, `intraday_gate_closure`, `intraday_preparation_time` und `resolution_minutes` müssen streng `> 0` sein.
  - `full_activation_time_afrr` und `full_activation_time_mfrr` müssen, falls gesetzt, streng `> 0` sein.
  - `simultaneous_reserve_direction_allowed` ist optional-boolean; fehlt/`false` wird als Default `false` interpretiert.
  - `simultaneous_reserve_direction_allowed=true` ist in Kombination mit nicht-linearer Kopplung nur mit klarer
    Reihenfolgeimplementierung in der Worst-Case-Rekursion erlaubt.

- `ReserveEnergyEnvelope`
  - Zeitschrittweise Worst-Case-Energiehuelle fuer Up- und Down-Richtung.
  - Mindestfelder pro Schritt:
    - `timestamp_utc`
    - `resolution_minutes` (int, muss mit Policy/Horizont konsistent sein)
    - `worst_up_kw`
    - `worst_down_kw`
    - `worst_up_kwh`
    - `worst_down_kwh`
    - `available_up_kwh`
    - `available_down_kwh`
    - `status`
    - `limiting_reason`
    - `required_up_kwh` (optional, fuer Restore-Planung)
    - `required_down_kwh` (optional, fuer Restore-Planung)
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
      `SOURCE_DATA_MISSING`, `NO_RECOVERY_PATH`, `INTRADAY_GATE_CLOSED`
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

Deterministische Berechnung (verbindlich):

- Eingänge:
  - `soc_t`: SOC zu Beginn des Intervalls in kWh.
  - `soc_min_kwh`, `soc_max_kwh` pro Asset.
  - `eta_discharge`, `eta_charge`.
  - `self_discharge_mode` und damit korrespondierende Verlustkennzahl:
    - für `is_ler=true` zuerst Policy versuchen, andernfalls fallback auf Assetmodell.
    - für `is_ler=true` fällt die Prüfung auf fehlende Werte auf `ROBUST_POLICY_UNSUPPORTED`.
    - für `is_ler=false`, falls keiner gesetzt ist: Verlustkorridor ist deaktiviert (`0`).
    - nach der Auflösung darf bei `is_ler=true` genau ein korrespondierender Wert gesetzt sein:
      - `self_discharge_kwh_per_hour` (absolute VerlustkWh/h) oder
      - `self_discharge_soc_per_hour` (SOC-Abschlag pro Stunde).
  - Vorzeichenkonvention wie im Optimierer: `b_t > 0` Entladen, `b_t < 0` Laden.
- Reserve-Anforderungen je Zeitschritt:
  - `fcr_up_kw_t`, `fcr_down_kw_t`, `afrr_up_kw_t`, `afrr_down_kw_t`, `mfrr_up_kw_t`, `mfrr_down_kw_t`
  - optionale Pflichtanteile je Produkt und Richtung:
  - `alpha_afrr_up_t`, `alpha_afrr_down_t`
  - `alpha_mfrr_up_t`, `alpha_mfrr_down_t`
  - Default: `1.0`, wenn das Produkt gebucht ist, sonst `0.0` bei nicht gebuchtem Produkt.
  - Bereich: `0 <= alpha_* <= 1` (numerisch stabiler Toleranzbereich in der Umsetzung, z. B. `1e-9`).
 - `Δt = resolution_minutes / 60` (in Stunden).
  - Sofern `simultaneous_reserve_direction_allowed=false`, wird konservativ angenommen, dass Up- und Down-Aktivierung nicht
    simultan in derselben Zeitscheibe als Primärfall auftreten.
  - Sofern `simultaneous_reserve_direction_allowed=true`, werden simultane Richtungen
    im selben Schritt wie folgt gekoppelt:
    - `worst_total_kwh_t = worst_up_kwh_t + worst_down_kwh_t`
    - `worst_total_kw_t = worst_total_kwh_t / Δt`
    - Die Worst-Case-Rekursion wird deterministisch im Sequenzpfad geprüft:
      1) Up-Branch auf `worst_up_kwh_t` (Absenkung),
      2) Down-Branch auf `worst_down_kwh_t` auf dem SOC nach Up.
    - Diese Kopplung ist bewusst konservativ und vollständig deterministisch.
  - Wenn simultanes Gegensignal laut Produktdefinition aktiv ist, aber `simultaneous_reserve_direction_allowed=false`,
    gilt hart `ROBUST_POLICY_UNSUPPORTED` mit `POLICY_MISMATCH`.
- FCR-Worst-Case-Konsistenz bei Mindestzeit:
  - Für FCR wird die Mindestaktivierungszeit als Zustandsgröße geführt:
    - `fcr_remaining_t` ist der verbleibende Restbedarf in Minuten.
    - `fcr_remaining_0 = t_min_fcr`.
    - Bei Schrittübergang in Alert wird `fcr_remaining_t` auf `t_min_fcr` gesetzt.
    - Während fortlaufender Alert-Phasen:
      `fcr_remaining_{t+1} = max(0, fcr_remaining_t - Δt*60)`.
    - Bei Alert in Schritt `t` ist die zunächst zu reservierende FCR-Energie auf
      `min(Δt, fcr_remaining_t/60)` h zu skalieren.
    - Mit dem obigen stateful Ansatz wird bei kleinen `Δt` die volle Mindestdauer konservativ über Folgeintervalle berücksichtigt.
- Hilfsgröße:
  - `self_discharge_loss_kwh_t` ist deterministisch zu berechnen:
    - bei `self_discharge_mode=absolute_kwh_per_hour`:
      `self_discharge_loss_kwh_t = self_discharge_kwh_per_hour * Δt`.
    - bei `self_discharge_mode=relative_soc_per_hour`:
      `self_discharge_loss_kwh_t = soc_t * self_discharge_soc_per_hour * Δt`.
- Interner SOC-Rechnungszugang aus geplanter Fahrplanposition:
  - `soc_{t+1,plan}` ist für gültige Laufparameter nur definiert, wenn `eta_charge >= eta_min` und `eta_discharge >= eta_min` gilt.
  - `soc_{t+1,plan} = soc_t - max(0, b_t) * Δt / eta_discharge + max(0, -b_t) * eta_charge * Δt - self_discharge_loss_kwh_t`.
- Effektiver SOC für Worst-Case-Prüfung:
  - `soc_t` vor der Worst-Case-Prüfung hart validieren:
    - `soc_min_eff_kwh <= soc_t <= soc_max_eff_kwh`, sonst `ROBUST_POLICY_UNSUPPORTED`.
  - `soc_t^{eff} = soc_t - self_discharge_loss_kwh_t`.
- Verfügbare Energiemengen für die Verfügbarkeitsprüfung am Schrittanfang auf Basis der geplan­ten Fahrplanwirkung:
  - `soc_plan_base_t = soc_t^{eff} - max(0, b_t) * Δt / eta_discharge + max(0, -b_t) * eta_charge * Δt`
  - `available_up_kwh_t = max(0, soc_plan_base_t - soc_min_eff_kwh) * eta_discharge`
  - `available_down_kwh_t = max(0, soc_max_eff_kwh - soc_plan_base_t) / eta_charge`
- Worst-Case-Energie je Richtung (kWh):
  - `fcr_remaining_t`-abhängiger FCR-Term:
    - `fcr_term_up_t = min(Δt, fcr_remaining_t/60) * fcr_up_kw_t`
    - `fcr_term_down_t = min(Δt, fcr_remaining_t/60) * fcr_down_kw_t`
  - `worst_up_kwh_t = fcr_term_up_t + min(Δt, full_activation_time_afrr_eff/60) * (alpha_afrr_up_t * afrr_up_kw_t) + min(Δt, full_activation_time_mfrr_eff/60) * (alpha_mfrr_up_t * mfrr_up_kw_t)`
  - `worst_down_kwh_t = fcr_term_down_t + min(Δt, full_activation_time_afrr_eff/60) * (alpha_afrr_down_t * afrr_down_kw_t) + min(Δt, full_activation_time_mfrr_eff/60) * (alpha_mfrr_down_t * mfrr_down_kw_t)`
- Normalisierte Worst-Case-Leistung:
  - `worst_up_kw_t = worst_up_kwh_t / Δt`
  - `worst_down_kw_t = worst_down_kwh_t / Δt`
- Abgleich (kumulativ, pro Richtungs-Szenario):
  - Kumulativer SOC-Zustand für Up- und Down-Reserve wird je Schritt fortgeschrieben.
  - Initialzustand pro Schrittreihenfolge:
    - `soc_up_0 = soc_t`
    - `soc_down_0 = soc_t`
  - Zeitschrittweise Rekursion:
    - `soc_plan_up_t = soc_up_t - max(0, b_t) * Δt / eta_discharge + max(0, -b_t) * eta_charge * Δt - self_discharge_loss_kwh_t`
    - `soc_plan_down_t = soc_down_t - max(0, b_t) * Δt / eta_discharge + max(0, -b_t) * eta_charge * Δt - self_discharge_loss_kwh_t`
    - Sicherheitsanker:
      - Falls `soc_plan_up_t < soc_min_eff_kwh` oder `soc_plan_up_t > soc_max_eff_kwh`, gilt `ROBUST_INFEASIBLE` mit `SOC_LIMIT`.
      - Falls `soc_plan_down_t < soc_min_eff_kwh` oder `soc_plan_down_t > soc_max_eff_kwh`, gilt `ROBUST_INFEASIBLE` mit `SOC_LIMIT`.
    - Up-Branch: `worst_up_kwh_t <= (soc_plan_up_t - soc_min_eff_kwh) * eta_discharge`
      - falls ja: `soc_up_{t+1} = soc_plan_up_t - worst_up_kwh_t / eta_discharge`
      - falls nein: `ROBUST_INFEASIBLE` mit `SOC_LIMIT`
      - Nach dem Update: `soc_up_{t+1} >= soc_min_eff_kwh` und `soc_up_{t+1} <= soc_max_eff_kwh`, sonst `ROBUST_INFEASIBLE` mit `SOC_LIMIT`
    - Down-Branch: `worst_down_kwh_t <= (soc_max_eff_kwh - soc_plan_down_t) / eta_charge`
      - falls ja: `soc_down_{t+1} = soc_plan_down_t + worst_down_kwh_t * eta_charge`
      - falls nein: `ROBUST_INFEASIBLE` mit `SOC_LIMIT`
      - Nach dem Update: `soc_down_{t+1} >= soc_min_eff_kwh` und `soc_down_{t+1} <= soc_max_eff_kwh`, sonst `ROBUST_INFEASIBLE` mit `SOC_LIMIT`
- `ROBUST_OK`, wenn kein Schritt in beiden Branches versagt.
- Bei Grenz- und Dateninkonsistenzen: `ROBUST_NEEDS_INTRADAY_RESTORE` oder
  `ROBUST_INFEASIBLE` anhand `limiting_reason_code`.

Restore- und Gate-Entscheidungslogik (verbindlich):

- Bei `ROBUST_INFEASIBLE` mit Primärursache `SOC_LIMIT` oder `RESERVE_CAPACITY` prüft der Prüfer zuerst, ob
  die Abweichung durch kontrollierte Intraday-Restore-Aktionen behoben werden kann:
  - Up-Verstoß-Defizit:
    `restore_shortfall_up_kwh = max(0, worst_up_kwh_t - (soc_plan_up_t - soc_min_eff_kwh) * eta_discharge)`
  - Down-Verstoß-Defizit:
    `restore_shortfall_down_kwh = max(0, worst_down_kwh_t - (soc_max_eff_kwh - soc_plan_down_t) / eta_charge)`
- Verfügbare Wiederherstellungsleistung je Schritt:
    - Up-Branch: `restore_shortfall_up_kwh` kann durch `worst_up_kw_t` rückgewonnen werden:
      `required_activation_kw_up = if restore_shortfall_up_kwh > 0 then worst_up_kw_t else 0`
    - Down-Branch:
      `required_activation_kw_down = if restore_shortfall_down_kwh > 0 then worst_down_kw_t else 0`
    - Richtungsbasierte Mindest-Rekonstruktionsdauer:
      - `restore_time_up = if restore_shortfall_up_kwh > 0 and required_activation_kw_up > 0 then restore_shortfall_up_kwh / required_activation_kw_up * 60 else 0`
      - `restore_time_down = if restore_shortfall_down_kwh > 0 and required_activation_kw_down > 0 then restore_shortfall_down_kwh / required_activation_kw_down * 60 else 0`
    - `restore_shortfall_kwh = max(restore_shortfall_up_kwh, restore_shortfall_down_kwh)`
    - Falls ein aktiver Branch (`restore_shortfall_*_kwh > 0`) jedoch `required_activation_kw_* <= 0` hat:
      `ROBUST_INFEASIBLE` mit `limiting_reason_code=NO_RECOVERY_PATH`.
    - Mit manuellem Override `required_activation_kw > 0`:
      `required_recovery_minutes = restore_shortfall_kwh / required_activation_kw * 60`.
    - Ohne Override:
      `required_recovery_minutes = max(restore_time_up, restore_time_down)` (konservativster Branch).
    - `required_activation_kw_used` ist der Richtungswert (`worst_up_kw_t` oder `worst_down_kw_t`), der `required_recovery_minutes` bestimmt; bei Gleichstand deterministisch die Up-Richtung.
- Falls `required_recovery_minutes > max_recovery_time` => `ROBUST_INFEASIBLE` mit
  `limiting_reason_code=RECOVERY_TIMEOUT`.
- Falls kein zulässiges Window vorliegt:
  (`minutes_until_next_restore_window_start` ist `null` oder
  `minutes_until_next_restore_window_end` ist `null`):
  - Ergebnis `ROBUST_INFEASIBLE` mit `limiting_reason_code=INTRADAY_GATE_CLOSED`.
- Falls das nächste Window den benötigten Zeitraum nicht trägt:
  `minutes_until_next_restore_window_start + intraday_preparation_time + intraday_gate_closure + required_recovery_minutes >
  minutes_until_next_restore_window_end`:
  - Ergebnis `ROBUST_INFEASIBLE` mit `limiting_reason_code=INTRADAY_GATE_CLOSED`.
- Ansonsten wird der Endstatus `ROBUST_NEEDS_INTRADAY_RESTORE`.
- Bei allen anderen Fällen bleibt der bestehende Abgleich auf `ROBUST_OK`/`ROBUST_INFEASIBLE` unverändert.

Explizite Mappings zwischen Eingangsfehlern und Ergebniscodes:

- `ROBUST_POLICY_UNSUPPORTED`
  - Policy fehlt oder ist inkompatibel (`market_time_unit`, Auflösungsinkonsistenzen,
    unbekannte Produktparameter, inkonsistente Zeitkonfiguration je Produkt, ungültige Wirkungsgrade).
  - `limiting_reason_code=POLICY_MISMATCH`
- `ROBUST_SOURCE_DATA_MISSING`
  - Erforderliche Reserve-/Statusdaten (z. B. `fcr_*`, `afrr_*`, `mfrr_*`, Frequenz-/LER-Status) sind nicht verfügbar.
  - `limiting_reason_code=SOURCE_DATA_MISSING`
- `ROBUST_INFEASIBLE` bei `RESERVE_CAPACITY`
  - Kumulierte Worst-Case-Energie oder `available_*`-Berechnung reicht für geplante Reserven nicht aus.
- `ROBUST_INFEASIBLE` bei `SOC_LIMIT`
  - Plausibilisierte SOC-Rekursion verletzt Mindest-/Maximal-SOC in einem der Richtungs-Szenarien.
- `ROBUST_NEEDS_INTRADAY_RESTORE`
  - Reserve ist prinzipiell buchbar, aber ein aktueller Wiederherstellungsschritt ist im aktuellen
    Restore-Fenster notwendig bzw. nur nach Intraday-Regelung wiederherstellbar.
- `ROBUST_INFEASIBLE` bei `NO_RECOVERY_PATH`
  - Kein zulässiger Restore-Vorschlag wegen Leistungs-/SOC-/Reserveband-Limit oder fehlender
    Wiederherstellung im Zeitfenster.
- `ROBUST_INFEASIBLE` bei `INTRADAY_GATE_CLOSED`
  - Restore erforderlich, aber Gate-/Vorlaufregeln verhindern Ausführung.

Verbindliche Zeitscheiben-Definition:

- `Δt = resolution_minutes / 60` (in Stunden) je Schritt.
- `worst_up_kwh_t`/`worst_down_kwh_t` werden exakt gemäss vorheriger Deterministik-Berechnung
  berechnet; die daraus abgeleiteten `worst_up_kw_t`/`worst_down_kw_t` sind normalisierte
  Leistungsgrössen für die `ReserveEnergyEnvelope`-Ausgabe.
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
- `full_activation_time` ist in derselben Zeiteinheit wie `t_min_fcr` anzugeben;
  produkt-spezifische Werte `full_activation_time_afrr` und
  `full_activation_time_mfrr` verwenden ebenfalls dieselbe Einheit.
- `intraday_preparation_time` gilt als Mindestvorlauf für den Restore-Einstieg:
  Ein Restore-Vorschlag darf nur erzeugt werden, wenn vor dem geplanten Ausführungsfenster
  mindestens die Vorlaufzeit eingehalten ist.
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

 - Zuerst wird je Zeitfenster berechnet, ob eine Wiederherstellung je Richtung nötig ist:
   - `required_restore_up_kwh_t = coalesce(required_up_kwh_t, worst_up_kwh_t)`
   - `required_restore_down_kwh_t = coalesce(required_down_kwh_t, worst_down_kwh_t)`
   - `restore_up_kwh_t = max(0, required_restore_up_kwh_t - available_up_kwh_t)`
   - `restore_down_kwh_t = max(0, required_restore_down_kwh_t - available_down_kwh_t)`
 - Wenn `restore_up_kwh_t > 0` und `restore_down_kwh_t > 0` im selben Schritt gilt:
   - `ReserveRobustnessResult` wird mit `ROBUST_INFEASIBLE` und `limiting_reason_code = NO_RECOVERY_PATH` geführt (ein Batteriekorridor kann pro Schritt nicht zugleich laden und entladen).
 - Sonst:
   - Laden, wenn `restore_up_kwh_t > 0` (Wird Worst-Case-Up nicht gedeckt).
   - Entladen, wenn `restore_down_kwh_t > 0` (Wird Worst-Case-Down nicht gedeckt).
 - harte Begrenzung durch Asset-Leistung, Reservebaender,
   SOC-Grenzen und Gate-Closure-Zeit.
 - Restore ist nur dann ausgabe- und ausführungspfadfähig, wenn aktuell ein
   ausreichendes Wiederherstellungsfenster offen ist und `intraday_gate_closure`
   nicht aktiv ist.
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
  - Run-Metadatum: `CanExecute` (bool), default `true` bei produktiv
    nutzbaren Läufen, `false` bei klaren Restore- oder Gate-bedingten
    Ausführungsblockaden.

### Phase 3: Optimierungsintegration

- Dieser Abschnitt ist der gemeinsame Referenzvertrag für Ausführungs- und
  Guard-Verhalten (`OptimizationSolverStatus`, `TerminationCode`, `CanExecute`,
  `CanExecute=false`-Interpretation).  
  Das Gegenstück im Co-Location-Slice ist
  [`plan-market-colocation-model.md`](plan-market-colocation-model.md);
  beide Abschnitte müssen semantisch identisch bleiben.

- Precheck fuer Schedule-Optimierung:
  - Reserveband darf nicht nur leistungsmässig passen, sondern muss
    energie-/SOC-robust sein.
- Intraday-Restauration:
  - optionaler Optimierungsinput fuer benötigte Up-/Down-Energiekorrektur.
- Ergebnisverhalten:
  - Robustheitsverletzung beendet den Lauf nicht direkt über neue Run-Statuswerte,
    sondern über bestehende `OptimizationSolverStatus` + strukturierte Termination-Codes.
  - Keine impliziten Reserveverletzungen im produzierten Fahrplan.
  - Bestehender Pfad ohne LER/FCR-Robustheit bleibt unveraendert.
- Ergebnis-/Run-Mapping (verbindlich):
  - `reserve_robustness_status = ReserveRobustnessResult.status`.
  - `reserve_robustness_limiting_reason = ReserveRobustnessResult.limiting_reason_code`.
  - Bei `reserve_robustness_status != ROBUST_OK` ist
    `reserve_robustness_limiting_reason` als Pflichtfeld im `OptimizationRun`-`TerminationDetail`
    zu persistieren, inkl. optionaler `status_description`, damit Operator-/Replay-Sichten die Ursache eindeutig nachvollziehen.
  - `CanExecute = (reserve_robustness_status == ROBUST_OK)` ist die einzige ausführbare
    Betriebsart; bei allen anderen Statuswerten ist `CanExecute=false`.
  - `CanExecute` ist harte Ausführungsbedingung im Dispatcher/Scheduler:
    `OptimizationSolverStatus.Feasible` bei `CanExecute=false` gilt weiterhin als nicht auszuführen.
  - `ROBUST_OK` => keine zusätzliche Hard-Stop-Sperre.
  - `ROBUST_NEEDS_INTRADAY_RESTORE` =>
    - bei verfügbarem Restore-Fenster: Optimierung bleibt lauffähig,
      der Lauf wird mit `OptimizationSolverStatus.Feasible` persistiert,
      `CanExecute=false` und Operator-Hinweis `action=intraday_restore_required`.
    - bei geschlossenem Gate / keinem Wiederherstellungsfenster:
      `OptimizationSolverStatus.Failed` mit
      `TerminationCode=reserve-robustness-not-executable`,
      `TerminationDetail=INTRADAY_GATE_CLOSED`,
      `CanExecute=false` in Operator-/Replay-Ausgabe.
  - `ROBUST_INFEASIBLE`:
    - bei `limiting_reason_code=RECOVERY_TIMEOUT` =>
      `OptimizationSolverStatus.Failed` mit
      `TerminationCode=reserve-robustness-recovery-timeout`,
      `TerminationDetail=RECOVERY_TIMEOUT`.
    - sonst => `OptimizationSolverStatus.Failed` mit
      `TerminationCode=reserve-robustness-infeasible`.
  - `ROBUST_SOURCE_DATA_MISSING` => `OptimizationSolverStatus.Failed`
    mit `TerminationCode=reserve-robustness-source-missing`.
  - `ROBUST_POLICY_UNSUPPORTED` => `OptimizationSolverStatus.Failed` mit
    `TerminationCode=reserve-robustness-policy-unsupported`.
- `ROBUST_OK` wird mit dem eigentlichen Optimizerergebnis (`Optimal`/`Feasible`) persistiert.
- `ROBUST_NEEDS_INTRADAY_RESTORE` wird immer als `OptimizationSolverStatus.Feasible`
  mit `CanExecute=false` und explizitem Restore-Hinweis persistiert.

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
7. Operator-faehige Fehlercodes, Metriken und API-Ausgabe inkl. `CanExecute`.
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
- Bei `ROBUST_NEEDS_INTRADAY_RESTORE` ist der Optimierungslauf als
 `CanExecute=false` persistiert und die notwendige Restore-Massnahme
 in Operator-/Replay-Ausgabe explizit markiert.
  - `CanExecute=false` wird in allen nicht-`ROBUST_OK`-Laufpfaden als harte
    Nicht-Ausführungsbedingung durchgesetzt, unabhängig vom Solver-Status.
- `t_min_fcr`, `full_activation_time` und `max_recovery_time` sind in
  Tests sichtbar und nicht nur Konfigurationsfelder.
- `full_activation_time_afrr` / `full_activation_time_mfrr` sind in den
  Akzeptanztests explizit belegt und nicht nur als Fallback-Default `full_activation_time`.
- Alert-State-/Recovery-Transitions sind replaybar und deterministisch.
- Keine Reserveverletzung wird durch stilles Clamping versteckt.
- Operator-Ausgabe nennt limitierende Ursache, Zeitschritt und Richtung.
- Alle Golden-Fixtures laufen ohne Netzwerk, ohne Python-Runtime und ohne
  Excel-Abhaengigkeit im `bess-ems`-Testpfad.

---

## Testideen

- `E_avail_up/down` bei SOC-Min/Max und Effizienz < 1.
- Selbstentlade-Schemafehler (explizit):
  - `self_discharge_mode=invalid` (`x_per_hour` unbekannt) → `ROBUST_POLICY_UNSUPPORTED`.
  - beide Moduswerte gesetzt (`self_discharge_kwh_per_hour` und `self_discharge_soc_per_hour`) → `ROBUST_POLICY_UNSUPPORTED`.
  - `is_ler=true` und kein Moduswert gesetzt (beide optional Felder leer/nicht gesetzt) → `ROBUST_POLICY_UNSUPPORTED`.
  - negative/überschüssige Werte (`self_discharge_kwh_per_hour < 0`, `self_discharge_soc_per_hour < 0` oder `> 1`) → `ROBUST_POLICY_UNSUPPORTED`.
- `market_time_unit` abseits von `minute` → `ROBUST_POLICY_UNSUPPORTED`.
- Auflösungsfelder kleiner oder gleich 0 (`t_min_fcr`, `full_activation_time`, `max_recovery_time`, `intraday_gate_closure`, `intraday_preparation_time`, `resolution_minutes`) → `ROBUST_POLICY_UNSUPPORTED`.
- `full_activation_time_afrr` / `full_activation_time_mfrr` werden nur bei gesetztem Feldwert geprüft:
  - gesetzt und `<= 0` → `ROBUST_POLICY_UNSUPPORTED`.
- `conservative_soc_headroom` außerhalb `0..1` (ratio) bzw. `<0` (kwh) → `ROBUST_POLICY_UNSUPPORTED`.
- FCR-Worst-Case fuer nicht-LER: volle FCR-Leistung ueber Horizont.
- LER-konservative FCR-Huelle mit `t_min_fcr`.
- Voller Aktivierungszeitraum deutlich größer als Horizon (`full_activation_time`, `full_activation_time_afrr`, `full_activation_time_mfrr`) nutzt deterministisch den `Δt`-clip ohne Negative oder unzulässige Reserve-Anforderung.
- Grenzwert-Effizienz `eta_charge` / `eta_discharge` nahe Null (z. B. `1e-9`) wird explizit getestet auf deterministische Behandlung (entweder clamp/bounding oder klare Verfahrens-Fehlerklasse, kein implizites Clampen in `NaN`/`inf`).
- Alert State startet bei definierter Frequenzabweichung und endet erst
  bei Rueckkehr in den Normalbereich.
- Recovery scheitert nach `max_recovery_time` mit operator-faehigem
  Status.
- Voluntary-aFRR-Bid wird reduziert, bis Worst-Case-Pruefung besteht.
- Intraday-Restauration respektiert Gate Closure und MTU.
- Replay-Fall "Reserveleistung passt, Energie reicht nicht" bricht vor
  Schedule-Aktivierung ab.

---

## Zusätzliche Testfälle (Simulation)

- Simultane Richtungsanforderungen:
  - bei `simultaneous_reserve_direction_allowed=false` muss bei gleichzeitigen
    Up-/Down-Anforderungen im selben Zeitschritt hart `ROBUST_POLICY_UNSUPPORTED`
    mit `POLICY_MISMATCH` resultieren.
  - bei `simultaneous_reserve_direction_allowed=true` wird bei Gleichzeitigkeit
    die gekoppelte Worst-Case-Rekursion über `worst_total_kwh_t` geprüft.

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
