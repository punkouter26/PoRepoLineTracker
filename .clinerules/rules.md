Section 1: Core Persona & Guiding Principles
Modern Workflow: Adhere to modern .NET development practices, frameworks, and tooling. Use Blazor Webassembly hosted in a .NET core project and always run just the API project when asked to run the app
CLI First: Perform all possible actions using CLI commands (dotnet, az, gh).
Test-Driven: No feature is complete without corresponding tests. Follow the test-first workflow defined in Section 6.
Clean Code: Strictly adhere to SOLID  and GoF patterns and principles. The code should be self-documenting, but add comments to explain complex logic or design pattern choices.
Proactive Cleanup: If you identify unused files, code, or project references, list them with a brief justification. Await my approval before generating the rm or dotnet remove commands.
Section 2: Project Initiation & Scaffolding
2.1. Project Naming: All projects and the solution will be prefixed with Po.. The application name (e.g., AppName) will be established in the initial prompt. The full solution name will be PoAppName.
2.2. Initial Scaffolding: Upon starting a new project, generate a single, runnable shell script that:
Creates the entire directory and solution structure using the ardalis/clean-architecture template.
Sets the target framework for all projects to .NET 9.0.
Generates a standard .gitignore file for a .NET solution.
2.3. Local Environment Configuration: As part of the initial scaffolding, generate the correct .vscode/launch.json and src/PoAppName.Api/Properties/launchSettings.json files.
: Configure to launch and debug the PoAppName.Api project.
: Set applicationUrl to https://localhost:5001;http://localhost:5000.
Section 3: Backend & Architecture Standards (C# / .NET)
3.1. Architecture: Default to the Ardalis Clean Architecture structure. Within the Application layer, apply principles of Vertical Slice Architecture for individual features.
3.2. API Design: Utilize Minimal APIs for simple, resource-based endpoints. Use Controllers for more complex operations involving multiple services or steps.
3.4. Design Patterns: When implementing a GoF design pattern (e.g., Repository, Mediator, Strategy)or SOLID, explicitly state the pattern's name and its purpose in a code comment directly above the implementation.
3.5. Global Exception Handling: Implement a global exception handling middleware in the API. It must log the full exception details using Serilog and return a standardized  (RFC 7807) response to the client on error.
3.6. API Documentation: Ensure the API project is configured with Swagger/OpenAPI support from the start using AddSwaggerGen and UseSwaggerUI


Section 4: Frontend Standards (Blazor WebAssembly)
4.1. Project Type: Use a Hosted Blazor WebAssembly project (PoAppName.Web hosted by PoAppName.Api).
4.2. Component Strategy:
Start with standard built-in Blazor components.
For complex UI needs (e.g., data grids, charts, advanced forms), proactively suggest and use the Radzen.Blazor component library.
4.3. State Management: For simple, component-level state, use standard Blazor parameters and events. For managing state shared across multiple, non-related components, propose using a scoped, cascaded service as a state container.
4.4. Mandatory Diagnostics Page:
Every application must have a diagnostics page at the /diag route that shows if connections to external connections, APIs, databases, internet etc. are working
This page will call a /healthz API endpoint.
Implement this using .NET's built-in Health Check features. The health checks must validate all critical dependencies (Database, external APIs, Azure Storage).
Section 5: Data & Persistence
5.1. ORM: Use Entity Framework Core as the default Object-Relational Mapper. Implement the Repository Pattern in the Infrastructure project to abstract data access logic.
5.2. Azure Storage:
Local Development: Use Azurite for emulating Azure Storage.
Table Naming: Azure Storage Tables must be named following the pattern PoAppName[TableName] (e.g., PoAppNameOrders).
Section 6: Testing & Quality Assurance
6.1. Frameworks: Use xUnit for all tests. Use FluentAssertions for assertions and NSubstitute for mocking/substitutions.
6.2. Test-First Workflow: Follow this exact sequence when adding new features:
Propose the required changes to the Domain and Application layers (e.g., new entities, services, handlers). Await my approval.
Upon approval, generate the code for the Application services/handlers.
Immediately generate the Integration Tests for the new application logic. The tests must cover the happy path, validation failures, and key edge cases.
Await my confirmation that tests pass.
Once tests are confirmed, implement the API endpoint and the Blazor UI components.
6.3. Test Project Structure: Maintain separate test projects for each type of test: PoAppName.UnitTests, PoAppName.IntegrationTests, PoAppName.FunctionalTests (for API/System tests).










Section 7: DevOps, Configuration & Logging
7.1. Secrets Management:
Store secrets in appsettings.development.json. When running code locally
Azure Deployment: Use Azure App Service Application Settings (or Key Vault for higher security) to inject secrets as environment variables.
7.2. Logging:
Implement Serilog.
Configure two sinks:
Console Sink: For real-time viewing during development.
File Sink: Write to a single log.txt file located in the API project's root directory (src/PoAppName.Api/log.txt). The file must be overwritten on each application run and configured for Verbose level logging to aid in post-run analysis.
7.3. Containerization: For any new solution, generate a multi-stage Dockerfile for the API project and a docker-compose.yml file. The compose file should define services for the API and an Azurite instance for a fully containerized local development environment




Additional Notes:
The API project is the only project that needs to run / When I say RUN APP only start the spi project
Always run the API project on port 5000/5001 http/https
If any .cs or .razor file is over 500 lines then refactor it
Create controllers and methods in a way that make it easy to test functionality in the Swagger UI related to getting data from APIs
Create all APIs in such a way that the functionality of the app can be easily replicated by doing curl calls to the API methods / this makes it easier to test without a UI
Use this repo as inspiration how to architect the app : https://github.com/fullstackhero/dotnet-starter-kit/tree/main/src
