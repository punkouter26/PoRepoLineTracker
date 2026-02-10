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

namespace PoRepoLineTracker.IntegrationTests;

/// <summary>
/// Verifies that the ExceptionHandlingMiddleware returns ProblemDetails JSON
/// when an unhandled exception is thrown in the pipeline.
/// Uses a custom factory that configures the mediator to throw on a specific query.
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
        // The factory configures GetLineCountsForRepository to throw
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linecounts");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task UnhandledException_Returns_ProblemJson_ContentType()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linecounts");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_Has_Title()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linecounts");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("title", out var title).Should().BeTrue();
        title.GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_Has_Status_500()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linecounts");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("status", out var status).Should().BeTrue();
        status.GetInt32().Should().Be(500);
    }

    [Fact]
    public async Task UnhandledException_ProblemDetails_Has_Detail()
    {
        var repoId = ExceptionMiddlewareFactory.ThrowingRepoId;
        var response = await _client.GetAsync($"/api/repositories/{repoId}/linecounts");
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("detail", out var detail).Should().BeTrue();
        detail.GetString().Should().NotBeNullOrEmpty();
    }
}

/// <summary>
/// Custom factory that overrides the MediatR mediator to throw an exception
/// for a specific repository ID, triggering the ExceptionHandlingMiddleware.
/// Inherits from <see cref="CustomWebApplicationFactory"/> to keep all other
/// service mocks and test auth active.
/// </summary>
public class ExceptionMiddlewareFactory : CustomWebApplicationFactory
{
    /// <summary>
    /// The repository ID that will cause the mediator to throw.
    /// </summary>
    public static readonly Guid ThrowingRepoId = new("DEADBEEF-DEAD-BEEF-DEAD-BEEFDEADBEEF");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            // Replace the mediator with one that throws for our specific query
            var mockMediator = Substitute.For<IMediator>();
            mockMediator
                .Send(Arg.Is<GetLineCountsForRepositoryQuery>(q => q.RepositoryId == ThrowingRepoId), Arg.Any<CancellationToken>())
                .ThrowsAsync(new InvalidOperationException("Deliberate test exception for middleware verification"));

            services.AddScoped(_ => mockMediator);
        });
    }
}

