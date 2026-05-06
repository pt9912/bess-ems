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
	build ci runtime fullbuild

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
	@echo "Welle 5 (Closure, active):"
	@echo "  make build           Multi-stage runtime image (non-root, /health HEALTHCHECK)"
	@echo "  make runtime         Compose-up + /health probe + down (depends on make build)"
	@echo "  make test-container  Runtime smoke (alias for make runtime)"
	@echo "  make ci              Sequential CI run of every M1 mandatory gate"
	@echo "  make fullbuild       make ci + make build + make runtime (M1 closure)"

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
