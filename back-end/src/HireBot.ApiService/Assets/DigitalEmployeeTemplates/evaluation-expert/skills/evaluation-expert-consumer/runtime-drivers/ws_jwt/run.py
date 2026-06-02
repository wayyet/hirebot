"""
run.py — STEP-3-conformant entrypoint for the ws_jwt runtime driver (v2.0).

In v2.0 STEP 3 is **dual-role with asymmetric execution**:

  - driver_role  (THIS subprocess): a long-lived stdin/stdout JSON loop. It
    owns the WebSocket+JWT wire, sends customer utterances to the evaluatee,
    collects assistant_done turns, and writes the final ExecutionTrace. It
    makes NO decisions about what the customer says or when to stop.

  - simulator_role (NOT a subprocess): the host evaluation-expert agent
    itself, using its own LLM brain, plays the customer. It feeds decisions
    into THIS driver's stdin and reads the evaluatee's replies from THIS
    driver's stdout.

Wire protocol (line-delimited JSON, one JSON object per line):

  driver -> agent (stdout):
    {"event":"ready","driver_id":"ws_jwt","effective_max_turns":N}
    {"event":"evaluatee_turn","turn_index":N,"content":"...","tool_calls":[...],"raw_messages":[...]}
    {"event":"trace_written","path":"..."}
    {"event":"error","detail":"..."}                  # any unrecoverable failure

  agent -> driver (stdin):
    {"action":"send","turn_index":N,"text":"...","decision":{...full SimulatorDecision...}}
    {"action":"end","decision":{...final SimulatorDecision...},
     "termination":{"reason":"...", "detail":"...", "final_emotion":"...", "turns_used":N}}

Lifecycle:
  1. Spawn with --evaluation-context, --enriched-test-case, --output.
  2. Load eval_ctx + enriched_tc, validate driver_config, open WS, emit "ready".
  3. Loop reading stdin lines:
       - on "send": cache decision into simulator_trail; if turn_index==0 just
         record + send via WS without expecting a prior reply; collect the
         evaluatee turn; emit "evaluatee_turn".
       - on "end": cache final decision; assemble ExecutionTrace; write to
         --output; emit "trace_written"; close WS; exit 0.
  4. Any I/O / protocol error is surfaced as {"event":"error","detail":...},
     a best-effort partial trace is still written, and the driver exits 2.

This file remains the ONLY runtime entry that talks to the evaluatee for
protocol=websocket+jwt. It still does not score, never raises observed_signals,
never judges red lines.
"""

import argparse
import asyncio
import json
import os
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from ws_client import WsCollector


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _load_json(path: str) -> dict:
    p = Path(path)
    if not p.exists():
        _emit_error(f"file not found: {path}")
        sys.exit(2)
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


def _emit(obj: dict) -> None:
    """Write one JSON object as a line to stdout and flush immediately.

    The host agent reads this stdout line-by-line, so flushing is mandatory.
    """
    sys.stdout.write(json.dumps(obj, ensure_ascii=False))
    sys.stdout.write("\n")
    sys.stdout.flush()


def _emit_error(detail: str) -> None:
    _emit({"event": "error", "detail": detail})


def _resolve_driver_config(eval_ctx: dict) -> dict:
    rd = eval_ctx.get("runtime_driver") or {}
    if rd.get("driver_id") != "ws_jwt":
        _emit_error(
            f"evaluation_context.runtime_driver.driver_id is "
            f"{rd.get('driver_id')!r}, expected 'ws_jwt'"
        )
        sys.exit(2)
    cfg = dict(rd.get("driver_config") or {})
    for required in ("endpoint", "token"):
        if not cfg.get(required):
            _emit_error(
                f"driver_config.{required} is missing. STEP 3 must validate "
                f"driver_config against driver.json#/config_schema before "
                f"spawning this driver."
            )
            sys.exit(2)
    cfg.setdefault("timeout", 60)
    cfg.setdefault("auto_approve_tools", True)
    return cfg


def _resolve_simulator_id(eval_ctx: dict) -> str:
    """Best-effort capture of simulator_id for trace audit; not used to spawn anything."""
    rs = eval_ctx.get("runtime_simulator") or {}
    sim_id = rs.get("simulator_id") or os.environ.get("EVALUATION_SIMULATOR_ID")
    if not sim_id:
        _emit_error(
            "evaluation_context.runtime_simulator.simulator_id is empty. "
            "STEP 3 v2.0 requires a simulator role profile."
        )
        sys.exit(2)
    return sim_id


def _resolve_effective_max_turns(eval_ctx: dict, tc: dict) -> int:
    tc_budget = (tc.get("turn_budget") or {}).get("hard_max_turns")
    global_cap = eval_ctx.get("global_turn_cap") or 30
    if isinstance(tc_budget, int) and tc_budget > 0:
        return min(int(tc_budget), int(global_cap))
    return int(global_cap)


def _classify_outcome(msg: dict) -> str:
    t = msg.get("type")
    if t == "tool_result":
        return "success"
    if t == "tool_error":
        return "error"
    if t == "tool_timeout":
        return "timeout"
    if t == "tool_rejected" or msg.get("approved") is False:
        return "rejected"
    return "success"


# ---------------------------------------------------------------------------
# raw_messages -> ExecutionTrace mapping
# ---------------------------------------------------------------------------

def _flatten_assistant_text(turn_messages: list[dict]) -> str:
    parts: list[str] = []
    for m in turn_messages:
        t = m.get("type")
        if t == "assistant_message":
            parts.append(m.get("content") or m.get("text") or "")
        elif t in ("text_delta", "assistant_chunk"):
            parts.append(m.get("delta") or m.get("text") or "")
    return "".join(parts).strip()


def _extract_tool_calls(
    turn_messages: list[dict],
    after_turn_index: int,
) -> list[dict]:
    out: list[dict] = []
    pending: dict[str, Any] | None = None

    for m in turn_messages:
        t = m.get("type")
        ts = m.get("_received_at") or _now_iso()

        if t == "tool_start":
            pending = {
                "tool_name": m.get("text") or m.get("tool_name") or "unknown",
                "called_at": ts,
                "arguments": m.get("parameters") or m.get("args") or {},
                "outcome": "success",
                "after_turn_index": after_turn_index,
            }
        elif t == "tool_result" and pending is not None:
            pending["outcome"] = "success"
            out.append(pending)
            pending = None
        elif t in ("tool_error", "tool_timeout", "tool_rejected") and pending is not None:
            pending["outcome"] = _classify_outcome(m)
            err = m.get("text") or m.get("error") or m.get("message")
            if err:
                pending["error_message"] = str(err)
            out.append(pending)
            pending = None
        elif t == "tool_call":
            entry = {
                "tool_name": m.get("toolName") or m.get("tool_name") or m.get("name") or "unknown",
                "called_at": ts,
                "arguments": m.get("parameters") or m.get("args") or {},
                "outcome": "success",
                "after_turn_index": after_turn_index,
            }
            out.append(entry)

    if pending is not None:
        pending["outcome"] = "timeout"
        pending["error_message"] = "no matching tool_result before turn ended"
        out.append(pending)

    return out


def _has_error(turn_messages: list[dict]) -> tuple[bool, str | None]:
    for m in turn_messages:
        if m.get("type") == "error":
            return True, str(m.get("message") or m.get("error") or m)
    return False, None


# ---------------------------------------------------------------------------
# stdin reader (async)
# ---------------------------------------------------------------------------

async def _read_stdin_line(loop: asyncio.AbstractEventLoop) -> str | None:
    """Read one line from stdin without blocking the event loop. None on EOF."""
    line = await loop.run_in_executor(None, sys.stdin.readline)
    if line == "":
        return None
    return line.rstrip("\n")


# ---------------------------------------------------------------------------
# main async loop — driven by host agent via stdin
# ---------------------------------------------------------------------------

async def _serve(
    evaluation_id: str,
    test_case_id: str,
    cfg: dict,
    simulator_id: str,
    effective_max_turns: int,
    output_path: Path,
) -> int:
    """Run the long-lived driver loop. Returns the process exit code."""
    started_at = _now_iso()
    dialog_turns: list[dict] = []
    actual_tool_calls: list[dict] = []
    simulator_trail: list[dict] = []
    termination_reason = "completed_normally"
    termination_detail: str | None = None
    final_emotion: str | None = None
    turns_used = 0

    auto_approve = bool(cfg["auto_approve_tools"])
    loop = asyncio.get_event_loop()
    exit_code = 0

    def _write_trace_file() -> None:
        ended_at = _now_iso()
        termination: dict[str, Any] = {"reason": termination_reason}
        if termination_detail:
            termination["detail"] = termination_detail
        if final_emotion in {
            "angry", "anxious", "neutral", "curious",
            "satisfied", "skeptical", "frustrated",
        }:
            termination["final_emotion"] = final_emotion
        termination["turns_used"] = turns_used

        trace: dict[str, Any] = {
            "evaluation_id": evaluation_id,
            "test_case_id": test_case_id,
            "simulator_id": simulator_id,
            "started_at": started_at,
            "ended_at": ended_at,
            "dialog_turns": dialog_turns,
            "actual_tool_calls": actual_tool_calls,
            "simulator_trail": simulator_trail,
            "termination": termination,
        }
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(trace, f, ensure_ascii=False, indent=2)

    try:
        async with WsCollector(cfg["endpoint"], cfg["token"], timeout=int(cfg["timeout"])) as ws:
            _emit({
                "event": "ready",
                "driver_id": "ws_jwt",
                "effective_max_turns": effective_max_turns,
                "evaluation_id": evaluation_id,
                "test_case_id": test_case_id,
            })

            while True:
                line = await _read_stdin_line(loop)
                if line is None:
                    termination_reason = "evaluatee_error"
                    termination_detail = "stdin closed before 'end' action received"
                    exit_code = 2
                    break

                line = line.strip()
                if not line:
                    continue

                try:
                    cmd = json.loads(line)
                except json.JSONDecodeError as e:
                    _emit_error(f"invalid JSON on stdin: {e}; raw={line[:200]!r}")
                    continue

                action = cmd.get("action")

                if action == "send":
                    turn_index = int(cmd.get("turn_index", len(simulator_trail)))
                    text = (cmd.get("text") or "").strip()
                    decision = cmd.get("decision") or {}

                    # cache the decision into simulator_trail with timestamp
                    trail_entry = dict(decision)
                    trail_entry.setdefault("turn_index", turn_index)
                    trail_entry["decided_at"] = _now_iso()
                    simulator_trail.append(trail_entry)
                    final_emotion = decision.get("internal_emotion") or final_emotion

                    if not text:
                        _emit_error(
                            f"'send' action with empty text at turn_index={turn_index}"
                        )
                        termination_reason = "evaluatee_error"
                        termination_detail = "host agent issued empty 'send' utterance"
                        exit_code = 2
                        break

                    # record the customer turn
                    dialog_turns.append({
                        "turn_index": turn_index,
                        "actor": "evaluator",
                        "content": text,
                        "timestamp": _now_iso(),
                    })

                    # drive the evaluatee
                    try:
                        raw = await ws.send_and_collect(text)
                    except asyncio.TimeoutError:
                        termination_reason = "timeout"
                        _emit_error(f"evaluatee response timeout at turn_index={turn_index}")
                        exit_code = 2
                        break
                    except Exception as e:  # noqa: BLE001
                        termination_reason = "evaluatee_error"
                        termination_detail = f"{type(e).__name__}: {e}"
                        _emit_error(termination_detail)
                        exit_code = 2
                        break

                    if auto_approve:
                        for m in raw:
                            if m.get("type") == "approval_required":
                                call_id = m.get("callId")
                                if call_id:
                                    await ws.approve_tool(call_id, approved=True)

                    evaluatee_text = _flatten_assistant_text(raw)
                    dialog_turns.append({
                        "turn_index": turn_index,
                        "actor": "evaluatee",
                        "content": evaluatee_text,
                        "timestamp": _now_iso(),
                    })
                    new_tool_calls = _extract_tool_calls(raw, after_turn_index=turn_index)
                    actual_tool_calls.extend(new_tool_calls)
                    turns_used = turn_index + 1

                    err, err_msg = _has_error(raw)
                    if err:
                        termination_reason = "evaluatee_error"
                        termination_detail = err_msg
                        _emit({
                            "event": "evaluatee_turn",
                            "turn_index": turn_index,
                            "content": evaluatee_text,
                            "tool_calls": new_tool_calls,
                            "raw_messages": raw,
                            "error": err_msg,
                        })
                        exit_code = 2
                        break

                    _emit({
                        "event": "evaluatee_turn",
                        "turn_index": turn_index,
                        "content": evaluatee_text,
                        "tool_calls": new_tool_calls,
                        "raw_messages": raw,
                    })

                    if turns_used >= effective_max_turns:
                        # hard cap reached; let the host agent decide whether
                        # to send 'end' with reason=max_turns_reached or to
                        # squeeze in a final 'send'. We do NOT auto-end here
                        # so the agent stays in control.
                        pass

                elif action == "end":
                    decision = cmd.get("decision")
                    if isinstance(decision, dict):
                        trail_entry = dict(decision)
                        trail_entry.setdefault("turn_index", len(simulator_trail))
                        trail_entry["decided_at"] = _now_iso()
                        simulator_trail.append(trail_entry)
                        final_emotion = decision.get("internal_emotion") or final_emotion

                    term = cmd.get("termination") or {}
                    termination_reason = term.get("reason") or termination_reason
                    termination_detail = term.get("detail") or termination_detail
                    final_emotion = term.get("final_emotion") or final_emotion
                    if "turns_used" in term:
                        try:
                            turns_used = int(term["turns_used"])
                        except (TypeError, ValueError):
                            pass
                    break

                else:
                    _emit_error(f"unknown action {action!r}; expected 'send' or 'end'")
                    continue

    except asyncio.TimeoutError:
        termination_reason = "timeout"
        exit_code = 2
    except Exception as e:  # noqa: BLE001
        termination_reason = "evaluatee_error"
        termination_detail = f"{type(e).__name__}: {e}"
        _emit_error(termination_detail)
        exit_code = 2

    # write trace regardless of how we got here (best-effort partial trace
    # on failure paths)
    try:
        _write_trace_file()
        _emit({
            "event": "trace_written",
            "path": str(output_path),
            "termination": {
                "reason": termination_reason,
                "turns_used": turns_used,
            },
        })
    except Exception as e:  # noqa: BLE001
        _emit_error(f"failed to write trace: {type(e).__name__}: {e}")
        exit_code = 2

    return exit_code


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main() -> None:
    ap = argparse.ArgumentParser(
        description="ws_jwt runtime driver — STEP 3 (v2.0, long-lived stdin/stdout protocol). "
                    "The host agent drives turns via JSON lines on stdin; "
                    "the driver streams evaluatee replies back on stdout.",
    )
    ap.add_argument("--evaluation-context", required=True,
                    help="path to ./runs/<eval_id>/evaluation_context.json")
    ap.add_argument("--enriched-test-case", required=True,
                    help="path to one enriched test case under ./runs/<eval_id>/enriched-cases/")
    ap.add_argument("--output", required=True,
                    help="output path; MUST validate against runtime-schemas/execution_trace.schema.json")
    args = ap.parse_args()

    eval_ctx = _load_json(args.evaluation_context)
    tc = _load_json(args.enriched_test_case)
    cfg = _resolve_driver_config(eval_ctx)
    simulator_id = _resolve_simulator_id(eval_ctx)
    effective_max_turns = _resolve_effective_max_turns(eval_ctx, tc)

    evaluation_id = eval_ctx.get("evaluation_id") or f"eval-{uuid.uuid4().hex[:8]}"
    test_case_id = tc.get("test_case_id") or Path(args.enriched_test_case).stem

    inp = tc.get("input") or {}
    if not (inp.get("opening_message") or inp.get("user_message")):
        _emit_error(
            f"enriched_test_case.input has neither opening_message nor "
            f"(deprecated) user_message for {test_case_id}"
        )
        sys.exit(2)

    exit_code = asyncio.run(_serve(
        evaluation_id=evaluation_id,
        test_case_id=test_case_id,
        cfg=cfg,
        simulator_id=simulator_id,
        effective_max_turns=effective_max_turns,
        output_path=Path(args.output),
    ))

    sys.exit(exit_code)


if __name__ == "__main__":
    main()
