"""
verdict_uploader.py - 把评估结果上传到 HireBot 后端

职责：
  - 读取 evaluation_report.json（STEP 9 buildOverallReport 输出）
  - 读取 evaluation_context.json（与 run.py / trace_uploader.py 共用同一文件）
  - 构造 EvaluationVerdictSyncRequestDto
  - 调用 POST {base_url}/api/v1/employees/{employeeId}/evaluation/sync-verdict
  - 把上传结果写入 output 文件

字段来源（对比旧版 live_evaluator/verdict_uploader.py 的映射变化）：
  旧 runtime_context.session.employee_id      → evaluation_context.hirebot_api.employee_id
  旧 runtime_context.session.session_id       → evaluation_context.hirebot_api.session_id
  旧 runtime_context.ncrew_hire.base_url      → evaluation_context.hirebot_api.base_url
  旧 runtime_context.target_sandbox.auth      → evaluation_context.hirebot_api.auth
                                                (缺省 fallback: runtime_driver.driver_config.token)

评估报告格式变化（STEP 9 evaluation_report.json vs 旧 evaluator evaluation_result.json）：
  新 report.dimension_scores         → {dimension: score} 扁平 dict（来自 STEP 6 byte-copy）
  新 report.per_metric_final_scores  → [{metric_code, final_score, ...}] 数组
  新 report.narrative.executive_summary → 综合摘要文本
  新 report.passed / overall_score   → 与旧格式相同
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
# 配置解析
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
# 数据转换：evaluation_report.json → API 请求体
# ---------------------------------------------------------------------------

def _build_dimension_scores(report: dict[str, Any]) -> list[dict[str, Any]]:
    """
    将 STEP 9 evaluation_report.json 中的 dimension_scores 和 per_metric_final_scores
    转换为 EvaluationDimensionScoreDto 列表。

    STEP 9 格式（dimension_scores 是 STEP 6 byte-copy，per_metric_final_scores 是 STEP 5 byte-copy）：
      dimension_scores: { "functional_completeness": 75, "interaction_quality": 82, ... }
      per_metric_final_scores: [{ "metric_code": "factual_accuracy", "final_score": 82, ... }, ...]
      selected_metrics: [{ "metric_code": "...", "parent_dimension": "...", ... }, ...]

    目标格式 (EvaluationDimensionScoreDto):
      { "dimension": str, "score": float, "comment": str, "evidenceRefs": [str] }
    """
    dim_scores: dict[str, Any] = report.get("dimension_scores") or {}
    per_metric: list[dict[str, Any]] = report.get("per_metric_final_scores") or []
    selected_metrics: list[dict[str, Any]] = report.get("selected_metrics") or []

    # metric_code → parent_dimension 映射
    metric_to_dim: dict[str, str] = {
        m["metric_code"]: m["parent_dimension"]
        for m in selected_metrics
        if m.get("metric_code") and m.get("parent_dimension")
    }

    # 按 parent_dimension 分组的子指标分数
    dim_to_metric_scores: dict[str, list[dict[str, Any]]] = {}
    for ms in per_metric:
        mc = ms.get("metric_code", "")
        parent_dim = metric_to_dim.get(mc, "")
        if parent_dim:
            dim_to_metric_scores.setdefault(parent_dim, []).append(ms)

    result: list[dict[str, Any]] = []
    for dim_name, score_val in dim_scores.items():
        score = float(score_val)
        metrics_in_dim = dim_to_metric_scores.get(dim_name, [])

        if metrics_in_dim:
            parts = [
                f"{ms['metric_code']}={ms['final_score']}"
                for ms in metrics_in_dim
            ]
            comment = "子指标: " + "、".join(parts)
        else:
            comment = ""

        result.append({
            "dimension": dim_name,
            "score": score,
            "comment": comment,
            "evidenceRefs": [],
        })

    return result


def _build_summary(report: dict[str, Any]) -> str:
    """从 evaluation_report.json 提取综合摘要文本。"""
    # 1. STEP 9 LLM 撰写的 executive_summary
    narrative = report.get("narrative") or {}
    summary = str(narrative.get("executive_summary") or "").strip()
    if summary:
        return summary

    # 2. 红线触发说明
    red_line = report.get("red_line") or {}
    if red_line.get("triggered"):
        triggers = red_line.get("triggers") or []
        rules = [str(t.get("rule") or t) for t in triggers[:3]]
        return "触发红线：" + "、".join(rules)

    # 3. 最终 pass/fail 兜底
    passed = bool(report.get("passed"))
    return "综合评估通过" if passed else "综合评估未通过"


def build_verdict_payload(
    session_id: str,
    report: dict[str, Any],
) -> dict[str, Any]:
    """
    构造 EvaluationVerdictSyncRequestDto 字典。

    对应 C# 结构：
      {
        sessionId: string,
        verdict: EvaluationVerdictPayloadDto {
          verdict: "PASS" | "FAIL",
          overallScore: decimal,
          summary: string,
          dimensionScores: EvaluationDimensionScoreDto[]
        }
      }
    """
    passed = bool(report.get("passed"))
    overall_score = float(report.get("overall_score") or 0)

    return {
        "sessionId": session_id,
        "verdict": {
            "verdict": "PASS" if passed else "FAIL",
            "overallScore": overall_score,
            "summary": _build_summary(report),
            "dimensionScores": _build_dimension_scores(report),
        },
    }


# ---------------------------------------------------------------------------
# HTTP 上传
# ---------------------------------------------------------------------------

def upload_verdict(
    base_url: str,
    auth_headers: dict[str, str],
    employee_id: str,
    payload: dict[str, Any],
) -> dict[str, Any]:
    """
    调用 POST /api/v1/employees/{employeeId}/evaluation/sync-verdict。

    Returns:
        后端返回的 JSON 响应，失败时包含 _error 字段。
    """
    url = f"{base_url}/api/v1/employees/{employee_id}/evaluation/sync-verdict"
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
        with urllib.request.urlopen(req, timeout=30) as resp:
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
        description="verdict_uploader - 把 STEP 9 评估报告上传到 HireBot 后端"
    )
    parser.add_argument(
        "--evaluation-context",
        required=True,
        help="evaluation_context.json 路径（runs/<eval_id>/evaluation_context.json）",
    )
    parser.add_argument(
        "--evaluation-report",
        required=True,
        help="STEP 9 输出的 evaluation_report.json 路径（runs/<eval_id>/reports/evaluation_report.json）",
    )
    parser.add_argument(
        "--output",
        required=True,
        help="上传结果 JSON 输出路径",
    )
    args = parser.parse_args()

    eval_ctx = _load_json(args.evaluation_context)
    report = _load_json(args.evaluation_report)

    try:
        base_url, employee_id, session_id = resolve_hirebot_api_config(eval_ctx)
        auth = resolve_auth_from_eval_ctx(eval_ctx)
    except (ValueError, RuntimeError) as exc:
        print(f"[错误] 配置解析失败: {exc}")
        return 1

    overall_score = float(report.get("overall_score") or 0)
    passed = bool(report.get("passed"))
    verdict_str = "PASS" if passed else "FAIL"

    payload = build_verdict_payload(session_id, report)

    print(f"[上传] 目标地址:   {base_url}")
    print(f"[上传] 员工 ID:    {employee_id}")
    print(f"[上传] 会话 ID:    {session_id}")
    print(f"[上传] 评估结论:   {verdict_str}，综合评分 {overall_score}")
    print(f"[上传] 鉴权方式:   {auth.source}")

    response = upload_verdict(base_url, auth.build_http_headers(), employee_id, payload)
    upload_ok = "_error" not in response

    _write_json(args.output, {
        "status": "success" if upload_ok else "error",
        "employee_id": employee_id,
        "session_id": session_id,
        "verdict": verdict_str,
        "overall_score": overall_score,
        "request_payload": payload,
        "response": response,
    })

    if not upload_ok:
        print(f"[错误] 上传失败: {response['_error']}")
        print(f"[输出] {args.output}")
        return 1

    print("[成功] 评估结论已上传到 HireBot 后端")
    print(f"[输出] {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
