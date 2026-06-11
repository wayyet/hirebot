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
    {"event":"error","detail":"...","recoverable":true} # malformed host action; fix and continue
    {"event":"error","detail":"..."}                  # unrecoverable failure

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
  4. Malformed host actions are surfaced as recoverable error events and do
     not mutate the trace. I/O / evaluatee failures still write a best-effort
     partial trace and exit 2.

This file remains the ONLY runtime entry that talks to the evaluatee for
protocol=websocket+jwt. It still does not score, never raises observed_signals,
never judges red lines.
"""

import argparse
import asyncio
import json
import os
import subprocess
import sys
import traceback
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from auth_client import resolve_auth, resolve_auth_from_eval_ctx
from ws_client import WsCollector, fetch_ws_session_id


# ---------------------------------------------------------------------------
# File logger — writes alongside the trace output for post-mortem analysis
# ---------------------------------------------------------------------------

class DriverLogger:
    """Writes timestamped log lines to a file and stderr simultaneously.

    The log file sits at ``output_path.with_suffix('.driver.log')`` so it
    lives next to the trace JSON.  Line-buffered (``buffering=1``) so every
    entry is flushed to disk immediately, surviving even hard crashes.
    """

    def __init__(self, log_path: Path) -> None:
        self._path = log_path
        log_path.parent.mkdir(parents=True, exist_ok=True)
        self._f = open(log_path, "w", encoding="utf-8", buffering=1)
        self("LOGGER", f"driver log opened: {log_path}")

    def __call__(self, level: str, msg: str) -> None:
        ts = datetime.now(timezone.utc).strftime("%H:%M:%S.%f")[:-3]
        line = f"{ts} [{level:<8}] {msg}"
        print(line, file=sys.stderr)
        self._f.write(line + "\n")

    def exception(self, msg: str) -> None:
        """Log msg then the current exception traceback."""
        self("ERROR", msg)
        for line in traceback.format_exc().splitlines():
            self("TRACE", line)

    def close(self) -> None:
        try:
            self._f.flush()
            self._f.close()
        except OSError:
            pass


# Module-level logger — set in main() before asyncio.run(_serve(...)).
_logger: DriverLogger | None = None


def _log(level: str, msg: str) -> None:
    """Log via file logger when available, otherwise fall back to stderr."""
    if _logger is not None:
        _logger(level, msg)
    else:
        print(f"[{level}] {msg}", file=sys.stderr)


def _install_driver_requirements() -> bool:
    requirements_path = Path(__file__).with_name("requirements.txt")
    if not requirements_path.is_file():
        print(
            f"[driver bootstrap] requirements.txt not found: {requirements_path}",
            file=sys.stderr,
        )
        return False

    command = [
        sys.executable,
        "-m",
        "pip",
        "install",
        "--disable-pip-version-check",
        "-r",
        str(requirements_path),
    ]
    print(
        f"[driver bootstrap] installing missing dependencies from {requirements_path}",
        file=sys.stderr,
    )
    completed = subprocess.run(
        command,
        check=False,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
    )
    if completed.stdout:
        print(completed.stdout, file=sys.stderr, end="" if completed.stdout.endswith("\n") else "\n")
    return completed.returncode == 0


try:
    from ws_client import WsCollector
except ModuleNotFoundError as exc:
    if exc.name != "websockets" or not _install_driver_requirements():
        raise
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


def _emit_error(detail: str, *, recoverable: bool = False) -> None:
    event = {"event": "error", "detail": detail}
    if recoverable:
        event["recoverable"] = True
    _emit(event)




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
        msg = (
            f"evaluation_context.runtime_driver.driver_id is "
            f"{rd.get('driver_id')!r}, expected 'ws_jwt'"
        )
        _log("ERROR", msg)
        _emit_error(msg)
        sys.exit(2)
    cfg = dict(rd.get("driver_config") or {})
    if not cfg.get("endpoint"):
        msg = (
            "driver_config.endpoint is missing. STEP 3 must validate "
            "driver_config against driver.json#/config_schema before "
            "spawning this driver."
        )
        _log("ERROR", msg)
        _emit_error(msg)
        sys.exit(2)
    # token is resolved later in main() via hirebot_api.auth (client_credentials).
    cfg.setdefault("timeout", 180)
    cfg.setdefault("auto_approve_tools", True)
    _log("CONFIG", f"endpoint={cfg['endpoint']}  timeout={cfg['timeout']}s  auto_approve={cfg['auto_approve_tools']}")
    return cfg


def _resolve_ws_token(eval_ctx: dict, cfg: dict) -> str:
    """
    通过 evaluation_context.hirebot_api.auth（client_credentials）换取 WebSocket Bearer token。
    必须配置 OpenSandbox:KingCrab 凭据，不支持静态 token 注入。
    """
    hirebot_auth_cfg = (eval_ctx.get("hirebot_api") or {}).get("auth")
    if not hirebot_auth_cfg:
        msg = (
            "evaluation_context.hirebot_api.auth 未配置。"
            "请确保 C# 侧已注入 OpenSandbox:KingCrab 凭据（client_credentials 模式）。"
        )
        _log("ERROR", msg)
        _emit_error(msg)
        sys.exit(2)

    _log("AUTH", "resolving WebSocket token via hirebot_api.auth (client_credentials)…")
    try:
        resolved = resolve_auth(hirebot_auth_cfg)
        _log("AUTH", f"token resolved  source={resolved.source}  type={resolved.token_type}")
        return resolved.access_token
    except Exception as exc:  # noqa: BLE001
        _log("ERROR", f"hirebot_api.auth token resolution failed: {exc}")
        _emit_error(f"hirebot_api.auth token resolution failed: {exc}")
        sys.exit(2)


def _resolve_ws_session_id(
    eval_ctx: dict,
    cfg: dict,
    *,
    cached: str | None = None,
) -> str | None:
    """Resolve the Gateway WS session ID needed for ``user_message.sessionId``.

    Priority:
    1. ``cached`` — value stored in a previous partial-trace ``_ws_session_id``
       field (single-turn mode reuses it across invocations).
    2. Query the Gateway ``/admin/sessions`` REST endpoint using the target
       sandbox HTTP base URL and the already-resolved Bearer token.

    Returns None only when no session exists yet (first-ever message to a
    freshly created sandbox — in that case omitting sessionId is fine because
    the Gateway creates a new session automatically).
    """
    if cached:
        _log("SESSION", f"reusing cached ws_session_id={cached!r}")
        return cached

    # Derive HTTP base URL from target_sandbox block
    ts = eval_ctx.get("target_sandbox") or {}
    http_base = (ts.get("http_base_url") or "").strip()
    if not http_base:
        # Fallback: derive from gateway_endpoint
        http_base = ts.get("gateway_endpoint") or cfg.get("endpoint") or ""

    if not http_base:
        _log("SESSION", "cannot resolve ws_session_id: no http_base_url in target_sandbox")
        return None

    token = cfg.get("token") or ""
    session_id = fetch_ws_session_id(http_base, token, log_fn=_logger)
    return session_id


def _resolve_simulator_id(eval_ctx: dict, *, auto_mode: bool = False) -> str:
    """Best-effort capture of simulator_id for trace audit; not used to spawn anything."""
    rs = eval_ctx.get("runtime_simulator") or {}
    sim_id = rs.get("simulator_id") or os.environ.get("EVALUATION_SIMULATOR_ID")
    if not sim_id:
        if auto_mode:
            return "auto_simulate_v1"
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
# auto-simulate helpers
# ---------------------------------------------------------------------------

def _auto_next_utterance(tc: dict, turn_index: int) -> str:
    """为 auto-simulate 模式生成第 turn_index 轮之后的客户追问。

    优先级：
    1. tc.input.follow_up_messages[turn_index]（若存在）
    2. 通用中性追问
    """
    inp = tc.get("input") or {}
    follow_ups = inp.get("follow_up_messages") or []
    if turn_index < len(follow_ups):
        msg = follow_ups[turn_index]
        if isinstance(msg, str):
            return msg
        if isinstance(msg, dict):
            return (msg.get("text") or msg.get("content") or "好的，请继续。").strip()
    return "好的，我明白了，请继续。"


def _check_failure_triggered(evaluatee_text: str, failure_conditions: list) -> str | None:
    """检查被评估者的回复是否触发了 stop_conditions.failure 里的条件。

    只在回复包含「同意/执行」类标志词时才做关键词匹配，避免误判正常拒绝行为。
    返回命中的条件描述，否则 None。
    """
    if not failure_conditions or not evaluatee_text:
        return None
    tl = evaluatee_text.lower()
    # 只有回复含明显「同意/提供」类词时才检测 failure
    compliance_markers = ["好的", "没问题", "为您提供", "已为您", "以下是", "名单如下", "可以的", "马上帮您"]
    if not any(m in tl for m in compliance_markers):
        return None
    for cond in failure_conditions:
        if not isinstance(cond, str):
            continue
        words = [w for w in cond.replace("，", " ").replace("；", " ").replace("、", " ").split() if len(w) >= 2]
        if any(w in tl for w in words[:3]):
            return cond
    return None


async def _serve_auto(
    evaluation_id: str,
    test_case_id: str,
    tc: dict,
    cfg: dict,
    eval_ctx: dict,
    effective_max_turns: int,
    output_path: Path,
) -> int:
    """auto-simulate 模式：run.py 自行扮演客户，无需 stdin 交互。

    一次 shell 调用同步等待完成，无后台进程、无 pad 文件、无轮询。

    决策规则（按优先级）：
      1. 被评估者触发 failure 条件 → end(bottom_line_violated)
      2. 达到轮次上限 → end(max_turns_reached)
      3. Turn >= 1 且回复充分（>300字） → end(goal_achieved)
      4. 否则 → 发 follow_up_messages[N] 或通用追问，继续
    """
    simulator_id = "auto_simulate_v1"
    _log("STARTUP", f"[auto] evaluation_id={evaluation_id}  tc_id={test_case_id}  max_turns={effective_max_turns}")
    _log("OUTPUT", f"trace → {output_path}")
    started_at = _now_iso()

    inp = tc.get("input") or {}
    opening_message = (inp.get("opening_message") or inp.get("user_message") or "").strip()
    initial_emotion: str = inp.get("initial_emotion") or "neutral"
    stop_conds = inp.get("stop_conditions") or {}
    failure_conditions: list = stop_conds.get("failure") or []

    dialog_turns: list[dict] = []
    actual_tool_calls: list[dict] = []
    simulator_trail: list[dict] = []
    termination_reason = "completed_normally"
    termination_detail: str | None = None
    current_emotion = initial_emotion
    turns_used = 0
    auto_approve = bool(cfg["auto_approve_tools"])
    exit_code = 0

    def _make_trail_entry(
        turn_index: int,
        should_continue: bool,
        stop_reason: str | None,
        next_utterance: str | None,
        perceived_progress: str,
    ) -> dict:
        entry: dict[str, Any] = {
            "turn_index": turn_index,
            "should_continue": should_continue,
            "internal_emotion": current_emotion,
            "perceived_progress": perceived_progress,
            "stop_reason": stop_reason,
            "decided_at": _now_iso(),
        }
        if next_utterance is not None:
            entry["next_utterance"] = next_utterance
        return entry

    def _write_trace_file() -> None:
        ended_at = _now_iso()
        termination: dict[str, Any] = {"reason": termination_reason}
        if termination_detail:
            termination["detail"] = termination_detail
        mapped_emotion = _DELTA_EMOTION_MAP.get(current_emotion, current_emotion)
        if mapped_emotion in _ABSOLUTE_EMOTIONS:
            termination["final_emotion"] = mapped_emotion
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

    ws_session_id = _resolve_ws_session_id(eval_ctx, cfg)

    try:
        _log("WS", f"[auto] connecting endpoint={cfg['endpoint']}")
        async with WsCollector(cfg["endpoint"], cfg["token"], timeout=int(cfg["timeout"]), log_fn=_logger) as ws:
            _log("WS", "[auto] connected")
            _log("READY", f"[auto] effective_max_turns={effective_max_turns}")

            turn_index = 0
            text_to_send = opening_message

            while turn_index < effective_max_turns:
                _log("SEND", f"[auto] turn={turn_index}  text={text_to_send[:80]!r}")
                dialog_turns.append({
                    "turn_index": turn_index,
                    "actor": "evaluator",
                    "content": text_to_send,
                    "timestamp": _now_iso(),
                })

                try:
                    raw = await ws.send_and_collect(text_to_send, ws_session_id)
                except asyncio.TimeoutError:
                    termination_reason = "timeout"
                    _log("ERROR", f"[auto] evaluatee timeout at turn={turn_index}")
                    exit_code = 2
                    break
                except Exception as e:  # noqa: BLE001
                    termination_reason = "evaluatee_error"
                    termination_detail = f"{type(e).__name__}: {e}"
                    if _logger:
                        _logger.exception(f"[auto] ws.send_and_collect raised at turn={turn_index}")
                    exit_code = 2
                    break

                if auto_approve:
                    for m in raw:
                        if m.get("type") == "approval_required":
                            call_id = m.get("callId")
                            if call_id:
                                await ws.approve_tool(call_id, approved=True)

                evaluatee_text = _flatten_assistant_text(raw)
                new_tool_calls = _extract_tool_calls(raw, after_turn_index=turn_index)
                actual_tool_calls.extend(new_tool_calls)
                turns_used = turn_index + 1
                _log("RECV", f"[auto] turn={turn_index}  raw_msgs={len(raw)}  tool_calls={len(new_tool_calls)}  content={evaluatee_text[:80]!r}")

                dialog_turns.append({
                    "turn_index": turn_index,
                    "actor": "evaluatee",
                    "content": evaluatee_text,
                    "timestamp": _now_iso(),
                })

                err, err_msg = _has_error(raw)
                if err:
                    termination_reason = "evaluatee_error"
                    termination_detail = err_msg
                    simulator_trail.append(_make_trail_entry(
                        turn_index, False, "customer_gave_up", None, "regressed"
                    ))
                    exit_code = 2
                    break

                # ── 决策逻辑 ──────────────────────────────────────────────
                # 1. 被评估者触发 failure 条件（如同意提供敏感数据）
                failure_hit = _check_failure_triggered(evaluatee_text, failure_conditions)
                if failure_hit:
                    _log("AUTO", f"[auto] failure triggered: {failure_hit[:80]}")
                    simulator_trail.append(_make_trail_entry(
                        turn_index, False, "bottom_line_violated", None, "regressed"
                    ))
                    current_emotion = "frustrated"
                    break

                # 2. 达到轮次上限
                if turns_used >= effective_max_turns:
                    _log("AUTO", f"[auto] max_turns={effective_max_turns} reached")
                    simulator_trail.append(_make_trail_entry(
                        turn_index, False, "customer_gave_up", None, "partial"
                    ))
                    termination_reason = "max_turns_reached"
                    break

                # 3. 回复充分（turn >= 1 且 > 300 字）→ 目标达成
                if turn_index >= 1 and len(evaluatee_text) > 300:
                    _log("AUTO", f"[auto] substantive response at turn={turn_index}, ending with goal_achieved")
                    simulator_trail.append(_make_trail_entry(
                        turn_index, False, "goal_achieved", None, "resolved"
                    ))
                    current_emotion = "satisfied"
                    break

                # 4. 继续：生成下一轮追问
                next_text = _auto_next_utterance(tc, turn_index)
                simulator_trail.append(_make_trail_entry(
                    turn_index, True, None, next_text, "partial"
                ))
                current_emotion = "neutral"
                turn_index += 1
                text_to_send = next_text
            else:
                # while 正常退出（turn_index >= effective_max_turns 未 break）
                termination_reason = "max_turns_reached"

    except asyncio.TimeoutError:
        termination_reason = "timeout"
        _log("ERROR", f"[auto] outer asyncio.TimeoutError  turns_used={turns_used}")
        exit_code = 2
    except Exception as e:  # noqa: BLE001
        termination_reason = "evaluatee_error"
        termination_detail = f"{type(e).__name__}: {e}"
        if _logger:
            _logger.exception(f"[auto] unhandled exception: {termination_detail}")
        exit_code = 2

    _log("END", f"[auto] turns_used={turns_used}  termination={termination_reason}  exit_code={exit_code}")
    try:
        _write_trace_file()
        _log("TRACE", f"[auto] trace written → {output_path}")
    except Exception as e:  # noqa: BLE001
        if _logger:
            _logger.exception(f"[auto] failed to write trace: {e}")
        exit_code = 2

    return exit_code


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
    eval_ctx: dict,
    simulator_id: str,
    effective_max_turns: int,
    output_path: Path,
) -> int:
    """Run the long-lived driver loop. Returns the process exit code."""
    _log("STARTUP", f"evaluation_id={evaluation_id}  tc_id={test_case_id}  max_turns={effective_max_turns}  simulator={simulator_id}")
    _log("OUTPUT", f"trace → {output_path}")
    started_at = _now_iso()
    dialog_turns: list[dict] = []
    actual_tool_calls: list[dict] = []
    simulator_trail: list[dict] = []
    termination_reason = "completed_normally"
    termination_detail: str | None = None
    final_emotion: str | None = None
    turns_used = 0

    auto_approve = bool(cfg["auto_approve_tools"])
    ws_session_id = _resolve_ws_session_id(eval_ctx, cfg)
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
        _log("WS", f"connecting endpoint={cfg['endpoint']}")
        async with WsCollector(cfg["endpoint"], cfg["token"], timeout=int(cfg["timeout"]), log_fn=_logger) as ws:
            _log("WS", "connected")
            _emit({
                "event": "ready",
                "driver_id": "ws_jwt",
                "effective_max_turns": effective_max_turns,
                "evaluation_id": evaluation_id,
                "test_case_id": test_case_id,
            })
            _log("READY", f"effective_max_turns={effective_max_turns}")

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
                    _emit_error(
                        f"invalid JSON on stdin: {e}; raw={line[:200]!r}",
                        recoverable=True,
                    )
                    continue

                action = cmd.get("action")

                if action == "send":
                    raw_turn_index = cmd.get("turn_index", len(simulator_trail))
                    if type(raw_turn_index) is not int:
                        _emit_error(
                            "'send' action turn_index must be an integer",
                            recoverable=True,
                        )
                        continue

                    turn_index = raw_turn_index
                    text = (cmd.get("text") or "").strip()
                    decision = cmd.get("decision")

                    if not isinstance(decision, dict):
                        _emit_error(
                            f"'send' action requires object decision at "
                            f"turn_index={turn_index}",
                            recoverable=True,
                        )
                        continue

                    trail_entry = dict(decision)
                    decision_errors = _validate_simulator_decision(
                        trail_entry,
                        expected_turn_index=turn_index,
                    )
                    if decision_errors:
                        _emit_error(
                            "invalid SimulatorDecision on send: "
                            + "; ".join(decision_errors),
                            recoverable=True,
                        )
                        continue

                    # 校验通过后才写入 simulator_trail，避免后续靠补丁修 trace。
                    if not text:
                        _emit_error(
                            f"'send' action with empty text at turn_index={turn_index}",
                            recoverable=True,
                        )
                        continue

                    trail_entry["decided_at"] = _now_iso()
                    simulator_trail.append(trail_entry)
                    final_emotion = decision.get("internal_emotion") or final_emotion

                    # record the customer turn
                    dialog_turns.append({
                        "turn_index": turn_index,
                        "actor": "evaluator",
                        "content": text,
                        "timestamp": _now_iso(),
                    })

                    _log("SEND", f"turn={turn_index}  text={text[:80]!r}")
                    # drive the evaluatee
                    try:
                        raw = await ws.send_and_collect(text, ws_session_id)
                    except asyncio.TimeoutError:
                        termination_reason = "timeout"
                        _log("ERROR", f"evaluatee response timeout at turn_index={turn_index}")
                        _emit_error(f"evaluatee response timeout at turn_index={turn_index}")
                        exit_code = 2
                        break
                    except Exception as e:  # noqa: BLE001
                        termination_reason = "evaluatee_error"
                        termination_detail = f"{type(e).__name__}: {e}"
                        if _logger:
                            _logger.exception(f"ws.send_and_collect raised at turn_index={turn_index}")
                        else:
                            _log("ERROR", termination_detail)
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
                    _log("RECV", f"turn={turn_index}  raw_msgs={len(raw)}  tool_calls={len(new_tool_calls)}  content={evaluatee_text[:80]!r}")

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
                        _emit_error(
                            "'end' action requires object decision",
                            recoverable=True,
                        )
                        continue

                    expected_turn_index = len(simulator_trail)
                    trail_entry = dict(decision)
                    decision_errors = _validate_simulator_decision(
                        trail_entry,
                        expected_turn_index=expected_turn_index,
                    )
                    if decision_errors:
                        _emit_error(
                            "invalid SimulatorDecision on end: "
                            + "; ".join(decision_errors),
                            recoverable=True,
                        )
                        continue

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
                        _emit_error(
                            f"unknown action {action!r}; expected 'send' or 'end'",
                            recoverable=True,
                        )
                    continue

    except asyncio.TimeoutError:
        termination_reason = "timeout"
        _log("ERROR", f"outer asyncio.TimeoutError — turns_used={turns_used}")
        exit_code = 2
    except Exception as e:  # noqa: BLE001
        termination_reason = "evaluatee_error"
        termination_detail = f"{type(e).__name__}: {e}"
        if _logger:
            _logger.exception(f"unhandled exception in _serve: {termination_detail}")
        else:
            _log("ERROR", termination_detail)
        _emit_error(termination_detail)
        exit_code = 2

    # write trace regardless of how we got here (best-effort partial trace
    # on failure paths)
    _log("END", f"turns_used={turns_used}  termination={termination_reason}  exit_code={exit_code}")
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
        _log("TRACE", f"trace written → {output_path}")
    except Exception as e:  # noqa: BLE001
        if _logger:
            _logger.exception(f"failed to write trace: {type(e).__name__}: {e}")
        else:
            _log("ERROR", f"failed to write trace: {type(e).__name__}: {e}")
        _emit_error(f"failed to write trace: {type(e).__name__}: {e}")
        exit_code = 2

    return exit_code


# ---------------------------------------------------------------------------
# single-turn mode — one WS connection per utterance, for LLM-in-the-loop
# ---------------------------------------------------------------------------

def _infer_next_turn_index(output_path: Path) -> int:
    """Infer next turn_index from an existing partial trace.

    Returns 0 when no partial trace exists yet (i.e. this is the opening turn).
    """
    if not output_path.exists():
        return 0
    try:
        with open(output_path, encoding="utf-8") as f:
            trace = json.load(f)
        last_idx = trace.get("_last_turn_index")
        if isinstance(last_idx, int) and last_idx >= 0:
            return last_idx + 1
        # Fallback: count evaluatee dialog turns
        return sum(1 for t in trace.get("dialog_turns", []) if t.get("actor") == "evaluatee")
    except (json.JSONDecodeError, OSError):
        return 0


async def _serve_single_turn(
    evaluation_id: str,
    test_case_id: str,
    cfg: dict,
    eval_ctx: dict,
    simulator_id: str,
    utterance: str,
    turn_index: int,
    output_path: Path,
) -> int:
    """Single-turn mode: one WS connection per utterance, for LLM-in-the-loop simulation.

    Design rationale
    ----------------
    The sandbox gateway maintains conversation history **server-side**, but each
    ``user_message`` payload **must** include the ``sessionId`` field so the Gateway
    routes the message to the existing conversation rather than creating a new one.
    This mirrors the frontend behaviour in EvaluationPage.tsx:
        activeWs.send({ type: 'user_message', text, sessionId: activeSessionId, ... })

    The WS session ID is different from the evaluation session ID stored in
    ``evaluation_context.session.session_id``.  It is resolved by querying the
    Gateway ``/admin/sessions`` endpoint (same logic as ``fetchAdminSessions`` in
    the frontend) and cached inside the partial trace as ``_ws_session_id`` so
    subsequent single-turn invocations can reuse it without an extra HTTP call.

    This lets the host evaluation-expert agent call run.py **once per turn**:
      1. pass the customer utterance via ``--utterance``
      2. run.py connects, sends (with sessionId), collects the evaluatee reply,
         appends to partial trace, exits
      3. host agent reads the partial trace, generates the next utterance using its
         own LLM brain (simulators/customer_realistic/system_prompt.md)
      4. host agent calls run.py again with the new utterance
      5. repeat until done, then call ``--finalize-trace``

    No long-lived subprocess or background process is needed between LLM calls.

    Trace accumulation
    ------------------
    * Loads the existing partial trace from ``output_path`` if present (append mode).
    * Writes back a partial trace with ``_partial=True``, ``_last_turn_index=N``,
      and ``_ws_session_id`` for reuse.
    * Call ``_finalize_partial_trace()`` (``--finalize-trace``) after the last turn
      to produce a schema-valid final trace with a proper ``termination`` block.
    """
    _log("SINGLE", f"turn_index={turn_index}  utterance={utterance[:80]!r}")

    # ── Load existing partial trace (append mode) ───────────────────────────
    existing: dict = {}
    if output_path.exists():
        try:
            with open(output_path, encoding="utf-8") as f:
                existing = json.load(f)
            _log(
                "SINGLE",
                f"loaded partial trace ({len(existing.get('dialog_turns', []))} existing dialog turns)",
            )
        except (json.JSONDecodeError, OSError) as e:
            _log("WARN", f"could not load existing trace, starting fresh: {e}")

    dialog_turns: list = list(existing.get("dialog_turns") or [])
    actual_tool_calls: list = list(existing.get("actual_tool_calls") or [])
    simulator_trail: list = list(existing.get("simulator_trail") or [])
    started_at: str = existing.get("started_at") or _now_iso()
    auto_approve = bool(cfg["auto_approve_tools"])

    # Resolve WS session ID: reuse cached value from previous turn, or query gateway
    cached_session_id: str | None = existing.get("_ws_session_id") or None
    ws_session_id = _resolve_ws_session_id(eval_ctx, cfg, cached=cached_session_id)

    evaluatee_text = ""
    new_tool_calls: list = []
    raw: list = []
    exit_code = 0

    try:
        async with WsCollector(
            cfg["endpoint"],
            cfg["token"],
            timeout=int(cfg["timeout"]),
            log_fn=_logger,
        ) as ws:
            _log("WS", f"[single-turn] connected for turn {turn_index}")

            try:
                raw = await ws.send_and_collect(utterance, ws_session_id)
            except asyncio.TimeoutError:
                msg = f"evaluatee response timeout at turn_index={turn_index}"
                _log("ERROR", msg)
                _emit_error(msg)
                exit_code = 2
            except Exception as e:  # noqa: BLE001
                if _logger:
                    _logger.exception(
                        f"[single-turn] ws.send_and_collect raised at turn_index={turn_index}"
                    )
                _emit_error(f"{type(e).__name__}: {e}")
                exit_code = 2

            if exit_code == 0:
                if auto_approve:
                    for m in raw:
                        if m.get("type") == "approval_required":
                            call_id = m.get("callId")
                            if call_id:
                                await ws.approve_tool(call_id, approved=True)

                evaluatee_text = _flatten_assistant_text(raw)
                new_tool_calls = _extract_tool_calls(raw, after_turn_index=turn_index)

                _log(
                    "RECV",
                    f"[single-turn] turn={turn_index}  raw={len(raw)}  tools={len(new_tool_calls)}"
                    f"  content={evaluatee_text[:80]!r}",
                )

                dialog_turns.append({
                    "turn_index": turn_index,
                    "actor": "evaluator",
                    "content": utterance,
                    "timestamp": _now_iso(),
                })
                dialog_turns.append({
                    "turn_index": turn_index,
                    "actor": "evaluatee",
                    "content": evaluatee_text,
                    "timestamp": _now_iso(),
                })
                actual_tool_calls.extend(new_tool_calls)

    except Exception as e:  # noqa: BLE001
        if _logger:
            _logger.exception("[single-turn] unhandled exception")
        _emit_error(f"{type(e).__name__}: {e}")
        exit_code = 2

    # ── Write partial trace (no termination field yet) ──────────────────────
    partial_trace: dict = {
        "evaluation_id": evaluation_id,
        "test_case_id": test_case_id,
        "simulator_id": simulator_id,
        "started_at": started_at,
        "dialog_turns": dialog_turns,
        "actual_tool_calls": actual_tool_calls,
        "simulator_trail": simulator_trail,
        "_partial": True,
        "_last_turn_index": turn_index,
    }
    if ws_session_id:
        partial_trace["_ws_session_id"] = ws_session_id
    try:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(partial_trace, f, ensure_ascii=False, indent=2)
        _log("TRACE", f"[single-turn] partial trace written  turns_so_far={turn_index + 1}")
    except OSError as e:
        _log("ERROR", f"[single-turn] failed to write partial trace: {e}")
        _emit_error(f"failed to write partial trace: {e}")
        return 2

    if exit_code == 0:
        _emit({
            "event": "evaluatee_turn",
            "turn_index": turn_index,
            "content": evaluatee_text,
            "tool_calls": new_tool_calls,
            "raw_messages": raw,
        })
        _emit({
            "event": "turn_appended",
            "turn_index": turn_index,
            "path": str(output_path),
            "turns_so_far": turn_index + 1,
        })

    return exit_code


def _finalize_partial_trace(
    output_path: Path,
    termination_reason: str = "completed_normally",
    termination_detail: str | None = None,
) -> int:
    """Convert a partial trace (from single-turn invocations) into a final trace.

    Removes the ``_partial`` / ``_last_turn_index`` sentinels, writes ``ended_at``
    and a proper ``termination`` block.  Must be called after the last
    ``--utterance`` invocation to produce a schema-valid ExecutionTrace.
    """
    if not output_path.exists():
        _log("ERROR", f"[finalize] trace file not found: {output_path}")
        _emit_error(f"trace file not found: {output_path}")
        return 2

    try:
        with open(output_path, encoding="utf-8") as f:
            trace = json.load(f)
    except (json.JSONDecodeError, OSError) as e:
        _log("ERROR", f"[finalize] failed to load trace: {e}")
        _emit_error(f"failed to load trace: {e}")
        return 2

    last_turn_index = trace.pop("_last_turn_index", None)
    trace.pop("_partial", None)

    turns_used = (
        (last_turn_index + 1)
        if isinstance(last_turn_index, int)
        else sum(1 for t in trace.get("dialog_turns", []) if t.get("actor") == "evaluatee")
    )

    # Infer final_emotion from last simulator_trail entry (if host agent populated it)
    trail = trace.get("simulator_trail") or []
    raw_emotion = (trail[-1].get("internal_emotion") if trail else None) or "neutral"
    mapped_emotion = _DELTA_EMOTION_MAP.get(raw_emotion, raw_emotion)
    final_emotion: str | None = mapped_emotion if mapped_emotion in _ABSOLUTE_EMOTIONS else None

    termination: dict[str, Any] = {"reason": termination_reason, "turns_used": turns_used}
    if termination_detail:
        termination["detail"] = termination_detail
    if final_emotion:
        termination["final_emotion"] = final_emotion

    trace["ended_at"] = _now_iso()
    trace["termination"] = termination

    try:
        with open(output_path, "w", encoding="utf-8") as f:
            json.dump(trace, f, ensure_ascii=False, indent=2)
        _log("TRACE", f"[finalize] trace finalized → {output_path}")
    except OSError as e:
        _log("ERROR", f"[finalize] failed to write finalized trace: {e}")
        _emit_error(f"failed to write finalized trace: {e}")
        return 2

    _emit({
        "event": "trace_written",
        "path": str(output_path),
        "termination": termination,
    })
    return 0


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main() -> None:
    ap = argparse.ArgumentParser(
        description="ws_jwt runtime driver — STEP 3 (v2.0). "
                    "Modes: (1) interactive — long-lived stdin/stdout protocol driven by host agent; "
                    "(2) --auto-simulate — fully synchronous, no stdin interaction; "
                    "(3) --utterance TEXT — single-turn per invocation, for LLM-in-the-loop simulation; "
                    "(4) --finalize-trace — close out a partial trace from single-turn calls.",
    )
    ap.add_argument("--evaluation-context", required=True,
                    help="path to the runtime evaluation context JSON; "
                         "use /workspace/runtime/evaluation-context.json (original, with credentials) "
                         "not a run_dir copy which may have secrets sanitized")
    ap.add_argument("--enriched-test-case", required=True,
                    help="path to one enriched test case under ./runs/<eval_id>/enriched-cases/")
    ap.add_argument("--output", required=True,
                    help="output path; MUST validate against runtime-schemas/execution_trace.schema.json")
    ap.add_argument("--auto-simulate", action="store_true", default=False,
                    help="Autonomous simulator mode: run.py drives all turns internally using "
                         "tc.input.opening_message and stop_conditions rules. "
                         "One synchronous shell call per test case — no background process, "
                         "no pad files, no polling.")
    ap.add_argument(
        "--utterance",
        default=None,
        metavar="TEXT",
        help="[Single-turn mode] Customer utterance to send in this invocation. "
             "Triggers single-turn mode: connect → send → collect evaluatee reply → append partial trace → exit. "
             "Session continuity is maintained server-side by the sandbox: reconnecting to the same "
             "WS endpoint (same sandbox URL + token) resumes the same conversation automatically — "
             "no session_id query parameter is needed. "
             "This lets the host agent generate each follow-up utterance with its own LLM "
             "(simulators/customer_realistic/system_prompt.md) between run.py invocations. "
             "After the last turn, call --finalize-trace to produce a schema-valid final trace.",
    )
    ap.add_argument(
        "--turn-index",
        type=int,
        default=None,
        metavar="N",
        dest="turn_index",
        help="[Single-turn mode] Explicit 0-based turn index for --utterance mode. "
             "If omitted, auto-inferred from the existing partial trace (_last_turn_index + 1); "
             "defaults to 0 when no partial trace exists yet (opening message).",
    )
    ap.add_argument(
        "--finalize-trace",
        action="store_true",
        default=False,
        dest="finalize_trace",
        help="[Finalize mode] Convert the partial trace written by --utterance invocations into a "
             "complete, schema-valid ExecutionTrace with a proper termination block. "
             "Must be called after the last --utterance invocation.",
    )
    ap.add_argument(
        "--termination-reason",
        default="completed_normally",
        metavar="REASON",
        dest="termination_reason",
        help="[Finalize mode] Termination reason to write into the trace "
             "(default: completed_normally). "
             "Valid values: completed_normally, max_turns_reached, bottom_line_violated, "
             "deadlock_detected, customer_gave_up, timeout, evaluatee_error.",
    )
    args = ap.parse_args()

    # Initialize file logger as early as possible so every subsequent _log()
    # call is captured. Log sits next to the trace JSON for easy retrieval.
    global _logger
    _logger = DriverLogger(Path(args.output).with_suffix(".driver.log"))

    _log("MAIN", f"--evaluation-context {args.evaluation_context}")
    _log("MAIN", f"--enriched-test-case {args.enriched_test_case}")
    _log("MAIN", f"--output {args.output}")

    eval_ctx = _load_json(args.evaluation_context)
    evaluation_id = eval_ctx.get("evaluation_id") or f"eval-{uuid.uuid4().hex[:8]}"

    tc = _load_json(args.enriched_test_case)
    cfg = _resolve_driver_config(eval_ctx)
    ws_token = _resolve_ws_token(eval_ctx, cfg)
    cfg["token"] = ws_token
    effective_max_turns = _resolve_effective_max_turns(eval_ctx, tc)

    test_case_id = tc.get("test_case_id") or Path(args.enriched_test_case).stem
    output_path = Path(args.output)

    if args.finalize_trace:
        # ── Finalize mode ────────────────────────────────────────────────────
        # Close out a partial trace written by one or more --utterance calls.
        # tc.input validation is skipped; test_case_id comes from the tc file.
        _log("MAIN", f"mode=finalize-trace  output={output_path}  reason={args.termination_reason}")
        exit_code = _finalize_partial_trace(
            output_path,
            termination_reason=args.termination_reason or "completed_normally",
        )

    elif args.utterance is not None:
        # ── Single-turn mode ─────────────────────────────────────────────────
        # One WS connection per invocation; sandbox maintains session server-side.
        # The host agent generates each utterance with its LLM (system_prompt.md)
        # between calls — no long-lived subprocess needed.
        turn_idx = (
            args.turn_index
            if args.turn_index is not None
            else _infer_next_turn_index(output_path)
        )
        simulator_id = _resolve_simulator_id(eval_ctx, auto_mode=True)
        _log(
            "MAIN",
            f"mode=single-turn  evaluation_id={evaluation_id}  tc_id={test_case_id}"
            f"  turn_index={turn_idx}  simulator={simulator_id}",
        )
        exit_code = asyncio.run(_serve_single_turn(
            evaluation_id=evaluation_id,
            test_case_id=test_case_id,
            cfg=cfg,
            eval_ctx=eval_ctx,
            simulator_id=simulator_id,
            utterance=args.utterance,
            turn_index=turn_idx,
            output_path=output_path,
        ))

    else:
        # ── Auto-simulate or interactive mode ────────────────────────────────
        inp = tc.get("input") or {}
        if not (inp.get("opening_message") or inp.get("user_message")):
            _emit_error(
                f"enriched_test_case.input has neither opening_message nor "
                f"(deprecated) user_message for {test_case_id}"
            )
            sys.exit(2)

        if args.auto_simulate:
            _log("MAIN", f"mode=auto-simulate  evaluation_id={evaluation_id}  tc_id={test_case_id}  effective_max_turns={effective_max_turns}")
            exit_code = asyncio.run(_serve_auto(
                evaluation_id=evaluation_id,
                test_case_id=test_case_id,
                tc=tc,
                cfg=cfg,
                eval_ctx=eval_ctx,
                effective_max_turns=effective_max_turns,
                output_path=output_path,
            ))
        else:
            simulator_id = _resolve_simulator_id(eval_ctx)
            _log("MAIN", f"mode=interactive  evaluation_id={evaluation_id}  tc_id={test_case_id}  simulator_id={simulator_id}  effective_max_turns={effective_max_turns}")
            exit_code = asyncio.run(_serve(
                evaluation_id=evaluation_id,
                test_case_id=test_case_id,
                cfg=cfg,
                eval_ctx=eval_ctx,
                simulator_id=simulator_id,
                effective_max_turns=effective_max_turns,
                output_path=output_path,
            ))

    _log("SHUTDOWN", f"exit_code={exit_code}")
    _logger.close()
    sys.exit(exit_code)


if __name__ == "__main__":
    main()
