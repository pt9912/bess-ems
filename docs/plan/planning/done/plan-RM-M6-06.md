# Plan RM-M6-06 Zertifizierungsnahe Regelleistungsintegration

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M6-06)
**Status:** Abgeschlossen am 2026-05-13 als Readiness- und Trigger-Gate.
Keine produktive Zertifizierungs- oder TSO-Integration ohne konkretes
Produkt-/TSO-/Anlagenkonzept.
**Bezug:**
[`plan-RM-M6.md`](plan-RM-M6.md)
(M6-Masterplan),
[`plan-RM-M4-03.md`](plan-RM-M4-03.md)
(Regelleistungs-Aktivierungssignal-Verarbeitung),
[`../open/note-RM-M4-followups.md`](../open/note-RM-M4-followups.md)
(F-08..F-12 Regelleistungs-Folgearbeiten),
[`../open/note-RM-M6-followups.md`](../open/note-RM-M6-followups.md)
(M6-Trigger-Watch),
[`../../adr/0008-edge-controller-boundary.md`](../../adr/0008-edge-controller-boundary.md)
(Edge-/Hardwaregrenze),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
§28.3 und LH-RISK-001

---

## Ziel

RM-M6-06 klaert den Abnahmeschnitt fuer zertifizierungsnahe
Regelleistungsintegration. Der Slice implementiert bewusst keine neue
Produktlogik, keinen TSO-Adapter und keine Zertifizierungszusage, weil im
Repo kein konkretes Produkt-, TSO-, DSO-, Hersteller- oder
Anlagenkonzept vorliegt.

Das Ergebnis ist ein verbindliches Gate: Die vorhandene M4-Basis darf fuer
Tests, Simulation, Audit und produktnahe Vorbereitung genutzt werden. Eine
echte zertifizierungsnahe Integration startet erst mit einem eigenen
Folge-Slice, der die externen Regeln, Nachweise, Schnittstellen und
Safety-Grenzen explizit beschreibt.

---

## Stand der Basis

| Bereich | Vorhandener Stand | Grenze |
| ------- | ----------------- | ------ |
| Aktivierungsmodell | RM-M4-03 liefert Domain-Modell, UTC-Window-Validation, Dedupe/Replay, Production-Gate, Health und Dispatch-Integration fuer aFRR. | Keine konkrete Source-Wire-Spec als produktiver Adapter. |
| Reservierung | RM-M4-02 modelliert Reserve-Bands und Solver-Caps. | Keine TSO-spezifische Praequalifikations- oder Nachweislogik. |
| OPC-UA | RM-M4-04/05/08 liefern Telemetrie/Command, Security und Simulator-Integration. | Kein OPC-UA-Activation-Source-Adapter; F-09 bleibt Trigger-Watch. |
| Audit/Replay | M4/M5 liefern Dedupe, Replay- und Trace-Bausteine. | Kein vollstaendiges Zertifizierungsnachweis-Paket ohne externe Regeln. |
| Edge/Hardware | ADR 0008 grenzt harte Echtzeit und Schutzketten ab. | EMS ersetzt keine zertifizierte Schutzkette und keinen Safety Case. |

---

## Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | RM-M6-06-A | Readiness-Abgrenzung | Bestehende M4/M5-Bausteine und fehlende zertifizierungsnahe Pflichten sind explizit getrennt. |
| ✅ | RM-M6-06-B | Aktivierungsgate | Produkt-/TSO-/Anlagenkonzept, externe Schnittstellen, Nachweisumfang und Safety-Grenze sind Pflichttrigger fuer jede echte Integration. |
| ✅ | RM-M6-06-C | Folgearbeiten | M4-Folgearbeiten F-08..F-12 und M6-Folgearbeit F-M6-06-01 bleiben die Heimat fuer produktive mFRR, Source-Adapter, TSO-Reporting und Security-Profile. |
| ✅ | RM-M6-06-D | Plan-/Roadmap-Sync | M6-Masterplan und Roadmap markieren RM-M6-06 als abgeschlossenes Gate, nicht als implementierte Zertifizierung. |
| ➡️ | RM-M6-06-E | Produktive Zertifizierungswelle | Bewusst spaeter: eigener Slice mit externer Spezifikation, HIL/Nachweisen, Reporting und ggf. Edge-/Hardwarepfad. |

---

## Aktivierungskriterien fuer eine echte Folgeintegration

Ein produktiver RM-M6-06-Folge-Slice darf erst starten, wenn mindestens
alle folgenden Artefakte benannt sind:

- Produkt- und Marktrolle: FCR, aFRR, mFRR, Reservekapazitaet,
  Aktivierungsenergie, Aggregator-/Direktvermarktungsrolle und
  betroffene Regelzone.
- Externe Spezifikation: TSO-/DSO-/Aggregator-/Vendor-Dokument mit
  Protokoll, Authentisierung, Zeitbasis, Payload und Quittungsregeln.
- Nachweisumfang: Praequalifikation, Audit, Replay, HIL,
  Failover-/Reconnect-Nachweis, Zeitstempel- und Dedupe-Anforderung.
- Sicherheitsprofil: TLS/AuthN/AuthZ, Zertifikate, Secret-Rotation,
  Netzwerksegmentierung und materialisiertes Production-Gate.
- Echtzeit-/Schutzgrenze: klare Entscheidung, welche Teile im EMS
  bleiben und welche in Edge, BMS, PCS oder Hardware-Schutzkette liegen.
- Betriebsmodell: Operator-Freigabe, Rollback, Monitoring, Incident-
  Runbook und Verantwortlichkeiten zwischen Betreiber, EMS und
  Herstellersteuerung.

---

## Entscheidungen

- **Keine Zertifizierungsbehauptung:** M6 liefert eine
  zertifizierungsnahe Bereitschaftsgrenze, keine bestandene
  Praequalifikation oder regulatorische Freigabe.
- **M4 bleibt technische Basis:** RM-M4-03 ist der richtige Kern fuer
  Aktivierung, Dedupe, Replay und Production-Gate. RM-M6-06 dupliziert
  diese Logik nicht.
- **F-Items bleiben aktiv:** Produktive mFRR/MOLS/MARI, konkrete
  Source-Wire-Adapter, TSO-Quittungen, Dedupe-v2 und generisches
  Security-Profile bleiben Trigger-Arbeiten aus M4/M6.
- **ADR 0008 ist bindend:** Sobald harte Echtzeit oder
  zertifizierungsnahe Schutzfunktionen betroffen sind, muss der
  Folge-Slice die Edge-/Hardwaregrenze explizit referenzieren.
- **Audit ohne Produktregeln reicht nicht:** Logs, Persistenz und Replay
  sind Bausteine. Ein Nachweis-Paket entsteht erst aus externem Regelwerk
  plus reproduzierbaren Test-/HIL-Szenarien.

---

## Akzeptanzkriterien

- RM-M6-06 ist als Gate abgeschlossen, ohne eine unbelegte
  Zertifizierungszusage zu erzeugen.
- Die Voraussetzungen fuer produktnahe Regelleistungsintegration sind
  konkret und pruefbar.
- M4-Folgearbeiten und M6-Folgearbeiten haben klare Heimat und Trigger.
- Roadmap und M6-Masterplan zeigen M6 als abgeschlossen, waehrend die
  produktive Zertifizierungswelle trigger-getrieben bleibt.

## Verifikation

- ✅ `git diff --check`
