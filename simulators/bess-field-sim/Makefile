# bess-field-sim Makefile (plan-RM-M1-simulator.md). Docker-only workflow,
# adapted from m-trace/apps/api/Makefile.

IMAGE := bess-field-sim
THRESHOLD ?= 90

NO_CACHE_FILTER_TEST     := --no-cache-filter test
NO_CACHE_FILTER_LINT     := --no-cache-filter lint
NO_CACHE_FILTER_COVERAGE := --no-cache-filter coverage

.DEFAULT_GOAL := help

.PHONY: help test race lint gates build deps compile run coverage coverage-gate clean

help:
	@printf '%s\n' \
		'bess-field-sim Makefile' \
		'' \
		'Targets:' \
		'  make test           Run Go unit tests in Docker' \
		'  make lint           Run golangci-lint in Docker' \
		'  make race           Run race-detector tests for concurrent packages' \
		'  make gates          Run simulator test, lint, and race gates' \
		'  make coverage-gate  Run Go coverage gate (threshold via THRESHOLD, default 90)' \
		'  make coverage       Alias for coverage-gate' \
		'  make compile        Build the simulator binary stage' \
		'  make build          Build the runtime image' \
		'  make deps           Rebuild dependency cache stage' \
		'  make clean          Remove simulator Docker images' \
		'' \
		'Variables:' \
		'  IMAGE=$(IMAGE)' \
		'  THRESHOLD=$(THRESHOLD)'

test:
	docker build $(NO_CACHE_FILTER_TEST) --target test -t $(IMAGE):test .

race:
	docker build --no-cache-filter race --target race -t $(IMAGE):race .

lint:
	docker build $(NO_CACHE_FILTER_LINT) --target lint -t $(IMAGE):lint .

gates: test lint race
	@echo "[bess-field-sim] gates green: test, lint, race"

coverage-gate:
	docker build $(NO_CACHE_FILTER_COVERAGE) \
		--target coverage \
		--build-arg COVERAGE_THRESHOLD=$(THRESHOLD) \
		-t $(IMAGE):coverage .

coverage: coverage-gate

build:
	docker build --target runtime -t $(IMAGE):latest .

deps:
	docker build --no-cache --target deps -t $(IMAGE):deps .

compile:
	docker build --target compile -t $(IMAGE):compile .

clean:
	-docker rmi $(IMAGE):latest $(IMAGE):test $(IMAGE):lint $(IMAGE):compile $(IMAGE):deps $(IMAGE):coverage
