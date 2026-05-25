# Plan: Domain-Migration AssetModel.ValidationHelper

Status: Open

## Ziel

Gemeinsame Assetmodell-Validierungen werden vor den konsumierenden Slices
zentralisiert, damit Co-Location und LER/FCR dieselben numerischen Grenzen und
Fehlersemantiken verwenden.

## Scope

- Eine gemeinsame Konstante `eta_min = 1e-6` wird in einem Assetmodell-nahen
  Validierungshelfer etabliert.
- Der Helper validiert Wirkungsgrade einheitlich:
  `eta_min <= eta_charge <= 1` und `eta_min <= eta_discharge <= 1`.
- Konsumierende Slices dürfen den Wert nicht planlokal oder testlokal
  duplizieren.
- Der Helper liefert nur die gemeinsame numerische Invariante. Die fachliche
  Übersetzung in `CONFIG_INCONSISTENT`, `ROBUST_POLICY_UNSUPPORTED` oder andere
  Slice-Codes bleibt Eigentum des jeweiligen konsumierenden Plans.

## Aktivierungsreihenfolge

Dieser Pre-Slice ist abzuschließen, bevor einer der folgenden Slices produktiv
Wirkungsgradvalidierung aktiviert:

- [`plan-market-colocation-model.md`](plan-market-colocation-model.md)
- [`plan-ler-fcr-reserve-robustness.md`](plan-ler-fcr-reserve-robustness.md)

Starten beide konsumierenden Slices parallel, ist dieser Pre-Slice der
Tiebreaker und wird zuerst umgesetzt.

## DoD

- [ ] `eta_min` ist in einem gemeinsamen Assetmodell-Validierungshelfer definiert.
- [ ] Co-Location und LER/FCR importieren den Helper statt eigener Konstanten.
- [ ] Grenzwerttests decken `eta_min`, knapp darunter und knapp darüber ab.
- [ ] Slice-spezifische Fehlercodes bleiben in den konsumierenden Plänen
      testbar.
