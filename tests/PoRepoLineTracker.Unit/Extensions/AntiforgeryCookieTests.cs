using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using PoRepoLineTracker.API.Extensions;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Guards the antiforgery cookie's naming/Secure pairing.
///
/// <para>Regression: the cookie was named <c>__Host-PoRepoLineTracker.Antiforgery</c> with
/// <c>SecurePolicy.SameAsRequest</c>. RFC 6265bis requires a <c>__Host-</c> cookie to carry
/// <c>Secure</c>, and browsers reject one that does not — so on the plain-HTTP launch profile the
/// Set-Cookie was emitted without Secure, silently dropped by the browser, and every
/// state-changing request failed antiforgery with "the required antiforgery cookie is not
/// present". Reads worked, so the app looked healthy while every write returned 400.</para>
///
/// <para>These assert the invariant rather than the specific names: a <c>__Host-</c> prefix is
/// only ever paired with a policy that guarantees Secure.</para>
///
/// <para>The choice is driven by <c>Security:RequireSecureCookies</c>, not by the environment
/// name. Keying it on <c>IsDevelopment()</c> looked equivalent and was not: the integration tier
/// runs under the environment name "Test" on plain http://localhost, so it was handed
/// <c>__Host-</c> + Secure and every state-changing test in that suite failed.</para>
/// </summary>
public class AntiforgeryCookieTests
{
    private const string HostPrefix = "__Host-";

    private static AntiforgeryOptions OptionsFor(string environmentName, bool? requireSecureCookies = null)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var builder = new ConfigurationBuilder();
        if (requireSecureCookies.HasValue)
        {
            builder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ConfigKeys.Security.RequireSecureCookies] = requireSecureCookies.Value ? "true" : "false"
            });
        }

        var services = new ServiceCollection();
        services.AddInfrastructure(builder.Build(), environment);

        return services.BuildServiceProvider()
            .GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void HostPrefixedCookie_AlwaysCarriesSecure(string environmentName)
    {
        var options = OptionsFor(environmentName);

        if (options.Cookie.Name?.StartsWith(HostPrefix, StringComparison.Ordinal) == true)
        {
            options.Cookie.SecurePolicy.Should().Be(
                CookieSecurePolicy.Always,
                "a __Host- cookie without Secure is rejected outright by the browser");
        }
    }

    [Fact]
    public void Development_UsesAnUnprefixedCookie_SoThePlainHttpProfileWorks()
    {
        var options = OptionsFor("Development");

        options.Cookie.Name.Should().NotStartWith(HostPrefix,
            "the http launch profile cannot set Secure, and the prefix would make the browser drop the cookie");
        options.Cookie.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Production_KeepsTheHostPrefixHardening()
    {
        var options = OptionsFor("Production");

        options.Cookie.Name.Should().StartWith(HostPrefix);
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    [Fact]
    public void Staging_KeepsTheHostPrefixHardening_BecauseTheDefaultIsSecure()
    {
        var options = OptionsFor("Staging");

        options.Cookie.Name.Should().StartWith(HostPrefix);
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    /// <summary>
    /// The escape hatch the integration tier uses. A host that genuinely serves plain HTTP under
    /// a non-Development environment name says so, and gets a cookie a plain-HTTP client can
    /// actually return. This is the case whose absence broke every write in that suite.
    /// </summary>
    [Fact]
    public void PlainHttpHost_OptsOutByConfiguration_WhateverItsEnvironmentIsCalled()
    {
        var options = OptionsFor("Test", requireSecureCookies: false);

        options.Cookie.Name.Should().NotStartWith(HostPrefix);
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.SameAsRequest);
    }

    /// <summary>Configuration wins over the environment default in the other direction too.</summary>
    [Fact]
    public void DevelopmentOverHttps_CanOptIn()
    {
        var options = OptionsFor("Development", requireSecureCookies: true);

        options.Cookie.Name.Should().StartWith(HostPrefix);
        options.Cookie.SecurePolicy.Should().Be(CookieSecurePolicy.Always);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void EveryEnvironment_KeepsTheDoubleSubmitContract(string environmentName)
    {
        var options = OptionsFor(environmentName);

        // The header half is what makes antiforgery work for a JSON API at all — the default
        // validator reads a form field, and no endpoint here posts a form.
        options.HeaderName.Should().Be("X-CSRF-TOKEN");
        options.Cookie.HttpOnly.Should().BeTrue();
        options.Cookie.SameSite.Should().Be(SameSiteMode.Strict);
    }
}
