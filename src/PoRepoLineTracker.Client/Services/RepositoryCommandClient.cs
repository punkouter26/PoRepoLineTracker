using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Shared.Domain;
using Radzen;

namespace PoRepoLineTracker.Client.Services;

/// <summary>
/// The destructive repository commands — delete one, delete all, queue a re-analysis — with their
/// confirmation prompt, their toast and their logging in one place.
///
/// <para><b>What this replaces.</b> Repositories.razor carried three near-identical blocks: call the
/// API, branch on the status code, raise a success toast or an error toast, log the outcome. Around
/// forty lines each, differing only in the URL and the wording. Three copies of an error path is
/// three chances for one of them to quietly stop notifying — and a delete that silently fails looks
/// exactly like a delete that worked until the page is reloaded.</para>
///
/// <para><b>What it deliberately does NOT own.</b> Page state. Removing the row from the grid,
/// tracking which repository is mid-reanalysis, and restarting the poll all stay on the page,
/// because they are about what the user is looking at rather than about the command. This returns a
/// plain bool and lets the caller decide what to redraw.</para>
/// </summary>
public sealed class RepositoryCommandClient(
    HttpClient http,
    DialogService dialogService,
    NotificationService notificationService,
    ILogger<RepositoryCommandClient> logger)
{
    /// <summary>Radzen's Confirm returns bool? — null when dismissed, which is not consent.</summary>
    public async Task<bool> ConfirmAsync(string message, string title, string okButtonText) =>
        await dialogService.Confirm(message, title,
            new ConfirmOptions { OkButtonText = okButtonText, CancelButtonText = "Cancel" }) == true;

    public Task<bool> ConfirmDeleteAsync(GitHubRepository repo) => ConfirmAsync(
        $"Are you sure you want to delete '{repo.Owner}/{repo.Name}'? This will remove all associated data.",
        "Delete Repository", "Delete");

    public Task<bool> ConfirmRemoveAllAsync() => ConfirmAsync(
        "Are you sure you want to remove ALL repositories? This action cannot be undone.",
        "Remove All Repositories", "Remove All");

    public Task<bool> ConfirmReanalyzeAsync(GitHubRepository repo) => ConfirmAsync(
        $"Re-analyze '{repo.Owner}/{repo.Name}' from scratch?\n\nThis will delete all existing commit data "
        + "and re-analyze the entire repository using your current file extension settings. This may take "
        + "some time for repositories with many commits.",
        "Re-analyze Repository", "Re-analyze");

    public Task<bool> DeleteAsync(RepositoryId repositoryId, string label) =>
        SendAsync(() => http.DeleteAsync($"/api/repositories/{repositoryId}"),
            successSummary: "Deleted",
            successDetail: $"Repository '{label}' deleted successfully!",
            failureVerb: "delete repository",
            context: repositoryId.ToString());

    public Task<bool> RemoveAllAsync() =>
        SendAsync(() => http.DeleteAsync("/api/repositories/all"),
            successSummary: "Success",
            successDetail: "All repositories and data have been successfully removed!",
            failureVerb: "remove all repositories",
            context: "all");

    public Task<bool> QueueReanalysisAsync(RepositoryId repositoryId, string label) =>
        SendAsync(() => http.PostAsync($"/api/repositories/{repositoryId}/reanalyze", content: null),
            successSummary: "Re-analysis started",
            successDetail: $"'{label}' is being re-analyzed. Progress will appear in the grid.",
            failureVerb: "start re-analysis",
            context: repositoryId.ToString());

    /// <summary>
    /// The one copy of call-branch-notify-log.
    ///
    /// <para>A thrown exception and a non-success status are reported identically to the user on
    /// purpose: both mean the command did not happen, and the distinction between "the server said
    /// no" and "the request never arrived" is not one the person clicking Delete can act on. The
    /// log keeps the difference.</para>
    /// </summary>
    private async Task<bool> SendAsync(
        Func<Task<HttpResponseMessage>> send,
        string successSummary,
        string successDetail,
        string failureVerb,
        string context)
    {
        try
        {
            var response = await send();

            if (response.IsSuccessStatusCode)
            {
                Notify(NotificationSeverity.Success, successSummary, successDetail, 4000);
                logger.LogInformation("{Verb} succeeded for {Context}", failureVerb, context);
                return true;
            }

            Notify(NotificationSeverity.Error, "Error", $"Failed to {failureVerb}: {response.StatusCode}", 6000);
            logger.LogError("Failed to {Verb} for {Context}. Status: {StatusCode}",
                failureVerb, context, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            Notify(NotificationSeverity.Error, "Error", $"Error trying to {failureVerb}: {ex.Message}", 6000);
            logger.LogError(ex, "Error during {Verb} for {Context}", failureVerb, context);
            return false;
        }
    }

    private void Notify(NotificationSeverity severity, string summary, string detail, double duration) =>
        notificationService.Notify(new NotificationMessage
        {
            Severity = severity,
            Summary = summary,
            Detail = detail,
            Duration = duration
        });
}
