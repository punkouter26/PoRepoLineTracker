using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;
using PoRepoLineTracker.Client;
using PoRepoLineTracker.Client.Services;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure HttpClient for API calls.
//
// Rule 4.2 — every request now goes through AntiforgeryHandler, which attaches the CSRF token to
// state-changing calls. Registering the client by name and resolving HttpClient from the factory
// keeps `@inject HttpClient Http` working unchanged across every page and component.
//
// "ApiRaw" is the same client WITHOUT the handler: AntiforgeryHandler fetches the token with it,
// and routing that fetch back through the handler would re-enter its own gate and deadlock.
builder.Services.AddHttpClient("ApiRaw", client =>
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress));

builder.Services.AddTransient<AntiforgeryHandler>();
builder.Services.AddHttpClient("Api", client =>
        client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<AntiforgeryHandler>();

builder.Services.AddScoped(sp =>
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("Api"));

builder.Services.AddScoped<UserPreferencesClient>();

// Add authentication services
builder.Services.AddScoped<ApiAuthenticationStateProvider>(sp =>
    new ApiAuthenticationStateProvider(sp.GetRequiredService<HttpClient>()));
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<ApiAuthenticationStateProvider>());
builder.Services.AddAuthorizationCore();

// Add Radzen services with Material3 theme
builder.Services.AddScoped<DialogService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TooltipService>();
builder.Services.AddScoped<ContextMenuService>();

await builder.Build().RunAsync();
