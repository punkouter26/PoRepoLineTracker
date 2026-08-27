using MediatR;
using Microsoft.Extensions.Logging;

namespace PoRepoLineTracker.API.Features.CodeHealth;

/// <summary>
/// Measures one repository's source at its most recent analysed commit.
/// </summary>
public record GetCodeHealthQuery(RepositoryId RepositoryId) : IRequest<CodeHealthDto?>;

/// <summary>
/// <para><b>Why this reads the working clone rather than stored data.</b> Every other query in the
/// app answers from Azure Table Storage, because what it needs — line totals per commit — is what
/// analysis wrote there. This one needs the source text itself, which is not stored anywhere: the
/// analyser reads blobs, counts lines and keeps only the counts. Re-deriving the text from stored
/// figures is impossible, so the report is computed on demand from the local clone.</para>
///
/// <para><b>And why only the newest commit.</b> Health is a statement about the code as it stands.
/// Measuring every commit would multiply the work by the length of the history to produce a trend
/// nobody asked for, and the trend that IS wanted — is the repository growing — is already on the
/// charts.</para>
///
/// <para>Returns null when the repository is unknown or has never been analysed, which the
/// endpoint turns into a 404: a health report for a repository with no commits on record would be
/// a page of zeroes indistinguishable from genuinely terrible code.</para>
/// </summary>
public sealed class GetCodeHealthQueryHandler(
    IRepositoryDataService repositoryDataService,
    IGitHubService gitHubService,
    IUserPreferencesService userPreferencesService,
    ILogger<GetCodeHealthQueryHandler> logger)
    : IRequestHandler<GetCodeHealthQuery, CodeHealthDto?>
{
    public async Task<CodeHealthDto?> Handle(GetCodeHealthQuery request, CancellationToken cancellationToken)
    {
        var repository = await repositoryDataService.GetRepositoryByIdAsync(request.RepositoryId);
        if (repository is null) return null;

        var commits = (await repositoryDataService.GetCommitLineCountsByRepositoryIdAsync(request.RepositoryId)).ToList();
        if (commits.Count == 0)
        {
            logger.LogInformation("Code health for {RepositoryId}: no analysed commits", request.RepositoryId);
            return null;
        }

        // The newest commit, on the same definition RepositoryTotals uses for "now" — never a
        // window, never a sum.
        var newest = commits.MaxBy(c => c.CommitDate)!;

        var extensions = repository.UserId != UserId.Empty
            ? await userPreferencesService.GetFileExtensionsAsync(repository.UserId)
            : UserPreferences.DefaultFileExtensions;

        var repositoryPath = gitHubService.ResolveRepositoryPath(repository.LocalPath);

        // One Task.Run around the whole walk, not one per file. The enumeration is synchronous
        // LibGit2Sharp work plus regex matching — CPU-bound from end to end — and it holds the git
        // repository open for its duration, so it has to run as a single unit.
        var report = await Task.Run(() =>
        {
            var analyzer = new CodeMetricsAnalyzer();
            var metrics = gitHubService
                .EnumerateSourceFiles(repositoryPath, newest.CommitSha, extensions)
                .Select(file => analyzer.Analyze(file.Path, file.Extension, file.Content))
                .ToList();

            return CodeHealthScoring.Build(metrics);
        }, cancellationToken);

        report.RepositoryId = repository.Id;
        report.Owner = repository.Owner;
        report.Name = repository.Name;
        report.CommitSha = newest.CommitSha.Length > 7 ? newest.CommitSha[..7] : newest.CommitSha;
        report.CommitDate = newest.CommitDate;

        logger.LogInformation(
            "Code health for {Owner}/{Name}: {Score} ({Grade}) over {Files} files",
            repository.Owner, repository.Name, report.Score, report.Grade, report.FilesAnalyzed);

        return report;
    }
}
