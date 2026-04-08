using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.Application.Features.Repositories.Commands;
using PoRepoLineTracker.Application.Interfaces;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.UnitTests;

/// <summary>
/// Tests for AddRepositoryCommandHandler to ensure a repository is persisted
/// and the correct entity is returned.
/// </summary>
public class AddRepositoryCommandHandlerTests
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly ILogger<AddRepositoryCommandHandler> _logger = Substitute.For<ILogger<AddRepositoryCommandHandler>>();
    private readonly AddRepositoryCommandHandler _sut;

    public AddRepositoryCommandHandlerTests()
    {
        _sut = new AddRepositoryCommandHandler(_dataService, _logger);
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsRepositoryAndReturnsIt()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var command = new AddRepositoryCommand("testowner", "testrepo", "https://github.com/testowner/testrepo.git", userId);

        // Act
        var result = await _sut.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Owner.Should().Be("testowner");
        result.Name.Should().Be("testrepo");
        result.CloneUrl.Should().Be("https://github.com/testowner/testrepo.git");
        result.UserId.Should().Be(userId);
        result.Id.Should().NotBeEmpty();

        await _dataService.Received(1).AddRepositoryAsync(
            Arg.Is<GitHubRepository>(r =>
                r.Owner == "testowner" &&
                r.Name == "testrepo" &&
                r.CloneUrl == "https://github.com/testowner/testrepo.git" &&
                r.UserId == userId));
    }

    [Fact]
    public async Task Handle_WhenDataServiceThrows_ExceptionPropagates()
    {
        // Arrange
        var command = new AddRepositoryCommand("owner", "repo", "https://github.com/owner/repo.git", Guid.NewGuid());
        _dataService
            .When(s => s.AddRepositoryAsync(Arg.Any<GitHubRepository>()))
            .Do(_ => throw new InvalidOperationException("Storage unavailable"));

        // Act
        var act = async () => await _sut.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Storage unavailable");
    }
}

