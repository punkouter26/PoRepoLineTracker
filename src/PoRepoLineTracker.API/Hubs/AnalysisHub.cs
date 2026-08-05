using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace PoRepoLineTracker.API.Hubs;

/// <summary>
/// Pushes live analysis progress to the browser.
///
/// <para><b>Why this replaces polling.</b> The Repositories page previously asked
/// <c>/analysis-progress</c> for every pending repository on a five-second timer, plus a full
/// <c>/api/repositories</c> read on each tick — so a page with ten pending repositories issued
/// eleven requests every five seconds and still showed each step up to five seconds late. The
/// server already knows the instant a step changes; this lets it say so.</para>
///
/// <para><b>Isolation.</b> Connections are placed in a group derived from the authenticated
/// principal — never from anything the client sends. A caller therefore cannot subscribe to
/// another user's jobs by guessing a repository id, which is the failure mode a
/// client-supplied "subscribe to this repo" method would have had to defend against on every
/// call.</para>
/// </summary>
[Authorize]
public sealed class AnalysisHub : Hub
{
    /// <summary>
    /// Group name for a user's own jobs. Kept here rather than at the broadcast site so the
    /// producer and the consumer of the name cannot drift apart.
    /// </summary>
    public static string GroupFor(UserId userId) => $"user:{userId}";

    /// <summary>Name of the client-side handler; referenced by <c>AnalysisProgressService</c>.</summary>
    public const string ProgressMethod = "AnalysisProgress";

    public override async Task OnConnectedAsync()
    {
        if (Context.User is not null && Context.User.TryGetUserId(out var userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(userId));
        }
        else
        {
            // [Authorize] guarantees an identity, but not that it carries the claim this app keys
            // on. Aborting is the safe reading: leaving the connection open would produce a client
            // that looks connected and silently receives nothing.
            Context.Abort();
            return;
        }

        await base.OnConnectedAsync();
    }
}
