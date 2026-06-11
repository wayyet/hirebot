"""
trace_uploader.py - 把执行轨迹上传到 HireBot 后端
Path: runtime-drivers/ws_jwt/trace_uploader.py
职责：
  - 扫描 runs/<eval_id>/traces/ 目录下所有 *.trace.json 文件（STEP 3 ws_jwt driver 输出）
  - 读取 evaluation_context.json（与 verdict_uploader.py 共用同一文件）
  - 将所有场景轨迹合并为一个 bundle 文档
  - 调用 POST {base_url}/api/v1/employees/{employeeId}/evaluation/sync-trace
  - 把上传结果写入 output 文件

字段来源：
  evaluation_context.hirebot_api.base_url       → HireBot REST API 地址
  evaluation_context.hirebot_api.employee_id    → 员工 ID
  evaluation_context.hirebot_api.session_id     → 评估会话 ID
  evaluation_context.hirebot_api.auth           → 鉴权配置（缺省 fallback: runtime_driver.driver_config.token）

轨迹文件格式变化（对比旧版 live_evaluator/trace_uploader.py）：
  旧版：单一 trace_result.json（evaluate.py --mode execute 输出，含 turns[*].execution_trace）
  新版：每场景一个 <tc_id>.trace.json（run.py STEP 3 driver 输出，含 dialog_turns / actual_tool_calls / simulator_trail）
  合并策略：多个 trace 文件打包为 { "evaluation_id": ..., "traces": [<tc_id>.trace.json内容, ...] }
"""

from __future__ import annotations

import argparse
import json
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from auth_client import resolve_auth_from_eval_ctx


# ---------------------------------------------------------------------------
# 配置解析（复用 verdict_uploader.py 中相同的逻辑）
# ---------------------------------------------------------------------------

def _load_json(path: str) -> dict[str, Any]:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def resolve_hirebot_api_config(eval_ctx: dict[str, Any]) -> tuple[str, str, str]:
    """
    从 evaluation_context 中解析 HireBot API 调用的三要素：base_url、employee_id、session_id。

    Returns:
        (base_url, employee_id, session_id)
    """
    api = eval_ctx.get("hirebot_api") or {}
    base_url = str(api.get("base_url") or "").strip().rstrip("/")
    employee_id = str(api.get("employee_id") or "").strip()
    session_id = str(api.get("session_id") or "").strip()

    if not base_url:
        raise ValueError(
            "evaluation_context.hirebot_api.base_url 未配置。"
            "请在 evaluation_context.json 的 hirebot_api 块中填写 HireBot 后端地址。"
        )
    if not employee_id:
        raise ValueError("evaluation_context.hirebot_api.employee_id 未配置。")
    if not session_id:
        raise ValueError("evaluation_context.hirebot_api.session_id 未配置。")

    return base_url, employee_id, session_id


# ---------------------------------------------------------------------------
# 轨迹文件扫描与合并
# ---------------------------------------------------------------------------

def collect_synthesized_testcases(synthesized_dir: str) -> list[dict[str, Any]]:
    """
    扫描 synthesized_dir 下所有 *.json 文件并返回内容列表（按文件名排序）。

    这些文件由 STEP 1.5 在对话中合成，落盘于 runs/<eval_id>/synthesized-cases/。
    上传后，后端 EnsureQuestionCardsFromRuntimeTextAsync 会从 trace bundle 中
    自动提取 test_cases 并持久化为 Question Cards，展示在前端右侧面板。

    Args:
        synthesized_dir: STEP 1.5 输出目录，通常为 runs/<eval_id>/synthesized-cases/

    Returns:
        [{test case 文件内容}, ...] 按文件名升序排列；目录不存在或为空时返回 []
    """
    d = Path(synthesized_dir)
    if not d.is_dir():
        print(f"  [采集] 合成用例目录不存在，跳过: {synthesized_dir}")
        return []

    case_files = sorted(d.glob("*.json"))
    if not case_files:
        print(f"  [采集] 合成用例目录为空: {synthesized_dir}")
        return []

    result: list[dict[str, Any]] = []
    for cf in case_files:
        try:
            content = json.loads(cf.read_text(encoding="utf-8"))
            result.append(content)
            print(f"  [采集] 合成用例: {cf.name}")
        except Exception as exc:
            print(f"  [警告] 跳过无法解析的合成用例文件 {cf.name}: {exc}")

    return result


def collect_traces(traces_dir: str) -> list[dict[str, Any]]:
    """
    扫描 traces_dir 下所有 *.trace.json 文件并返回内容列表（按文件名排序）。

    Args:
        traces_dir: STEP 3 输出目录，通常为 runs/<eval_id>/traces/

    Returns:
        [{trace 文件内容}, ...] 按文件名升序排列
    """
    d = Path(traces_dir)
    if not d.is_dir():
        raise FileNotFoundError(f"轨迹目录不存在: {traces_dir}")

    trace_files: list[Path] = []
    seen: set[str] = set()
    for pattern in ("*.trace.json", "*.execution_trace.json"):
        for trace_file in sorted(d.glob(pattern)):
            key = str(trace_file)
            if key in seen:
                continue
            seen.add(key)
            trace_files.append(trace_file)

    if not trace_files:
        raise FileNotFoundError(
            "trace directory contains no *.trace.json or "
            f"*.execution_trace.json files: {traces_dir}"
        )

    result: list[dict[str, Any]] = []
    for tf in trace_files:
        try:
            content = json.loads(tf.read_text(encoding="utf-8"))
            result.append(content)
            print(f"  [采集] {tf.name}")
        except Exception as exc:
            print(f"  [警告] 跳过无法解析的轨迹文件 {tf.name}: {exc}")

    return result


def collect_tainted_notice(traces_dir: str) -> dict[str, Any] | None:
    notice_path = Path(traces_dir).parent / "TAINTED.md"
    if not notice_path.is_file():
        return None

    try:
        content = notice_path.read_text(encoding="utf-8")
    except Exception as exc:
        return {
            "present": True,
            "path": str(notice_path),
            "read_error": str(exc),
        }

    return {
        "present": True,
        "path": str(notice_path),
        "content_preview": content[:4000],
    }


def _parse_datetime(value: Any) -> Any:
    if not isinstance(value, str) or not value.strip():
        return None

    normalized = value.strip().replace("Z", "+00:00")
    try:
        return datetime.fromisoformat(normalized)
    except ValueError:
        return None


def _duration_seconds(started_at: Any, ended_at: Any) -> Any:
    start = _parse_datetime(started_at)
    end = _parse_datetime(ended_at)
    if start is None or end is None:
        return None

    try:
        return max((end - start).total_seconds(), 0.0)
    except TypeError:
        return None


def _dialog_content_by_actor(dialog_turns: list[dict[str, Any]], actor: str) -> dict[int, str]:
    result: dict[int, str] = {}
    for turn in dialog_turns:
        if turn.get("actor") != actor:
            continue

        try:
            turn_index = int(turn.get("turn_index"))
        except (TypeError, ValueError):
            continue

        content = str(turn.get("content") or "").strip()
        if content:
            result[turn_index] = content

    return result


def _coerce_int(value: Any) -> Any:
    try:
        return int(value)
    except (TypeError, ValueError):
        return None


def _tool_logs_for_turn(tool_calls: list[dict[str, Any]], source_turn_index: int) -> list[dict[str, Any]]:
    logs: list[dict[str, Any]] = []
    for call in tool_calls:
        if not isinstance(call, dict):
            continue

        if _coerce_int(call.get("after_turn_index")) != source_turn_index:
            continue

        tool_name = str(call.get("tool_name") or call.get("name") or "unknown")
        called_at = call.get("called_at")
        arguments = call.get("arguments") or call.get("input") or {}

        logs.append({
            "type": "tool_use",
            "timestamp_start": called_at,
            "name": tool_name,
            "input": arguments,
        })

        outcome = str(call.get("outcome") or "success")
        result_log: dict[str, Any] = {
            "type": "tool_result",
            "timestamp_end": call.get("completed_at") or called_at,
            "name": tool_name,
            "content": {
                "outcome": outcome,
            },
        }
        if call.get("error_message"):
            result_log["content"]["error_message"] = call.get("error_message")
        logs.append(result_log)

    return logs


def _tool_names_for_turn(tool_calls: list[dict[str, Any]], source_turn_index: int) -> list[str]:
    names: list[str] = []
    for call in tool_calls:
        if not isinstance(call, dict):
            continue
        if _coerce_int(call.get("after_turn_index")) != source_turn_index:
            continue
        names.append(str(call.get("tool_name") or call.get("name") or "unknown"))
    return names


def _to_frontend_turns(
    trace: dict[str, Any],
    start_turn_index: int,
) -> list[dict[str, Any]]:
    dialog_turns = [
        turn
        for turn in (trace.get("dialog_turns") or [])
        if isinstance(turn, dict)
    ]
    tool_calls = [
        call
        for call in (trace.get("actual_tool_calls") or [])
        if isinstance(call, dict)
    ]

    evaluator_by_index = _dialog_content_by_actor(dialog_turns, "evaluator")
    evaluatee_by_index = _dialog_content_by_actor(dialog_turns, "evaluatee")
    source_turn_indexes = sorted(set(evaluator_by_index) | set(evaluatee_by_index))
    test_case_id = str(trace.get("test_case_id") or "").strip()
    duration = _duration_seconds(trace.get("started_at"), trace.get("ended_at"))

    frontend_turns: list[dict[str, Any]] = []
    for offset, source_turn_index in enumerate(source_turn_indexes):
        logs = _tool_logs_for_turn(tool_calls, source_turn_index)
        tool_names = _tool_names_for_turn(tool_calls, source_turn_index)
        summary: dict[str, Any] = {
            "total_messages": int(source_turn_index in evaluator_by_index)
            + int(source_turn_index in evaluatee_by_index),
            "total_tool_calls": len(tool_names),
            "has_thought": False,
            "think_count": 0,
            "tool_calls_list": tool_names,
        }
        # driver 只提供场景级耗时；挂在最后一轮，避免每轮重复展示同一总耗时。
        if duration is not None and offset == len(source_turn_indexes) - 1:
            summary["execution_time_seconds"] = duration

        frontend_turns.append({
            "turn_index": start_turn_index + offset,
            "test_case_id": test_case_id,
            "user_input": evaluator_by_index.get(source_turn_index, ""),
            "execution_trace": {
                "logs": logs,
                "assembled_assistant_text": evaluatee_by_index.get(source_turn_index, ""),
                "summary": summary,
            },
        })

    if frontend_turns:
        return frontend_turns

    termination = trace.get("termination") if isinstance(trace.get("termination"), dict) else {}
    return [{
        "turn_index": start_turn_index,
        "test_case_id": test_case_id,
        "user_input": "",
        "execution_trace": {
            "logs": [],
            "assembled_assistant_text": "",
            "summary": {
                "total_messages": 0,
                "total_tool_calls": 0,
                "has_thought": False,
                "think_count": 0,
                "tool_calls_list": [],
                "termination_reason": termination.get("reason"),
            },
        },
    }]


def _build_frontend_turns(traces: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for trace in traces:
        result.extend(_to_frontend_turns(trace, len(result)))
    return result


def _overall_status(traces: list[dict[str, Any]]) -> str:
    failed_reasons = {"evaluatee_error", "timeout"}
    for trace in traces:
        termination = trace.get("termination") if isinstance(trace.get("termination"), dict) else {}
        reason = str(termination.get("reason") or "")
        if reason in failed_reasons:
            return "failed"
    return "completed"


def build_trace_bundle(
    evaluation_id: str,
    session_id: str,
    eval_ctx: dict[str, Any],
    traces: list[dict[str, Any]],
    synthesized_testcases: list[dict[str, Any]] | None = None,
    tainted_notice: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """
    将多个场景轨迹和合成测试用例合并为一个 bundle 文档。

    EvaluationTraceSyncRequestDto.traceJson 是该 bundle 的 JSON 字符串。

    后端 SyncTraceAsync → EnsureQuestionCardsFromRuntimeTextAsync 会通过
    ParseRuntimeTestcasesFromText → CollectRuntimeTestcases 递归扫描整个
    trace JSON，自动发现顶层 test_cases 数组并持久化为 Question Cards。
    """
    frontend_turns = _build_frontend_turns(traces)
    session = eval_ctx.get("session") if isinstance(eval_ctx.get("session"), dict) else {}
    employee = eval_ctx.get("employee") if isinstance(eval_ctx.get("employee"), dict) else {}
    target_sandbox = (
        eval_ctx.get("target_sandbox")
        if isinstance(eval_ctx.get("target_sandbox"), dict)
        else {}
    )

    bundle: dict[str, Any] = {
        "evaluation_id": evaluation_id,
        "session_id": session_id,
        "status": _overall_status(traces),
        "meta": {
            "total_turns": len(frontend_turns),
            "session_id": session_id,
            "employee_name": employee.get("display_name") or session.get("employee_id") or "",
            "iteration": session.get("iteration"),
            "collected_at": datetime.now(timezone.utc).isoformat(),
            "target_sandbox_id": target_sandbox.get("sandbox_id") or "",
            "tainted": tainted_notice is not None,
        },
        "turns": frontend_turns,
        "trace_count": len(traces),
        "trace_format": "evaluation-expert-consumer.v2.bundle_with_frontend_turns",
        "traces": traces,
    }

    # 嵌入合成测试用例，供后端提取为 Question Cards（前端右侧面板展示）
    # 后端 IsExplicitTestcaseContainer 检测含 test_cases 数组的对象；
    # CollectRuntimeTestcases 递归遍历整个 JSON 树，在顶层即可发现
    if synthesized_testcases:
        bundle["test_cases"] = synthesized_testcases
        bundle["test_case_count"] = len(synthesized_testcases)

    if tainted_notice is not None:
        bundle["tainted"] = tainted_notice

    return {
        "sessionId": session_id,
        "traceJson": json.dumps(bundle, ensure_ascii=False),
    }


# ---------------------------------------------------------------------------
# HTTP 上传
# ---------------------------------------------------------------------------

def upload_trace(
    base_url: str,
    auth_headers: dict[str, str],
    employee_id: str,
    payload: dict[str, Any],
) -> dict[str, Any]:
    """
    调用 POST /api/v1/employees/{employeeId}/evaluation/sync-trace。

    Returns:
        后端返回的 JSON 响应，失败时包含 _error 字段。
    """
    url = f"{base_url}/api/v1/employees/{employee_id}/evaluation/sync-trace"
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")

    headers = {"Content-Type": "application/json; charset=utf-8", "Accept": "application/json"}
    headers.update(auth_headers)

    req = urllib.request.Request(
        url,
        data=body,
        method="POST",
        headers=headers,
    )

    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        body_bytes = e.read()
        try:
            err_body = json.loads(body_bytes.decode("utf-8"))
        except Exception:
            err_body = body_bytes.decode("utf-8", errors="replace")
        return {"_error": f"HTTP {e.code}", "url": url, "response": err_body}
    except Exception as exc:
        return {"_error": str(exc), "url": url}


# ---------------------------------------------------------------------------
# 主入口
# ---------------------------------------------------------------------------

def _write_json(path: str, data: dict[str, Any]) -> None:
    p = Path(path)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(
        description="trace_uploader - 把 STEP 3 执行轨迹与 STEP 1.5 合成用例上传到 HireBot 后端"
    )
    parser.add_argument(
        "--evaluation-context",
        required=True,
        help="evaluation_context.json 路径（runs/<eval_id>/evaluation_context.json）",
    )
    parser.add_argument(
        "--traces-dir",
        required=True,
        help="STEP 3 输出目录（runs/<eval_id>/traces/），包含所有 *.trace.json 文件",
    )
    parser.add_argument(
        "--synthesized-dir",
        default=None,
        help="STEP 1.5 合成用例目录（runs/<eval_id>/synthesized-cases/），可选；提供后用例会被嵌入 bundle 供后端解析为 Question Cards",
    )
    parser.add_argument(
        "--output",
        required=True,
        help="上传结果 JSON 输出路径",
    )
    args = parser.parse_args()

    # 初始化文件日志 — 与 output 同目录，后缀 .log
    log_path = Path(args.output).with_suffix(".log")
    log_path.parent.mkdir(parents=True, exist_ok=True)
    _logf = open(log_path, "w", encoding="utf-8", buffering=1)

    def _log(tag: str, msg: str) -> None:
        from datetime import datetime, timezone
        ts = datetime.now(timezone.utc).strftime("%H:%M:%S.%f")[:-3]
        line = f"{ts} [{tag:<8}] {msg}"
        print(line)
        _logf.write(line + "\n")

    _log("STARTUP", f"--evaluation-context {args.evaluation_context}")
    _log("STARTUP", f"--traces-dir         {args.traces_dir}")
    _log("STARTUP", f"--output             {args.output}")
    if args.synthesized_dir:
        _log("STARTUP", f"--synthesized-dir    {args.synthesized_dir}")
    _log("STARTUP", f"log → {log_path}")

    eval_ctx = _load_json(args.evaluation_context)

    try:
        base_url, employee_id, session_id = resolve_hirebot_api_config(eval_ctx)
        auth = resolve_auth_from_eval_ctx(eval_ctx)
    except (ValueError, RuntimeError) as exc:
        _log("ERROR", f"配置解析失败: {exc}")
        _logf.close()
        return 1

    evaluation_id = str(eval_ctx.get("evaluation_id") or "").strip()
    _log("CONFIG", f"base_url={base_url}  employee_id={employee_id}  session_id={session_id}  auth_source={auth.source}")

    _log("COLLECT", f"扫描轨迹目录: {args.traces_dir}")
    print(f"[采集] 扫描轨迹目录: {args.traces_dir}")
    try:
        traces = collect_traces(args.traces_dir)
        tainted_notice = collect_tainted_notice(args.traces_dir)
    except FileNotFoundError as exc:
        _log("ERROR", str(exc))
        print(f"[错误] {exc}")
        _logf.close()
        return 1

    _log("COLLECT", f"共 {len(traces)} 个场景轨迹  tainted_notice={tainted_notice is not None}")
    print(f"[采集] 共 {len(traces)} 个场景轨迹")

    # 采集合成测试用例（可选）
    synthesized_testcases: list[dict[str, Any]] = []
    if args.synthesized_dir:
        synthesized_testcases = collect_synthesized_testcases(args.synthesized_dir)
        if synthesized_testcases:
            _log("COLLECT", f"共 {len(synthesized_testcases)} 个合成用例")
            print(f"[采集] 共 {len(synthesized_testcases)} 个合成用例（将嵌入 bundle 供后端解析为 Question Cards）")

    payload = build_trace_bundle(
        evaluation_id,
        session_id,
        eval_ctx,
        traces,
        synthesized_testcases=synthesized_testcases if synthesized_testcases else None,
        tainted_notice=tainted_notice,
    )

    trace_json_bytes = len(payload["traceJson"].encode("utf-8"))
    _log("UPLOAD", f"base_url={base_url}  employee_id={employee_id}  session_id={session_id}  bundle={trace_json_bytes / 1024:.1f}KB  auth_source={auth.source}")
    print(f"[上传] 目标地址:   {base_url}")
    print(f"[上传] 员工 ID:    {employee_id}")
    print(f"[上传] 会话 ID:    {session_id}")
    print(f"[上传] Trace 大小: {trace_json_bytes / 1024:.1f} KB")
    print(f"[上传] 鉴权方式:   {auth.source}")

    response = upload_trace(base_url, auth.build_http_headers(), employee_id, payload)
    upload_ok = "_error" not in response

    _write_json(args.output, {
        "status": "success" if upload_ok else "error",
        "employee_id": employee_id,
        "session_id": session_id,
        "evaluation_id": evaluation_id,
        "trace_count": len(traces),
        "test_case_count": len(synthesized_testcases),
        "tainted": tainted_notice is not None,
        "response": response,
    })

    if not upload_ok:
        _log("ERROR", f"上传失败: {response['_error']}")
        print(f"[错误] 上传失败: {response['_error']}")
        print(f"[输出] {args.output}")
        _logf.close()
        return 1

    _log("SUCCESS", f"执行轨迹已上传  output={args.output}")
    print("[成功] 执行轨迹已上传到 HireBot 后端")
    print(f"[输出] {args.output}")
    _logf.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
