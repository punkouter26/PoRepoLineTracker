# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PoRepoLineTracker is a .NET 9.0 application that automates GitHub repository analysis by cloning repos, analyzing commits, and tracking lines of code evolution over time. Built with Clean Architecture and Vertical Slice Architecture principles.

**Tech Stack**: Blazor WebAssembly (Client), ASP.NET Core Web API (Server), Azure Table Storage (Azurite local/Azure production), LibGit2Sharp, MediatR, Serilog, Polly, xUnit

## Build and Run Commands

### Running the Application
```powershell
# Run the API project (hosts Blazor WebAssembly client)
dotnet run --project src\PoRepoLineTracker.Api

# Application will be available at:
# https://localhost:7000 (HTTPS)
# http://localhost:5000 (HTTP)
```

### Building
```powershell
# Build entire solution
dotnet build

# Build in Release mode
dotnet build --configuration Release

# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

### Testing
```powershell
# Run all tests
dotnet test

# Run specific test project
dotnet test tests\PoRepoLineTracker.UnitTests
dotnet test tests\PoRepoLineTracker.IntegrationTests
dotnet test tests\PoRepoLineTracker.ApiTests
dotnet test tests\PoRepoLineTracker.SystemTests

# Run tests with detailed output
dotnet test --verbosity normal

# Run single test
dotnet test --filter FullyQualifiedName~NameOfTest
```

### Prerequisites
```powershell
# Install Azurite for local Azure Storage emulation
npm install -g azurite

# Start Azurite (required before running the app)
azurite --silent --location c:\azurite --debug c:\azurite\debug.log

# Or start just table storage
azurite-table
```

### Configuration
```powershell
# Set up user secrets (recommended for development)
cd src\PoRepoLineTracker.Api
dotnet user-secrets init
dotnet user-secrets set "GitHub:PAT" "your_github_token_here"
dotnet user-secrets set "GitHub:LocalReposPath" "C:\YourCustomPath\LocalRepos"
dotnet user-secrets set "AzureTableStorage:ConnectionString" "UseDevelopmentStorage=true"
```

## Architecture and Key Concepts

### Clean Architecture Layers

1. **PoRepoLineTracker.Domain** - Core entities and business models
   - `GitHubRepository`: Repository metadata
   - `CommitLineCount`: Commit-level line count data with diff stats
   - `FailedOperation`: Dead letter queue for failed operations

2. **PoRepoLineTracker.Application** - Business logic using MediatR
   - **Vertical Slice Architecture**: Features organized by use case (Commands/Queries)
   - **Commands**: `AddRepositoryCommand`, `AnalyzeRepositoryCommitsCommand`, `DeleteRepositoryCommand`, etc.
   - **Queries**: `GetAllRepositoriesQuery`, `GetLineCountHistoryQuery`, `GetFileExtensionPercentagesQuery`, etc.
   - **Services**: `RepositoryService` (orchestration), Line counters (C# vs default)

3. **PoRepoLineTracker.Infrastructure** - External dependencies
   - `GitHubService`: Git operations via LibGit2Sharp, commit analysis
   - `RepositoryDataService`: Azure Table Storage operations
   - `FailedOperationService`: Dead letter queue management
   - `FailedOperationBackgroundService`: Automatic retry with exponential backoff
   - `GitClient`: Abstraction over LibGit2Sharp for DIP
   - `FileIgnoreFilter`: Filters out node_modules, bin, obj, etc.

4. **PoRepoLineTracker.Api** - Minimal APIs and Blazor host
   - All endpoints in `Program.cs` using Minimal API pattern
   - Global exception handling via `ExceptionHandlingMiddleware`
   - Polly circuit breaker for GitHub API
   - Swagger/OpenAPI at `/swagger`

5. **PoRepoLineTracker.Client** - Blazor WebAssembly UI
   - Radzen.Blazor components for charts and data grids
   - Pages: Repositories, AddRepository, AllCharts, Diag (diagnostics)
   - State management via scoped services

### Key Design Patterns

- **CQRS with MediatR**: Commands and queries separated, handlers registered automatically
- **Repository Pattern**: `IRepositoryDataService` abstracts Azure Table Storage
- **Dependency Inversion**: `IGitClient`, `ILineCounter`, `IFileIgnoreFilter` interfaces
- **Strategy Pattern**: Multiple `ILineCounter` implementations (CSharpLineCounter, DefaultLineCounter)
- **Circuit Breaker**: Polly resilience for GitHub API (50% failure ratio, 30s break)
- **Dead Letter Queue**: `FailedOperation` tracks failures, background service retries with exponential backoff

### Error Handling and Retry

The application implements comprehensive error handling:

1. **Failed Operation Tracking**
   - All commit processing failures recorded in `FailedOperation` table
   - Includes context data for retry (local path, commit SHA, diff stats)
   - Maximum 3 retry attempts with exponential backoff (5m, 10m, 20m)

2. **Background Retry Service**
   - `FailedOperationBackgroundService` polls every 5 minutes
   - Automatically retries failed operations
   - Removes from dead letter queue on success

3. **API Endpoints**
   - `GET /api/failed-operations/{repositoryId}` - View failures (requires auth)
   - `DELETE /api/failed-operations/{failedOperationId}` - Manual cleanup (requires auth)

See `docs/error-handling-and-retry-mechanisms.md` for full details.

### Data Flow

1. User adds repository via UI → `AddRepositoryCommand` → Store in Azure Table Storage
2. Repository addition triggers `AnalyzeRepositoryCommitsCommand`
3. Analysis flow:
   - Clone/pull repository using LibGit2Sharp
   - Get commit stats with diff data (lines added/removed)
   - For each commit: checkout, traverse tree, count lines by file type
   - Store `CommitLineCount` records in Azure Table Storage
4. UI queries `GetLineCountHistoryQuery` → Aggregates daily stats → Displays charts

### File Filtering

The `FileIgnoreFilter` automatically excludes:
- Directories: `node_modules`, `bin`, `obj`, `.git`, `.vs`, `packages`, `dist`, `build`, `coverage`
- Files: package-lock, yarn.lock, minified files (`.min.js`, `.min.css`)
- Binary files and other common build artifacts

Located in: `PoRepoLineTracker.Infrastructure/FileFilters/FileIgnoreFilter.cs`

### Configuration System

Configuration precedence (later overrides earlier):
1. `appsettings.json` (checked into git, no secrets)
2. `appsettings.Development.json` (environment-specific)
3. `appsettings.Development.local.json` (gitignored, for local secrets)
4. User Secrets (`dotnet user-secrets`)
5. Environment Variables (production)

**Azure App Service**: Detects `HOME` environment variable, uses ephemeral storage for cloned repos

### Health Checks

Diagnostics page at `/diag` route calls `/healthz` endpoint:
- Azure Table Storage connectivity
- GitHub API connectivity
- Built-in .NET Health Checks framework

Individual checks:
- `GET /api/health/azure-table-storage`
- `GET /api/health/github-api`

### Logging

Serilog configured with two sinks:
- **Console**: Real-time viewing during development
- **File**: `log.txt` in API project root, overwritten on each run, verbose level

## Development Practices (from Copilot Instructions)

### Workflow
- **CLI First**: Use `dotnet`, `az`, `gh` commands
- **Test-Driven**: No feature complete without tests (xUnit, FluentAssertions, NSubstitute)
- **Test Projects**: Separate projects for Unit, Integration, API, System tests

### API Design
- **Minimal APIs** for simple CRUD operations (see `Program.cs`)
- **Controllers** for complex multi-step operations (currently none)
- All endpoints testable via Swagger UI or curl

### Frontend
- Start with standard Blazor components
- Use Radzen.Blazor for complex UI (charts, grids, advanced forms)
- Scoped services for cross-component state management

### Code Quality
- Adhere to SOLID principles and GoF design patterns
- Comment design pattern usage above implementations
- Refactor any file over 500 lines
- Proactively identify and propose cleanup of unused files/references

### Azure Table Storage Naming
Tables follow pattern: `PoRepoLineTracker[TableName]`
- `PoRepoLineTrackerRepositories`
- `PoRepoLineTrackerCommitLineCounts`
- `PoRepoLineTrackerFailedOperations`

### Port Configuration
API always runs on ports 5000 (HTTP) / 5001 (HTTPS)

## Common Tasks

### Add New Feature with Tests
1. Propose changes to Domain/Application layers (await approval)
2. Implement Application services/handlers
3. Write Integration Tests (happy path, validation, edge cases)
4. Confirm tests pass
5. Implement API endpoint and Blazor UI

### Debug Analysis Issues
Check `log.txt` for detailed logs. Common issues:
- Git clone failures: Verify GitHub PAT permissions
- Line counting errors: Check `FileIgnoreFilter` rules
- Azure Storage: Ensure Azurite is running

### Update Diagrams
```powershell
.\update-diagrams.ps1  # Requires Node.js and Mermaid CLI
```

Diagrams in `Diagrams/` folder:
- `project-dependencies.mmd` - C4 component diagram
- `domain-model.mmd` - Class diagram
- `feature-sequence.mmd` - Repository analysis sequence
- `user-workflow.mmd` - User journey flowchart
