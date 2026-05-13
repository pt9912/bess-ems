# Plan RM-M6-02 Multi-Asset-Flottensteuerung / Hosting-Strategie

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M6-02)
**Status:** Abgeschlossen am 2026-05-13. Aktiviert am 2026-05-13 als
erster M6-Slice.
**Bezug:**
[`plan-RM-M6.md`](plan-RM-M6.md)
(Master-Plan),
[`../../adr/0007-multi-asset-hosting-strategy.md`](../../adr/0007-multi-asset-hosting-strategy.md)
(Hosting-ADR),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
(§18 `AR-OPEN-006`),
[`plan-RM-M5.md`](plan-RM-M5.md)
(M5-Sidecar-/MPC-/Replay-Basis),
[`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md)
(Trigger-Watch fuer bewusst ausgeklammerte Folgearbeiten)

---

## Ziel

RM-M6-02 schliesst `AR-OPEN-006` und macht die Multi-Asset-Topologie
verbindlich. Der Slice entscheidet nicht ueber UI-Layouts, Kubernetes-
Chart-Struktur oder Edge-Controller-Implementierung, sondern liefert
deren gemeinsame Fachgrundlage: wie Assets im Host registriert,
isoliert, gelockt, observiert und deployed werden.

---

## Entscheidung

ADR 0007 waehlt shared Worker mit per-Asset fan-out als M6-Default.
Worker-pro-Asset bleibt zulaessiges Deployment-/Isolation-Pattern, aber
nicht der Default. Der bestehende Code stuetzt diese Linie bereits:

- `ControlCycleHostedService` iteriert pro Tick ueber
  `IBatteryAssetRegistry.GetAll()`.
- `IControlCycleUseCase`, Schedules, Commands, Telemetrie, MpcRuns,
  Metriken und Traces tragen `assetId`.
- Fehlerpfade im Worker werden per Asset geloggt und metrisiert, ohne
  den Hosted-Service zu beenden.

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M6-02-A | Hosting-ADR | ADR 0007 ist akzeptiert und schliesst `AR-OPEN-006`: shared Worker als Default, Worker-pro-Asset als Trigger-/Deployment-Pattern. |
| ✅ | RM-M6-02-B | Architektur-/Roadmap-Sync | `spec/architecture.md` §18 ist auf "Geschlossen mit ADR 0007" gesetzt; M6-Masterplan und Roadmap verweisen auf den Slice. |
| ✅ | RM-M6-02-C | Multi-Asset-Konfigurationsvalidierung | Host-/Config-Pfad validiert mehrere Assets: `assets.schema.json` verlangt eine nicht-leere Asset-Liste, `JsonFileConfigurationLoader.LoadAssets` erhaelt Single-Asset-Kompatibilitaet und rejected doppelte `asset_id`, der Host seeded alle Assets in die Registry und verweigert konkrete IO-Adapter bei Multi-Asset-Konfig bis per-Asset Adapter-/Command-Sink-Zuordnung existiert. |
| ✅ | RM-M6-02-D | Orchestrierungs-Pins | Worker-Tests pinnen den shared-Worker-Default explizit: ein Tick fan-outet ueber mehrere Assets, persistiert pro Asset eigene Commands, isoliert per-Asset Fehler mit `asset_id`-Metrik und emittiert pro Asset Control-/Dispatch-Spans mit `asset_id`. |
| ✅ | RM-M6-02-E | Folgearbeiten / Trigger | Parallel-Fanout, Worker-pro-Asset-Deployment, per-Asset-Sidecar und Multi-Asset-MPC sind in [`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md) als Trigger-Watch dokumentiert. |

---

## Akzeptanzkriterien

- `AR-OPEN-006` ist in der Architektur geschlossen.
- Der M6-Default ist eindeutig: shared Worker mit per-Asset fan-out.
- Worker-pro-Asset ist als bewusstes Deployment-/Isolation-Pattern
  dokumentiert, nicht als konkurrierender Default.
- Konfigurations- und Orchestrierungs-Pins verhindern, dass mehrere
  Assets nur zufaellig funktionieren.
- Nachgelagerte Slices RM-M6-01 (UI), RM-M6-03 (Kubernetes), RM-M6-05
  (Edge) und RM-M6-06 (zertifizierungsnahe Regelleistung) koennen auf
  dieselbe Asset-Topologie verweisen.

---

## Bewusst draussen

- **Parallel-Fanout.** Trigger: Tick-Dauer verletzt mit mehreren Assets
  reproduzierbar das Budget. Bis dahin bleibt sequenzieller fan-out der
  einfachere, deterministische Default.
- **Worker-pro-Asset als Helm-Pattern.** Trigger: RM-M6-03 oder
  Mandanten-/Fault-Domain-Anforderung.
- **Per-Asset-Sidecar.** Trigger: Asset-spezifische Backend-Wahl,
  Performance-Engpass oder Sidecar-Fault-Isolation.
- **Multi-Asset-MPC.** Trigger: gemeinsamer Netzpunkt oder Operator-
  Workflow mit gekoppelten Assets; gehoert nicht in den Hosting-Slice.

Die detaillierte Trigger-Watch fuer diese Folgearbeiten lebt in
[`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md).

---

## Closure

RM-M6-02 ist abgeschlossen: ADR 0007 ist akzeptiert, Architektur und
Roadmap referenzieren den shared-Worker-Default, Multi-Asset-
Konfiguration wird validiert, Orchestrierungs-/Observability-Pins
decken mehrere Assets ab, und die bewusst ausgeklammerten
Folgearbeiten sind als Trigger-Watch sichtbar.
