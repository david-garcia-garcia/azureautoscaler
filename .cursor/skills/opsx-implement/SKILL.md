---
name: opsx-implement
description: Orchestrate the full post-propose workflow for an OpenSpec change. Use when the user wants to implement a change end-to-end after a proposal exists: delegates apply to a composer-2.5-fast subagent, evaluates deviations against the original spec, delegates review to a composer-2.5-fast subagent, then synthesizes findings and presents the user with actionable decisions.
disable-model-invocation: true
---

# opsx-implement

Full post-propose workflow orchestrated by the parent agent. The parent delegates heavy lifting to subagents but stays in control of spec fidelity and final decisions.

**Input**: Change name (optional). Infer from context, auto-select if unambiguous, otherwise use `openspec list --json` + **AskUserQuestion**.

---

## Phase A — Implement (subagent)

1. Announce: "Implementing change: <name> — delegating to composer-2.5-fast"
2. Launch a **Task subagent** with model `composer-2.5-fast`:

   > Prompt: "Read the skill `.cursor/skills/openspec-apply-change/SKILL.md` and follow it to implement the OpenSpec change named `<name>`. Work directly in the repository root. Do NOT create a git worktree. Complete all tasks. Report: (a) every task completed, (b) every decision or deviation made during implementation with a brief rationale, (c) any blocker that stopped progress."

3. Wait for the subagent to finish. Read its full output.

---

## Phase B — Deviation analysis (parent, you)

Read the spec artifacts to ground yourself:

```bash
openspec instructions apply --change "<name>" --json
```

Read every file in `contextFiles` (proposal, design, specs, tasks).

Then compare the subagent's implementation against the original intent:

For each deviation the subagent reported (or that you notice via `git diff`):

| Verdict | Criteria | Action |
|---------|----------|--------|
| **Aligned** | Subagent adapted to a real constraint; outcome still satisfies the spec | Accept, note it |
| **Overreach** | Subagent added scope beyond the spec | Revert or trim |
| **Misaligned** | Subagent diverged from the design without a good reason | Fix directly |
| **Blocker** | Subagent stopped; task is not done | Decide: fix yourself or pause and ask the user |

Fix misaligned items and blockers before moving to Phase C. If a blocker requires a user decision, pause and ask with **AskUserQuestion** before continuing.

Announce what you found and what you did, e.g.:
```
## Deviation Review

✓ Accepted: <deviation> — rationale aligns with spec
✗ Fixed: <deviation> — reverted overreach in <file>
⚠ Blocker: <description> — asking for guidance
```

---

## Phase C — Review (subagent)

Once the implementation is clean and complete:

1. Announce: "Running code review — delegating to composer-2.5-fast"
2. Launch a **Task subagent** with model `composer-2.5-fast`:

   > Prompt: "Read the skill `.cursor/skills/openspec-review/SKILL.md` and follow it to review the implementation for the OpenSpec change named `<name>`. Work directly in the repository root. Return the complete prioritised findings list (P1/P2/P3) with file:line references and concrete fix suggestions. Do NOT apply any fixes."

3. Wait for the subagent to finish. Read its full output.

---

## Phase D — Synthesize and present to user (parent, you)

Read the review findings alongside the spec artifacts to add context.

For each finding, enrich it with:
- **Why it matters here** — connect to the specific change intent (not just generic advice)
- **Risk if deferred** — what breaks or degrades if this is skipped
- **Fix effort** — tiny / small / medium (your estimate)

Then present a numbered list grouped by priority tier, and use **AskUserQuestion** (multi-select) with:
- Each individual item as a selectable option
- Shortcut options: "Fix all P1", "Fix all P1 + P2", "Fix everything", "Skip — I'll fix later"

**Do not fix anything until the user selects items.**

Once the user selects, fix each chosen item in priority order (P1 → P2 → P3):
- Announce: "Fixing item N: <short description>"
- Apply the minimal targeted change
- Confirm: "Fixed item N"

---

## Phase E — Final summary

```
## opsx-implement complete: <change-name>

### Implementation
- Tasks: N/N complete
- Deviations resolved: <list or "none">

### Review
- Findings: P1: x  P2: y  P3: z
- Fixed: <items>
- Deferred: <items or "none">

Next: run `/opsx:archive` to close this change.
```

---

**Guardrails**
- Never skip Phase B — always evaluate subagent deviations before the review
- Never apply review fixes before the user selects items (Phase D)
- Fixes must be minimal and scoped; do not refactor beyond the selected finding
- Pause before any fix that alters a public API, cross-module contract, or task requirements
- If the apply subagent hits a blocker, do not proceed to review until it is resolved
- Do not create git worktrees; work in the main tree only
