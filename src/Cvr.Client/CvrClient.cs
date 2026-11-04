using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CompanyEnrichment.Application;
using CompanyEnrichment.Observability;
using Cvr.Client.Models;
using Microsoft.Extensions.Logging;

namespace cvrapi.Infrastructure.Cvr;

public interface ICvrClient
{
    Task<CvrCompany?> GetByCvrAsync(string cvr, CancellationToken cancellationToken);

    Task<IReadOnlyList<CvrCompany>> SearchByNameAsync(string name, CancellationToken cancellationToken);
}

/// <summary>Typed HttpClient for apicvr.dk. Translates transport/parse failures into domain exceptions.</summary>
public sealed partial class CvrClient(HttpClient httpClient, ILogger<CvrClient> logger, DependencyMetrics metrics) : ICvrClient
{
    private const string ProviderName = "cvr";

    [GeneratedRegex(@"^\d{8}$")]
    private static partial Regex CvrNumberPattern();

    public async Task<CvrCompany?> GetByCvrAsync(string cvr, CancellationToken cancellationToken)
    {
        if (!CvrNumberPattern().IsMatch(cvr))
        {
            throw new InvalidCvrFormatException(cvr);
        }

        var scope = metrics.StartCall(ProviderName);
        try
        {
            using var response = await SendAsync($"{cvr}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                scope.NotFound();
                return null;
            }

            EnsureSuccess(response);

            var dto = await DeserializeAsync<CvrCompanyResponse>(response, cancellationToken);
            var company = dto is null || string.IsNullOrWhiteSpace(dto.Name) ? null : Map(dto, cvr);

            if (company is null)
            {
                scope.NotFound();
            }
            else
            {
                scope.Success();
            }

            return company;
        }
        catch (ExternalTimeoutException)
        {
            scope.Timeout();
            throw;
        }
        catch (EnrichmentException)
        {
            scope.Error();
            throw;
        }
    }

    public async Task<IReadOnlyList<CvrCompany>> SearchByNameAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidSearchNameException(name);
        }

        var scope = metrics.StartCall(ProviderName);
        try
        {
            using var response = await SendAsync($"search/company/{Uri.EscapeDataString(name)}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                scope.NotFound();
                return [];
            }

            EnsureSuccess(response);

            var results = await DeserializeAsync<List<CvrCompanyResponse>>(response, cancellationToken) ?? [];

            var mapped = results
                .Where(r => r.Vat.HasValue && !string.IsNullOrWhiteSpace(r.Name))
                .Select(r => Map(r, r.Vat!.Value.ToString()))
                .ToList();

            if (mapped.Count == 0)
            {
                scope.NotFound();
            }
            else
            {
                scope.Success();
            }

            return mapped;
        }
        catch (ExternalTimeoutException)
        {
            scope.Timeout();
            throw;
        }
        catch (EnrichmentException)
        {
            scope.Error();
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.GetAsync(relativePath, cancellationToken);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ExternalTimeoutException(ProviderName, ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceException(ProviderName, ex);
        }
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        logger.LogWarning("CVR provider returned non-success status {StatusCode}", response.StatusCode);

        if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
        {
            throw new ExternalServiceException(ProviderName);
        }

        throw new InvalidExternalResponseException(ProviderName, $"unexpected status code {(int)response.StatusCode}");
    }

    private async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidExternalResponseException(ProviderName, "unable to parse JSON body", ex);
        }
    }

    private static CvrCompany Map(CvrCompanyResponse dto, string cvrFallback)
    {
        var address = string.Join(", ", new[] { dto.Address, dto.ZipCode?.ToString(), dto.City }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        return new CvrCompany(
            cvr: dto.Vat?.ToString() ?? cvrFallback,
            name: dto.Name!,
            address: string.IsNullOrWhiteSpace(address) ? null : address,
            industryCode: dto.IndustryCode?.ToString() ?? string.Empty,
            industryText: dto.IndustryText,
            employeeCount: dto.Employees);
    }
}
