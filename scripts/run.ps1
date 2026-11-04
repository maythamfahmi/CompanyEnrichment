$env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://localhost:4317'
dotnet run --project src/CompanyEnrichment.Api --launch-profile http