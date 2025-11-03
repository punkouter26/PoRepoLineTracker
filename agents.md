# Development Agent Instructions

## 1. Environment & Setup

### .NET SDK
- **Version**: Use .NET 9 exclusively
- **Version Pinning**: The `global.json` file must be pinned to the 9.0.xxx SDK version
- **Build Enforcement**: Fail the build if the SDK version does not match

### Local Development Ports
- **HTTP**: Port 5000
- **HTTPS**: Port 5001
- **Configuration**: Set in `launchSettings.json`

### Storage
- **Default**: Azure Table Storage
- **Local Development**: Use Azurite for local storage emulation

### Secrets Management
- **Local Development**: All sensitive keys (connection strings, API keys) must be stored using .NET User Secrets manager
- **Azure Deployment**: Store keys in App Service Environment Variables

### CLI Usage
- **Principle**: Do not create PowerShell scripts for tasks that can be executed one line at a time in the CLI
- **Rationale**: Avoid accumulating one-shot `.ps1` scripts in the application source

---

## 2. Solution Structure

### Naming Convention
**Prefix**: All projects, solutions, and storage tables must use the prefix `Po.[AppName].*`

### Root Folder Organization

```
/src      - Application source code
/tests    - Test projects
/docs     - Documentation (Readme.md, Prd.md, Mermaid diagrams, coverage reports)
/scripts  - Utility scripts (.ps1, .sh)
```

### Source Projects (`/src`)

#### Standard Projects
- **`Po.[AppName].Api`**: ASP.NET Core API project (hosts Blazor WASM client)
- **`Po.[AppName].Client`**: Blazor WebAssembly project
- **`Po.[AppName].Shared`**: DTOs and models shared between API and Client

#### Onion Architecture Projects (Optional)
Only create these if explicitly specified:
- **`Po.[AppName].Infrastructure`**: Infrastructure layer
- **`Po.[AppName].Domain`**: Domain layer

### Test Projects (`/tests`)

- **`Po.[AppName].UnitTests`**: Unit tests using xUnit
- **`Po.[AppName].IntegrationTests`**: Integration tests using xUnit
- **`Po.[AppName].E2ETests`**: End-to-end tests using Playwright with TypeScript

---

## 3. Architecture

### Primary Architecture: Vertical Slice Architecture
- **Organization**: Organize code by feature, not by layer
- **Self-Contained**: Each slice should be self-contained
- **Philosophy**: Prioritize simple, well-factored code

### Design Principles
- **SOLID**: Apply SOLID principles pragmatically
- **GoF Patterns**: Use Gang of Four design patterns where appropriate and document their use

### Recommended Tooling
Consider using the following tools if they improve code quality and debugging:
- **CQRS**: Command Query Responsibility Segregation
- **MediatR**: Mediator pattern implementation
- **Minimal APIs**: Lightweight API endpoints
- **Polly**: Resilience and transient-fault-handling
- **Microsoft.FluentUI.AspNetCore.Components**: Fluent UI components
- **OpenTelemetry**: Observability and telemetry
- **dotnet-monitor**: On-Demand Diagnostics

---

## 4. Backend (API) Rules

### Error Handling
- **Global Middleware**: Use global exception handling middleware
- **Standard Format**: All errors must be returned as RFC 7807 Problem Details

### API Documentation
- **Swagger**: Enable Swagger for all endpoints
- **HTTP Files**: Generate `.http` files for easy manual testing of API endpoints

### Health Checks
- **Mandatory**: Implement readiness and liveness health check endpoints
- **Purpose**: Monitor application health and dependencies

### Logging & Telemetry

#### Logging (Serilog)
- **Framework**: Use Serilog for structured logging
- **Development**: Configure to write to Debug Console
- **Production**: Configure to write to Application Insights

#### Telemetry (OpenTelemetry)
- **Custom Traces**: Use `ActivitySource` for custom trace events
- **Custom Metrics**: Use `Meter` for custom metrics
- **Scope**: Instrument main application events

---

## 5. Frontend (Client) Rules

### User Experience (UX)
- **Mobile-First**: Design must be mobile-first (portrait mode)
- **Desktop**: Must also look professional on desktop layouts
- **Responsive**: Layout must be responsive, fluid, and touch-friendly

### Component Strategy
1. **Default**: Use standard Blazor components first
2. **Radzen.Blazor**: Only use if clearly necessary for specific, complex requirements

---

## 6. Testing Strategy

### Test-Driven Development (TDD)
- **Workflow**: Follow the Red → Green → Refactor cycle
- **Discipline**: Write tests before implementing features

### Unit Tests
- **Coverage**: Must cover all new business logic
- **Framework**: xUnit
- **Assertions**: Use FluentAssertions
- **Mocking**: Use NSubstitute

### Integration Tests
- **Coverage**: Must have a "happy path" test for every new API endpoint
- **Framework**: xUnit

### Test Isolation
- **Database**: Integration tests must run against an isolated test database
  - Use Azurite test container or in-memory storage
- **Setup/Teardown**: Full setup and teardown for each test run
- **Data Persistence**: No data shall persist between test runs
- **Independence**: Each test must be completely independent

### Test Organization
- **Separation**: Maintain separate test projects for each test type
- **Naming**: Follow the `Po.[AppName].[TestType]Tests` pattern

---

## Summary Checklist

### Before Starting Development
- [ ] .NET 9 SDK is installed and pinned in `global.json`
- [ ] Azurite is running for local storage
- [ ] User Secrets are configured for local development
- [ ] Ports 5000/5001 are available and configured

### For Each Feature
- [ ] Organized as a vertical slice
- [ ] Unit tests written (TDD: Red → Green → Refactor)
- [ ] Integration test for API endpoint (happy path minimum)
- [ ] Error handling returns RFC 7807 Problem Details
- [ ] Logging and telemetry added for key events
- [ ] `.http` file created for manual testing
- [ ] Mobile-responsive UI (if applicable)

### Before Committing
- [ ] All tests pass (unit + integration)
- [ ] No PowerShell scripts for simple CLI tasks
- [ ] Swagger documentation is accurate
- [ ] Health checks validate all dependencies
- [ ] Code follows SOLID principles
- [ ] GoF patterns are documented where used
