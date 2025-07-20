# PoRepoLineTracker Product Requirements Document (PRD)

## 1. Introduction
PoRepoLineTracker is an application designed to track the total lines of source code over time across GitHub repositories. This tool aims to provide insights into code growth and development activity by analyzing commit history.

## 2. Core Functionality

*   **Repository Cloning**: Ability to clone public and personal GitHub repositories locally. Authentication for personal repositories will be handled via a GitHub Personal Access Token (PAT) for ease of development.
*   **Commit Analysis**: Analyze every commit in each tracked repository.
*   **Line Counting**: Count total lines for specific file types: `.cs`, `.js`, `.ts`, `.jsx`, `.tsx`, `.html`, `.css`, `.razor`. Blank lines will be excluded from the line count.
*   **Data Storage**: Store timestamped line totals for each commit in Azure Table Storage. The storage schema will use the repository name as `PartitionKey` and the commit SHA as `RowKey` to ensure uniqueness and prevent re-processing of already analyzed commits.
*   **Visual Charts**: Display visual charts showing code growth per repository over time. This will be implemented using Radzen Blazor components.
*   **Manual Refresh**: Provide a mechanism (a UI button) to manually trigger a re-analysis of all commits for a selected repository. This process will perform a `git pull` to fetch new commits and then analyze only the new entries.
*   **Diagnostics View**: A dedicated diagnostics page (`/diag` route) will display the real-time status of all critical external dependencies, including:
    *   Backend API connectivity.
    *   Azure Table Storage connectivity.
    *   GitHub API connectivity (e.g., successful PAT authentication).
    *   Status of local Git repository storage (e.g., directory existence, last access time).

## 3. Architecture & Patterns

*   **Framework**: .NET 8 (latest stable version).
*   **Architecture**: Vertical Slice Architecture, promoting modularity and expandability. Adherence to SOLID principles and Clean Architecture patterns.
*   **Dependency Injection (DI)**: Extensive use of Dependency Injection for all services.
*   **Exception Handling**: Centralized global exception handling middleware will be implemented to log full exception details and return standardized `ProblemDetails` JSON responses.
*   **Resiliency**: For external HTTP calls (e.g., to GitHub API), the Polly library will be used to implement the Circuit Breaker pattern. `HttpClient` instances will be registered using `IHttpClientFactory`.
*   **Logging**: Serilog will be implemented as the logging provider, configured to write to both the Console and a rolling file named `log.txt` in the solution root. The default logging level will be Information, configurable via `appsettings.json`.
*   **Frontend**: Hosted Blazor WebAssembly project (`PoRepoLineTracker.Client`) served by the ASP.NET Core backend (`PoRepoLineTracker.Api`).
*   **UI Components**: Radzen.Blazor component library will be used for complex UI controls like grids and charts.

## 4. Development Workflow & Standards

*   **CLI-First**: All setup and development tasks will primarily be performed using `dotnet`, `az`, and `gh` CLI commands.
*   **Local Development**: Azurite will be used for local Azure Table Storage emulation. Azure Storage Tables will follow the naming pattern `PoAppName[TableName]` (e.g., `PoRepoLineTrackerRepositories`).
*   **Secrets Management**: Azure CLI will be used to retrieve secrets; direct secret values will never be stored in chat history or source code. Placeholders reading from `IConfiguration` will be used in code.
*   **CI/CD**: A basic CI/CD workflow file (`.github/workflows/deploy.yml`) will be created to build and test the solution on every push to the `main` branch.
*   **Testing**: xUnit will be used for all tests. A Test-Driven Development (TDD) flow will be followed for new features: propose changes, generate code, generate integration tests, confirm tests pass, then implement API/UI.
*   **Debugging**: `dotnet watch` will be used for local debugging with hot reload.
*   **Failure Protocol**: In case of build or runtime failures, full console output and `log.txt` contents will be provided for root cause analysis and precise fixes.
