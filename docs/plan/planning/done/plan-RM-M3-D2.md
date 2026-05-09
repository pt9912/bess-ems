# RM-M3-D2 — Produktive Profilaktivierung Native Control Routing

**Dokumenttyp:** Slice-Plan
**Status:** Abgeschlossen — alle 7 Arbeitspakete (M3-D2-01..07) grün, alle Akzeptanzkriterien erfüllt, `make gates` / `make ci` / `make runtime` reproduzierbar grün.
**Bezug:**
[`../in-progress/plan-RM-M3.md`](../in-progress/plan-RM-M3.md) (M3-D2 als „Separater Folge-Slice
nach abgeschlossenen RM-M3-03/05/06/07/10/11/12"),
[`../../adr/0003-native-kernel-language.md`](../../adr/0003-native-kernel-language.md)
(Sprache C bleibt für M3-D2),
[`../../adr/0004-native-kernel-process-isolation.md`](../../adr/0004-native-kernel-process-isolation.md)
(In-Process P/Invoke bleibt für M3-D2),
[`../../../user/quality.md`](../../../user/quality.md) §5.2
(Native ABI Policy: Default-Managed-Fallback, opt-in
`AbortOnAbiMismatch`),
[`../../../../spec/architecture.md`](../../../../spec/architecture.md)
§13.4 (Fallback-Vertrag),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md)
LH-NATIVE-001 (C/C++ für performance-kritische Komponenten)

---

## 1. Zweck

RM-M3-01..13 hat den Native Control Core gebaut, getestet,
hardware-bereit installiert (`/app/native/libbattery_control_core.so`)
und mit allen Quality-Gates abgesichert. **Aktiviert** ist er aber
nicht: das Default-DI-Wiring registriert keinen `IControlKernel`,
und `ControlCycleUseCase` fällt auf `new ManagedControlKernel()`
im Konstruktor zurück. M3-D2 schließt diese Lücke — ein
produktionsnahes Konfigurationsprofil setzt `NativeControl:Enabled=true`
und der Host registriert den `NativeFallbackControlKernel` statt
der Managed-Referenz, sodass der heiße Pfad produktiv durch die
`.so` läuft.

Die ADRs 0003 (Sprache: C) und 0004 (Prozess-Modell: In-Process
P/Invoke) sind die Architektur-Anker für diesen Slice. Kein neuer
Architektur-Pfad — nur Aktivierung des bereits gebauten.

---

## 2. Aktivierungsbedingungen

Alle ✅ — siehe [`plan-RM-M3.md`](plan-RM-M3.md):

| Bedingung | Status |
|-----------|--------|
| RM-M3-03 ABI-Loader mit `Disabled`/`Missing`/`LoadFailed`/`Mismatch`/`Loaded` | ✅ |
| RM-M3-05 `IControlKernel`-Port + Routing + Managed-Fallback bei Native-Fehler | ✅ |
| RM-M3-06 Teil 2 Runtime-Image-Pfad `/app/native/` + ldd-Gate | ✅ |
| RM-M3-07 Layout-/ABI-/non-finite-Tests gegen echte `.so` | ✅ |
| RM-M3-10 Replay-Parity (25 Cases) | ✅ |
| RM-M3-11 `make gates`/`ci` zieht alle Native-Quality-Gates mit | ✅ |
| RM-M3-12 Doku-Sync (Adaptername, Header-Pfad, Coverage-Scope, Policy) | ✅ |

---

## 3. Scope

**In Scope:**

- DI-Registrierung von `IControlKernel`: bei
  `NativeControl:Enabled=false` (Default) → `ManagedControlKernel`;
  bei `Enabled=true` → `NativeFallbackControlKernel` mit echter
  `.so`, mit deterministischem Managed-Fallback bei Native-Fehler
  aus validem Kontext.
- Loader-Handshake beim Host-Start: `NativeControlLoader.TryLoad`
  klassifiziert `LibraryMissing` / `LoadFailed` / `AbiMismatch` /
  `Loaded`; auf alle Nicht-`Loaded`-Pfade fällt das Wiring auf
  `ManagedControlKernel` zurück (Default-Policy gemäß
  `quality.md` §5.2).
- Opt-in `AbortOnAbiMismatch=true` → harter Startup-Abbruch bei
  ABI-Mismatch (Production-Policy aus §5.2).
- Strukturiertes Logging (RM-M3-03 Logger-Messages) bleibt
  unverändert; in `Disabled`/`Missing`/`LoadFailed`/`Mismatch`/
  `Loaded` werden die existierenden Log-Events mit
  `native_control_status=*` emittiert.
- Unit-Tests für die DI-Extension.
- Integrationstest, der den realen Host-Build mit
  `NativeControl:Enabled=true` und `LibraryPath=/app/native/...so`
  spinnt und prüft, dass die produktive Konfiguration den
  `NativeFallbackControlKernel` registriert hat.
- Dokumentation: `appsettings.json`-Struktur kommentiert,
  Verweis auf ADR 0003+0004 in der Plan-Doku, Eintrag in
  `quality.md` §5.2 (M3-D2 als geschlossen markiert).

**Out of Scope (separate Folge-Slices):**

- **PID native routing**. `BatteryEms.Domain.PidController.Step`
  ist heute nicht im Regelzyklus verdrahtet (RM-M2-08 lieferte
  PID als Domain-Primitive ohne produktive Verdrahtung). Native
  PID via `bcc_pid_step` (RM-M3-13) ist über die ABI verfügbar,
  aber ohne Konsumenten ergibt eine Routing-Aktivierung nichts.
  Eigenständiger Slice „M3-D3 PID-Routing" sobald ein konkreter
  PID-Konsument im Regelzyklus vorhanden ist.
- **Production-Profil-Defaults zentralisieren**. Heute leben
  Konfigurations-Defaults in mehreren `appsettings.json`-Stages
  (Host, Tests, deploy/compose). Eine Umstellung auf eine zentrale
  Profil-Strategie (z. B. `appsettings.Production.json` /
  Environment-spezifische Overrides) ist eigenständige
  Operations-Arbeit jenseits dieses Slices.
- **`NativeControl`-Gesundheits-Endpoint**. Ein dedizierter
  `/health/native`-Probe-Endpoint mit Loader-Status wäre nützlich
  für Operations, ist aber nicht Teil von M3-D2 — der heutige
  `/health` deckt Container-Health ab.
- **Out-of-Process / Sprach-Pivot**. ADR 0004 / 0003 — separate
  Trigger-getriebene Folge-Slices.

---

## 4. Arbeitspakete

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ✅ | M3-D2-01 | `NativeControlLoadResult.Handle` (`nint?`) | Loader-Result trägt den OS-Handle bei `Loaded`-Status; null sonst. Existierende Loader-Tests werden um Handle-Assertion erweitert. Kein anderer Caller heute, keine Migrations-Last. |
| ✅ | M3-D2-02 | `NativeInteropRegistration.AddBessNativeControl(IConfiguration)` | DI-Extension in `BatteryEms.Adapters.NativeInterop`, registriert `IControlKernel` als Singleton — Implementierung abhängig vom Loader-Result. Default `Enabled=false` → Managed. `Enabled=true` + `Loaded` → NativeFallback. `Enabled=true` + Nicht-Loaded → Managed (Default-Policy) ODER throw (Abort-Policy bei Mismatch). |
| ✅ | M3-D2-03 | DI-Wiring in `BessHostBuilder.BuildApp` | `AddBessNativeControl(builder.Configuration)` zwischen `AddBessApplicationInMemoryStores` und `AddBessWorker`; `IControlKernel` ist ab dann explizit im Container, der Konstruktor-Default in `ControlCycleUseCase` wird nicht mehr getroffen. |
| ✅ | M3-D2-04 | `appsettings.json`-Default + Native-Profil | Default-`appsettings.json` enthält `"NativeControl": { "Enabled": false }` (M1-Verhalten unverändert). Ein zusätzliches `appsettings.Native.json` (oder Env-Variable `NativeControl__Enabled=true`) flippt die Aktivierung — beides dokumentiert in `docs/user/quality.md` §5.2. |
| ✅ | M3-D2-05 | Unit-Tests `AddBessNativeControlTests` | Vier Pfade: (a) Default disabled → Managed registriert; (b) Enabled, Library missing → Managed registriert (Fallback-Policy); (c) Enabled, AbiMismatch ohne Abort → Managed; (d) Enabled, AbiMismatch mit Abort → Throws bei Container-Build. |
| ✅ | M3-D2-06 | Integrationstest in `BatteryEms.NativeInterop.IntegrationTests` | Dreht den realen Host-Build mit `NativeControl:Enabled=true` und Pfad zur echten `.so`, prüft `IControlKernel` ist `NativeFallbackControlKernel` (oder dessen aktiver Kompositions-Marker). Läuft im `test-native-interop`-Stage (Category!=Parity). |
| ✅ | M3-D2-07 | Plan/Doku-Update | `plan-RM-M3.md` Aktivierungsbedingungs-Tabellenzeile auf „M3-D2 abgeschlossen"; `docs/user/quality.md` §5.2 ergänzt um den DI-Aktivierungs-Pfad; `roadmap.md` "Aktueller Stand"-Block reflektiert M3-D2-Closure. |

---

## 5. Akzeptanzkriterien

- `make gates` / `make ci` bleiben grün.
- Default-Konfiguration (`appsettings.json` ohne `NativeControl`-
  Section) verhält sich byte-identisch zum Pre-M3-D2-Stand: alle
  bestehenden Application-/Worker-Tests bleiben grün ohne
  Anpassung.
- Mit `NativeControl:Enabled=true` (im Native-Profil) registriert
  der Host den `NativeFallbackControlKernel` und der
  Regelzyklus berechnet Constraint+Ramp produktiv durch die
  `.so`. Der Integrationstest aus M3-D2-06 belegt das.
- Bewusst kaputter Library-Pfad (Datei nicht vorhanden) führt
  deterministisch zu `ManagedControlKernel`-Registrierung — der
  Host startet sauber, `/health` ist grün.
- ABI-Mismatch mit `AbortOnAbiMismatch=true` führt zum harten
  Startup-Fehler — der Host startet nicht.
- `runtime`-Smoke (`make runtime`) bleibt grün; das M3-D2-Wiring
  ändert die runtime-Image-Inhalte nicht (`/app/native/.so` ist
  schon da).

---

## 6. Risiken und Tradeoffs

- **Singleton-Lifetime des Native-Kernels.** `NativeControlKernel`
  ist `IDisposable`; im DI-Container als Singleton registriert
  wird er bei App-Shutdown via `IServiceProvider.Dispose()`
  freigegeben (ruft `NativeLibrary.Free` über die Gateway).
  Mehrfach-Singleton-Konstruktion ist ausgeschlossen, weil DI
  den Singleton-Cache hält.
- **Library-Refcount.** `NativeControlLoader.TryLoad` öffnet die
  `.so` einmal via `NativeLibrary.Load` und benutzt das Handle
  nur für den ABI-Check; das Handle wird nicht freigegeben. Die
  M3-D2-01-Erweiterung gibt das Handle an den Caller weiter, der
  es im `NativeControlKernel` per Dispose schließt — ein Leak
  pro Load wäre auf Singleton-Niveau harmlos, aber sauber gelöst
  ist sauberer.
- **AbortOnAbiMismatch im DI-Konstruktor.** Wenn die
  Abort-Policy aktiv ist UND die Library `AbiMismatch` meldet,
  wirft der DI-Singleton-Factory-Delegate. Der Host wirft den
  Fehler beim ersten Service-Resolve, nicht beim Container-Build.
  Das matchen wir bewusst auf den existierenden
  `NativeControlLoader.ApplyAbortPolicy`-Vertrag (RM-M3-03), der
  `InvalidOperationException` wirft.

---

## 7. Sequenz

1. M3-D2-01 (Loader-Handle).
2. M3-D2-02 (DI-Extension) plus M3-D2-05 (Unit-Tests).
3. M3-D2-03 (Host-Wiring) plus M3-D2-04 (Config-Default).
4. M3-D2-06 (Integrationstest).
5. M3-D2-07 (Doku/Plan).
6. `make gates` + `make ci` + `make runtime` Verifikation.
