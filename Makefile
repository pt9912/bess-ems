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
	simulator-test simulator-lint simulator-coverage-gate \
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
	@echo "  make simulator-lint          golangci-lint with SOLID profile"
	@echo "  make simulator-coverage-gate Go coverage gate (90% line)"
	@echo "  make test-integration        pending (RM-M1-09/10/11)"
	@echo ""
	@echo "Welle 5 (Closure, pending):"
	@echo "  make build, make test-container, make ci, make runtime, make fullbuild"

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

gates: lint arch-check test test-safety coverage-gate simulator-lint simulator-test simulator-coverage-gate
	@echo "[gates] M1 mandatory gates green: lint, arch-check, test, test-safety, coverage-gate, simulator-{lint,test,coverage-gate}"

# --- Welle 3 (partially active) --------------------------------------------

SIMULATOR_DIR := simulators/bess-field-sim
SIMULATOR_MAKE := $(MAKE) -C $(SIMULATOR_DIR)

simulator-test:
	$(SIMULATOR_MAKE) test

simulator-lint:
	$(SIMULATOR_MAKE) lint

simulator-coverage-gate:
	$(SIMULATOR_MAKE) coverage-gate

test-integration:
	@echo "make test-integration: not active. Activated in Welle 3 (Config and Adapters)."
	@exit 2

# --- Welle 5 (pending) -----------------------------------------------------

build:
	@echo "make build: not active. Runtime image lands in Welle 5 (RM-M1-19)."
	@exit 2

test-container:
	@echo "make test-container: not active. Activated in Welle 5 (RM-M1-19)."
	@exit 2

ci:
	@echo "make ci: not active. CI-compatible aggregator activated in Welle 5."
	@exit 2

runtime:
	@echo "make runtime: not active. Runtime smoke activated in Welle 5."
	@exit 2

fullbuild:
	@echo "make fullbuild: not active. Activated in Welle 5."
	@exit 2
