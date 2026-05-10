# Plan RM-M4-05 — OPC-UA-Security (Zertifikate, Security Mode/Policy, RuntimeProfile)

**Dokumenttyp:** Slice-Plan (Detail-Plan zum Master-Arbeitspaket RM-M4-05)
**Status:** Offen — wird in Sub-Slices RM-M4-05-A..D umgesetzt
**Bezug:**
[`plan-RM-M4.md`](plan-RM-M4.md) (Master-Plan, RM-M4-05-Zeile mit DoD und LH-Bezug),
[`../done/plan-RM-M4-04.md`](../done/plan-RM-M4-04.md) (RM-M4-04-D-04 hat die Security-Slots `SecurityMode/SecurityPolicy/AllowUnsecured/AllowUnsecuredReason` in `OpcUaAdapterOptions` und den Bool-Achsen-Startup-Guard bereits gelegt; M4-05 layert die Profile-Awareness und das echte SDK-Binding für `SignAndEncrypt` drauf),
[`../done/plan-RM-M4-08.md`](../done/plan-RM-M4-08.md) (RM-M4-08-A liefert das Embedded TestServer-Fixture-Pattern; M4-05 erweitert die Fixture für SignAndEncrypt + Cert-Trust-Bridge),
[`../done/plan-RM-M4-03.md`](../done/plan-RM-M4-03.md) (F-12 Cross-Adapter-RuntimeProfile-Source ist getrackt; M4-05 implementiert ein **OPC-UA-lokales** RuntimeProfile-Field auf der Adapter-Schicht und überlässt F-12 die globale Quelle — siehe D-01),
[`../open/note-RM-M4-followups.md`](../open/note-RM-M4-followups.md) (F-09 OPC-UA-Activation-Source-Adapter; F-13 Multi-Server; F-14 Method-Calls; F-15 Type-System; M4-05 fügt **F-17 Allowlist-Erweiterung** und **F-18 Cert-Rotation/Renewal** hinzu — siehe §9),
[`../../../../spec/lastenheft.md`](../../../../spec/lastenheft.md) (LH-OPCUA-005 Security)

---

## 1. Zweck

RM-M4-05 ist die **Production-Härtung der OPC-UA-Linie**. Pre-M4-05
läuft der Adapter mit `MessageSecurityMode.None` plus dem
AllowUnsecured-Bool-Guard (M4-04-D-04) — das ist explizit als
„nicht produktiv freigebbar bevor RM-M4-05 die volle Security-
Härtung dranhängt" dokumentiert.

M4-05 liefert:

- **`SignAndEncrypt`-Default** mit Cert-basiertem Handshake gegen
  den Server.
- **Security-Policy-Allowlist** (`Basic256Sha256` als M4-Start-
  Allowlist; jede Erweiterung verlangt eine Plan-Änderung — siehe
  D-04).
- **`OpcUaRuntimeProfile`-Field** als Production/Pre-Production-
  Achse auf der Adapter-Schicht (`Production`, `HilSimulator`,
  `Development`); `Production` macht `SecurityMode=None` zum harten
  Startup-Fehler **unabhängig** von `AllowUnsecured`.
- **Production-Code-Pfad freigeschaltet**: nach M4-05-Closure ist
  der OPC-UA-Adapter erstmals produktiv freigegeben — vorausgesetzt
  der Operator setzt `RuntimeProfile=Production` plus einen
  `SignAndEncrypt`-Endpoint plus Cert-Trust-Provisioning.
- **Embedded TestServer-Erweiterung** für SignAndEncrypt-Policies
  plus Cert-Trust-Bridge zwischen Test-Client und Test-Server
  (replaces der heutige `AutoAccept`-Hook für die neuen Security-
  Pins).
- **6 pinned Integration-Pins** (siehe §4 Sub-Slice D): gesicherter
  Handshake, nicht-allowlistete Policy → Reject, Production-Fail-
  Closed bei `SecurityMode=None`, AllowUnsecured-Override gegen
  HilSimulator-Profile, und fehlendes Server-Trust im Secure-Production-Pfad.

**Bewusster Scope-Cut**: M4-05 macht **nur** die OPC-UA-Adapter-
Security. Cross-Adapter-RuntimeProfile-Verkabelung (F-12 aus M4-03),
User/Token-Identity (heute Anonymous), Cert-Rotation/Renewal-
Workflows und Allowlist-Erweiterung sind separate Folgearbeiten
(siehe §3 Out-of-Scope und §9).

---

## 2. Aktivierungsbedingungen

- **RM-M4-04 ✅** (`plan-RM-M4.md:167`) — die Security-Slots
  (`SecurityMode/SecurityPolicy/AllowUnsecured/AllowUnsecuredReason`)
  existieren in `OpcUaAdapterOptions`; der Bool-Achsen-Startup-Guard
  fired bei `SecurityMode=None && !AllowUnsecured`.
- **RM-M4-08 ✅** (`plan-RM-M4.md:171`) — das Embedded TestServer-
  Fixture-Pattern existiert in
  `tests/integration/BatteryEms.OpcUa.IntegrationTests/`; `make
  test-hil-opcua` ist im Pflicht-`make ci`.

**Optional, aber nicht-zündend:**

- **F-12 Cross-Adapter-RuntimeProfile-Source** bleibt offen.
  M4-05 implementiert ein **adapter-lokales** RuntimeProfile-Field
  (D-01); F-12 ergänzt später die globale Quelle und feedet die
  Adapter-Optionen (analog zur heutigen `BessHostOptions`-zu-
  `OpcUaAdapterOptions`-Verdrahtung in `BessConfigurationBootstrap`).

---

## 3. Scope

**In Scope (RM-M4-05-A..D zusammen):**

- **`OpcUaRuntimeProfile`-Enum** (`Development`, `HilSimulator`,
  `Production`) im Adapter-Projekt. Default ist
  `RuntimeProfile.Production` — ein Operator, der nicht produktiv
  fährt, **muss** explizit umstellen (analog zum AllowUnsecured-
  Doppel-Opt-in-Pattern aus M4-04-D-04).
- **`OpcUaAdapterOptions`-Erweiterung**:
  - `OpcUaRuntimeProfile RuntimeProfile { get; init; } = OpcUaRuntimeProfile.Production;`
  - `OpcUaSecurityMode SecurityMode { get; init; } = OpcUaSecurityMode.SignAndEncrypt;` (Default-Schwenk!)
  - `string SecurityPolicy { get; init; } = OpcUaSecurityPolicies.Basic256Sha256;` (Default-Schwenk!)
  - Neuer Konstanten-Container `OpcUaSecurityPolicies` mit
    `Basic256Sha256` und einer dokumentierten Allowlist.
  - `string ApplicationCertificateSubject { get; init; }` (Default
    abgeleitet von `SessionName` analog zu heute), plus optionale
    `string ApplicationCertificateStorePath`-Slots für Operator-
    bereitgestellte Cert-Stores.
- **`EnsureValid()`-Erweiterung** mit Profile-Awareness:
  - `RuntimeProfile=Production` + `SecurityMode=None` ⇒
    **harter Startup-Fehler** `opcua-security-not-hardened-in-
    production`, **unabhängig** von `AllowUnsecured` (D-02). Der
    Bool-Guard ist nicht mehr ausreichend.
  - `RuntimeProfile=HilSimulator|Development` + `SecurityMode=None`
    + `AllowUnsecured=true` + `AllowUnsecuredReason!=null` ⇒
    durch (heutiges Pre-M4-05-Verhalten bleibt für Test-Profile
    erhalten).
  - `SecurityMode=Sign|SignAndEncrypt` + `SecurityPolicy` nicht in
    Allowlist ⇒ Startup-Fehler
    `opcua-security-policy-not-allowlisted`. Allowlist-Inhalt:
    `Basic256Sha256` (M4-05-Start; Erweiterung verlangt Plan-Änderung
    per D-04).
  - `SecurityMode=Sign|SignAndEncrypt` + `AllowUnsecured=true` ⇒
    Startup-Fehler `opcua-allow-unsecured-with-secure-mode-
    inconsistent` (Operator-Bug-Detection).
  - LoggerMessage-EventIds ergänzt (heute 4200 = LogUnsecuredOpcUa-
    Connection; neu 4221 = LogSecureProfile, 4222 = LogPolicy-
    Allowlist-Check).
- **`OpcUaClient`-SDK-Binding für SignAndEncrypt**:
   - `EnsureApplicationConfiguredAsync` baut die Builder-Chain abhängig vom
     `SecurityMode`:
     - `SignAndEncrypt` → `AddSignAndEncryptPolicies()`
     - `Sign` → `AddSignPolicies()`
     - `None` → `AddUnsecurePolicyNone()`.
  - `CoreClientUtils.SelectEndpointAsync(_appConfig, url,
    useSecurity: true, _telemetry, ct)` (heute hard-coded
    `useSecurity: false`) — wird abhängig vom `SecurityMode`
    gemappt.
  - `AutoAccept`-Hook bleibt **nur** im `RuntimeProfile=
    Development|HilSimulator` aktiv; im `Production`-Profile ist
    `SetAutoAcceptUntrustedCertificates(false)` und der Server-
    Cert muss explizit getrusted sein. Der Adapter wirft auf einem
    nicht-getrusteten Server-Cert mit `opcua-server-certificate-
    not-trusted`.
  - **Cert-Trust-Pfad**: `OpcUaAdapterOptions.TrustedServer-
    CertificatesPath` (optional Operator-bereitgestellt, default
    abgeleitet vom PKI-Root) bekommt die Server-Cert vom Operator
    pre-deployment kopiert. Der Adapter prüft beim Connect, dass
    die Server-Cert im Trusted-Store ist.
- **Embedded TestServer-Erweiterung** für SignAndEncrypt-Pins:
  - Test-Server bekommt `AddSignAndEncryptPolicies(true)` und `AddSignPolicies()` zusätzlich
    zur bestehenden `AddUnsecurePolicyNone()` (existing 7 Pins
    laufen weiter im None-Profile).
  - **Trust-Bridge**: nach `EmbeddedTestServerHost.StartAsync` plus
    `OpcUaClient.EnsureApplicationConfiguredAsync` werden die
    beiden App-Certs gegenseitig in die jeweiligen Trusted-Stores
    kopiert (Trust-Provisioning-Helper in der Test-Fixture).
    Damit testet die Fixture echtes Cert-Trust statt AutoAccept —
    das ist die ganze Pin-Substanz.
- **`Defaults.cs`-Erweiterung**:
  - `Defaults.ForHilSimulator()` bleibt im None-AllowUnsecured-Pfad,
    setzt aber jetzt explizit `RuntimeProfile=HilSimulator` (heute
    implizit über fehlendes Field). Pin-Tests aus M4-04-D + M4-08-A
    laufen unverändert.
  - **Neu**: `Defaults.ForProductionSecure()`-Builder mit
    `RuntimeProfile=Production`, `SecurityMode=SignAndEncrypt`,
    `SecurityPolicy=Basic256Sha256`, `AllowUnsecured=false`.
    Verlangt die Trust-Bridge in der Fixture.
- **`BessHostOptions`-Erweiterung**:
  - `OpcUaRuntimeProfile?` (Default `null` → `Production`).
  - `OpcUaSecurityMode?` (Default `null` → Adapter-Default
    `SignAndEncrypt`).
  - `OpcUaSecurityPolicy?` (Default `null` → Adapter-Default
    `Basic256Sha256`).
  - `OpcUaApplicationCertificateSubject?` und
    `OpcUaTrustedServerCertificatesPath?`.
- **`BessConfigurationBootstrap`-Erweiterung**: reicht die neuen
  Felder durch in die `OpcUaAdapterOptions`. Die Pre-Conditions
  laufen am `EnsureValid()` durch (Production-Fail-Closed bei Fehl-
  Konfiguration).
- **6 pinned Integration-Pins** in einer neuen
  `OpcUaSecurityTests.cs` (Sub-Slice D):
  1. **Secure_handshake_succeeds_against_test_server** — Client
     connectet mit `Defaults.ForProductionSecure()` (Production-
     Profile, SignAndEncrypt, Basic256Sha256), Trust-Bridge ist
     gepatcht; eine `ReadAsync`-Operation gibt einen Valid-Sample
     zurück.
  2. **Non_allowlisted_policy_throws_at_construction** — Options
     mit `SecurityPolicy="Basic128Rsa15"` oder
     `http://opcfoundation.org/UA/SecurityPolicy#Aes128Sha256RsaOaep` ⇒ `EnsureValid` wirft
     `opcua-security-policy-not-allowlisted`.
  3. **Production_profile_with_unsecured_mode_throws_at_construction**
     — `RuntimeProfile=Production` + `SecurityMode=None` +
     `AllowUnsecured=true` + `AllowUnsecuredReason="...”` ⇒
     `EnsureValid` wirft `opcua-security-not-hardened-in-
     production` (der AllowUnsecured-Bool-Pfad ist im Production-
     Profile **nicht ausreichend**).
  4. **Hil_simulator_profile_with_unsecured_mode_passes** —
     `RuntimeProfile=HilSimulator` + `SecurityMode=None` +
     `AllowUnsecured=true` + Reason ⇒ `EnsureValid` lässt durch
     (heutiges Pre-M4-05-Verhalten bleibt).
  5. **Production_profile_without_trusted_server_certificate_fails** — `Defaults.ForProductionSecure()` + keine Trust-Bridge (Server-Cert
     fehlt im Client-TrustedStore) ⇒ `ConnectAsync` wirft
     `opcua-server-certificate-not-trusted`.
- **Master-Plan-Wortlaut-Cleanup** bei Closure: RM-M4-05-Zeile
  flippt auf ✅ mit dem in D-05 vorab gepinnten DoD-Replacement-
  Text.

**Out of Scope (separate Slices / Folgearbeiten):**

- **Cross-Adapter-RuntimeProfile-Source** → **F-12** (M4-03-
  Followup). Trigger: erste Slice, die ein einheitliches
  Production-Profile-Signal über mehrere Adapter benötigt
  (z. B. wenn Modbus-TLS-Härtung landet und denselben Production-
  Gate-Pfad braucht). M4-05 implementiert das adapter-lokale
  Field; F-12 wird später die globale Quelle ergänzen ohne
  Adapter-Schema-Bruch.
- **User/Token-Identity** → **F-19 OPC-UA-User-Identity**. Trigger:
  TSO-Spec verlangt UserName/Password oder UserToken statt
  Anonymous. Heute fährt der Adapter `UserIdentity=null` (= Anonymous);
  Server-seitige Authentifizierung jenseits der Cert-basierten ist
  out-of-scope.
- **Cert-Rotation/Renewal-Workflows** → **F-18**. Trigger: erstes
  Cert-Lifecycle-Event in der Operator-Praxis (Validity-Period
  läuft ab; Operator bekommt eine neue Server-Cert). Heute geht
  M4-05 davon aus, dass Certs statisch sind und der Operator manuell
  re-deployt; F-18 liefert ein automatisiertes Reload + Re-Trust
  ohne Process-Restart.
- **Allowlist-Erweiterung** (`Aes128Sha256RsaOaep`,
  `Aes256Sha256RsaPss`, ECC-Policies) → **F-17
  OPC-UA-Security-Policy-Allowlist-Erweiterung**.
  Trigger: TSO-/Vendor-Spec verlangt eine andere Policy. Per D-04
  verlangt **jede** Allowlist-Erweiterung eine Plan-Änderung — F-17
  ist der Carrier dafür.
- **HSM-Integration** → unabhängige Folgearbeit; kein F-Item heute,
  weil kein Trigger.

---

## 4. Sub-Slices

| Status | ID | Paket | DoD |
| ------ | -- | ----- | --- |
| ⬜ | RM-M4-05-A | Domain/Options-Erweiterung: `OpcUaRuntimeProfile` + Allowlist + `EnsureValid`-Profile-Awareness — **~300-500 LOC** | Neue Datei `OpcUaRuntimeProfile.cs` mit Enum (`Development`, `HilSimulator`, `Production`). Neue Datei `OpcUaSecurityPolicies.cs` mit Konstanten (`Basic256Sha256 = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256"`) und `IsAllowed(string policy)`-Helper. **`OpcUaAdapterOptions`** bekommt: `RuntimeProfile` (Default `Production`), `ApplicationCertificateSubject` (Default abgeleitet von `SessionName`), `TrustedServerCertificatesPath` (optional). **Defaults schwenken**: `SecurityMode` jetzt `SignAndEncrypt`, `SecurityPolicy` jetzt `Basic256Sha256`. **`EnsureValid()`** bekommt die vier neuen Validations: (a) `RuntimeProfile=Production` + `SecurityMode=None` ⇒ `opcua-security-not-hardened-in-production` (überschreibt den Bool-Guard); (b) `SecurityMode!=None` + `SecurityPolicy` nicht in Allowlist ⇒ `opcua-security-policy-not-allowlisted`; (c) `SecurityMode!=None` + `AllowUnsecured=true` ⇒ `opcua-allow-unsecured-with-secure-mode-inconsistent`; (d) `RuntimeProfile=HilSimulator|Development` + `SecurityMode=None` Pfad bleibt wie heute (Bool-Guard fired). LoggerMessage-EventIds 4221 (`LogSecureProfileEstablished`, Information-Level, mit Profile + Policy + Endpoint) und 4222 (`LogAllowlistedPolicyAccepted`, Information). Tests (Adapters.OpcUa.Tests): 8 neue Pins für `OpcUaAdapterOptionsTests` — Production+None throws unabhängig von AllowUnsecured (zwei Pins: AllowUnsecured=false und =true), HilSimulator+None+AllowUnsecured passes (heutiger Pfad), Production+SignAndEncrypt+Basic256Sha256 passes, Production+SignAndEncrypt+Basic128Rsa15 throws (nicht in Allowlist), Production+SignAndEncrypt+AllowUnsecured=true throws (Inkonsistenz), Default-Pin (Default = Production+SignAndEncrypt+Basic256Sha256+AllowUnsecured=false), `OpcUaSecurityPolicies.IsAllowed`-Pin. **Bestehende Pins**: alle Test-Defaults müssen `RuntimeProfile=HilSimulator` setzen, sonst werfen die `EnsureValid`-Aufrufe in `OpcUaTelemetrySource`/`OpcUaCommandSink`-Konstruktoren (im Production-Default mit None-Mode). Das ist ein One-Time-Test-Refactor — `Defaults.cs` zentralisiert. |
| ⬜ | RM-M4-05-B | `OpcUaClient`-SDK-Binding für SignAndEncrypt + Cert-Trust — **~400-600 LOC** | `OpcUaClient.EnsureApplicationConfiguredAsync` baut die ApplicationConfiguration profile-abhängig: `RuntimeProfile=Production` + `SecurityMode!=None` ⇒ Builder-Chain mit `AddSignAndEncryptPolicies()` bei `SignAndEncrypt` und `AddSignPolicies()` bei `Sign`, `SetAutoAcceptUntrustedCertificates(false)`, plus expliziter `CertificateValidator`-Hook der nur bei in der Trusted-Store geladene Server-Certs `Accept=true` setzt (Mismatch ⇒ kein Accept, Connect schlägt fehl mit `opcua-server-certificate-not-trusted`). `RuntimeProfile=HilSimulator|Development` + `SecurityMode=None` Pfad bleibt wie heute (`AddUnsecurePolicyNone()` + `SetAutoAcceptUntrustedCertificates(true)` + lambda-handler) — ist by-design das pre-M4-05-Verhalten. **`OpcUaClient.ConnectAsync`** ändert die `useSecurity`-Logik: `useSecurity = _options.SecurityMode != None`. Damit wählt `CoreClientUtils.SelectEndpointAsync` den passenden Endpoint vom Server (Server muss den passenden `EndpointDescription` exportieren — der Embedded TestServer in Sub-Slice C erweitert das). **App-Cert-Subject**: heute hard-coded `$"CN={_options.SessionName}, O=BatteryEms, DC=localhost"`; M4-05 nutzt `_options.ApplicationCertificateSubject` (Default-Builder hängt `SessionName` rein, Operator kann override). Tests (Adapters.OpcUa.Tests): nur strukturelle Pins (kein echter Server hier — der Wire-Test ist Sub-Slice D). Pin: `OpcUaClient.EnsureApplicationConfiguredAsync` mit Production-Profile baut die ApplicationConfiguration mit nicht-leerer Server-Trusted-Store-Pfad-Konfiguration; Pin: gleiche Methode mit HilSimulator-Profile baut sie mit `AutoAccept=true`. Diese Tests benutzen einen Mock-Telemetry und führen den Builder-Chain bis zum `CreateAsync` durch (kein echter Network-Call). |
| ⬜ | RM-M4-05-C | Embedded TestServer-Erweiterung für SignAndEncrypt + Cert-Trust-Bridge — **~300-400 LOC** | `BessEmsTestServer` und `EmbeddedTestServerHost.StartAsync` erweitern: zur bestehenden `AddUnsecurePolicyNone()` zusätzlich `AddSignAndEncryptPolicies(true)` und `AddSignPolicies()`. Server bekommt damit drei Endpoint-Descriptions, der Client wählt per `useSecurity`-Flag (plus `SecurityMode`). **Trust-Bridge**: neuer Helper `CertificateTrustBridge.EstablishMutualTrust(serverHost, clientApplication, ct)` in der Test-Fixture-Linie. Der Helper kopiert die App-Cert des Servers in den Trusted-Store des Clients und umgekehrt — beide vertrauen einander explizit, **kein** AutoAccept. Plus `SetAutoAcceptUntrustedCertificates(false)` auf beiden Seiten im Production-Profile-Pfad (HilSimulator-Pfad bleibt mit AutoAccept). **`Defaults.cs`-Erweiterung**: `Defaults.ForHilSimulator()` setzt jetzt explizit `RuntimeProfile=HilSimulator`. Neue Builder `Defaults.ForProductionSecure(uri)` setzt `RuntimeProfile=Production`, `SecurityMode=SignAndEncrypt`, `SecurityPolicy=Basic256Sha256`, `AllowUnsecured=false` plus die `TrustedServerCertificatesPath` auf einen by-the-fixture-managed Pfad. **`OpcUaTestServerFixture`-Erweiterung**: ein neues `EstablishSecureTrustAsync(OpcUaClient client)`-Hook, der die Trust-Bridge nach der Client-`EnsureApplicationConfiguredAsync` aber vor dem ersten `ConnectAsync` aufruft. Tests in den **bestehenden** Pin-Klassen (`OpcUaRoundtripTests`, `OpcUaNegativeTests`) bleiben **unverändert** — sie setzen weiterhin `Defaults.ForHilSimulator()` (nun mit explizitem HilSimulator-Profile, gleicher Connect-Pfad wie heute). Nur die neuen Sub-Slice-D-Pins benutzen `Defaults.ForProductionSecure()`. **Fixture-Lifecycle**: PKI-Verzeichnisse pro Test-Klasse (heute schon), Trust-Bridge-Hook idempotent. |
| ⬜ | RM-M4-05-D | 6 pinned Integration-Pins gegen Embedded TestServer + Quality-Doku + Master-Plan-Cleanup — **~200-300 LOC** | Neue Datei `tests/integration/BatteryEms.OpcUa.IntegrationTests/OpcUaSecurityTests.cs` mit den sechs in §3 gelisteten Pins, in einer neuen Klasse `OpcUaSecurityTests : IClassFixture<OpcUaTestServerFixture>, IAsyncLifetime` analog zu `OpcUaRoundtripTests`/`OpcUaNegativeTests` (auch in `[Collection("OpcUa Integration")]` für Serialisierung; per-class Fixture per D-06 aus M4-08-A). **Pin 1 Secure-Handshake**: baut Client mit `Defaults.ForProductionSecure(host.EndpointUrl)`, ruft `_fixture.EstablishSecureTrustAsync(client)` auf, dann `client.ConnectAsync` + `client.ReadAsync(...)`; Result-StatusCode == Good. **Pin 2 Secure_handshake_sign_mode_succeeds_against_test_server**: baut Client mit `Defaults.ForProductionSecure(host.EndpointUrl)`, setzt `SecurityMode=Sign`, ruft `_fixture.EstablishSecureTrustAsync(client)` auf, dann `client.ConnectAsync` + `client.ReadAsync(...)`; Result-StatusCode == Good. **Pin 3 Allowlist-Reject**: baut Options mit `SecurityPolicy="Basic128Rsa15"`, ruft `EnsureValid()` direkt (kein Connect nötig); erwartet `InvalidOperationException` mit Message-Contain `opcua-security-policy-not-allowlisted`. **Pin 4 Production-Fail-Closed**: baut Options mit `RuntimeProfile=Production`, `SecurityMode=None`, `AllowUnsecured=true`, `AllowUnsecuredReason="hil-simulator-pre-m4-05"` (genau die heute-Test-Konfig!); erwartet `InvalidOperationException` mit Message-Contain `opcua-security-not-hardened-in-production`. **Pin 5 HilSimulator-Override**: baut Options mit `RuntimeProfile=HilSimulator`, `SecurityMode=None`, `AllowUnsecured=true`, `AllowUnsecuredReason="..."`; `EnsureValid()` lässt durch ohne Throw, plus structured warning EventId 4200 wird emittiert (Pin liest den Test-Logger).<br>**Pin 6 Trust-Store-Miss**: baut Client mit `Defaults.ForProductionSecure(host.EndpointUrl)` **ohne** `_fixture.EstablishSecureTrustAsync(client)` und erwartet `ConnectAsync`-Fehler `opcua-server-certificate-not-trusted`.<br>**Quality-Doku-Update**: `docs/user/quality.md` Abschnitt 2.2.2 wird erweitert um die neue 6. Pin-Inventory-Zeile (jetzt 13 Pins gesamt: 5 happy-path + 2 negativ/stress + 6 security). Plus ein Hinweis: pre-M4-05 lief der OPC-UA-Adapter ohne Cert-Trust; ab M4-05 ist Production-Default `SignAndEncrypt`. **Master-Plan-Wortlaut-Cleanup**: bei Closure flippt RM-M4-05-Zeile in `plan-RM-M4.md` auf ✅ mit dem in D-05 vorab gepinnten DoD-Replacement-Text. **F-17/F-18 in `note-RM-M4-followups.md` anlegen**: neue `## Item F-17:`- und `## Item F-18:`-Header mit Trigger-Beschreibung. |

---

## 5. Design-Entscheidungen

**D-01 OPC-UA-lokales `RuntimeProfile`-Field statt F-12-Erwartung.**
M4-05 implementiert `OpcUaRuntimeProfile` als adapter-lokales Field
auf `OpcUaAdapterOptions`. F-12 (Cross-Adapter-RuntimeProfile/
Security-Profile-Source aus M4-03) bleibt offen — wird später die
globale Quelle ergänzen, ohne dass M4-05 retro-passt.

Begründung gegen Alternative (a) „M4-05 wartet auf F-12": F-12 hat
heute keinen aktiven Trigger; die OPC-UA-Linie kann aber jetzt
produktiv freigegeben werden (das ist der Zweck dieses Slices).
Die Adapter-lokale Lösung ist konsistent mit dem heutigen
`AllowUnsecured`-Pattern (auch adapter-lokal, kein cross-adapter
state).

Begründung gegen Alternative (b) „M4-05 zündet F-12 mit": Cross-
Adapter-RuntimeProfile-Verkabelung ist eine Architektur-Entscheidung
quer zur Adapter-Linie (Modbus/MQTT haben heute keinen analogen
Slot); M4-05 würde dort mehr ändern als „OPC-UA-Security".

**Konsequenz für F-12-Carrier**: wenn F-12 zündet, wird die globale
Quelle in `BessConfigurationBootstrap` einen
`IRuntimeProfileSource`-Driven-Port konsumieren und das Adapter-
Field daraus feeden. Das ist ein einzeiliger Wrapper-Change, kein
Schema-Bruch — exakt der Zweck der adapter-lokalen
Vorbereitung.

**D-02 Production-Profile macht den AllowUnsecured-Bool-Guard
unwirksam.**
Pre-M4-05 ist der AllowUnsecured-Bool der einzige Schutz gegen
unverschlüsselte Production-Konnektivität. M4-05 schaltet das ab:
`RuntimeProfile=Production` + `SecurityMode=None` ist ein Startup-
Fehler **unabhängig von AllowUnsecured**.

Operator-Sicht:
- Pre-M4-05: `AllowUnsecured=true` reichte aus (Operator hatte
  einen Override-Knopf für Production-Endpoints im None-Modus).
- Post-M4-05: in Production muss `SecurityMode=Sign|SignAndEncrypt`.
  Wer trotzdem unsecured fahren muss (z. B. legacy server ohne
  cert support), muss explizit `RuntimeProfile=HilSimulator|
  Development` setzen — und das ist in einer produktiven Deploy-
  Konfiguration bewusst-sichtbar (kein silent-Default-Pfad).

**D-03 SecurityMode-Default schwenkt auf SignAndEncrypt.**
M4-04-D-04 hatte `SecurityMode=None` als Default mit Bool-Guard;
M4-05 schwenkt auf `SecurityMode=SignAndEncrypt`. Das ist ein
breaking-change für **Tests**, die heute keinen expliziten
SecurityMode setzen — das `EnsureValid` würde im Production-
Default-Pfad failen. Mitigation: alle bestehenden Test-Defaults
(`Defaults.ForHilSimulator`) setzen `RuntimeProfile=HilSimulator`,
das macht den None-AllowUnsecured-Pfad weiterhin gültig. Eine
Operator-Konfiguration ohne explizite Profile-/Mode-Wahl bekommt
den sicheren Default — was der Master-DoD verlangt.

**D-04 Allowlist-Erweiterung verlangt Plan-Änderung — kein Magic-
Config-Knopf.**
Master-DoD: „M4-Allowlist startet mit `Basic256Sha256`, jede
Erweiterung braucht Planänderung, Tests und Doku". Konsequenz:
`OpcUaSecurityPolicies` ist eine **statische Klasse** mit
const-Strings; die Allowlist ist hard-coded, keine
`OpcUaAdapterOptions.AllowedPolicies`-Liste. Wer eine Policy
hinzufügen will, muss den Adapter-Code anfassen, mit Plan-Slice
landen und Tests schreiben.

Heute in der Allowlist:
- `Basic256Sha256` — `http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256`

Bewusst draussen (Trigger F-17):
- `http://opcfoundation.org/UA/SecurityPolicy#Aes128Sha256RsaOaep`,
  `http://opcfoundation.org/UA/SecurityPolicy#Aes256Sha256RsaPss` —
  modernere RSA-Policies; nicht in heutiger Allowlist weil Server-Side-
  Support in der Praxis variabel ist.
- `Basic256` (deprecated), `Basic128Rsa15` (deprecated) — bewusst
  ausgeschlossen, würden im Code-Review explicit zurückgewiesen.
- ECC-Policies (`ECC_nistP256` etc.) — ECC-Cert-Provisioning ist
  nicht Teil von M4-05.

**D-05 Master-Plan-Wortlaut bei Closure (vorab gepinnt).**
Bei Closure wird RM-M4-05-Zeile in `plan-RM-M4.md` umformuliert.
Verbindlicher Replacement-Text:

> Slice-Plan:
> [`done/plan-RM-M4-05.md`](../done/plan-RM-M4-05.md). `OpcUaRuntime-
> Profile`-Field (`Development`/`HilSimulator`/`Production`,
> Default `Production`) plus `SecurityPolicy`-Allowlist
> (M4-Start: `Basic256Sha256`; Erweiterung verlangt Planänderung
> per D-04) auf `OpcUaAdapterOptions`. Production-Default schwenkt
> auf `SecurityMode=SignAndEncrypt`. `EnsureValid` wirft
> `opcua-security-not-hardened-in-production` bei
> `RuntimeProfile=Production` + `SecurityMode=None` (D-02 — der
> AllowUnsecured-Bool ist im Production-Profile bewusst nicht
> ausreichend), `opcua-security-policy-not-allowlisted` bei
> Off-Allowlist-Policy, `opcua-allow-unsecured-with-secure-mode-
> inconsistent` bei Konfigurations-Inkonsistenz. `OpcUaClient`
> bindet `AddSignAndEncryptPolicies` plus echtes Cert-Trust ohne
> AutoAccept im Production-Profile; `RuntimeProfile=HilSimulator|
> Development` behält das pre-M4-05-AutoAccept-Verhalten für
> Test-Defaults. Embedded TestServer-Fixture aus M4-08-A wird um
> SignAndEncrypt-Policies + bidirektionale Trust-Bridge erweitert.
> 6 pinned Security-Pins in `OpcUaSecurityTests.cs` (Secure-Handshake, Sign-Mode-Handshake, Allowlist-Reject, Production-Fail-Closed,
> HilSimulator-Override, Trust-Store-Miss). `make test-hil-opcua` läuft jetzt mit 13 Pins gesamt
> in `make gates` und `make ci`. Cross-Adapter-RuntimeProfile-
> Source bleibt **F-12** (M4-03-Followup); Allowlist-Erweiterung
> ist **F-17**; Cert-Rotation/Renewal ist **F-18**; User/Token-
> Identity ist **F-19** (alle in `note-RM-M4-followups.md`).

Der Closure-Reviewer matcht die Implementierung gegen diesen
Replacement-Text — keine Rückwärts-Rekonstruktion bei Closure.

**D-06 PKI-Pfad-Konvention bleibt M4-04-D-Pattern.**
M4-04-D-Review-Fix H5 hat per-Instanz-PKI-Pfade unter
`Path.GetTempPath()/BatteryEms/OpcUa/pki/{Guid:N}` etabliert.
M4-05 erbt das. Operator-bereitgestellte Server-Trusted-Store-
Pfade liegen im `OpcUaAdapterOptions.TrustedServerCertificatesPath`-
Feld; default leer ⇒ adapter erzeugt einen unter dem PKI-Root.
Die Operator-Variante ermöglicht Pre-Deployment-Cert-Provisioning
(Operator kopiert Server-Cert in einen wohlbekannten Pfad,
Adapter trustet sie beim Boot).

**D-07 Test-Layout bleibt M4-08-A-Pattern.**
`OpcUaSecurityTests.cs` ist eine separate Datei neben
`OpcUaRoundtripTests.cs` und `OpcUaNegativeTests.cs`. Beide
bestehenden Klassen bleiben unverändert (sie nutzen
`Defaults.ForHilSimulator()` mit explizitem
`RuntimeProfile=HilSimulator`-Setting); nur die neue Klasse nutzt
`Defaults.ForProductionSecure()`. Per-class Fixture-Isolation
und `[Collection("OpcUa Integration")]`-Serialisierung erbt von
M4-08-A D-06.

---

## 6. Akzeptanzkriterien

- **`OpcUaRuntimeProfile`-Enum** im Adapter-Projekt;
  `OpcUaAdapterOptions.RuntimeProfile`-Field mit Default `Production`.
- **`OpcUaSecurityPolicies`-Konstanten-Container** mit
  `Basic256Sha256` und `IsAllowed(string)`-Helper.
- **`SecurityMode`-Default** ist jetzt `SignAndEncrypt`;
  **`SecurityPolicy`-Default** ist `Basic256Sha256`.
- **`EnsureValid()`** wirft die vier neuen Reasons:
  - `opcua-security-not-hardened-in-production`,
  - `opcua-security-policy-not-allowlisted`,
  - `opcua-allow-unsecured-with-secure-mode-inconsistent`,
  - bestehender `opcua-security-not-hardened` bleibt für den
    HilSimulator/Development-Bool-Pfad.
- **`OpcUaClient.EnsureApplicationConfiguredAsync`** baut profile-
  abhängig: Production ohne AutoAccept, mit Trust-Validator;
  HilSimulator/Development mit AutoAccept (heutiges Verhalten).
- **Embedded TestServer** unterstützt SignAndEncrypt-Policies plus
  bidirektionale Trust-Bridge.
- **`Defaults.ForHilSimulator()`** setzt explizit
  `RuntimeProfile=HilSimulator`; **`Defaults.ForProductionSecure()`**
  als neuer Builder verfügbar.
- **`OpcUaSecurityTests.cs`** mit 5 grünen Pins.
- **Bestehende 7 Pins** in `OpcUaRoundtripTests` + `OpcUaNegativeTests`
  laufen weiterhin grün (kein Test-Regression).
- **`make test-hil-opcua` grün** mit 13 Pins gesamt; **`make gates`
  + `make ci` grün** unverändert (Verdrahtung aus M4-08-A bleibt).
- **Quality-Doku** (`docs/user/quality.md`) listet die 6 neuen Pins
  unter §2.2.2 plus den Production-Default-Schwenk-Hinweis.
- **`note-RM-M4-followups.md`** trägt jetzt explizit die Items
  F-17 (Allowlist-Erweiterung), F-18 (Cert-Rotation), F-19
  (User/Token-Identity) mit konkreten Triggern.
- **Slice-Plan** in `docs/plan/planning/done/plan-RM-M4-05.md`.
- **Master-Plan-Zeile RM-M4-05** flippt auf ✅ mit dem D-05-
  Replacement-Text.

---

## 7. Risiken und Tradeoffs

- **Test-Regression-Risiko durch SecurityMode-Default-Schwenk
  (D-03).** Jeder Test-Fixture-Aufbau, der heute nicht explizit
  `SecurityMode=None` UND `AllowUnsecured=true` UND
  `AllowUnsecuredReason!=null` setzt, schlug schon vor M4-05 am
  EnsureValid-Bool-Guard fehl — nach M4-05 schlägt zusätzlich
  `RuntimeProfile=Production` (Default) am None-Mode fehl, **selbst
  wenn AllowUnsecured=true gesetzt ist**. Mitigation: alle
  bestehenden Test-Defaults zentralisiert in `Defaults.cs`; die
  M4-05-A-DoD pinnt das `RuntimeProfile=HilSimulator`-Setting
  explizit.
- **OPC-Foundation-SDK-Cert-Trust-API-Stabilität.** Der echte
  Cert-Validator-Hook (ohne AutoAccept) verlangt SDK-API-Calls die
  eventuell zwischen Versions-Updates breaken. Mitigation:
  Sub-Slice B testet die Builder-Chain strukturell (Pin gegen
  `appConfig.SecurityConfiguration.AutoAcceptUntrustedCertificates`
  und `TrustedPeerCertificates.StorePath`). Bei Major-SDK-Upgrade
  sind diese Pins der erste Indikator für Breakage.
- **Trust-Bridge-Test-Affordance ist Cert-File-IO.** Sub-Slice C
  kopiert Cert-Files zwischen PKI-Verzeichnissen; das ist
  Filesystem-State, der bei Test-Crash leakt. Mitigation:
  `EmbeddedTestServerHost.DisposeAsync` löscht den PKI-Root
  recursive (heute schon); Trust-Bridge-Helper schreibt nur in den
  fixture-managed PKI-Pfad — kein Leak außerhalb.
- **Production-Fail-Closed-Pin ändert die heutige Test-Konfig-
  Gewohnheit.** Operatoren, die heute lokal `AllowUnsecured=true`
  in einer dev-Konfig setzen, kriegen nach M4-05 einen Boot-Fehler
  wenn `RuntimeProfile` nicht explizit auf `Development` umgestellt
  ist. Das ist **das gewollte Verhalten** (Master-DoD-Konsequenz),
  aber es ist eine Operator-UX-Hürde. Mitigation: die Error-Message
  trägt den expliziten Hinweis auf `RuntimeProfile=Development`-
  Override; Quality-Doku dokumentiert den Schwenk.
- **Allowlist-Erweiterung als Plan-Pflicht (D-04) ist eine Reibung,
  wenn ein Vendor-Spec eine andere Policy verlangt.** Das ist
  beabsichtigt — der Plan-Pflicht-Lock ist die Kontrolle gegen
  silent Policy-Schwenks im Operator-Lager. F-17 ist der saubere
  Carrier; ohne Trigger zündet er nicht.
- **F-12-Erwartung in `IProductionPreconditionProvider` bleibt
  unbeantwortet.** M4-05 ändert die OPC-UA-Adapter-Production-
  Fail-Closed-Logik, aber `DefaultProductionPreconditionProvider.
  Evaluate` (M4-03) gibt weiterhin `security-profile-enforcement-
  not-wired` zurück — die Aktivierungs-Use-Case-Schicht bleibt
  fail-closed bis F-12. Das ist konsistent mit D-01 (M4-05 ist
  adapter-lokal); Reviewer-Hinweis: F-12 wird die globale Quelle
  ergänzen, ohne M4-05 retro zu ändern.
- **Sub-Slice-D 5-Pin-Coverage ist verpflichtend.** Pin 5 deckt den
  produktiven Trust-Store-Miss-Fall ab (Server-Zertifikat nicht
  im Client-TrustStore ⇒ `opcua-server-certificate-not-trusted`), da der
  `SetAutoAcceptUntrustedCertificates(false)` in Production inzwischen
  deaktiviert ist.

---

## 8. Sequenz

**Schritt 1: Plan reviewen.** Externer Review-Pass analog zur
M4-04/M4-08-Linie. Kritische Punkte:
- Hält D-01 (adapter-lokales RuntimeProfile statt F-12-Erwartung)?
  Reviewer prüft, ob die Cross-Adapter-Erweiterung (F-12 später)
  ohne Schema-Bruch möglich bleibt.
- Hält D-04 (Allowlist als hard-coded statt config)? Reviewer
  prüft, ob ein konkretes Vendor-Spec-Beispiel den hard-coded
  Pfad bricht.
- Sind die 6 Pins in Sub-Slice D vollständig abgedeckt (darunter
  Trust-Store-Miss)?

**Schritt 2: Sub-Slices in Reihenfolge A → B → C → D umsetzen.**

1. **Sub-Slice A**: Domain/Options-Erweiterung. Reine Adapter-
   Schicht-Änderung; keine SDK-Calls. Test-First gegen
   `OpcUaAdapterOptionsTests`; bestehende Tests müssen mit
   `RuntimeProfile=HilSimulator`-Setting in `Defaults.cs` weiter
   grün laufen.
2. **Sub-Slice B**: SDK-Binding für SignAndEncrypt + Cert-Trust.
   Strukturelle Pins, kein echter Wire-Test (das ist D).
3. **Sub-Slice C**: Embedded TestServer + Trust-Bridge + Defaults-
   Builder. Bestehende 7 Pins müssen weiterhin grün laufen.
4. **Sub-Slice D**: 6 Security-Pins. Quality-Doku-Update.
   F-17/F-18/F-19 in note anlegen. Master-Plan-Cleanup.

**Schritt 3: Closure-Commit.** Pattern wie M4-04-D / M4-08-A — ein
Commit pro Sub-Slice plus optional Review-Fix-Commit nach
externer Review-Runde. Master-Plan-Move nach allen Sub-Slices
grün.

**Schritt 4 (optional): Production-Smoke gegen einen externen
OPC-UA-Server.** Wenn ein Operator-Cert für einen realen
HIL-/Vendor-Server bereitsteht, kann ein einmaliger Smoke-Test mit
`Defaults.ForProductionSecure()` gegen den realen Server-Endpoint
gefahren werden. Das ist **kein** Pin (kein CI-Asset); reine
Validierung dass die Produktiv-Konfiguration funktioniert. Falls
der Smoke fail-t, ist das Sub-Slice-B-Bug-Indikator.

---

## 9. Folgearbeiten (gehen in `note-RM-M4-followups.md`)

**Neu von M4-05-D explizit angelegt:**

- **F-17 OPC-UA-Security-Policy-Allowlist-Erweiterung.** Trigger:
  konkrete TSO-/Vendor-Spec verlangt eine Policy außerhalb der
  M4-05-Start-Allowlist (`Basic256Sha256`). Sub-Bullets:
  - (a) Policy-Konstante in `OpcUaSecurityPolicies` ergänzen.
  - (b) `IsAllowed`-Pin erweitern; `EnsureValid`-Pin pro neuer
    Policy.
  - (c) Embedded TestServer-Fixture um die Policy erweitern
    (Server muss `AddPolicy(SecurityMode, Policy)` rufen).
  - (d) Quality-Doku dokumentiert die erweiterte Allowlist.
- **F-18 OPC-UA-Cert-Rotation/Renewal.** Trigger: erstes Cert-
  Lifecycle-Event in der Operator-Praxis (Validity-Period läuft
  ab; Server liefert eine neue Cert; Adapter muss reload-en ohne
  Process-Restart). Sub-Bullets:
  - (a) Cert-Watcher auf dem Trusted-Store-Path.
  - (b) `OpcUaClient.ReloadCertificatesAsync()`-Pfad.
  - (c) Pin-Test gegen Embedded TestServer mit Cert-Swap mid-stream.
- **F-19 OPC-UA-User-Identity (UserName/Password / UserToken).**
  Trigger: TSO-Spec verlangt nicht-Anonymous-Authentication. Sub-
  Bullets:
  - (a) `OpcUaAdapterOptions.UserIdentity` als
    `OpcUaUserIdentityOptions`-Record (UserName + Password aus
    Secret-Store, oder UserToken).
  - (b) `OpcUaClient.ConnectAsync` reicht die Identity an
    `DefaultSessionFactory.CreateAsync` durch.
  - (c) Pin-Test gegen Embedded TestServer mit UserName-Token-Policy.

**Bestehend, unverändert:**

- **F-09 OPC-UA-Activation-Source-Adapter** (incl. Failover-Replay-
  via-Reconnect) — von M4-08-A angelegt; Trigger TSO-Spec mit
  OPC-UA-Aktivierungsendpoint.
- **F-12 Cross-Adapter-RuntimeProfile/Security-Profile-Source** —
  von M4-03 angelegt; Trigger erste Slice die ein einheitliches
  Production-Profile-Signal über mehrere Adapter braucht. Ergänzt
  später die globale Quelle für `OpcUaAdapterOptions.RuntimeProfile`
  (D-01-Konsequenz).
- **F-13 OPC-UA-Multi-Server / Endpoint-Failover** — Trigger
  dual-Verteilnetzbetreiber-Spec.
- **F-14 OPC-UA-Method-Calls / HistoricalAccess / Events**.
- **F-15 OPC-UA-Type-System-Erweiterung**.
- **F-16 Compose-Sidecar-Fallback** (heute nicht zündend).
