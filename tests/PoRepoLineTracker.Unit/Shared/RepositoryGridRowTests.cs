using FluentAssertions;
using PoRepoLineTracker.Client.Models;

namespace PoRepoLineTracker.Unit.Shared;

/// <summary>
/// Locks the <see cref="RepositoryGridRow"/> contract: the four properties the
/// <c>RepositoriesGrid</c> columns bind to must keep their names and types, because they are
/// referenced by both the Radzen column templates (compile-time) and the sort/click
/// handlers (compile-time). A rename here is a wire-shape change at the grid level.
/// </summary>
public class RepositoryGridRowTests
{
    [Fact]
    public void RepositoryGridRow_exposes_Id_Owner_Name_LastAnalyzedCommitDate_and_TotalLinesSortValue()
    {
        var row = new RepositoryGridRow(
            Repository: default!,
            TotalLinesSortValue: 4_242);

        row.Id.Should().Be(default(RepositoryId));
        row.Owner.Should().Be(default(string));
        row.Name.Should().Be(default(string));
        row.LastAnalyzedCommitDate.Should().Be(default(DateTime?));
        row.TotalLinesSortValue.Should().Be(4_242);
    }
}
