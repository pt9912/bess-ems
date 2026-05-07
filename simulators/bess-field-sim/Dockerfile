# syntax=docker/dockerfile:1.7
#
# bess-field-sim — Go simulator (plan-RM-M1-simulator.md).
# Docker-only workflow; no host Go toolchain required. Standard
# multi-stage Go build with separate deps/build/runtime layers.
#
# Stages:
#   deps     — go module download (cache layer)
#   compile  — fast `go build` feedback
#   lint     — golangci-lint with the SOLID-leaning profile in .golangci.yml
#   test     — `go test ./...`
#   race     — `go test -race` on packages with real concurrency (CGO=1)
#   coverage — coverage profile + threshold gate (default 90%)
#   build    — production binary
#   runtime  — distroless image

FROM golang:1.26 AS deps
WORKDIR /src
ENV GOFLAGS="-mod=readonly" \
    GOMODCACHE=/go/pkg/mod \
    GOCACHE=/root/.cache/go-build
COPY go.mod ./
COPY go.su[m] ./
RUN mkdir -p "$GOMODCACHE" && go mod download

FROM deps AS compile
COPY . .
RUN CGO_ENABLED=0 go build -o /tmp/bess-field-sim ./cmd/bess-field-sim

FROM golangci/golangci-lint:v2.12-alpine AS lint
WORKDIR /src
COPY --from=deps /go/pkg/mod /go/pkg/mod
COPY . .
RUN golangci-lint run ./...

FROM deps AS test
COPY . .
RUN CGO_ENABLED=0 go test ./...

# Race-detector gate. CGO=1 is required by `-race`; the default golang
# image ships gcc so no extra install. Only this stage links libc —
# runtime/build stages stay CGO=0 and distroless-compatible.
FROM deps AS race
COPY . .
RUN CGO_ENABLED=1 go test -race -count=1 ./...

FROM deps AS coverage
ARG COVERAGE_THRESHOLD=90
ENV COVERAGE_THRESHOLD=${COVERAGE_THRESHOLD}
COPY . .
RUN mkdir -p /out && \
    COVERPKG=$(go list ./internal/... | tr '\n' ',' | sed 's/,$//') && \
    CGO_ENABLED=0 go test \
        -coverpkg="$COVERPKG" \
        -coverprofile=/out/coverage.out \
        -covermode=atomic \
        ./... && \
    go tool cover -func=/out/coverage.out | tee /out/coverage-func.txt && \
    go tool cover -html=/out/coverage.out -o /out/coverage.html && \
    bash scripts/coverage-gate.sh /out/coverage-func.txt "$COVERAGE_THRESHOLD"

FROM deps AS build
COPY . .
RUN CGO_ENABLED=0 go build -ldflags="-s -w" -o /out/bess-field-sim ./cmd/bess-field-sim

FROM gcr.io/distroless/static-debian12:nonroot AS runtime
COPY --from=build /out/bess-field-sim /usr/local/bin/bess-field-sim
# Pin the working directory so safepath's relative-only contract resolves
# operator-supplied fixture paths against a known root.
WORKDIR /
EXPOSE 5020 1883
ENTRYPOINT ["/usr/local/bin/bess-field-sim"]
