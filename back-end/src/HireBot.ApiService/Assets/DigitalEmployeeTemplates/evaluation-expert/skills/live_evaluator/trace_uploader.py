"""
trace_uploader.py - 把执行轨迹上传到 NCrew Hire 后端

职责：
  - 读取 trace_result.json（evaluate.py --mode execute 输出）
  - 读取 runtime_context.json（与 evaluate.py / verdict_uploader.py 共用同一文件）
  - 构造 EvaluationTraceSyncRequestDto
  - 调用 POST {base_url}/api/v1/employees/{employeeId}/evaluation/sync-trace
  - 把上传结果写入 output 文件

Token 获取：与 verdict_uploader.py 相同，通过 auth_client.resolve_auth() 从 auth_config.json 读取。

Base URL 来源（优先级从高到低）：
  1. runtime_context.ncrew_hire.base_url
  2. 环境变量 NCREW_HIRE_API_BASE_URL
"""

from __future__ import annotations

import argparse
import json
import os
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any

from auth_client import resolve_auth


# ---------------------------------------------------------------------------
# 配置解析（与 verdict_uploader.py 复用相同逻辑）
# ---------------------------------------------------------------------------

def _load_json(path: str) -> dict[str, Any]:
    """从文件加载 JSON。"""
    return json.loads(Path(path).read_text(encoding="utf-8"))


def resolve_base_url(runtime_context: dict[str, Any]) -> str:
    """
    解析 NCrew Hire 后端 Base URL。

    优先级：
      1. runtime_context.ncrew_hire.base_url
      2. 环境变量 NCREW_HIRE_API_BASE_URL
    """
    ncrew_hire = runtime_context.get("ncrew_hire") or {}
    base_url = str(ncrew_hire.get("base_url") or "").strip()
    if not base_url:
        base_url = os.environ.get("NCREW_HIRE_API_BASE_URL", "").strip()
    if not base_url:
        raise ValueError(
            "无法解析 NCrew Hire 后端地址：请在 runtime_context.ncrew_hire.base_url "
            "或环境变量 NCREW_HIRE_API_BASE_URL 中配置"
        )
    return base_url.rstrip("/")


# ---------------------------------------------------------------------------
# 数据转换：trace_result.json → API 请求体
# ---------------------------------------------------------------------------

def _build_trace_summary(trace_result: dict[str, Any]) -> dict[str, Any]:
    """
    从 trace_result 提取用于日志的摘要，不包含原始消息体（体积可能很大）。

    返回字段：
      - total_turns:          总轮次数
      - status:               执行状态
      - per_turn_summary:     每轮的 test_case_id + summary 字段
    """
    turns = trace_result.get("turns") or []
    per_turn_summary = []
    for turn in turns:
        trace = turn.get("execution_trace") or {}
        summary = trace.get("summary") or {}
        per_turn_summary.append({
            "turn_index": turn.get("turn_index"),
            "test_case_id": turn.get("test_case_id"),
            "total_messages": summary.get("total_messages"),
            "total_tool_calls": summary.get("total_tool_calls"),
            "has_thought": summary.get("has_thought"),
            "execution_time_seconds": summary.get("execution_time_seconds"),
            "tool_calls_list": summary.get("tool_calls_list") or [],
        })

    return {
        "total_turns": len(turns),
        "status": trace_result.get("status"),
        "per_turn_summary": per_turn_summary,
    }


def build_trace_payload(
    session_id: str,
    trace_result: dict[str, Any],
) -> dict[str, Any]:
    """
    构造 EvaluationTraceSyncRequestDto 的字典表示。

    对应 C# 结构：
      {
        sessionId: string,
        traceJson: string   // trace_result.json 的完整 JSON 字符串
      }
    """
    return {
        "sessionId": session_id,
        "traceJson": json.dumps(trace_result, ensure_ascii=False),
    }


# ---------------------------------------------------------------------------
# HTTP 上传
# ---------------------------------------------------------------------------

def upload_trace(
    base_url: str,
    token: str,
    employee_id: str,
    payload: dict[str, Any],
) -> dict[str, Any]:
    """
    调用 POST /api/v1/employees/{employeeId}/evaluation/sync-trace。

    Returns:
        后端返回的 JSON 响应，失败时包含 _error 字段
    """
    url = f"{base_url}/api/v1/employees/{employee_id}/evaluation/sync-trace"
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")

    req = urllib.request.Request(
        url,
        data=body,
        method="POST",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json; charset=utf-8",
            "Accept": "application/json",
        },
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
        description="trace_uploader - 把执行轨迹上传到 HireBot 后端"
    )
    parser.add_argument(
        "--runtime-context",
        required=True,
        help="运行时上下文 JSON 路径（与 evaluate.py 使用同一文件）",
    )
    parser.add_argument(
        "--trace-result",
        required=True,
        help="evaluate.py --mode execute 输出的 trace_result.json 路径",
    )
    parser.add_argument(
        "--output",
        required=True,
        help="上传结果 JSON 输出路径",
    )
    args = parser.parse_args()

    # ---- 加载输入文件 ----
    runtime_context = _load_json(args.runtime_context)
    trace_result = _load_json(args.trace_result)

    # ---- 从运行时上下文提取必要字段 ----
    session = runtime_context.get("session") or {}
    employee_id = str(session.get("employee_id") or "").strip()
    session_id = str(session.get("session_id") or "").strip()

    if not employee_id or not session_id:
        print("[错误] runtime_context.session 中缺少 employee_id 或 session_id")
        return 1

    # ---- 解析配置 ----
    try:
        base_url = resolve_base_url(runtime_context)
        auth = resolve_auth(runtime_context.get("target_sandbox", {}).get("auth"))
        token = auth.access_token
    except (ValueError, FileNotFoundError, RuntimeError) as exc:
        print(f"[错误] 配置解析失败: {exc}")
        return 1

    # ---- 打印摘要日志 ----
    summary = _build_trace_summary(trace_result)
    print(f"[上传] 目标地址:  {base_url}")
    print(f"[上传] 员工 ID:   {employee_id}")
    print(f"[上传] 会话 ID:   {session_id}")
    print(f"[上传] 总轮次:    {summary['total_turns']}")
    print(f"[上传] 执行状态:  {summary['status']}")
    for turn_info in summary["per_turn_summary"]:
        print(
            f"  └ [{turn_info['turn_index']}] {turn_info['test_case_id']} "
            f"消息数={turn_info['total_messages']} "
            f"工具调用={turn_info['tool_calls_list']} "
            f"耗时={turn_info['execution_time_seconds']}s"
        )

    # ---- 构造 payload 并上传 ----
    payload = build_trace_payload(session_id, trace_result)

    # trace_result.json 体积可能较大，打印字节数便于排查超时问题
    payload_size_kb = len(payload["traceJson"].encode("utf-8")) / 1024
    print(f"[上传] Payload 大小: {payload_size_kb:.1f} KB")

    response = upload_trace(base_url, token, employee_id, payload)

    upload_ok = "_error" not in response

    _write_json(args.output, {
        "status": "success" if upload_ok else "error",
        "employee_id": employee_id,
        "session_id": session_id,
        "trace_summary": summary,
        "payload_size_kb": round(payload_size_kb, 1),
        "response": response,
    })

    if not upload_ok:
        print(f"[错误] 上传失败: {response['_error']}")
        print(f"[输出] {args.output}")
        # trace 上传失败不阻断后续 verdict 上传，返回非零但后续可继续
        return 1

    asset_id = (response.get("data") or {}).get("assetId", "")
    trace_url = (response.get("data") or {}).get("traceJsonUrl", "")
    print(f"[成功] 执行轨迹已上传到 HireBot 后端")
    print(f"[成功] assetId:     {asset_id}")
    print(f"[成功] traceJsonUrl: {trace_url}")
    print(f"[输出] {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
