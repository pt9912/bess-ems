# bess-ems Helm Chart

RM-M6-03 introduces this chart as the Kubernetes path next to the
existing Compose reference. Compose remains the runtime smoke until a
cluster-level Kubernetes gate is activated.

## Topology

Default:

```bash
helm template bess-ems deploy/helm/bess-ems
```

`topology.mode=shared` renders one bess-ems Deployment and one Service.
The asset config is mounted as `assets.json`, matching ADR 0007's shared
Worker default.

`replicaCount` is fixed at `1`. bess-ems does not yet provide leader
election or distributed per-asset locking, so rendering more than one
replica is rejected to avoid duplicate control cycles.

Worker-pro-Asset:

```bash
helm template bess-ems deploy/helm/bess-ems --set topology.mode=workerPerAsset
```

This renders one Deployment and one Service per asset. Each pod receives
a single `asset.json`, so API traffic must target the per-asset Service
instead of a shared load-balanced Service.

## Secrets And Volumes

The chart creates one runtime Secret containing:

- `api-token`
- `postgres-password`
- `persistence-connection-string` when `postgres.enabled=true`

Postgres uses a StatefulSet with a PVC. Asset configuration is rendered
as a ConfigMap and mounted read-only at `/etc/bess-ems/assets`.

Production deployments should override the default token and database
password through environment-specific values management. The chart keeps
the default values development-friendly so `helm template` and local
smokes remain reproducible.

## Probes

The bess-ems container exposes HTTP on port `8080`.

- Readiness: `GET /health`
- Liveness: `GET /health`
- Metrics: `GET /metrics`

Postgres uses `pg_isready`. Mosquitto, when enabled, uses a TCP probe on
the MQTT port. The optimization-core test sidecar, when enabled, uses
`GET /healthz` on its health port.

## Optional Sidecars

Mosquitto:

```bash
helm template bess-ems deploy/helm/bess-ems \
  --set topology.mode=workerPerAsset \
  --set mqtt.enabled=true
```

MQTT is intended for single-asset or worker-pro-asset topologies until
per-asset adapter bindings exist. In worker-pro-asset mode the chart
suffixes `Bess__MqttClientId` with the `asset_id` to avoid MQTT session
collisions.

Optimization-core:

```bash
helm template bess-ems deploy/helm/bess-ems \
  --set optimizationCore.enabled=true
```

The default rendered sidecar path uses the test sidecar image and
`RuntimeProfile=Development`; it is a topology smoke, not a production
solver image. Production cross-host optimization-core deployments should
use `optimizationCore.externalEndpoint` with `https://` and mTLS, or a
future same-pod UDS sidecar pattern.

For HTTPS/mTLS external endpoints, provide Kubernetes Secrets containing
the client certificate and trusted server certificates:

```bash
helm template bess-ems deploy/helm/bess-ems \
  --set optimizationCore.externalEndpoint=https://optimization-core.example:8443 \
  --set optimizationCore.transport.mtls.enabled=true \
  --set optimizationCore.transport.mtls.clientCertificateSecret=bess-ems-optimization-core-client \
  --set optimizationCore.transport.mtls.trustedServerCertificatesSecret=bess-ems-optimization-core-ca
```

The client certificate Secret is mounted at
`/etc/bess-ems/optimization-core/client` and the trusted server
certificates Secret at `/etc/bess-ems/optimization-core/server`. The
chart sets `Bess__OptimizationCoreClientCertificatePath` to the mounted
`tls.crt` file and `Bess__OptimizationCoreTrustedServerCertificatesPath`
to the server certificate directory. Rendering is rejected when mTLS is
enabled without both Secret names or without an `https://`
`optimizationCore.externalEndpoint`.

UDS values are visible under `optimizationCore.transport.uds`. Rendering
a separate optimization-core Deployment with UDS is rejected because
separate pods cannot share an `emptyDir` Unix socket. Use
`externalEndpoint` for an already-managed UDS endpoint, or add a future
same-pod sidecar slice.

## Rollout And Restart

Asset ConfigMap changes are included in a pod-template checksum
annotation, so Helm upgrades roll the bess-ems pods when asset config
changes.

Manual restart examples:

```bash
kubectl rollout restart deployment/<release>-bess-ems
kubectl rollout status deployment/<release>-bess-ems
```

For worker-pro-asset, restart the per-asset Deployment name rendered by
Helm, for example `<release>-bess-ems-bess-a`.

## Gate

```bash
make helm-lint
```

The target runs `helm lint` and renders:

- shared Worker default
- worker-pro-asset
- optimization-core enabled
- optimization-core external HTTPS/mTLS
- MQTT enabled in worker-pro-asset mode

It is intentionally not part of `make ci` yet. A later RM-M6-03 pass must
define a reproducible kind/k3d or cluster smoke before Kubernetes joins
the mandatory CI gate set.
