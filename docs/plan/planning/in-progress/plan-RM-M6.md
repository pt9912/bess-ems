# Plan RM-M6 Skalierung, UI, Edge / Multi-Asset

**Dokumenttyp:** Detailplan / M6 (aktiv)
**Status:** In Arbeit - aktiviert am 2026-05-13 nach Abschluss von M5.
Erstes Arbeitspaket ist RM-M6-02 als Architektur-/Hosting-Slice;
`AR-OPEN-006` ist mit ADR 0007 geschlossen (shared Worker als Default,
Worker-pro-Asset als Deployment-/Isolation-Pattern).
**Bezug:**
[`roadmap.md`](roadmap.md) (M6),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
(§13 Phase 4, §18 `AR-OPEN-006`),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
(§28.3 spaetere Erweiterungen, LH-PERSIST-005, LH-RISK-001,
LH-OPEN-005, LH-OPEN-006),
[`../../../user/persistence.md`](../../../user/persistence.md)
(TimescaleDB als kompatibler Folgeausbau),
[`../done/plan-RM-M5.md`](../done/plan-RM-M5.md)
(M5-Closure und post-M5-Grenzen),
[`../open/note-RM-M5-followups.md`](../open/note-RM-M5-followups.md)
(Sidecar-/MPC-Trigger-Watch),
[`../../adr/0007-multi-asset-hosting-strategy.md`](../../adr/0007-multi-asset-hosting-strategy.md)
(Multi-Asset-Hosting-Strategie)

---

## Zweck

M6 zieht die Phase-4-Themen aus der Roadmap in einen umsetzbaren Rahmen:
Multi-Asset-Hosting, Operator-UI, Kubernetes/Helm, optionale
TimescaleDB-Erweiterung, Edge-Abgrenzung und zertifizierungsnahe
Regelleistungsvertiefung.

Der Meilenstein ist bewusst kein "alles skalieren"-Blankoscheck. M1-M5
haben API-first, Single-Asset-Regelkreis, M4-Regelleistungsbasis und M5
Sidecar-/MPC-/Replay-Pfade geliefert. M6 muss zuerst klaeren, wie mehrere
Assets im Host modelliert, isoliert und observiert werden, bevor UI,
Kubernetes-Topologie oder Edge-Pfade produktiv werden.

---

## Abgrenzung

**In Scope:**

- Multi-Asset-Hosting-Strategie inklusive Asset-Isolation,
  Scheduling-/Locking-Modell, Konfigurationsform und Observability-Labels.
- Operator-UI als API-first Web-Ausbau auf vorhandenen HTTP- und
  Audit-Pfaden; keine fachliche Bypass-Logik im UI.
- Kubernetes-/Helm-Deployment fuer Worker, API, Postgres und Sidecars,
  ohne die Compose-Referenz sofort zu entfernen.
- TimescaleDB als kompatible Persistenz-Erweiterung fuer Zeitreihen- und
  Retention-Bedarf, ohne fachliche Persistenzmodelle an Timescale zu
  koppeln.
- Edge-Controller-Abgrenzung fuer harte Echtzeit- oder
  zertifizierungsnahe Anforderungen.
- Zertifizierungsnahe Regelleistungsintegration als eigener Slice, erst
  nach Produkt-/TSO-/Anlagenkonzept-Klaerung.

**Out of Scope:**

- Harte Echtzeitgarantien im Docker-/Worker-Regelkreis.
- UI-only-Workflow, der bestehende API-, AuthN/AuthZ- oder Audit-Pfade
  umgeht.
- TimescaleDB als neuer Default oder Voraussetzung fuer MVP-Betrieb.
- Multi-Asset-MPC oder per-Asset-Sidecar-Topologie ohne
  Hosting-Entscheidung und Folge-ADR.
- Zertifizierungszusage ohne externe Produkt-, Hardware- und
  Anlagenkonzept-Abgrenzung.

---

## Aktivierungsbedingungen

M6 ist seit 2026-05-13 aktiviert: M5 ist abgeschlossen, die
Sidecar-/MPC-/Replay-Basis steht, und die offenen Phase-4-Fragen sind in
Roadmap, Lastenheft und Architektur sichtbar.

| Check | Erwartung |
| ----- | --------- |
| M5-Closure | ✅ `../done/plan-RM-M5.md` ist geschlossen; RM-M5-01..07 sind gruen. |
| API-first | Operator-Funktionen bleiben bis zur UI ueber HTTP-API, AuthN/AuthZ und Audit nutzbar. |
| Multi-Asset-Blocker | ✅ `AR-OPEN-006` ist mit ADR 0007 geschlossen; konkrete Flotten-, UI- und Kubernetes-Slices referenzieren den shared-Worker-Default. |
| Timescale-Grenze | PostgreSQL bleibt Default; TimescaleDB darf nur kompatible Erweiterung sein. |
| Edge-Grenze | Harte Echtzeit und zertifizierungsrelevante Schutzfunktionen bleiben ausserhalb des Docker-Regelkreises oder brauchen eigenen Edge-/Herstellerpfad. |

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ⬜ | RM-M6-01 | Operator UI (Web) | API-first Web-Shell fuer vorhandene Operator-Funktionen. Nutzt bestehende AuthN/AuthZ-/Audit- und HTTP-Pfade; keine fachliche Bypass-Logik. Erste Views: Health/Status, aktive Fahrplaene, Optimierungs-/Replay-Run-Liste und Emergency-/Operator-Stop-Status. Start erst nach RM-M6-02-Entscheidung, damit Asset-Auswahl und Mandanten-/Flottenmodell nicht im UI erfunden werden. |
| 🟡 | RM-M6-02 | Multi-Asset-Flottensteuerung / Hosting-Strategie | Slice-Plan: [`plan-RM-M6-02.md`](plan-RM-M6-02.md). ADR 0007 schliesst `AR-OPEN-006`: shared Worker mit per-Asset fan-out ist Default; Worker-pro-Asset bleibt Deployment-/Isolation-Pattern. Naechste Nachweise: Architektur-/Roadmap-Sync, Konfigurationsvalidierung und mindestens ein Multi-Asset-Orchestrierungs-Pin ohne MPC-Coupling. |
| ⬜ | RM-M6-03 | Kubernetes-Deployment + Helm Charts | Helm-Chart fuer Worker/API, Postgres, optionale Sidecars und Secrets/Volumes. Muss Health-/Readiness-/Liveness-Probes, UDS-/mTLS-Optionen und Rollout-/Restart-Verhalten dokumentieren. Compose bleibt Referenzpfad bis Kubernetes-Gate stabil ist. |
| ⬜ | RM-M6-04 | TimescaleDB-Erweiterung | Nur aktivieren, wenn Zeitreihenvolumen, Retention-Abfragen oder Aggregationsbedarf den PostgreSQL-Default operativ begrenzen. Fachliche Persistenzmodelle bleiben unveraendert; Timescale-Hypertables/Continuous-Aggregates sind Adapter-/Migrationsdetail. |
| ⬜ | RM-M6-05 | Edge-Controller-Integration | Abgrenzungs- und Integrationspfad fuer harte Echtzeitkomponenten. Liefert klare Verantwortung zwischen EMS, Edge/Herstellersteuerung, BMS/PCS und hardwareseitiger Schutzkette; kein impliziter Ersatz fuer zertifizierte Schutzfunktionen. |
| ⬜ | RM-M6-06 | Zertifizierungsnahe Regelleistungsintegration | Aktivierung erst mit konkretem Produkt-/TSO-/Anlagenkonzept. Vertieft M4-Regelleistung um Produktregeln, Nachweise, Audit/Replay und externe Schnittstellen; harte Echtzeit bleibt Edge-/Hardware-Thema, wenn das Produkt es verlangt. |

---

## Sequenz

1. RM-M6-02 zuerst: Multi-Asset-Hosting haerten; `AR-OPEN-006` ist mit
   ADR 0007 geschlossen.
2. RM-M6-03 danach: Kubernetes/Helm auf der entschiedenen
   Hosting-Topologie schneiden.
3. RM-M6-01 erst auf stabiler Asset-/Operator-API-Sicht aktivieren.
4. RM-M6-04 bleibt trigger-getrieben; PostgreSQL ist weiter Default.
5. RM-M6-05 und RM-M6-06 nur mit konkretem Edge-/Produkttrigger
   aktivieren; beide duerfen keine harte Echtzeitfaehigkeit des
   Docker-Regelkreises behaupten.

---

## Akzeptanzkriterien

- `AR-OPEN-006` ist geschlossen und in ADR 0007 normativ entschieden.
- Multi-Asset-Betrieb hat ein klares Isolation-, Locking-, Clocking-,
  Persistenz- und Observability-Modell.
- UI-, Kubernetes-, Timescale-, Edge- und Zertifizierungs-Slices koennen
  auf diese Topologie referenzieren, ohne eigene Asset-Semantik zu
  erfinden.
- PostgreSQL bleibt lauffaehiger Default; TimescaleDB ist kompatibler
  Ausbau.
- Harte Echtzeit- und Schutzketten bleiben in Lastenheft und Architektur
  klar ausserhalb des Docker-Regelkreises abgegrenzt.

---

## Risiken und Entscheidungen

- **Multi-Asset-Topologie als Blocker.** Ohne `AR-OPEN-006` wuerden UI,
  Kubernetes und Sidecar-Topologie jeweils eigene Asset-Semantik
  erfinden. Deshalb ist RM-M6-02 erster Slice.
- **UI vor API-Stabilitaet.** UI darf keine neue Fachlogik schaffen.
  Mitigation: UI nur ueber vorhandene oder explizit geplante Operator-API.
- **TimescaleDB-Kopplung.** Hypertables duerfen nicht in Domain- oder
  Application-Ports leaken. Mitigation: Adapter-/Migrationsgrenze und
  PostgreSQL-Default-Pins.
- **Edge-/Zertifizierungsversprechen.** EMS-Software ersetzt keine
  Hardware-Schutzkette. Mitigation: M6-05/M6-06 starten nur mit
  expliziter Produkt- und Anlagenkonzept-Abgrenzung.
