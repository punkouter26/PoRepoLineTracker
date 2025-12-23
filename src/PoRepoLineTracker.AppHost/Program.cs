var builder = DistributedApplication.CreateBuilder(args);

// Add Azure Storage emulator for local development
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator
        .WithLifetime(ContainerLifetime.Persistent));

var tables = storage.AddTables("tables");

// Add the API project with Blazor WASM client
var api = builder.AddProject<Projects.PoRepoLineTracker_Api>("api")
    .WithReference(tables)
    .WaitFor(tables)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health/live");

builder.Build().Run();
