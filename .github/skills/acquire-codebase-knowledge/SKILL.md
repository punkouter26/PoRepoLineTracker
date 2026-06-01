---
name: acquire-codebase-knowledge
description: 'Use this user explicitly asks to map, document, or onboard into an existing codebase. Trigger for prompts like "map this codebase", "document this architecture", "onboard me to this repo", or "create codebase docs". Do not trigger for routine feature implementation, bug fixes, or narrow code edits unless the user asks for repository-level discovery.'
---

# Acquire Codebase Knowledge

Produces seven populated documents in `docs/codebase/` covering everything needed to work effectively on the project. Only document what is verifiable from files or terminal output — never infer or assume.

## Output Contract (Required)

Before finishing, all of the following must be true:

1. Exactly these files exist in `docs/codebase/`: `STACK.md`, `STRUCTURE.md`, `ARCHITECTURE.md`, `CONVENTIONS.md`, `INTEGRATIONS.md`, `TESTING.md`, `CONCERNS.md`.
2. Every claim is traceable to source files, config, or terminal output.
3. Unknowns are marked as `[TODO]`; intent-dependent decisions are marked `[ASK USER]`.
4. Every document includes a short "evidence" list with concrete file paths.
5. Final response includes numbered `[ASK USER]` questions and intent-vs-reality divergences.

## Workflow

```
- [ ] Phase 1: Run scan, read intent documents
- [ ] Phase 2: Investigate each documentation area
- [ ] Phase 3: Populate all seven docs in docs/codebase/
- [ ] Phase 4: Validate docs, present findings, resolve all [ASK USER] items
```

### Phase 1: Scan and Read Intent

1. Search for `PRD`, `TRD`, `README`, `ROADMAP`, `SPEC`, `DESIGN` files and read them.
2. Summarise the stated project intent before reading any source code.
3. Scan the directory structure to understand the project layout.

### Phase 2: Investigate

For each of the seven documentation areas, investigate:
- **STACK.md** — language, runtime, frameworks, all dependencies
- **STRUCTURE.md** — directory layout, entry points, key files
- **ARCHITECTURE.md** — layers, patterns, data flow
- **CONVENTIONS.md** — naming, formatting, error handling, imports
- **INTEGRATIONS.md** — external APIs, databases, auth, monitoring
- **TESTING.md** — frameworks, file organization, mocking strategy
- **CONCERNS.md** — tech debt, bugs, security risks, perf bottlenecks

### Phase 3: Populate Templates

Fill in this order: STACK → STRUCTURE → ARCHITECTURE → CONVENTIONS → INTEGRATIONS → TESTING → CONCERNS.

Use `[TODO]` for anything that cannot be determined from code. Use `[ASK USER]` where the right answer requires team intent.

### Phase 4: Validate, Repair, Verify

1. Validate each doc against investigation checkpoints.
2. For each non-trivial claim, confirm at least one evidence reference exists.
3. If any required section is missing or unsupported, fix the document.
4. Repeat until all seven docs pass.

## Gotchas

- **Monorepos:** Root `package.json` may have no source — check for `workspaces`, `packages/`, or `apps/` directories.
- **Outdated README:** Cross-reference with actual file structure before treating any README claim as fact.
- **Generated/compiled output:** Never document patterns from `dist/`, `build/`, `bin/`, `obj/`, `.next/`, etc.
- **`.env.example` reveals required config:** Read `.env.example`, `.env.template`, or `.env.sample` to discover required environment variables.
- **`devDependencies` ≠ production stack:** Only `dependencies` runs in production.
