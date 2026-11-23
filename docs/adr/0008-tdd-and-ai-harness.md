# 0008. TDD workflow and AI harness

- Status: Accepted (data policy: open)
- Date: 2026-11-07
- Amended: 2026-11-22: safe phase default (spec), `.claude/human-only`, and a stop check of the work since a recorded baseline

## Context
We develop agents for a digital twin, increasingly with LLM coding agents. LLMs follow instructions most of
the time, not always, and can "pass" tests by weakening them. Team members should do similar things in
similar ways.

## Decision
1. Test-driven development in two phases, spec and implement, with a human review of tests between them
   (docs/workflow.md).
2. Three layers of control: instructions (`AGENTS.md`, skills), automated enforcement (hooks, architecture and
   determinism tests), and a universal gate (CI plus human review) that applies whatever tool was used.
3. Claude Code is the primary harness because its hooks let us enforce rules, not just state them:
   - `SessionStart` tells the agent the current phase,
   - `PreToolUse` (`guard.cs`) locks `src/` in spec phase and existing tests in implement phase, and keeps the
     harness itself human-only,
   - `Stop` (`verify.cs`) formats the code and refuses to let the agent finish while tests are red, giving up
     after three attempts so a stuck agent hands over to a person.

   Amended 2026-11-22: with no phase set the phase is `spec`. One list, `.claude/human-only`, names the human-only
   paths (the harness, build, solution and CI files) and both hooks read it. `guard.cs` is a quick early warning.
   `verify.cs` is the backstop: at stop it diffs the work since a baseline (HEAD at session start or at the last
   `./dev phase`) and blocks on human-only changes, phase violations, unclean formatting and red tests; it no
   longer reformats. Its messages reach the agent as one JSON object plus the reason on stderr with exit 2.
4. `AGENTS.md` is the single source of instructions. `CLAUDE.md` imports it, so tools that read `AGENTS.md`
   get the same rules (without hook enforcement).
5. Cloud-hosted models are allowed (no offline requirement).

## Limits
Hooks are guard rails, not a security boundary: a determined agent could find another route. Human review of
tests and CI are the real gates.

The stop check sees only what git sees: ignored files (`.claude/state/`, `.claude/settings.local.json`) are
invisible to it, and `disableAllHooks` turns it off; only the guard (writes under `.claude/`) covers those.

## Open: data policy
Confirm with security which code, data and documents may be sent to cloud AI providers, and on which contract
terms (data retention, training use). Until confirmed: synthetic data only, no classified or personal data in
the repository, prompts, tests or fixtures.

## Consequences
Small overhead per feature (switching phases, reviewing tests). In return, agents get an objective target,
and the specification is always visible and reviewed.
