# session-report

Builds a single self-contained **HTML + JS** report from Claude Code session logs.

Claude Code stores one folder per project/worktree under `~/.claude/projects`, and
one `*.jsonl` file per session inside each. This tool reads those top-level session
files and renders:

- **Summary stats** — worktrees, sessions, user prompts, tool calls, wall-clock span.
- **Gantt chart** — one bar per session on a shared time axis (coloured by worktree)
  so overlapping sessions are obvious. Hover for details; click a bar to jump to that
  session in the timeline.
- **Timeline** — grouped *worktree → session → messages*, top to bottom. **User
  prompts are shown prominently**; each **Claude reply is a collapsible box** with its
  text, the tools it called (as chips), and its thinking (nested, collapsed).
- A **search box** (filters sessions by prompt/title text) and expand/collapse-all.

All times render in the viewer's local timezone.

## What it ignores

- **Subagents** — the `subagents/` and `tool-results/` subfolders are never read, and
  any `isSidechain` record inside a session file is skipped. Only the main
  human-facing conversation is reported.
- Tool-result payloads, attachments, and internal bookkeeping records are dropped from
  the timeline (tool calls are still summarised as chips).

## Usage

```sh
python session_report.py
```

Defaults: scans `~/.claude/projects` for folders matching `D--Work-Sonrisa-*` and
writes `session-report.html` next to the script.

Options:

| flag | default | meaning |
|------|---------|---------|
| `--projects-dir` | `~/.claude/projects` | Claude Code projects directory |
| `--pattern` | `D--Work-Sonrisa-*` | glob for the worktree folders to include |
| `--out` | `./session-report.html` | output file |

Example (all projects, custom output):

```sh
python session_report.py --pattern "*" --out C:/Temp/all-sessions.html
```

Requires only Python 3.11+ (standard library only). The output HTML has no external
dependencies and can be opened directly or shared as a file.
