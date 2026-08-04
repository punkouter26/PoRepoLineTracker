using System.Text.Json;
using System.Text.Json.Serialization;
using PoRepoLineTracker.Domain.Models;
using PoRepoLineTracker.Shared.Models;
using PoRepoLineTracker.Shared.Models.Dtos;

namespace PoRepoLineTracker.Shared.Serialization;

/// <summary>
/// Source-generated metadata for every type that crosses the wire (Rule 1.2 — zero-reflection
/// serialization across the API and the WASM client).
///
/// <para><b>Why it lives in Shared.</b> Both ends of every call deserialize the same contract, so
/// generating the metadata once in the leaf assembly means the API and the client are provably
/// using the same shape — and the WASM client never has to carry the reflection-based
/// serializer, which is what lets the published output be trimmed (Rule 1.2 / .Client csproj).</para>
///
/// <para><b>Options.</b> <see cref="JsonSerializerDefaults.Web"/> matches what both ASP.NET Core
/// minimal APIs and Blazor's <c>HttpClientJsonExtensions</c> use by default: camelCase names,
/// case-insensitive reads, and numbers readable from strings. Declaring it here rather than
/// relying on the ambient default keeps the two ends aligned even if a host default changes.</para>
///
/// <para><b>Adding a type.</b> Add a <c>[JsonSerializable]</c> line for the exact root type you
/// (de)serialize — the generator walks properties from there, but a <c>List&lt;T&gt;</c> root is
/// a different root than <c>T</c>, so both are listed where both are used. A missing entry does
/// not fail the build; it fails at runtime with "metadata for type ... was not provided", so
/// prefer adding the root eagerly.</para>
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]

// ─── Domain entities returned directly ──────────────────────────────────────────────────────
[JsonSerializable(typeof(GitHubRepository))]
[JsonSerializable(typeof(List<GitHubRepository>))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(UserPreferences))]
[JsonSerializable(typeof(CommitLineCount))]
[JsonSerializable(typeof(List<CommitLineCount>))]

// ─── Repository / chart DTOs ────────────────────────────────────────────────────────────────
[JsonSerializable(typeof(GitHubRepositoryDto))]
[JsonSerializable(typeof(List<GitHubRepositoryDto>))]
[JsonSerializable(typeof(GitHubUserRepositoryDto))]
[JsonSerializable(typeof(List<GitHubUserRepositoryDto>))]
[JsonSerializable(typeof(BulkRepositoryDto))]
[JsonSerializable(typeof(List<BulkRepositoryDto>))]
[JsonSerializable(typeof(IEnumerable<BulkRepositoryDto>))]
[JsonSerializable(typeof(BulkAddResult))]
[JsonSerializable(typeof(DailyLineCountDto))]
[JsonSerializable(typeof(List<DailyLineCountDto>))]
[JsonSerializable(typeof(RepositoryLineCountHistoryDto))]
[JsonSerializable(typeof(List<RepositoryLineCountHistoryDto>))]
[JsonSerializable(typeof(FileExtensionPercentageDto))]
[JsonSerializable(typeof(List<FileExtensionPercentageDto>))]
[JsonSerializable(typeof(TopFileDto))]
[JsonSerializable(typeof(List<TopFileDto>))]
[JsonSerializable(typeof(CommitStatsDto))]
[JsonSerializable(typeof(List<CommitStatsDto>))]

// ─── Contributor / AI-detection DTOs ────────────────────────────────────────────────────────
[JsonSerializable(typeof(ContributorStatsDto))]
[JsonSerializable(typeof(List<ContributorStatsDto>))]
[JsonSerializable(typeof(DailyContributorStatsDto))]
[JsonSerializable(typeof(List<DailyContributorStatsDto>))]
[JsonSerializable(typeof(AiDetectionStatsDto))]
[JsonSerializable(typeof(List<AiDetectionStatsDto>))]
[JsonSerializable(typeof(AiStatsByUserDto))]
[JsonSerializable(typeof(List<AiStatsByUserDto>))]
[JsonSerializable(typeof(UserAiPercentagePerCommitDto))]
[JsonSerializable(typeof(DailyAiDetectionDto))]
[JsonSerializable(typeof(List<DailyAiDetectionDto>))]

// ─── Progress, auth, diagnostics, upload, antiforgery ───────────────────────────────────────
[JsonSerializable(typeof(AnalysisProgressDto))]
[JsonSerializable(typeof(AuthResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(DiagnosticsResponse))]
[JsonSerializable(typeof(MaskedConfigurationResponse))]
[JsonSerializable(typeof(UploadResult))]
[JsonSerializable(typeof(UploadError))]
[JsonSerializable(typeof(AntiforgeryTokenResponse))]

// ─── Primitive collections persisted as JSON columns in Azure Table Storage ─────────────────
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(Dictionary<string, int>))]
[JsonSerializable(typeof(Dictionary<string, double>))]
[JsonSerializable(typeof(ChartDisplayMode))]
public sealed partial class AppJsonSerializerContext : JsonSerializerContext;
