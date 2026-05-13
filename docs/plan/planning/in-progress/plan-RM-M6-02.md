# Plan RM-M6-02 Multi-Asset-Flottensteuerung / Hosting-Strategie

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M6-02)
**Status:** In Arbeit - aktiviert am 2026-05-13 als erster M6-Slice
**Bezug:**
[`plan-RM-M6.md`](plan-RM-M6.md) (Master-Plan),
[`../../adr/0007-multi-asset-hosting-strategy.md`](../../adr/0007-multi-asset-hosting-strategy.md)
(Hosting-ADR),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
(§18 `AR-OPEN-006`),
[`../done/plan-RM-M5.md`](../done/plan-RM-M5.md)
(M5-Sidecar-/MPC-/Replay-Basis)

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
| ⬜ | RM-M6-02-D | Orchestrierungs-Pins | Tests belegen shared-Worker-Default: ein Tick fan-outet ueber mehrere Assets, isoliert per-Asset Fehler und setzt `asset_id` in Metriken/Traces/Persistenz. Bestehende Pins duerfen genutzt, aber die M6-DoD muss explizit sichtbar sein. |
| ⬜ | RM-M6-02-E | Folgearbeiten / Trigger | Parallel-Fanout, Worker-pro-Asset-Deployment, per-Asset-Sidecar und Multi-Asset-MPC werden als Trigger-Watch dokumentiert, falls sie nicht direkt implementiert werden. |

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
