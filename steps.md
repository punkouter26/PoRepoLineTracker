# PoRepoLineTracker Development Steps

This document outlines the high-level steps for completing the PoRepoLineTracker application.

## Completed Steps:

1.  **Initial Project Setup**:
    *   Verified .NET 8 SDK, Git, and Azure CLI prerequisites.
    *   Created the main solution directory (`PoRepoLineTracker`) and all subdirectories (`src`, `tests`, `.github/workflows`, `.vscode`, `AzuriteData`).
    *   Created the solution file (`PoRepoLineTracker.sln`) and placeholder files (`.editorconfig`, `.gitignore`, `README.md`, `log.txt`).
    *   Created all .NET projects (`Api`, `Application`, `Client`, `Domain`, `Infrastructure`, and test projects) using appropriate templates.
    *   Established initial project references between layers (e.g., `Api` referencing `Application` and `Client`, `Application` referencing `Domain`).
    *   Performed `dotnet restore` to fetch initial NuGet packages.

2.  **Core Domain & Application Interfaces**:
    *   Defined core domain models (`GitHubRepository`, `CommitLineCount`) in `PoRepoLineTracker.Domain`.
    *   Defined application-level service interfaces (`IGitHubService`, `IRepositoryService`, `IRepositoryDataService`) in `PoRepoLineTracker.Application/Interfaces`. `IRepositoryDataService` was moved from Infrastructure to Application to resolve circular dependency and adhere to Clean Architecture.

3.  **Infrastructure Service Implementations**:
    *   Implemented `GitHubService` in `PoRepoLineTracker.Infrastructure/Services` for Git operations (clone, pull, get commits, count lines excluding blank lines) using `LibGit2Sharp`.
    *   Implemented `RepositoryDataService` in `PoRepoLineTracker.Infrastructure/Services` for Azure Table Storage persistence of repository and commit data using `Azure.Data.Tables`.
    *   Defined Table Storage entities (`GitHubRepositoryEntity`, `CommitLineCountEntity`) for data mapping.

4.  **Application Service Implementation**:
    *   Implemented `RepositoryService` in `PoRepoLineTracker.Application/Services` to orchestrate Git operations and data persistence using `IGitHubService` and `IRepositoryDataService`.

5.  **Dependency Injection & API Endpoints**:
    *   Registered all application and infrastructure services with the Dependency Injection container in `PoRepoLineTracker.Api/Program.cs`.
    *   Exposed RESTful API endpoints in `PoRepoLineTracker.Api/Program.cs` for adding repositories, triggering analysis, and retrieving repository/line count data.

6.  **Logging & Resilience Configuration**:
    *   Configured Serilog in `PoRepoLineTracker.Api` to log to console and a rolling `log.txt` file.
    *   Integrated Polly with a Circuit Breaker pattern for the `GitHubClient` in `PoRepoLineTracker.Api` to enhance HTTP call resilience.

7.  **Basic Frontend UI & Diagnostics**:
    *   Configured `PoRepoLineTracker.Api` to host the `PoRepoLineTracker.Client` Blazor WebAssembly application.
    *   Created a `Diagnostics.razor` page (`/diag`) in the client to check backend API status.
    *   Created a `Repositories.razor` page (`/repositories`) in the client for adding/listing repositories and triggering analysis.
    *   Updated `NavMenu.razor` to include links to both `Diagnostics` and `Repositories` pages.

8.  **Integrate Radzen Blazor Charts**:
    *   Added the Radzen.Blazor NuGet package to `PoRepoLineTracker.Client`.
    *   Implemented visual charts on the `Repositories.razor` page to display code growth over time using Radzen Blazor components.

9.  **Enhance Diagnostics View**:
    *   Expanded the `/diag` page to include detailed health checks for Azure Table Storage and GitHub API connectivity by exposing new health check endpoints from the backend.

## Remaining High-Level Steps:

11. **Implement Comprehensive Testing**:
    *   Wrote unit tests for `Domain` logic using xUnit (e.g., `GitHubRepositoryTests.cs`).
    *   Wrote unit tests for `Application` logic using xUnit (e.g., `RepositoryServiceTests.cs`).
    *   Attempted to develop integration tests for `Application` and `Infrastructure` services, including interactions with mocked or emulated external dependencies (e.g., Azurite). Encountered persistent compilation issues with `Azure.Data.Tables` methods (`DeleteTableIfExistsAsync`, `CreateTableIfNotExistsAsync`) not being recognized in the test project, despite correct package references and code. This issue requires further investigation or manual intervention.

12. **Develop CI/CD Workflow**:
    *   Created a basic GitHub Actions workflow file (`.github/workflows/deploy.yml`) to automate the build and unit test process on every push to the `main` branch.

13. **Refine Line Counting & Git Operations**:
    *   Reviewed and optimized the line counting logic for efficiency and accuracy, ensuring proper handling of various file types and edge cases.
    *   Enhanced Git operations to retrieve actual commit dates for more accurate `LastAnalyzedCommitDate` tracking.

This completes the development of the PoRepoLineTracker application based on the outlined steps.
