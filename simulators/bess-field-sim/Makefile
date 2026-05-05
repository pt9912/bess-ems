# bess-field-sim Makefile (plan-RM-M1-simulator.md). Docker-only workflow,
# adapted from m-trace/apps/api/Makefile.

IMAGE := bess-field-sim
THRESHOLD ?= 90

NO_CACHE_FILTER_TEST     := --no-cache-filter test
NO_CACHE_FILTER_LINT     := --no-cache-filter lint
NO_CACHE_FILTER_COVERAGE := --no-cache-filter coverage

.PHONY: test lint build deps compile run coverage coverage-gate clean

test:
	docker build $(NO_CACHE_FILTER_TEST) --target test -t $(IMAGE):test .

lint:
	docker build $(NO_CACHE_FILTER_LINT) --target lint -t $(IMAGE):lint .

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
