using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Client.Models;

public class RepositoryPageState
{
    public string NewRepoCloneUrl { get; set; } = string.Empty;
    public string AddRepoMessage { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool IsLoadingRepositories { get; set; } = false;
    public bool IsAddingRepository { get; set; } = false;
    public bool IsLoadingGitHubRepos { get; set; } = false;
    public string ProgressMessage { get; set; } = string.Empty;
    public int ProgressPercentage { get; set; } = 0;
    public List<GitHubRepository> Repositories { get; set; } = new();
    public List<GitHubUserRepositoryDto> GitHubUserRepositories { get; set; } = new();
    public HashSet<GitHubUserRepositoryDto> SelectedRepositories { get; set; } = new();
    public bool ShowRepositorySelector { get; set; } = false;
    public HashSet<Guid> ShowAllCommitsFor { get; set; } = new();

    public void ClearMessages()
    {
        AddRepoMessage = string.Empty;
        ErrorMessage = string.Empty;
        ProgressMessage = string.Empty;
        ProgressPercentage = 0;
    }

    public void ClearSelection()
    {
        SelectedRepositories.Clear();
        ShowRepositorySelector = false;
    }

    public void SetProgress(string message, int percentage)
    {
        ProgressMessage = message;
        ProgressPercentage = percentage;
    }
}
