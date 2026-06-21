"""
report_assembler.py — HTML 报告确定性装配器

职责：
  读取最终汇总的 evaluation_report.json + 各 TC 的 scenario_report / trace / enriched，
  将数据注入 report-template.html 的三个占位符，生成可直接阅读和保存的 HTML 报告。

占位符合同（模板中固定，不得变更）：
  {{EMPLOYEE_NAME}}  → 员工显示名称（直接字符串替换）
  {{REPORT_DATA}}    → evaluation_report.json 完整内容（JSON 序列化后注入 <script> 块）
  {{SCENARIOS_DATA}} → 场景数组（每项含 report / trace / enriched，JSON 序列化）

模板 JS 期望的数据字段（从模板 render logic 中提取）：
  REPORT_DATA（evaluation_report.json）：
    report.employee.display_name / employee_id / role
    report.generated_at
    report.passed
    report.overall_score
    report.dimension_scores        { functional_completeness, interaction_quality,
                                     process_compliance, problem_resolution, tool_call_correctness }
    report.red_line.triggered
    report.red_line.narratives     (字符串数组，中文叙述)
    report.executive_summary  OR  report.narrative.executive_summary
    report.strengths          OR  report.narrative.strengths
    report.weaknesses         OR  report.narrative.weaknesses
    report.cross_scenario_patterns OR report.narrative.cross_scenario_patterns
    report.improvement_plan   OR  report.narrative.improvement_plan
    report.open_questions          (字符串或 {question, context} 数组)
    report.metric_labels           { metric_code: 中文名 }  — K18
    report.tool_labels             { tool_name:   中文名 }  — K18
    report.evaluation_id

  SCENARIOS_DATA（数组，每项）：
    sc.report   → scenario_report.json 内容
      .test_case_id / test_case_display_name / scenario_name
      .metric_results[]  { metric_code, score, signals/observed_signals }
      .what_went_well / what_went_wrong / narrative.what_went_well / narrative.what_went_wrong
    sc.trace    → trace.json 内容
      .dialog_turns[]      { actor, content }
      .simulator_trail[]   { turn_index, internal_emotion, perceived_progress,
                             should_continue, stop_reason, rationale }
      .actual_tool_calls[] { tool_name, arguments, outcome, after_turn_index }
    sc.enriched → enriched_test_case.json 内容（可选，模板暂未使用）

调用方式：
  python3 runtime-drivers/ws_jwt/report_assembler.py \\
    --evaluation-report   runs/<eval_id>/reports/evaluation_report.json \\
    --scenarios-dir       runs/<eval_id>/reports/scenarios \\
    --traces-dir          runs/<eval_id>/traces \\
    --enriched-dir        runs/<eval_id>/enriched-cases \\
    --template            runtime-schemas/report-template.html \\
    --output              runs/<eval_id>/reports/evaluation_report.html

退出码：
  0 — 成功
  1 — 文件缺失 / JSON 解析失败 / 必填字段缺失
  2 — 自检失败（占位符未替换 / script 块 JSON 非法）
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


# ---------------------------------------------------------------------------
# JSON 工具
# ---------------------------------------------------------------------------

def _load_json(path: Path, label: str) -> dict[str, Any]:
    """加载 JSON 文件，缺失或解析失败时打印明确原因并 raise。"""
    if not path.exists():
        raise FileNotFoundError(f"{label} 文件不存在: {path}")
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        raise ValueError(
            f"{label} JSON 解析失败: {path}\n"
            f"  位置: line {exc.lineno} col {exc.colno} — {exc.msg}"
        ) from exc


def _to_json_for_script(obj: Any) -> str:
    """
    将 Python 对象序列化为注入 <script type="application/json"> 的 JSON 字符串。

    关键处理：
    1. ensure_ascii=False — 保留中文，不产生 \\uXXXX 转义
    2. </script> → <\\/script> — 防止 script 标签被提前闭合
    """
    raw = json.dumps(obj, ensure_ascii=False, separators=(",", ":"))
    return raw.replace("</script>", "<\\/script>")


# ---------------------------------------------------------------------------
# 员工名称推导
# ---------------------------------------------------------------------------

def _resolve_employee_name(report: dict[str, Any], override: str | None) -> str:
    if override and override.strip():
        return override.strip()
    emp = report.get("employee") or {}
    if isinstance(emp, dict):
        name = (
            emp.get("display_name")
            or emp.get("name")
            or emp.get("employee_id")
            or ""
        )
        return str(name).strip() or "未知员工"
    return str(emp).strip() or "未知员工"


# ---------------------------------------------------------------------------
# 场景数据收集
# ---------------------------------------------------------------------------

def _collect_one_scenario(
    tc_id: str,
    scenarios_dir: Path,
    traces_dir: Path,
    enriched_dir: Path,
) -> dict[str, Any]:
    """
    收集单个 TC 的三元组：{ report, trace, enriched }。
    任何文件缺失时用空 dict 兜底，不中断整体流程。
    """
    result: dict[str, Any] = {"report": {}, "trace": {}, "enriched": {}}

    # scenario report
    rp = scenarios_dir / f"{tc_id}.report.json"
    if rp.exists():
        try:
            result["report"] = _load_json(rp, f"scenario_report({tc_id})")
        except (FileNotFoundError, ValueError) as exc:
            print(f"[warn] {exc}", file=sys.stderr)
    else:
        print(f"[warn] scenario_report 不存在，已跳过: {rp}", file=sys.stderr)

    # trace
    tp = traces_dir / f"{tc_id}.trace.json"
    if tp.exists():
        try:
            result["trace"] = _load_json(tp, f"trace({tc_id})")
        except (FileNotFoundError, ValueError) as exc:
            print(f"[warn] {exc}", file=sys.stderr)
    else:
        print(f"[warn] trace 不存在（failed TC?），已跳过: {tp}", file=sys.stderr)

    # enriched（兼容两种命名）
    ep = enriched_dir / f"{tc_id}.enriched.json"
    if not ep.exists():
        ep = enriched_dir / f"{tc_id}.json"
    if ep.exists():
        try:
            result["enriched"] = _load_json(ep, f"enriched({tc_id})")
        except (FileNotFoundError, ValueError) as exc:
            print(f"[warn] {exc}", file=sys.stderr)
    else:
        print(f"[warn] enriched 不存在，已跳过: {ep}", file=sys.stderr)

    return result


def _collect_all_scenarios(
    report: dict[str, Any],
    scenarios_dir: Path,
    traces_dir: Path,
    enriched_dir: Path,
) -> list[dict[str, Any]]:
    """
    按 report.test_cases 的顺序收集所有场景数据。
    若 test_cases 字段缺失，则扫描 scenarios_dir 目录。
    """
    test_cases = report.get("test_cases") or []
    if test_cases:
        tc_ids = [
            str(tc.get("test_case_id") or tc.get("tc_id") or "").strip()
            for tc in test_cases
            if isinstance(tc, dict)
        ]
        tc_ids = [t for t in tc_ids if t]
    else:
        # 降级：按文件名字母序扫描目录
        tc_ids = sorted(
            p.stem.replace(".report", "")
            for p in scenarios_dir.glob("*.report.json")
        )
        if not tc_ids:
            print("[warn] report.test_cases 为空，且 scenarios_dir 无 .report.json 文件", file=sys.stderr)

    return [
        _collect_one_scenario(tc_id, scenarios_dir, traces_dir, enriched_dir)
        for tc_id in tc_ids
    ]


# ---------------------------------------------------------------------------
# 自检
# ---------------------------------------------------------------------------

def _self_check(html: str) -> list[str]:
    """
    检查生成的 HTML 的基本完整性。返回所有违规描述，空列表 = 通过。
    """
    violations: list[str] = []

    # 1. 三个占位符必须全部被替换
    for ph in ("{{REPORT_DATA}}", "{{SCENARIOS_DATA}}", "{{EMPLOYEE_NAME}}"):
        if ph in html:
            violations.append(f"占位符未被替换: {ph}")

    # 2. 两个 script 块的内容必须是合法 JSON
    for block_id in ("report-data", "scenarios-data"):
        open_tag = f'<script id="{block_id}" type="application/json">'
        close_tag = "</script>"
        idx_open = html.find(open_tag)
        if idx_open == -1:
            violations.append(f'缺少 <script id="{block_id}"> 块')
            continue
        idx_content_start = idx_open + len(open_tag)
        idx_close = html.find(close_tag, idx_content_start)
        if idx_close == -1:
            violations.append(f'<script id="{block_id}"> 块未正常闭合')
            continue
        raw = html[idx_content_start:idx_close].strip()
        # 还原转义再验证
        raw = raw.replace("<\\/script>", "</script>")
        try:
            json.loads(raw)
        except json.JSONDecodeError as exc:
            violations.append(
                f'<script id="{block_id}"> 内容不是合法 JSON: '
                f'line {exc.lineno} col {exc.colno} — {exc.msg}'
            )

    return violations


# ---------------------------------------------------------------------------
# 主装配函数
# ---------------------------------------------------------------------------

def assemble(
    report_path: Path,
    scenarios_dir: Path,
    traces_dir: Path,
    enriched_dir: Path,
    template_path: Path,
    output_path: Path,
    employee_name_override: str | None = None,
) -> None:
    """
    装配 HTML 报告。成功写入 output_path，失败时 raise。
    """
    # ── 1. 读取模板和报告 ─────────────────────────────────────────────────
    if not template_path.exists():
        raise FileNotFoundError(f"模板文件不存在: {template_path}")
    template = template_path.read_text(encoding="utf-8")

    report = _load_json(report_path, "evaluation_report")

    # ── 2. 基础字段校验 ───────────────────────────────────────────────────
    missing = [f for f in ("evaluation_id", "overall_score", "passed", "dimension_scores")
               if f not in report]
    if missing:
        raise ValueError(
            f"evaluation_report.json 缺少必填字段: {missing}\n"
            f"  文件: {report_path}"
        )

    # ── 3. 收集场景数据 ────────────────────────────────────────────────────
    scenarios = _collect_all_scenarios(report, scenarios_dir, traces_dir, enriched_dir)

    # ── 4. 推导员工显示名称 ────────────────────────────────────────────────
    employee_name = _resolve_employee_name(report, employee_name_override)

    # ── 5. 序列化 JSON 数据 ────────────────────────────────────────────────
    report_data_json = _to_json_for_script(report)
    scenarios_data_json = _to_json_for_script(scenarios)

    # ── 6. 三次字符串替换（顺序无关） ──────────────────────────────────────
    html = template
    html = html.replace("{{EMPLOYEE_NAME}}", employee_name, 1)
    html = html.replace("{{REPORT_DATA}}", report_data_json, 1)
    html = html.replace("{{SCENARIOS_DATA}}", scenarios_data_json, 1)

    # ── 7. 自检 ───────────────────────────────────────────────────────────
    violations = _self_check(html)
    if violations:
        raise RuntimeError(
            f"HTML 自检失败（{len(violations)} 项违规）：\n"
            + "\n".join(f"  - {v}" for v in violations)
            + "\n\n报告未写入。请检查 evaluation_report.json 内容后重新运行。"
        )

    # ── 8. 写入输出 ───────────────────────────────────────────────────────
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(html, encoding="utf-8")

    sc_count = len(scenarios)
    ok_count = sum(1 for sc in scenarios if sc.get("report"))
    print(
        f"[report_assembler] 报告生成成功\n"
        f"  输出:   {output_path}\n"
        f"  大小:   {len(html):,} 字节\n"
        f"  场景:   {ok_count}/{sc_count} 有 scenario_report\n"
        f"  员工:   {employee_name}\n"
        f"  评估ID: {report.get('evaluation_id', '?')}"
    )


# ---------------------------------------------------------------------------
# 命令行入口
# ---------------------------------------------------------------------------

def _parse_args() -> argparse.Namespace:
    p = argparse.ArgumentParser(
        description="report_assembler — 将 evaluation_report.json 注入 HTML 模板生成可读报告"
    )
    p.add_argument("--evaluation-report", required=True,
                   help="evaluation_report.json 路径")
    p.add_argument("--scenarios-dir", required=True,
                   help="scenario report 目录（含 <tc_id>.report.json）")
    p.add_argument("--traces-dir", required=True,
                   help="trace 目录（含 <tc_id>.trace.json）")
    p.add_argument("--enriched-dir", required=True,
                   help="enriched 目录（含 <tc_id>.enriched.json）")
    p.add_argument("--template", required=True,
                   help="report-template.html 路径")
    p.add_argument("--output", required=True,
                   help="输出 HTML 路径（evaluation_report.html）")
    p.add_argument("--employee-name", default=None,
                   help="员工显示名称（覆盖 report.employee.display_name）")
    return p.parse_args()


def main() -> int:
    args = _parse_args()
    try:
        assemble(
            report_path=Path(args.evaluation_report),
            scenarios_dir=Path(args.scenarios_dir),
            traces_dir=Path(args.traces_dir),
            enriched_dir=Path(args.enriched_dir),
            template_path=Path(args.template),
            output_path=Path(args.output),
            employee_name_override=args.employee_name,
        )
        return 0
    except FileNotFoundError as exc:
        print(f"[error] 文件不存在: {exc}", file=sys.stderr)
        return 1
    except ValueError as exc:
        print(f"[error] 数据验证失败:\n{exc}", file=sys.stderr)
        return 1
    except RuntimeError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    except Exception as exc:
        print(f"[error] 未预期的错误: {exc}", file=sys.stderr)
        import traceback
        traceback.print_exc(file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
