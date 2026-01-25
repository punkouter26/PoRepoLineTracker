// If an Aspire process is already running, exit early to avoid port binding conflicts when starting AppHost directly.
// Running both the Aspire orchestrator and a separately-started AppHost can result in "address already in use" socket errors.
if (System.Diagnostics.Process.GetProcessesByName("aspire").Length > 0)
{
    Console.Error.WriteLine("Detected an existing 'aspire' process. To debug AppHost separately, stop the Aspire process first or change the port configuration. Exiting to avoid socket binding conflicts.");
    Environment.Exit(0);
}

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

// Build and run the AppHost. If a port is already in use (e.g., Aspire is already running),
// catch the socket/address-in-use error and fail gracefully with a helpful message.
try
{
    var app = builder.Build();
    app.Run();
}
catch (Exception ex)
{
    // Unwrap AggregateException to find underlying SocketException if present
    Exception inner = ex;
    if (ex is System.AggregateException agg && agg.InnerException != null)
    {
        inner = agg.GetBaseException();
    }

    if (inner is System.Net.Sockets.SocketException sockEx &&
        sockEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
    {
        Console.Error.WriteLine("Error: Unable to start AppHost because a TCP port is already in use (AddressAlreadyInUse).\n" +
                                "This commonly happens when an Aspire AppHost is already running.\n" +
                                "Stop the existing Aspire/AppHost process or change the AppHost ports, then retry.");
        // Exit gracefully with a non-zero code so CI/automation knows it failed to start
        Environment.Exit(1);
    }

    // Re-throw if it's a different exception
    throw;
}
