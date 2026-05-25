# Plan: LER/FCR Reserve Robustness

**Dokumenttyp:** Slice-Skizze / offen
**Status:** Open - wartet auf Regelleistungs-/FCR-Produkttrigger
**Datum:** 2026-05-24
**Quelle-Repo:** Öffentliches Referenzmaterial – Referenzpublikation als fachlicher Ausgangspunkt, kein Code-Übernahmeplan.
Baltputnis et al. (2024), Journal of Energy Storage 102, 114082
**Bezug:**
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md),
[`../done/plan-RM-M4.md`](../done/plan-RM-M4.md),
[`../done/plan-RM-M5.md`](../done/plan-RM-M5.md),
[`../open/plan-price-forecast-adapters.md`](../open/plan-price-forecast-adapters.md)

---

## Ziel

`bess-ems` soll für FCR-/aFRR-Reservebereitstellung explizit prüfen
können, ob ein Asset oder Asset-Verbund unter Worst-Case-Aktivierung
die zugesagte Reserve ohne SOC-Verletzung liefern kann.

Der Slice schließt die fachliche Lücke zwischen:

- heute vorhandenen `ReserveBand`-Kapazitätsrestriktionen,
- Regelleistungsaktivierungen im Regelkreis,
- Day-Ahead-/Intraday-Optimierung,
- und dem robusten Betriebsfall für Limited Energy Reservoirs (LER).

Der technische Regelkreis bleibt Safety-first:

```text
Regelleistungsaktivierung -> Fahrplanauflösung -> State Machine -> Limiter -> Command
```

Die Robustheitsprüfung darf Markt- und Optimierungsentscheidungen
vorbereiten oder ablehnen, aber keine BMS-/PCS-Schutzfunktion ersetzen.

---

## Ausgangslage

Heute in `bess-ems` vorhanden:

- `ReserveBand` für FCR, aFRR und mFRR.
- FCR als symmetrisches Reserveprodukt.
- aFRR/mFRR als Up-/Down-Reserve.
- Regelleistungsaktivierung als priorisierte Eingangsquelle im
  Regelkreis.
- Optimierer, der Reservebänder von Lade-/Entladeleistung abzieht.
- Intraday-Reoptimierung und `PriceSeries`-basierte Fahrplanoptimierung.

Nicht explizit modelliert:

- LER-Status eines Assets.
- FCR-Alert-State-Erkennung und Reserve-Mode-Transitions.
- `t_min_FCR`, Full Activation Time und Recovery-Zeit als fachliche
  Constraints.
- Worst-Case-Energiebedarf für FCR/aFRR über einen Horizont.
- Intraday-SOC-Restauration als gezielte Massnahme zur
  Reservefähigkeit.
- Operator-fähige Fehlermodi für "Reserve zugesagt, aber robust nicht
  lieferbar".

---

## Arbeitsmodell

### Begriffe

- `LimitedEnergyReservoir`
  - Asset-Eigenschaft für Batterien, deren FCR-Lieferfähigkeit durch
    Energieinhalt und Recovery-Regeln begrenzt ist.

  - `ReserveRobustnessPolicy`
    - Produkt- und Standortregel für robuste Reserveprüfung.
  - Mindestfelder:
    - `asset_id`
    - `reserve_product`
    - `is_ler`
    - `soc_strategy` (`active` | `conservative`)
      - `active`: Berechnung nutzt den planmäßigen SOC-Rahmen (`soc_min_kwh`, `soc_max_kwh`) ohne Zusatzpuffer.
      - `conservative`: Berechnung nutzt Zusatzränder auf beiden SOC-Grenzen.
    - optional `conservative_soc_headroom_kwh` (default `0`, nur bei `soc_strategy=conservative`)
    - optional `conservative_soc_headroom_ratio` (default `0`, nur bei `soc_strategy=conservative`); Bereich `0..1`
    - Wenn kein `conservative_soc_headroom_kwh` gesetzt ist, wird bei `conservative` `conservative_soc_headroom_ratio` genutzt.
    - `t_min_fcr` (`FCR`-Pflichtfeld, falls FCR im gebuchten Portfolio aktiv)
      - FCR gilt als aktiv, wenn `reserve_product=FCR` im Request/Portfolio markiert
        ist oder eine der FCR-Zeitreihen (`fcr_up_kw_t`, `fcr_down_kw_t`) für den
        Horizont positive Werte enthält.
  - `full_activation_time` (`FCR`-Pflichtfeld bei aktivem FCR-Produkt; bei inaktivem FCR kann `0` gesetzt werden)
- optional `full_activation_time_afrr` (nur bei produktiv aktivem aFRR; bei gesetztem Wert `> 0`)
- optional `full_activation_time_mfrr` (nur bei produktiv aktivem mFRR; bei gesetztem Wert `> 0`)
  - optional `simultaneous_reserve_direction_allowed` (default `false`)
  - optional `restore_capability_up_kw` (optional, erlaubt zusätzliche obere Schranke für intraday Wiederherstellung in Up-Lade-Richtung)
  - optional `restore_capability_down_kw` (optional, erlaubt zusätzliche obere Schranke für intraday Wiederherstellung in Down-Entlade-Richtung)
  - optional `eta_charge` (default: `1.0`, falls nicht aus dem Assetmodell übernommen)
  - optional `eta_discharge` (default: `1.0`, falls nicht aus dem Assetmodell übernommen)
  - optional `self_discharge_mode` (Werte: `absolute_kwh_per_hour` | `relative_soc_per_hour`, optional nur relevant wenn `is_ler=true`)
  - optional `self_discharge_kwh_per_hour` (gültig nur bei `self_discharge_mode=absolute_kwh_per_hour`, optional nur bei `is_ler=true`)
  - optional `self_discharge_soc_per_hour` (gültig nur bei `self_discharge_mode=relative_soc_per_hour`, optional nur bei `is_ler=true`)
  - Für `is_ler=true` gilt nach Policy-/Asset-Auflösung: genau ein Verlustwert ist zulässig (`self_discharge_kwh_per_hour` oder `self_discharge_soc_per_hour`). Sind beide vorhanden, ist die Konfiguration als inkompatibel zu behandeln (`ROBUST_POLICY_UNSUPPORTED`).
  - `max_recovery_time` (harte obere Grenze für die Wiederherstellungsdauer in Minuten; bei aktivem Restore-Pfad erforderlich, sonst darf `0` gesetzt werden)
  - `intraday_gate_closure` (optional, Default `0`, wird für Restore-gesteuerte Läufe benötigt)
  - `intraday_preparation_time` (optional, Default `0`, wird für Restore-gesteuerte Läufe benötigt)
  - `minutes_until_next_restore_window_start` (optional, `>= 0`)
  - `minutes_until_next_restore_window_end` (optional, wenn ein Fenster bekannt ist)
  - `market_time_unit` (Kompatibilitäts-/Zukunfts-Hook; im ersten Slice fest auf `minute`)
  - Zeiteinheiten (verbindlich, alle in Minuten):
    - `t_min_fcr`, `full_activation_time`, `full_activation_time_afrr`,
      `full_activation_time_mfrr`, `max_recovery_time`, `intraday_gate_closure`,
      `intraday_preparation_time`, `minutes_until_next_restore_window_start`,
      `minutes_until_next_restore_window_end`
    - `market_time_unit` muss im ersten Slice `minute` sein; andere MTU-Werte sind
      reserviert für spätere 15-/30-Minuten-Produktlogik und aktuell nicht erlaubt.
    - `resolution_minutes` und diese Policy-Felder müssen kompatibel sein.
  - Wirkungsgrade:
    - Wird `eta_charge`/`eta_discharge` in der Policy nicht gesetzt, sind sie aus dem Assetmodell zu lesen.
    - Falls gesetzt, muss gelten: `eta_min <= eta_charge <= 1` und `eta_min <= eta_discharge <= 1`.
    - `eta_min` ist ein verbindlicher, kleiner positiver Toleranzwert: `eta_min = 1e-6`.
    - `eta_charge < eta_min` oder `eta_discharge < eta_min` führt zu `ROBUST_POLICY_UNSUPPORTED`.
  - Selbstentladung:
  - Für `is_ler=true` gilt: Es muss nach Policy-/Asset-Auflösung ein effektiver LER-Verlustwert
    eindeutig bestimmt werden (`self_discharge_kwh_per_hour` oder `self_discharge_soc_per_hour`).
    - Policy-Werte gelten als vorrangig.
    - Wenn die Policy keinen Wert liefert, darf als Fallback ein Asset-Mindestwert verwendet werden.
  - Für `is_ler=false` können die Werte gesetzt sein oder entfallen; fehlt jeder Wert, wird kein zusätzlicher LER-spezifischer Verlustfaktor verwendet.
  - `self_discharge_kwh_per_hour` und `self_discharge_soc_per_hour` können bei Bedarf aus dem Assetmodell geerbt werden.
  - `self_discharge_mode=absolute_kwh_per_hour`:
      - für `is_ler=true`: `self_discharge_kwh_per_hour` ist Pflichtfeld (`>= 0`),
      - `self_discharge_soc_per_hour` darf nicht gesetzt sein.
  - `self_discharge_mode=relative_soc_per_hour`:
      - für `is_ler=true`: `self_discharge_soc_per_hour` ist Pflichtfeld (`>= 0`, Anteil SOC-Verlust pro Stunde),
      - `self_discharge_kwh_per_hour` darf nicht gesetzt sein.
    - Verlustberechnung ist deterministisch: `self_discharge_loss_kwh_t = soc_t * self_discharge_soc_per_hour * Δt`,
      wobei `soc_t` der SOC zu Beginn der Zeitscheibe ist und `Δt = resolution_minutes / 60`.
  - Restore-Leistung:
    - Wenn gesetzt, begrenzt `restore_capability_up_kw` den maximal nutzbaren Restore-Leistungsfluss der Up-Richtung (Lade-Richtung) und `restore_capability_down_kw` die Down-Richtung (Entlade-Richtung).
    - `restore_capability_up_kw` und `restore_capability_down_kw` müssen, falls gesetzt, strikt `> 0` sein.
    - Die Restore-Richtung ist die Wiederherstellung nach einer Reserveaktivierung:
      Up-Reserve entlädt, Up-Restore lädt die dadurch fehlende Energie zurück;
      Down-Reserve lädt, Down-Restore entlädt überschüssige Energie wieder.
    - Wenn ein Feld fehlt:
      - wird im ersten Slice zuerst auf eine technische Basisgrenze aus der Asset-/Reserveband-Konfiguration zurückgegriffen:
        - Up-Restore (Lade-Richtung): verfügbare Ladeleistung aus Asset-Ladelimit,
          bereits gebundener Fahrplanleistung und aktiven Reserveband-Abzügen.
        - Down-Restore (Entlade-Richtung): verfügbare Entladeleistung aus Asset-Entladelimit,
          bereits gebundener Fahrplanleistung und aktiven Reserveband-Abzügen.
        - Bei Co-Location zusätzlich die im Request bereits validierten Import-/Export- und
          Netzanschlusspunktgrenzen.
      - ist diese Basisgrenze nicht verfügbar und ist ein Restore-Szenario aktiv, ist der Request mit `ROBUST_POLICY_UNSUPPORTED` (`POLICY_MISMATCH`) zu blockieren.
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
  - Effektivzeiten je Produkt:
  - `full_activation_time_afrr_eff = full_activation_time_afrr` (bei aFRR-Aktivierung; sonst `0`)
  - `full_activation_time_mfrr_eff = full_activation_time_mfrr` (bei mFRR-Aktivierung; sonst `0`)
  - Harte Schema-Validierung:
  - `self_discharge_mode` darf, soweit gesetzt, **nur** `absolute_kwh_per_hour` oder `relative_soc_per_hour` sein; alles andere -> `ROBUST_POLICY_UNSUPPORTED`.
  - `self_discharge_kwh_per_hour` darf nicht negativ sein.
  - `self_discharge_soc_per_hour` muss in `[0,1]` liegen (als Anteil pro Stunde).
  - Für `is_ler=true` ist ein effektiver LER-Wert erforderlich; fehlen beide Werte nach Policy/Asset-Auflösung,
    oder sind beide gesetzt, gilt `ROBUST_POLICY_UNSUPPORTED`.
  - `minutes_until_next_restore_window_start` ist optional, muss aber bei
    aktiviertem Restore-Szenario `>= 0` sein.
  - Wenn `minutes_until_next_restore_window_start` gesetzt ist, muss
    `minutes_until_next_restore_window_end` gesetzt und
    `minutes_until_next_restore_window_end > minutes_until_next_restore_window_start` sein.
  - Wenn `minutes_until_next_restore_window_start` `null` ist, liegt zum Bewertungszeitpunkt
    kein gültiges Intraday-Restorefenster vor.
  - `market_time_unit` muss exakt `minute` sein und mit `resolution_minutes` konsistent sein.
  - `t_min_fcr`, `full_activation_time` und `max_recovery_time` müssen bei aktivem Anwendungsfall streng `> 0` sein; bei inaktivem FCR bzw. deaktiviertem Restore-Pfad darf `0` gesetzt werden.
  - `full_activation_time_afrr` und `full_activation_time_mfrr` müssen bei produktiv aktivem aFRR bzw. mFRR strikt `> 0` sein.
  - Ist das jeweilige Produkt aktiviert und der Wert fehlt oder ist `0`, gilt `ROBUST_POLICY_UNSUPPORTED` (`POLICY_MISMATCH`).
  - `simultaneous_reserve_direction_allowed` ist optional-boolean; fehlt/`false` wird als Default `false` interpretiert.
  - `simultaneous_reserve_direction_allowed=true` ist in Kombination mit nicht-linearer Kopplung nur mit klarer
    Reihenfolgeimplementierung in der Worst-Case-Rekursion erlaubt.

- `ReserveEnergyEnvelope`
  - Zeitschrittweise Worst-Case-Energiehuelle für Up- und Down-Richtung.
  - Mindestfelder pro Schritt:
    - `timestamp_utc`
    - `resolution_minutes` (int, muss mit Policy/Horizont konsistent sein)
    - `worst_up_kw`
    - `worst_down_kw`
    - `worst_up_kwh`
    - `worst_down_kwh`
    - `available_up_kwh`
    - `available_down_kwh`
    - `step_status`
    - `limiting_reason`
    - `required_up_kwh` (optional, für Restore-Planung)
    - `required_down_kwh` (optional, für Restore-Planung)
  - Optional:
    - `restore_capability_up_kw_step`:
      - optionales per-step Override in kW für den Up-Wiederherstellungszweig auf Basis der betrachteten Reservezelle;
      - muss `> 0` sein.
      - wird in der Restore-Rekonstruktion als harte Wiederherstellungskapazität verwendet, falls gesetzt.
      - bei Wert `<= 0` oder fehlendem Wert wird kein Override angenommen.
    - `restore_capability_down_kw_step`:
      - optionales per-step Override in kW für den Down-Wiederherstellungszweig auf Basis der betrachteten Reservezelle;
      - muss `> 0` sein.
      - wird in der Restore-Rekonstruktion als harte Wiederherstellungskapazität verwendet, falls gesetzt.
      - bei Wert `<= 0` oder fehlendem Wert wird kein Override angenommen.
    - `required_activation_minutes`:
      - optionales Feld in Minuten als Operatorkontext.
      - dient in der Erstimplementierung rein auditierbar und ist nicht verpflichtender
        Steuerparameter.
      - Wird das Feld später Steuerparameter, braucht es einen expliziten Migrationsschritt:
        Validierung, Persistenz-/API-Ausgabe und Konsumenten müssen dann gemeinsam
        eingeführt werden; bis dahin darf kein Laufverhalten davon abhängen.

- `AlertStateTimeline`
  - Zeitreihe für FCR-Alert-State-Status, Reserve Mode,
    Recovery-Status und `t_min_fcr`-Fortschritt.

- `ReserveRobustnessResult`
  - Ergebnis einer Prüfung:
  - `result_status`:
    - `ROBUST_OK`
    - `ROBUST_NEEDS_INTRADAY_RESTORE`
    - `ROBUST_INFEASIBLE`
    - `ROBUST_SOURCE_DATA_MISSING`
    - `ROBUST_POLICY_UNSUPPORTED`
  - `limiting_reason_code`:
    - `SOC_LIMIT`, `RESERVE_CAPACITY`, `RECOVERY_TIMEOUT`, `POLICY_MISMATCH`,
      `SOURCE_DATA_MISSING`, `NO_RECOVERY_PATH`, `INTRADAY_GATE_CLOSED`
  - `status_description` (optional, menschenlesbar)
  - `action` (optional, maschinenlesbares Operator-Token; erster Wert:
    `intraday_restore_required`)

### Worst-Case-Energie

Der Slice übernimmt die fachliche Idee aus der Referenzpublikation, nicht den
Python-Code:

- verfügbare Energie für positive Reserve:
  - SOC oberhalb Mindest-SOC, inklusive Entladeeffizienz
- verfügbare Energie für negative Reserve:
  - freier SOC-Raum bis Max-SOC, inklusive Ladeeffizienz
- Worst-Case-Up:
  - FCR-Energiebedarf
  - aFRR-/mFRR-Up-Kapazität und Aktivierungsenergie
  - bereits geplante Intraday-/Fahrplanpositionen
  - Selbstentladung, falls konfiguriert
- Worst-Case-Down:
  - FCR-Energiebedarf
  - aFRR-/mFRR-Down-Kapazität und Aktivierungsenergie
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
  - Vor der Rechnung ist hart zu validieren:
    - Alle Reserve- und Alphafelder pro Zeitschritt sind endlich und nicht negativ.
    - `fcr_*`, `afrr_*`, `mfrr_*` und die optionalen `required_*`-Felder sind `>= 0`.
    - Bei `alpha_*` gilt `0 <= alpha_* <= 1` mit Toleranz; `alpha_* < 0` oder `> 1` führt auf
      `ROBUST_POLICY_UNSUPPORTED` (`POLICY_MISMATCH`).
  - Bereich: `0 <= alpha_* <= 1` (numerisch stabiler Toleranzbereich in der Umsetzung, z. B. `1e-9`).
 - `Δt = resolution_minutes / 60` (in Stunden).
  - Sofern `simultaneous_reserve_direction_allowed=false`, wird konservativ angenommen, dass Up- und Down-Aktivierung nicht
    simultan in derselben Zeitscheibe als Primärfall auftreten.
  - Sofern `simultaneous_reserve_direction_allowed=true`, werden simultane Richtungen
    im selben Schritt wie folgt gekoppelt:
    - `worst_total_kwh_t = worst_up_kwh_t + worst_down_kwh_t`
    - `worst_total_kw_t = worst_total_kwh_t / Δt`
    - `worst_total_*` dient als auditierbare kombinierte Kennzahl für UI/Replay; für die harte Verifikation wird die sequentielle Reihenfolge explizit definiert:
      1) Up-Branch auf `worst_up_kwh_t` (Absenkung),
      2) anschließend Down-Branch auf `worst_down_kwh_t` auf dem SOC nach Up.
      - Im sequentiellen Pfad wird Up zuerst auf den Schrittzustand angewendet und Down nur auf dem danach aktualisierten SOC geführt.
      - Up zuerst ist der konservative Default, weil die LER-Worst-Case-Annahme auf
        der Entlade-/Up-Seite die kritischere Untergrenze zuerst belastet.
    - Diese Kopplung ist bewusst konservativ und vollständig deterministisch.
  - Wenn simultanes Gegensignal laut Produktdefinition aktiv ist, aber `simultaneous_reserve_direction_allowed=false`,
    gilt hart `ROBUST_POLICY_UNSUPPORTED` mit `POLICY_MISMATCH`.
- FCR-Worst-Case-Konsistenz bei Mindestzeit:
  - Für FCR wird die Mindestaktivierungszeit mit zwei getrennten Zustandsgrößen geführt:
    - `fcr_remaining_envelope_t` ist die konservative Worst-Case-Hülle in Minuten.
      Für die Prüfung am Entscheidungszeitpunkt gilt pro Schritt:
      `fcr_remaining_envelope_t = t_min_fcr`, unabhängig vom aktuellen Alert-Status.
    - `fcr_remaining_tracking_t` ist der Alert-Tracking-Zustand in Minuten und dient
      nur Laufzeit-Replay, Operator-Ausgabe und fortlaufender Alert-Rekursion.
    - Bei Schrittübergang in Alert wird für die Tracking-Ansicht
      `fcr_remaining_tracking_t` auf `t_min_fcr` gesetzt.
    - Während fortlaufender Alert-Phasen:
      `fcr_remaining_tracking_{t+1} = max(0, fcr_remaining_tracking_t - Δt*60)`.
    - Für die Worst-Case-Hülle ist die zu reservierende FCR-Energie auf
      `min(Δt, fcr_remaining_envelope_t/60)` h zu skalieren.
    - Mit der Tracking-Logik wird bei kleinen `Δt` die volle Mindestdauer konservativ über Folgeintervalle berücksichtigt.
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
    - `soc_min_eff_kwh <= soc_t <= soc_max_eff_kwh`, sonst `ROBUST_INFEASIBLE`
      mit `limiting_reason_code=SOC_LIMIT`.
  - `soc_t^{eff} = soc_t - self_discharge_loss_kwh_t`.
- Verfügbare Energiemengen für die `ReserveEnergyEnvelope` sind branch-spezifisch:
  - `available_up_kwh_t` wird pro Schritt aus dem Up-Branch-Zustand nach geplanter
    Fahrplanwirkung berechnet:
    `available_up_kwh_t = max(0, soc_plan_up_t - soc_min_eff_kwh) * eta_discharge`.
  - `available_down_kwh_t` wird pro Schritt aus dem Down-Branch-Zustand nach geplanter
    Fahrplanwirkung berechnet:
    `available_down_kwh_t = max(0, soc_max_eff_kwh - soc_plan_down_t) / eta_charge`.
  - Es gibt kein persistiertes globales `soc_plan_base_t` für diese Envelope-Felder;
    `soc_up_t` und `soc_down_t` aus der Rekursion sind die maßgeblichen Zustände.
- Worst-Case-Energie je Richtung (kWh):
  - `fcr_remaining_envelope_t`-abhängiger FCR-Term:
    - `fcr_term_up_t = min(Δt, fcr_remaining_envelope_t/60) * fcr_up_kw_t`
    - `fcr_term_down_t = min(Δt, fcr_remaining_envelope_t/60) * fcr_down_kw_t`
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
  - Effektive Restore-Kapazität je Richtung berechnet sich per Coalesce:
    per-step Envelope-Override > Policy-Skalar > technischer Default.
    - `restore_capability_up_t_fallback` ist der deterministische technische Default je Schritt
      (z. B. aus Asset- oder Reservebandgrenzen), nicht aus der bereits erzeugten
      `ReserveEnergyEnvelope`.
    - `restore_capability_down_t_fallback` ist der analoge deterministische technische Default je Schritt
      (z. B. aus Asset- oder Reservebandgrenzen), nicht aus der bereits erzeugten
      `ReserveEnergyEnvelope`.
    - `restore_capability_up_policy_t = if ReserveRobustnessPolicy.restore_capability_up_kw is set then ReserveRobustnessPolicy.restore_capability_up_kw else restore_capability_up_t_fallback`
    - `restore_capability_down_policy_t = if ReserveRobustnessPolicy.restore_capability_down_kw is set then ReserveRobustnessPolicy.restore_capability_down_kw else restore_capability_down_t_fallback`
    - `restore_capability_up_source_t = if ReserveEnergyEnvelope.restore_capability_up_kw_step is set then ReserveEnergyEnvelope.restore_capability_up_kw_step else restore_capability_up_policy_t`
    - `restore_capability_down_source_t = if ReserveEnergyEnvelope.restore_capability_down_kw_step is set then ReserveEnergyEnvelope.restore_capability_down_kw_step else restore_capability_down_policy_t`
    - `restore_capability_up_t = if restore_shortfall_up_kwh > 0 then restore_capability_up_source_t else 0`
    - `restore_capability_down_t = if restore_shortfall_down_kwh > 0 then restore_capability_down_source_t else 0`
  - Richtungsbasierte Mindest-Rekonstruktionsdauer:
    - Auswertungsreihenfolge ist verbindlich:
      1. Wenn ein aktiver Branch (`restore_shortfall_*_kwh > 0`) keine positive
         Restore-Kapazität hat, endet die Bewertung sofort mit
         `ROBUST_INFEASIBLE` und `limiting_reason_code=NO_RECOVERY_PATH`.
      2. Erst danach werden `required_recovery_minutes_*` berechnet und gegen
         `max_recovery_time` verglichen.
    - `required_recovery_minutes_up = if restore_capability_up_t > 0 and restore_shortfall_up_kwh > 0 then restore_shortfall_up_kwh / restore_capability_up_t * 60 else +inf`
    - `required_recovery_minutes_down = if restore_capability_down_t > 0 and restore_shortfall_down_kwh > 0 then restore_shortfall_down_kwh / restore_capability_down_t * 60 else +inf`
    - `required_recovery_minutes = max(required_recovery_minutes_up, required_recovery_minutes_down)`
    - `restore_capability_used` ist die effektive Restore-Kapazität (`restore_capability_up_t`
      oder `restore_capability_down_t`), die den maximalen `required_recovery_minutes`-Wert
      bestimmt; bei Gleichstand deterministisch die Up-Richtung.
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
  müssen pro Check konsistent sein. Im ersten Slice heißt konsistent:
  `market_time_unit=minute` und alle Zeitfelder sind Minutenwerte; andere MTU-Werte
  führen zu `ROBUST_POLICY_UNSUPPORTED`.

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
- `t_min_fcr` zählt nur in zulässigen Betriebsfenstern.
- `full_activation_time` ist in derselben Zeiteinheit wie `t_min_fcr` anzugeben;
  produkt-spezifische Werte `full_activation_time_afrr` und
  `full_activation_time_mfrr` verwenden ebenfalls dieselbe Einheit.
- `intraday_preparation_time` gilt als Mindestvorlauf für den Restore-Einstieg:
  Ein Restore-Vorschlag darf nur erzeugt werden, wenn vor dem geplanten Ausführungsfenster
  mindestens die Vorlaufzeit eingehalten ist.
- Reserve Mode darf nur aktiviert werden, wenn der resultierende
  Robustheitszustand operator-fähig erklaert wird.
- Recovery endet erst, wenn Worst-Case-Lieferfähigkeit wieder
  hergestellt ist.
- Wird `max_recovery_time` erreicht ohne Wiederherstellung, endet die
  Recovery mit `ROBUST_INFEASIBLE` und `limiting_reason_code=RECOVERY_TIMEOUT`.
- Infeasible Recovery führt zu einem klaren Status, nicht zu stiller
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
  - Vor der Berechnung gilt hart:
    - `required_restore_up_kwh_t` und `required_restore_down_kwh_t` müssen `>= 0` sein.
    - Wird ein negativer Rohwert übermittelt (`coalesce` nicht möglich), gilt `ROBUST_POLICY_UNSUPPORTED` mit `POLICY_MISMATCH` als harte Vorvalidierung.
 - Wenn `restore_up_kwh_t > 0` und `restore_down_kwh_t > 0` im selben Schritt gilt:
   - `ReserveRobustnessResult` wird mit `ROBUST_INFEASIBLE` und `limiting_reason_code = NO_RECOVERY_PATH` geführt (ein Batteriekorridor kann pro Schritt nicht zugleich laden und entladen).
 - Sonst:
   - Laden, wenn `restore_up_kwh_t > 0` (Wird Worst-Case-Up nicht gedeckt).
   - Entladen, wenn `restore_down_kwh_t > 0` (Wird Worst-Case-Down nicht gedeckt).
 - harte Begrenzung durch Asset-Leistung, Reservebänder,
   SOC-Grenzen und Gate-Closure-Zeit.
 - Restore ist nur dann ausgabe- und ausführungspfadfähig, wenn aktuell ein
   ausreichendes Wiederherstellungsfenster offen ist und `intraday_gate_closure`
   nicht aktiv ist.
 - keine automatische Marktorder im Domain- oder Regelkreis.

Der Slice definiert nur EMS-seitig den Bedarf und einen Fahrplanvorschlag;
Order-Routing oder Börsenanbindung bleibt außerhalb.

---

## Scope bei Aktivierung

### Phase 1: Fachvertrag und Golden Fixtures

- Fachliche Portierung der relevanten Formeln als Spezifikation:
  - verfügbare Up-/Down-Energie
  - FCR-Worst-Case-Huelle
  - kombinierte FCR/aFRR-Energiehuelle
  - LER-Alert-/Recovery-Regeln
- Replay-/Golden-Fixtures aus synthetischen Szenarien:
  - 6h volle FCR-Aktivierung
  - 30min `t_min_fcr`-Erfüllung
  - Alert-State-Ende mit Recovery
  - zu hohe aFRR-Up-Reserve bei niedrigem SOC
  - zu hohe aFRR-Down-Reserve bei hohem SOC
- Keine Abhängigkeit auf Python oder Excel im Produktpfad.

### Phase 2: Domain- und Application-Modell

- Domain-/Application-Typen:
  - `ReserveRobustnessPolicy`
  - `ReserveEnergyEnvelope`
  - `AlertStateTimeline`
  - `ReserveRobustnessResult`
- Application-Port:
  - `IReserveRobustnessCheck`
  - optional später `IReserveRestorationPlanner`
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

- Precheck für Schedule-Optimierung:
  - Reserveband darf nicht nur leistungsmässig passen, sondern muss
    energie-/SOC-robust sein.
- Intraday-Restauration:
  - optionaler Optimierungsinput für benötigte Up-/Down-Energiekorrektur.
- Ergebnisverhalten:
  - Robustheitsverletzung wird über bestehende `OptimizationSolverStatus` +
    strukturierte `TerminationCode` + harte `CanExecute`-Sperren ausgedrückt.
  - Mapping-Matrix in diesem Plan ist die autoritative gemeinsame Matrix für
    [`plan-market-colocation-model.md`](plan-market-colocation-model.md):

    | Ergebnisklasse                                  | OptimizationSolverStatus | TerminationCode (Beispiel)                                           | CanExecute |
    | --- | --- | --- | --- |
    | Gültiger Plan/Plan verwendbar                    | `Optimal` oder `Feasible` | bestehende Erfolgs-Codes, z. B. `or-tools-optimal` oder `or-tools-feasible-not-proven-optimal` | `true` |
    | Solver-seitige mathematische Infeasibility       | `Infeasible`             | bestehender Solver-Code, z. B. `or-tools-infeasible` | `false` |
    | Time Limit ohne ausführbaren Plan                 | `TimeLimit`              | bestehender Timeout-Code, z. B. `or-tools-time-limit` | `false` |
    | Iteration Limit ohne ausführbaren Plan            | `IterationLimit`         | bestehender Iterations-Code, sofern vom Solver geliefert | `false` |
    | Reiner Rechenfehler/Solverfehler                  | `Failed`                 | Solver-spezifische harte Codes, z. B. `or-tools-abnormal`, `or-tools-model-invalid`, `or-tools-not-solved` | `false` |
    | Konfigurationsfehler (`CONFIG_*`)                | `Failed`                 | `config-invalid` oder `config-inconsistent` | `false` |
    | Schematafehler (`SCHEMA_INCONSISTENT`)          | `Failed`                 | `schema-inconsistent` | `false` |
    | Restore erforderlich, Plan nicht ausführbar bis Restore erfolgt | eigentliches Solverergebnis (`Optimal` oder `Feasible`) | `reserve-robustness-needs-restore` | `false` |
    | Robustheits-/Reserve-Blockade                      | `Failed`                 | `reserve-robustness-*` | `false` |
    | Harte Source-/Policy-Abweisungen außerhalb Produktbereichs | `Failed`          | `source-*`/`policy-*` falls eingeführt | `false` |
  - Die verwendeten `TerminationCode` für robustheitsbezogene Sperren sind
    `reserve-robustness-needs-restore`, `reserve-robustness-infeasible`,
    `reserve-robustness-recovery-timeout`, `reserve-robustness-source-missing`,
    `reserve-robustness-policy-unsupported`.
  - Keine impliziten Reserveverletzungen im produzierten Fahrplan.
  - Bestehender Pfad ohne LER/FCR-Robustheit bleibt unverändert.
- Ergebnis-/Run-Mapping (verbindlich):
  - `reserve_robustness_status = ReserveRobustnessResult.result_status`.
  - `reserve_robustness_limiting_reason = ReserveRobustnessResult.limiting_reason_code`.
  - `HasUsableSolution` wird in der aktuellen Codebasis weiterhin aus
    `OptimizationSolverStatus.{Optimal,Feasible}` abgeleitet; die produktive
    Ausführungssteuerung nutzt darüber hinaus das neue harte Gate `CanExecute`.
  - `CanExecute` als hartes Persistenzfeld erfordert eine Domain-Constructor-Migration
    für `OptimizationRun`: bestehender immutable Konstruktor und
    `OptimizationRun`-Wire/DB-Pfad müssen gemeinsam erweitert werden (`can_execute`
    in Wireobjekt + Datenhaltung + Mapping), damit das Feld die Invariante
    korrekt trägt.
  - Diese Migration ist ein eigener Pre-Slice
    [`Domain-Migration OptimizationRun.CanExecute`](plan-domain-migration-optimization-run-can-execute.md)
    und umfasst alle Konstruktor-Aufrufer, Repository-Implementierungen, Tests/Fixtures,
    Proto-/API-/Wire-Mappings sowie die Umstellung aller Dispatch-/Scheduler-/API-Konsumenten
    von `HasUsableSolution` auf `HasUsableSolution && CanExecute`.
  - Bei `reserve_robustness_status != ROBUST_OK` ist
    `reserve_robustness_limiting_reason` als Pflichtfeld im `OptimizationRun`-`TerminationDetail`
    zu persistieren, inkl. optionaler `status_description`, damit Operator-/Replay-Sichten die Ursache eindeutig nachvollziehen.
  - Aggregationsregel für den gemeinsamen Cross-Slice-Vertrag:
    `CanExecute = robust_ok && config_ok && schema_ok && source_ok && solver_result_executable`.
    Jeder Slice darf `CanExecute` nur von `true` auf `false` ziehen; kein Slice darf
    ein durch einen anderen Slice gesetztes `false` wieder auf `true` setzen.
  - Für diesen Slice gilt `robust_ok = (reserve_robustness_status == ROBUST_OK)`;
    bei allen anderen Robustheitsstatuswerten ist `CanExecute=false`.
  - `CanExecute` ist harte Ausführungsbedingung im Dispatcher/Scheduler:
    `OptimizationSolverStatus.Feasible` bei `CanExecute=false` gilt weiterhin als nicht auszuführen.
  - `ROBUST_OK` => keine zusätzliche Hard-Stop-Sperre.
  - `ROBUST_INFEASIBLE` mit `limiting_reason_code=RESERVE_CAPACITY` oder
    `SOC_LIMIT` => `OptimizationSolverStatus.Failed` mit
    `TerminationCode=reserve-robustness-infeasible`.
  - `ROBUST_INFEASIBLE` mit `limiting_reason_code=INTRADAY_GATE_CLOSED` oder
    `NO_RECOVERY_PATH` => `OptimizationSolverStatus.Failed` mit
    `TerminationCode=reserve-robustness-infeasible`.
  - `ROBUST_NEEDS_INTRADAY_RESTORE` =>
    - bei verfügbarem Restore-Fenster: Optimierung bleibt lauffähig,
      der Lauf wird mit dem eigentlichen Solverstatus (`Optimal` oder `Feasible`) persistiert,
      `TerminationCode=reserve-robustness-needs-restore`, `CanExecute=false`
      und Operator-Hinweis `action=intraday_restore_required`.
    - bei geschlossenem Gate / keinem Wiederherstellungsfenster entsteht nach der
      verbindlichen Entscheidungslogik kein `ROBUST_NEEDS_INTRADAY_RESTORE`, sondern
      `ROBUST_INFEASIBLE` mit `limiting_reason_code=INTRADAY_GATE_CLOSED`.
  - `ROBUST_INFEASIBLE`:
    - bei `limiting_reason_code=RECOVERY_TIMEOUT` =>
      `OptimizationSolverStatus.Failed` mit
      `TerminationCode=reserve-robustness-recovery-timeout`,
      `TerminationDetail=reason=RECOVERY_TIMEOUT`.
      Für robustheitsbezogene Codes ist `TerminationDetail` strukturiert:
      `reason=<LIMITING_REASON_CODE>` und optional `solver_code=<original-code>`.
      Messwertartige Details dürfen als weitere `key=value`-Paare ergänzt werden.
      Reine menschenlesbare Detailstrings bleiben nur bei bestehenden Solver-/
      Laufzeitcodes zulässig.
    - sonst => `OptimizationSolverStatus.Failed` mit
      `TerminationCode=reserve-robustness-infeasible`.
  - `ROBUST_SOURCE_DATA_MISSING` => `OptimizationSolverStatus.Failed`
    mit `TerminationCode=reserve-robustness-source-missing`.
  - `ROBUST_POLICY_UNSUPPORTED` => `OptimizationSolverStatus.Failed` mit
    `TerminationCode=reserve-robustness-policy-unsupported`.
- `ROBUST_OK` wird mit dem eigentlichen Optimizerergebnis (`Optimal`/`Feasible`) persistiert.
- `ROBUST_NEEDS_INTRADAY_RESTORE` bewahrt den eigentlichen Solverstatus (`Optimal`
  oder `Feasible`) und wird
  mit `TerminationCode=reserve-robustness-needs-restore`, `CanExecute=false`
  und explizitem Restore-Hinweis persistiert.

### Phase 4: Operator- und Replay-Sicht

- API-/UI-Ausgabe für:
  - aktuelle Reservefähigkeit
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

- Kein direkter Python-Port aus der Referenzpublikation.
- Keine Excel-Konfiguration im Produktpfad.
- Keine Börsenorder-Ausführung.
- Keine Zertifizierung als FCR-/aFRR-Präqualifikationsnachweis.
- Kein Ersatz für BMS-/PCS-Schutzfunktionen.
- Keine Änderung der bestehenden Vorzeichenkonvention.
- Kein Forecast-Adapter; Quellenbeschaffung bleibt
  [`plan-price-forecast-adapters.md`](../open/plan-price-forecast-adapters.md).

---

## Liefergegenstaende bei Aktivierung

1. ADR oder Architektur-Schärf für LER/FCR-Robustheitsmodell.
2. Domain-/Application-Typen für Robustheit, Alert State und Recovery.
3. Deterministische Golden-Fixtures für Worst-Case-Energiehuellen.
4. Application-Port `IReserveRobustnessCheck`.
5. Optimierungs-Precheck für energie-robuste Reservebänder.
6. Intraday-Restaurationsbedarf als strukturierter Optimierungsinput.
7. Operator-fähige Fehlercodes, Metriken und API-Ausgabe inkl. `CanExecute`.
8. Dokumentation der Grenzen: keine Präqualifikation, keine
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
  Excel-Abhängigkeit im `bess-ems`-Testpfad.

## Definition of Done (DoD)

- [ ] Reserve-Robustheitskontrakt ist formalisiert:
  - `ReserveRobustnessPolicy`, `ReserveEnergyEnvelope`, `ReserveRobustnessResult`,
    `AlertStateTimeline` fachlich vollständig definiert,
  - Last-Validierung/Status- und Übergänge (Alert/Reserve/Recovery) dokumentiert.
- [ ] Berechnungslogik ist implementiert (oder vertraglich festgehalten) für:
  - Worst-Case-Bandbrechung je Schritt (UP/DOWN),
  - FCR `t_min_fcr`-Dynamik,
  - LER-Zustandsbezug/`is_ler`-Pfad,
  - konservative `simultaneous_reserve_direction_allowed`-Semantik.
- [ ] Wiederherstellungslogik ist implementiert:
  - `ROBUST_NEEDS_INTRADAY_RESTORE`-Routing,
  - Intraday-Gate-/Fensterlogik,
  - harte `CanExecute`-Abgrenzung im Dispatcher.
- [ ] Ergebnis-Mapping ist aktiv:
  - `reserve_robustness_status` + `reserve_robustness_limiting_reason`,
  - Mapping auf `OptimizationSolverStatus`/`TerminationCode` inkl. `CanExecute=false` bei Nicht-`ROBUST_OK`.
- [ ] Liefergegenstände bei Aktivierung umgesetzt:
  - ADR/Schärfung,
  - Domain-/Application-Typen,
  - Optimierungsintegration + Golden-Fixtures,
  - Operator-/Replay-Output inkl. Restore-Hinweis.
- [ ] Akzeptanzkriterien und Testideen sind grün inklusive Replay-/Golden-Fixtures ohne Netzwerk/Excel/Python.

---

## Testideen

- `E_avail_up/down` bei SOC-Min/Max und Effizienz < 1.
- Selbstentlade-Schemafehler (explizit):
  - `self_discharge_mode=invalid` (`x_per_hour` unbekannt) → `ROBUST_POLICY_UNSUPPORTED`.
  - `is_ler=true` und beide Moduswerte gesetzt (`self_discharge_kwh_per_hour` und `self_discharge_soc_per_hour`) → `ROBUST_POLICY_UNSUPPORTED`.
  - `is_ler=true` und kein Moduswert gesetzt (beide optional Felder leer/nicht gesetzt) → `ROBUST_POLICY_UNSUPPORTED`.
  - negative/überschüssige Werte (`self_discharge_kwh_per_hour < 0`, `self_discharge_soc_per_hour < 0` oder `> 1`) → `ROBUST_POLICY_UNSUPPORTED`.
- Scheduler-/Dispatcher-Guard (hart):
  - Bei `OptimizationSolverStatus` (`Optimal|Feasible`) + `CanExecute=false` darf der Lauf in keiner API/Dispatcher-Route in den ausführbaren Zustand übergehen.
- `ROBUST_NEEDS_INTRADAY_RESTORE`-Pfad bewahrt den eigentlichen Solverstatus
  (`Optimal` oder `Feasible`) und erzeugt `CanExecute=false`,
  `reserve_robustness_status=ROBUST_NEEDS_INTRADAY_RESTORE` und
  `action=intraday_restore_required` (optional ergänzt durch `status_description`) im
  Replay-/Operator-Output.
- `market_time_unit` abseits von `minute` → `ROBUST_POLICY_UNSUPPORTED`.
- Auflösungsfelder kleiner oder gleich 0 (`resolution_minutes`) → `ROBUST_POLICY_UNSUPPORTED`.
- Bei aktivem FCR gilt `t_min_fcr`/`full_activation_time <= 0` → `ROBUST_POLICY_UNSUPPORTED`.
- Bei aktivem Restore-Pfad gilt `max_recovery_time <= 0` → `ROBUST_POLICY_UNSUPPORTED`.
- `full_activation_time_afrr` / `full_activation_time_mfrr` werden nur bei gesetztem Feldwert geprüft:
  - gesetzt und `<= 0` → `ROBUST_POLICY_UNSUPPORTED`.
- `conservative_soc_headroom` außerhalb `0..1` (ratio) bzw. `<0` (kwh) → `ROBUST_POLICY_UNSUPPORTED`.
- FCR-Worst-Case für nicht-LER: volle FCR-Leistung über Horizont.
- LER-konservative FCR-Huelle mit `t_min_fcr`.
- Voller Aktivierungszeitraum deutlich größer als Horizon (`full_activation_time`, `full_activation_time_afrr`, `full_activation_time_mfrr`) nutzt deterministisch den `Δt`-clip ohne Negative oder unzulässige Reserve-Anforderung.
- Grenzwert-Effizienz `eta_charge` / `eta_discharge` nahe Null (z. B. `1e-9`) wird explizit als harte Verweigerung
  auf `ROBUST_POLICY_UNSUPPORTED` getestet (`eta_min`-Grenzprüfung), ohne implizite `clamp`-/`bounding`-Logik.
- Alert State startet bei definierter Frequenzabweichung und endet erst
  bei Rückkehr in den Normalbereich.
- Recovery scheitert nach `max_recovery_time` mit operator-fähigem
  Status.
- Voluntary-aFRR-Bid wird reduziert, bis Worst-Case-Prüfung besteht.
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
    die sequentielle Rekursion geprüft (Up-Branch zuerst, danach Down auf dem SOC
    nach Up); `worst_total_kwh_t` bleibt zusätzliche auditierbare Kennzahl.

## Abschlussentscheidungen

- Abschlussentscheidungen für Release-Umsetzung:
  - Alert State wird als externer Input geliefert (`AlertStateTimeline` als
    partielle Laufzeit-Repräsentation); interne Frequenzableitung ist für den
    ersten Slice **nicht** vorgesehen.
  - `IReserveRobustnessCheck` befindet sich in einem eigenen
    Markets-/Reserve-Modul als Anwendungskomponente; der Optimierungs-Lauf nutzt nur
    das Ergebnis als `CanExecute`-/Robustheits-Guard.
  - LER-Robustheit ist verbindlich als Produktiv-Precheck mit hartem
    Dispatch-Guard (`CanExecute=false` bei Nicht-`ROBUST_OK`), nicht als direkter harter
    Optimierungs-Constraint in den LP/MILP-Kern eingefügt.
  - aFRR-/mFRR-Aktivierungsenergie ist im ersten produktiven Slice voll modelliert
    inkl. `full_activation_time_afrr` / `full_activation_time_mfrr` und Tests dafür.
- Operator-Sicht ist entschieden:
  - API ist Primärkanal für Aktivierungsentscheidungen (`CanExecute`, Status, Limiting Reason, Restore-Hinweis).
  - UI zeigt denselben operativen Zustand für Bedienung.
  - Replay ist Sekundäransicht mit identischen Kernwerten plus Zeitachsen-Nachvollziehbarkeit.
