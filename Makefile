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

DOCKER_BUILD = $(DOCKER) build $(BUILD_CONTEXT) \
	-f $(DOCKERFILE) \
	--build-arg BUILD_CONFIGURATION=$(BUILD_CONFIGURATION) \
	$(DOCKER_BUILD_ARGS)

.DEFAULT_GOAL := help

.PHONY: help \
	lint arch-check gates \
	test test-safety test-integration test-container coverage-gate \
	simulator-test simulator-race simulator-lint simulator-coverage-gate \
	build ci runtime fullbuild lock-refresh \
	schema-validate schema-generate schema-snapshot-test

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
	@echo "  make lint        Build with -warnaserror plus code-metrics gate (CA1501/1502/1505/1506)"
	@echo "  make arch-check  Boundary tests (Dependency Rule + tabus)"
	@echo ""
	@echo "Welle 2 (Domain & Control, active):"
	@echo "  make test          Domain + Application unit tests"
	@echo "  make test-safety   Safety-path subset (Category=Safety)"
	@echo "  make coverage-gate Line-coverage gate, 90% per M1 production assembly"
	@echo ""
	@echo "Aggregated:"
	@echo "  make gates       Aggregated mandatory M1 gates for the current wave"
	@echo ""
	@echo "Welle 3 (Simulator + Adapters, partially active):"
	@echo "  make simulator-test          Go simulator unit tests"
	@echo "  make simulator-race          Race-detector on goroutine-bearing packages (CGO=1)"
	@echo "  make simulator-lint          golangci-lint with SOLID profile"
	@echo "  make simulator-coverage-gate Go coverage gate (90% line)"
	@echo "  make test-integration        Modbus roundtrip vs Go-Simulator via docker compose"
	@echo ""
	@echo "Maintenance:"
	@echo "  make lock-refresh    Refresh packages.lock.json files in Docker (per docs/user/quality.md §1.4)"
	@echo "  make schema-validate      Validate schema/schema.yaml via d-migrate (RM-M2-MIG-02)"
	@echo "  make schema-generate      Generate ?001_initial.sql from schema/schema.yaml (RM-M2-MIG-02)"
	@echo "  make schema-snapshot-test Diff M1 BessDbInitializer DDL vs. 0001_initial.sql (RM-M2-MIG-03)"
	@echo ""
	@echo "Welle 5 (Closure, active):"
	@echo "  make build           Multi-stage runtime image (non-root, /health HEALTHCHECK)"
	@echo "  make runtime         Compose-up + /health probe + down (depends on make build)"
	@echo "  make test-container  Runtime smoke (alias for make runtime)"
	@echo "  make ci              Sequential CI run of every M1 mandatory gate"
	@echo "  make fullbuild       make ci + make build + make runtime (M1 closure)"

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
D_MIGRATE_IMAGE ?= d-migrate:dev
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

# RM-M2-MIG-03: snapshot test that verifies the committed
# 0001_initial.sql produces the same database schema as the M1
# BessDbInitializer + BessDbSchema.CreateScript path. Spins up a
# throw-away Postgres container, applies both DDLs to separate
# databases, compares column / constraint / index metadata via
# information_schema queries (pg_dump-level comparison would surface
# cosmetic diffs — column order, restrict salts — without semantic
# meaning). Override PG_IMAGE to test against postgres:17.
schema-snapshot-test:
	scripts/schema-snapshot-test.sh

# --- Welle 1 (active) ------------------------------------------------------

lint:
	$(DOCKER_BUILD) --target lint -t $(IMAGE_PREFIX)-lint:latest

arch-check:
	$(DOCKER_BUILD) --target arch-check -t $(IMAGE_PREFIX)-arch-check:latest

# --- Welle 2 (active) ------------------------------------------------------

test:
	$(DOCKER_BUILD) --target test -t $(IMAGE_PREFIX)-test:latest

test-safety:
	$(DOCKER_BUILD) --target test-safety -t $(IMAGE_PREFIX)-test-safety:latest

coverage-gate:
	$(DOCKER_BUILD) --target coverage-gate -t $(IMAGE_PREFIX)-coverage-gate:latest

# --- Aggregated gates ------------------------------------------------------

gates: lint arch-check test test-safety coverage-gate simulator-lint simulator-test simulator-race simulator-coverage-gate
	@echo "[gates] M1 mandatory gates green: lint, arch-check, test, test-safety, coverage-gate, simulator-{lint,test,race,coverage-gate}"

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

# --- Welle 5 (partially active) --------------------------------------------

# Runtime image: multi-stage publish + non-root aspnet image with /health
# HEALTHCHECK (RM-M1-19b, LH-DEPLOY-001/003).
build:
	$(DOCKER_BUILD) --target runtime -t $(IMAGE_PREFIX)-runtime:latest

# Compose smoke: bring the production-shaped stack up, poll /health, down.
# Requires: `make build` (bess-ems image) and `make -C simulators/bess-field-sim build`
# (bess-field-sim image). The target rebuilds them itself so a fresh
# checkout reaches a healthy stack with one command.
runtime: build
	$(SIMULATOR_MAKE) build
	$(DOCKER) compose -f deploy/compose.yml up -d --wait --wait-timeout 60
	@echo "[runtime] stack is up; probing /health"
	$(DOCKER) compose -f deploy/compose.yml exec -T bess-ems curl --fail --silent --show-error http://localhost:8080/health
	@echo "[runtime] /health ok; tearing down"
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
ci: lint arch-check test test-safety coverage-gate \
    simulator-lint simulator-test simulator-race simulator-coverage-gate \
    test-integration
	@echo "[ci] M1 mandatory gates green: lint, arch-check, test, test-safety, coverage-gate, simulator-{lint,test,race,coverage-gate}, test-integration"

# Fresh-clone-naher Komplettlauf: alle CI-Gates plus Runtime-Image und
# Compose-Smoke. Letzte Stufe vor einem M1-Tag (RM-M1-20).
fullbuild: ci build runtime
	@echo "[fullbuild] M1 closure: all gates + runtime image + compose smoke green"
