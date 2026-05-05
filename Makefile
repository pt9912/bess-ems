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
	@echo "  make lint        Build with -warnaserror in the lint stage"
	@echo "  make arch-check  Boundary tests (Dependency Rule + tabus)"
	@echo ""
	@echo "Welle 2 (Domain & Control, partially active):"
	@echo "  make test        Domain unit tests (RM-M1-02/04/05/06)"
	@echo "  make test-safety pending (RM-M1-07 fallback paths)"
	@echo "  make coverage-gate pending"
	@echo ""
	@echo "Aggregated:"
	@echo "  make gates       Aggregated mandatory M1 gates for the current wave"
	@echo ""
	@echo "Welle 3 (Adapters, pending):"
	@echo "  make test-integration"
	@echo ""
	@echo "Welle 5 (Closure, pending):"
	@echo "  make build, make test-container, make ci, make runtime, make fullbuild"

# --- Welle 1 (active) ------------------------------------------------------

lint:
	$(DOCKER_BUILD) --target lint -t $(IMAGE_PREFIX)-lint:latest

arch-check:
	$(DOCKER_BUILD) --target arch-check -t $(IMAGE_PREFIX)-arch-check:latest

# --- Welle 2 (active: test) ------------------------------------------------

test:
	$(DOCKER_BUILD) --target test -t $(IMAGE_PREFIX)-test:latest

# --- Aggregated gates ------------------------------------------------------

gates: lint arch-check test
	@echo "[gates] M1 mandatory gates green: lint, arch-check, test"

# --- Welle 2 (pending) -----------------------------------------------------

test-safety:
	@echo "make test-safety: not active. Activated with RM-M1-07 (control cycle and fallback)."
	@exit 2

coverage-gate:
	@echo "make coverage-gate: not active. Activated incrementally from Welle 2 onwards."
	@exit 2

# --- Welle 3 (pending) -----------------------------------------------------

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
