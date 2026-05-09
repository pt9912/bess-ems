# ADR 0004 — Native Control Kernel: Process Isolation

**Status:** Accepted — In-Process P/Invoke ist die Architektur
für die RM-M3-Closure und M3-D2-Aktivierung (post-RM-M3-03..05,
durch RM-M3-13-PID-Closure validiert); diese ADR fixiert die Wahl
für M3 + M3-D2 und benennt die Trigger (§4) für einen späteren
Out-of-Process-Pivot. Anwendungs-Profil-Caveats sind in §3
explizit (LH-RT-004 / LH-NF-005). Zwei unabhängige Review-Pässe
durchlaufen.
**Datum:** 2026-05-09
**Bezug:**
[`../planning/done/plan-RM-M3.md`](../planning/done/plan-RM-M3.md)
(RM-M3-03..05 ABI-Loader/P-Invoke/Routing, RM-M3-13 PID-Slice mit
state-tragender Surface),
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§13.1 (Phasenmodell),
§13.2 (Bibliothek vs. Sidecar — Entscheidungskriterien),
§13.4 (Fallback),
[`../../user/quality.md`](../../user/quality.md) §5.2 (Native ABI
Policy),
[ADR 0003 — Native Control Kernel: Implementierungs-Sprache](0003-native-kernel-language.md)
(orthogonale Entscheidung über die Sprachwahl).

---

## 1. Kontext

Der Native Control Core wird heute via P/Invoke **in-process**
geladen: der `BatteryEms.Worker`-Container hat
`libbattery_control_core.so` unter `/app/native/` und der
`BatteryEms.Adapters.NativeInterop`-Adapter ruft ihn mit
`NativeLibrary.Load` + `Marshal.GetDelegateForFunctionPointer`
direkt auf (`SystemNativeLibraryGateway`).

Das hat zwei orthogonale Konsequenzen:

- **Latenz** ist auf Funktionsaufruf-Niveau (Größenordnung
  sub-µs für den P/Invoke-Call inklusive `[StructLayout(Sequential)]`-
  Marshalling der vier BCC-Structs; eigenständige Messung steht
  noch aus, siehe ADR 0003 §3 zur Latenz-Quellen-Disziplin).
  Determinismus ist hoch — kein IPC-Scheduling-Jitter, kein
  Netzwerk-Stack, kein Prozesswechsel.
- **Blast Radius** ist der gesamte EMS-Container. Ein SIGSEGV
  oder Stack-Overflow im Native-Code reißt den .NET-Host mit;
  Recovery läuft über den Container-`HEALTHCHECK` und
  Kubernetes-Pod-Restart (~5–15 s Downtime, alle Telemetrie-
  Adapter, Schedule-Use-Cases und API-Endpunkte simultan offline).

Die Frage „bleiben wir in-process oder pivotieren wir auf
Out-of-Process?" wurde während RM-M3-13 explizit aufgeworfen, mit
besonderem Bezug auf die wachsende Zustandsbehaftetheit im
PID-Slice (Integrator-State, Anti-Windup-State, perspektivisch
Multi-Asset-PID mit per-Asset-State, RM-M5-Phase mit MPC und
Sliding-Window).

Architektur-Spec §13.1 nennt das Phasenmodell:

```
Phase 1 (MVP)        : .NET-only, kein Native Core
Phase 2 (post-MVP)   : Native Library via P/Invoke   ← M3, hier
Phase 3 (later)      : Native Sidecar via gRPC
Phase 4 (optional)   : Shared Memory / CPU Pinning / Edge Controller
```

§13.2 nennt die Entscheidungskriterien explizit:

| Kriterium                    | Library (P/Invoke)        | Sidecar (gRPC)          |
| ---------------------------- | ------------------------- | ----------------------- |
| Latenz                       | sehr niedrig              | mittel                  |
| Crash-Isolation              | nein (Prozessabsturz)     | ja                      |
| Deployment                   | ein Container             | zwei Prozesse           |
| Geeignet für                 | Limiter, Rampen, PID      | MPC, Solver, große Kerne |
| ABI-Stabilität               | hoch erforderlich         | nur Protobuf-Vertrag    |

Diese ADR fixiert die Entscheidung für die M3-Closure (Phase 2
gemäß §13.1) und benennt die Trigger, die einen Phase-3-Pivot
auslösen würden. Sie ergänzt §13.2, indem sie die Trigger
konkretisiert — die Spec-Tabelle nennt nur die Eigenschaften, nicht
die Übergangs-Bedingungen.

---

## 2. Entscheidung

| Achse                   | Entscheidung                                     | Pin / Trigger                                                                       |
| ----------------------- | ------------------------------------------------ | ----------------------------------------------------------------------------------- |
| Prozess-Modell M3       | **In-Process P/Invoke**                         | `BatteryEms.Adapters.NativeInterop` mit `NativeLibrary.Load` und `cdecl`-Delegates  |
| Recovery-Mechanismus    | Container-`HEALTHCHECK` + Kubernetes-Pod-Restart | `/health` / `/app/native/.so`-Check / `ldd`-Gate (RM-M3-06 Teil 2 + `make runtime`) |
| Fallback bei Native-Fehler aus validem Kontext | Managed-Fallback im selben Tick (kein Crash) | `NativeFallbackControlKernel.Source = NativeFallbackToManaged` (RM-M3-05) |
| Fallback bei ABI-Mismatch | Default Managed-Fallback; opt-in `AbortOnAbiMismatch=true` als Production-Policy | `NativeControlOptions` (RM-M3-03) |
| Out-of-Process-Pivot    | **Trigger-getrieben** (siehe §4); nicht für M3 gewählt | Phase-3-Übergang mit eigener ADR + Folge-Plan |

---

## 3. Achse 1 — Prozess-Modell-Optionen

### Optionen

**In-Process P/Invoke (gewählt für M3).** Der heutige Stand:

- `libbattery_control_core.so` wird beim Worker-Start via
  `NativeControlLoader.TryLoad` geladen (Library-Missing /
  ABI-Mismatch / Load-Failed → Managed-Fallback ohne Abort).
- Compute-Aufrufe gehen über `NativeControlKernel.Compute` und
  PID-Aufrufe über `NativeControlKernel.PidStep` mit value-typed
  Structs (Snapshot/Limits/Request/Command bzw. PidState/Options/
  Input/Command). Marshalling ist `[StructLayout(Sequential)]`
  ohne Heap-Allocation.
- Native-Fehler aus validem .NET-Kontext (`BCC_STATUS_INVALID_INPUT`,
  `NON_FINITE`, `NEGATIVE_DT`, `UNSUPPORTED_STATE`) führen
  deterministisch zum Managed-Fallback im selben Tick — kein
  Crash, kein Skip-Cycle.
- Memory-Bugs (Use-after-free, Buffer-Overflow, Stack-Smash) sind
  durch ASan + UBSan im `native-sanitizer`-Stage abgesichert
  (RM-M3-09); 100 % Line-Coverage in `native-coverage-gate`
  (RM-M3-09) plus 25-Cases-Replay-Parity (RM-M3-10) plus 21
  Wire-Tests durch P/Invoke (RM-M3-13) sind die Disziplin
  gegen Memory-Bugs zur Test-Zeit. **Offene Erweiterung:**
  Coverage-Guided Fuzzing (libFuzzer / AFL++) auf den
  `battery_control_core_compute`- und
  `battery_control_core_pid_step`-Exporten würde die
  Test-Time-Verteidigung über die handgepflegten Replay-Cases
  hinaus heben; aktuell nicht im Scope von M3, aber als
  Ergänzungs-Slice nach M3-D2 sinnvoll und unabhängig vom
  Out-of-Process-Pivot wertstiftend.
- Realer Crash heute: bisher null beobachtet, mit niedriger
  Eintrittswahrscheinlichkeit auf der heutigen Surface. Die
  `.so` ist 16 KB, hat null `NEEDED`-Einträge (`readelf -d`),
  keine Allocation, keine STL, keine Exception-Maschinerie (siehe
  ADR 0003), keine Transzendentalfunktionen. Ein realistischer
  Crash würde NULL-Deref oder Stack-Overflow voraussetzen; beide
  Klassen werden durch ASan + UBSan zur Test-Zeit erkannt
  (RM-M3-09 `native-sanitizer`-Stage hard-fail). PID erweitert
  die Surface, bleibt aber value-only — ein zukünftiger
  state-erweiternder Slice (Multi-Asset-State, MPC-State) verschiebt
  diese Risikoeinschätzung und triggert §4.

Konkrete Wins gegenüber Out-of-Process:

- **Determinismus.** Größenordnung sub-µs pro Call vs. µs–ms
  für IPC (siehe §3 Out-of-Process für die IPC-Latenz-Auflösung).
  1-Hz-Regulation gemäß LH-RT-004 braucht das nicht, aber ein
  perspektivisches 10–100-Hz-Ziel (Architektur §13.1 lässt das
  offen) wäre out-of-process mit Stress; LH-RT-004 grenzt das
  explizit auf Edge-Controller ab und ist damit kein
  M3-Architektur-Druck.
- **Kein IPC-Stack** im kritischen Pfad. Keine Serialisierung
  (Protobuf / FlatBuffers / shared-memory-Layout), kein
  Versions-Handshake jenseits der ABI-Version, kein Timeout-
  /Retry-/Supervisor-Stack.
- **Heutige Surface ist erschöpfend abgedeckt — Out-of-Process
  zahlt nur unter Trigger.** Die Surface ist value-only, 16 KB
  `.so` ohne `NEEDED`-Einträge, keine Allocation, keine STL,
  keine Exception-Maschinerie. Crash-Risiko ist durch ASan +
  UBSan + 100 %-Line-Coverage + Replay-Parity (RM-M3-09/10/13)
  zur Test-Zeit erschöpfend abgedeckt. Out-of-Process zahlt
  konkret erst, wenn diese Disziplin reißt (z. B. ein realer
  Crash in Production gemäß §4 Trigger 1) oder die Surface über
  value-only hinauswächst (Trigger 2 / Trigger 3). Das Argument
  steht **nicht** auf dem Sunk-Cost der RM-M3-03..13-Investition
  (das wäre Fallacy); es steht auf der Risiko-Abdeckung der
  heutigen Surface durch Test-Time-Disziplin.

**Out-of-Process Sidecar via gRPC (deferred, Phase-3-Pfad).**
Konkrete Wins **wenn die Surface staatszentrischer und
crash-relevanter wird**:

- **Crash-Isolation.** SIGSEGV im Kernel-Prozess kostet nicht den
  EMS-Container. Der Host detektiert per `keepalive` /
  `health-check-rpc` den Tod und startet den Kernel-Prozess
  intern neu (50–500 ms vs. Pod-Restart 5–15 s); während des
  Neustarts läuft der Managed-Fallback weiter, alle anderen
  Adapter (Telemetry, Schedule, API) bleiben oben.
- **Hot-Reload.** Ein neuer Kernel-Build kann ohne Container-
  Restart eingespielt werden (Kernel-Prozess stoppen, Binary
  ersetzen, Prozess neu starten). Heute ist Image-Rebuild +
  Pod-Restart der einzige Update-Pfad.
- **Sandbox-/Cgroup-Isolation.** Der Kernel kann mit
  cgroup-CPU-Pinning, RT-Priorität, Memory-Limits, seccomp-
  Filter laufen — ohne dass der .NET-Host darauf eingeschränkt
  wird.
- **Sprach-Choice unabhängig vom .NET-Stack.** Der Kernel könnte
  in einer anderen Sprache als die heutige C laufen (Rust, oder
  bei Phase 3 ggf. eine Solver-Bibliothek mit eigener Toolchain),
  ohne ABI-Stabilität-Druck — der gRPC-Vertrag ist Schnittstelle.

Aufwandsschätzung für einen Out-of-Process-Pivot der heutigen
Surface (Constraint, Ramp, PID): grob **4–8 Wochen** Gesamtaufwand
für ein verantwortbares Replika; ein Großteil von RM-M3-03..13
würde dabei durch IPC-Äquivalente ersetzt. Ein **kombinierter
Pivot** (Sprache + Prozess-Grenze, falls beide Trigger zusammen
zünden — siehe ADR 0003 §4 Trigger 5) ist nicht streng additiv:
das Toolchain-Setup für einen Rust-Kernel-Prozess wird **einmal**
gezahlt, nicht zweimal. Realistische Größenordnung **5–10
Wochen** für die heutige Surface (zufällig in derselben Range wie
die additive Rechnung 1–2 + 4–8 Wochen, weil das gesparte
Doppel-Tooling die Kombinations-Komplexität ungefähr aufwiegt).
Konkrete Zerlegung:

- **gRPC-Dienst** in der Native-Sprache (C: kein gRPC ohne
  Drittbibliotheken — würde wahrscheinlich Sprach-Pivot auf C++
  oder Rust mitziehen, siehe ADR 0003).
- **Protobuf-Vertrag** für Snapshot/Limits/Request/Command
  und PidState/Options/Input/Command. Versionierung,
  Backward-Compat-Disziplin parallel zur ABI-Header-Disziplin
  (jetzt überflüssig).
- **Supervisor in `BatteryEms.Adapters.NativeInterop`**:
  Prozess-Lifecycle-Management, Timeouts, Retries, Dead-Letter-
  Path, Healthcheck-Polling, Connection-Pool, Authentifizierung.
  Das ist die Größenordnung des heutigen ABI-Loaders mal 3–5.
- **Container-Topologie** ändert sich: aus einem Container
  werden zwei (oder ein Container mit zwei Prozessen plus
  Supervisor wie tini / s6-overlay). Compose-/Kubernetes-
  Manifeste, Healthcheck-Policy, Restart-Policy, Logging-
  Aggregation müssen alle nachgezogen werden.
- **Test-Surface explodiert.** Statt P/Invoke-Wire-Tests +
  Replay-Parity gegen die `.so` haben wir IPC-Contract-Tests,
  Timeout-Verhalten, Reconnect-Verhalten, Process-Crash-
  Recovery-Tests, plus Sanitizer-Pass im Kernel-Prozess vs.
  P/Invoke-Boundary-Tests im .NET-Host.

Verworfen **für M3** weil:

- Das echte Risiko (SIGSEGV im Kernel) ist heute praktisch null
  (siehe oben), und die Disziplinen die es so klein halten
  (ASan/UBSan, 100 %-Coverage, Replay-Parity) sind bereits da.
- Der Recovery-Mechanismus „Pod-Restart bei Crash" ist
  Kubernetes-Standard und akzeptabel im **heutigen Anwendungs-
  Profil**. Lastenheft-Anker: **LH-RT-004** definiert den
  Regelzyklus auf 1 Sekunde und sagt explizit „Das System
  beansprucht keine harte Echtzeitfähigkeit; Anforderungen mit
  härterem Zeitverhalten müssen über Edge-Controller oder
  herstellerspezifische Steuerungen abgegrenzt werden". **LH-NF-005**
  fordert Verfügbarkeit nur als „soll" mit der Abnahme
  „Kommunikationsfehler führen nicht zu undefiniertem Verhalten",
  ohne quantitative Downtime-Schwellen. Aus dieser Vertragslage
  sind 5–15 s Pod-Restart-Downtime tolerabel. Eine spätere Welle
  Richtung **Primärregelleistung / Frequency-Containment-Reserve
  / harte Realzeit-Pflicht** würde gemäß LH-RT-004 explizit als
  Edge-Controller-Pfad abgegrenzt — also ein neuer Architektur-
  Slice, nicht eine bess-ems-interne Optimierung; das wäre
  Trigger 4 aus §4. Die Sicherheits-State-Machine selbst bleibt
  in der Domain (.NET), der Kernel rechnet nur Setpoints aus.
- Der Aufwand (4–8 Wochen, siehe oben) liegt um Größenordnungen
  über dem verbleibenden Restaufwand für die M3-D2-Aktivierung
  (deren Slice-Plan steht noch aus, aber der Scope ist klein:
  `IPidKernel`-Port + Routing-Wiring + produktionsnahes Profil),
  ohne proportionalen Sicherheitsgewinn auf der heutigen
  value-only Surface.

**Latenz-Größenordnungen für die Out-of-Process-Optionen** (für
die nachfolgenden Sub-Diskussionen):

- **gRPC über Loopback (Protobuf-Encode/Decode + HTTP/2):** p50
  grob 200 µs–2 ms je nach Payload, Tail unter Last 5–10 ms.
- **Unix-Domain-Socket** mit eigenem Wire-Format: 10–30 µs
  Roundtrip ohne Serialisierung; mit Protobuf-Encode/Decode
  realistisch 30–100 µs.
- **Shared Memory** mit lock-free Ringpuffer oder Futex-Locks:
  sub-µs (nicht „ns", weil Cache-Kohärenz und Memory-Barrier-
  Kosten eingerechnet sind).
- **fork+pipe** (Worker forkt Kernel-Subprozess, IPC über
  anonymous pipes oder Unix-Socketpair): vergleichbar mit
  Unix-Socket (10–30 µs roh, 30–100 µs mit Serialisierung).

Diese Werte sind aus öffentlichen Benchmarks abgeleitet, **nicht
eigenständig gemessen** — eine Folge-Slice mit `BenchmarkDotNet`
oder einem dedizierten Latenz-Benchmark würde die Zahlen für
unsere konkrete Topologie konkretisieren.

**Out-of-Process Sidecar via Unix-Socket / Shared Memory
(verworfen für M3, Phase-4-Material).** Würde den Latenz-Hit
gegenüber gRPC reduzieren, ist aber komplexer zu betreiben
(eigene Wire-Format-Disziplin, eigene Synchronisation,
Versions-Handshake auf Bytefluss-Ebene) und löst die ABI-Frage
nicht — ein Shared-Memory-Layout ist auch eine ABI mit eigener
Versionierungs-Disziplin. Phase-4-Material gemäß §13.1.

**Out-of-Process Sidecar via fork+pipe (verworfen für M3).**
Klassisches Unix-Pattern: Worker forkt einen Kernel-Subprozess
beim Start, IPC über anonymous pipes oder Unix-Socketpair mit
einem minimalen Length-Prefix-Wire-Format (4 Byte length + N Byte
Payload). Crash-Recovery durch fork-respawn. Vorteile gegenüber
gRPC: kein Protobuf-Toolchain-Overhead, kein HTTP/2-Stack, kein
Service-Discovery; die `bcc_*_t`-Structs könnten direkt
serialisiert werden. Verworfen für M3 weil:

- **Eigene Wire-Versionierung** wäre nötig — die ABI-Disziplin
  von §13.3 muss durch eine Bytefluss-Disziplin ersetzt werden,
  die ihrerseits Backward-Compat-Tests braucht.
- **Plattform-Bindung** (fork ist Unix-spezifisch); Windows-Hosts
  sind heute kein M3-Ziel, aber der Wir-bauen-für-Linux-Default
  würde damit härter eingebrannt als nötig.
- **Geringer Mehrwert vs. gRPC bei Phase 3.** Wenn wir
  out-of-process gehen, dann meist um zugleich MPC oder Solver-
  Kerne mit anzubinden — die brauchen einen reichhaltigeren
  IPC-Stack (Streaming, Cancellation, Health-Checks), den gRPC
  liefert. fork+pipe wäre eine billigere Reise mit kleinerem
  Ziel-Korridor.

Bleibt im Auge falls der Out-of-Process-Pivot ohne Phase-3-
Komponenten zündet (Trigger 1 oder 4 in §4 ohne Trigger 2).

**Out-of-Process via REST/HTTP (verworfen).** Über Loopback mit
JSON-Payload würde Roundtrips vergleichbar mit gRPC haben (p50
~200 µs–2 ms), nur mit höherem Per-Call-Overhead durch
HTTP-Header-Parsing und JSON-Decode. Kein Vorteil gegenüber
gRPC — gleiche Latenz-Klasse, schwächeres Schema-Tooling, keine
Streaming-Semantik. Erwähnt für Vollständigkeit; keine
Re-Evaluierungs-Trigger reservieren ihn.

**Out-of-Process via WebAssembly-Sandbox (Wasmtime / Wasmer als
embedded Runtime, verworfen für M3).** Reale Zwischenoption
zwischen In-Process P/Invoke und einem voll separierten Prozess:
der Kernel läuft als WASM-Modul innerhalb des .NET-Hosts, aber
in einer Linear-Memory-Sandbox die der Host-Adressraum nicht
sieht. Crash-Isolation auf der Granularität des WASM-Traps
(Memory-Bug → Trap → kontrollierter Module-Restart, kein Host-
Crash). Verworfen für M3 weil:

- **Toolchain-Reife für deterministische Compute-Pfade.** WASM
  garantiert Memory-Isolation, aber Floating-Point-Bit-Determinismus
  über WASM-Implementierungen hinweg ist subtil — die
  RM-M3-10-Replay-Parity-Tests setzen bit-exakte Ergebnisse
  voraus (Toleranz 1e-12 als Headroom für FMA-Kontraktion); ein
  WASM-Pivot würde die Parity-Test-Disziplin neu definieren.
- **Performance-Kosten.** Wasmtime-Calls in den Host-Adressraum
  haben einen Overhead in der gleichen Klasse wie gRPC-Loopback
  (~µs), trotz weiterer In-Process-Form. Die Disziplin „in-process"
  liefert den Latenz-Vorteil hier nicht.
- **Linguistische Komplexität.** Ein WASM-Pivot impliziert
  Sprach-Pivot mit, weil C-zu-WASM in der Praxis über
  Emscripten/WASI-SDK läuft und neue Toolchain-Sorgfalt fordert
  (siehe ADR 0003 für die Sprach-Achse).

Bleibt im Auge als Mittelweg falls Crash-Isolation gefordert ist
ohne den vollen Aufwand eines IPC-Sidecars zu wollen — typische
Trigger-Kombination: Trigger 1 (Production-Crash) ohne Trigger 2
oder 6.

**Hardened-Sandbox-Container für den Kernel (gVisor / Firecracker,
verworfen für M3).** gVisor ist ein User-Space-Kernel-Reimplementat
(Linux-Syscall-Filter via Sentry-Prozess + Gofer-FS-Adapter),
Firecracker ein microVM-Hypervisor. Beide sind faktisch
**Out-of-Process-Lösungen** mit zusätzlicher Sandbox-Schicht
gegenüber dem nackten Sidecar-Pattern: der Kernel läuft in einem
isolierten Adressraum **plus** einem reduzierten
Syscall-/Hardware-Surface. Wert: Defense-in-Depth gegen Memory-
Korruption + Privilege-Escalation in einem Schritt. Verworfen für
M3 als „Out-of-Process plus extra Härtung" — zünden konkret bei
Trigger 6 (Funktionale Sicherheit / Zertifizierung) zusätzlich
zu einem ohnehin gewählten Out-of-Process-Pivot, nicht als
Standalone-Option.

**In-Process MAC-Schichten (`seccomp-bpf`, AppArmor/SELinux,
Capability-Dropping).** Filtern Syscalls, beschränken Privilegien,
erzwingen Mandatory-Access-Control-Profile. Lösen Crash-Isolation
**nicht** — ein Memory-Bug innerhalb des Host-Adressraums (Use-
after-free, Buffer-Overflow im Datensegment, Stack-Smash)
korrumpiert den Host weiterhin. Sind als orthogonale Defense-in-
Depth-Schicht *zusätzlich* zur Prozess-Wahl wertvoll
(syscall-Filter im Worker-Container schließen aus dass
fehlgeschlagene Native-Aufrufe versuchen Privilegien zu
eskalieren), aber ersetzen die Process-Isolation-Diskussion
nicht. Wird hier nicht als Alternative zum Out-of-Process-Pivot
geführt, sondern als eigenständige Defense-in-Depth-Slice die
unabhängig von dieser ADR betrachtet werden kann.

---

## 4. Achse 2 — Trigger für Out-of-Process-Pivot

Die Re-Evaluierung als Out-of-Process-Sidecar ist explizit
deferred. Sie wird mit einer eigenen ADR + einem eigenen Folge-
Slice gezogen, sobald einer der folgenden Trigger zündet:

1. **Realer Native-Crash in Production.** Ein einziger SIGSEGV /
   SIGABRT im Native-Code, der den EMS-Container in Production
   gerissen hat — unabhängig davon ob er sofort durch Pod-
   Restart aufgefangen wurde. Postmortem mit Root-Cause +
   ADR-Trigger.
2. **Phase-3-Komponenten (MPC, State-Space, Solver) kommen
   in Scope.** Die Architektur-Spec §13.1 sieht Phase 3 als
   „Native Sidecar via gRPC" vor. Sobald RM-M5 oder eine andere
   Welle den `state_space_core` oder `optimization_core`
   konkret macht, ist out-of-process der Default für die
   neuen Komponenten — und es lohnt sich,
   `battery_control_core` in den gleichen Pivot mitzunehmen
   statt Hybrid-Stack zu betreiben. **Bündel-Trigger:** dieser
   Trigger ist faktisch dasselbe Architektur-Event wie Trigger 2
   in ADR 0003 („MPC-/State-Space-Kern in Scope"); die zwei ADRs
   erfassen ihn aus zwei Blickwinkeln (Prozess-Grenze vs.
   Sprache), aber er wird durch eine **gemeinsame Folge-ADR**
   adressiert, nicht durch zwei separate. Aufwandsschätzung für
   den kombinierten Pivot in §3 oben: grob 5–10 Wochen.
3. **Multi-Asset oder Multi-Tenant mit Isolation-Anforderung.**
   Sobald ein einziger EMS-Host mehrere Assets oder mehrere
   Mandanten gleichzeitig regelt und ein Crash in einem Asset-
   Kernel die anderen nicht runterreißen darf, ist
   Out-of-Process der Standard-Ausweg (ein Kernel-Prozess pro
   Asset, oder ein Kernel-Prozess pro Mandant).
4. **Realtime-/RT-Priorität / CPU-Pinning erforderlich.** Sobald
   eine RT-Anforderung (z. B. harte Latenz-Bound für
   Primärregelleistung, höher als 1 Hz hochgezogene Regulation
   mit harten Tail-Latenz-SLAs) auf den Tisch kommt, ist ein
   dedizierter Kernel-Prozess mit `SCHED_FIFO`-Priorität, CPU-
   Pinning und ggf. preempt-rt-Kernel der etablierte Ausweg.
   In-process im .NET-Host kann Tail-Latenzen durch GC-Pausen,
   Just-in-Time-Kompilierung oder Thread-Pool-Scheduling-Jitter
   ohne harte Vermeidung nicht garantieren — nicht weil
   irgendeine dieser Komponenten heute messbar Probleme macht,
   sondern weil ein RT-Kontext sie als Risiko ausschließt.
   **Achtung:** dieser Trigger überlappt teilweise mit Trigger 6
   (Zertifizierung), aber sie sind nicht identisch — eine
   regulatorische Anforderung mit RT-Pflicht zündet beide; eine
   Zertifizierungsanforderung ohne harte RT (z. B. funktional-
   sichere Steuerung mit weichen Latenz-Bounds) zündet nur
   Trigger 6; eine RT-Anforderung ohne formale Zertifizierung
   (z. B. Marktteilnahme an FCR mit kontraktlicher Latenz-Pflicht
   ohne IEC 61508) zündet nur Trigger 4. Beide separat halten,
   damit eines ohne das andere zünden kann.
5. **Hot-Reload / In-Service-Upgrade des Kernels gefordert.**
   Sobald Operations einen Kernel-Update ohne EMS-Host-Restart
   fahren möchte (z. B. ein Hotfix für einen Algorithmus-Fehler
   ohne Auflauf von Telemetry-Backlog während des Pod-Restarts),
   ist out-of-process der konkrete Antwortpfad.
6. **Funktionale Sicherheit / Zertifizierung mit Crash-
   Isolation-Anforderung.** Sobald eine Norm den Kernel als
   eigenen Failure-Domain fordert (klassisches IEC-61508-/
   ISO-26262-Argument), ist out-of-process die zertifizierbare
   Architektur. Siehe Trigger 4 für die Abgrenzung.
7. **Performance-Trigger.** Sobald eine wiederholbare Messung
   zeigt dass die heutige in-process-Architektur eine konkrete
   p99-Latenz-Schwelle pro Tick reißt (Beispiel-Schwellen je nach
   zukünftigem Anwendungsprofil: p99 > 50 ms bei 1 Hz, p99 > 5
   ms bei 10 Hz, p99 > 500 µs bei 100 Hz), ist die
   Architektur-Frage neu zu stellen — Out-of-Process würde
   mit Latenz-Hit zwar **nicht** helfen, aber ein dedizierter
   Kernel-Prozess mit RT-Priorität (Trigger 4) oder ein
   anderer Pfad (Edge-Controller gemäß LH-RT-004) wären die
   konkreten Antworten. Dieser Trigger erfordert eine eigene
   Mess-Slice die p99-Latenzen erst messbar macht; aktuell gibt
   es kein `BenchmarkDotNet`-Gate dafür.

Kein einzelner dieser Trigger zündet automatisch — alle erfordern
eine separate ADR-Diskussion. Aber jeder ist ein klarer Re-
Evaluierungs-Anlass.

---

## 5. Konsequenzen

### Positiv

- **M3-Closure ist deployable.** Der heutige in-process-P/Invoke-
  Stack hat alle RM-M3-Pflichten geschlossen (RM-M3-01..13 ✅).
  Eine Pivot-Diskussion vor M3-Aktivierung würde 4–8 Wochen
  Replikations-Aufwand kosten (siehe §3) ohne proportionalen
  Sicherheitsgewinn auf der heutigen Surface — die Risiko­abdeckung
  wandert von „Compile-Time + Test-Time + Container-Restart" zu
  „Compile-Time + Test-Time + IPC-Health-Check + Prozess-
  Restart", was eine andere Architektur ist, kein offensichtlich
  sicherereres System auf einer kleinen value-only Surface.
- **Determinismus auf Funktionsaufruf-Niveau.** Keine IPC-
  Latenz, keine Serialisierung, keine Reconnect-Logik.
  Regulation-Cycle bleibt deterministisch im Sinne von
  „identische Inputs in identischer Sequenz produzieren
  identische Outputs", was die RM-M3-10-Parity-Tests bit-exakt
  voraussetzen.
- **Kleine Fehler-Surface zur Test-Zeit gefangen.** Die
  RM-M3-09-Sanitizer + 100 %-Coverage + RM-M3-10-Parity +
  RM-M3-13-Wire-Tests sind die Verteidigung gegen die einzige
  Crash-Klasse die in-process kostet (Memory-Bugs).
- **Recovery-Pfad ist Standard-Kubernetes.** `HEALTHCHECK`
  fail't bei /health-Ausfall, Pod-Restart läuft die übliche
  Restart-Policy. Operations-Pfad ist nichts Besonderes.

### Negativ

- **Blast-Radius eines unentdeckten Native-Crashs ist der ganze
  Container.** Telemetry-Adapter (Modbus, MQTT), Schedule-
  Optimizer, API-Endpunkte und Worker gehen alle gleichzeitig
  offline für die Pod-Restart-Zeit. Mitigation: 100 %-Coverage
  + Sanitizer + Replay-Parity zur Test-Zeit; in der Praxis
  null Crashes bisher.
- **Recovery-Zeit ist Pod-Restart, nicht Prozess-Restart.**
  5–15 s vs. 50–500 ms. Für 1Hz-Regulation tolerabel, für
  einen Echtzeit-Pfad mit harter Latenz-Anforderung nicht.
- **Sprach-Pivot (ADR 0003) ist mit Out-of-Process günstiger
  als in-process.** Wenn beide Pivots gewollt werden, sollten
  sie zusammen passieren — die ABI-Diskussion entfällt zugunsten
  eines Protobuf-Vertrags. Diese ADR macht das explizit, indem
  sie Trigger 2 als Bündel-Trigger nennt.

### Neutral

- **`spec/architecture.md` §13.2 bleibt unverändert.** Die
  Tabelle dort listet die Eigenschaften beider Modelle korrekt;
  diese ADR konkretisiert nur die Pivot-Trigger.
- **`docs/user/quality.md` §5.2 (Native ABI Policy) bleibt
  unverändert.** Die ABI-Mismatch / Load-Failed / Managed-
  Fallback-Logik (RM-M3-03) trägt den heutigen Stack; ein
  Out-of-Process-Pivot würde sie durch eine IPC-Reconnect-/
  Healthcheck-Policy ersetzen, aber nicht entfernen.
- **Die RM-M3-FUP-Slices (Persistence-Migrationen, OP-OPEN-05/
  06, Replay-Carve-outs) sind orthogonal.** Sie zünden nicht
  diese ADR und werden nicht durch sie blockiert.

---

## 6. Sequenz und Aktivierung

1. **M3-Closure (RM-M3-01..13):** abgeschlossen. In-Process
   P/Invoke ist die produktive Architektur.
2. **M3-D2 (offen):** produktive Profilaktivierung von Native als
   bevorzugtem Pfad in einem Profil.
   `NativeControlOptions.Enabled=true` + `NativeFallbackControlKernel`
   im Routing. Bleibt in-process. Diese ADR ist Voraussetzung
   für M3-D2 — sie muss vorher mindestens auf Status `Accepted`
   stehen, weil M3-D2 sonst implizit den Architektur-Pfad
   festlegt.
3. **Trigger-Watch:** die §4-Liste ist Operator-Verantwortung.
   Sobald ein Trigger zündet, wird eine **neue ADR** (z. B.
   `0006-native-kernel-out-of-process.md`) gezogen, mit
   konkretem IPC-Vertrag, Supervisor-Design, Container-Topologie,
   Migration-Plan und einer Aufwandsschätzung die §3 ersetzt.
   Diese ADR (0004) wird dann zu **Superseded by 0006** umgesetzt.
4. **Phase-3-MPC (Architektur §13.1):** der nächste
   architektonisch große Punkt, an dem die Prozess-Frage neu
   gestellt wird. Wenn `state_space_core` als gRPC-Sidecar
   konkretisiert wird, ist das der natürliche Trigger 2 aus §4.
   In dem Fall lohnt sich, `battery_control_core` in denselben
   Pivot mitzunehmen statt Hybrid-Stack zu betreiben.

Bis ein Trigger zündet bleibt diese ADR `Accepted` und
in-process P/Invoke ist die Prozess-Architektur des Native
Control Cores.
