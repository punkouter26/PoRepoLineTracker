using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PoRepoLineTracker.Application.Features.Repositories.Queries;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Shared.Models.Dtos;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Verifies that unhandled exceptions in the repository line-history pipeline
/// return ProblemDetails JSON (via the endpoint's own try/catch which calls Results.Problem).
/// Uses a custom factory that:
///   1. Mocks IRepositoryDataService so ThrowingRepoId is owned by the test user (passes IDOR check).
///   2. Mocks IMediator so the query throws, triggering Results.Problem(500).
/// </summary>
public class ExceptionMiddlewareTests : IClassFixture<ExceptionMiddlewareFactory>
{
    private readonly HttpClient _client;

    public ExceptionMiddlewareTests(ExceptionMiddlewareFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task UnhandledException_Returns_500_ProblemDetails()
    {
        // The factory configures GetLineCountHistoryQuery to throw
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linehistory/365");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UnhandledException_Returns_ProblemJson_ContentType()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linehistory/365");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_Has_Title()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linehistory/365");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("title", out var title).Should().BeTrue();
        title.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_Has_Status_500()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linehistory/365");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetInt32().Should().Be(500);
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_Has_Detail()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linehistory/365");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.GetString().Should().NotBeNullOrEmpty();
    }
}

/// <summary>
/// Custom factory that:
///   1. Overrides IRepositoryDataService so ThrowingRepoId is "found" and owned by the test user,
///      allowing the endpoint to proceed past the repo-existence / IDOR checks.
///   2. Overrides IMediator so the GetLineCountHistoryQuery throws for ThrowingRepoId,
///      exercising the endpoint's try/catch which returns Results.Problem (500).
/// Inherits from <see cref="CustomWebApplicationFactory"/> to keep all other service mocks
/// and test authentication active.
/// </summary>
public class ExceptionMiddlewareFactory : CustomWebApplicationFactory
{
    /// <summary>The repository ID that causes the mediator to throw.</summary>
    public static readonly RepositoryId ThrowingRepoId = new(new Guid("DEADBEEF-DEAD-BEEF-DEAD-BEEFDEADBEEF"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Mock IRepositoryDataService: return a fake repo owned by the test user for ThrowingRepoId
            // so the endpoint passes its repo-existence and IDOR ownership checks.
            var mockRepoDataService = Substitute.For<IRepositoryDataService>();
            var fakeRepo = new GitHubRepository
            {
                Id = ThrowingRepoId,
                UserId = UserId.Parse(TestAuthHandler.TestUserId),
                Owner = "testowner",
                Name = "testrepo",
                CloneUrl = "https://github.com/testowner/testrepo.git"
            };
            mockRepoDataService
                .GetRepositoryByIdAsync(ThrowingRepoId)
                .Returns(Task.FromResult<GitHubRepository?>(fakeRepo));

            services.AddScoped<IRepositoryDataService>(_ => mockRepoDataService);

            // Mock IMediator: throw for GetLineCountHistoryQuery so Results.Problem(500) is returned.
            var mockMediator = Substitute.For<IMediator>();
            mockMediator
                .Send(Arg.Is<GetLineCountHistoryQuery>(q => q.RepositoryId == ThrowingRepoId), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Deliberate test exception for middleware verification"));

            services.AddScoped(_ => mockMediator);
        });
    }
}

