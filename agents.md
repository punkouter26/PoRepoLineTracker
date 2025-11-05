# Development Agent Instructions# Development Agent Instructions



## 1. Core Architecture & Principles## 1. Environment & Setup



### Architecture Style### .NET SDK

- **Vertical Slice Architecture**: All code must be organized by feature, not by technical layer- **Version**: Use .NET 9 exclusively

- **Organization**: Group files by feature (e.g., `/Features/GetProduct/Endpoint.cs`)- **Version Pinning**: The `global.json` file must be pinned to the 9.0.xxx SDK version

- **Self-Contained**: Each feature slice should encapsulate all related logic- **Build Enforcement**: Fail the build if the SDK version does not match



### Design Philosophy### Local Development Ports

- **Simplicity First**: Prioritize simple, well-factored, and self-contained code- **HTTP**: Port 5000

- **SOLID Principles**: Apply pragmatically, not dogmatically- **HTTPS**: Port 5001

- **Design Patterns**: If Gang of Four (GoF) design patterns are used, document their use clearly in code comments- **Configuration**: Set in `launchSettings.json`



### Recommended Tooling### Storage

- **MediatR**: Implement CQRS pattern within vertical slices- **Default**: Azure Table Storage

- **Minimal APIs**: Use for all new API endpoints- **Local Development**: Use Azurite for local storage emulation

- **OpenTelemetry**: 

  - Use `ActivitySource` for custom traces### Secrets Management

  - Use `Meter` for custom metrics- **Local Development**: All sensitive keys (connection strings, API keys) must be stored using .NET User Secrets manager

- **Polly**: Use for resilience and transient-fault-handling where it adds clear value- **Azure Deployment**: Store keys in App Service Environment Variables

- **dotnet-monitor**: Consider for on-demand diagnostics

### CLI Usage

---- **Principle**: Do not create PowerShell scripts for tasks that can be executed one line at a time in the CLI

- **Rationale**: Avoid accumulating one-shot `.ps1` scripts in the application source

## 2. Solution & Project Structure

---

### Root Folder Organization

```## 2. Solution Structure

/src      - Application source code

/tests    - All test projects### Naming Convention

/docs     - Documentation (Readme.md, Prd.md, Mermaid diagrams, coverage reports)**Prefix**: All projects, solutions, and storage tables must use the prefix `Po.[AppName].*`

/infra    - Infrastructure as Code (Bicep files)

/scripts  - Utility scripts (.ps1, .sh)### Root Folder Organization

```

```

### Naming Conventions/src      - Application source code

**Prefix Rule**: All projects, solutions, and Azure Storage tables must use the prefix `Po.[AppName].*`/tests    - Test projects

/docs     - Documentation (Readme.md, Prd.md, Mermaid diagrams, coverage reports)

**HTML Title**: User-facing HTML `<title>` tag must include the `Po.` prefix/scripts  - Utility scripts (.ps1, .sh)

```

### Project Organization

### Source Projects (`/src`)

#### Standard Projects

- **`/src/Po.[AppName].Api/`**: ASP.NET Core API project#### Standard Projects

  - **`/Features/`**: Contains backend vertical slices (MediatR handlers, endpoints)- **`Po.[AppName].Api`**: ASP.NET Core API project (hosts Blazor WASM client)

- **`/src/Po.[AppName].Client/`**: Blazor WebAssembly application- **`Po.[AppName].Client`**: Blazor WebAssembly project

- **`/src/Po.[AppName].Shared/`**: DTOs and models shared between API and Client- **`Po.[AppName].Shared`**: DTOs and models shared between API and Client



#### Example Structure#### Onion Architecture Projects (Optional)

```Only create these if explicitly specified:

/src/Po.[AppName].Api/- **`Po.[AppName].Infrastructure`**: Infrastructure layer

  /Features/- **`Po.[AppName].Domain`**: Domain layer

    /GetProduct/

      - GetProductQuery.cs### Test Projects (`/tests`)

      - GetProductQueryHandler.cs

      - GetProductEndpoint.cs- **`Po.[AppName].UnitTests`**: Unit tests using xUnit

    /CreateProduct/- **`Po.[AppName].IntegrationTests`**: Integration tests using xUnit

      - CreateProductCommand.cs- **`Po.[AppName].E2ETests`**: End-to-end tests using Playwright with TypeScript

      - CreateProductCommandHandler.cs

      - CreateProductEndpoint.cs---

```

## 3. Architecture

### Dependency Management

- **Central Package Management**: All NuGet packages must be managed in a single `Directory.Packages.props` file at the repository root### Primary Architecture: Vertical Slice Architecture

- **Version Control**: Use centralized package versions across all projects- **Organization**: Organize code by feature, not by layer

- **Self-Contained**: Each slice should be self-contained

---- **Philosophy**: Prioritize simple, well-factored code



## 3. Environment & Configuration### Design Principles

- **SOLID**: Apply SOLID principles pragmatically

### .NET SDK- **GoF Patterns**: Use Gang of Four design patterns where appropriate and document their use

- **Version**: Use .NET 9 exclusively

- **Version Pinning**: The `global.json` file must be pinned to a specific 9.0.xxx SDK version### Recommended Tooling

- **Build Enforcement**: Fail the build if the SDK version does not matchConsider using the following tools if they improve code quality and debugging:

- **CQRS**: Command Query Responsibility Segregation

### Local Development- **MediatR**: Mediator pattern implementation

- **Ports**:- **Minimal APIs**: Lightweight API endpoints

  - HTTP: Port 5000- **Polly**: Resilience and transient-fault-handling

  - HTTPS: Port 5001- **Microsoft.FluentUI.AspNetCore.Components**: Fluent UI components

  - Configuration: Set in `launchSettings.json`- **OpenTelemetry**: Observability and telemetry

- **dotnet-monitor**: On-Demand Diagnostics

### Secrets Management

- **Local Development**: All sensitive keys (connection strings, API keys) must be stored using .NET User Secrets manager---

- **Never Commit**: Secrets must not be committed to `appsettings.json`

- **Azure Deployment**: Store keys in Azure Key Vault, accessed via Key Vault References## 4. Backend (API) Rules



### Storage### Error Handling

- **Default**: Azure Table Storage- **Global Middleware**: Use global exception handling middleware

- **Local Development**: Use Azurite for local storage emulation- **Standard Format**: All errors must be returned as RFC 7807 Problem Details

- **Integration Tests**: Use isolated Azurite test container or in-memory storage

### API Documentation

### Null Safety- **Swagger**: Enable Swagger for all endpoints

- **Nullable Reference Types**: Must be enabled in all `.csproj` files:- **HTTP Files**: Generate `.http` files for easy manual testing of API endpoints

  ```xml

  <Nullable>enable</Nullable>### Health Checks

  ```- **Mandatory**: Implement readiness and liveness health check endpoints

- **Purpose**: Monitor application health and dependencies

### CLI Usage

- **Principle**: Prefer single-line CLI commands over creating one-shot PowerShell scripts### Logging & Telemetry

- **Rationale**: Avoid accumulating simple `.ps1` scripts in the application source

#### Logging (Serilog)

---- **Framework**: Use Serilog for structured logging

- **Development**: Configure to write to Debug Console

## 4. Backend (API) Implementation- **Production**: Configure to write to Application Insights



### API Documentation#### Telemetry (OpenTelemetry)

- **Swagger**: Enable Swagger (OpenAPI) generation for all endpoints- **Custom Traces**: Use `ActivitySource` for custom trace events

- **HTTP Files**: Generate `.http` files with sample requests for all endpoints to facilitate easy manual testing- **Custom Metrics**: Use `Meter` for custom metrics

- **Scope**: Instrument main application events

### Health Checks

- **Mandatory Endpoints**:---

  - `/api/health/live` - Liveness check

  - `/api/health/ready` - Readiness check (must include checks for all critical external dependencies)## 5. Frontend (Client) Rules

- **Purpose**: Monitor application health and dependencies

### User Experience (UX)

### Logging & Telemetry- **Mobile-First**: Design must be mobile-first (portrait mode)

- **Desktop**: Must also look professional on desktop layouts

#### Logging (Serilog)- **Responsive**: Layout must be responsive, fluid, and touch-friendly

- **Framework**: Use Serilog for structured logging

- **Configuration**: Configure via `appsettings.json`### Component Strategy

  - **Development**: Write to Debug Console1. **Default**: Use standard Blazor components first

  - **Production**: Write to Application Insights2. **Radzen.Blazor**: Only use if clearly necessary for specific, complex requirements

- **Enrichment**: Enrich all logs with `CorrelationId` using `Activity.Current.Id`

- **Error Handling**: Use structured `ILogger.LogWarning` or `LogError` within all catch blocks---



#### Telemetry (OpenTelemetry)## 6. Testing Strategy

- **Custom Traces**: Use `ActivitySource` for custom trace events

- **Custom Metrics**: Use `Meter` for custom metrics### Test-Driven Development (TDD)

- **Scope**: Instrument main application events and critical operations- **Workflow**: Follow the Red → Green → Refactor cycle

- **Discipline**: Write tests before implementing features

### Error Handling

- **Global Middleware**: Use global exception handling middleware### Unit Tests

- **Standard Format**: All errors must be returned as RFC 7807 Problem Details- **Coverage**: Must cover all new business logic

- **Structured Logging**: Always log exceptions with context before returning error responses- **Framework**: xUnit

- **Assertions**: Use FluentAssertions

---- **Mocking**: Use NSubstitute



## 5. Frontend (Client) Implementation### Integration Tests

- **Coverage**: Must have a "happy path" test for every new API endpoint

### UI Framework- **Framework**: xUnit

- **Primary**: Use `Microsoft.FluentUI.AspNetCore.Components` as the primary component library

- **Secondary**: Use `Radzen.Blazor` only if its tools are essential for a specific, complex requirement### Test Isolation

- **Database**: Integration tests must run against an isolated test database

### User Experience (UX)  - Use Azurite test container or in-memory storage

- **Mobile-First**: Design must be mobile-first (portrait mode)- **Setup/Teardown**: Full setup and teardown for each test run

- **Responsive**: Layout must be responsive, fluid, and touch-friendly- **Data Persistence**: No data shall persist between test runs

- **Desktop**: Must also look professional on desktop layouts- **Independence**: Each test must be completely independent

- **Consistency**: Ensure consistent spacing, typography, and component usage

### Test Organization

### Client-Side Telemetry- **Separation**: Maintain separate test projects for each test type

- **Application Insights**: Blazor client must integrate Application Insights JavaScript SDK- **Naming**: Follow the `Po.[AppName].[TestType]Tests` pattern

- **Capture**:

  - Page views---

  - Client-side errors

  - Performance metrics## Summary Checklist

  - User interactions (optional)

### Before Starting Development

---- [ ] .NET 9 SDK is installed and pinned in `global.json`

- [ ] Azurite is running for local storage

## 6. Testing Strategy- [ ] User Secrets are configured for local development

- [ ] Ports 5000/5001 are available and configured

### Test-Driven Development (TDD)

- **Workflow**: Strictly follow the Red → Green → Refactor cycle### For Each Feature

- **Discipline**: Write tests before implementing features- [ ] Organized as a vertical slice

- **Coverage**: Aim for high coverage of business logic- [ ] Unit tests written (TDD: Red → Green → Refactor)

- [ ] Integration test for API endpoint (happy path minimum)

### Test Naming Convention- [ ] Error handling returns RFC 7807 Problem Details

- **Format**: `MethodName_StateUnderTest_ExpectedBehavior`- [ ] Logging and telemetry added for key events

- **Examples**:- [ ] `.http` file created for manual testing

  - `CreateProduct_WithValidData_ReturnsCreatedProduct`- [ ] Mobile-responsive UI (if applicable)

  - `GetProduct_WhenNotFound_ReturnsNotFound`

### Before Committing

### Unit Tests (xUnit)- [ ] All tests pass (unit + integration)

- **Coverage**: Must cover all new backend business logic (e.g., MediatR handlers)- [ ] No PowerShell scripts for simple CLI tasks

- **Framework**: xUnit- [ ] Swagger documentation is accurate

- **Assertions**: Use FluentAssertions for readable assertions- [ ] Health checks validate all dependencies

- **Mocking**: Use NSubstitute to mock all external dependencies- [ ] Code follows SOLID principles

- **Isolation**: Each test must be completely independent- [ ] GoF patterns are documented where used


### Component Tests (bUnit)
- **Coverage**: Must cover all new Blazor components
- **Test Areas**:
  - Component rendering
  - User interactions (clicks, input)
  - State changes
- **Mocking**: Mock dependencies such as `IHttpClientFactory` and `IJSRuntime`
- **Framework**: bUnit with xUnit

### Integration Tests (xUnit)
- **Coverage**: Create a "happy path" test for every new API endpoint
- **Framework**: xUnit with `WebApplicationFactory`
- **Database**: Tests must run against an isolated Azurite instance or in-memory storage
- **Setup/Teardown**: Full setup and teardown for each test run
- **Data Persistence**: No data shall persist between test runs
- **Independence**: Each test must be completely independent
- **Test Data**: Use Bogus library to generate realistic test data

### End-to-End Tests (Playwright)
- **Language**: Write tests in TypeScript
- **Browser**: Target Chromium for both mobile and desktop views
- **Network Mocking**: Use network interception (`page.route()`) to mock API responses for stable testing
- **Accessibility**: Integrate axe-core for automated accessibility checks
- **Visual Regression**: Implement screenshot testing for visual regression checks
- **Organization**: Maintain separate E2E test project following `Po.[AppName].E2ETests.TS` pattern

### Test Organization
- **Separation**: Maintain separate test projects for each test type:
  - `Po.[AppName].UnitTests`
  - `Po.[AppName].IntegrationTests`
  - `Po.[AppName].E2ETests.TS`

---

## 7. Infrastructure as Code (Bicep)

### Automation
- **Declarative**: All Azure resources must be defined in Bicep files
- **Deployment**: Must be fully deployable via `azd up` command without requiring manual user input
- **Idempotent**: Infrastructure deployment must be idempotent and repeatable

### Security

#### Managed Identity
- **Enable**: System-Assigned Managed Identity on Azure App Service
- **Purpose**: Eliminate the need for credentials in application code

#### Key Vault
- **Provision**: Azure Key Vault for secret storage
- **Access Policy**: Grant the App Service's Managed Identity `Get` and `List` secret permissions
- **Configuration**: Configure App Service to use Key Vault References to access secrets
- **Security Rule**: Never check secrets into any configuration files

### Observability
- **Diagnostic Settings**: Provision `Microsoft.Insights/diagnosticSettings` for all resources
- **Log Forwarding**: Configure to forward all logs and metrics to a central Log Analytics Workspace
- **Monitoring**: Enable Application Insights for application-level telemetry

### Resource Naming
- **Consistency**: Follow Azure naming conventions with `Po.[AppName]` prefix
- **Environment**: Include environment suffix (e.g., `-dev`, `-staging`, `-prod`) where appropriate

---

## Summary Checklist

### Before Starting Development
- [ ] .NET 9 SDK is installed and pinned in `global.json`
- [ ] Azurite is running for local storage
- [ ] User Secrets are configured for local development
- [ ] Ports 5000/5001 are available and configured
- [ ] Nullable Reference Types enabled in all projects

### For Each Feature
- [ ] Organized as a vertical slice under `/Features/`
- [ ] Unit tests written (TDD: Red → Green → Refactor)
- [ ] Integration test for API endpoint (happy path minimum)
- [ ] Error handling returns RFC 7807 Problem Details
- [ ] Logging and telemetry added for key events
- [ ] `.http` file created for manual testing
- [ ] Mobile-responsive UI (if applicable)
- [ ] Component tests for Blazor components (if applicable)

### Before Committing
- [ ] All tests pass (unit + integration + component)
- [ ] No PowerShell scripts for simple CLI tasks
- [ ] Swagger documentation is accurate
- [ ] Health checks validate all dependencies
- [ ] Code follows SOLID principles
- [ ] GoF patterns are documented where used
- [ ] Secrets not committed to source control
- [ ] `Directory.Packages.props` updated if new packages added

### Before Deployment
- [ ] Bicep files validated and linted
- [ ] `azd up` tested in development environment
- [ ] Application Insights configured
- [ ] Key Vault references configured
- [ ] Diagnostic settings enabled for all resources
- [ ] E2E tests pass in staging environment

---

## Additional Best Practices

### Performance
- Use asynchronous programming (`async`/`await`) for all I/O operations
- Implement proper cancellation token support in MediatR handlers
- Use `IAsyncEnumerable<T>` for streaming large datasets

### Code Quality
- Follow C# coding conventions
- Use meaningful variable and method names
- Keep methods small and focused (Single Responsibility Principle)
- Avoid deep nesting (maximum 3 levels)
- Document complex business logic with comments

### Dependency Injection
- Register services with appropriate lifetimes:
  - Singleton for stateless services
  - Scoped for per-request state
  - Transient for lightweight, stateless services
- Use constructor injection exclusively

### API Versioning
- Consider implementing API versioning from the start if the API will be public or long-lived
- Use URL-based versioning (e.g., `/api/v1/products`) or header-based versioning

### Security
- Always validate input at API boundaries
- Use parameterized queries to prevent SQL injection
- Implement proper authentication and authorization
- Follow principle of least privilege for all Azure resources
- Enable HTTPS only in production
