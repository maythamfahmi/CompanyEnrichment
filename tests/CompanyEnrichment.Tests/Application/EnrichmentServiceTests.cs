using CompanyEnrichment.Application;
using CompanyEnrichment.Infrastructure.Statistics;
using Cvr.Client.Models;
using cvrapi.Infrastructure.Cvr;
using Microsoft.Extensions.Logging.Abstractions;
using StatBank.Client.Utils;

namespace CompanyEnrichment.Tests.Application;

public class EnrichmentServiceTests
{
    private sealed class FakeCvrClient(CvrCompany? company, Exception? throwException = null) : ICvrClient
    {
        public int CallCount { get; private set; }

        public Task<CvrCompany?> GetByCvrAsync(string cvr, CancellationToken cancellationToken)
        {
            CallCount++;
            if (throwException is not null)
            {
                throw throwException;
            }

            return Task.FromResult(company);
        }

        public Task<IReadOnlyList<CvrCompany>> SearchByNameAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeStatisticsClient(IReadOnlyList<StatBankObservation>? observations = null) : IStatisticsClient
    {
        public int CallCount { get; private set; }

        public Task<IReadOnlyList<StatBankObservation>> GetIndustryStatisticsAsync(
            string branche07Code, int year, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(observations ?? []);
        }
    }

    private static readonly CvrCompany SampleCompany =
        new("33000011", "Foo Bar ApS", "Testvej 1, 9999, Testby", "999999", "Test industry", 42);

    [Fact]
    public async Task EnrichByCvrAsync_ReturnsCombinedResult_WhenBothCallsSucceed()
    {
        var cvrClient = new FakeCvrClient(SampleCompany);
        var observations = new List<StatBankObservation> { new("ANSATTE", "42", 42m, false) };
        var statisticsClient = new FakeStatisticsClient(observations);
        var service = new EnrichmentService(cvrClient, statisticsClient, NullLogger<EnrichmentService>.Instance);

        var result = await service.EnrichByCvrAsync("33000011", 2023, CancellationToken.None);

        Assert.Equal(SampleCompany, result.Company);
        Assert.Same(observations, result.Statistics);
        Assert.Equal(1, cvrClient.CallCount);
        Assert.Equal(1, statisticsClient.CallCount);
    }

    [Fact]
    public async Task EnrichByCvrAsync_ThrowsCompanyNotFoundException_AndSkipsStatBankCall_WhenCvrLookupReturnsNull()
    {
        var cvrClient = new FakeCvrClient(company: null);
        var statisticsClient = new FakeStatisticsClient();
        var service = new EnrichmentService(cvrClient, statisticsClient, NullLogger<EnrichmentService>.Instance);

        await Assert.ThrowsAsync<CompanyNotFoundException>(
            () => service.EnrichByCvrAsync("33000011", 2023, CancellationToken.None));

        Assert.Equal(0, statisticsClient.CallCount);
    }

    [Fact]
    public async Task EnrichByCvrAsync_PropagatesExceptionAndSkipsStatBankCall_WhenCvrClientFails()
    {
        var cvrClient = new FakeCvrClient(company: null, throwException: new ExternalServiceException("cvr"));
        var statisticsClient = new FakeStatisticsClient();
        var service = new EnrichmentService(cvrClient, statisticsClient, NullLogger<EnrichmentService>.Instance);

        await Assert.ThrowsAsync<ExternalServiceException>(
            () => service.EnrichByCvrAsync("33000011", 2023, CancellationToken.None));

        Assert.Equal(0, statisticsClient.CallCount);
    }

    [Fact]
    public async Task EnrichByCvrAsync_ThrowsIndustryStatisticsUnavailableException_AndSkipsStatBankCall_WhenIndustryCodeIsMissing()
    {
        var companyWithoutIndustry = new CvrCompany(
            SampleCompany.Cvr,
            SampleCompany.Name,
            SampleCompany.Address,
            industryCode: "",
            SampleCompany.IndustryText,
            SampleCompany.EmployeeCount);
        var cvrClient = new FakeCvrClient(companyWithoutIndustry);
        var statisticsClient = new FakeStatisticsClient();
        var service = new EnrichmentService(cvrClient, statisticsClient, NullLogger<EnrichmentService>.Instance);

        await Assert.ThrowsAsync<IndustryStatisticsUnavailableException>(
            () => service.EnrichByCvrAsync("33000011", 2023, CancellationToken.None));

        Assert.Equal(0, statisticsClient.CallCount);
    }

    [Fact]
    public async Task EnrichByCvrAsync_ActivityTags_DoNotContainRawCvrNumber()
    {
        var activitySource = EnrichmentService.ActivitySource;
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source == activitySource,
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) =>
                System.Diagnostics.ActivitySamplingResult.AllData,
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);

        System.Diagnostics.Activity? captured = null;
        listener.ActivityStopped = activity => captured = activity;

        var cvrClient = new FakeCvrClient(SampleCompany);
        var statisticsClient = new FakeStatisticsClient();
        var service = new EnrichmentService(cvrClient, statisticsClient, NullLogger<EnrichmentService>.Instance);

        await service.EnrichByCvrAsync("33000011", 2023, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.DoesNotContain(captured!.Tags, tag => tag.Value is not null && tag.Value.Contains("33000011"));
    }
}
