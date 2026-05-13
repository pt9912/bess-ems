# ADR 0003 — Native Control Kernel: Implementierungs-Sprache

**Status:** Accepted — C ist die Implementierungssprache für die
RM-M3-Closure (post-RM-M3-09-Pivot, durch RM-M3-13-PID-Closure
validiert); diese ADR fixiert die Wahl für M3 und benennt die
Trigger für eine spätere Re-Evaluierung als Rust. Rust-Pivot ist
als Folge-Slice mit klar benannten Triggern (§4) offen, aber nicht
für M3 gewählt. Zwei unabhängige Review-Pässe durchlaufen.
**Datum:** 2026-05-09
**Bezug:**
[`../planning/done/plan-RM-M3.md`](../planning/done/plan-RM-M3.md)
(RM-M3-01..13, insbesondere RM-M3-09-Closure mit dem C-Pivot und
RM-M3-13-Closure mit dem PID-Slice),
[`../../../spec/architecture.md`](../../../spec/architecture.md)
§13 (Native-Core-Strategie) + §13.3 (ABI-Regeln),
[`../../../spec/lastenheft.md`](../../../spec/lastenheft.md)
(LH-NATIVE-001..006),
[`../../user/quality.md`](../../user/quality.md) §1.2 (Native
Lint), §3.2 (Native-Coverage), §6 (Native-/.NET-Parity),
[ADR 0004 — Native Control Kernel: Process Isolation](0004-native-kernel-process-isolation.md)
(orthogonale Entscheidung über die Prozess-Grenze).

---

## 1. Kontext

Der Native Control Core (`battery_control_core`) wurde in RM-M3-02
zunächst in C++17 begonnen (Anonymous-Namespace-Helfer, `&`-
Referenzen, `try`/`catch (...)` an der C-ABI-Grenze als
Defense-in-Depth) und in RM-M3-09 auf C11 umgestellt. Die
Reihenfolge: zuerst stand die Sprachfrage fest (die Surface war
bereits „C-mit-Zucker" — keine Klassen, keine STL, keine
Allocation, keine real geworfenen Exceptions); der Pivot war die
Konsequenz daraus, mit zwei sauberen Nebenwirkungen — der `try`/
`catch (...)`-Block + sein dauerhaft toter Code-Pfad
verschwanden, und der gleichzeitig im RM-M3-09-Slice eingeführte
100 %-Line-Coverage-Gate konnte ohne `GCOVR_EXCL`-Begründungen
auskommen.

Mit RM-M3-13 (PID-Slice) wächst die Surface erstmals um echten
zustandsbehafteten Code:

- Integrator und Previous-Error werden über Ticks fortgeschrieben,
  per Caller-Threading (`bcc_pid_state_t` in/out, kein interner
  globaler Zustand).
- Anti-Windup-Direction-Logik mit signed-`Ki·error`-Vergleich:
  Negativ-Ki-Konfigurationen müssen korrekt unterscheiden ob der
  Integrator in Richtung Saturation drückt oder in Richtung
  Relief unwindt. Implementierung in
  `native/battery_control_core/src/compute.c::battery_control_core_pid_step`
  (Variablen `freeze_high` / `freeze_low`); diese ADR-Beschreibung
  ist die Kurzfassung — die normative Quelle ist der Code, und
  ein PID-Refactor muss diese Stelle hier mit aktualisieren.
- Deadband mit `previous_error`-Erhaltung über die Bandgrenze
  hinweg, damit der Derivative-Term beim Ausstieg den realen
  Error-Wechsel und nicht einen Spike gegen Null misst.
- Integrator-Overflow- und Pre-Clamp-Output-Non-Finite-Pfade als
  explizite Status-Codes, statt einer Exception über die C-ABI.

Die Frage „bleiben wir auf C oder pivotieren wir?" wurde daher
explizit aufgeworfen, mit den realistischen Optionen **C**, **C++**,
**Rust**, **Zig** und **Go**. Diese ADR fixiert die Antwort für
die M3-Closure und benennt die Trigger, unter denen sie zugunsten
einer anderen Sprache neu evaluiert werden muss — bevor M4 oder
ein PID-State-erweiternder Folge-Slice die Surface vergrößern.

---

## 2. Entscheidung

| Achse                       | Entscheidung                              | Pin / Trigger                                                          |
| --------------------------- | ----------------------------------------- | ---------------------------------------------------------------------- |
| Implementierungssprache M3  | **C11** (`compute.c`, Header in C mit `extern "C"`-Block) | `CMAKE_C_STANDARD 11`, `-Wall -Wextra -Wpedantic -Werror -Wshadow -Wnull-dereference -Wdouble-promotion`; static_assert via `<assert.h>` |
| Test-Harness                | **C++17** (doctest 2.4.11, FetchContent + URL_HASH SHA256-Pinning) | `tests/test_compute.cpp`; ruft die C-Funktionen über den `extern "C"`-Block |
| Sanitizer / Coverage        | gcc/clang ASan + UBSan + gcovr            | RM-M3-09 Native-Quality-Gates                                           |
| Abgelehnte Alternativen     | **C++** (Pivot wäre Rückwärts), **Go** (GC + Runtime-Init via cgo schlecht für deterministischen Compute-Kernel), **Zig** (pre-1.0, dünne Production-Track-Record für safety-critical) | siehe §3 |
| Re-Evaluierung als Rust     | **Trigger-getrieben** (siehe §4); aktuell nicht umgesetzt | Rust-Pivot wird via separater ADR + Folge-Slice gezogen, sobald ein Trigger zündet |

---

## 3. Achse 1 — Sprach-Optionen

### Optionen

**C11 (gewählt für M3).** Die Sprache passt zur tatsächlichen
Surface — wertbasiert, keine Allocation, keine STL, keine
Exceptions, value-typed Structs an der ABI. Konkrete Wins gegenüber
dem vorherigen C++-Stand:

- **Keine Exception-Maschinerie.** Der `try`/`catch (...)`-Wrapper
  als Defense-in-Depth gegen UB an der `extern "C"`-Grenze entfällt
  vollständig — C kann gar keine Exception werfen. Damit verschwindet
  auch der `GCOVR_EXCL`-Block aus dem Coverage-Report;
  RM-M3-09 erreicht 100 % Line-Coverage **ohne** Exclusion (vor dem
  Pivot 96 % mit einem Exclusion-Block).
- **Keine libstdc++-Linkage.** Die `.so` ist auf 15 KB geschrumpft
  und hat **null `NEEDED`**-Einträge (`readelf -d`). Damit ist die
  ABI-Diskussion über glibc-Versionen / Debian-Releases / Ubuntu-
  Noble-Bookworm-Kompatibilität für den Native-Kernel praktisch
  gegenstandslos — egal welche aspnet-Base in der Runtime steht,
  die Kernel-`.so` linkt gegen nichts Ablauffähig-Gefährdetes.
- **Kleinere Sprachoberfläche, weniger Footguns.** Weniger
  clang-tidy-False-Positives (`modernize-*` ist gegenstandslos),
  und der Reviewer-Aufwand für „passt der Code zur Sprache?"
  schrumpft.
- **Mechanisch einfachere Cross-Compilation falls später ein
  anderer Target-Stack relevant wird.** Die heutige `.so` ist
  x86_64-Linux-glibc und damit explizit kein Embedded-Artefakt;
  C bleibt aber substantiell einfacher cross-zu-compilieren als
  C++ (kein libstdc++-ABI-Match-Druck, geringere Toolchain-
  Anforderungen), falls künftig ein bare-metal- oder ARM-Linux-
  Target dazukommt.

Die C-Closure deckt heute **alle** RM-M3-Pflichten der
Constraint-/Ramp-/PID-Surface ab — alle vier Native-Quality-Gates
sind grün, 100 % Line-Coverage, P/Invoke-Bindings unverändert,
ABI-Header derselbe. Ein .NET-Caller (`BatteryEms.Adapters.NativeInterop`)
hat von dem Sprachwechsel **nichts** gemerkt.

**C++17 (verworfen für die Re-Pivot-Diskussion).** Der ursprüngliche
RM-M3-02-Stand. Verloren mit dem Pivot: nichts Substantielles auf
unserer Surface (keine RAII-Cleanup-Pfade, keine Templates, keine
Klassen-Hierarchie). Behalten: der `catch (...)`-Wrapper als
ABI-Schutz, der allerdings dauerhaft nicht testbar war und mit
Begründungs-Disziplin von der 100 %-Coverage ausgenommen werden
musste. Eine Rück-Pivot auf C++ würde eine Diskussion neu
eröffnen, die mit dem C-Stand bereits sauber gelöst ist.

**Rust (stable, deferred).** Echtes Memory-Safety-Modell auf
Sprachebene (Borrow-Checker), trivialer C-ABI-Export
(`#[no_mangle] pub extern "C" fn` + `#[repr(C)]`-Structs),
`panic = "abort"` im Cargo-Release-Profil verhindert Unwinding
über die FFI-Grenze (Default-Unwinding über `extern "C"` ist UB;
seit Rust 1.71 gibt es zusätzlich `extern "C-unwind"` für
explizit-erlaubtes Unwinding, das brauchen wir aber nicht). Eine
zukünftige Workspace-Cargo-Konfiguration muss `panic = "abort"`
in *allen* relevanten Profilen setzen — eine Subkrate die das
überschreibt würde die FFI-Sicherheit lokal zurücknehmen, das
gehört in das ADR-Folge-Slice für den Pivot. Konkrete Wins
**wenn die Surface staatszentrischer wird**:

- Borrow-Checker fängt State-Aliasing-Fehler zur Compile-Zeit
  (PID-Integrator, künftige PID-State-Erweiterungen wie Filter-
  Variablen, MPC-Sliding-Window-State, etc.). C lässt diese
  Klasse Bugs durchgehen und verlässt sich auf ASan/UBSan zur
  Test-Zeit.
- `cargo-llvm-cov` und `miri` (UB-Interpreter) sind reife
  Werkzeuge; das `rust-toolchain.toml`-File pinnt die Toolchain-
  Version analog zur NuGet-locked-mode-/URL_HASH-Disziplin.
- `clippy` ersetzt clang-tidy; `bugprone-easily-swappable-parameters`-
  Diskussionen gibt es nicht, weil typed Newtypes der idiomatische
  Ausweg sind.
- Wachsende Adoption in safety-relevanten Domänen: Linux-Kernel
  Toolchain-Support für Rust seit 6.1 (2022), erste produktive
  Rust-Treiber ab ~6.8 (2024), insgesamt weiterhin als
  „experimental" markiert. Mehrere Automotive-OEMs in
  Pilot-Programmen für Safety-Code (konkrete Implementations-
  Tiefe ist projektspezifisch und vor der Entscheidung zu
  prüfen, nicht aus dieser ADR abzuleiten). Industrie-
  Hypervisoren wie Cloud Hypervisor und Firecracker (beide in
  Rust geschrieben) sowie Embedded-RTOS-nahe Stacks wie Tock
  zeigen den breiten Adoptionspfad.

Aufwandsschätzung für einen Rust-Pivot der heutigen Surface
(Constraint, Ramp, PID): grob **1–2 Wochen** Gesamtaufwand. Davon
ist der eigentliche Rewrite (`compute.c` aktuell ~520 Zeilen
inkl. Helpers, davon ~280 Zeilen Kern-Logik der drei Endpunkte
`battery_control_core_compute` / `battery_control_core_pid_step` /
`battery_control_core_abi_version`) mit `#[repr(C)]`-Structs
mechanisch und kostet nur einen Bruchteil; der Großteil des
Aufwands liegt in CMake-Wiring via `corrosion-cmake` (oder
`ExternalProject_Add(cargo build)`), Sanitizer-/Coverage-Setup-
Pendant in Rust (`cargo-llvm-cov`, `miri`), Reproduktion der vier
Native-Quality-Gates auf der Rust-Toolchain, plus dotnet/sdk-
Image-Erweiterung um `rustup` mit `rust-toolchain.toml`-Pinning.
Schätzung ist eine grobe Range, nicht ein Punktwert. **Null
Aufwand auf der .NET-Seite** — die ABI bleibt gleich, P/Invoke-
Bindings bleiben gleich, der Loader bleibt gleich.

Verworfen **für M3** weil:

- Die heutigen Constraint/Ramp/PID-Pfade sind **value-only** —
  der Borrow-Checker hätte hier nahezu null Bugs verhindert die
  ASan + UBSan + 100 %-Coverage nicht schon fangen.
- Die Toolchain-Komplexität (rustup im native-build-Stage,
  cargo-Lock-File neben packages.lock.json + URL_HASH-doctest,
  CMake-Brücke) zahlt sich erst aus, wenn die Surface gewachsen
  ist.
- Compile-Zeit für den Slice ist 30–60 s vs. ~5 s für den C-Build;
  irrelevant für Wartezeiten, aber merkbar im
  `make native-build`-Loop.

**Ada / SPARK (verworfen für M3).** Etablierter Track-Record in
Avionik (DO-178C), Bahn (EN 50128) und Industrie-Steuerung;
SPARK-Subset bietet auf Sprachebene formale Verifikation
(beweisbare Abwesenheit von Pufferüberläufen, Datenrennen,
unbeabsichtigtem Aliasing). Wäre gegenüber Rust + C die
disziplinierteste Wahl falls eine konkrete Zertifizierung nach
IEC 61508 / ISO 26262 / DO-178C anstehen würde. Verworfen für
M3 weil:

- **Toolchain- und Personal-Aufwand.** GNAT + SPARK-Tooling
  einzuführen + ein Team-Skill-Aufbau für eine Sprache mit
  geringer Verbreitung im EMS-/Energie-Sektor sprengt den
  Nutzen für die heutige value-only Surface.
- **C-ABI-Export aus Ada** ist machbar (`pragma Export
  (Convention => C, …)`), aber zwei Konsumenten der ABI (.NET-
  P/Invoke + zukünftig potentiell Rust-Kernel-Ports) gegen eine
  Ada-Source ist unverhältnismäßig.
- **Keine konkrete Zertifizierungs-Anforderung in Sicht.** LH-
  NATIVE-001 nennt „C/C++ für performance-kritische Komponenten"
  — eine Zertifizierungsanforderung würde Trigger 3 in §4
  zünden und Ada/SPARK wäre dort die naheliegende Antwort.

Re-Evaluierung mit Trigger 3 (Funktionale Sicherheit) — Ada/SPARK
ist dort eine ernstzunehmende Alternative zu Rust und sollte bei
diesem Trigger explizit verglichen werden, nicht von Rust
abgeleitet werden.

**Zig 0.13.x (verworfen).** Sprach-fit ist gut: kein GC, keine
Exceptions, `comptime` ersetzt Templates, C-ABI-Interop trivial
über `extern fn`. Verworfen weil:

- **Pre-1.0** — die Sprache bricht zwischen Releases noch
  Semantik. Eine BESS-Codebase die später Richtung Grid-Scale-
  Deployment soll, wettet mit Zig auf eine bewegliche Basis.
- **Kein Reife-Vorteil gegenüber Rust für unsere Achsen.** Die
  Wins von Zig (kleinere Sprache, schneller Compile, kein
  Borrow-Checker-Lernaufwand) zahlen erst, wenn man bereit ist
  die Toolchain-Stabilität als Risiko mitzunehmen. Auf einer
  safety-relevanten Codebase ist die etablierte Pinning-Disziplin
  einer 1.x-Sprache mehr wert als die Sprach-Ergonomie.

Re-Evaluierung in 2–3 Jahren denkbar, wenn 1.0 da ist.

**Go 1.24+ (verworfen).** Falscher Fit für die Stelle:

- **GC mit non-deterministischen Pausen.** 1Hz-Regulation wäre
  noch tolerabel, aber wir wachsen perspektivisch Richtung
  10–100 Hz; GC-Pausen kompromittieren das Determinismus-
  Argument durch den ganzen Stack.
- **Go-Runtime im `.so`.** `go build -buildmode=c-shared` produziert
  eine `.so` die die Go-Runtime mitschleppt (~5–10 MB) und beim
  ersten Call initialisieren muss; cgo-Boundary-Overhead liegt
  basierend auf öffentlichen Benchmarks im Bereich 150–250 ns
  pro Call zusätzlich zum eigentlichen Funktionsaufruf. Für
  unseren Anwendungsfall (1-Hz-Regelzyklus, LH-RT-004) ist die
  absolute Latenz nicht der Knock-out — der Knock-out ist die
  GC-Pausen-Klasse darunter; die Zahl steht nur für
  Vollständigkeit. Die Latenzen für unsere bestehende P/Invoke-
  Kopplung sind bisher nicht eigenständig gemessen (kein
  `BenchmarkDotNet`-Lauf in der Test-Suite); eine künftige
  Mess-Slice könnte die Größenordnungen beider ADRs konkret
  belegen.
- **Strengths nicht abgerufen.** Goroutines, Channels und das
  Standard-Library-Networking sind null Wert für einen
  synchronen Compute-Kernel — Go würde teurer kommen als nötig
  ohne den Vorteil zu liefern, für den die Sprache designt ist.

Go würde nur Sinn ergeben in Kombination mit dem Out-of-Process-
Pivot aus [ADR 0004](0004-native-kernel-process-isolation.md), wo
der Kernel ein eigenständiger Microservice über gRPC ist — und
selbst dann wäre er nur eine von vielen okayen Optionen ohne
klaren Vorteil gegenüber Rust oder C.

---

## 4. Achse 2 — Trigger für Rust-Pivot

Die Re-Evaluierung als Rust ist explizit deferred. Sie wird mit
einer eigenen ADR + einem eigenen Folge-Slice gezogen, sobald
einer der folgenden Trigger zündet:

1. **State-Surface wächst materiell.** Beobachtbar als
   mindestens **eines** der folgenden Kriterien zünden:
   (a) ein State-Struct (heute `bcc_pid_state_t` mit 2 Skalaren)
   wächst über 8 skalare Felder oder enthält ein Pointer-Feld;
   (b) cross-tick-State wird zwischen mehreren Funktionsaufrufen
   im gleichen Tick geteilt (statt nur vom Caller durchgereicht
   wie heute); (c) ein neuer Kernel-Slice (z. B. Kalman-Filter,
   MPC-Sliding-Window) führt internen mutable Cache jenseits
   reiner Wert-Berechnung ein. Sobald eines davon konkret wird,
   zahlt der Borrow-Checker seinen Preis durch das Abfangen von
   State-Aliasing-Fehlern zur Compile-Zeit, die in C nur via
   ASan zur Test-Zeit gefunden werden.
2. **MPC-/State-Space-Kern (Phase 3, Architektur §13.1).** Die
   Sprachwahl für `state_space_core` ist offen; Rust ist hier
   bereits attraktiver als für die heutige Constraint+Ramp+PID-
   Surface, und ein gemischter C-Plus-Rust-Stack innerhalb von
   `native/` wäre eine teure Hybrid-Lösung. **Bündel-Trigger:**
   dieser Trigger ist faktisch dasselbe Event wie Trigger 2 in
   ADR 0004 („Phase-3-Komponenten kommen in Scope"); die zwei
   ADRs erfassen ihn aus zwei Blickwinkeln (Sprache vs. Prozess-
   Grenze), aber er wird als **ein** Architektur-Event durch eine
   gemeinsame Folge-ADR adressiert.
3. **Externer Code-Audit / Funktionale Sicherheit / Zertifizierung.**
   Sobald eine Zertifizierungsanforderung (IEC 61508, ISO 26262 für
   automotive Cousins, oder ein Grid-spezifisches Pendant) auf den
   Tisch kommt, **erleichtert** Rust den Verifikationsaufwand
   substantiell — der Audit kann sich auf Algorithmik und
   Schnittstellen konzentrieren statt auf Memory-Safety-Beweise.
   Die Normen verlangen kein Rust und sind in C mit Disziplin
   (MISRA-C, statische Analyse, formale Methoden für kritische
   Pfade) erfüllbar; die Frage ist Aufwand-pro-Audit, nicht
   Erfüllbarkeit-prinzipiell.
4. **Konkrete Bug-Klasse, die in C entsteht und in Rust per
   Konstruktion verhindert wäre.** Z. B. Use-after-free in einem
   neu eingeführten Lifetime-Pfad, Aliasing-Fehler über mehrere
   Ticks, Datenrennen wenn der Kernel in einen Multi-Threading-
   Kontext kommt.
5. **Out-of-Process-Pivot (ADR 0004) zündet UND die Sprachwahl
   wird mitgezogen.** Wenn die Kernel-Komponente ein eigener
   Prozess wird, ist der Sprach-Reset günstiger als später
   (kein eingebauter Stack mit vielen Konsumenten). **Bündel-
   Aufwand:** ein kombinierter Pivot (Sprache + Prozess-Grenze)
   ist nicht streng additiv aus den Einzelschätzungen — der
   Aufwand für Toolchain-Setup, IPC-Vertrag und Test-Surface
   wird **einmal** gezahlt, nicht zweimal. Realistische
   Größenordnung für die heutige Surface (Constraint, Ramp, PID):
   grob **5–10 Wochen** Gesamtaufwand (vs. 1–2 + 4–8 = 5–10 Wochen
   wenn additiv gerechnet, was zufällig im selben Bereich liegt
   weil das Toolchain-Setup für einen Rust-Kernel-Prozess die
   doppelte Arbeit der reinen Sprach-Pivot ist). Ein gemeinsamer
   Pivot-Slice (statt zwei sequenziellen) erspart das Reload der
   ABI-Diskussion zwischen Schritt 1 und Schritt 2.

Kein einzelner dieser Trigger zündet automatisch — alle erfordern
eine separate ADR-Diskussion. Aber jeder ist ein klarer
Re-Evaluierungs-Anlass.

---

## 5. Konsequenzen

### Positiv

- **C-Pivot ist heute realisiert und produktiv.** RM-M3-09 grün
  mit 100 % Coverage **ohne Exclusion**, RM-M3-13 grün inklusive
  PID + Anti-Windup + Deadband, `.so` unter 16 KB ohne
  Dynamic-Deps. Die ADR fixiert nur den Status quo, sie schreibt
  ihn nicht erst.
- **`catch (...)`-Diskussion final geklärt.** Keine Exception-
  Maschinerie, keine Defense-in-Depth-Wrapper, kein
  `GCOVR_EXCL`-Block. Wenn der Kernel mal wirklich abstürzt
  (SIGSEGV), ist das ein Memory-Bug; ASan + UBSan + 100 %-
  Coverage sind die Disziplin dagegen, nicht der Catch-All.
- **ABI-Hygiene auf Wartungsniveau.** Die `.so` ist gegen
  glibc-Drift / libstdc++-ABI-Inkonsistenzen / Debian-Ubuntu-
  Wechsel praktisch immun, weil sie nichts dynamisch linkt.
  Das ldd-Build-Time-Gate (RM-M3-06 Teil 2) bleibt aktiv für
  künftige Slices.
- **.NET-Seite vom Sprach-Pivot vollständig isoliert.** Die
  `extern "C"`-ABI ist die Sprachgrenze; ein zukünftiger
  Rust-Pivot ändert die ABI nicht und damit den
  P/Invoke-Adapter und den Loader nicht.

### Negativ

- **Memory-Safety bleibt Test-Time-Disziplin, nicht Compile-Time-
  Garantie.** Eine zukünftige Slice die einen Aliasing-Fehler
  einführt würde diesen erst beim Sanitizer-Lauf treffen, im
  Worst Case erst in Production. ASan + UBSan + 100 %-Coverage
  + Code-Review sind die Mitigationen — Rust würde diese Klasse
  Bugs im Compiler abfangen.
- **Skalierungs-Decke ist sichtbar.** Sobald die Surface über
  „ein paar value-only Step-Funktionen" hinauswächst (MPC,
  Multi-Asset, Threading), wird die C-Disziplin teuer. Diese
  ADR macht das explizit, indem sie die Trigger benennt — aber
  sie verschiebt damit auch die Schmerzgrenze in die Zukunft.
- **Zwei Sprachen im `native/`-Tree wenn ein Rust-Pivot teilweise
  passiert.** Falls ein Folge-Slice nur einen Teil (z. B.
  `state_space_core`) auf Rust zieht, koexistieren C-Constraint+
  Ramp+PID und Rust-MPC im selben Build. Das CMake-Wiring kann
  das, ist aber ein Hybrid-Stack mit doppeltem Toolchain-Setup.

### Neutral

- **Test-Harness bleibt C++ (doctest), Production-Code ist C.**
  Bewusste Asymmetrie: doctest war beim RM-M3-08-Slice gewählt
  (vor dem C-Pivot), und der Aufwand-zu-Wert-Ratio für einen
  zusätzlichen Wechsel auf einen reinen-C-Test-Stack (Unity,
  cmocka, `check`) ist gering — der Header hat einen
  `extern "C"`-Block, doctest ruft die C-Funktionen direkt auf,
  Coverage und Sanitizer arbeiten auf der C-Seite, der C++-
  Aufrufpfad fügt keine Testpfad-spezifische Komplexität hinzu.
  Ein späterer Rust-Pivot würde den Test-Harness möglicherweise
  auch nach Rust ziehen (`cargo test`); doctest bleibt bis
  dahin funktional, kein Refactoring-Druck.
- **Toolchain auf Ubuntu-24.04-Noble (`mcr.microsoft.com/dotnet/
  sdk:10.0`)** gilt für native-build, native-lint, native-
  sanitizer, native-coverage. Bei einem Rust-Pivot kommt
  `rustup` als zusätzliche apt/curl-Stage hinzu; CVE-Hygiene
  bleibt durch das vendored sdk-Image getragen, Rust-toolchain-
  Pinning übernimmt `rust-toolchain.toml`.

---

## 6. Sequenz und Aktivierung

1. **C-Pivot (RM-M3-09):** abgeschlossen. `compute.cpp` →
   `compute.c`, `LANGUAGES C CXX` in `CMakeLists.txt`,
   `try`/`catch`-Wrapper entfernt, `GCOVR_EXCL`-Block entfällt.
2. **PID-Slice (RM-M3-13):** abgeschlossen in C. ABI-Minor-Bump
   0.1 → 0.2 mit vier neuen Structs, 4 neuen Reason-Codes,
   neuem Export `battery_control_core_pid_step`.
3. **M3-D2 (offen):** produktive Aktivierung von Native als
   bevorzugtem Pfad. Sprachwahl bleibt C — kein Pivot-Druck aus
   diesem Slice.
4. **Trigger-Watch:** die §4-Liste ist Operator-Verantwortung.
   Sobald ein Trigger zündet, wird eine **neue ADR** (z. B.
   `0005-native-kernel-rust-pivot.md`) gezogen, mit
   konkretisiertem Aufwand, Test-Strategie für den Rewrite, und
   Migration-Plan. Diese ADR (0003) wird dann zu **Superseded by
   0005** umgesetzt.
5. **Phase-3-MPC (Architektur §13.1):** ist der nächste
   architektonisch große Punkt, an dem die Sprachfrage neu
   gestellt wird. Der `state_space_core` kann einen anderen
   Sprach-Pfad gehen als `battery_control_core`, sofern beide
   parallel im Build koexistieren können (CMake erlaubt das).

Bis ein Trigger zündet bleibt diese ADR `Accepted` und C ist die
Implementierungssprache des Native Control Cores.
