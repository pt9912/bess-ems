# ADR 0009 - API Service Extraction Criteria

**Status:** Accepted - der kombinierte Worker/API-Host bleibt Default.
Eine API-Auskopplung ist trigger-basiert und kein impliziter
Skalierungsschritt. Schliesst `AR-OPEN-001` und `RM-OPEN-06`.
**Datum:** 2026-05-13
**Bezug:**
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§15 und §18 (`AR-OPEN-001`),
[`../planning/in-progress/roadmap.md`](../planning/in-progress/roadmap.md)
(`RM-OPEN-06`),
[`../planning/done/plan-RM-M6.md`](../planning/done/plan-RM-M6.md)
(M6-Abschluss),
[`../planning/done/plan-RM-M6-03.md`](../planning/done/plan-RM-M6-03.md)
(Kubernetes/Helm-Topologie),
[`0007-multi-asset-hosting-strategy.md`](0007-multi-asset-hosting-strategy.md)
(shared Worker und Worker-pro-Asset)

---

## 1. Kontext

Die Architektur sah von Beginn an vor, dass die API spaeter als eigener
`bess-ems-api`-Service aus demselben Codebestand oder als separates Image
ausgekoppelt werden kann. Nach M6 ist die produktive Baseline jedoch
bewusst einfacher:

- Ein OCI-Image startet Worker und API gemeinsam.
- Der 1-s-Control-Cycle bleibt die fuehrende Laufzeitverantwortung.
- Kubernetes/Helm rendert den gemeinsamen `bess-ems`-Host; `replicaCount
  > 1` ist fuer diesen kombinierten Host nicht der Skalierungsweg.
- ADR 0007 entscheidet shared Worker mit per-Asset fan-out als
  Multi-Asset-Default. Worker-pro-Asset ist ein Isolation-Pattern, aber
  keine API-Auskopplung.

Damit ist `AR-OPEN-001` keine Frage des "ob technisch moeglich", sondern
eine Betriebs- und Verantwortungsentscheidung: Wann ist die API als
eigene Fault-, Security-, Scaling- oder Release-Domain noetig?

---

## 2. Entscheidung

Der Default bleibt ein kombinierter Worker/API-Host. Die API wird erst
ausgekoppelt, wenn mindestens ein fachlicher oder betrieblicher Trigger
vorliegt und die Mindestvoraussetzungen aus §4 erfuellt sind.

Eine Auskopplung darf als separater Host aus demselben Codebestand oder
als separates Image erfolgen. Sie ist eine explizite Topologieaenderung
mit eigener Helm-/Deployment-Konfiguration, eigenen Health-Signalen und
angepassten Tests. Sie ist **nicht** gleichbedeutend mit `replicaCount >
1` fuer den kombinierten Host.

---

## 3. Trigger fuer API-Auskopplung

Die API-Auskopplung wird neu bewertet, wenn eines dieser Signale
eintritt:

- API-Traffic, Operator-UI oder Northbound-Zugriffe brauchen
  unabhaengige Skalierung oder SLA, ohne weitere Control-Loop-Instanzen
  zu starten.
- Betreiber-, Mandanten-, Security- oder Zertifizierungsgrenzen verlangen
  getrennte Identitaeten, Secrets, Netzwerkregeln oder Audit-Domaenen.
- API-/UI-Last, lange Requests oder Restart-Verhalten beeinflussen den
  1-s-Control-Cycle, Worker-Health oder per-Asset fan-out messbar.
- Ein API-only Footprint wird fachlich benoetigt, z. B. fuer
  Northbound-Gateways, Multi-Tenant-Zugaenge oder getrennte
  Operator-Domaenen.
- API-Vertraege, AuthN/AuthZ oder UI-nahe Endpunkte brauchen eine andere
  Release-Kadenz als der Control Runtime.

---

## 4. Mindestvoraussetzungen

Vor einer produktiven Auskopplung muessen diese Punkte erfuellt sein:

- API-Lese- und Schreibpfade laufen ueber Ports, Persistenz und
  versionierte Contracts; sie duerfen nicht von in-memory Worker-State
  im selben Prozess abhaengen.
- Schreibpfade sind idempotent, auditierbar, persistiert und fuer mehrere
  API-Replikate eindeutig bewertet.
- Command- und Konfigurationswrites haben eine klare Ownership- oder
  Locking-Strategie, falls mehrere API-Instanzen aktiv sein koennen.
- Health- und Readiness-Signale sind fuer API-only und Worker-only Hosts
  getrennt modelliert.
- AuthN/AuthZ, Audit, Tracing, Metriken und Secret-Rotation bleiben ueber
  die Prozessgrenze hinweg gleichwertig.
- Helm/Kubernetes beschreibt getrennte Deployments, Service Accounts,
  Secrets, Network Policies und Upgrade-/Rollback-Verhalten.
- Tests decken API-only Host, Worker-only Host, parallele API-Replikate
  und End-to-End-Verhalten gegen gemeinsame Persistenz ab.

---

## 5. Nicht-Ziele

- Keine API-Auskopplung nur zur formalen Microservice-Optik.
- Keine horizontale Skalierung des kombinierten Worker/API-Hosts ueber
  `replicaCount > 1`.
- Keine harte Echtzeit- oder Safety-Verbesserung durch API-Trennung; fuer
  diese Grenze gilt ADR 0008.
- Keine Umgehung der per-Asset-Fault-Domain aus ADR 0007.

---

## 6. Konsequenzen

- `AR-OPEN-001` und `RM-OPEN-06` sind geschlossen.
- Architektur §15 beschreibt den kombinierten Host als Default und
  verweist fuer Auskopplung auf diese ADR.
- Folgearbeiten zur API-Auskopplung starten erst bei einem Trigger aus
  §3 und muessen die Mindestvoraussetzungen aus §4 nachweisen.
- Bis dahin bleibt die M6-Topologie aus Worker/API-Host, PostgreSQL,
  optionalem MQTT-Broker und Monitoring die dokumentierte Baseline.
