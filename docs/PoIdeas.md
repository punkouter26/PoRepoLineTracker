# PoIdeas — PoRepoLineTracker Innovation Matrix

> **Generated:** 2026-06-01 | **Codebase:** Blazor WASM + ASP.NET Core 10 | **Storage:** Azure Table Storage | **AI:** Heuristic AI Detection | **Auth:** GitHub OAuth + Microsoft OAuth + GUEST

---

## Macro-Innovations (1–10): Foundational / Pivot-Worthy

### 1. 🤖 **AgentForge** — Multi-Agent Analysis Pipeline

| Field | Detail |
|-------|--------|
| **User Problem** | Current AI detection is a single-pass regex heuristic — no depth, no confidence scoring, no cross-referencing. Users can't trust the "AI %" number. |
| **Core Experience** | User clicks "Deep Analyze" → **Worker Agent** scans commits with heuristic + AST patterns → **Critic Agent** reviews Worker's output, flags false positives, adjusts scores → **Synthesis Agent** merges results into a confidence-banded report (High/Medium/Low certainty). User sees a trustworthy, layered AI-detection score instead of a single number. |
| **Implementation Path** | **Agentic Framework (MAF)** — Semantic Kernel with 3 planner agents. Worker uses existing `AiDetectionService` + new AST analyzer. Critic uses a small LLM via Azure AI Foundry. Synthesis merges into `CommitLineCount.AiConfidenceBand`. |
| **Value Metric** | AI detection accuracy ↑40%; user trust in AI score ↑ (measured by re-analysis rate ↓30%). |
| **Effort** | L | **Risk** | Medium |

---

### 2. 📡 **RepoRadar** — Cross-Repo Intelligence Swarm

| Field | Detail |
|-------|--------|
| **User Problem** | Each repo is analyzed in isolation. Users with 5+ repos can't see cross-cutting patterns (e.g., "AI code is creeping into all your repos simultaneously" or "Contributor X is active across 3 repos"). |
| **Core Experience** | User lands on Dashboard → sees **Radar View**: a radial chart with repos as nodes, edges showing shared contributors, AI-code hotspots, and dependency overlap. Clicking an edge reveals the shared intelligence. |
| **Implementation Path** | **No-API** — Merge `GetAllRepositoriesLineCountHistoryQuery` + `GetContributorStatsQuery` + `GetAiStatsByUserQuery` into a single cross-repo aggregation service. New `CrossRepoIntelligenceService` in Application layer. |
| **Value Metric** | Session length ↑50% (users explore cross-repo connections); repos tracked per user ↑25%. |
| **Effort** | M | **Risk** | Low |

---

### 3. 🎯 **CodePulse** — Real-Time Commit Streaming & Dopamine Feed

| Field | Detail |
|-------|--------|
| **User Problem** | Analysis is batch-only (click "Re-analyze"). No sense of liveness. Users add a repo and wait — no micro-feedback during the process. |
| **Core Experience** | User adds repo → **live progress feed** streams commit-by-commit: "Commit abc1234 — 47 lines, 12% AI detected 🤖" with animated counters. Each commit processed triggers a micro-animation (line counter ticks up, AI meter fills). When done, a **confetti burst** for milestones (1000 lines, 0% AI, etc.). |
| **Implementation Path** | **Azure** — SignalR service for real-time streaming from `AnalyzeRepositoryCommitsCommandHandler`. Client subscribes via `HubConnection`. Dopamine moments are pure Blazor state animations (no API needed). |
| **Value Metric** | Time-to-first-value ↓60%; user satisfaction ↑ (perceived speed ↑ even if actual time same). |
| **Effort** | M | **Risk** | Medium |

---

### 4. 🧠 **FoundryLens** — Azure AI Foundry Deep Scan

| Field | Detail |
|-------|--------|
| **User Problem** | Heuristic AI detection misses sophisticated AI output (refactored AI code, AI-assisted debugging, Copilot-generated code that mimics human style). |
| **Core Experience** | User toggles "Deep Scan" on a repo → Azure AI Foundry endpoint receives commit diffs → returns per-line AI probability scores → UI shows a **heatmapped diff view** where each line glows red→yellow→green by AI probability. Users can toggle between "Heuristic" and "Foundry" scores. |
| **Implementation Path** | **Azure AI Foundry** — Deploy a fine-tuned code-classification model. New `IFoundryAiDetectionService` in Application layer. `AiDetectionEndpoints` gains `/api/repositories/{id}/foundry-scan`. Fallback to heuristic if Foundry unavailable. |
| **Value Metric** | AI detection precision ↑60% on sophisticated AI code; enterprise readiness (differentiator). |
| **Effort** | L | **Risk** | High |

---

### 5. 🌐 **ContextMesh** — External Intelligence Fusion

| Field | Detail |
|-------|--------|
| **User Problem** | Line counts exist in a vacuum. A repo's 10K lines mean nothing without context — is this a weekend project or a Fortune 500 codebase? |
| **Core Experience** | User views repo detail → sees **Context Panel**: GitHub stars/forks/issues (via GitHub API), repo language ecosystem health (via GitHub Language Stats), comparable repo sizes in same org, and CI/CD status. Each data point enriches the line-count narrative. |
| **Implementation Path** | **Public API** — GitHub REST API (already authenticated via OAuth — zero new auth needed). New `IContextEnrichmentService` fetching `/repos/{owner}/{repo}` metadata. Store enriched data in a new Azure Table `PoRepoLineTrackerRepoContext`. |
| **Value Metric** | Data points per repo ↑3x; user session depth ↑40% (more context = more exploration). |
| **Effort** | S | **Risk** | Low |

---

### 6. 🎮 **RepoArena** — Pseudo-Multiplayer Gamification

| Field | Detail |
|-------|--------|
| **User Problem** | Solo tracking is utilitarian but not engaging. No reason to come back daily. No social proof or competitive drive. |
| **Core Experience** | User sees **Arena Leaderboard**: "Your repos average 8% AI code — better than 72% of tracked repos!" Anonymous aggregate stats fuel daily/weekly challenges: "Zero-AI Week" (lowest AI % wins badge), "Line Sprint" (most lines added in 7 days). Badges appear as animated unlock moments with sound. |
| **Implementation Path** | **No-API + Azure** — Aggregate anonymous stats via new `ArenaService` reading from `CommitLineCount` table. Badges stored in `UserPreferences` as `List<Badge>`. Leaderboard uses Azure Table aggregated counts (no PII). Daily challenge state in LocalStorage. |
| **Value Metric** | DAU ↑35%; session return rate ↑50% (daily challenges = habit loop). |
| **Effort** | M | **Risk** | Low |

---

### 7. 🔮 **TrendOracle** — Predictive Line & AI Forecasting

| Field | Detail |
|-------|--------|
| **User Problem** | Users see historical trends but have no forward-looking insight. "Is AI code accelerating in my repo?" is answered by eyeballing a chart, not by data. |
| **Core Experience** | User views line history chart → sees **dashed forecast line** extending 30 days into the future. Toggle: "AI Trajectory" shows projected AI % growth. Alert: "At current rate, this repo will be 50% AI-generated by August." |
| **Implementation Path** | **No-API** — Linear regression + exponential smoothing on existing `DailyLineCountDto` data. New `ForecastService` in Application layer. Zero external dependencies — pure math on data already in Azure Tables. |
| **Value Metric** | Decision-making speed ↑ (users act on forecasts, not just history); proactive alert engagement ↑40%. |
| **Effort** | S | **Risk** | Low |

---

### 8. 🗺️ **CodeCartographer** — Dependency & Architecture Map

| Field | Detail |
|-------|--------|
| **User Problem** | "Top Files" shows flat file sizes but no structural understanding. Users can't see which files are architecture bottlenecks or which modules are growing fastest. |
| **Core Experience** | User clicks "Architecture Map" → sees an interactive **force-directed graph** of file dependencies (imports/references). Node size = line count. Color = AI %. Hover reveals commit velocity. Zoom into a module to see its internal structure. |
| **Implementation Path** | **No-API** — Parse `using`/`import` statements during commit analysis (extend `DefaultLineCounter`). Store dependency edges in new Azure Table `PoRepoLineTrackerDependencies`. Client renders with D3.js interop in Blazor. |
| **Value Metric** | Architectural insight time ↓80% (vs. manual code review); new user onboarding to codebase ↓50%. |
| **Effort** | L | **Risk** | Medium |

---

### 9. 🎨 **AestheticShift** — Dynamic Theme Engine

| Field | Detail |
|-------|--------|
| **User Problem** | The UI is static and utilitarian. No emotional connection. No visual feedback that reflects the *state* of the codebase. |
| **Core Experience** | Repo with high AI % → UI shifts to a **cyberpunk neon theme** (AI-dominated = machine aesthetic). Repo with low AI % → **organic/nature theme** (human-crafted = natural aesthetic). Theme transitions are smooth CSS variable morphs. Users can override, but the default creates an emotional "feel" for the data. |
| **Implementation Path** | **No-API** — Pure CSS custom properties driven by Blazor state. `ChartDisplayMode` pattern already exists — extend to `ThemeMode`. Calculate AI % from repo data, map to HSL color shifts. Zero server calls. |
| **Value Metric** | User delight ↑ (qualitative); session length ↑15% (aesthetic novelty = exploration). |
| **Effort** | S | **Risk** | Low |

---

### 10. 🤝 **CodeCrew** — Collaborative Repo Tracking

| Field | Detail |
|-------|--------|
| **User Problem** | Only the repo owner can see their tracked repos. Teams can't share analysis, compare notes, or collaboratively monitor AI code infiltration. |
| **Core Experience** | User clicks "Share Repo" → generates invite link → teammate joins → both see the same repo dashboard with shared annotations (e.g., "This spike in AI code was the Copilot rollout"). Real-time cursor presence shows who's viewing what. |
| **Implementation Path** | **Azure** — SignalR for presence. New `TeamRepository` table with `OwnerId` + `MemberIds`. Share links use short-lived SAS tokens. Annotations stored in new `PoRepoLineTrackerAnnotations` table. |
| **Value Metric** | Users per repo ↑3x; enterprise adoption ↑ (team features = B2B gateway). |
| **Effort** | L | **Risk** | High |

---

## Micro-Refinements (11–20): Quality-of-Life / UX Polish

### 11. ⚡ **InstantReplay** — Commit Timeline Scrubber

| Field | Detail |
|-------|--------|
| **User Problem** | Line history is a static chart. Users can't "scrub through time" to see what the repo looked like on a specific date. |
| **Core Experience** | User drags a **timeline scrubber** below the chart → all metrics (total lines, AI %, top contributors) update in real-time to reflect that date's snapshot. Like a video timeline for your codebase. |
| **Implementation Path** | **No-API** — Data already in `DailyLineCountDto`. Client-side state filter on `CommitDate`. Pure Blazor reactivity. |
| **Value Metric** | Exploration speed ↑3x; "aha moment" rate ↑ (users discover patterns faster). |
| **Effort** | S | **Risk** | Low |

---

### 12. 🔔 **SmartAlert** — Threshold-Based Notifications

| Field | Detail |
|-------|--------|
| **User Problem** | Users must manually check repos for anomalies. No proactive notification when AI % spikes or line count drops unexpectedly. |
| **Core Experience** | User sets thresholds: "Alert me if AI % > 30% on any repo" or "Alert if weekly lines drop > 20%". When triggered, in-app notification + optional email via Azure Communication Services. |
| **Implementation Path** | **Azure** — New `AlertRule` entity in Table Storage. Background service evaluates rules after each analysis. Azure Communication Services for email. |
| **Value Metric** | Time-to-anomaly-detection ↓90% (from manual check to instant). |
| **Effort** | M | **Risk** | Low |

---

### 13. 📊 **SnapshotExport** — One-Click Report Generation

| Field | Detail |
|-------|--------|
| **User Problem** | No way to share analysis results with stakeholders who don't have app access. "Can you send me the AI detection report?" = manual screenshot + paste. |
| **Core Experience** | User clicks "Export Report" → generates a polished PDF/HTML report with charts, AI detection summary, contributor stats, and trend forecast. Includes company branding if configured. |
| **Implementation Path** | **No-API** — Blazor rendering to HTML → Puppeteer/Playwright (already in E2E tests) for PDF generation on server. Or client-side HTML export with print CSS. |
| **Value Metric** | Stakeholder communication time ↓70%; report generation from 30 min → 10 sec. |
| **Effort** | S | **Risk** | Low |

---

### 14. 🏷️ **CommitTagger** — Auto-Label Significant Commits

| Field | Detail |
|-------|--------|
| **User Problem** | All commits are treated equally. A 2000-line AI-generated commit and a 2-line typo fix get the same visual weight. |
| **Core Experience** | After analysis, commits are auto-tagged: "🤖 AI Burst" (>100 lines, >50% AI), "🔥 Hot Streak" (>500 lines in 1 day), "🐛 Bug Fix Pattern" (lines removed > added), "🧹 Refactor" (lines added ≈ lines removed). Tags appear as badges in the contributor chart and timeline. |
| **Implementation Path** | **No-API** — Pure algorithmic classification in `AnalyzeRepositoryCommitsCommandHandler`. Tags stored as `CommitLineCount.Tags` (new `List<string>` property). |
| **Value Metric** | Commit scan time ↓50% (tags let users skip irrelevant commits); insight quality ↑. |
| **Effort** | S | **Risk** | Low |

---

### 15. 🔄 **AutoSync** — Scheduled Repository Re-Analysis

| Field | Detail |
|-------|--------|
| **User Problem** | Users must manually click "Re-analyze" to get fresh data. No scheduled cadence. Data goes stale. |
| **Core Experience** | User sets sync schedule: "Daily at 2 AM" or "On every push" (via GitHub webhook). Repo auto-analyzes in background. Dashboard shows "Last synced: 2 hours ago" with a freshness indicator (green → yellow → red as data ages). |
| **Implementation Path** | **Azure + Public API** — Azure WebJobs/TimerTrigger for scheduled analysis. GitHub Webhooks for push-triggered analysis. New `SyncSchedule` property on `GitHubRepository`. |
| **Value Metric** | Data freshness ↑ (stale data ↓80%); manual re-analysis clicks ↓90%. |
| **Effort** | M | **Risk** | Medium |

---

### 16. 🧩 **ExtensionForge** — Custom File Filter Presets

| Field | Detail |
|-------|--------|
| **User Problem** | Users must manually configure file extensions. No presets for common stacks (React, .NET, Python ML, etc.). New users don't know what to include. |
| **Core Experience** | User clicks "Add Preset" → selects "React + TypeScript" → extensions auto-populate with `.tsx, .ts, .jsx, .js, .css, .scss`. Presets are shareable via URL. Community presets browsable. |
| **Implementation Path** | **No-API** — Static preset dictionary in `UserPreferences`. Share via base64-encoded URL params. Community presets as a JSON file in wwwroot. |
| **Value Metric** | Onboarding time ↓60% (new users get correct filters instantly); misconfigured analysis ↓70%. |
| **Effort** | S | **Risk** | Low |

---

### 17. 🕵️ **AnomalyHunter** — Statistical Outlier Detection

| Field | Detail |
|-------|--------|
| **User Problem** | Unusual patterns (sudden AI code spike, contributor ghosting, massive deletion) are invisible until manually spotted in charts. |
| **Core Experience** | After each analysis, `AnomalyHunter` runs statistical tests (Z-score on daily line counts, AI % deviation). Anomalies surface as **highlighted callouts** on charts: "⚠️ Unusual: AI code jumped 45% on Mar 15 — 3x the rolling average." |
| **Implementation Path** | **No-API** — Z-score + IQR methods on existing `DailyLineCountDto` data. New `DetectAnomalies` method in Application layer. Results stored as `CommitLineCount.AnomalyFlags`. |
| **Value Metric** | Anomaly detection from "never" to "automatic"; user investigation rate ↑40%. |
| **Effort** | S | **Risk** | Low |

---

### 18. 🎤 **VoiceQuery** — Natural Language Repo Queries

| Field | Detail |
|-------|--------|
| **User Problem** | Navigating dashboards requires clicks and scrolls. Power users want instant answers: "What's the AI percentage for my dotnet-api repo?" |
| **Core Experience** | User clicks mic icon → speaks query → Azure Speech-to-Text → Semantic Kernel planner maps intent to API call → result spoken back + displayed. "Show me repos with more than 30% AI code" → filtered list appears. |
| **Implementation Path** | **Azure** — Azure AI Speech (STT/TTS) + Semantic Kernel for intent mapping. New `VoiceQueryHub` SignalR endpoint. Client uses Web Speech API as fallback. |
| **Value Metric** | Query speed ↑5x for power users; accessibility ↑ (voice = inclusive). |
| **Effort** | M | **Risk** | Medium |

---

### 19. 📱 **PocketView** — Mobile-Optimized Dashboard

| Field | Detail |
|-------|--------|
| **User Problem** | Blazor WASM UI is desktop-first. Mobile experience is cramped — charts are unreadable, buttons too small. |
| **Core Experience** | Mobile viewport → auto-switches to **card-based summary view**: repo name + line count + AI % as big numbers. Swipe between repos. Pull-to-refresh triggers re-analysis. Charts become sparklines. |
| **Implementation Path** | **No-API** — CSS responsive breakpoints + Radzen's mobile components. New `MobileRepoCard` component. `ChartDisplayMode` already supports different views — extend with `MobileSparkline` mode. |
| **Value Metric** | Mobile session length ↑200%; mobile DAU ↑ (currently near-zero). |
| **Effort** | M | **Risk** | Low |

---

### 20. 🧪 **WhatIfSim** — Hypothetical Scenario Simulator

| Field | Detail |
|-------|--------|
| **User Problem** | Users can't explore "what if" scenarios: "What if we remove all AI-generated code?" or "What if contributor X leaves?" without affecting real data. |
| **Core Experience** | User clicks "What-If Simulator" → toggles contributors on/off, adjusts AI % threshold → chart **live-updates** with projected line counts. "If we remove all AI code, this repo drops from 12K to 7K lines." No data is modified — pure client-side projection. |
| **Implementation Path** | **No-API** — Client-side data manipulation on already-fetched `DailyLineCountDto` and `ContributorStatsDto`. New `WhatIfSimulator.razor` component with toggle chips for each contributor and a slider for AI threshold. |
| **Value Metric** | Strategic planning engagement ↑ (users spend 3x longer exploring scenarios). |
| **Effort** | S | **Risk** | Low |

---

## The Pilot Matrix — Ranked by ROI

| Rank | Idea | Value | Effort | Risk | ROI Score |
|------|------|-------|--------|------|-----------|
| 1 | **7. TrendOracle** | High | S | Low | ⭐⭐⭐⭐⭐ |
| 2 | **5. ContextMesh** | High | S | Low | ⭐⭐⭐⭐⭐ |
| 3 | **9. AestheticShift** | Med | S | Low | ⭐⭐⭐⭐ |
| 4 | **14. CommitTagger** | Med | S | Low | ⭐⭐⭐⭐ |
| 5 | **11. InstantReplay** | Med | S | Low | ⭐⭐⭐⭐ |
| 6 | **17. AnomalyHunter** | Med | S | Low | ⭐⭐⭐⭐ |
| 7 | **20. WhatIfSim** | Med | S | Low | ⭐⭐⭐⭐ |
| 8 | **13. SnapshotExport** | Med | S | Low | ⭐⭐⭐⭐ |
| 9 | **16. ExtensionForge** | Med | S | Low | ⭐⭐⭐ |
| 10 | **2. RepoRadar** | High | M | Low | ⭐⭐⭐ |
| 11 | **6. RepoArena** | High | M | Low | ⭐⭐⭐ |
| 12 | **3. CodePulse** | High | M | Med | ⭐⭐⭐ |
| 13 | **19. PocketView** | Med | M | Low | ⭐⭐⭐ |
| 14 | **12. SmartAlert** | High | M | Low | ⭐⭐⭐ |
| 15 | **15. AutoSync** | High | M | Med | ⭐⭐ |
| 16 | **18. VoiceQuery** | Med | M | Med | ⭐⭐ |
| 17 | **1. AgentForge** | V.High | L | Med | ⭐⭐ |
| 18 | **8. CodeCartographer** | High | L | Med | ⭐⭐ |
| 19 | **4. FoundryLens** | V.High | L | High | ⭐ |
| 20 | **10. CodeCrew** | V.High | L | High | ⭐ |

---

## 🏆 Top 3 Quick Wins (High ROI, Small Effort)

### 🥇 Quick Win 1: **TrendOracle** (#7)
Pure-math forecasting on data already in the system. Zero new APIs, zero new Azure services, zero new tables. Just a `ForecastService` with linear regression + exponential smoothing, and a dashed line on the existing chart. **Ship in 1–2 days.**

### 🥈 Quick Win 2: **ContextMesh** (#5)
GitHub API is already authenticated via OAuth — the access token is right there in `User.AccessToken`. One new service (`IContextEnrichmentService`), one new table, and the repo detail page gets stars, forks, issues, and language breakdown. **Ship in 2–3 days.**

### 🥉 Quick Win 3: **CommitTagger** (#14)
Algorithmic commit classification during the existing `AnalyzeRepositoryCommitsCommandHandler` flow. Add a `Tags` list to `CommitLineCount`, classify in-memory, store in the existing table. Badges render in the existing contributor chart. **Ship in 1 day.**

---

## 🌟 North Star Feature: **AgentForge** (#1)

The multi-agent analysis pipeline is the **strategic differentiator** that transforms PoRepoLineTracker from a "line counter with AI detection" into an **AI Code Intelligence Platform**. 

**Why this is North Star:**
- **Moat**: Multi-agent orchestration is hard to replicate. Competitors with single-pass detection can't match the depth.
- **Extensibility**: The 3-agent pattern (Worker → Critic → Synthesis) is a template. Future agents: SecurityAgent, PerformanceAgent, ComplianceAgent.
- **Azure Ecosystem Lock-In**: Deep integration with Azure AI Foundry + Semantic Kernel makes the app a showcase for Microsoft's AI stack.
- **Enterprise Gateway**: "Trustworthy AI detection" is the #1 ask from engineering leaders. AgentForge delivers it with confidence bands, not just percentages.

**Implementation Roadmap:**
1. **Phase 1** (2 weeks): Worker Agent = existing heuristic + new AST pattern matcher. Critic Agent = rule-based false-positive filter. Synthesis = confidence band assignment.
2. **Phase 2** (4 weeks): Critic Agent upgraded to LLM via Azure AI Foundry. Add per-line AI probability.
3. **Phase 3** (6 weeks): Full MAF orchestration with Semantic Kernel planner. Agent memory across analyses. Streaming results via SignalR.

---

## Feature Swarm Consolidation Map

```
┌─────────────────────────────────────────────────────────┐
│                    AGENTFORGE SWARM                      │
│  ┌──────────┐   ┌──────────┐   ┌──────────┐            │
│  │ Worker   │──▶│ Critic   │──▶│ Synthesis│            │
│  │ Agent    │   │ Agent    │   │ Agent    │            │
│  └────┬─────┘   └────┬─────┘   └────┬─────┘            │
│       │              │              │                   │
│  ┌────▼─────┐   ┌────▼─────┐   ┌────▼─────┐            │
│  │ Heuristic│   │ Foundry  │   │ Anomaly  │            │
│  │ + AST    │   │ Lens     │   │ Hunter   │            │
│  └──────────┘   └──────────┘   └──────────┘            │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                   DOPAMINE SWARM                         │
│  CodePulse ──▶ RepoArena ──▶ AestheticShift            │
│  (streaming)   (badges)      (theme morph)              │
│                                                         │
│  CommitTagger ──▶ InstantReplay ──▶ WhatIfSim          │
│  (labels)       (scrubber)       (simulator)            │
└─────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────┐
│                  INTELLIGENCE SWARM                      │
│  ContextMesh ──▶ TrendOracle ──▶ AnomalyHunter         │
│  (enrich)       (forecast)     (outliers)               │
│                                                         │
│  RepoRadar ──▶ CodeCartographer ──▶ SmartAlert         │
│  (cross-repo)  (architecture)     (notifications)       │
└─────────────────────────────────────────────────────────┘
```

---

## Force Multiplier Map — One Data Point, Three Contexts

| Data Point | Context 1 (Existing) | Context 2 (New) | Context 3 (New) |
|------------|---------------------|-----------------|-----------------|
| `CommitLineCount.AiPercentage` | AI Detection Chart | RepoArena badge scoring | AestheticShift theme engine |
| `CommitLineCount.LinesByFileType` | Extensions Counted page | CodeCartographer node sizing | ExtensionForge preset suggestion |
| `CommitLineCount.AuthorName` | Contributor Chart | RepoRadar cross-repo edges | CommitTagger attribution |
| `GitHubRepository.LastAnalyzedCommitDate` | "Analyzed X ago" badge | SmartAlert staleness trigger | AutoSync schedule check |
| `DailyLineCountDto.TotalLines` | Line history chart | TrendOracle forecast input | WhatIfSim projection base |

---

*PoIdeas v1.0 — Zero-Waste Innovation for PoRepoLineTracker*
