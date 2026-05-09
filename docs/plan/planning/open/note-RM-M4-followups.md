# Notiz: M4-Folgearbeiten (Trigger-Watch)

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen — Folgearbeiten zu aktiven M4-Slices, ohne Plan-Heimat im Master-Plan
**Bezug:**
[`../in-progress/plan-RM-M4.md`](../in-progress/plan-RM-M4.md) (Master-Slice-Plan, in Arbeit — RM-M4-02 ✅, RM-M4-01 in Vorbereitung),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md)

---

## Zweck

Während der M4-Umsetzung tauchen Folgearbeiten auf, die der jeweilige
Slice **bewusst draußen lässt** — entweder weil sie nicht-trivialen
Eigenscope haben (Operator-API, neuer Use-Case) oder weil ihr
Trigger noch nicht zündet. Damit sie beim nächsten
Trigger-Watch-Scan sichtbar bleiben — statt in Plan-Tabellen-
Kommentaren zu versinken — sind sie hier zentral geführt.

Anders als der **Bewusst-draußen-Block** in einer Plan-Zeile
(der einen abgeschlossenen Slice-Carve-out beschreibt) ist diese
Notiz **Trigger-Watch-Material** für Folgearbeiten, die einen
eigenen Slice-Plan brauchen wenn sie zünden.

Konkrete Slice-Pläne entstehen erst beim Trigger:

- `plan-RM-M4-01-FUP-NN.md` für Folgearbeiten zum Intraday-Slice
- Oder Carve-out-Sektion innerhalb des auslösenden Plans, falls
  der Trigger ein anderer Slice ist (z. B. Operator-API-Slice der
  F-01 + F-02 zusammenzieht).

---

## Item F-01: Cold-Start-Bootstrap (Day-Ahead → Intraday-Initial)

**Quelle:** RM-M4-01 Design-Entscheidung D-01 — Reoptimierung
verlangt eine existierende Intraday-Baseline. Bei `intraday-
baseline-missing` heute Failed-Run; das produktive Cold-Start-
Verhalten ist nicht modelliert.

**Trigger:** Erster operativer Workflow, der eine frische
Intraday-Welt braucht ohne dass jemand einen Intraday-Schedule
manuell gesetzt hat. Konkret eines der drei:

- Ein Operator-UI-Workflow „initial Intraday-Setup" zündet (heute
  nicht existent).
- Ein produktives Deployment fährt einen neuen Asset hoch und der
  erste Intraday-Reopt-Aufruf scheitert reproduzierbar mit
  `intraday-baseline-missing`.
- Eine Compliance-/Operations-Anforderung verlangt automatischen
  DA→ID-Initial-Transfer beim Tageswechsel.

**Scope-Skizze** (wenn der Trigger zündet):

Drei mögliche Mechanik-Entwürfe — jeder mit eigenem Implementierungs-
Aufwand und Operator-Modell:

- **(a) Auto-Copy bei Reoptimierung-Aufruf:** wenn die Intraday-
  Baseline fehlt, kopiert die Use-Case implizit die aktuelle Day-
  Ahead-Schedule als Intraday-`v1` und reoptimiert sofort darauf.
  Vorteil: kein zusätzlicher API-Endpunkt, Operator sieht den
  Reopt einfach durchlaufen. Nachteil: implizite Schedule-Erzeugung
  ohne explizite Operator-Bestätigung — schwerer zu auditieren.

- **(b) Expliziter `POST /markets/intraday/initialize-from-day-ahead`-
  Endpoint:** Operator triggert die DA→ID-Kopie als eigene Aktion,
  danach läuft Reopt regulär. Vorteil: sauberes Audit-Trail.
  Nachteil: zwei-Schritt-Workflow.

- **(c) Cron-/Scheduled-Initialization:** zum Tageswechsel kopiert
  ein Hosted-Service automatisch DA→ID. Vorteil: kein Operator-
  Eingriff. Nachteil: implizit + braucht Cron-Infrastruktur.

**Aufwandsschätzung:** grob 1-2 Wochen je nach Variante. (a) ist am
kleinsten (~150-200 LOC), (b) braucht zusätzlichen API-Endpoint plus
Auth-/Audit-Bindings (~250-350 LOC), (c) braucht Hosted-Service plus
Cron-Konfiguration (~300-400 LOC).

**Aktivierungs-Pfad:** eigener `plan-RM-M4-01-FUP-cold-start.md` in
`open/`, dann `in-progress/`, dann `done/`. Roadmap-Eintrag.

---

## Item F-02: Alignment-Toleranz (sub-Step-`residualStart`-Snap)

**Quelle:** RM-M4-01 Design-Entscheidung D-02 — `residualStart`
muss heute exakt an einer Window-Grenze liegen. Misalignment ⇒
Failed-Run mit `residual-start-not-aligned`. Operator-Reibung
denkbar wenn die Reopt-Trigger-Zeit nicht natürlicherweise auf
einem Step-Boundary liegt.

**Trigger** (eines reicht):

- Operations-Reibung sichtbar: ein Operator/Service-Account
  bekommt reproduzierbar `residual-start-not-aligned` zurück, weil
  die Reopt-Trigger-Zeit z. B. „now()" ist und der Schedule
  15-min-Steps nutzt, die typischerweise nicht zur Sekunde am
  Quartal beginnen.
- Eine API-Konsumenten-Spec verlangt „Reopt zum aktuellen
  Zeitpunkt, nicht zum nächsten Step-Boundary".
- Telemetrie zeigt nicht-triviale Failed-Run-Rate mit
  `residual-start-not-aligned` als TerminationCode.

**Scope-Skizze** (wenn der Trigger zündet):

- Implementierungswahl 1 — **Snap-Forward**: `residualStart` wird
  intern auf den nächsten Step-Boundary `≥ residualStart`
  hochgesetzt. Die Window dazwischen bleibt unverändert (also als
  Past-Window erhalten). Konservativ: optimiert weniger statt
  potenziell falsch viel.
- Implementierungswahl 2 — **Window-Split mit Power-Erhaltung**:
  das Straddling-Window wird in zwei Teile gesplittet
  (`[Start, residualStart)` als Past mit unverändertem
  `TargetPowerKw`, `[residualStart, End)` als Future-Optimierungs-
  Eingang).
- Tests: alignment-Toleranz-Pin (Snap-Forward), Negative-Test (
  noch immer Failed bei extremer Misalignment z. B. < 1 ms vor
  Step-Boundary?).

**Aufwandsschätzung:** ~1-2 Tage. ~100-150 LOC inkl. Tests.

**Aktivierungs-Pfad:** kleiner Slice, möglicherweise Carve-out im
auslösenden Plan (z. B. wenn ein Operator-Slice F-01 zieht und
beide Items zusammen abdeckt). Sonst eigener `plan-RM-M4-01-FUP-
alignment-tolerance.md`.

---

## Trigger-Watch-Disziplin

Diese Notiz wird **nicht aktiv abgearbeitet**. Sie wird gescannt:

- Beim Beginn jedes neuen M4-Slice-Plans (insbesondere wenn der
  Slice Operator-Workflows, API-Endpoints oder Schedule-Lebenszyklus
  berührt).
- Beim quartalsweisen Architektur-Review.
- Bei jedem Production-Vorfall-Postmortem rund um Intraday-Reopt
  oder DA→ID-Übergänge.

Beim Zünden eines Triggers:

1. Item aus dieser Notiz extrahieren.
2. Eigenen Slice-Plan in `docs/plan/planning/open/` anlegen (Name
   `plan-RM-M4-01-FUP-NN.md` für RM-M4-01-Folgen, oder analoger
   Name für andere M4-Slices).
3. Item-Eintrag hier mit Verweis auf den neuen Plan markieren oder
   nach Abschluss entfernen.
4. Roadmap-„Aktueller Stand"-Block ergänzen.

So bleibt die Trigger-Liste lebendig statt in Plan-Tabellen-
Kommentaren zu verschwinden.
