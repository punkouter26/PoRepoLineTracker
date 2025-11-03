# Testing Strategy

## Overview

This solution follows a comprehensive testing strategy with three layers of testing:
- **Unit Tests**: Fast, isolated tests of business logic
- **Integration Tests**: Tests with real dependencies (Azure Table Storage via Testcontainers)
- **E2E Tests**: Full browser automation tests using Playwright

## Test Projects

### PoRepoLineTracker.UnitTests
- **Framework**: xUnit
- **Mocking**: NSubstitute
- **Assertions**: FluentAssertions
- **Purpose**: Test business logic in isolation
- **Speed**: Very fast (<1ms per test typical)

### PoRepoLineTracker.IntegrationTests
- **Framework**: xUnit
- **Infrastructure**: Testcontainers (Azurite for Azure Table Storage)
- **Assertions**: FluentAssertions
- **Purpose**: Test API endpoints and data access with real storage
- **Isolation**: Each test run gets a fresh Azurite container
- **Speed**: Fast (~100ms per test typical)

### PoRepoLineTracker.E2ETests
- **Framework**: NUnit + Playwright
- **Purpose**: Full browser automation testing
- **Speed**: Slower (~1-5s per test typical)

## Running Tests

### All Tests
```powershell
dotnet test
```

### Unit Tests Only
```powershell
dotnet test --filter "FullyQualifiedName~UnitTests"
```

### Integration Tests Only
```powershell
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

### E2E Tests Only
```powershell
dotnet test --filter "FullyQualifiedName~E2ETests"
```

### With Code Coverage
```powershell
dotnet test --collect:"XPlat Code Coverage" --settings .runsettings
```

## Code Coverage

Coverage reports are generated in the `TestResults` directory. To view coverage:

1. Install ReportGenerator tool (one-time):
   ```powershell
   dotnet tool install -g dotnet-reportgenerator-globaltool
   ```

2. Generate coverage report:
   ```powershell
   dotnet test --collect:"XPlat Code Coverage" --settings .runsettings
   reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"docs/coverage" -reporttypes:Html
   ```

3. Open `docs/coverage/index.html` in browser

## Test Isolation Principles

### Unit Tests
- No external dependencies (database, file system, network)
- Use NSubstitute to mock interfaces
- Tests should be deterministic and repeatable

### Integration Tests
- Use Testcontainers.Azurite for isolated Azure Table Storage
- Each test run gets a fresh container
- No data persists between runs
- Tests are independent and can run in any order

### E2E Tests
- Use Playwright for browser automation
- Tests should clean up after themselves
- Use unique test data to avoid conflicts

## Best Practices

### Test Naming
Use descriptive test names following the pattern:
```csharp
[Fact]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    // Act
    // Assert
}
```

### Arrange-Act-Assert (AAA)
Structure all tests with clear AAA sections:
```csharp
[Fact]
public void AddRepository_WithValidData_ReturnsSuccess()
{
    // Arrange
    var repository = new Repository { Name = "test" };
    
    // Act
    var result = await service.AddRepositoryAsync(repository);
    
    // Assert
    result.Should().BeTrue();
}
```

### FluentAssertions
Use FluentAssertions for readable assertions:
```csharp
// Good
result.Should().BeTrue();
repository.Name.Should().Be("test");
list.Should().HaveCount(5);

// Avoid
Assert.True(result);
Assert.Equal("test", repository.Name);
Assert.Equal(5, list.Count);
```

### NSubstitute Mocking
Use NSubstitute for clean mocking:
```csharp
// Create mock
var mockService = Substitute.For<IMyService>();

// Setup return value
mockService.GetDataAsync().Returns(Task.FromResult(data));

// Verify method was called
await mockService.Received(1).GetDataAsync();
```

## CI/CD Integration

Tests run automatically in the CI/CD pipeline:
- **Build**: All tests must pass before deployment
- **Coverage**: Minimum coverage threshold enforced
- **Reports**: Coverage reports published as artifacts

## Troubleshooting

### Testcontainers Issues
If integration tests fail to start Azurite:
1. Ensure Docker Desktop is running
2. Check Docker Desktop has resources allocated (2GB+ RAM recommended)
3. Verify network connectivity

### Playwright Issues
If E2E tests fail:
1. Install Playwright browsers: `pwsh tests/PoRepoLineTracker.E2ETests/bin/Debug/net9.0/playwright.ps1 install`
2. Update Playwright: `dotnet add package Microsoft.Playwright.NUnit`

## Performance Targets

- **Unit Tests**: < 1s total for all unit tests
- **Integration Tests**: < 30s total
- **E2E Tests**: < 2 minutes total
- **Code Coverage**: > 70% line coverage
