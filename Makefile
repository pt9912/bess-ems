# Bess-EMS Makefile (RM-M1-21).
#
# Welle 1 (Foundation) liefert: lint, arch-check, gates.
# Spätere Wellen aktivieren weitere Targets; sie sind hier sichtbar und
# liefern eine klare Meldung auf ihre Aktivierungswelle.

DOCKER ?= docker
DOCKERFILE ?= Dockerfile
BUILD_CONTEXT ?= .
IMAGE_PREFIX ?= bess-ems
BUILD_CONFIGURATION ?= Release
DOCKER_BUILD_ARGS ?=
HELM ?= helm
HELM_CHART ?= deploy/helm/bess-ems

DOCKER_BUILD = $(DOCKER) build $(BUILD_CONTEXT) \
	-f $(DOCKERFILE) \
	--build-arg BUILD_CONFIGURATION=$(BUILD_CONFIGURATION) \
	$(DOCKER_BUILD_ARGS)

.DEFAULT_GOAL := help

.PHONY: help \
	lint solid-suppression-gate arch-check gates \
	test test-safety test-mpc-property test-replay test-integration test-hil-modbus test-hil-opcua test-hil-optimization-core test-optimization-core-compose test-hil-closed-loop test-container coverage-gate \
	native-build test-native-interop test-native-parity \
	native-lint native-sanitizer native-coverage-report native-coverage-gate native-coverage-exclusions \
	simulator-test simulator-race simulator-lint simulator-coverage-gate \
	build ci runtime fullbuild lock-refresh release-assets \
	schema-validate schema-generate schema-drift-check \
	helm-lint

help:
	@echo "bess-ems Makefile (RM-M1-21)"
	@echo ""
	@echo "Override variables (defaults shown):"
	@echo "  DOCKER=$(DOCKER)"
	@echo "  DOCKERFILE=$(DOCKERFILE)"
	@echo "  BUILD_CONTEXT=$(BUILD_CONTEXT)"
	@echo "  IMAGE_PREFIX=$(IMAGE_PREFIX)"
	@echo "  BUILD_CONFIGURATION=$(BUILD_CONFIGURATION)"
	@echo "  DOCKER_BUILD_ARGS=$(DOCKER_BUILD_ARGS)"
	@echo ""
	@echo "Welle 1 (Foundation, active):"
	@echo "  make lint        SOLID suppression audit + build with -warnaserror/code-metrics gate"
	@echo "  make arch-check  Boundary tests (Dependency Rule + tabus)"
	@echo ""
	@echo "Welle 2 (Domain & Control, active):"
	@echo "  make test          Domain + Application unit tests"
	@echo "  make test-safety   Safety-path subset (Category=Safety)"
	@echo "  make test-mpc-property  MPC determinism / identity / replay-hook pins (RM-M5-02)"
	@echo "  make test-replay   Replay manifest / fixture / golden-diff pins (RM-M5-04)"
	@echo "  make coverage-gate Line-coverage gate, 90% per M1 production assembly"
	@echo ""
	@echo "Aggregated:"
	@echo "  make gates       Aggregated mandatory gates: M1 + M3 native"
	@echo "  make ci          Sequential CI run of all mandatory gates incl. schema + integration"
	@echo ""
	@echo "Welle 3 (Simulator + Adapters, partially active):"
	@echo "  make simulator-test          Go simulator unit tests"
	@echo "  make simulator-race          Race-detector on goroutine-bearing packages (CGO=1)"
	@echo "  make simulator-lint          golangci-lint with SOLID profile"
	@echo "  make simulator-coverage-gate Go coverage gate (90% line)"
	@echo "  make test-integration        Modbus roundtrip vs Go-Simulator via docker compose"
	@echo "  make test-hil-modbus         Optional: HIL roundtrip vs bess-hil-simulator:local (RM-M2-HIL-08)"
	@echo "  make test-hil-opcua          5 pinned OPC-UA-Roundtrips vs embedded TestServer (RM-M4-04 Sub-Slice D)"
	@echo "  make test-hil-optimization-core   26 pins vs In-Process gRPC-Sidecar (RM-M5-01/RM-M5-04/RM-M5-05)"
	@echo "  make test-optimization-core-compose Worker + standalone optimization-core TestSidecar compose gate (RM-M5-06)"
	@echo "  make test-hil-closed-loop    Optional: Closed-loop optimize→dispatch→HIL smoke (Carve-out Demo-01)"
	@echo ""
	@echo "Welle M3 (active):"
	@echo "  make native-build               Build + smoke-test native control core (RM-M3-06 part 1)"
	@echo "  make test-native-interop        Layout / ABI / non-finite contract against real .so (RM-M3-07)"
	@echo "  make test-native-parity         Replay-based native↔.NET parity gate, cases.v1.json (RM-M3-10)"
	@echo "  make native-lint                clang-tidy gate, --warnings-as-errors=* (RM-M3-09)"
	@echo "  make native-sanitizer           ASan + UBSan run on native test suite (RM-M3-09)"
	@echo "  make native-coverage-report     Build gcovr report and print it (RM-M3-09)"
	@echo "  make native-coverage-gate       100% line coverage gate, override BCC_COVERAGE_THRESHOLD (RM-M3-09)"
	@echo "  make native-coverage-exclusions Reject GCOVR exclusion markers in native src/ (RM-M3-09)"
	@echo ""
	@echo "Maintenance:"
	@echo "  make lock-refresh    Refresh packages.lock.json files in Docker (per docs/user/quality.md §1.4)"
	@echo "  make schema-validate      Validate schema/schema.yaml via d-migrate (RM-M2-MIG-02)"
	@echo "  make schema-generate      Generate ?001_initial.sql from schema/schema.yaml (RM-M2-MIG-02)"
	@echo "  make helm-lint            Lint/render Kubernetes Helm chart (RM-M6-03)"
	@echo ""
	@echo "Welle 5 (Closure, active):"
	@echo "  make build           Multi-stage runtime image (non-root, /health HEALTHCHECK)"
	@echo "  make runtime         Compose-up + /health probe + down (depends on make build)"
	@echo "  make test-container  Runtime smoke (alias for make runtime)"
	@echo "  make ci              Sequential CI run of every M1 mandatory gate"
	@echo "  make fullbuild       make ci + make build + make runtime (M1 closure)"
	@echo "  make release-assets VERSION=vX.Y.Z   Local dry-run of release artefacts (no push)"

# --- Maintenance -----------------------------------------------------------

# Lock-file refresh per docs/user/quality.md §1.4. Use after bumping a
# version in Directory.Packages.props or adding a new PackageReference;
# the refreshed packages.lock.json files MUST be committed alongside
# the version change so `make lint` (which runs `dotnet restore --locked-mode`)
# stays green for the next CI run.
lock-refresh:
	$(DOCKER) run --rm -v "$$(pwd)":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
		dotnet restore BatteryEms.sln /p:RestoreLockedMode=false

# --- RM-M2-MIG-02: schema tooling (d-migrate) ------------------------------

# d-migrate is invoked via Docker. The image override stays a Make
# variable so CI can pin a registry-published digest while local
# development uses the freshly built dev tag. Once the next d-migrate
# release lands on ghcr.io/pt9912/d-migrate, override is set to
# `ghcr.io/pt9912/d-migrate:<version>@sha256:<digest>` (parallel to
# the NuGet lock-file discipline in docs/user/quality.md §1.4).
D_MIGRATE_IMAGE ?= ghcr.io/pt9912/d-migrate:0.9.6@sha256:e4ad469ea9bdd6a2d6138a2ba68096581273d3b64198d9b79fe96376ba3c1940
SCHEMA_DIR := schema
SCHEMA_SOURCE := $(SCHEMA_DIR)/schema.yaml
GENERATED_SQL := src/adapters/driven/BatteryEms.Adapters.Persistence/Migrations/RunOnce/0001_initial.sql

# Static YAML check — runs without a database, returns non-zero on
# any structural violation. CI gate alongside `make lint`.
schema-validate:
	$(DOCKER) run --rm -v "$$(pwd)":/work -w /work $(D_MIGRATE_IMAGE) \
		schema validate --source $(SCHEMA_SOURCE)

# Re-generate $(GENERATED_SQL) from $(SCHEMA_SOURCE). The committed SQL
# file is the build artefact; re-running this target on a clean
# checkout MUST produce a zero-diff git status for the schema file —
# any drift between the YAML source and the committed SQL is a
# build error (drift-check gate, runs on PRs that touch the schema).
#
# The post-generation sed strips two non-deterministic outputs that
# would otherwise break the zero-diff promise: (1) the `Generated:
# <timestamp>` line that d-migrate writes into every header; (2) the
# `*.report.yaml` companion file (also timestamp-bearing) which is
# .gitignore'd anyway but removed here so a stray un-ignored copy
# can't leak into a commit.
schema-generate:
	$(DOCKER) run --rm -v "$$(pwd)":/work -w /work $(D_MIGRATE_IMAGE) \
		schema generate \
		--source $(SCHEMA_SOURCE) \
		--target postgresql \
		--output $(GENERATED_SQL)
	sed -i 's/| Generated: [0-9TZ:.\-]*$$/| Generated: <stripped — see Makefile schema-generate>/' $(GENERATED_SQL)
	rm -f $(GENERATED_SQL:.sql=.report.yaml)

# Drift gate (RM-M2-MIG-02): re-generate the SQL from YAML and fail
# if the result differs from what's committed. Wired into `make ci`
# so a direct edit of 0001_initial.sql without an echoing YAML
# update never reaches main.
schema-drift-check: schema-generate
	@git diff --exit-code -- $(GENERATED_SQL) \
		|| (echo "[drift] schema/schema.yaml and $(GENERATED_SQL) have drifted — re-run 'make schema-generate' and commit, or fix the YAML" >&2; exit 1)

# --- RM-M6-03: Kubernetes / Helm ------------------------------------------

helm-lint:
	$(HELM) lint $(HELM_CHART)
	$(HELM) template bess-ems $(HELM_CHART) >/tmp/bess-ems-helm-shared.yaml
	$(HELM) template bess-ems-worker-per-asset $(HELM_CHART) \
		--set topology.mode=workerPerAsset >/tmp/bess-ems-helm-worker-per-asset.yaml
	$(HELM) template bess-ems-optimization-core $(HELM_CHART) \
		--set optimizationCore.enabled=true >/tmp/bess-ems-helm-optimization-core.yaml
	$(HELM) template bess-ems-optimization-core-mtls $(HELM_CHART) \
		--set optimizationCore.externalEndpoint=https://optimization-core.example:8443 \
		--set optimizationCore.transport.mtls.enabled=true \
		--set optimizationCore.transport.mtls.clientCertificateSecret=bess-ems-optimization-core-client \
		--set optimizationCore.transport.mtls.trustedServerCertificatesSecret=bess-ems-optimization-core-ca \
		>/tmp/bess-ems-helm-optimization-core-mtls.yaml
	$(HELM) template bess-ems-mqtt $(HELM_CHART) \
		--set topology.mode=workerPerAsset \
		--set mqtt.enabled=true >/tmp/bess-ems-helm-mqtt.yaml

# --- Welle 1 (active) ------------------------------------------------------

solid-suppression-gate:
	./scripts/solid-suppression-gate.sh

lint: solid-suppression-gate
	$(DOCKER_BUILD) --target lint -t $(IMAGE_PREFIX)-lint:latest

arch-check:
	$(DOCKER_BUILD) --target arch-check -t $(IMAGE_PREFIX)-arch-check:latest

# --- Welle 2 (active) ------------------------------------------------------

test:
	$(DOCKER_BUILD) --target test -t $(IMAGE_PREFIX)-test:latest

test-safety:
	$(DOCKER_BUILD) --target test-safety -t $(IMAGE_PREFIX)-test-safety:latest

test-mpc-property:
	$(DOCKER_BUILD) --target test-mpc-property -t $(IMAGE_PREFIX)-test-mpc-property:latest

test-replay:
	$(DOCKER_BUILD) --target test-replay -t $(IMAGE_PREFIX)-test-replay:latest

coverage-gate:
	$(DOCKER_BUILD) --target coverage-gate -t $(IMAGE_PREFIX)-coverage-gate:latest

# --- Aggregated gates ------------------------------------------------------

gates: lint arch-check test test-safety test-mpc-property test-replay coverage-gate \
	simulator-lint simulator-test simulator-race simulator-coverage-gate \
	native-build native-lint native-sanitizer \
	native-coverage-gate native-coverage-exclusions \
	test-native-interop test-native-parity \
	test-hil-opcua test-hil-optimization-core test-optimization-core-compose
	@echo "[gates] mandatory gates green: M1 (lint, arch-check, test, test-safety, coverage-gate, simulator-{lint,test,race,coverage-gate}) + M3 native (build, lint, sanitizer, coverage-gate, coverage-exclusions, test-native-{interop,parity}) + M4 (test-hil-opcua) + M5 (test-hil-optimization-core, test-optimization-core-compose, test-mpc-property, test-replay)"

# --- Welle 3 (partially active) --------------------------------------------

SIMULATOR_DIR := simulators/bess-field-sim
SIMULATOR_MAKE := $(MAKE) -C $(SIMULATOR_DIR)

simulator-test:
	$(SIMULATOR_MAKE) test

simulator-race:
	$(SIMULATOR_MAKE) race

simulator-lint:
	$(SIMULATOR_MAKE) lint

simulator-coverage-gate:
	$(SIMULATOR_MAKE) coverage-gate

test-integration:
	$(SIMULATOR_MAKE) build
	$(DOCKER) compose -f tests/integration/compose.yml up --build --abort-on-container-exit --exit-code-from test-runner; \
	exit_code=$$?; \
	$(DOCKER) compose -f tests/integration/compose.yml down -v --remove-orphans >/dev/null 2>&1; \
	exit $$exit_code

# RM-M4-04 Sub-Slice D: 5 pinned end-to-end Tests gegen den embedded
# OPC-UA-TestServer (process-internal, kein Compose-Asset, kein Sidecar).
# Schnell iterierbar; in `make ci` über das gleiche Stage erreichbar
# (siehe Dockerfile `test-hil-opcua`).
test-hil-opcua:
	$(DOCKER_BUILD) --target test-hil-opcua -t $(IMAGE_PREFIX)-test-hil-opcua:latest

# RM-M5-01..05: pinned Tests gegen den In-Process gRPC-Sidecar
# (Grpc.AspNetCore + Kestrel-UDS-Listener im selben Test-Prozess).
# Process-internal — das echte Cross-Container-Gate ist RM-M5-06
# (`make test-optimization-core-compose`).
test-hil-optimization-core:
	$(DOCKER_BUILD) --target test-hil-optimization-core -t $(IMAGE_PREFIX)-test-hil-optimization-core:latest

# RM-M5-06: real cross-container Worker + optimization-core sidecar gate.
# Builds the production runtime image plus a standalone test-sidecar image,
# then drives Health, Sidecar-Optimize, Sidecar-stop fallback and restart.
test-optimization-core-compose: build
	$(DOCKER_BUILD) --target optimization-core-test-sidecar -t $(IMAGE_PREFIX)-optimization-core-test-sidecar:latest
	DOCKER=$(DOCKER) IMAGE_PREFIX=$(IMAGE_PREFIX) scripts/test-optimization-core-compose.sh

# RM-M2-HIL-08: optionales HIL-Gate. Bringt den externen
# `bess-hil-simulator:local`-Container hoch (siehe HIL-OPEN-01:
# muss vorab lokal gebaut sein) und führt nur das HIL-Test-Projekt
# aus. NICHT in `make ci` / `make test-integration` verdrahtet —
# der M1-Pflichtpfad bleibt auf bess-field-sim.
test-hil-modbus:
	$(DOCKER) compose -f tests/hil/compose.yml up --build --abort-on-container-exit --exit-code-from hil-test-runner; \
	exit_code=$$?; \
	$(DOCKER) compose -f tests/hil/compose.yml down -v --remove-orphans >/dev/null 2>&1; \
	exit $$exit_code

# RM-M2 Carve-out Demo-01: Closed-Loop-Smoke. Bringt deploy/compose.
# hil.yml hoch (bess-ems + Postgres + Mosquitto + bess-hil-simulator),
# postet einen Day-Ahead-Optimize-Request und prüft dass die
# Discharge-Order durch die EMS-Pipeline an HIL-Modbus gelangt.
# Verlangt BESS_HIL_OPERATOR_TOKEN in der Umgebung. Nicht in
# `make ci` enthalten — gleiches Opt-in-Modell wie test-hil-modbus.
test-hil-closed-loop:
	scripts/hil-closed-loop-smoke.sh

# --- Welle M3 native control core (active) ---------------------------------

# RM-M3-06 part 1: build + smoke-test the native library inside a
# dedicated Docker stage. Does NOT install the .so into the runtime
# image (that's RM-M3-06 part 2 once routing in RM-M3-04/05 can
# consume it) and is NOT in `make ci` yet; gate wiring follows with
# RM-M3-09/11.
native-build:
	$(DOCKER_BUILD) --target native-build -t $(IMAGE_PREFIX)-native-build:latest

# RM-M3-07 / RM-M3-11: native-interop integration tests against the
# real libbattery_control_core.so — layout, ABI handshake, and
# non-finite-input contract (Category!=Parity). Werte-Parität is
# the separate test-native-parity gate.
test-native-interop:
	$(DOCKER_BUILD) --target test-native-interop -t $(IMAGE_PREFIX)-test-native-interop:latest

# RM-M3-10 / RM-M3-11: replay-based native↔.NET parity gate.
# Loads tests/fixtures/native_parity/cases.v1.json and asserts
# both kernels match the documented expectations for every case.
test-native-parity:
	$(DOCKER_BUILD) --target test-native-parity -t $(IMAGE_PREFIX)-test-native-parity:latest

# RM-M3-09 native-quality gates. Each runs in its own dedicated
# Docker stage on the same Ubuntu Noble base as the runtime image so
# clang-tidy, sanitizer and gcovr findings reflect the production
# toolchain. None of these are wired into `make ci` / `make gates`
# yet — that's RM-M3-11's scope.

# native-lint: clang-tidy with --warnings-as-errors=* against the
# project's `.clang-tidy` config. Fails on any finding.
native-lint:
	$(DOCKER_BUILD) --target native-lint -t $(IMAGE_PREFIX)-native-lint:latest

# native-sanitizer: rebuild .so + tests with ASan + UBSan and run
# the doctest suite with -fno-sanitize-recover=all (any detection
# is fatal). Catches use-after-free, undefined behaviour and
# misaligned pointer derefs as the kernel surface grows.
native-sanitizer:
	$(DOCKER_BUILD) --target native-sanitizer -t $(IMAGE_PREFIX)-native-sanitizer:latest

# native-coverage-report: gcovr report (developer-facing). Builds
# the report stage and prints it on stdout; the threshold check
# is the separate native-coverage-gate target. Renamed from the
# earlier `native-coverage` to align with the RM-M3-11 plan
# vocabulary (native-coverage-report vs native-coverage-gate).
BCC_COVERAGE_THRESHOLD ?= 100

native-coverage-report:
	$(DOCKER_BUILD) --target native-coverage -t $(IMAGE_PREFIX)-native-coverage:latest
	$(DOCKER) run --rm $(IMAGE_PREFIX)-native-coverage:latest

# native-coverage-gate: 100 % line on native/battery_control_core/src/.
# Override BCC_COVERAGE_THRESHOLD locally during a refactor; CI keeps
# the default.
native-coverage-gate:
	$(DOCKER_BUILD) --target native-coverage-gate \
		--build-arg BCC_COVERAGE_THRESHOLD=$(BCC_COVERAGE_THRESHOLD) \
		-t $(IMAGE_PREFIX)-native-coverage-gate:latest

# native-coverage-exclusions: reject every gcovr exclusion marker in
# native/battery_control_core/src/. The native core keeps 100 % line
# coverage without denominator carve-outs.
native-coverage-exclusions:
	@echo "[native-coverage-exclusions] auditing native/battery_control_core/src/ for gcovr exclusion markers..."
	@awk ' \
		BEGIN { marker = "GCOVR_EXCL_"; err = 0 } \
		index($$0, marker "START") || index($$0, marker "STOP") { \
			printf "ERROR: coverage exclusion marker is not allowed: %s:%d: %s\n", FILENAME, FNR, $$0 > "/dev/stderr"; \
			err = 1; \
		} \
		END { exit err }' \
		native/battery_control_core/src/*.c \
		|| (echo "[native-coverage-exclusions] FAIL — coverage exclusion markers are not allowed" >&2; exit 1)
	@echo "[native-coverage-exclusions] OK — no coverage exclusion markers in native src/"

# --- Welle 5 (partially active) --------------------------------------------

# Runtime image: multi-stage publish + non-root aspnet image with /health
# HEALTHCHECK (RM-M1-19b, LH-DEPLOY-001/003).
build:
	$(DOCKER_BUILD) --target runtime -t $(IMAGE_PREFIX)-runtime:latest

# Compose smoke: bring the production-shaped stack up, poll /health, down.
# Requires: `make build` (bess-ems image) and `make -C simulators/bess-field-sim build`
# (bess-field-sim image). The target rebuilds them itself so a fresh
# checkout reaches a healthy stack with one command.
#
# RM-M3-06 part 2: also verify libbattery_control_core.so is in place
# at the runtime-image path NativeControlOptions.LibraryPath defaults
# to (/app/native/libbattery_control_core.so) and that the dynamic
# linker can resolve every dependency. The build-time ldd gate in
# the Dockerfile already covers unresolved-deps failures; this
# in-container check covers post-build mishaps (e.g. a volume mount
# shadowing the path) and proves the production deployment shape
# stays M3-D2-ready without enabling the routing yet.
runtime: build
	$(SIMULATOR_MAKE) build
	$(DOCKER) compose -f deploy/compose.yml up -d --wait --wait-timeout 60
	@echo "[runtime] stack is up; probing /health"
	$(DOCKER) compose -f deploy/compose.yml exec -T bess-ems curl --fail --silent --show-error http://localhost:8080/health
	@echo "[runtime] /health ok; verifying native control library is in place"
	$(DOCKER) compose -f deploy/compose.yml exec -T bess-ems test -f /app/native/libbattery_control_core.so
	$(DOCKER) compose -f deploy/compose.yml exec -T bess-ems sh -c 'ldd /app/native/libbattery_control_core.so > /tmp/ldd 2>&1; if grep -q "not found" /tmp/ldd; then cat /tmp/ldd >&2; exit 1; fi'
	@echo "[runtime] native control library at /app/native/ resolves cleanly; tearing down"
	$(DOCKER) compose -f deploy/compose.yml down -v --remove-orphans

# Container smoke: same as `runtime` but used as a gate target — the
# subsequent RM-M1-19c step extends this with /metrics + a regulation
# cycle smoke.
test-container: runtime
	@echo "[test-container] runtime smoke green"

# CI-kompatibler Lauf der M1-Pflicht-Gates in dokumentierter Reihenfolge
# (RM-M1-20). Erst .NET-Lint/-Boundary/-Tests, dann Coverage, dann
# Simulator und zuletzt Integration — wenn ein früheres Gate kippt,
# bricht der Lauf hier ab. Container-Smoke gehört zu `runtime`/`fullbuild`.
ci: lint arch-check test test-safety test-mpc-property test-replay coverage-gate \
    simulator-lint simulator-test simulator-race simulator-coverage-gate \
    schema-validate schema-drift-check \
    native-build native-lint native-sanitizer \
    native-coverage-gate native-coverage-exclusions \
    test-native-interop test-native-parity \
    test-hil-opcua test-hil-optimization-core \
    test-optimization-core-compose test-integration
	@echo "[ci] mandatory gates green: M1 (lint, arch-check, test, test-safety, coverage-gate, simulator-*) + M2 schema (validate, drift-check) + M3 native (build, lint, sanitizer, coverage-gate, coverage-exclusions, test-native-{interop,parity}) + M4 (test-hil-opcua) + M5 (test-hil-optimization-core, test-optimization-core-compose, test-mpc-property, test-replay) + test-integration"

# Fresh-clone-naher Komplettlauf: alle CI-Gates plus Runtime-Image und
# Compose-Smoke. Letzte Stufe vor einem M1-Tag (RM-M1-20).
fullbuild: ci build runtime
	@echo "[fullbuild] M1 closure: all gates + runtime image + compose smoke green"

# --- Release-Trockenübung (docs/user/releasing.md §7) ----------------------
#
# Produziert lokal die gleichen Release-Assets wie der CI-Workflow, OHNE
# Push und ohne GitHub-Release. Pflicht-Schritt vor einem ersten Tag in
# einem neuen Major-/Minor-Zweig. Setzt voraus, dass `make build`
# (Runtime-Image $(IMAGE_PREFIX)-runtime:latest) bereits gelaufen ist;
# der Target ruft `build` selbst auf, um die .so-Extraktion deterministisch
# zu halten.
#
# Aufruf: `make release-assets VERSION=v1.0.0`
RELEASE_DIR ?= artifacts/release-local
SYFT_IMAGE ?= anchore/syft:v1.17.0
.PHONY: release-assets
release-assets: build
	@VERSION="$(VERSION)" \
	 RELEASE_DIR="$(RELEASE_DIR)" \
	 IMAGE_PREFIX="$(IMAGE_PREFIX)" \
	 HELM_CHART="$(HELM_CHART)" \
	 HELM="$(HELM)" \
	 DOCKER="$(DOCKER)" \
	 SYFT_IMAGE="$(SYFT_IMAGE)" \
	 scripts/build-release-assets.sh
