---
name: security-review
description: 'AI-powered codebase security scanner that reasons about code like a security researcher — tracing data flows, understanding component interactions, and catching vulnerabilities that pattern-matching tools miss. Use this skill when asked to scan code for security vulnerabilities, find bugs, check for SQL injection, XSS, command injection, exposed API keys, hardcoded secrets, insecure dependencies, access control issues, or any request like "is my code secure?", "review for security issues", "audit this codebase", or "check for vulnerabilities". Covers injection flaws, authentication and access control bugs, secrets exposure, weak cryptography, insecure dependencies, and business logic issues across JavaScript, TypeScript, Python, Java, PHP, Go, Ruby, and Rust.'
---

# Security Review

An AI-powered security scanner that reasons about your codebase the way a human security researcher would — tracing data flows, understanding component interactions, and catching vulnerabilities that pattern-matching tools miss.

## When to Use This Skill

- Scanning a codebase or file for security vulnerabilities
- Checking for SQL injection, XSS, command injection, or other injection flaws
- Finding exposed API keys, hardcoded secrets, or credentials in code
- Auditing dependencies for known CVEs
- Reviewing authentication, authorization, or access control logic
- Detecting insecure cryptography or weak randomness
- Performing a data flow analysis to trace user input to dangerous sinks

## How This Skill Works

1. **Reads code like a security researcher** — understanding context, intent, and data flow
2. **Traces across files** — following how user input moves through your application
3. **Self-verifies findings** — re-examines each result to filter false positives
4. **Assigns severity ratings** — CRITICAL / HIGH / MEDIUM / LOW / INFO
5. **Proposes targeted patches** — every finding includes a concrete fix
6. **Requires human approval** — nothing is auto-applied; you always review first

## Execution Workflow

### Step 1 — Scope Resolution
- If a path was provided, scan only that scope
- If no path given, scan the **entire project** starting from the root
- Identify the language(s) and framework(s) in use

### Step 2 — Dependency Audit
- Check dependency manifests for known vulnerable packages
- Flag packages with known CVEs, deprecated crypto libs, or suspiciously old pinned versions

### Step 3 — Secrets & Exposure Scan
- Scan ALL files (including config, env, CI/CD, Dockerfiles, IaC) for hardcoded secrets
- Check for `.env` files accidentally committed, secrets in comments, cloud credentials, database connection strings with embedded credentials

### Step 4 — Vulnerability Deep Scan
- **Injection Flaws**: SQLi, XSS, command injection, LDAP, XPath, header injection
- **Authentication & Access Control**: Missing auth, BOLA/IDOR, JWT weaknesses, session fixation, CSRF
- **Data Handling**: Sensitive data exposure, insecure deserialization, path traversal, XXE, SSRF
- **Cryptography**: MD5/SHA1/DES for security, hardcoded IVs, weak randomness
- **Business Logic**: Race conditions, integer overflow, missing rate limiting

### Step 5 — Cross-File Data Flow Analysis
- Trace user-controlled input from entry points to sinks
- Identify vulnerabilities that only appear when looking at multiple files together

### Step 6 — Self-Verification Pass
- Re-read relevant code with fresh eyes
- Check if framework/middleware already handles this upstream
- Downgrade or discard findings that aren't genuine vulnerabilities

### Step 7 — Generate Security Report
Output a structured report with findings summary, detailed findings by category, and dependency audit.

### Step 8 — Propose Patches
For every CRITICAL and HIGH finding, generate a concrete patch with before/after code.

**Explicitly state: "Review each patch before applying. Nothing has been changed yet."**

## Severity Guide

| Severity | Meaning | Example |
|----------|---------|---------|
| 🔴 CRITICAL | Immediate exploitation risk | SQLi, RCE, auth bypass |
| 🟠 HIGH | Serious vulnerability | XSS, IDOR, hardcoded secrets |
| 🟡 MEDIUM | Exploitable with conditions | CSRF, open redirect, weak crypto |
| 🔵 LOW | Best practice violation | Verbose errors, missing headers |
| ⚪ INFO | Observation worth noting | Outdated dependency (no CVE) |

## Output Rules

- **Always** produce a findings summary table first (counts by severity)
- **Never** auto-apply any patch — present patches for human review only
- **Always** include a confidence rating per finding (High / Medium / Low)
- **Group findings** by category, not by file
- **Be specific** — include file path, line number, and the exact vulnerable code snippet
- If the codebase is clean, say so clearly: "No vulnerabilities found"
