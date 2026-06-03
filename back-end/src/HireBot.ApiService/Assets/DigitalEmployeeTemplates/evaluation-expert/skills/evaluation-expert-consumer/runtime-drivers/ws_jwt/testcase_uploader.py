"""
testcase_uploader.py - 把 STEP 1.5 合成的测试用例推送到 HireBot 后端

职责：
  - 扫描 runs/<eval_id>/synthesized-cases/ 目录下所有 *.json 文件
  - 打包为后端 ParseRuntimeTestcasesFromText 可解析的格式
  - 调用 POST {base_url}/api/v1/employees/{employeeId}/evaluation/sync-trace
  - 后端 EnsureQuestionCardsFromRuntimeTextAsync 会自动提取并持久化为
    Question Cards（testcases-json 资产），前端右侧面板即可展示。

与 trace_uploader.py 的区别：
  - trace_uploader 在 STEP 10 上传完整轨迹（含 turns、traces、test_cases）
  - testcase_uploader 在 STEP 1.5 之后立即上传仅含 test_cases 的轻量 bundle
    目的：让前端右侧面板在对话阶段就能看到合成用例卡片

为什么用 sync-trace 而不是新 API：
  - 后端 SyncTraceAsync 内建了 test_cases 提取逻辑
  - EnsureQuestionCardsFromRuntimeTextAsync 有早退守卫：已有卡片则跳过
  - 如果在 STEP 1.5 先调一次 → 卡片提前落盘 → 后续 trace_uploader 重入时被跳过
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
# 配置解析（与 trace_uploader.py 共用相同逻辑）
# ---------------------------------------------------------------------------

def _load_json(path: str) -> dict[str, Any]:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def resolve_hirebot_api_config(eval_ctx: dict[str, Any]) -> tuple[str, str, str]:
    api = eval_ctx.get("hirebot_api") or {}
    base_url = str(api.get("base_url") or "").strip().rstrip("/")
    employee_id = str(api.get("employee_id") or "").strip()
    session_id = str(api.get("session_id") or "").strip()

    if not base_url:
        raise ValueError(
            "evaluation_context.hirebot_api.base_url 未配置。"
        )
    if not employee_id:
        raise ValueError("evaluation_context.hirebot_api.employee_id 未配置。")
    if not session_id:
        raise ValueError("evaluation_context.hirebot_api.session_id 未配置。")

    return base_url, employee_id, session_id


# ---------------------------------------------------------------------------
# 合成用例采集
# ---------------------------------------------------------------------------

def collect_synthesized_testcases(synthesized_dir: str) -> list[dict[str, Any]]:
    """
    扫描 synthesized_dir 下所有 *.json 文件，返回内容列表。

    目录不存在或为空时返回 []（不报错，允许跳过）。
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


# ---------------------------------------------------------------------------
# Bundle 构造（仅含 test_cases，不含 traces/turns）
# ---------------------------------------------------------------------------

def build_testcase_bundle(
    evaluation_id: str,
    session_id: str,
    eval_ctx: dict[str, Any],
    testcases: list[dict[str, Any]],
) -> dict[str, Any]:
    """
    构造仅含 test_cases 的轻量 bundle。

    后端 SyncTraceAsync →
      PersistTextAssetAsync (存为 trace-json 资产) +
      EnsureQuestionCardsFromRuntimeTextAsync (提取 test_cases → Question Cards)

    因为 trace 是空的（没有实际轨迹），我们用一个显式标记让后续
    trace_uploader.py 的完整 bundle 可以覆盖这个占位资产。
    """
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
        "status": "testcases_only",
        "meta": {
            "session_id": session_id,
            "employee_name": employee.get("display_name") or session.get("employee_id") or "",
            "iteration": session.get("iteration"),
            "collected_at": datetime.now(timezone.utc).isoformat(),
            "target_sandbox_id": target_sandbox.get("sandbox_id") or "",
        },
        "turns": [],
        "trace_count": 0,
        "trace_format": "evaluation-expert-consumer.v2.testcase_bundle_only",
        "traces": [],
        # 核心：test_cases 数组 — 后端 CollectRuntimeTestcases 递归扫描会发现它
        "test_cases": testcases,
        "test_case_count": len(testcases),
        # 标记这是一个占位 bundle，后续 trace_uploader 会以完整数据覆盖
        "_placeholder": True,
    }

    return {
        "sessionId": session_id,
        "traceJson": json.dumps(bundle, ensure_ascii=False),
    }


# ---------------------------------------------------------------------------
# HTTP 上传（复用 sync-trace endpoint）
# ---------------------------------------------------------------------------

def upload_testcase_bundle(
    base_url: str,
    auth_headers: dict[str, str],
    employee_id: str,
    payload: dict[str, Any],
) -> dict[str, Any]:
    url = f"{base_url}/api/v1/employees/{employee_id}/evaluation/sync-trace"
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")

    headers = {"Content-Type": "application/json; charset=utf-8", "Accept": "application/json"}
    headers.update(auth_headers)

    req = urllib.request.Request(url, data=body, method="POST", headers=headers)

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
        description="testcase_uploader - 把 STEP 1.5 合成用例推送到 HireBot 后端（提前展示右侧卡片）"
    )
    parser.add_argument(
        "--evaluation-context",
        required=True,
        help="evaluation_context.json 路径",
    )
    parser.add_argument(
        "--synthesized-dir",
        required=True,
        help="STEP 1.5 合成用例目录（runs/<eval_id>/synthesized-cases/）",
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

    print(f"[采集] 扫描合成用例目录: {args.synthesized_dir}")
    testcases = collect_synthesized_testcases(args.synthesized_dir)
    if not testcases:
        print("[跳过] 没有找到合成用例，无需上传")
        _write_json(args.output, {
            "status": "skipped",
            "reason": "no_synthesized_testcases_found",
            "employee_id": employee_id,
            "session_id": session_id,
            "evaluation_id": evaluation_id,
        })
        return 0

    print(f"[采集] 共 {len(testcases)} 个合成用例")

    payload = build_testcase_bundle(evaluation_id, session_id, eval_ctx, testcases)

    trace_json_bytes = len(payload["traceJson"].encode("utf-8"))
    print(f"[上传] 目标地址:   {base_url}")
    print(f"[上传] 员工 ID:    {employee_id}")
    print(f"[上传] 会话 ID:    {session_id}")
    print(f"[上传] 用例数:     {len(testcases)}")
    print(f"[上传] Payload:    {trace_json_bytes / 1024:.1f} KB")
    print(f"[上传] 鉴权方式:   {auth.source}")

    response = upload_testcase_bundle(base_url, auth.build_http_headers(), employee_id, payload)
    upload_ok = "_error" not in response

    _write_json(args.output, {
        "status": "success" if upload_ok else "error",
        "employee_id": employee_id,
        "session_id": session_id,
        "evaluation_id": evaluation_id,
        "test_case_count": len(testcases),
        "response": response,
    })

    if not upload_ok:
        print(f"[错误] 上传失败: {response['_error']}")
        print(f"[输出] {args.output}")
        return 1

    print("[成功] 合成用例已推送到 HireBot 后端")
    print("  → 后端 EnsureQuestionCardsFromRuntimeTextAsync 将提取 test_cases 并生成 Question Cards")
    print("  → 前端右侧面板刷新后应可见用例卡片")
    print(f"[输出] {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
