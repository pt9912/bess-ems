# Notiz: M3-Follow-up-Slices (Trigger-Watch)

**Dokumenttyp:** Vorabklärung / Trigger-Watch
**Status:** Offen — vier vor-Trigger Follow-up-Items aus der M3-Closure
**Bezug:**
[`../done/plan-RM-M3-D2.md`](../done/plan-RM-M3-D2.md) (M3-D2-Slice „Out of Scope"-Block, der diese vier Items als separate Folge-Slices benannt hat),
[`../in-progress/plan-RM-M3.md`](../in-progress/plan-RM-M3.md),
[`../in-progress/roadmap.md`](../in-progress/roadmap.md),
[`../../adr/0003-native-kernel-language.md`](../../adr/0003-native-kernel-language.md) (Sprach-Pivot-Trigger §4),
[`../../adr/0004-native-kernel-process-isolation.md`](../../adr/0004-native-kernel-process-isolation.md) (Out-of-Process-Pivot-Trigger §4)

---

## Zweck

Die M3-Closure hat den Native Control Core von in-tree-Implementierung
über volle Quality-Gates bis zur produktiven DI-Aktivierung gezogen
(RM-M3-01..13 + M3-D2). Vier Follow-up-Items wurden im
`plan-RM-M3-D2.md`-Slice explizit als „Out of Scope / separate
Folge-Slice" benannt. Damit sie beim nächsten Trigger-Watch
sichtbar bleiben — statt in einem geschlossenen Plan unter `done/`
zu verschwinden — sind sie hier zentral mit Trigger-Bedingung,
Scope-Skizze und Aktivierungs-Pfad geführt.

Kein Item zündet aktuell. Diese Notiz ist **Trigger-Watch-Material**,
kein Slice-Plan: der konkrete Plan entsteht erst, wenn ein
Trigger zündet, und zwar im Format `plan-RM-M3-D3.md` (für M3-
Folge-Slices) oder einer eigenen ADR (für die Architektur-Pivots).

---

## Item 1: M3-D3 — PID-Routing in den Regelzyklus

**Trigger:** Ein konkreter Konsument von `PidController.Step`
materialisiert im Regelzyklus. Heute ist `BatteryEms.Domain.PidController`
eine reine Domain-Primitive ohne produktive Verdrahtung (RM-M2-08
lieferte ihn als „Soll" / Domain-Primitive). Ohne Konsument bringt
eine Routing-Aktivierung nichts.

**Beobachtbare Trigger-Bedingungen** (eines reicht):

- Ein Anwendungs-Use-Case verlangt weichen Setpoint-Tracking
  (z. B. Power-Following statt direktem Limit-und-Forward — der
  Konsument würde `IControlCycleUseCase` ergänzen oder eine neue
  Use-Case-Klasse aufmachen, die PID inline einbettet).
- Frequency-Tracking / FCR-naher Pfad braucht PI/PID-Stabilisierung
  über die Constraint+Ramp-Pipeline hinaus.
- Ein Plan-Slice formuliert konkret „Konsument X soll PID nutzen".

**Scope-Skizze für M3-D3** (wenn der Trigger zündet):

- Neuer Driven-Port `IPidKernel` in `BatteryEms.Application` analog
  zu `IControlKernel`, mit `PidStep(state, options, input) → result`.
- `ManagedPidKernel` in `BatteryEms.Application.Control` als Wrapper
  um `BatteryEms.Domain.PidController.Step`.
- `NativePidFallbackKernel` in `BatteryEms.Adapters.NativeInterop`
  analog zu `NativeFallbackControlKernel`, ruft
  `NativeControlKernel.PidStep` (aus RM-M3-13).
- DI-Erweiterung: `AddBessNativeControl` registriert auch
  `IPidKernel` analog zur `IControlKernel`-Logik.
- Replay-Parity-Fixture für PID happy-path-Cases in
  `tests/fixtures/native_parity/pid_cases.v1.json` (separates Schema,
  weil PID-Cases andere Felder brauchen).
- Wire-Tests durch P/Invoke + Replay-Parity gegen den realen
  `bcc_pid_step`-Export (Native Coverage 100 % bleibt erhalten).

**Aufwandsschätzung:** grob 1–2 Wochen, ähnlich M3-D2 plus die
Replay-Fixture.

**Aktivierungs-Pfad:** eigener `plan-RM-M3-D3.md`, nach
`docs/plan/planning/open/` während der Planung, dann nach
`in-progress/` für die Umsetzung, dann nach `done/`. Roadmap-Eintrag
unter „Aktueller Stand" + neue Zeile in `plan-RM-M3.md` (oder
direkt als „RM-M3-D3"-Eintrag).

---

## Item 2: Production-Profil-Defaults zentralisieren

**Trigger:** Operations-Reibung mit den heutigen `appsettings.json`-
Stages. Aktuell leben Konfigurations-Defaults verteilt:

- `src/host/BatteryEms.Host/appsettings.json` (Standard-Defaults)
- `deploy/compose.yml` (Container-spezifische Env-Variables)
- Test-Hosts (eigene `appsettings.json` pro Test-Projekt)

**Beobachtbare Trigger-Bedingungen** (eines reicht):

- Mehrere Operations-Profile (Test / Staging / Production / Native-
  On / Native-Off) müssen tatsächlich nebeneinander gepflegt werden
  und die Env-Variable-basierte Override-Strategie reicht nicht mehr.
- Ein konkreter Operations-Anlass: Profil-Drift zwischen Staging und
  Production hat zu einer fehlerhaften Deployment-Konfiguration
  geführt, Postmortem fordert zentrale Profile.
- Compliance-Anforderung verlangt audit-trail-fähige Profil-Versionierung.

**Scope-Skizze:**

- `appsettings.Production.json` / `appsettings.Native.json` /
  `appsettings.Test.json` als reproduzierbare Standardprofile;
  klare Override-Hierarchie (Standard → Profil → Env-Variable).
- Profile-Schema-Validierung beim Start (analog zur Modbus-/MQTT-
  Mapping-Validation aus RM-M1) — fehlerhaftes Profil kippt den Boot.
- Doku-Update in `docs/user/quality.md` §4 mit der neuen Profil-Matrix.

**Aufwandsschätzung:** grob 1 Woche, primär Operations- und Doku-
Arbeit; null Application-Code.

**Aktivierungs-Pfad:** eher als RM-M3-FUP-Carve-out im Zuge eines
Operations-Hardening-Slices (z. B. Multi-Replica-Deployment,
Compliance-Audit) als eigener M3-Slice.

---

## Item 3: NativeControl-Gesundheits-Endpoint

**Trigger:** Operator-Anforderung, dass der heutige `/health`-
Endpoint zu grob ist. Aktuell deckt `/health` nur Container-/
Postgres-Health ab; den Loader-Status (`Disabled` / `LibraryMissing` /
`LoadFailed` / `AbiMismatch` / `Loaded`) sieht ein Operator nur
in den strukturierten Logs (`native_control_status=*`).

**Beobachtbare Trigger-Bedingungen** (eines reicht):

- Ein Pod-Crash-Investigation zeigt dass das
  Standard-Monitoring-Dashboard den Loader-Status nicht
  separat ausweist und der Vorfall dadurch erst spät erkannt wurde.
- Standard-Operations-Probe-Tooling (z. B. Kubernetes
  Liveness/Readiness-Differenzierung) verlangt eine separate
  `/health/native`-Probe, die nur den Native-Pfad bewertet.
- Ein neuer SLO/SLA verlangt eine messbare „Native-aktiv"-
  Verfügbarkeit, getrennt von der allgemeinen Container-Verfügbarkeit.

**Scope-Skizze:**

- Neuer Endpoint `/health/native` in `BatteryEms.Api` (analog zu
  `/health`), der den `NativeControlLoadResult`-Status aus dem
  DI-Container ausliest und als JSON-Body emittiert
  (`{"status": "loaded", "library_path": "...", "abi_version": "0.2.0"}`).
- Status-Persistierung beim Start: `NativeControlLoadResult` als
  Singleton im Container (heute wird er in `AddBessNativeControl`
  konstruiert und nur intern für die Factory verwendet — eine
  zusätzliche Singleton-Registrierung würde den Endpoint
  versorgen).
- Prometheus-Metrik `bess_native_control_status{status="..."}` als
  Gauge analog zu den existierenden `BatteryEms.Adapters.Telemetry`-
  Mustern.
- Architektur-Tabu-Test verifiziert dass `/health/native` keinen
  zusätzlichen Adapter-Cross-Reference einführt.

**Aufwandsschätzung:** grob 2–3 PT, kleiner Slice. Primär API-
Endpoint + DI-Anpassung + Metric.

**Aktivierungs-Pfad:** eigener kleiner Slice, möglicherweise als
RM-M3-FUP-Eintrag oder direkt in einem Observability-Slice
(zusammen mit z. B. `bess_native_control_calls_total` /
`bess_native_control_fallback_total` Counter-Metriken).

---

## Item 4: Out-of-Process / Sprach-Pivot

**Trigger:** Eine der fünf Sprach-Trigger aus
[`ADR 0003 §4`](../../adr/0003-native-kernel-language.md) oder
eine der sieben Process-Isolation-Trigger aus
[`ADR 0004 §4`](../../adr/0004-native-kernel-process-isolation.md)
zündet. Beide ADRs sind die normative Trigger-Liste — diese Notiz
fasst sie nicht zusammen, sie verweist auf sie.

**Bündel-Trigger:** ADR 0003 Trigger 2 (MPC/Phase-3) und ADR 0004
Trigger 2 (Phase-3-Komponenten in Scope) sind faktisch dasselbe
Architektur-Event aus zwei Blickwinkeln. Wenn dieser Trigger zündet,
wird er durch eine **gemeinsame Folge-ADR** plus einen **gemeinsamen
Slice-Plan** adressiert (Aufwand grob 5–10 Wochen, ADR 0004 §3).

**Aktivierungs-Pfad:**

- Sprach-Pivot allein: neue ADR `0005-native-kernel-rust-pivot.md`,
  bestehende ADR 0003 wird zu „Superseded by 0005".
- Out-of-Process-Pivot allein: neue ADR `0006-native-kernel-out-of-process.md`,
  bestehende ADR 0004 wird zu „Superseded by 0006".
- Bündel-Pivot: eine gemeinsame ADR (z. B. `0005-native-kernel-pivot.md`)
  die beide Achsen adressiert; beide bestehenden ADRs werden zu
  „Superseded".

**Aktueller Status:** kein Trigger zündet. Diese Notiz hält die
Trigger-Watch-Pflicht fest — beim nächsten architektonischen Review
ist die Trigger-Liste in den ADRs explizit gegen die Roadmap zu
prüfen.

---

## Trigger-Watch-Disziplin

Diese Notiz wird **nicht aktiv abgearbeitet**. Sie wird gescannt:

- Beim Beginn jedes neuen Slice-Plans (insbesondere wenn der Slice
  PID, MPC, Multi-Asset, Realzeit-Anforderungen oder Zertifizierung
  berührt).
- Beim quartalsweisen Architektur-Review.
- Bei jedem Production-Crash-Postmortem (Items 3 + 4 sind hier
  relevant).

Beim Zünden eines Triggers:

1. Item aus dieser Notiz extrahieren.
2. Eigenen Slice-Plan in `docs/plan/planning/open/` anlegen
   (`plan-RM-M3-D3.md` für PID-Routing, `plan-RM-M3-FUP-*.md` für
   Operations-Items, neue ADR + zugehöriger Plan für Architektur-
   Pivots).
3. Item-Eintrag hier mit Verweis auf den neuen Plan markieren oder
   nach `done/` verschieben sobald der Slice abgeschlossen ist.
4. Roadmap-„Aktueller Stand"-Block ergänzen.

So bleibt die Trigger-Liste lebendig statt in einem closed-Plan zu
verschwinden.
