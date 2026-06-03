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

from auth_client import resolve_auth, resolve_auth_from_eval_ctx
from ws_client import WsCollector


# ---------------------------------------------------------------------------
# helpers
# ---------------------------------------------------------------------------

def _load_json(path: str) -> dict:
    p = Path(path)
    if not p.exists():
        _emit_error(f"file not found: {path}")
        sys.exit(2)
    with open(p, encoding="utf-8-sig") as f:
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




_STOP_REASONS = {
    "goal_achieved",
    "bottom_line_violated",
    "deadlock_detected",
    "customer_gave_up",
}

_EMOTIONS = {
    "angry",
    "anxious",
    "neutral",
    "curious",
    "satisfied",
    "skeptical",
    "frustrated",
    "calmer",
    "more_upset",
}

_ABSOLUTE_EMOTIONS = {
    "angry",
    "anxious",
    "neutral",
    "curious",
    "satisfied",
    "skeptical",
    "frustrated",
}

_DELTA_EMOTION_MAP = {
    "calmer": "neutral",
    "more_upset": "frustrated",
}

_PERCEIVED_PROGRESS = {
    "none",
    "partial",
    "resolved",
    "regressed",
}

_SIMULATOR_DECISION_KEYS = {
    "turn_index",
    "should_continue",
    "stop_reason",
    "next_utterance",
    "internal_emotion",
    "perceived_progress",
    "rationale",
    "violated_bottom_line",
}

_SIMULATOR_DECISION_REQUIRED_KEYS = {
    "turn_index",
    "should_continue",
    "internal_emotion",
    "perceived_progress",
}


def _validate_simulator_decision(decision: dict, expected_turn_index: int) -> list[str]:
    """校验宿主 agent 写入 driver 前的 SimulatorDecision，避免坏 trace 落盘后再补丁修复。"""
    errors: list[str] = []

    unknown_keys = sorted(set(decision) - _SIMULATOR_DECISION_KEYS)
    if unknown_keys:
        errors.append(f"decision has unknown field(s): {unknown_keys}")

    missing_keys = sorted(_SIMULATOR_DECISION_REQUIRED_KEYS - set(decision))
    if missing_keys:
        errors.append(f"decision missing required field(s): {missing_keys}")

    turn_index = decision.get("turn_index")
    if type(turn_index) is not int:
        errors.append("decision.turn_index must be an integer")
    elif turn_index < 0:
        errors.append("decision.turn_index must be >= 0")
    elif turn_index != expected_turn_index:
        errors.append(
            f"decision.turn_index ({turn_index}) must match outer action "
            f"turn_index ({expected_turn_index})"
        )

    should_continue = decision.get("should_continue")
    if not isinstance(should_continue, bool):
        errors.append("decision.should_continue must be a boolean")

    internal_emotion = decision.get("internal_emotion")
    if internal_emotion not in _EMOTIONS:
        errors.append(
            "decision.internal_emotion must be one of "
            f"{sorted(_EMOTIONS)}, got {internal_emotion!r}"
        )

    perceived_progress = decision.get("perceived_progress")
    if perceived_progress not in _PERCEIVED_PROGRESS:
        errors.append(
            "decision.perceived_progress must be one of "
            f"{sorted(_PERCEIVED_PROGRESS)}, got {perceived_progress!r}"
        )

    stop_reason = decision.get("stop_reason")
    next_utterance = decision.get("next_utterance")
    if "next_utterance" in decision and not isinstance(next_utterance, str):
        errors.append("decision.next_utterance must be a string when present")
    if "rationale" in decision and not isinstance(decision["rationale"], str):
        errors.append("decision.rationale must be a string when present")
    if (
        "violated_bottom_line" in decision
        and not isinstance(decision["violated_bottom_line"], bool)
    ):
        errors.append("decision.violated_bottom_line must be a boolean when present")

    if should_continue is True:
        if stop_reason is not None:
            errors.append("decision.stop_reason must be null when should_continue is true")
        if "next_utterance" not in decision:
            errors.append("decision.next_utterance is required when should_continue is true")
    elif should_continue is False:
        if stop_reason not in _STOP_REASONS:
            errors.append(
                "decision.stop_reason must be a non-null enum value when "
                f"should_continue is false, got {stop_reason!r}"
            )

    if decision.get("violated_bottom_line") is True and (
        should_continue is not False or stop_reason != "bottom_line_violated"
    ):
        errors.append(
            "decision.violated_bottom_line=true requires should_continue=false "
            "and stop_reason='bottom_line_violated'"
        )

    return errors


def _resolve_driver_config(eval_ctx: dict) -> dict:
    rd = eval_ctx.get("runtime_driver") or {}
    if rd.get("driver_id") != "ws_jwt":
        _emit_error(
            f"evaluation_context.runtime_driver.driver_id is "
            f"{rd.get('driver_id')!r}, expected 'ws_jwt'"
        )
        sys.exit(2)
    cfg = dict(rd.get("driver_config") or {})
    if not cfg.get("endpoint"):
        _emit_error(
            "driver_config.endpoint is missing. STEP 3 must validate "
            "driver_config against driver.json#/config_schema before "
            "spawning this driver."
        )
        sys.exit(2)
    # token is optional when hirebot_api.auth provides client_credentials;
    # _resolve_ws_token() in main() resolves it before WsCollector is opened.
    cfg.setdefault("timeout", 60)
    cfg.setdefault("auto_approve_tools", True)
    return cfg


def _resolve_ws_token(eval_ctx: dict, cfg: dict) -> str:
    """
    Resolve the WebSocket Bearer token.

    Priority:
      1. evaluation_context.hirebot_api.auth with mode=client_credentials
         -> fresh token fetched at runtime; same Keycloak realm as HireBot REST API.
      2. driver_config.token (injected by C# at sandbox creation; may have expired).

    Exits with code 2 if neither source yields a token.
    """
    hirebot_auth_cfg = (eval_ctx.get("hirebot_api") or {}).get("auth")
    if hirebot_auth_cfg:
        try:
            resolved = resolve_auth(hirebot_auth_cfg)
            print(
                f"[ws_jwt] WebSocket token resolved via hirebot_api.auth ({resolved.source})",
                file=sys.stderr,
            )
            return resolved.access_token
        except Exception as exc:  # noqa: BLE001
            _emit_error(f"hirebot_api.auth token resolution failed: {exc}")
            sys.exit(2)

    static_token = str(cfg.get("token") or "").strip()
    if static_token:
        print(
            "[ws_jwt] WebSocket token from driver_config.token (static fallback)",
            file=sys.stderr,
        )
        return static_token

    _emit_error(
        "No WebSocket token available: hirebot_api.auth is not configured and "
        "driver_config.token is empty. Configure OpenSandbox:KingCrab credentials "
        "so the evaluator sandbox can obtain its own token."
    )
    sys.exit(2)


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
        mapped_final_emotion = _DELTA_EMOTION_MAP.get(final_emotion, final_emotion)
        if mapped_final_emotion in _ABSOLUTE_EMOTIONS:
            termination["final_emotion"] = mapped_final_emotion
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
                    raw_turn_index = cmd.get("turn_index", len(simulator_trail))
                    if type(raw_turn_index) is not int:
                        termination_reason = "evaluatee_error"
                        termination_detail = "'send' action turn_index must be an integer"
                        _emit_error(termination_detail)
                        exit_code = 2
                        break

                    turn_index = raw_turn_index
                    text = (cmd.get("text") or "").strip()
                    decision = cmd.get("decision")

                    if not isinstance(decision, dict):
                        termination_reason = "evaluatee_error"
                        termination_detail = (
                            f"'send' action requires object decision at "
                            f"turn_index={turn_index}"
                        )
                        _emit_error(termination_detail)
                        exit_code = 2
                        break

                    trail_entry = dict(decision)
                    decision_errors = _validate_simulator_decision(
                        trail_entry,
                        expected_turn_index=turn_index,
                    )
                    if decision_errors:
                        termination_reason = "evaluatee_error"
                        termination_detail = (
                            "invalid SimulatorDecision on send: "
                            + "; ".join(decision_errors)
                        )
                        _emit_error(termination_detail)
                        exit_code = 2
                        break

                    # 校验通过后才写入 simulator_trail，避免后续靠补丁修 trace。
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
                    if not isinstance(decision, dict):
                        termination_reason = "evaluatee_error"
                        termination_detail = "'end' action requires object decision"
                        _emit_error(termination_detail)
                        exit_code = 2
                        break

                    expected_turn_index = len(simulator_trail)
                    trail_entry = dict(decision)
                    decision_errors = _validate_simulator_decision(
                        trail_entry,
                        expected_turn_index=expected_turn_index,
                    )
                    if decision_errors:
                        termination_reason = "evaluatee_error"
                        termination_detail = (
                            "invalid SimulatorDecision on end: "
                            + "; ".join(decision_errors)
                        )
                        _emit_error(termination_detail)
                        exit_code = 2
                        break

                    # 终止动作同样先校验再落盘，确保 trace 首次生成即满足 schema。
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
                    # Give a targeted diagnosis when the agent sends the old
                    # WebSocket-level format {"type":"user_message",...} to
                    # driver stdin — a common confusion with the legacy
                    # evaluate.py single-layer architecture.
                    if cmd.get("type") in ("user_message", "approve_tool"):
                        _emit_error(
                            f"wrong protocol layer: received {{\"type\":\"{cmd['type']}\",...}} "
                            "on driver stdin. That format is the WebSocket wire protocol used "
                            "by ws_client.py to talk to the evaluatee — the host agent must "
                            "never write it directly. Use {\"action\":\"send\",...} or "
                            "{\"action\":\"end\",...} on driver stdin instead. "
                            "See step-03-driver-and-simulator-loop.md §4 for the exact shape."
                        )
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
                    help="path to the runtime evaluation context JSON; "
                         "use /workspace/runtime/evaluation-context.json (original, with credentials) "
                         "not a run_dir copy which may have secrets sanitized")
    ap.add_argument("--enriched-test-case", required=True,
                    help="path to one enriched test case under ./runs/<eval_id>/enriched-cases/")
    ap.add_argument("--output", required=True,
                    help="output path; MUST validate against runtime-schemas/execution_trace.schema.json")
    args = ap.parse_args()

    eval_ctx = _load_json(args.evaluation_context)
    evaluation_id = eval_ctx.get("evaluation_id") or f"eval-{uuid.uuid4().hex[:8]}"

    tc = _load_json(args.enriched_test_case)
    cfg = _resolve_driver_config(eval_ctx)
    ws_token = _resolve_ws_token(eval_ctx, cfg)
    cfg["token"] = ws_token
    simulator_id = _resolve_simulator_id(eval_ctx)
    effective_max_turns = _resolve_effective_max_turns(eval_ctx, tc)

    test_case_id = tc.get("test_case_id") or Path(args.enriched_test_case).stem
    output_path = Path(args.output)

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
        output_path=output_path,
    ))

    sys.exit(exit_code)


if __name__ == "__main__":
    main()
