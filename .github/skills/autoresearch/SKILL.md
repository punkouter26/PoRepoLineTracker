---
name: autoresearch
description: 'Autonomous iterative experimentation loop for any programming task. Guides the user through defining goals, measurable metrics, and scope constraints, then runs an autonomous loop of code changes, testing, measuring, and keeping/discarding results. Inspired by Karpathy''s autoresearch. USE FOR: autonomous improvement, iterative optimization, experiment loop, auto research, performance tuning, automated experimentation, hill climbing, try things automatically, optimize code, run experiments, autonomous coding loop. DO NOT USE FOR: one-shot tasks, simple bug fixes, code review, or tasks without a measurable metric.'
---

# Autoresearch: Autonomous Iterative Experimentation

An autonomous experimentation loop for any programming task. You define the goal and how to measure it; the agent iterates autonomously — modifying code, running experiments, measuring results, and keeping or discarding changes — until interrupted.

## Agent Behavior Rules

1. **DO** guide the user through the Setup phase interactively before starting the loop.
2. **DO** establish a baseline measurement before making any changes.
3. **DO** commit every experiment attempt before running it (so it can be reverted cleanly).
4. **DO** keep a results log (TSV) tracking every experiment.
5. **DO** revert changes that do not improve the metric (git reset to last known good).
6. **DO** run autonomously once the loop starts — never pause to ask "should I continue?".
7. **DO NOT** modify files the user marked as out-of-scope.
8. **DO NOT** skip the measurement step — every experiment must be measured.
9. **DO NOT** keep changes that regress the metric unless the user explicitly allowed trade-offs.
10. **DO NOT** install new dependencies or make environment changes unless the user approved it.

## Phase 1: Setup (Interactive)

Before any experimentation begins, work with the user to establish these parameters:

### 1.1 Define the Goal
What are you trying to improve or optimize? (execution time, memory usage, test pass rate, response time, build time, etc.)

### 1.2 Define the Metric
- The command to run
- How to extract the metric from output
- Direction: Is lower better or higher better?

### 1.3 Define the Scope
Which files or directories am I allowed to modify? Which files are OFF LIMITS?

### 1.4 Define Constraints
Time budget, no new dependencies, must keep tests passing, etc.

### 1.5 Define the Experiment Budget
How many experiments to run?

### 1.6 Simplicity Criterion
All else being equal, simpler is better.

### 1.7 Confirm Setup
Summarize all parameters back to the user. Do not proceed until confirmed.

## Phase 2: Branch & Baseline

1. Create a branch: `git checkout -b autoresearch/<date-tag>`
2. Read in-scope files to build full context
3. Initialize `results.tsv` with header: `experiment\tcommit\tmetric\tstatus\tdescription`
4. Run the baseline metric command
5. Report baseline to the user

## Phase 3: Experiment Loop

Run continuously until budget reached or user interrupts:

```
LOOP:
  1. THINK   - Analyze previous results, generate hypothesis
  2. EDIT    - Modify in-scope files, keep changes focused
  3. COMMIT  - git add + git commit with descriptive message
  4. RUN     - Execute metric command, redirect output to run.log
  5. MEASURE - Extract metric from run.log
  6. DECIDE  - Compare to best: keep (improved) or revert (same/worse)
  7. LOG     - Append row to results.tsv
  8. CONTINUE
```

### Experiment Strategy Priority
1. Low-hanging fruit first
2. Informed by results
3. Diversify after plateaus
4. Combine winners
5. Simplification passes
6. Radical changes

## Phase 4: Reporting

1. Print the full results.tsv as a formatted table
2. Summarize: total experiments, kept/discarded/crashed, improvement percentage
3. Show the cumulative git log of kept experiments
4. Recommend next steps
