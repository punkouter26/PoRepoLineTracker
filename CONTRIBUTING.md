# Contributing to PoRepoLineTracker

Thank you for your interest in contributing to PoRepoLineTracker! This document provides guidelines and best practices for contributing to the project.

## Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Process](#development-process)
- [Coding Standards](#coding-standards)
- [Testing Requirements](#testing-requirements)
- [Pull Request Process](#pull-request-process)
- [Issue Guidelines](#issue-guidelines)
- [Documentation](#documentation)

---

## Code of Conduct

### Our Pledge

We are committed to providing a welcoming and inclusive environment for all contributors. We expect everyone to:

- ✅ Be respectful and professional
- ✅ Welcome newcomers and help them learn
- ✅ Accept constructive feedback gracefully
- ✅ Focus on what is best for the project and community
- ❌ No harassment, discrimination, or inappropriate behavior

---

## Getting Started

### Prerequisites

Before contributing, ensure you have:

1. **Environment Setup** - See [Developer Onboarding Guide](./DEVELOPER_ONBOARDING.md)
2. **Git Configuration:**
   ```bash
   git config user.name "Your Name"
   git config user.email "your.email@example.com"
   ```
3. **GitHub Account** - [Sign up](https://github.com/signup) if you don't have one

### Finding Issues to Work On

- **Good First Issues:** [github.com/punkouter26/PoRepoLineTracker/issues?q=label%3A"good+first+issue"](https://github.com/punkouter26/PoRepoLineTracker/issues?q=label%3A%22good+first+issue%22)
- **Help Wanted:** [github.com/punkouter26/PoRepoLineTracker/issues?q=label%3A"help+wanted"](https://github.com/punkouter26/PoRepoLineTracker/issues?q=label%3A%22help+wanted%22)
- **Bugs:** [github.com/punkouter26/PoRepoLineTracker/issues?q=label%3Abug](https://github.com/punkouter26/PoRepoLineTracker/issues?q=label%3Abug)

**Before starting work:**
1. Comment on the issue to let others know you're working on it
2. Wait for approval from maintainers (for larger features)
3. Fork the repository

---

## Development Process

### 1. Fork and Clone

```bash
# Fork the repository on GitHub, then:
git clone https://github.com/YOUR-USERNAME/PoRepoLineTracker.git
cd PoRepoLineTracker

# Add upstream remote
git remote add upstream https://github.com/punkouter26/PoRepoLineTracker.git
```

### 2. Create a Branch

```bash
# Update your main branch
git checkout main
git pull upstream main

# Create feature branch
git checkout -b feature/your-feature-name

# Or for bug fixes
git checkout -b fix/bug-description
```

**Branch Naming Convention:**
- `feature/` - New features
- `fix/` - Bug fixes
- `docs/` - Documentation changes
- `refactor/` - Code refactoring
- `test/` - Test additions/improvements

### 3. Make Changes

Follow our [Coding Standards](#coding-standards) and development agent rules in `agents.md`.

### 4. Test Your Changes

```bash
# Format code
dotnet format

# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter "FullyQualifiedName~UnitTests"
dotnet test --filter "FullyQualifiedName~IntegrationTests"

# Generate coverage
dotnet test --collect:"XPlat Code Coverage" --settings .runsettings
```

### 5. Commit Changes

```bash
git add .
git commit -m "type: description"
```

**Commit Message Format:**

```
type(scope): short description

Longer description if needed (optional)

Fixes #123
```

**Types:**
- `feat:` - New feature
- `fix:` - Bug fix
- `docs:` - Documentation changes
- `style:` - Code style changes (formatting)
- `refactor:` - Code refactoring
- `test:` - Test additions or modifications
- `chore:` - Build process or tooling changes

**Examples:**
```bash
git commit -m "feat(repositories): add bulk import functionality"
git commit -m "fix(api): correct line count calculation for empty files"
git commit -m "docs: update API documentation with new endpoints"
git commit -m "test: add integration tests for GitHub service"
```

### 6. Push Changes

```bash
git push origin feature/your-feature-name
```

### 7. Create Pull Request

1. Go to your fork on GitHub
2. Click "Compare & pull request"
3. Fill in the PR template (see below)
4. Submit the pull request

---

## Coding Standards

### General Principles

We follow the development standards defined in `agents.md`. Key principles:

1. **Vertical Slice Architecture** - Organize by feature, not layer
2. **SOLID Principles** - Write clean, maintainable code
3. **Test-Driven Development (TDD)** - Write tests first (Red → Green → Refactor)
4. **Mobile-First Design** - UI must work on mobile devices

### .NET / C# Standards

#### Code Formatting

**Automated:** Use `dotnet format` before committing

```bash
dotnet format
```

**Manual Rules:**
- Use 4 spaces for indentation (no tabs)
- Max line length: 120 characters
- Place braces on new line (Allman style)
- Use `var` for implicitly typed local variables

#### Naming Conventions

```csharp
// Classes and Methods: PascalCase
public class RepositoryService { }
public void AnalyzeRepository() { }

// Private fields: _camelCase
private readonly ILogger _logger;

// Local variables and parameters: camelCase
var repositoryId = Guid.NewGuid();
public void ProcessData(string inputData) { }

// Constants: PascalCase
public const int MaxRetryAttempts = 3;

// Interfaces: IPascalCase
public interface IRepositoryStorage { }
```

#### Code Organization

**File Structure:**
```csharp
using System;
using System.Collections.Generic;
using ThirdParty.Namespace;
using PoRepoLineTracker.Application.Models;

namespace PoRepoLineTracker.Application.Features.Repositories;

public class RepositoryService
{
    // 1. Private fields
    private readonly ILogger<RepositoryService> _logger;
    
    // 2. Constructors
    public RepositoryService(ILogger<RepositoryService> logger)
    {
        _logger = logger;
    }
    
    // 3. Public methods
    public async Task<Repository> GetByIdAsync(Guid id) { }
    
    // 4. Private methods
    private bool ValidateRepository(Repository repo) { }
}
```

### Blazor / Razor Standards

```razor
@* Good: Single responsibility component *@
<div class="repository-card">
    <h3>@Repository.Name</h3>
    <p>@Repository.Description</p>
</div>

@code {
    [Parameter]
    public Repository Repository { get; set; } = default!;
}
```

**Component Guidelines:**
- Keep components small and focused
- Use `@code` block at bottom of file
- Extract reusable logic to services
- Use parameters for component inputs

---

## Testing Requirements

All contributions must include appropriate tests.

### Unit Tests (Required)

**Location:** `tests/PoRepoLineTracker.UnitTests`

**Coverage:** All new business logic

**Example:**
```csharp
using FluentAssertions;
using NSubstitute;
using Xunit;

public class RepositoryServiceTests
{
    [Fact]
    public async Task GetByIdAsync_WhenRepositoryExists_ReturnsRepository()
    {
        // Arrange
        var mockStorage = Substitute.For<IRepositoryStorage>();
        var expectedRepo = new Repository { Id = Guid.NewGuid() };
        mockStorage.GetByIdAsync(Arg.Any<Guid>()).Returns(expectedRepo);
        var service = new RepositoryService(mockStorage);
        
        // Act
        var result = await service.GetByIdAsync(expectedRepo.Id);
        
        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(expectedRepo.Id);
    }
}
```

### Integration Tests (Recommended)

**Location:** `tests/PoRepoLineTracker.IntegrationTests`

**Purpose:** Test API endpoints with real dependencies (Testcontainers)

**Example:**
```csharp
public class RepositoryApiTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;
    
    public RepositoryApiTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }
    
    [Fact]
    public async Task AddRepository_ReturnsCreated()
    {
        // Arrange
        var client = _fixture.CreateClient();
        var request = new { cloneUrl = "https://github.com/octocat/Hello-World.git" };
        
        // Act
        var response = await client.PostAsJsonAsync("/api/repositories", request);
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
```

### Test Guidelines

✅ **DO:**
- Use FluentAssertions for readable assertions
- Use NSubstitute for mocking
- Follow Arrange-Act-Assert pattern
- Test both success and failure paths
- Use descriptive test names

❌ **DON'T:**
- Test implementation details
- Create tests with external dependencies (use mocks)
- Write tests that depend on execution order
- Commit commented-out tests

---

## Pull Request Process

### PR Template

When creating a PR, use this template:

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Testing
- [ ] Unit tests added/updated
- [ ] Integration tests added/updated
- [ ] Manual testing completed

## Checklist
- [ ] Code follows project coding standards
- [ ] All tests pass locally
- [ ] Documentation updated (if applicable)
- [ ] No new warnings introduced
- [ ] Branch is up to date with main

## Related Issues
Fixes #123
```

### Review Process

1. **Automated Checks:**
   - ✅ Build succeeds
   - ✅ All tests pass
   - ✅ Code formatting verified
   - ✅ No new errors/warnings

2. **Code Review:**
   - At least one approval required
   - Address all reviewer feedback
   - Keep PR scope focused

3. **Merge:**
   - Squash and merge (preferred)
   - Delete branch after merge

### PR Size Guidelines

**Ideal PR size:**
- **Small:** <100 lines changed (✅ Preferred)
- **Medium:** 100-500 lines changed (⚠️ Acceptable)
- **Large:** >500 lines changed (❌ Discouraged - consider splitting)

**Large PRs require:**
- Detailed description
- Multiple reviewers
- Comprehensive tests

---

## Issue Guidelines

### Creating Issues

**Good Issue Template:**

```markdown
## Description
Clear description of the issue or feature request

## Steps to Reproduce (for bugs)
1. Go to '...'
2. Click on '...'
3. See error

## Expected Behavior
What should happen

## Actual Behavior
What actually happens

## Environment
- OS: Windows 11
- .NET Version: 9.0.306
- Browser: Chrome 120 (if applicable)

## Screenshots (if applicable)
[Attach screenshots]

## Additional Context
Any other relevant information
```

### Issue Labels

| Label | Purpose |
|-------|---------|
| `bug` | Something isn't working |
| `feature` | New feature request |
| `documentation` | Documentation improvements |
| `good first issue` | Good for newcomers |
| `help wanted` | Extra attention needed |
| `priority: high` | Critical issues |
| `priority: low` | Nice to have |

---

## Documentation

### When to Update Documentation

Update documentation when you:
- Add new features
- Change API endpoints
- Modify configuration
- Add dependencies
- Change deployment process

### Documentation Locations

| Type | Location |
|------|----------|
| Project Overview | `README.md` |
| Architecture | `docs/ARCHITECTURE.md` |
| API Reference | `docs/API.md` |
| Development Guide | `docs/DEVELOPER_ONBOARDING.md` |
| CI/CD | `docs/CICD.md` |
| Telemetry | `docs/TELEMETRY.md` |

### Documentation Standards

**Good Documentation:**
- ✅ Clear and concise
- ✅ Includes examples
- ✅ Up to date
- ✅ Covers common use cases
- ✅ Links to related docs

**Bad Documentation:**
- ❌ Vague or ambiguous
- ❌ No examples
- ❌ Outdated information
- ❌ Missing edge cases

---

## Security

### Reporting Security Issues

**DO NOT** create public issues for security vulnerabilities.

Instead:
1. Email security concerns to: [security contact]
2. Include detailed description
3. Provide steps to reproduce
4. Allow time for fix before disclosure

### Security Guidelines

- Never commit secrets or credentials
- Use user secrets for local development
- Use environment variables for production
- Sanitize user input
- Follow principle of least privilege

---

## Recognition

Contributors are recognized in:
- **GitHub Contributors Page**
- **Release Notes** (for significant contributions)
- **Project README** (top contributors)

---

## Questions?

- **General Questions:** Open a GitHub Discussion
- **Bug Reports:** Create an issue
- **Feature Requests:** Create an issue with `feature` label
- **Security:** Email [security contact]

---

## License

By contributing, you agree that your contributions will be licensed under the same license as the project (MIT License).

---

Thank you for contributing to PoRepoLineTracker! 🎉

