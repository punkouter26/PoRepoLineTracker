# NET_RULES

Authoritative rules for `Po{Name}` .NET solutions. Deviations must be recorded in `AGENT.MD` with a reason.

## 1. Core Principles & Governance

- **1.1 Naming** — Solution, projects, and root namespaces use the `Po{Name}` prefix (`PoWatch`, `PoWalker`).
- **1.2 Stack** — .NET 10 / C# 15. Dependencies centralized in `/Directory.Packages.props`.
- **1.3 Compiler** — Every project: `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. Zero warnings.
- **1.4 Performance & Trimming** — `<IsTrimmable>true</IsTrimmable>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`. Standardize on `System.Text.Json` source generators (`JsonSerializerContext`) in `Po{Name}.Shared` for zero-reflection serialization across API and WASM.
  - Use the `JsonTypeInfo<T>` overloads at call sites, not the `JsonSerializerOptions` ones — the latter carry `RequiresUnreferencedCode` on the method, so they fail the trim analyzer regardless of what the options contain.
  - A wire contract must be a concrete named type. Anonymous types cannot be source-generated, so an endpoint returning one silently forces the reflection path.
- **1.5 Git** — Trunk-based on `master`. No other branches unless explicitly requested.
- **1.6 Domain Integrity** — No primitive obsession. Strongly-typed IDs (`readonly record struct`) and enums. Zero magic strings.
- **1.7 AI Provider Selection** — Default to the most cost-effective model across Azure, Google, or Hugging Face that can fulfil the task.

## 2. Directory & Architecture Layout

- **2.1 Depth** — Max 2 levels inside `src/`.

```
/
├── AGENT.md
├── Directory.Packages.props
├── SCRIPTS/setup.ps1
├── src/
│   ├── Po{Name}.API/       # Minimal API, BFF host, storage, feature slices
│   ├── Po{Name}.Client/    # Blazor WASM UI
│   └── Po{Name}.Shared/    # DTOs, enums, interfaces, validation
└── tests/
    ├── Po{Name}.Unit/          # Pure logic, no I/O
    ├── Po{Name}.Integration/   # Azurite / Testcontainers
    ├── Po{Name}.E2EAPI/        # API contract only
    └── Po{Name}.E2EUI/         # Playwright (mobile + desktop)
```

- **2.2 Vertical Slices**
  - Endpoints, request/response DTOs, and handlers live together in `Po{Name}.API/Features/{FeatureName}`.
  - Slices never reference each other. Shared models belong strictly in `Po{Name}.Shared`.
  - The API project hosts and serves the Blazor WASM client.

## 3. API, Security & BFF

- **3.1 Endpoints** — `IEndpointRouteBuilder` + `MapGroup()`. Document via `Microsoft.AspNetCore.OpenApi` + Scalar UI.
- **3.2 Diagnostics** — Expose `/health` and `/diag`. `/diag` masks every secret value.
- **3.3 BFF**
  - Zero tokens in the browser: the client talks only through `HttpOnly`, `SameSite=Strict`, secure cookies.
  - Entra ID OAuth uses the `/common` endpoint with a server-side `FallbackPolicy`.
  - Propagate `X-Session-ID` and `X-Correlation-ID` through all outbound HTTP calls.
- **3.4 Dev/Test Auth** — `FakeAuthHandler` driven by `X-Fake-User` / `X-Fake-Roles`. It **must throw `InvalidOperationException` if constructed in Production**.

## 4. UI/UX & Blazor WASM

- **4.1 Layout Contract** — Header: left = branding, center = actions, right = session/logout.
- **4.2 State & Security** — Antiforgery validation on every state-changing Minimal API endpoint (POST/PUT/DELETE/PATCH). Enforce state isolation in Blazor WASM with explicit `IDisposable`/`IAsyncDisposable` cleanups so nothing leaks across sessions.
  - `app.UseAntiforgery()` alone does **not** satisfy this: it validates only endpoints whose metadata requests it, which the framework adds just for form-binding endpoints. A JSON API needs middleware that validates unsafe methods by default and requires an explicit, reasoned opt-out.
- **4.3 State Visibility** — Persistent "USING MOCK DATA" banner whenever local mock data is active. *(Deprecated — no mock-data plumbing is wired in this app.)*
- **4.4 Components** — Radzen Blazor is the primary UI library; prefer its advanced interactive components over hand-rolled equivalents. Before building a control, check whether Radzen already ships it — `RadzenMediaQuery`, `RadzenBreadCrumb` and `RadzenSplitter` in particular replace custom JS interop and markup.
- **4.5 Styles** — Inline styles forbidden. Scoped CSS (`.razor.css`) + `:root` custom properties for design tokens. Light/dark themes follow the system setting dynamically.
  - `color-scheme` must track the app's own theme attribute, not just the OS, or UA-rendered surfaces (scrollbars, form controls, `<progress>`) disagree with the page.
  - Prefer `@container` over `@media` for anything sized by the content well rather than the viewport — a sidebar changes the well's width without the viewport moving.
  - Status and series colours belong in theme-aware tokens. A hardcoded hex cannot pass contrast in both themes.
- **4.6 Performance** — `Virtualize` for long lists. WebGL/Canvas for heavy visuals.
- **4.7 Accessibility** — WCAG 2.2 AA on every interactive element, including measured text contrast (4.5:1 body, 3:1 large text and UI components).

## 5. Local AI, Observability & Performance

- **5.1 Local AI** — Model registries with dtype fallback chains for browser/worker-native execution.
- **5.2 AI Test Interception** — A custom `DelegatingHandler` intercepts Azure AI pipeline calls in test environments so no tokens are consumed.
- **5.3 Logging** — `[LoggerMessage]` source generators on high-frequency paths. No string interpolation in logs.
- **5.4 Resilience & Cache** — .NET 10 `AddResiliencePipeline` and `HybridCache` for all HTTP resilience and caching.

## 6. Testing, CI/CD & Hygiene

- **6.1 Test Counts** — 100 Unit | 50 Integration | 25 API E2E | 25 UI E2E.
- **6.2 Azure** — Resources in resource group `PoShared` (or `Po{SolutionName}`). Auth via System-Assigned Managed Identity + Key Vault. No raw connection strings in app settings.
- **6.3 Post-Deploy Smoke Test** — CI verifies, after deploy: Blazor render-tree initialization, `/health` returns healthy, `/diag` returns masked config safely.
- **6.4 Hygiene** — Continuously purge dead code and orphaned assets. `AGENT.MD` is the living architectural source of truth.
