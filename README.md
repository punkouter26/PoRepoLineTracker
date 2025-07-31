# PoRepoLineTracker

A powerful .NET application for tracking and analyzing lines of code across GitHub repositories over time. Built with Clean Architecture principles, this application provides comprehensive insights into repository evolution through automated commit analysis and interactive data visualization.

## 🎯 Project Description

PoRepoLineTracker automates the process of cloning GitHub repositories, analyzing commits, and tracking lines of code evolution over time. It provides detailed metrics including:

- **Repository Management**: Add and manage multiple GitHub repositories
- **Automated Analysis**: Automatic commit analysis upon repository addition
- **Line Count Tracking**: Track total lines, additions, and removals per commit
- **File Type Analysis**: Breakdown by file extensions (.cs, .js, .ts, .jsx, .tsx, .html, .css, .razor)
- **Historical Insights**: Daily line count history with interactive charts
- **Health Monitoring**: Built-in diagnostics for external dependencies

## 🏗️ Architecture Overview

This project follows **Clean Architecture** principles with clear separation of concerns across multiple layers:

- **PoRepoLineTracker.Client**: Blazor WebAssembly frontend for user interaction
- **PoRepoLineTracker.Api**: ASP.NET Core Web API hosting the Blazor client
- **PoRepoLineTracker.Application**: Business logic, services, and application workflows
- **PoRepoLineTracker.Domain**: Core entities and business models
- **PoRepoLineTracker.Infrastructure**: External dependencies (Azure Table Storage, Git operations)

### Key Technologies
- **.NET 8.0**: Latest stable framework
- **Blazor WebAssembly**: Modern web UI framework
- **Azure Table Storage**: Scalable data persistence via Azurite (local) or Azure (production)
- **LibGit2Sharp**: Git repository operations
- **Serilog**: Structured logging
- **Polly**: Circuit breaker pattern for resilience
- **xUnit**: Testing framework

## 📊 Architecture & Diagrams

This project includes comprehensive documentation through various diagrams located in the [`Diagrams/`](./Diagrams/) folder:

### Available Diagrams

1. **[Project Dependencies](./Diagrams/project-dependencies.svg)** - C4-style component diagram showing system architecture
2. **[Domain Model](./Diagrams/domain-model.svg)** - Class diagram of core entities and their relationships  
3. **[Feature Sequence](./Diagrams/feature-sequence.svg)** - Detailed sequence diagram of repository analysis workflow
4. **[User Workflow](./Diagrams/user-workflow.svg)** - User journey flowchart from repository addition to visualization

All diagrams are available in both Mermaid source format (`.mmd`) and rendered SVG format for easy viewing.

### Diagram Maintenance

Diagrams are automatically maintained using Mermaid syntax. To update all diagrams:

```powershell
# Run the diagram update script
.\update-diagrams.ps1
```

This script requires Node.js and will automatically install the Mermaid CLI if needed.

## 🚀 Getting Started

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Azurite](https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (for local development)
- [Git](https://git-scm.com/downloads)
- Visual Studio 2022 or VS Code (recommended)

### Step-by-Step Setup

#### 1. Clone the Repository
```powershell
git clone <your-repo-url>
cd PoRepoLineTracker
```

#### 2. Install Azurite (Local Azure Storage Emulator)
```powershell
# Install Azurite globally
npm install -g azurite

# Or using alternative methods:
# Via Chocolatey: choco install azurite
# Via winget: winget install Microsoft.Azurite
```

#### 3. Start Azurite
```powershell
# Start Azurite with default settings
azurite --silent --location c:\azurite --debug c:\azurite\debug.log

# Or start in the background
azurite-table
```

#### 4. Configure User Secrets (Development)
```powershell
# Navigate to the API project
cd src\PoRepoLineTracker.Api

# Initialize user secrets
dotnet user-secrets init

# Set your GitHub Personal Access Token (optional but recommended)
dotnet user-secrets set "GitHub:PAT" "your_github_personal_access_token_here"

# Set custom local repos path (optional)
dotnet user-secrets set "GitHub:LocalReposPath" "C:\YourCustomPath\LocalRepos"

# Azurite connection (should work with defaults)
dotnet user-secrets set "AzureTableStorage:ConnectionString" "UseDevelopmentStorage=true"
```

#### 5. Restore Dependencies
```powershell
# Return to solution root
cd ..\..

# Restore all packages
dotnet restore
```

#### 6. Build the Solution
```powershell
dotnet build
```

#### 7. Run the Application
```powershell
# Start the application (API hosts the Blazor client)
dotnet run --project src\PoRepoLineTracker.Api

# The application will be available at:
# https://localhost:7000 (or http://localhost:5000)
```

#### 8. Verify Setup
1. Navigate to `http://localhost:5000` in your browser
2. Try adding a GitHub repository (e.g., `https://github.com/octocat/Hello-World.git`)
3. Check the diagnostics page at `/diag` for health status

### 🧪 Running Tests

```powershell
# Run all tests
dotnet test

# Run specific test projects
dotnet test tests\PoRepoLineTracker.UnitTests
dotnet test tests\PoRepoLineTracker.IntegrationTests

# Run tests with detailed output
dotnet test --verbosity normal
```

### 🔧 Configuration Options

#### appsettings.json
```json
{
  "GitHub": {
    "LocalReposPath": "C:\\Path\\To\\Your\\LocalRepos",
    "PAT": "your_github_token"
  },
  "AzureTableStorage": {
    "ConnectionString": "UseDevelopmentStorage=true",
    "RepositoryTableName": "PoRepoLineTrackerRepositories",
    "CommitLineCountTableName": "PoRepoLineTrackerCommitLineCounts"
  }
}
```

#### Environment Variables (Production)
- `HOME`: Azure App Service home directory (auto-detected)
- `GitHub__PAT`: GitHub Personal Access Token
- `AzureTableStorage__ConnectionString`: Azure Table Storage connection string

## 📊 Features

### Repository Management
- Add repositories via GitHub clone URLs
- Automatic repository validation
- Real-time progress tracking during setup

### Commit Analysis
- Automated commit analysis on repository addition
- Historical commit processing
- File type specific line counting
- Diff-based line addition/removal tracking

### Data Visualization
- Interactive line count history charts
- Daily aggregated statistics
- Multi-repository comparison views
- File type breakdown analysis

### Health Monitoring
- Azure Table Storage connectivity checks
- GitHub API connectivity validation
- Application health diagnostics at `/api/health/*`

## 🏛️ Project Structure

```
PoRepoLineTracker/
├── src/
│   ├── PoRepoLineTracker.Api/          # ASP.NET Core API & Blazor host
│   ├── PoRepoLineTracker.Client/       # Blazor WebAssembly UI
│   ├── PoRepoLineTracker.Application/  # Business logic & services
│   ├── PoRepoLineTracker.Domain/       # Core entities & models
│   └── PoRepoLineTracker.Infrastructure/# Data access & external services
├── tests/
│   ├── PoRepoLineTracker.UnitTests/    # Unit tests
│   ├── PoRepoLineTracker.IntegrationTests/ # Integration tests
│   ├── PoRepoLineTracker.ApiTests/     # API endpoint tests
│   └── PoRepoLineTracker.SystemTests/  # End-to-end tests
├── Diagrams/                           # Architecture & workflow diagrams
│   ├── project-dependencies.mmd        # C4 component diagram
│   ├── domain-model.mmd                # Domain model class diagram
│   ├── feature-sequence.mmd            # Repository analysis sequence
│   └── user-workflow.mmd               # User interaction flowchart
├── LocalRepos/                         # Local git repositories (auto-created)
├── log.txt                            # Application logs
└── README.md
```

## 🔒 Security Notes

- Never commit GitHub Personal Access Tokens to source control
- Use user secrets for local development
- Use Azure Key Vault or environment variables for production
- The application creates local git repositories - ensure adequate disk space

## 🚢 Deployment

### Azure App Service Deployment
The application is designed to run in Azure App Service with:
- Automatic Azure Table Storage integration
- Ephemeral storage for temporary git repositories
- Environment-based configuration

### Local Production Mode
```powershell
# Build in Release mode
dotnet build --configuration Release

# Run in Production mode
$env:ASPNETCORE_ENVIRONMENT="Production"
dotnet run --project src\PoRepoLineTracker.Api --configuration Release
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes following the established patterns
4. Add tests for new functionality
5. Run the full test suite
6. Submit a pull request

## 📝 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🆘 Troubleshooting

### Common Issues

**Azurite Connection Issues**
```powershell
# Stop any running Azurite instances
taskkill /f /im azurite.exe

# Restart Azurite
azurite --silent --location c:\azurite
```

**Git Clone Failures**
- Ensure your GitHub PAT has repository read permissions
- Check network connectivity
- Verify repository URL format

**Build Errors**
```powershell
# Clean and rebuild
dotnet clean
dotnet restore
dotnet build
```

For additional support, check the application logs in `log.txt` or enable detailed logging by setting the log level to `Debug` in appsettings.json.
