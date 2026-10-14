using Azure.Data.Tables;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The server PAT stands in for a token-less user only where fake auth exists (Development and
/// Test). Anywhere else it is a confused deputy: any principal whose row lacks a token would list
/// and clone the PAT owner's private repositories. "Staging" is the case an "anything but
/// Production" test got wrong.
/// </summary>
public sealed class UserServiceTests
{
    [Theory]
    [InlineData("Development", "ghp_server_pat")]
    [InlineData("Test", "ghp_server_pat")]
    [InlineData("Production", null)]
    [InlineData("Staging", null)]
    public async Task ResolveGitHubToken_FallsBackToServerPat_OnlyInDevelopmentAndTest(string environmentName, string? expected)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GitHub:PAT"] = "ghp_server_pat" })
            .Build();
        var sut = new UserService(
            new TableServiceClient("UseDevelopmentStorage=true"), configuration, environment,
            Substitute.For<IDataProtectionProvider>(), Substitute.For<ILogger<UserService>>());

        // UserId.Empty is a user-less legacy row: nothing to look up, so no storage is touched.
        (await sut.ResolveGitHubTokenAsync(UserId.Empty)).Should().Be(expected);
    }

    [Theory]
    [InlineData("Development", "ghp_user", "ghp_user")]
    [InlineData("Production", "ghp_user", "ghp_user")]
    [InlineData("Development", "", "ghp_server_pat")]
    [InlineData("Test", null, "ghp_server_pat")]
    [InlineData("Production", "", null)]
    [InlineData("Staging", null, null)]
    public void ChooseGitHubToken_UserTokenWins_TokenlessUserGetsPatOnlyInDevAndTest(string environmentName, string? userToken, string? expected)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        UserService.ChooseGitHubToken(userToken, environment, "ghp_server_pat").Should().Be(expected);
    }
}
