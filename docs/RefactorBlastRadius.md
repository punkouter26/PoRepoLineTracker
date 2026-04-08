# Documentation Refactor Blast Radius

## Scope

This refactor replaces the previous mixed documentation set with a consolidated master-suite model:

- one high-density master Mermaid for each major concern
- one `_SIMPLE` variant for stakeholder review
- one root README that links to the full set
- one reserved screenshot directory under `docs/screenshots/`

## Blast radius assessment

| Proposed refactor | Downstream impact | Risk level | Mitigation |
| --- | --- | --- | --- |
| Replace legacy docs with `*_MASTER` and `*_SIMPLE` Mermaid assets | Any internal bookmarks or external references pointing to removed legacy files like `docs/Architecture.mmd`, `docs/SystemFlow.mmd`, `docs/ProductSpec.md`, or `docs/DevOps.md` will break | Medium | Root [README.md](../README.md) now links only to the new canonical assets; update any wiki or pipeline references that still target deleted files |
| Consolidate architecture, data lifecycle, and system flow into glanceable diagrams | Reviewers lose the previous split between product-spec and devops narratives, but gain a single-source diagram set optimized for quick scanning and AI retrieval | Low | Keep the README summary explicit about deployment, auth, storage, and runtime behavior so narrative context is still available |
| Reserve `docs/screenshots/` exclusively for visual assets | Any future non-image files dropped into the screenshot folder will violate the new convention and make doc discovery noisier | Low | Treat `docs/screenshots/` as image-only and keep all text-based documentation in the docs root |

## Runtime impact

This documentation refactor does not change application code, deployment assets, Azure resources, API contracts, test layout, or persistence schemas. The blast radius is therefore limited to human workflows:

- onboarding links
- internal bookmarks
- README references
- any automation that assumes deleted doc filenames still exist

## Service dependency impact

- Blazor client: no runtime impact
- ASP.NET Core API: no runtime impact
- Azure Table Storage: no runtime impact
- GitHub OAuth and GitHub API integration: no runtime impact
- Azure App Service, Key Vault, App Insights, Log Analytics, and ACR: no runtime impact

## Follow-up checks

1. Update any PR templates, wiki pages, or CI annotations that still point to removed documentation files.
2. Keep future architecture updates in the new master/simple pairs rather than creating new one-off documents.