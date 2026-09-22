#!/usr/bin/env python3
"""
session_report.py — build a single-file HTML report from Claude Code session logs.

It scans the per-project folders that Claude Code keeps under
`~/.claude/projects` (each folder is a *worktree*), reads the top-level
`*.jsonl` files (each file is a *session*), and emits a self-contained
HTML + JS report that shows, top to bottom:

  * a Gantt chart of every session so overlaps are obvious, and
  * a per-worktree / per-session timeline that foregrounds the user's
    prompts and tucks the assistant's replies into collapsible boxes.

Subagent logs (the `subagents/` and `tool-results/` subfolders, and any
`isSidechain` records) are deliberately ignored — only the human-facing
main-thread conversation is reported.

Usage:
    python session_report.py                    # defaults below
    python session_report.py --out report.html
    python session_report.py --projects-dir "C:/Users/me/.claude/projects" \
                             --pattern "D--Work-Sonrisa-*"
"""

from __future__ import annotations

import argparse
import glob
import html
import json
import os
import re
from datetime import datetime, timezone

# --------------------------------------------------------------------------- #
# Defaults (tuned for this machine; override on the command line)              #
# --------------------------------------------------------------------------- #
DEFAULT_PROJECTS_DIR = os.path.expanduser("~/.claude/projects").replace("\\", "/")
DEFAULT_PATTERN = "D--Work-Sonrisa-*"

# Keep the report a sensible size: prompts are never truncated (they are the
# point of the report); assistant prose and thinking are bounded.
MAX_RESPONSE_CHARS = 20_000
MAX_THINKING_CHARS = 3_000
MAX_TOOL_DETAIL = 140

PALETTE = [
    "#4f7cff", "#e8710a", "#12a594", "#d5408c", "#8250df", "#c99a06", "#5b8c00",
]


# --------------------------------------------------------------------------- #
# Parsing                                                                      #
# --------------------------------------------------------------------------- #
def parse_ts(value):
    """Parse an ISO-8601 timestamp (with trailing Z) to an aware UTC datetime."""
    if not value:
        return None
    try:
        s = value.replace("Z", "+00:00")
        dt = datetime.fromisoformat(s)
        if dt.tzinfo is None:
            dt = dt.replace(tzinfo=timezone.utc)
        return dt.astimezone(timezone.utc)
    except (ValueError, AttributeError):
        return None


def blocks_of(message):
    """Return the content of a message as a list of blocks (normalising str)."""
    content = message.get("content")
    if isinstance(content, str):
        return [{"type": "text", "text": content}]
    if isinstance(content, list):
        return content
    return []


def is_tool_result_user(message):
    """A `type:user` record that only carries tool results, not a real prompt."""
    blocks = blocks_of(message)
    if not blocks:
        return False
    saw_result = False
    for b in blocks:
        if not isinstance(b, dict):
            continue
        t = b.get("type")
        if t == "tool_result":
            saw_result = True
        elif t == "text" and b.get("text", "").strip():
            return False  # has real text -> treat as a prompt
    return saw_result


def user_text(message):
    """Concatenate the human-readable text of a user prompt."""
    parts = []
    for b in blocks_of(message):
        if isinstance(b, dict) and b.get("type") == "text":
            parts.append(b.get("text", ""))
        elif isinstance(b, str):
            parts.append(b)
    return "\n".join(p for p in parts if p).strip()


# promptSource values that mark a record as something the *user* actually sent.
HUMAN_PROMPT_SOURCES = {"typed", "queued", "suggestion_accepted"}


def classify_user_record(text, prompt_source):
    """Decide what a `type:user` string record really is.

    Returns one of:
      "user"    — a genuine message the user sent (typed / queued / accepted).
      "command" — a slash-command the user invoked (e.g. /model); shown, muted.
      "skip"    — harness plumbing that is NOT a user message and must never be
                  shown as one: background <task-notification>s (promptSource
                  "system"), command stdout/caveats, and interrupt markers.
    """
    stripped = text.lstrip()

    # Background task-completion notices are injected by the harness when a
    # subagent/tool finishes; the user never sent them. They carry
    # promptSource "system".
    if prompt_source == "system" or stripped.startswith("<task-notification>"):
        return "skip"

    # Output/plumbing surrounding a slash command — not a user message.
    if (
        stripped.startswith("<local-command-stdout>")
        or stripped.startswith("<local-command-caveat>")
        or stripped.startswith("<bash-stdout>")
        or stripped.startswith("Caveat:")
        or stripped.startswith("[Request interrupted")
    ):
        return "skip"

    # The user invoking a slash command (e.g. /model). A real user action, but a
    # command rather than a message — shown muted, never counted as a prompt.
    if stripped.startswith("<command-name>") or "<command-message>" in text[:200]:
        return "command"

    # A null promptSource marks harness-injected content — interrupt markers,
    # tool/MCP instruction blocks, command output — never a message the user
    # typed. (Every genuinely user-authored prompt carries a non-null source:
    # "typed", "queued", "suggestion_accepted", …) So anything left with a null
    # source is dropped, which is what stops injected instruction dumps like the
    # "Claude in Chrome" block from being shown as a user prompt.
    if prompt_source is None:
        return "skip"

    # Any explicit, non-"system" source is a message the user actually sent.
    return "user"


def clean_command_text(text):
    """Turn a raw <command-name>…</command-name> blob into e.g. '/model foo'."""
    name = re.search(r"<command-name>(.*?)</command-name>", text, re.S)
    args = re.search(r"<command-args>(.*?)</command-args>", text, re.S)
    label = name.group(1).strip() if name else text.strip()
    extra = args.group(1).strip() if args else ""
    return (label + (" " + extra if extra else "")).strip()


# AskUserQuestion answers arrive as a tool_result string of the form:
#   Your questions have been answered: "Q1"="A1" selected preview: … "Q2"="A2" …
_AQ_PAIR = re.compile(r'"([^"]+)"\s*=\s*"([^"]+)"')


def parse_question_answers(result_text):
    """Pull [(question, answer)] pairs out of an AskUserQuestion tool result."""
    return [
        {"q": q.strip(), "a": a.strip()}
        for q, a in _AQ_PAIR.findall(result_text or "")
    ]


def result_text_of(block):
    """Flatten a tool_result block's content (str or list of blocks) to text."""
    rc = block.get("content")
    if isinstance(rc, list):
        rc = " ".join(
            x.get("text", "")
            for x in rc
            if isinstance(x, dict) and x.get("type") == "text"
        )
    return rc if isinstance(rc, str) else ""


def askquestion_answer_event(message, pending_ids, ts):
    """If this user record answers an AskUserQuestion, build a user event for it.

    The user's picks in an AskUserQuestion dialog are real input, but the log
    stores them as a tool_result rather than a typed message — so they would
    otherwise be discarded with every other tool result.
    """
    content = message.get("content")
    if not isinstance(content, list):
        return None
    for b in content:
        if not isinstance(b, dict) or b.get("type") != "tool_result":
            continue
        rc = result_text_of(b)
        is_aq = b.get("tool_use_id") in pending_ids or rc.startswith(
            "Your questions have been answered"
        )
        if not is_aq:
            continue
        pending_ids.discard(b.get("tool_use_id"))
        qa = parse_question_answers(rc)
        if not qa:
            # No question=answer pairs — a dismissed dialog or a tool error, not
            # user input. Let it fall through to be skipped like any tool result.
            return None
        text = "\n".join(f"{p['q']} → {p['a']}" for p in qa)
        return {"kind": "prompt", "cls": "answer", "ts": ts, "qa": qa, "text": text}
    return None


# When the user rejects a tool/plan (e.g. ExitPlanMode) and types instructions
# instead, the log wraps their message in an is_error tool_result like:
#   The user doesn't want to proceed with this tool use. … the user said:
#   <the actual message>
#   \n\nNote: The user's next message …
_REJECT_PREAMBLE = "The user doesn't want to proceed with this tool use"
_USER_SAID = re.compile(r"the user said:\s*(.*)", re.S | re.I)


def rejection_feedback_event(message, ts):
    """Recover a real user message typed when rejecting a tool use / plan."""
    content = message.get("content")
    if not isinstance(content, list):
        return None
    for b in content:
        if not isinstance(b, dict) or b.get("type") != "tool_result":
            continue
        rc = result_text_of(b)
        # Anchor on the rejection preamble so we don't match tool output that
        # merely quotes the phrase "the user said:".
        if not rc.lstrip().startswith(_REJECT_PREAMBLE):
            continue
        m = _USER_SAID.search(rc)
        if not m:
            continue
        fb = re.split(r"\n\nNote: The user's next message", m.group(1).strip())[0].strip()
        # "let me clarify" injects boilerplate rather than a typed message.
        if not fb or fb.startswith("The user wants to clarify these questions"):
            continue
        return {"kind": "prompt", "cls": "user", "ts": ts, "text": fb}
    return None


def tool_detail(name, tool_input):
    """A short, human summary of a tool call's target."""
    if not isinstance(tool_input, dict):
        return ""
    keys_by_pref = [
        "description",
        "file_path",
        "path",
        "pattern",
        "command",
        "query",
        "prompt",
        "url",
        "skill",
        "todos",
    ]
    for k in keys_by_pref:
        if k in tool_input and tool_input[k]:
            val = tool_input[k]
            if isinstance(val, (list, dict)):
                val = json.dumps(val, ensure_ascii=False)
            val = str(val).strip().splitlines()[0] if str(val).strip() else ""
            if val:
                return val[:MAX_TOOL_DETAIL]
    return ""


def load_session(path):
    """Read one session .jsonl into a structured dict, or None if empty."""
    title = None
    branches = {}
    cwds = {}
    version = None
    first_dt = None
    last_dt = None

    # Turn-based event assembly.
    events = []
    current_response = None  # accumulating assistant reply
    pending_aq = set()  # ids of AskUserQuestion tool_use blocks awaiting answers

    def flush_response():
        nonlocal current_response
        if current_response is not None:
            resp = current_response
            resp["text"] = resp["text"].strip()
            if len(resp["text"]) > MAX_RESPONSE_CHARS:
                resp["text"] = resp["text"][:MAX_RESPONSE_CHARS] + "\n\n… [truncated]"
            resp["thinking"] = resp["thinking"].strip()
            if len(resp["thinking"]) > MAX_THINKING_CHARS:
                resp["thinking"] = resp["thinking"][:MAX_THINKING_CHARS] + "\n\n… [truncated]"
            # Only keep a response event if it carries something to show.
            if resp["text"] or resp["tools"] or resp["thinking"]:
                events.append(resp)
            current_response = None

    n_prompts = 0
    n_tools = 0

    with open(path, "r", encoding="utf-8") as fh:
        for line in fh:
            line = line.strip()
            if not line:
                continue
            try:
                rec = json.loads(line)
            except json.JSONDecodeError:
                continue

            rtype = rec.get("type")

            if rtype == "ai-title":
                title = rec.get("aiTitle") or title
                continue

            # Ignore subagent / sidechain records entirely.
            if rec.get("isSidechain"):
                continue

            if rec.get("cwd"):
                cwds[rec["cwd"]] = cwds.get(rec["cwd"], 0) + 1
            gb = rec.get("gitBranch")
            if gb and gb != "HEAD":
                branches[gb] = branches.get(gb, 0) + 1
            if rec.get("version"):
                version = rec["version"]

            dt = parse_ts(rec.get("timestamp"))
            if dt:
                if first_dt is None or dt < first_dt:
                    first_dt = dt
                if last_dt is None or dt > last_dt:
                    last_dt = dt

            message = rec.get("message") or {}

            if rtype == "user":
                reject_event = rejection_feedback_event(message, rec.get("timestamp"))
                if reject_event is not None:
                    # A message the user typed while rejecting a tool use / plan.
                    flush_response()
                    n_prompts += 1
                    events.append(reject_event)
                    continue
                answer_event = askquestion_answer_event(
                    message, pending_aq, rec.get("timestamp")
                )
                if answer_event is not None:
                    # The user's choices in an AskUserQuestion dialog — real
                    # input, so treat it like a prompt turn.
                    flush_response()
                    n_prompts += 1
                    events.append(answer_event)
                    continue
                if is_tool_result_user(message):
                    # A tool result closing an assistant step; keep it attached
                    # to the in-flight response rather than starting a turn.
                    continue
                text = user_text(message)
                if not text:
                    continue
                cat = classify_user_record(text, rec.get("promptSource"))
                if cat == "skip":
                    # Harness plumbing (e.g. <task-notification>): not a user
                    # message. Leave any in-flight reply merged rather than
                    # splitting it around a notification the user never sent.
                    continue
                flush_response()
                if cat == "command":
                    events.append(
                        {
                            "kind": "prompt",
                            "cls": "command",
                            "ts": rec.get("timestamp"),
                            "text": clean_command_text(text),
                        }
                    )
                else:  # "user"
                    n_prompts += 1
                    events.append(
                        {
                            "kind": "prompt",
                            "cls": "user",
                            "ts": rec.get("timestamp"),
                            "text": text,
                        }
                    )

            elif rtype == "assistant":
                if current_response is None:
                    current_response = {
                        "kind": "response",
                        "ts": rec.get("timestamp"),
                        "text": "",
                        "thinking": "",
                        "tools": [],
                    }
                for b in blocks_of(message):
                    if not isinstance(b, dict):
                        continue
                    bt = b.get("type")
                    if bt == "text":
                        t = b.get("text", "")
                        if t.strip():
                            if current_response["text"]:
                                current_response["text"] += "\n\n"
                            current_response["text"] += t
                    elif bt == "thinking":
                        t = b.get("thinking", "") or b.get("text", "")
                        if t.strip():
                            if current_response["thinking"]:
                                current_response["thinking"] += "\n\n"
                            current_response["thinking"] += t
                    elif bt == "tool_use":
                        if b.get("name") == "AskUserQuestion":
                            # Surfaced as a user "answer" event when the reply
                            # arrives, not as a tool chip.
                            pending_aq.add(b.get("id"))
                            continue
                        n_tools += 1
                        current_response["tools"].append(
                            {
                                "name": b.get("name", "tool"),
                                "detail": tool_detail(b.get("name"), b.get("input")),
                            }
                        )
            # other record types (mode, attachment, snapshots, ...) are ignored

    flush_response()

    if first_dt is None:
        return None

    primary_branch = max(branches, key=branches.get) if branches else None
    cwd = max(cwds, key=cwds.get) if cwds else None

    return {
        "id": os.path.splitext(os.path.basename(path))[0],
        "file": os.path.basename(path),
        "title": title,
        "branch": primary_branch,
        "branches": sorted(branches),
        "cwd": cwd,
        "version": version,
        "start": first_dt.isoformat().replace("+00:00", "Z"),
        "end": last_dt.isoformat().replace("+00:00", "Z"),
        "start_ms": int(first_dt.timestamp() * 1000),
        "end_ms": int(last_dt.timestamp() * 1000),
        "duration_sec": int((last_dt - first_dt).total_seconds()),
        "n_prompts": n_prompts,
        "n_tools": n_tools,
        "events": events,
    }


def pretty_worktree(folder_name, cwd):
    """Human-readable worktree name from the encoded folder or the cwd."""
    if cwd:
        base = re.split(r"[\\/]", cwd.rstrip("\\/"))[-1]
        if base:
            return base
    # Folder names encode the path with '-' separators; take the tail segment.
    name = folder_name
    for prefix in ("D--Work-Sonrisa-", "-Work-Sonrisa-"):
        if name.startswith(prefix):
            return name[len(prefix):]
    return name


def collect(projects_dir, pattern):
    worktrees = []
    for folder in sorted(glob.glob(os.path.join(projects_dir, pattern))):
        if not os.path.isdir(folder):
            continue
        folder_name = os.path.basename(folder)
        sessions = []
        for f in sorted(glob.glob(os.path.join(folder, "*.jsonl"))):
            s = load_session(f)
            if s:
                sessions.append(s)
        if not sessions:
            continue
        sessions.sort(key=lambda s: s["start_ms"])
        cwd = next((s["cwd"] for s in sessions if s["cwd"]), None)
        worktrees.append(
            {
                "folder": folder_name,
                "name": pretty_worktree(folder_name, cwd),
                "path": cwd or folder,
                "sessions": sessions,
                "start_ms": min(s["start_ms"] for s in sessions),
                "end_ms": max(s["end_ms"] for s in sessions),
            }
        )
    worktrees.sort(key=lambda w: w["start_ms"])
    return worktrees


# --------------------------------------------------------------------------- #
# HTML rendering                                                               #
# --------------------------------------------------------------------------- #
def build_html(worktrees, meta):
    # Assign a stable colour per worktree (by chronological order).
    for i, w in enumerate(worktrees):
        w["color"] = PALETTE[i % len(PALETTE)]

    data = {"worktrees": worktrees, "meta": meta}
    payload = json.dumps(data, ensure_ascii=False)
    # Guard against an accidental </script> inside the data closing the tag.
    payload = payload.replace("</", "<\\/")

    return HTML_TEMPLATE.replace("__DATA__", payload).replace(
        "__GENERATED__", html.escape(meta["generated"])
    )


HTML_TEMPLATE = r"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Claude Code Session Report</title>
<style>
  :root{
    --bg:#f6f7f9; --panel:#ffffff; --ink:#1a1d21; --muted:#6b7280;
    --line:#e4e7eb; --prompt-bg:#eef4ff; --prompt-bd:#4f7cff;
    --cmd:#8250df; --meta:#9aa0a6; --chip:#eef1f4; --accent:#4f7cff;
  }
  @media (prefers-color-scheme: dark){
    :root{
      --bg:#0f1115; --panel:#171a21; --ink:#e8eaed; --muted:#9aa0a6;
      --line:#272b33; --prompt-bg:#12203d; --prompt-bd:#5b86ff;
      --cmd:#b392f0; --meta:#6b7280; --chip:#222732; --accent:#5b86ff;
    }
  }
  *{box-sizing:border-box}
  body{margin:0;background:var(--bg);color:var(--ink);
    font:14px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,Helvetica,Arial,sans-serif;}
  a{color:var(--accent)}
  .wrap{max-width:1180px;margin:0 auto;padding:24px 16px 80px;}
  header h1{margin:0 0 4px;font-size:22px;}
  .sub{color:var(--muted);font-size:13px;margin-bottom:18px;}
  .stats{display:flex;flex-wrap:wrap;gap:10px;margin:14px 0 26px;}
  .stat{background:var(--panel);border:1px solid var(--line);border-radius:10px;
    padding:10px 14px;min-width:120px;}
  .stat b{display:block;font-size:20px;line-height:1.1;}
  .stat span{color:var(--muted);font-size:12px;}
  h2{font-size:16px;margin:34px 0 10px;padding-bottom:6px;border-bottom:1px solid var(--line);}
  .panel{background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:14px;}

  /* Gantt */
  .legend{display:flex;flex-wrap:wrap;gap:12px;margin:10px 0 8px;font-size:12px;color:var(--muted);}
  .legend span{display:inline-flex;align-items:center;gap:6px;}
  .swatch{width:12px;height:12px;border-radius:3px;display:inline-block;}
  .gantt{position:relative;overflow-x:auto;}
  .gantt-inner{position:relative;min-width:760px;}
  .axis{position:relative;height:22px;border-bottom:1px solid var(--line);margin-left:var(--lblw);}
  .tick{position:absolute;top:0;height:100%;border-left:1px solid var(--line);
    font-size:10px;color:var(--muted);padding-left:3px;white-space:nowrap;}
  .grow{position:relative;height:26px;}
  .grow .lbl{position:absolute;left:0;width:var(--lblw);height:100%;display:flex;align-items:center;
    font-size:11px;color:var(--muted);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;
    padding-right:8px;}
  .track{position:absolute;left:var(--lblw);right:0;top:0;height:100%;}
  .bar{position:absolute;top:5px;height:16px;border-radius:5px;cursor:pointer;opacity:.9;
    min-width:3px;box-shadow:0 1px 2px rgba(0,0,0,.15);}
  .bar:hover{opacity:1;outline:2px solid var(--ink);}
  .gantt-tip{position:fixed;z-index:50;background:var(--ink);color:var(--bg);
    padding:7px 9px;border-radius:7px;font-size:12px;max-width:320px;pointer-events:none;
    box-shadow:0 4px 14px rgba(0,0,0,.3);display:none;}

  /* Controls */
  .controls{display:flex;flex-wrap:wrap;gap:10px;align-items:center;margin:8px 0 4px;}
  .controls input[type=search]{flex:1;min-width:200px;padding:8px 10px;border:1px solid var(--line);
    border-radius:8px;background:var(--panel);color:var(--ink);}
  button{font:inherit;padding:7px 12px;border:1px solid var(--line);border-radius:8px;
    background:var(--panel);color:var(--ink);cursor:pointer;}
  button:hover{border-color:var(--accent);}

  /* Timeline */
  .wt{margin:22px 0;}
  .wt-head{display:flex;align-items:baseline;gap:10px;flex-wrap:wrap;}
  .wt-dot{width:12px;height:12px;border-radius:3px;}
  .wt-name{font-size:17px;font-weight:600;}
  .wt-path{color:var(--muted);font-size:12px;font-family:ui-monospace,Menlo,Consolas,monospace;}
  details.session{background:var(--panel);border:1px solid var(--line);border-radius:12px;
    margin:10px 0;overflow:hidden;}
  details.session>summary{list-style:none;cursor:pointer;padding:12px 14px;display:flex;
    gap:10px;align-items:baseline;flex-wrap:wrap;border-left:4px solid var(--sc);}
  details.session>summary::-webkit-details-marker{display:none}
  .s-title{font-weight:600;font-size:14px;}
  .s-meta{color:var(--muted);font-size:12px;}
  .s-pills{margin-left:auto;display:flex;gap:6px;flex-wrap:wrap;}
  .pill{background:var(--chip);border-radius:999px;padding:2px 9px;font-size:11px;color:var(--muted);}
  .events{padding:6px 14px 14px;border-top:1px solid var(--line);}

  .ev{margin:12px 0;}
  .ts{font-size:11px;color:var(--muted);font-family:ui-monospace,Menlo,Consolas,monospace;}
  .prompt{background:var(--prompt-bg);border-left:3px solid var(--prompt-bd);border-radius:8px;
    padding:10px 12px;margin-top:3px;}
  .prompt .who{font-weight:600;font-size:12px;color:var(--prompt-bd);margin-bottom:3px;
    text-transform:uppercase;letter-spacing:.04em;}
  .prompt.command{--prompt-bd:var(--cmd);}
  .prompt.meta{--prompt-bd:var(--meta);opacity:.75;}
  .prompt.answer{--prompt-bd:#12a594;--prompt-bg:rgba(18,165,148,.10);}
  .prompt.answer .who{color:#0e8577;}
  .qa{margin:6px 0;}
  .qa .q{font-size:12.5px;color:var(--muted);}
  .qa .a{font-size:14px;font-weight:600;}
  .qa .a::before{content:"→ ";color:#12a594;font-weight:400;}
  .txt{white-space:pre-wrap;word-break:break-word;overflow-wrap:anywhere;}
  .prompt .txt{font-size:14px;}

  details.resp{margin-top:3px;border:1px solid var(--line);border-radius:8px;background:transparent;}
  details.resp>summary{list-style:none;cursor:pointer;padding:8px 12px;display:flex;gap:8px;
    align-items:baseline;color:var(--muted);}
  details.resp>summary::-webkit-details-marker{display:none}
  details.resp>summary .who{font-weight:600;color:var(--ink);font-size:12px;}
  details.resp>summary .prev{overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1;
    font-size:12px;}
  details.resp[open]>summary .prev{display:none}
  .resp-body{padding:2px 12px 12px;}
  .resp-body .txt{font-size:13.5px;}
  .chips{display:flex;flex-wrap:wrap;gap:6px;margin:8px 0 2px;}
  .chip{background:var(--chip);border-radius:6px;padding:2px 8px;font-size:11px;
    font-family:ui-monospace,Menlo,Consolas,monospace;color:var(--muted);
    max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;}
  .chip b{color:var(--ink);font-weight:600;}
  details.think{margin-top:8px;}
  details.think>summary{cursor:pointer;font-size:11px;color:var(--muted);}
  details.think .txt{font-size:12px;color:var(--muted);border-left:2px solid var(--line);
    padding-left:8px;margin-top:6px;}
  .hidden{display:none !important;}
  mark{background:#ffe58a;color:#000;}
</style>
</head>
<body>
<div class="wrap">
  <header>
    <h1>Claude Code — Session Report</h1>
    <div class="sub">Generated __GENERATED__ · times shown in your local timezone</div>
  </header>
  <div class="stats" id="stats"></div>

  <h2>Session overlap (Gantt)</h2>
  <div class="legend" id="legend"></div>
  <div class="panel gantt"><div class="gantt-inner" id="gantt"></div></div>

  <h2>Timeline</h2>
  <div class="controls">
    <input type="search" id="search" placeholder="Filter sessions by text in prompts or title…">
    <button id="expandAll">Expand all</button>
    <button id="collapseAll">Collapse all</button>
  </div>
  <div id="timeline"></div>
</div>

<div class="gantt-tip" id="tip"></div>

<script id="data" type="application/json">__DATA__</script>
<script>
const DATA = JSON.parse(document.getElementById('data').textContent);
const esc = s => (s==null?'':String(s)).replace(/[&<>"]/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const fmt = ms => new Date(ms).toLocaleString([], {month:'short',day:'2-digit',hour:'2-digit',minute:'2-digit'});
const fmtT = ms => new Date(ms).toLocaleTimeString([], {hour:'2-digit',minute:'2-digit',second:'2-digit'});
const dur = sec => {
  if(sec<60) return sec+'s';
  const m=Math.round(sec/60); if(m<60) return m+'m';
  const h=Math.floor(m/60), r=m%60; return h+'h'+(r?' '+r+'m':'');
};

// ---- flatten sessions, compute globals ----
const worktrees = DATA.worktrees;
let allSessions = [];
worktrees.forEach(w => w.sessions.forEach(s => { s._wt = w; allSessions.push(s); }));
allSessions.sort((a,b)=>a.start_ms-b.start_ms);
const minMs = Math.min(...allSessions.map(s=>s.start_ms));
const maxMs = Math.max(...allSessions.map(s=>s.end_ms));
const totalPrompts = allSessions.reduce((n,s)=>n+s.n_prompts,0);
const totalTools = allSessions.reduce((n,s)=>n+s.n_tools,0);

// ---- stats ----
function stat(v,l){return `<div class="stat"><b>${v}</b><span>${l}</span></div>`;}
document.getElementById('stats').innerHTML =
  stat(worktrees.length,'worktrees') +
  stat(allSessions.length,'sessions') +
  stat(totalPrompts,'user prompts') +
  stat(totalTools,'tool calls') +
  stat(dur(Math.round((maxMs-minMs)/1000)),'wall-clock span');

// ---- legend ----
document.getElementById('legend').innerHTML = worktrees.map(w =>
  `<span><i class="swatch" style="background:${w.color}"></i>${esc(w.name)} · ${w.sessions.length}</span>`).join('');

// ---- gantt ----
(function(){
  const g = document.getElementById('gantt');
  const span = Math.max(1, maxMs - minMs);
  const pad = span * 0.01;
  const lo = minMs - pad, hi = maxMs + pad, range = hi - lo;
  const pct = ms => ((ms - lo) / range * 100);

  // hour ticks
  let axis = '<div class="axis">';
  const step = 60*60*1000; // 1 hour
  let t = new Date(lo); t.setMinutes(0,0,0); if(t.getTime()<lo) t=new Date(t.getTime()+step);
  for(let ms=t.getTime(); ms<hi; ms+=step){
    const left = pct(ms);
    if(left<0||left>100) continue;
    axis += `<div class="tick" style="left:${left}%">${new Date(ms).toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'})}</div>`;
  }
  axis += '</div>';

  let rows = '';
  allSessions.forEach((s,i)=>{
    const left = pct(s.start_ms), width = Math.max(0.4, pct(s.end_ms)-left);
    const label = s.title || s.file;
    rows += `<div class="grow">
      <div class="lbl" title="${esc(label)}">${esc(label)}</div>
      <div class="track">
        <div class="bar" data-i="${i}" style="left:${left}%;width:${width}%;background:${s._wt.color}"></div>
      </div></div>`;
  });
  g.style.setProperty('--lblw','190px');
  g.innerHTML = axis + rows;

  const tip = document.getElementById('tip');
  g.querySelectorAll('.bar').forEach(bar=>{
    const s = allSessions[+bar.dataset.i];
    bar.addEventListener('mousemove', e=>{
      tip.style.display='block';
      tip.style.left = Math.min(e.clientX+14, innerWidth-330)+'px';
      tip.style.top = (e.clientY+14)+'px';
      tip.innerHTML = `<b>${esc(s.title||s.file)}</b><br>${esc(s._wt.name)}`+
        (s.branch?` · <code>${esc(s.branch)}</code>`:'')+
        `<br>${fmt(s.start_ms)} → ${fmt(s.end_ms)} · ${dur(s.duration_sec)}`+
        `<br>${s.n_prompts} prompts · ${s.n_tools} tool calls`;
    });
    bar.addEventListener('mouseleave', ()=>{ tip.style.display='none'; });
    bar.addEventListener('click', ()=>{
      const el = document.getElementById('sess-'+s.id);
      if(el){ el.open = true; el.scrollIntoView({behavior:'smooth', block:'center'}); el.classList.add('flash');
        setTimeout(()=>el.classList.remove('flash'),1200); }
    });
  });
})();

// ---- timeline ----
(function(){
  const root = document.getElementById('timeline');
  let out = '';
  worktrees.forEach(w=>{
    out += `<div class="wt"><div class="wt-head">
        <i class="wt-dot" style="background:${w.color}"></i>
        <span class="wt-name">${esc(w.name)}</span>
        <span class="wt-path">${esc(w.path)}</span>
        <span class="s-meta"> · ${w.sessions.length} session${w.sessions.length>1?'s':''} · ${fmt(w.start_ms)} → ${fmt(w.end_ms)}</span>
      </div>`;
    w.sessions.forEach(s=>{
      out += renderSession(s, w);
    });
    out += `</div>`;
  });
  root.innerHTML = out;
})();

function renderSession(s, w){
  const branchPills = (s.branches||[]).map(b=>`<span class="pill">⑂ ${esc(b)}</span>`).join('');
  let ev = '';
  s.events.forEach(e=>{
    if(e.kind==='prompt'){
      const who = e.cls==='command' ? 'Command' : (e.cls==='answer' ? 'User · answered a prompt' : 'User');
      let body;
      if(e.cls==='answer' && e.qa && e.qa.length){
        body = e.qa.map(p=>`<div class="qa"><div class="q">${esc(p.q)}</div><div class="a">${esc(p.a)}</div></div>`).join('');
      } else {
        body = `<div class="txt">${esc(e.text)}</div>`;
      }
      ev += `<div class="ev"><div class="ts">${e.ts?fmtT(new Date(e.ts).getTime()):''}</div>
        <div class="prompt ${e.cls}"><div class="who">${who}</div>${body}</div></div>`;
    } else {
      const preview = (e.text||'').replace(/\s+/g,' ').slice(0,160) || (e.tools.length? e.tools.length+' tool call'+(e.tools.length>1?'s':'') : 'thinking');
      const chips = e.tools.map(t=>`<span class="chip" title="${esc(t.detail)}"><b>${esc(t.name)}</b>${t.detail?' '+esc(t.detail):''}</span>`).join('');
      const think = e.thinking ? `<details class="think"><summary>Show thinking</summary><div class="txt">${esc(e.thinking)}</div></details>`:'';
      const body = (e.text?`<div class="txt">${esc(e.text)}</div>`:'') +
                   (chips?`<div class="chips">${chips}</div>`:'') + think;
      ev += `<div class="ev"><div class="ts">${e.ts?fmtT(new Date(e.ts).getTime()):''}</div>
        <details class="resp"><summary><span class="who">Claude</span>
          <span class="prev">${esc(preview)}</span>
          <span class="s-meta">${e.tools.length?('· '+e.tools.length+'🔧'):''}</span></summary>
        <div class="resp-body">${body}</div></details></div>`;
    }
  });
  return `<details class="session" id="sess-${esc(s.id)}" style="--sc:${w.color}">
    <summary>
      <span class="s-title">${esc(s.title||s.file)}</span>
      <span class="s-meta">${fmt(s.start_ms)} → ${fmt(s.end_ms)} · ${dur(s.duration_sec)}</span>
      <span class="s-pills">
        ${s.branch?`<span class="pill">⑂ ${esc(s.branch)}</span>`:''}
        <span class="pill">${s.n_prompts} prompts</span>
        <span class="pill">${s.n_tools} tools</span>
      </span>
    </summary>
    <div class="events">${ev || '<div class="s-meta">No main-thread messages.</div>'}</div>
  </details>`;
}

// ---- controls ----
document.getElementById('expandAll').onclick = ()=>document.querySelectorAll('details.session').forEach(d=>d.open=true);
document.getElementById('collapseAll').onclick = ()=>document.querySelectorAll('details.session').forEach(d=>d.open=false);

const sessText = {};
allSessions.forEach(s=>{
  sessText[s.id] = ((s.title||'')+' '+s.events.filter(e=>e.kind==='prompt').map(e=>e.text).join(' ')).toLowerCase();
});
document.getElementById('search').addEventListener('input', e=>{
  const q = e.target.value.trim().toLowerCase();
  document.querySelectorAll('details.session').forEach(d=>{
    const id = d.id.replace(/^sess-/,'');
    const match = !q || (sessText[id]||'').includes(q);
    d.classList.toggle('hidden', !match);
    if(q && match) d.open = true;
  });
  document.querySelectorAll('.wt').forEach(wt=>{
    const anyVisible = wt.querySelector('details.session:not(.hidden)');
    wt.classList.toggle('hidden', !anyVisible);
  });
});
</script>
</body>
</html>
"""


# --------------------------------------------------------------------------- #
# Main                                                                         #
# --------------------------------------------------------------------------- #
def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--projects-dir", default=DEFAULT_PROJECTS_DIR,
                    help="Claude Code projects directory (default: %(default)s)")
    ap.add_argument("--pattern", default=DEFAULT_PATTERN,
                    help="glob for worktree folders (default: %(default)s)")
    default_out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "session-report.html")
    ap.add_argument("--out", default=default_out, help="output HTML file")
    args = ap.parse_args()

    worktrees = collect(args.projects_dir, args.pattern)
    n_sessions = sum(len(w["sessions"]) for w in worktrees)
    if not worktrees:
        print(f"No sessions found under {args.projects_dir} matching {args.pattern!r}")
        return

    meta = {
        "generated": datetime.now().astimezone().strftime("%Y-%m-%d %H:%M %Z"),
        "projects_dir": args.projects_dir,
        "pattern": args.pattern,
    }
    out_html = build_html(worktrees, meta)
    with open(args.out, "w", encoding="utf-8") as fh:
        fh.write(out_html)

    print(f"Wrote {args.out}")
    print(f"  worktrees: {len(worktrees)}  sessions: {n_sessions}")
    for w in worktrees:
        print(f"   • {w['name']:<24} {len(w['sessions'])} session(s)")


if __name__ == "__main__":
    main()
