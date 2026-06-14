using Xunit;

// Integration tests each spin up a WebApplicationFactory<Program> host. Running them in
// parallel makes concurrent host builds race on the static HostFactoryResolver listener,
// intermittently throwing "The entry point exited without ever building an IHost"
// (regression R2 — a different test lost the race on each run). Serializing the assembly
// makes the tier deterministic. The classes already share a single factory via
// [Collection("Integration Tests")] for speed; this also covers the out-of-collection
// ExceptionMiddlewareTests, which uses its own factory subclass.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
