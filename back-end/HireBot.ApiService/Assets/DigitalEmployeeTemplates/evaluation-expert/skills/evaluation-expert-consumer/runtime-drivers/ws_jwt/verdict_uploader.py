"""
verdict_uploader.py - 把评估结果上传到 HireBot 后端

职责：
  - 读取 evaluation_report.json（STEP 9 buildOverallReport 输出）
  - 读取 evaluation_context.json（与 run.py / trace_uploader.py 共用同一文件）
    - 构造轻量 EvaluationVerdictSyncRequestDto 并调用 sync-verdict
    - sync-verdict 成功后调用 report-content 接口，按 sessionId 上传完整 JSON / HTML 报告
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
import mimetypes
import urllib.error
import urllib.request
import uuid
from pathlib import Path
from typing import Any

from auth_client import resolve_auth_from_eval_ctx

_REPORT_CANDIDATE_NAMES = (
    "evaluation_report.json",
    "final_report.json",
    "evaluation_report_tainted.json",
    "final_report_tainted.json",
)


# ---------------------------------------------------------------------------
# 配置解析
# ---------------------------------------------------------------------------

def _load_json(path: str) -> dict[str, Any]:
    return json.loads(Path(path).read_text(encoding="utf-8"))


def _report_candidates(
    requested_path: Path,
    evaluation_context_path: Path,
    eval_ctx: dict[str, Any],
) -> list[Path]:
    paths = eval_ctx.get("paths") if isinstance(eval_ctx.get("paths"), dict) else {}
    run_dir_value = str(paths.get("run_dir") or "").strip()

    roots: list[Path] = []
    if requested_path.is_dir():
        roots.append(requested_path)
    roots.append(requested_path.parent)
    if run_dir_value:
        roots.append(Path(run_dir_value))
    roots.append(evaluation_context_path.parent)

    result: list[Path] = []
    seen: set[str] = set()
    for root in roots:
        if not str(root):
            continue

        candidates = [root / name for name in _REPORT_CANDIDATE_NAMES]
        candidates.extend(root / "reports" / name for name in _REPORT_CANDIDATE_NAMES)
        for candidate in candidates:
            key = str(candidate)
            if key in seen:
                continue
            seen.add(key)
            result.append(candidate)

    return result


def _resolve_report_path(
    requested_report_path: str,
    evaluation_context_path: str,
    eval_ctx: dict[str, Any],
) -> Path:
    requested = Path(requested_report_path)
    if requested.is_file():
        return requested

    context_path = Path(evaluation_context_path)
    candidates = _report_candidates(requested, context_path, eval_ctx)
    for candidate in candidates:
        if candidate.is_file():
            return candidate

    rendered = "\n  - ".join(str(candidate) for candidate in candidates)
    raise FileNotFoundError(
        "evaluation report file not found. Checked:\n"
        f"  - {requested}\n"
        f"  - {rendered}"
    )


def _unwrap_report(data: dict[str, Any]) -> dict[str, Any]:
    for key in ("evaluation_report", "report", "evaluation_result"):
        nested = data.get(key)
        if isinstance(nested, dict):
            return nested
    return data


def _coerce_float(value: Any, default: float = 0.0) -> float:
    if isinstance(value, bool):
        return default
    if isinstance(value, (int, float)):
        return float(value)
    if isinstance(value, str):
        text = value.strip().rstrip("%")
        if not text:
            return default
        try:
            return float(text)
        except ValueError:
            return default
    return default


def _coerce_bool(value: Any) -> Any:
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float)):
        return value != 0
    if isinstance(value, str):
        text = value.strip().lower()
        if text in ("true", "pass", "passed", "yes", "y", "1"):
            return True
        if text in ("false", "fail", "failed", "no", "n", "0"):
            return False
    return None


def _resolve_overall_score(report: dict[str, Any]) -> float:
    return _coerce_float(report.get("overall_score", report.get("overallScore")))


def _resolve_passed(report: dict[str, Any]) -> bool:
    explicit = _coerce_bool(report.get("passed"))
    if explicit is not None:
        return bool(explicit)

    verdict = str(report.get("verdict") or report.get("result") or "").strip().upper()
    if verdict in ("PASS", "PASSED"):
        return True
    if verdict in ("FAIL", "FAILED"):
        return False

    return _resolve_overall_score(report) >= 60.0


def _is_tainted_report(report: dict[str, Any], report_path: Path | None = None) -> bool:
    if report_path is not None and "tainted" in report_path.name.lower():
        return True

    explicit = _coerce_bool(report.get("tainted") or report.get("is_tainted"))
    if explicit is not None:
        return bool(explicit)

    for key in ("status", "run_status", "runStatus", "compliance_status", "complianceStatus"):
        value = str(report.get(key) or "").strip().lower()
        if "tainted" in value:
            return True

    violations = report.get("violations") or report.get("k_rule_violations")
    if isinstance(violations, list):
        for violation in violations:
            text = json.dumps(violation, ensure_ascii=False).lower()
            if "tainted" in text or "k8" in text:
                return True

    return False


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

def _build_dimension_scores_for_upload(report: dict[str, Any]) -> list[dict[str, Any]]:
    dim_scores = report.get("dimension_scores") or report.get("dimensionScores") or {}
    per_metric = report.get("per_metric_final_scores") or report.get("perMetricFinalScores") or []
    selected_metrics = report.get("selected_metrics") or report.get("selectedMetrics") or []

    if isinstance(dim_scores, list):
        result: list[dict[str, Any]] = []
        for item in dim_scores:
            if not isinstance(item, dict):
                continue

            evidence_refs = item.get("evidenceRefs") or item.get("evidence_refs") or []
            if not isinstance(evidence_refs, list):
                evidence_refs = [evidence_refs]

            dimension = str(item.get("dimension") or item.get("name") or "").strip()
            if not dimension:
                continue

            result.append({
                "dimension": dimension,
                "score": _coerce_float(
                    item.get("score", item.get("final_score", item.get("finalScore")))
                ),
                "comment": str(item.get("comment") or item.get("summary") or ""),
                "evidenceRefs": [str(ref) for ref in evidence_refs if str(ref).strip()],
            })
        return result

    if not isinstance(dim_scores, dict):
        return []

    metric_to_dim: dict[str, str] = {}
    for metric in selected_metrics:
        if not isinstance(metric, dict):
            continue
        metric_code = str(metric.get("metric_code") or "").strip()
        parent_dimension = str(metric.get("parent_dimension") or "").strip()
        if metric_code and parent_dimension:
            metric_to_dim[metric_code] = parent_dimension

    dim_to_metric_scores: dict[str, list[dict[str, Any]]] = {}
    for metric_score in per_metric:
        if not isinstance(metric_score, dict):
            continue
        metric_code = str(metric_score.get("metric_code") or "").strip()
        parent_dimension = metric_to_dim.get(metric_code)
        if parent_dimension:
            dim_to_metric_scores.setdefault(parent_dimension, []).append(metric_score)

    result: list[dict[str, Any]] = []
    for dimension_name, dimension_score in dim_scores.items():
        evidence_refs: list[str] = []
        comment = ""
        if isinstance(dimension_score, dict):
            score = _coerce_float(
                dimension_score.get(
                    "final_score",
                    dimension_score.get("finalScore", dimension_score.get("score")),
                )
            )
            comment = str(
                dimension_score.get("comment")
                or dimension_score.get("summary")
                or dimension_score.get("status")
                or ""
            )
            adjustments = dimension_score.get("adjustments") or []
            if isinstance(adjustments, list):
                for adjustment in adjustments:
                    if not isinstance(adjustment, dict):
                        continue
                    evidence = str(adjustment.get("evidence") or "").strip()
                    if evidence:
                        evidence_refs.append(evidence)
        else:
            score = _coerce_float(dimension_score)

        metrics_in_dimension = dim_to_metric_scores.get(str(dimension_name), [])
        if metrics_in_dimension:
            parts = [
                f"{metric.get('metric_code')}={_coerce_float(metric.get('final_score', metric.get('score'))):.1f}"
                for metric in metrics_in_dimension
            ]
            comment = "子指标: " + "、".join(parts)

        result.append({
            "dimension": str(dimension_name),
            "score": score,
            "comment": comment,
            "evidenceRefs": evidence_refs,
        })

    return result


def _with_tainted_prefix(summary: str, tainted: bool) -> str:
    if tainted and not summary.upper().startswith("TAINTED"):
        return f"TAINTED run: {summary}"
    return summary


def _build_summary_for_upload(report: dict[str, Any], *, tainted: bool = False) -> str:
    narrative = report.get("narrative") if isinstance(report.get("narrative"), dict) else {}
    for value in (
        narrative.get("executive_summary"),
        narrative.get("verdict_summary"),
        report.get("executive_summary"),
        report.get("summary"),
        report.get("reason"),
    ):
        summary = str(value or "").strip()
        if summary:
            return _with_tainted_prefix(summary, tainted)

    red_line = report.get("red_line") if isinstance(report.get("red_line"), dict) else {}
    if red_line.get("triggered"):
        triggers = red_line.get("triggers") or []
        rules = []
        for trigger in triggers[:3]:
            if isinstance(trigger, dict):
                rules.append(str(trigger.get("rule") or trigger.get("metric_code") or trigger))
            else:
                rules.append(str(trigger))
        if rules:
            return "触发红线: " + "、".join(rules)

    red_lines = report.get("red_lines_triggered") or report.get("red_lines") or []
    if isinstance(red_lines, list) and red_lines:
        return "触发红线: " + "、".join(str(item) for item in red_lines[:3])

    issues = report.get("issues") or []
    if isinstance(issues, list) and issues:
        descriptions = []
        for issue in issues[:3]:
            if isinstance(issue, dict):
                descriptions.append(str(issue.get("description") or issue.get("message") or issue))
            else:
                descriptions.append(str(issue))
        return "主要问题: " + "；".join(descriptions)

    return "综合评估通过" if _resolve_passed(report) else "综合评估未通过"


def build_verdict_payload(
    session_id: str,
    report: dict[str, Any],
    *,
    tainted: bool = False,
    report_json_content: str | None = None,
    report_html_content: str | None = None,
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

        report_json_content / report_html_content 仅保留给兼容调用方；主流程会通过 report-content 接口单独上传。
    """
    passed = False if tainted else _resolve_passed(report)
    overall_score = _resolve_overall_score(report)

    payload: dict[str, Any] = {
        "sessionId": session_id,
        "verdict": {
            "verdict": "PASS" if passed else "FAIL",
            "overallScore": overall_score,
            "summary": _build_summary_for_upload(report, tainted=tainted),
            "dimensionScores": _build_dimension_scores_for_upload(report),
        },
    }
    if report_json_content is not None:
        payload["reportJsonContent"] = report_json_content
    if report_html_content is not None:
        payload["reportHtmlContent"] = report_html_content
    return payload


# ---------------------------------------------------------------------------
# HTTP 上传
# ---------------------------------------------------------------------------

def upload_verdict(
    base_url: str,
    auth_headers: dict[str, str],
    employee_id: str,
    payload: dict[str, Any],
    *,
    timeout: int = 120,
) -> dict[str, Any]:
    """
    调用 POST /api/v1/employees/{employeeId}/evaluation/sync-verdict。

    timeout 默认 120 秒——HTML 报告可达 70KB+，加上 JSON 报告内容，
    整个 body 可能超过 200KB，30 秒超时不够用。

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
        with urllib.request.urlopen(req, timeout=timeout) as resp:
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


def _append_multipart_field(body: bytearray, boundary: str, name: str, value: str) -> None:
    body.extend(f"--{boundary}\r\n".encode("utf-8"))
    body.extend(f'Content-Disposition: form-data; name="{name}"\r\n\r\n'.encode("utf-8"))
    body.extend(value.encode("utf-8"))
    body.extend(b"\r\n")


def _append_multipart_file(body: bytearray, boundary: str, name: str, path: Path) -> int:
    content = path.read_bytes()
    mime_type = mimetypes.guess_type(path.name)[0] or "application/octet-stream"
    body.extend(f"--{boundary}\r\n".encode("utf-8"))
    body.extend(
        f'Content-Disposition: form-data; name="{name}"; filename="{path.name}"\r\n'.encode("utf-8")
    )
    body.extend(f"Content-Type: {mime_type}\r\n\r\n".encode("utf-8"))
    body.extend(content)
    body.extend(b"\r\n")
    return len(content)


def upload_report_content(
    base_url: str,
    auth_headers: dict[str, str],
    employee_id: str,
    session_id: str,
    report_json_path: Path,
    report_html_path: Path | None,
    *,
    timeout: int = 120,
) -> dict[str, Any]:
    """调用 POST /api/v1/employees/{employeeId}/evaluation/report-content 上传报告文件。"""
    url = f"{base_url}/api/v1/employees/{employee_id}/evaluation/report-content"
    boundary = f"----hirebot-report-{uuid.uuid4().hex}"
    body = bytearray()
    _append_multipart_field(body, boundary, "sessionId", session_id)

    uploaded_bytes = 0
    if report_json_path.is_file():
        uploaded_bytes += _append_multipart_file(body, boundary, "reportJsonFile", report_json_path)
    if report_html_path is not None and report_html_path.is_file():
        uploaded_bytes += _append_multipart_file(body, boundary, "reportHtmlFile", report_html_path)

    body.extend(f"--{boundary}--\r\n".encode("utf-8"))

    headers = {
        "Content-Type": f"multipart/form-data; boundary={boundary}",
        "Accept": "application/json",
    }
    headers.update(auth_headers)

    req = urllib.request.Request(
        url,
        data=bytes(body),
        method="POST",
        headers=headers,
    )

    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            result = json.loads(resp.read().decode("utf-8"))
            result["_uploadedBytes"] = uploaded_bytes
            return result
    except urllib.error.HTTPError as e:
        body_bytes = e.read()
        try:
            err_body = json.loads(body_bytes.decode("utf-8"))
        except Exception:
            err_body = body_bytes.decode("utf-8", errors="replace")
        return {"_error": f"HTTP {e.code}", "url": url, "response": err_body, "_uploadedBytes": uploaded_bytes}
    except Exception as exc:
        return {"_error": str(exc), "url": url, "_uploadedBytes": uploaded_bytes}


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
    _log("STARTUP", f"--evaluation-report  {args.evaluation_report}")
    _log("STARTUP", f"--output             {args.output}")
    _log("STARTUP", f"log → {log_path}")

    eval_ctx = _load_json(args.evaluation_context)
    try:
        report_path = _resolve_report_path(
            args.evaluation_report,
            args.evaluation_context,
            eval_ctx,
        )
        report = _unwrap_report(_load_json(str(report_path)))
    except FileNotFoundError as exc:
        _log("ERROR", str(exc))
        _write_json(args.output, {
            "status": "error",
            "error": str(exc),
        })
        _logf.close()
        return 1

    _log("REPORT", f"resolved → {report_path}")

    # 完整报告内容不再内联到 sync-verdict，改为拿到 reportId 后单独上传。
    report_json_bytes = report_path.stat().st_size if report_path.is_file() else 0
    report_html_path: Path | None = None
    html_candidates = [
        report_path.with_suffix(".html"),
        report_path.parent / "evaluation_report.html",
        report_path.parent / "final_report.html",
    ]
    for html_path in html_candidates:
        if html_path.is_file():
            report_html_path = html_path
            _log("REPORT", f"report html found ← {html_path} ({html_path.stat().st_size} bytes)")
            break
    if report_html_path is None:
        _log("REPORT", "evaluation_report.html 未找到，后端将使用自动生成的可视化页面")

    try:
        base_url, employee_id, session_id = resolve_hirebot_api_config(eval_ctx)
        auth = resolve_auth_from_eval_ctx(eval_ctx)
    except (ValueError, RuntimeError) as exc:
        _log("ERROR", f"配置解析失败: {exc}")
        _logf.close()
        return 1

    tainted = _is_tainted_report(report, report_path)
    overall_score = _resolve_overall_score(report)
    passed = False if tainted else _resolve_passed(report)
    verdict_str = "PASS" if passed else "FAIL"

    payload = build_verdict_payload(
        session_id,
        report,
        tainted=tainted,
    )

    # 计算 payload 大小，超过阈值时打印警告
    payload_bytes = len(json.dumps(payload, ensure_ascii=False).encode("utf-8"))
    payload_kb = payload_bytes / 1024
    _log("UPLOAD", f"base_url={base_url}")
    _log("UPLOAD", f"employee_id={employee_id}")
    _log("UPLOAD", f"session_id={session_id}")
    _log("UPLOAD", f"verdict={verdict_str}  score={overall_score}  tainted={tainted}")
    _log("UPLOAD", f"auth_source={auth.source}")
    _log("UPLOAD", f"payload_size={payload_kb:.1f}KB  report_json={report_json_bytes // 1024}KB  report_html={(report_html_path.stat().st_size if report_html_path else 0) // 1024}KB")

    print(f"[上传] 目标地址:   {base_url}")
    print(f"[上传] 员工 ID:    {employee_id}")
    print(f"[上传] 会话 ID:    {session_id}")
    print(f"[上传] 评估结论:   {verdict_str}，综合评分 {overall_score}")
    print(f"[上传] Payload:    {payload_kb:.1f} KB")
    print(f"[上传] 鉴权方式:   {auth.source}")

    response = upload_verdict(base_url, auth.build_http_headers(), employee_id, payload)
    verdict_upload_ok = "_error" not in response

    report_upload_response: dict[str, Any] | None = None
    if verdict_upload_ok:
        _log("REPORT", f"upload report content by session_id={session_id}")
        report_upload_response = upload_report_content(
            base_url,
            auth.build_http_headers(),
            employee_id,
            session_id,
            report_path,
            report_html_path,
        )
        if "_error" in report_upload_response:
            _log("ERROR", f"报告内容上传失败: {report_upload_response['_error']}")

    report_upload_ok = report_upload_response is not None and "_error" not in report_upload_response
    upload_ok = verdict_upload_ok and report_upload_ok
    report_upload_data = report_upload_response.get("data") if isinstance(report_upload_response, dict) and isinstance(report_upload_response.get("data"), dict) else {}
    report_id = report_upload_data.get("reportId") or report_upload_data.get("report_id")

    payload_for_log = dict(payload)
    payload_for_log["_reportJsonBytes"] = report_json_bytes
    payload_for_log["_reportHtmlBytes"] = report_html_path.stat().st_size if report_html_path else 0

    _write_json(args.output, {
        "status": "success" if upload_ok else "error",
        "employee_id": employee_id,
        "session_id": session_id,
        "report_id": report_id,
        "report_path": str(report_path),
        "report_html_path": str(report_html_path) if report_html_path else None,
        "tainted": tainted,
        "verdict": verdict_str,
        "overall_score": overall_score,
        "payload_kb": round(payload_kb, 1),
        "request_payload": payload_for_log,
        "verdict_response": response,
        "report_upload_response": report_upload_response,
    })

    if not upload_ok:
        error = response.get("_error") if not verdict_upload_ok else report_upload_response.get("_error") if report_upload_response else "report upload skipped"
        _log("ERROR", f"上传失败: {error}")
        print(f"[错误] 上传失败: {error}")
        print(f"[输出] {args.output}")
        _logf.close()
        return 1

    _log("SUCCESS", f"评估结论和报告内容已上传  output={args.output}")
    print("[成功] 评估结论和报告内容已上传到 HireBot 后端")
    print(f"[输出] {args.output}")
    _logf.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
