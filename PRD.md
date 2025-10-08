# Product Requirements Document (PRD)
# PoRepoLineTracker

## Application Overview

**PoRepoLineTracker** is a .NET 9.0 web application that automates GitHub repository analysis by tracking lines of code evolution over time. Built with Clean Architecture and Vertical Slice Architecture principles, it provides comprehensive code metrics, historical trends, and visual analytics for software development teams.

### Purpose

- **Automated Code Metrics**: Clone GitHub repositories and analyze commit history to track lines of code changes
- **Historical Trends**: Visualize code growth/reduction over time with interactive charts
- **Language Analysis**: Break down code by file extension and programming language
- **Failed Operation Management**: Dead letter queue with automatic retry for resilient operations

### Technology Stack

- **Frontend**: Blazor WebAssembly with Radzen.Blazor UI components
- **Backend**: ASP.NET Core 9.0 Minimal API
- **Database**: Azure Table Storage (Azurite for local development)
- **Architecture**: Clean Architecture + Vertical Slice Architecture + CQRS (MediatR)
- **Version Control Integration**: LibGit2Sharp for Git operations
- **Monitoring**: Serilog + Application Insights
- **Deployment**: Azure App Service with Bicep IaC

## UI Components

### 1. Repositories Page (`/repositories`)

**Purpose**: Main dashboard for managing tracked repositories

**Features**:
- **Data Grid**: Displays all tracked repositories with columns:
  - Repository name (Owner/Repo format)
  - Clone URL
  - Last analyzed commit date
  - Actions (Delete button)
- **Add Repository Button**: Opens dialog to add new repository
- **Delete All Button**: Removes all repositories (with confirmation)
- **Real-time Updates**: Auto-refreshes after operations

**User Actions**:
- View list of tracked repositories
- Add new repository via URL or GitHub user search
- Delete individual repository
- Bulk delete all repositories

### 2. Add Repository Dialog

**Purpose**: Add GitHub repositories for tracking

**Features**:
- **Manual Entry Tab**:
  - Repository URL input field
  - Owner and Repository name auto-extracted from URL
  - Clone URL validation
- **GitHub User Tab**:
  - GitHub username input
  - "Select from GitHub" button fetches user's repositories
  - Repository selector dropdown
- **Validation**: Ensures valid GitHub URLs and repository access

**User Actions**:
- Enter repository URL manually
- Search GitHub user's repositories
- Select repository from dropdown
- Submit to start tracking

### 3. All Charts Page (`/allcharts`)

**Purpose**: Visualize code metrics and trends across all repositories

**Features**:
- **Time Range Selector**: Dropdown to filter data (7, 14, 30, 60, 90 days)
- **Total Lines Over Time Chart**:
  - Line chart showing aggregate lines of code
  - X-axis: Date, Y-axis: Total lines
  - Tooltips with exact values
- **Lines by Repository Chart**:
  - Multi-line chart with one line per repository
  - Color-coded by repository
  - Legend for repository identification
- **File Extension Distribution Chart**:
  - Pie chart showing code breakdown by language/extension
  - Percentage labels
  - Interactive tooltips
- **Responsive Design**: Charts adapt to screen size

**User Actions**:
- Select time range for analysis
- Hover over charts for detailed values
- Compare multiple repositories visually
- Identify code composition by file type

### 4. Diagnostics Page (`/diag`)

**Purpose**: Monitor application health and connectivity

**Features**:
- **Overall System Health**:
  - Status indicator (Healthy/Unhealthy)
  - Timestamp of last check
- **Azure Table Storage Health**:
  - Connection status
  - Error messages if unavailable
- **GitHub API Health**:
  - API connectivity status
  - Rate limit information
- **System Information**:
  - Environment (Development/Production)
  - Application version
  - Last updated timestamp
- **Detailed Health Checks**: Expandable list of individual component checks

**User Actions**:
- Verify application health before operations
- Troubleshoot connectivity issues
- Monitor external dependency status

### 5. Navigation

**Layout**: Fixed sidebar with Material3 design

**Menu Items**:
- **Repositories**: Main repository management
- **All Charts**: Analytics dashboard
- **Diag**: System diagnostics

**Header**:
- Application title: "PoRepoLineTracker"
- Material3 purple primary color (#6750A4)

**Footer**:
- Copyright/version information
- Fixed at bottom of viewport

## Core Features

### Repository Management

1. **Add Repository**
   - Manual URL entry or GitHub user repository selection
   - Automatic metadata extraction (owner, name, clone URL)
   - Validates repository accessibility
   - Triggers automatic commit analysis

2. **Repository Analysis**
   - Clones repository using LibGit2Sharp
   - Processes all commits chronologically
   - Calculates lines of code per commit
   - Tracks diff statistics (lines added/removed)
   - Supports multiple line counting strategies (C# specialized, default)
   - Filters out build artifacts and dependencies (node_modules, bin, obj, etc.)

3. **Delete Repository**
   - Individual deletion from data grid
   - Bulk delete all repositories
   - Confirmation dialog for safety
   - Cleanup of associated commit data

### Code Analytics

1. **Line Count History**
   - Daily aggregation of lines of code
   - Historical trend tracking
   - Supports multiple repositories
   - Filterable by time range

2. **File Extension Analysis**
   - Groups code by file extension
   - Calculates percentage distribution
   - Identifies primary programming languages
   - Excludes non-code files

3. **Commit Statistics**
   - Lines added per commit
   - Lines removed per commit
   - Net line change tracking
   - Commit date/time metadata

### Error Handling

1. **Failed Operation Queue**
   - Dead letter queue for failed commit processing
   - Stores context data for retry (local path, commit SHA, diff stats)
   - Maximum 3 retry attempts
   - Exponential backoff (5m, 10m, 20m)

2. **Background Retry Service**
   - Polls every 5 minutes for retryable operations
   - Automatic retry execution
   - Cleanup on success
   - Logging of retry attempts

3. **API Endpoints for Failed Operations**
   - `GET /api/failed-operations/{repositoryId}` - View failures (requires auth)
   - `DELETE /api/failed-operations/{failedOperationId}` - Manual cleanup (requires auth)

## Data Models

### GitHubRepository
- **Id**: Unique identifier (GUID)
- **Owner**: Repository owner (user/organization)
- **Name**: Repository name
- **CloneUrl**: Git clone URL
- **LastAnalyzedCommitDate**: Last processed commit timestamp

### CommitLineCount
- **Id**: Unique identifier (GUID)
- **RepositoryId**: Foreign key to GitHubRepository
- **CommitSha**: Git commit hash
- **CommitDate**: Commit timestamp
- **TotalLines**: Total lines of code
- **LinesAdded**: Lines added in commit (from diff)
- **LinesRemoved**: Lines removed in commit (from diff)
- **FileExtension**: File extension for language breakdown

### FailedOperation
- **Id**: Unique identifier (GUID)
- **RepositoryId**: Related repository
- **OperationType**: Type of failed operation
- **ErrorMessage**: Exception/error details
- **RetryCount**: Number of retry attempts
- **CreatedAt**: Failure timestamp
- **LastRetryAt**: Last retry timestamp
- **ContextData**: JSON with operation context

## User Workflows

### 1. Add and Analyze Repository

```
User → Repositories Page → Add Repository Button →
  Enter GitHub URL → Submit →
  Repository Added → Background Analysis Starts →
  View in Data Grid
```

### 2. View Code Trends

```
User → All Charts Page → Select Time Range →
  View Total Lines Chart →
  Compare Repository Trends →
  Analyze File Extensions
```

### 3. Monitor System Health

```
User → Diag Page →
  Check Azure Storage Status →
  Check GitHub API Status →
  Review System Information
```

### 4. Troubleshoot Failed Operations

```
User → API Call → View Failed Operations →
  Review Error Messages →
  Wait for Automatic Retry →
  Manual Cleanup if Needed
```

## Technical Architecture

### Layers

1. **Domain** (`PoRepoLineTracker.Domain`)
   - Core business entities
   - No external dependencies

2. **Application** (`PoRepoLineTracker.Application`)
   - MediatR command/query handlers
   - Business logic orchestration
   - Vertical slice architecture

3. **Infrastructure** (`PoRepoLineTracker.Infrastructure`)
   - Azure Table Storage implementation
   - LibGit2Sharp Git client
   - GitHub API service
   - Failed operation management

4. **API** (`PoRepoLineTracker.Api`)
   - ASP.NET Core Minimal APIs
   - Blazor WebAssembly host
   - Serilog + Application Insights
   - Health check endpoints

5. **Client** (`PoRepoLineTracker.Client`)
   - Blazor WebAssembly UI
   - Radzen components
   - Client-side logging service

### Design Patterns

- **CQRS**: Commands and queries separated via MediatR
- **Repository Pattern**: IRepositoryDataService abstraction
- **Dependency Inversion**: Interfaces for external dependencies
- **Strategy Pattern**: Multiple ILineCounter implementations
- **Circuit Breaker**: Polly resilience for GitHub API
- **Dead Letter Queue**: Failed operation tracking with retry

## Configuration

### Required Settings

**Development** (appsettings.Development.json):
```json
{
  "GitHub": {
    "PAT": "your_github_personal_access_token",
    "LocalReposPath": "C:\\LocalRepos"
  },
  "AzureTableStorage": {
    "ConnectionString": "UseDevelopmentStorage=true"
  }
}
```

**Production** (Azure App Service Configuration):
- `APPLICATIONINSIGHTS_CONNECTION_STRING`: Application Insights
- `AzureTableStorage__ConnectionString`: Azure Storage Account
- `GitHub__PAT`: GitHub Personal Access Token (optional)

### Environment-Specific Behavior

**Development**:
- Uses Azurite (local Azure Storage emulator)
- File-based logging (`log.txt`)
- Client-to-server logging endpoint enabled
- Swagger UI available at `/swagger`

**Production**:
- Uses Azure Table Storage
- Application Insights telemetry
- No file logging (security/performance)
- No client logging endpoint (security)

## Security Considerations

1. **Authentication**: Currently not implemented (future enhancement)
2. **Authorization**: Failed operation endpoints require auth (placeholder)
3. **Data Validation**: Input validation on all API endpoints
4. **Secrets Management**: GitHub PAT via configuration (not hardcoded)
5. **CORS**: Not needed (same-origin hosting)
6. **HTTPS**: Enforced in production

## Performance Characteristics

- **Repository Addition**: < 500ms (metadata only)
- **Commit Analysis**: Varies by repository size (5-30 seconds typical)
- **Chart Data Retrieval**: < 1000ms (cached daily aggregations)
- **Health Checks**: < 500ms per check

## Future Enhancements

1. **Authentication/Authorization**: Azure AD or GitHub OAuth
2. **Branch Selection**: Analyze specific branches
3. **Commit Range Filtering**: Analyze specific date ranges
4. **Team Analytics**: Multi-user repository sharing
5. **Export Functionality**: CSV/Excel export of metrics
6. **Webhook Integration**: Automatic analysis on new commits
7. **Custom Metrics**: User-defined code quality metrics
8. **Notifications**: Email/Teams alerts for threshold breaches

## Success Metrics

- Repository analysis completion rate: > 95%
- Failed operation retry success rate: > 80%
- Chart load time: < 2 seconds
- System uptime: > 99%
- User satisfaction: Positive feedback on UI usability
