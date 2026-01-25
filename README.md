# PoRepoLineTracker

[![CI/CD Pipeline](https://github.com/YOUR-USERNAME/PoRepoLineTracker/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/YOUR-USERNAME/PoRepoLineTracker/actions/workflows/ci-cd.yml)
[![PR Validation](https://github.com/YOUR-USERNAME/PoRepoLineTracker/actions/workflows/pr-validation.yml/badge.svg)](https://github.com/YOUR-USERNAME/PoRepoLineTracker/actions/workflows/pr-validation.yml)

**Automated GitHub repository analysis with code metrics and historical trend visualization.**

PoRepoLineTracker is a .NET 9.0 web application that tracks lines of code evolution across GitHub repositories. Built with Clean Architecture and featuring a modern Blazor WebAssembly UI, it provides real-time insights into code growth, language distribution, and commit-level analysis.

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
- **.NET 9.0**: Latest framework with C# 13
- **Blazor WebAssembly**: Modern web UI with Radzen components
- **Azure Table Storage**: Scalable data persistence via Azurite (local) or Azure (production)
- **LibGit2Sharp**: Git repository operations
- **Serilog + Application Insights**: Structured logging and telemetry
- **MediatR**: CQRS pattern implementation
- **Polly**: Circuit breaker pattern for resilience
- **xUnit**: Comprehensive test coverage

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

## 🔄 CI/CD Pipeline

This project uses **GitHub Actions** for automated continuous integration and deployment. The pipeline includes:

- ✅ **Automated Build** - Compiles solution on every push/PR
- ✅ **Code Quality** - Enforces consistent formatting with `dotnet format`
- ✅ **Automated Tests** - Runs unit and integration tests with coverage reporting
- ✅ **Azure Deployment** - Deploys to Azure App Service on `main` branch
- ✅ **PR Validation** - Fast feedback for pull requests

### Quick Setup

1. **Configure GitHub Secrets** - See [docs/SETUP_GITHUB_SECRETS.md](./docs/SETUP_GITHUB_SECRETS.md) for step-by-step guide
2. **Push to Main** - Triggers full CI/CD pipeline with deployment
3. **Create PR** - Triggers validation (build + tests) without deployment

### Documentation

- [CI/CD Pipeline Guide](./docs/CICD.md) - Comprehensive workflow documentation
- [GitHub Secrets Setup](./docs/SETUP_GITHUB_SECRETS.md) - Azure credentials configuration
- [Telemetry Guide](./docs/TELEMETRY.md) - OpenTelemetry observability

## � Documentation

Comprehensive documentation is available to help you understand, use, and contribute to PoRepoLineTracker:

### For Users
- **[README](./README.md)** (this file) - Project overview and quick start guide
- **[Product Requirements](./PRD.md)** - Detailed product specifications and requirements

### For Developers
- **[Architecture Guide](./docs/ARCHITECTURE.md)** - System design, patterns, and technical decisions
- **[API Documentation](./docs/API.md)** - Complete API reference with examples
- **[Developer Onboarding](./docs/DEVELOPER_ONBOARDING.md)** - Step-by-step guide for new developers
- **[Contributing Guidelines](./CONTRIBUTING.md)** - How to contribute to the project
- **[Development Standards](./agents.md)** - Coding standards and best practices

### For DevOps
- **[CI/CD Pipeline](./docs/CICD.md)** - GitHub Actions workflows and deployment
- **[GitHub Secrets Setup](./docs/SETUP_GITHUB_SECRETS.md)** - Azure credentials configuration
- **[Telemetry Guide](./docs/TELEMETRY.md)** - OpenTelemetry monitoring and observability

### For Testers
- **[Test Documentation](./tests/README.md)** - Testing strategy and test execution
- **[E2E Test Guide](./tests/PoRepoLineTracker.E2ETests/README.md)** - Playwright browser testing

### Quick Links
| Document | Description | Audience |
|----------|-------------|----------|
| [ARCHITECTURE.md](./docs/ARCHITECTURE.md) | System architecture and design patterns | Developers |
| [API.md](./docs/API.md) | API endpoints and examples | Developers, Integrators |
| [DEVELOPER_ONBOARDING.md](./docs/DEVELOPER_ONBOARDING.md) | New developer setup guide | New Team Members |
| [CONTRIBUTING.md](./CONTRIBUTING.md) | Contribution guidelines | Contributors |
| [CICD.md](./docs/CICD.md) | CI/CD pipeline documentation | DevOps |
| [TELEMETRY.md](./docs/TELEMETRY.md) | Observability and monitoring | SRE, Developers |

## �🚀 Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Node.js](https://nodejs.org/) (for Azurite)
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

#### 4. Alternative: Local Configuration File (Development)
If you prefer file-based configuration instead of user secrets:
```powershell
# Navigate to the API project
cd src\PoRepoLineTracker.Api

# Create a local settings file (this file is gitignored)
# Create appsettings.Development.local.json with your secrets:
```
```json
{
  "GitHub": {
    "PAT": "your_github_personal_access_token_here"
  }
}
```
**Note**: Never commit files containing actual secrets to version control. The `appsettings.Development.local.json` file is automatically ignored by git.

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
# https://localhost:5001 (HTTPS) or http://localhost:5000 (HTTP)
```

**Quick Start (One Command)**:
```powershell
# Start Azurite in background, then run the app
azurite --silent --location c:\azurite --debug c:\azurite\debug.log & dotnet run --project src/PoRepoLineTracker.Api
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
- Application health diagnostics at `/health`

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
