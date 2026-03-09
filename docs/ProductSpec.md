# Product Specification — PoRepoLineTracker

## 1. Problem Statement

Software teams and independent developers lack a lightweight, self-hosted tool to visualise how their codebases grow over time. Existing solutions (SonarQube, GitHub Insights) either require expensive infrastructure, expose data to third parties, or provide only point-in-time snapshots. Developers want to:

- Track total lines of code (LOC) per repository across every commit.
- Break down LOC by file extension (`.cs`, `.ts`, `.razor`, …).
- Identify which commits drove the largest code volume changes.
- Own their data — no vendor lock-in, no third-party analytics platform.

---

## 2. Goals & Non-Goals

### Goals
| # | Goal | Success Metric |
|---|------|---------------|
| G1 | GitHub OAuth authentication — 2 clicks to sign in | OAuth round-trip completes in < 3 s on a standard connection |
| G2 | First insight visible after adding a repository | Time-to-first-insight ≤ 5 min for repos ≤ 2 000 commits |
| G3 | Historic LOC trend visible as a time-series chart | Chart renders data for all tracked commits |
| G4 | Per-extension LOC breakdown | Correct % breakdown comparing `.cs`, `.ts`, `.razor`, etc. |
| G5 | Zero data loss — failed analyses are recorded and retryable | All `FailedOperation` records surface in the UI with full error detail |
| G6 | Self-hosted, data stays in user-owned Azure Table Storage | No data sent to external analytics services beyond App Insights (user-controlled) |

### Non-Goals
- Real-time commit webhooks (polling/manual re-analysis only in v1).
- Multi-tenant SaaS hosted by the product team.
- PR-level or file-level diff views.
- Non-GitHub VCS (GitLab, Bitbucket) in v1.

---

## 3. Users

**Primary Persona — Independent Developer / Tech Lead**
- Maintains 3–20 GitHub repositories.
- Wants a personal productivity metric dashboard.
- Uses the app weekly to check growth trends.

**Technical Requirements:**
- Has a GitHub account.
- Has (or can create) an Azure subscription with Table Storage and Key Vault.
- Comfortable running `azd up` once to deploy.

---

## 4. Feature Inventory (v1)

### 4.1 Authentication
- GitHub OAuth 2.0 via `AspNet.Security.OAuth.GitHub`.  
- Auth cookie (`PoRepoLineTracker.Auth`): HttpOnly, Secure, 7-day sliding expiration.  
- User record (Id, GitHubId, Username, DisplayName, Email, AvatarUrl, AccessToken) persisted in `PoRepoLineTrackerUsers` table.

### 4.2 Repository Management
- **Add repositories** — single or bulk add from the authenticated user's GitHub repo list.  
- **Delete repository** — removes entry from Table Storage and local clone.  
- **Re-analyse** — re-runs the commit processing pipeline from the last analysed commit date.  
- Grid UI with search, sorting, and status badges ("Analysed", "Pending", "Error").

### 4.3 Commit Analysis Pipeline
- Clones repository to temporary local path via **LibGit2Sharp**.  
- Iterates commits in reverse-chronological order.  
- Per commit: computes diff, filters by `UserPreferences.FileExtensions`, counts added/removed/total lines via `LineCounter`.  
- Persists each `CommitLineCount` to `PoRepoLineTrackerCommitLineCounts` table.  
- Failures recorded as `FailedOperation` with full `ErrorMessage`, `StackTrace`, `RetryCount`, and `ContextData`.

### 4.4 Line Count Visualisation
- **Time-series chart** (Radzen `RadzenChart`): total lines over time for selected repository (last 365 days).  
- **File Extension breakdown** tab: % share per extension as a stacked/pie view.  
- **Top Files** tab: top 5 files by line count.

### 4.5 Failed Operations
- Dedicated `/failed-operations` page listing all failed pipeline runs across all repos.  
- Columns: Repository, OperationType, ErrorMessage, FailedAt, RetryCount.  
- Delete action for clearing resolved records.

### 4.6 User Preferences
- `/settings/extensions-counted` page.  
- Configurable list of file extensions to include in analysis.  
- Defaults: `.cs .razor .cshtml .xaml .js .jsx .ts .tsx .html .css .scss .less`  
- Persisted in `UserPreferences` (keyed by `UserId`).

---

## 5. Non-Functional Requirements

| Category | Requirement |
|----------|-------------|
| Performance | p95 analysis latency ≤ 30 s per commit batch (50 commits) |
| Scalability | Single-user deployment; horizontal scale not required in v1 |
| Security | HTTPS only (`httpsOnly: true` on App Service). Auth cookie HttpOnly + Secure. Secrets via Key Vault + Managed Identity. OWASP Top 10 mitigated. |
| Availability | Azure App Service B1/S1 — best-effort SLA |
| Observability | Structured logs via Serilog → App Insights + rolling log file. OpenTelemetry traces via App Insights exporter. |
| Data Residency | All user data in Azure Table Storage within user's own subscription |

---

## 6. Key User Flows

### Happy Path — Add & Analyse a Repository
1. User signs in via GitHub OAuth (**≤ 2 clicks**).  
2. User clicks "Add Repository" → selects from GitHub repo list.  
3. API clones repo, analyses all commits, writes `CommitLineCount` rows.  
4. Repositories grid shows "Analysed" badge.  
5. User expands row → line chart renders historic LOC trend.

### Failure Recovery
1. Analysis fails → `FailedOperation` record written.  
2. User navigates to Failed Operations page → sees error detail.  
3. User resolves root cause (e.g. bad clone URL) → deletes failed-op record.  
4. User triggers "Re-analyse" on the repository.

---

## 7. Known Limitations (v1)

- Only public or token-accessible GitHub repositories (PAT stored in Key Vault).  
- Local disk space required for clones (no streaming analysis yet).  
- No real-time commit detection (manual re-analysis trigger only).  
- No multi-user support — each deployment is for a single GitHub account.
