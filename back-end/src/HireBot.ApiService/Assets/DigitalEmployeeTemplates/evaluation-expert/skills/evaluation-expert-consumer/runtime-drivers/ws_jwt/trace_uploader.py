"""
trace_uploader.py - 把执行轨迹上传到 HireBot 后端

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

    trace_files = sorted(d.glob("*.trace.json"))
    if not trace_files:
        raise FileNotFoundError(f"轨迹目录中没有找到 *.trace.json 文件: {traces_dir}")

    result: list[dict[str, Any]] = []
    for tf in trace_files:
        try:
            content = json.loads(tf.read_text(encoding="utf-8"))
            result.append(content)
            print(f"  [采集] {tf.name}")
        except Exception as exc:
            print(f"  [警告] 跳过无法解析的轨迹文件 {tf.name}: {exc}")

    return result


def build_trace_bundle(
    evaluation_id: str,
    session_id: str,
    traces: list[dict[str, Any]],
) -> dict[str, Any]:
    """
    将多个场景轨迹合并为一个 bundle 文档。

    EvaluationTraceSyncRequestDto.traceJson 是该 bundle 的 JSON 字符串。
    """
    bundle = {
        "evaluation_id": evaluation_id,
        "session_id": session_id,
        "trace_count": len(traces),
        "traces": traces,
    }
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
        description="trace_uploader - 把 STEP 3 执行轨迹上传到 HireBot 后端"
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
        "--output",
        required=True,
        help="上传结果 JSON 输出路径",
    )
    args = parser.parse_args()

    eval_ctx = _load_json(args.evaluation_context)

    try:
        base_url, employee_id, session_id = resolve_hirebot_api_config(eval_ctx)
        auth = resolve_auth_from_eval_ctx(eval_ctx)
    except (ValueError, RuntimeError) as exc:
        print(f"[错误] 配置解析失败: {exc}")
        return 1

    evaluation_id = str(eval_ctx.get("evaluation_id") or "").strip()

    print(f"[采集] 扫描轨迹目录: {args.traces_dir}")
    try:
        traces = collect_traces(args.traces_dir)
    except FileNotFoundError as exc:
        print(f"[错误] {exc}")
        return 1

    print(f"[采集] 共 {len(traces)} 个场景轨迹")
    payload = build_trace_bundle(evaluation_id, session_id, traces)

    trace_json_bytes = len(payload["traceJson"].encode("utf-8"))
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
        "response": response,
    })

    if not upload_ok:
        print(f"[错误] 上传失败: {response['_error']}")
        print(f"[输出] {args.output}")
        return 1

    print("[成功] 执行轨迹已上传到 HireBot 后端")
    print(f"[输出] {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
