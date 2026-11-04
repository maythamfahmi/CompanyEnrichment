# CompanyEnrichment

A company enrichment service that combines data from two external APIs:

- **CVR API** (`apicvr.dk`) — company master data (name, address, industry code).
- **StatBank API** (`api.statbank.dk`, table `ERHV1`) — industry statistics, keyed by the
  industry code (`BRANCHE07`) returned by CVR.

`GET /api/companies/{cvr}/enrichment?year=YYYY` orchestrates both calls sequentially
(StatBank depends on the industry code from CVR) and returns a combined result.

## Projects

| Project | Responsibility |
|---|---|
| `src/Cvr.Client` | Reusable CVR HTTP client library. |
| `src/StatBank.Client` | Reusable StatBank HTTP client and CSV parser. |
| `src/CompanyEnrichment.Shared` | Shared exception types and `DependencyMetrics`, with no dependency on the API host. |
| `src/CompanyEnrichment.Api` | ASP.NET Core API, orchestration (`EnrichmentService`), controllers, and OpenTelemetry/health-check/DI configuration. |
| `tests/CompanyEnrichment.Tests` | Orchestration unit tests. |
| `tests/CvrClient.Tests` and `tests/StatBankClient.Tests` | HTTP client and parser unit tests. |
| `tests/CompanyEnrichment.IntegrationTests` | Endpoint tests using the live CVR and StatBank APIs. |
| `tests/CompanyEnrichment.TestSupport` | Shared test helpers. |

## Observability architecture

Responsibility is split so the reusable clients never depend on a monitoring vendor:

- **`Cvr.Client`** and **`StatBank.Client`** use
  only `ILogger<T>` (structured logging) and the shared `DependencyMetrics` helper
  (`System.Diagnostics.Metrics` — no OpenTelemetry package reference). They never configure
  providers or exporters.
- **`EnrichmentService`** (Application layer) uses `ILogger<EnrichmentService>` for orchestration
  events and a dedicated `ActivitySource("CompanyEnrichment.Orchestration")` for a custom span
  around the end-to-end enrichment flow.
- **The API project (`Program.cs`)** is the *only* place that configures OpenTelemetry
  (logging, tracing, metrics, exporters) and health checks. This keeps the client libraries
  reusable in any other host application.

## Health checks

| Endpoint | Purpose |
|---|---|
| `GET /health/live` | Liveness — process is up. Always healthy once the app has started; does **not** call any external dependency, so an upstream outage can never cause the process to be killed/restarted. |
| `GET /health/ready` | Readiness — app is ready to serve traffic. Currently backed by the same lightweight in-process check as liveness (no synchronous external calls), so readiness flips quickly on deploy/restart. |

A third check, `cvr-dependency` (tag `external`), probes `apicvr.dk` and is intentionally
**not** mapped to `/health/live` or `/health/ready`. It exists so the CVR dependency's health can
be inspected independently (e.g. via a custom `/health` endpoint with no predicate, or a
dashboard), without ever gating the app's own liveness/readiness.

## Logging strategy

- `Microsoft.Extensions.Logging` / `ILogger<T>` everywhere — no Serilog, since the project had no
  prior Serilog configuration to justify the extra dependency.
- All log messages use structured, named placeholders (e.g. `logger.LogWarning("CVR provider
  returned non-success status {StatusCode}", response.StatusCode)`), never string interpolation.
- Log levels:
  - `Debug`/`Information` — orchestration milestones (enrichment started/completed, observation
    counts).
  - `Warning` — recoverable/expected downstream conditions (non-success HTTP status, StatBank
    "not found"/invalid-selection responses).
  - `Error` — genuine failures surfaced to the API caller (timeouts, upstream 5xx, malformed
    responses), logged **once** at the controller boundary — the client layers log `Warning` for
    the raw HTTP symptom but do not re-log the translated domain exception, avoiding duplicate
    exception logging across layers.
- Never logged: API keys/auth headers, full request/response bodies, CVR numbers, or company
  names. Error response bodies are truncated (500 chars) and logged only at `Warning`/`Information`
  for diagnosing malformed upstream payloads — never at a level that would be routinely scraped
  into alerting without context.
- OpenTelemetry's logging provider (`builder.Logging.AddOpenTelemetry(...)`) forwards all
  `ILogger` output through the same pipeline as traces/metrics so log records carry
  trace/span correlation IDs automatically.

## Metrics

Three logical components are instrumented:

| Component | Source |
|---|---|
| Company Enrichment API (incoming requests) | `OpenTelemetry.Instrumentation.AspNetCore` (`http.server.request.duration` histogram + built-in request counters) |
| External CVR API (outgoing calls) | `OpenTelemetry.Instrumentation.Http` (`http.client.request.duration`) **and** the custom `dependency_requests_total` / `dependency_request_duration_seconds` meter, tagged `dependency=cvr` |
| External StatBank API (outgoing calls) | Same as above, tagged `dependency=statistics` |

Custom meter (`CompanyEnrichment.Dependencies`, see `src/CompanyEnrichment.Shared/DependencyMetrics.cs`):

- `dependency_requests_total` (counter) — one increment per logical downstream call, tagged with
  `dependency` (`cvr` | `statistics`) and `outcome` (`success` | `not_found` | `error` | `timeout`).
- `dependency_request_duration_seconds` (histogram) — wall-clock duration of each logical call,
  same tags.

**How CVR and StatBank are distinguished:** the single low-cardinality `dependency` tag
(`cvr` / `statistics`). No CVR numbers, company names, or raw URLs are ever used as tags/labels
(HTTP client instrumentation's `http.route`/`server.address` tags are similarly low-cardinality
host/path templates, not raw request URLs with query strings).

**How errors are counted:**
- A call is `error` if the client translates it into `ExternalServiceException`/
  `InvalidExternalResponseException` (HTTP 5xx/429, malformed body, unexpected status code).
- A call is `timeout` if it throws `TaskCanceledException` that was **not** caused by the
  caller's own `CancellationToken` (i.e. a genuine HTTP timeout, not a client-initiated
  cancellation). Caller-cancelled requests are not counted as errors or timeouts — they simply
  don't complete a scope.
- A call is `not_found` (not an error) when the provider legitimately has no data for the
  request (CVR 404, StatBank 404/204/400-for-unsupported-selection).
- Request **error rate** per dependency = `sum(dependency_requests_total{outcome="error" or
  outcome="timeout"}) / sum(dependency_requests_total)` over a window.

**How p95 latency is calculated:** both the ASP.NET Core/HttpClient instrumentation histograms
and the custom `dependency_request_duration_seconds` histogram are exported with their bucket
boundaries; p95 is computed at query time by the backend, e.g. in PromQL:

```promql
histogram_quantile(0.95, sum(rate(dependency_request_duration_seconds_bucket[5m])) by (le, dependency))
```

or the OTLP-native equivalent in your APM of choice (e.g. Grafana/Tempo, Honeycomb, Azure Monitor).

## Distributed tracing

A single request produces one trace:

```
Incoming HTTP request (ASP.NET Core instrumentation)
  └─ EnrichCompany (custom span, ActivitySource "CompanyEnrichment.Orchestration")
       ├─ Outgoing HTTP call to apicvr.dk      (HttpClient instrumentation)
       └─ Outgoing HTTP call to api.statbank.dk (HttpClient instrumentation)
```

- ASP.NET Core instrumentation instruments the incoming request; HttpClient instrumentation
  instruments both outgoing calls — parent/child relationships and trace context (W3C
  `traceparent`) are preserved automatically via `Activity`/`ActivityContext` propagation.
  No manual trace-context plumbing is required.
- The `EnrichCompany` custom span adds `enrichment.year` and `enrichment.industry_code` tags
  (never CVR numbers or company names) to make it easy to see *which* industry/year a given
  trace was processing without exposing PII/business-sensitive values.
- Failures are recorded on the relevant span by the OpenTelemetry instrumentation itself
  (HTTP status code / exception) — no duplicate manual span events are added for the same
  failure.

## Configuring telemetry exporters

Exporters are chosen based on the standard `OTEL_EXPORTER_OTLP_ENDPOINT` configuration value
(settable via `appsettings.json`, environment variable, or any other configuration provider):

- **Unset / empty** (default, e.g. local `dotnet run`) → logs/traces/metrics are written to the
  **console exporter**, so you can see output with zero extra infrastructure.
- **Set** (e.g. `http://localhost:4317` for the bundled collector, or any OTLP-compatible
  backend/Azure Monitor/Grafana Cloud/etc.) → all three signals are exported via **OTLP**.

```jsonc
// appsettings.Development.json
{
  "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317"
}
```

## Getting started

Install Docker Desktop (with Linux containers) and Git. For local builds and tests,
install the .NET 10 SDK. Manual registry operations also use the GitHub CLI (`gh`).
Run the commands below from the repository root.

### Start the API and observability stack

```powershell
git clone https://github.com/maythamfahmi/CompanyEnrichment.git
cd CompanyEnrichment
docker compose --profile observability up --build
```

This builds the API and starts the OpenTelemetry Collector, Prometheus, Jaeger,
and Grafana. Add `-d` to run in the background. Send an example API request to
populate metrics and traces, then explore them using the links below.

### Local endpoints

| Service | URL | Purpose |
|---|---|---|
| API documentation (Development only) | http://localhost:8080/scalar | Explore and try API requests. See the Development configuration below. |
| Readiness | http://localhost:8080/health/ready | Check whether the API is ready to serve requests. |
| Liveness | http://localhost:8080/health/live | Check whether the API process is running. |
| Example enrichment request | http://localhost:8080/api/companies/16500836/enrichment?year=2024 | Combine CVR company data with industry statistics. Requires access to both external APIs. |
| Metrics stream | http://localhost:8889/metrics | Inspect the collector's Prometheus metrics export. |
| Prometheus | http://localhost:9090 | Query and graph metrics. |
| Jaeger | http://localhost:16686 | Explore distributed traces; select the `CompanyEnrichment` service. |
| Grafana | http://localhost:3000/dashboards | Open the provisioned dashboards. Initial login: `admin` / `admin`. |

Monitoring links require the `observability` profile. The bundled Grafana credentials
are for local development.

Port 8080 serves **HTTP**. Scalar and the OpenAPI document are mapped only when
`ASPNETCORE_ENVIRONMENT=Development`; the default Compose environment does not enable
them. To enable API documentation locally, create `compose.override.yaml` in the
repository root:

```yaml
services:
  companyenrichment:
    environment:
      ASPNETCORE_ENVIRONMENT: Development
```

Run the same Compose command again to apply the override, then open
http://localhost:8080/scalar.

### Stop and clean up

Stop and remove the stack's containers and network, keeping stored metrics and dashboards:

```powershell
docker compose --profile observability down
```

To also delete the Prometheus and Grafana data volumes for a fresh start:

```powershell
docker compose --profile observability down --volumes
```

### Build, test, and run with .NET

```powershell
dotnet restore
dotnet build --no-restore
dotnet test
```

Both Debug (the default) and Release builds include `CompanyEnrichment.Shared`,
which supplies the clients' shared exceptions and dependency metrics.
`global.json` selects Microsoft.Testing.Platform so `dotnet test` works with the
installed xUnit version on .NET 10. Integration tests call the live CVR and
StatBank APIs and require network access.

Run one test project:

```powershell
dotnet test --project tests/CvrClient.Tests/CvrClient.Tests.csproj
```

Run the API without Docker:

```powershell
dotnet run --project src/CompanyEnrichment.Api --launch-profile http
```

This launch profile enables Development and listens on http://localhost:5195;
API documentation is at http://localhost:5195/scalar. With no OTLP endpoint
configured, telemetry is written to the console. To send it to the Compose
collector from PowerShell:

```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
dotnet run --project src/CompanyEnrichment.Api --launch-profile http
```

## CI and container publishing

The workflow in [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) restores
and builds the solution in Release mode, runs all unit and integration tests,
and then builds the Docker image. CI uses xUnit's executable runner to generate
TRX reports, uploaded as the `test-results` artifact even when tests fail.
Failures block image building and publication, including failures caused by
unavailable external APIs.

Pull requests build the image without publishing. Pushes to `main` or `master`,
version tags (`v*`), and manual runs trigger the workflow. Successful runs on the
repository's default branch or a version tag publish to
`ghcr.io/maythamfahmi/companyenrichment`:

| Tag | When it is published |
|---|---|
| `latest` | Default-branch builds. |
| `sha-<full-commit-sha>` | Every published build. |
| `v1.0.0` (example) | A build triggered by that Git tag. |

The image name is derived from the GitHub repository and converted to lowercase.
If the repository is renamed or forked, use its corresponding image path.
Publishing uses the automatic `GITHUB_TOKEN` with `packages: write`; no extra
registry credentials are needed in CI. If the default branch is neither `main`
nor `master`, update the workflow's push branch filter. Package visibility is
managed separately in GHCR.

### Publish an image from your local environment

The pipeline handles publishing automatically. To publish manually, first run:

```powershell
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

Continue only if all commands succeed. Sign in with `gh auth login --hostname github.com`
if needed, and use an account with write access to the package:

```powershell
gh auth refresh --hostname github.com -s write:packages
gh auth token --hostname github.com | docker login ghcr.io -u maythamfahmi --password-stdin
docker build -t ghcr.io/maythamfahmi/companyenrichment:latest .
docker push ghcr.io/maythamfahmi/companyenrichment:latest
```

Check that each command succeeds before continuing. This replaces the registry's
`latest` tag with your local build. For a versioned image, replace `latest` with
your chosen tag in both the build and push commands.

The login commands use the token stored by GitHub CLI. See the
[GitHub CLI authentication documentation](https://cli.github.com/manual/gh_auth_refresh)
and [GHCR authentication documentation](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry#authenticating-to-the-container-registry)
for token scopes and personal access token (classic) authentication if needed.

### Pull and run an image locally

For a private package, authenticate with an account that has read access:

```powershell
gh auth refresh --hostname github.com -s read:packages
gh auth token --hostname github.com | docker login ghcr.io -u maythamfahmi --password-stdin
```

Use your own GitHub username in the login command when using another account.
Public images can be pulled without authentication.

```powershell
docker pull ghcr.io/maythamfahmi/companyenrichment:latest
docker run --rm -p 8080:8080 ghcr.io/maythamfahmi/companyenrichment:latest
```

Stop any Compose API already using port 8080 before running this container.
This starts only the API; telemetry goes to the console. Use the Compose stack
above for monitoring. To enable Scalar for this standalone local container:

```powershell
docker run --rm -p 8080:8080 -e ASPNETCORE_ENVIRONMENT=Development ghcr.io/maythamfahmi/companyenrichment:latest
```

Replace `latest` with a published version or `sha-<full-commit-sha>` tag to run a
specific build. Press Ctrl+C to stop the container; `--rm` removes it after exit.

## Tradeoffs & potential production improvements

- **No retry/circuit-breaker policy** is configured on the typed `HttpClient`s beyond a total
  timeout; adding `Microsoft.Extensions.Http.Resilience` would improve resilience but was left out
  to keep scope focused on observability.
- **Readiness** does not probe CVR/StatBank synchronously per the assignment's guidance; a
  production system might run a periodic background health check (e.g. every 30s) and cache the
  result, exposed via a separate `/health/dependencies` endpoint, rather than leaving the CVR
  dependency check entirely unwired.
- **Sampling**: tracing currently samples everything (`AlwaysOnSampler`, the OTel default). A
  production deployment would configure head-based or tail-based sampling to control cost/volume.
- **Exemplars**: linking histogram buckets to example trace IDs (exemplars) would make it easier
  to jump from a p95 latency spike straight to a representative trace; not configured here for
  simplicity.
- **Correlation IDs**: W3C trace context (`traceparent`) is used for correlation; for clients
  that don't propagate it, a lightweight `X-Correlation-Id` middleware could be added, but was
  omitted since it duplicates `Activity.TraceId` for this synchronous, non-queued workload.
