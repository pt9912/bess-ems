# Spezifikation: bess-ems

**Dokumenttyp:** Technische Spezifikation (Technik-Stratum)
**Status:** Verbindlich, fortschreibbar
**Bezug:** [`spec/lastenheft.md`](lastenheft.md) (Vertrag — wird präzisiert, nie geschärft)

Dieses Dokument ist das **Technik-Stratum** zwischen Lastenheft (Vertrag: *was
wir versprechen*) und Architektur (Sicht: *so sieht es aus*). Es trägt die
eigenen technischen Festlegungen — Algorithmen, Defaults, Protokolle, ABI — und
**präzisiert** die Lastenheft-Anforderungen aufwärts, ohne sie zu schärfen (ADRs
dürfen die Spezifikation schärfen, nicht das Lastenheft). Eine Schärfung einer
Anforderung trägt die ID-Form `LH-<KAT>-<NN>.<a>` (Buchstaben-Suffix an der
geschärften Anforderung).

---

## 1. State Machine

```text
                ┌──────┐
                │ INIT │
                └──┬───┘
                   ▼
              ┌─────────┐ ─────────────────┐
              │ STANDBY │                  │
              └────┬────┘                  │
                   ▼                       │
              ┌─────────┐                  │
              │ READY   │ ◄─── Quittierung │
              └────┬────┘                  │
        ┌──────────┼─────────┐             │
        ▼          ▼         ▼             │
   ┌─────────┐ ┌──────┐ ┌─────────────┐    │
   │ IDLE    │ │CHARG.│ │ DISCHARGING │    │
   └────┬────┘ └──┬───┘ └──────┬──────┘    │
        └─────────┼────────────┘           │
                  ▼                        │
            ┌──────────┐                   │
            │ LIMITED  │                   │
            └────┬─────┘                   │
                 ▼                         │
        ┌──────────────────┐  ┌────────────┴────┐
        │ FAULT            │  │ MAINTENANCE     │
        └────────┬─────────┘  └─────────────────┘
                 ▼
        ┌──────────────────┐
        │ EMERGENCY_STOP   │   ◄── aus jedem Zustand erreichbar
        └──────────────────┘
```

`FAULT` und `EMERGENCY_STOP` übersteuern alle Betriebszustände
([LH-SM-002](lastenheft.md#lh-sm-002--sicherheitszustände-haben-vorrang)). `FAULT → READY` nur nach definierter Quittierung
([LH-SM-003](lastenheft.md#lh-sm-003--quittierung-von-fehlerzuständen)).

Bezug: [LH-SM-001](lastenheft.md#lh-sm-001--explizite-betriebszustände)..[003](lastenheft.md#lh-sm-003--quittierung-von-fehlerzuständen), [LH-SAFE-001](lastenheft.md#lh-safe-001--emergency-stop).

---

## 2. Adapter-Härtung: Fail-Closed in Production

Feld- und Transport-Adapter (MQTT, OPC-UA, optimization-core-Sidecar) sind
im Profil `Production` **fail-closed**:

- Transport ist verschlüsselt **und** authentifiziert; Secrets kommen aus
  Datei-/Secret-Mounts, nicht als Inline-Wert
  ([LH-API-008](lastenheft.md#lh-api-008--transport--und-netzwerkschutz),
  [LH-OPCUA-005](lastenheft.md#lh-opcua-005--opc-ua-security)).
- Unsichere Modi (Plaintext, `SecurityMode=None`, world-readable Sockets)
  sind **nur** in `Development`/`HilSimulator` und nur mit explizitem
  Opt-in **plus** dokumentierter Begründung zulässig; ein echter
  Production-Endpoint übernimmt diese Test-Profile nicht.
- Ein Production-Boot mit unsicherer, unvollständiger oder nicht
  fallback-fähiger Sicherheits-/Regelkonfiguration geht **nicht** in den
  aktiven Regelbetrieb über, sondern bricht beim Start mit benanntem Fehler
  ab ([LH-OPS-001](lastenheft.md#lh-ops-001--sicherer-start)). Das schließt
  den MPC-Pfad ein: Production startet nur mit Fallback-Optimierer und
  monotoner Uhr, reservierte Backends bleiben nicht-lauffähig.

---

## 3. Persistenz

| Bereich            | Detail                                                  | LH-Bezug         |
| ------------------ | ------------------------------------------------------- | ---------------- |
| RDBMS              | PostgreSQL; TimescaleDB optionaler Folgeausbau          | [LH-PERSIST-005](lastenheft.md#lh-persist-005--datenbank)   |
| Telemetrie         | Zeitstempel, AssetId, Werte, DataQuality, Quelle        | [LH-PERSIST-001](lastenheft.md#lh-persist-001--speicherung-von-messdaten)   |
| Commands           | jeder ausgegebene Command mit Reason und Source         | [LH-PERSIST-002](lastenheft.md#lh-persist-002--speicherung-von-commands)   |
| Fahrpläne          | versioniert für Day-Ahead, Intraday und Regelleistung   | [LH-PERSIST-003](lastenheft.md#lh-persist-003--speicherung-von-fahrplänen)   |
| Optimierungsläufe  | RunId, Inputs, Solverstatus, Objective Breakdown, erzeugte Fahrplanversion | [LH-PERSIST-007](lastenheft.md#lh-persist-007--speicherung-von-optimierungsläufen) |
| Operator-Audit     | Operator, Zeit, Aktion, Begründung, Ergebnis            | [LH-PERSIST-004](lastenheft.md#lh-persist-004--speicherung-von-operator-kommandos), [LH-OPS-004](lastenheft.md#lh-ops-004--auditierbarkeit) |
| Retention          | konfigurierbar, getrennt je Datentyp, kein Auto-Delete von Audit | [LH-PERSIST-006](lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen) |
| Persistenzfehler   | definiertes Verhalten, kein undefinierter Regelbetrieb  | [LH-PERSIST-006](lastenheft.md#lh-persist-006--aufbewahrung-und-datenvolumen)   |

Migrations-Strategie: versionierter Pfad ab M2 —
DDL aus einer neutralen `schema.yaml` per `d-migrate` (Build-Time)
generiert, zur Laufzeit per `DbUp` mit Tracking-Tabelle
`__schema_versions` angewendet. EF Core Migrations und FluentMigrator
sind als Alternativen geprüft und mit Begründung ausgeschlossen worden. `BessDbMigrator.MigrateAsync` ist
idempotent beim Worker-Start anwendbar und setzt vor DbUp einen
`pg_advisory_lock(hashtextextended('bess-ems:migrations', 0))`, sodass
mehrere Repliken sicher boot-rennen können.

---

## 4. Konfiguration

- Quelle: YAML/JSON-Dateien + Environment Variables, hierarchisch überlagernd.
- Bereiche: Assets, Capabilities, Device Points, Adapter, Mappings, Limits,
  Rampen, Markt-/Tarifparameter, Optimierungsparameter, Sicherheitsparameter
  und Northbound-Exports ([LH-CONF-001](lastenheft.md#lh-conf-001--externe-konfiguration)/[004](lastenheft.md#lh-conf-004--export--und-northbound-konfiguration)).
- Mappings (Modbus, MQTT, OPC-UA) versioniert in `config/`
  ([LH-CONF-002](lastenheft.md#lh-conf-002--versionierte-gerätemappings)).
- Validierung beim Start; bei Fehlern kein aktiver Regelbetrieb ([LH-CONF-003](lastenheft.md#lh-conf-003--validierung-der-konfiguration),
  [LH-OPS-001](lastenheft.md#lh-ops-001--sicherer-start)).

```text
config/
├─ assets/{assetId}.yaml
├─ adapters/modbus/{deviceProfile}.yaml
├─ adapters/mqtt/{deviceProfile}.yaml
├─ adapters/opcua/{deviceProfile}.yaml
├─ device-points/{profile}.yaml
├─ control/limits.yaml
├─ control/ramps.yaml
├─ markets/zones.yaml
├─ markets/tariffs.yaml
├─ exports/{target}.yaml                    # optionaler Folgeausbau
└─ safety/profiles.yaml
```

---

## 5. Native-Core-Strategie

### 5.1 Phasenmodell

```text
Phase 1 (M1/M2)      : .NET-only, kein Native Core
Phase 2 (M3)         : Native Library via P/Invoke
                       (Constraint, Ramp, PID, schnelle Plausi)
Phase 3 (M5)         : Native/externes Sidecar via gRPC
                       (MPC, State-Space, Solver-Anbindung)
Phase 4 (M6)         : Multi-Asset, UI, Kubernetes, Timescale-Option,
                       Edge-/Zertifizierungsgates ohne harte RT-Zusage
```

### 5.2 Bibliothek vs. Sidecar — Entscheidungskriterien

| Kriterium                        | Library (P/Invoke)        | Sidecar (gRPC)          |
| -------------------------------- | ------------------------- | ----------------------- |
| Latenz                           | sehr niedrig              | mittel                  |
| Crash-Isolation                  | nein (Prozessabsturz)     | ja                      |
| Deployment                       | ein Container             | zwei Prozesse           |
| Geeignet für                     | Limiter, Rampen, PID      | MPC, Solver, große Kerne |
| ABI-Stabilität                   | hoch erforderlich         | nur Protobuf-Vertrag    |

### 5.3 ABI-Regeln

- Stabile C-ABI, keine C++-Klassen/Exceptions exportieren ([LH-NATIVE-002](lastenheft.md#lh-native-002--stabile-c-abi)).
- Keine Speicherallokation über die Sprachgrenze ([LH-NATIVE-003](lastenheft.md#lh-native-003--keine-speicherallokation-über-sprachgrenzen)).
- Fehler über Statuscodes ([LH-NATIVE-004](lastenheft.md#lh-native-004--native-fehlercodes)).
- ABI-Version über Funktion abfragbar; Worker prüft beim Start
  ([LH-NATIVE-005](lastenheft.md#lh-native-005--abi-versionierung)). Die
  Version ist SemVer-artig: **major muss exakt matchen, minor darf höher
  sein** (additive Backward-Compat). Der Startup-Check klassifiziert
  deterministisch in fünf Endzustände: `disabled` (Opt-in nicht gesetzt),
  `library-missing`, `load-failed` (dlopen/Symbol-Lookup wirft),
  `abi-mismatch` (major ≠ erwartet **oder** minor < erwartet) und `loaded`.
- Native Komponenten reproduzierbar im Docker-Multi-Stage-Build
  ([LH-NATIVE-006](lastenheft.md#lh-native-006--container-build), [LH-DEPLOY-003](lastenheft.md#lh-deploy-003--multi-stage-build)/4).

### 5.4 Fallback

`BatteryEms.Adapters.NativeInterop` (`NativeFallbackControlKernel`)
implementiert denselben `IControlKernel`-Driven-Port wie die
.NET-Referenzimplementierung (`ManagedControlKernel`). Bei
fehlender Bibliothek, ABI-Mismatch oder nativem Fehler aus
validem .NET-Kontext (`BCC_STATUS_INVALID_INPUT` /
`BCC_STATUS_NON_FINITE` / `BCC_STATUS_NEGATIVE_DT` /
`BCC_STATUS_UNSUPPORTED_STATE`) ruft der Adapter im selben Tick
die Managed-Referenz und nutzt deren Ergebnis (Source =
`NativeFallbackToManaged`); der Regelkreis bleibt funktionsfähig
([LH-ARCH-006](lastenheft.md#lh-arch-006--native-core-als-optionaler-beschleuniger)).

Diese Default-Policy gilt verbindlich für M3. Eine produktive
Deployment-Variante darf zusätzlich
`NativeControlOptions.AbortOnAbiMismatch=true` setzen — dann führt
ein ABI-Mismatch beim Startup-Check zu einem harten Fehler statt
zum Managed-Fallback. Die Abort-Policy ist explizit Opt-in,
hat einen eigenen Integrationstest und überspielt nicht den
Default-Fallback-Vertrag ([LH-NATIVE-005](lastenheft.md#lh-native-005--abi-versionierung)).
