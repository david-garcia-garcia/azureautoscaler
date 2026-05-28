---
name: /opsx-review
id: opsx-review
category: Workflow
description: Review code produced during the apply phase — find smells, bugs, duplication and bad practices, then let the user choose what to fix (Experimental)
---

Review code produced during an OpenSpec apply phase.

**Input**: Optionally specify a change name (e.g., `/opsx-review add-auth`). If omitted, infer from conversation context or prompt the user.

**Steps**

1. **Select the change**

   If a name is provided, use it. Otherwise:
   - Infer from conversation context if the user mentioned a change
   - Auto-select if only one active change exists
   - If ambiguous, run `openspec list --json` and use the **AskUserQuestion tool** to let the user select

   Always announce: "Reviewing change: <name>" and how to override.

2. **Gather context**

   ```bash
   openspec status --change "<name>" --json
   openspec instructions apply --change "<name>" --json
   ```

   Read all `contextFiles` from the apply instructions (proposal, specs, design, tasks, or whatever the schema provides).

3. **Collect changed files**

   Use `git diff` (or equivalent) to get the list of files modified during the apply phase. Read each changed file in full.

4. **Run the review**

   Analyse every changed file against the context. Look for:

   - **Bugs** — logic errors, off-by-one, unhandled edge cases, null/undefined access, incorrect async/await usage
   - **Code smells** — long methods, deep nesting, magic numbers/strings, dead code, unclear naming
   - **Duplication** — copy-pasted blocks that could be extracted into shared helpers or reused existing utilities
   - **Lack of abstraction** — low-level detail leaking into high-level modules, missing interfaces/types, inconsistent layer separation
   - **Bad practices** — missing error handling, insecure patterns, console logs left in, commented-out code, unused imports, style inconsistencies vs. the rest of the codebase

5. **Build the prioritised findings list**

   Group findings into three priority tiers:

   | Priority | Criteria |
   |----------|---------|
   | **P1 – Must Fix** | Bugs, security issues, data-loss risks, broken functionality |
   | **P2 – Should Fix** | Code smells, bad practices, missing error handling |
   | **P3 – Nice to Fix** | Duplication, abstraction improvements, style/naming |

   Within each tier, sort by impact (highest first).

6. **Present findings and ask for action**

   Show the full prioritised list, then use the **AskUserQuestion tool** (multi-select) to ask the user which items to fix. Include "Fix all P1", "Fix all P1 + P2", "Fix all", and "Skip — I'll fix later" as shortcut options alongside individual items.

7. **Fix selected items**

   For each selected finding:
   - Announce which item is being fixed
   - Make the minimal targeted change
   - Confirm the fix with a brief note

8. **Show final summary**

   After all fixes are applied, display a summary table of what was found, what was fixed, and what was left for later.

**Output Template**

```
## Code Review: <change-name>

### P1 – Must Fix
1. [BUG] `src/foo.ts:42` — `userData` can be undefined here, causes crash on first login
2. ...

### P2 – Should Fix
3. [ERROR HANDLING] `src/bar.ts:88` — promise rejection not caught, will produce unhandled rejection warning
4. ...

### P3 – Nice to Fix
5. [DUPLICATION] `src/a.ts:10` and `src/b.ts:30` — same 15-line date-formatting logic; extract to `utils/date.ts`
6. ...

---
Total: N findings (P1: x, P2: y, P3: z)

Which items would you like me to fix?
```

**Guardrails**
- Read all context files before starting the review so findings are relevant to the intent
- Never modify files that were not changed during the apply phase
- Keep each fix minimal — do not refactor beyond the selected finding
- If a fix would change the public API or task requirements, pause and ask first
- If git history is unavailable, ask the user to point to the relevant files manually
