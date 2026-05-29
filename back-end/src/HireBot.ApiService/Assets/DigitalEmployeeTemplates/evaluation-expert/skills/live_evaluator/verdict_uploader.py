"""
verdict_uploader.py - 把评估结果上传到 NCrew Hire 后端

职责：
  - 读取 evaluation_result.json（evaluator skill 输出）
  - 读取 runtime_context.json（运行时上下文，与 evaluate.py 共用同一文件）
  - 构造 EvaluationVerdictSyncRequestDto
  - 调用 POST {base_url}/api/v1/employees/{employeeId}/evaluation/sync-verdict
  - 把上传结果写入 output 文件

Token 获取：与 evaluate.py 相同，通过 auth_client.resolve_auth() 从 auth_config.json 读取。

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
# 配置解析
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
# 数据转换：evaluator 输出 → API 请求体
# ---------------------------------------------------------------------------

def _build_dimension_scores(evaluation_result: dict[str, Any]) -> list[dict[str, Any]]:
    """
    将 evaluator 输出的 dimension_scores 字典转换为 API 所需的列表格式。

    evaluator 输出格式（每个维度）：
      {
        "score": 20,
        "final_score": 20,
        "adjustments": [
          {"reason": "...", "delta": -10, "evidence": "..."},
          ...
        ],
        "status": "critical"
      }

    API 目标格式 (EvaluationDimensionScoreDto)：
      {
        "dimension": "functional_completeness",
        "score": 20.0,
        "comment": "完成4/6步骤（+0）；遗漏关键步骤（-30）",
        "evidenceRefs": ["证据1", "证据2"]
      }
    """
    raw_dimensions = evaluation_result.get("dimension_scores") or {}
    result: list[dict[str, Any]] = []

    for dim_name, dim_data in raw_dimensions.items():
        if isinstance(dim_data, dict):
            # final_score 优先，fallback 到 score
            score = float(dim_data.get("final_score") or dim_data.get("score") or 0)

            adjustments = dim_data.get("adjustments") or []
            comment_parts: list[str] = []
            evidence_refs: list[str] = []

            for adj in adjustments:
                if not isinstance(adj, dict):
                    continue
                reason = str(adj.get("reason") or "").strip()
                evidence = str(adj.get("evidence") or "").strip()
                if reason:
                    delta = adj.get("delta")
                    if delta is not None:
                        sign = "+" if isinstance(delta, (int, float)) and delta >= 0 else ""
                        comment_parts.append(f"{reason}（{sign}{delta}）")
                    else:
                        comment_parts.append(reason)
                if evidence:
                    evidence_refs.append(evidence)

            comment = "；".join(comment_parts) if comment_parts else str(dim_data.get("status") or "")
        else:
            # 简单数值格式
            score = float(dim_data)
            comment = ""
            evidence_refs = []

        result.append({
            "dimension": dim_name,
            "score": score,
            "comment": comment,
            "evidenceRefs": evidence_refs,
        })

    return result


def _build_summary(evaluation_result: dict[str, Any]) -> str:
    """从评估结果提取摘要文本。"""
    # 1. 顶层 reason（evaluator 输出的判断原因）
    reason = str(evaluation_result.get("reason") or "").strip()
    if reason:
        return reason

    # 2. 触发红线说明
    red_lines = evaluation_result.get("red_lines_triggered") or []
    if red_lines:
        return "触发红线：" + "、".join(str(r) for r in red_lines)

    # 3. issues 列表摘要
    issues = evaluation_result.get("issues") or []
    if issues:
        top_issues = [str(i.get("description") or i) for i in issues[:3]]
        return "主要问题：" + "；".join(top_issues)

    # 4. 默认摘要：优先读 verdict 字符串字段
    verdict_str = str(evaluation_result.get("verdict") or "").strip().upper()
    if not verdict_str:
        verdict_str = "PASS" if bool(evaluation_result.get("passed")) else "FAIL"
    return "综合评估通过" if verdict_str == "PASS" else "综合评估未通过"


def build_verdict_payload(
    session_id: str,
    evaluation_result: dict[str, Any],
) -> dict[str, Any]:
    """
    构造 EvaluationVerdictSyncRequestDto 的字典表示。

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
    # 优先读 evaluator 输出的 verdict 字符串（"PASS" / "FAIL"）。
    # 兜底：若不存在，则从 passed 布尔字段转换（兼容旧格式）。
    verdict_text = str(evaluation_result.get("verdict") or "").strip().upper()
    if verdict_text not in ("PASS", "FAIL"):
        verdict_text = "PASS" if bool(evaluation_result.get("passed")) else "FAIL"
    overall_score = float(evaluation_result.get("overall_score") or 0)

    return {
        "sessionId": session_id,
        "verdict": {
            "verdict": verdict_text,
            "overallScore": overall_score,
            "summary": _build_summary(evaluation_result),
            "dimensionScores": _build_dimension_scores(evaluation_result),
        },
    }


# ---------------------------------------------------------------------------
# HTTP 上传
# ---------------------------------------------------------------------------

def upload_verdict(
    base_url: str,
    token: str,
    employee_id: str,
    payload: dict[str, Any],
) -> dict[str, Any]:
    """
    调用 POST /api/v1/employees/{employeeId}/evaluation/sync-verdict。

    Returns:
        后端返回的 JSON 响应，失败时包含 _error 字段
    """
    url = f"{base_url}/api/v1/employees/{employee_id}/evaluation/sync-verdict"
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
        description="verdict_uploader - 把评估结果上传到 HireBot 后端"
    )
    parser.add_argument(
        "--runtime-context",
        required=True,
        help="运行时上下文 JSON 路径（与 evaluate.py 使用同一文件）",
    )
    parser.add_argument(
        "--evaluation-result",
        required=True,
        help="evaluator skill 输出的 evaluation_result.json 路径",
    )
    parser.add_argument(
        "--output",
        required=True,
        help="上传结果 JSON 输出路径",
    )
    args = parser.parse_args()

    runtime_context = _load_json(args.runtime_context)
    evaluation_data = _load_json(args.evaluation_result)

    # evaluator 输出可能把结果嵌套在 "evaluation_result" 键下，也可能直接作为根
    evaluation_result: dict[str, Any] = evaluation_data.get("evaluation_result") or evaluation_data

    # 从 runtime_context 提取必要字段
    session = runtime_context.get("session") or {}
    employee_id = str(session.get("employee_id") or "").strip()
    session_id = str(session.get("session_id") or "").strip()

    if not employee_id or not session_id:
        print("[错误] runtime_context.session 中缺少 employee_id 或 session_id")
        return 1

    try:
        base_url = resolve_base_url(runtime_context)
        # token 与 evaluate.py 相同，通过 auth_client.resolve_auth() 从 auth_config.json 获取
        auth = resolve_auth(runtime_context.get("target_sandbox", {}).get("auth"))
        token = auth.access_token
    except (ValueError, FileNotFoundError, RuntimeError) as exc:
        print(f"[错误] 配置解析失败: {exc}")
        return 1

    overall_score = float(evaluation_result.get("overall_score") or 0)
    passed = bool(evaluation_result.get("passed"))
    verdict_str = "PASS" if passed else "FAIL"

    payload = build_verdict_payload(session_id, evaluation_result)

    print(f"[上传] 目标地址: {base_url}")
    print(f"[上传] 员工 ID:  {employee_id}")
    print(f"[上传] 会话 ID:  {session_id}")
    print(f"[上传] 评估结果: {verdict_str}，综合评分 {overall_score}")

    response = upload_verdict(base_url, token, employee_id, payload)

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

    print("[成功] 评估结果已上传到 HireBot 后端")
    print(f"[输出] {args.output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
