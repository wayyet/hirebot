"""
material_loader.py - 评估材料加载与检查

职责：
  - 从评估沙箱本地 workspace 目录发现 testcases / ontology
  - 解析测试用例，生成题卡
  - 汇总 ontology 权重与规则，输出就绪状态

不负责连接目标沙箱，也不做评分。
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

_DEFAULT_WORKSPACE_ROOT = "/workspace"


@dataclass(frozen=True)
class MaterialDocument:
    """统一表示一份可读取的材料文件。"""

    file_name: str
    source_path: str
    source_type: str
    content: str


def load_runtime_context(path: str) -> dict[str, Any]:
    """读取运行时上下文。"""
    context_path = Path(path)
    if not context_path.exists():
        raise FileNotFoundError(f"runtime context not found: {path}")

    data = json.loads(context_path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        raise ValueError("runtime context must be a JSON object")
    return data


def inspect_materials(runtime_context: dict[str, Any]) -> dict[str, Any]:
    """检查评估沙箱本地材料是否齐备，并产出题卡与本体摘要。"""
    materials_cfg = runtime_context.get("materials") or {}
    workspace_root = _clean_path(materials_cfg.get("workspace_root")) or _DEFAULT_WORKSPACE_ROOT

    testcase_docs = discover_testcase_documents(workspace_root=workspace_root)
    ontology_docs = discover_ontology_documents(workspace_root=workspace_root)

    parsed_testcases = parse_testcases(testcase_docs)
    ontology_summary = build_ontology_summary(ontology_docs)
    question_cards = build_question_cards(parsed_testcases, ontology_summary)

    missing: list[str] = []
    if not parsed_testcases:
        missing.append("testcases")
    if not ontology_docs:
        missing.append("ontology")

    status = "ready" if not missing else "materials_incomplete"
    return {
        "status": status,
        "missing": missing,
        "workspace_root": workspace_root,
        "testcases": {
            "ready": bool(parsed_testcases),
            "count": len(parsed_testcases),
            "files": [_serialize_document_meta(item) for item in testcase_docs],
            "items": parsed_testcases,
            "question_cards": question_cards,
        },
        "ontology": {
            "ready": bool(ontology_docs),
            "files": [_serialize_document_meta(item) for item in ontology_docs],
            "documents": [
                {
                    "file_name": item.file_name,
                    "source_path": item.source_path,
                    "source_type": item.source_type,
                    "content": item.content,
                }
                for item in ontology_docs
            ],
            "dimension_weights": ontology_summary["dimension_weights"],
            "dimension_rules": ontology_summary["dimension_rules"],
        },
    }


def discover_testcase_documents(*, workspace_root: str) -> list[MaterialDocument]:
    """发现测试用例文件。"""
    return _deduplicate_documents(_read_directory_documents(workspace_root, "testcases"))


def discover_ontology_documents(*, workspace_root: str) -> list[MaterialDocument]:
    """发现 ontology 文件。"""
    return _deduplicate_documents(_read_directory_documents(workspace_root, "ontology"))


def parse_testcases(documents: Iterable[MaterialDocument]) -> list[dict[str, Any]]:
    """解析所有测试用例文件，输出统一数组。"""
    parsed: list[dict[str, Any]] = []
    for document in documents:
        try:
            payload = json.loads(document.content)
        except json.JSONDecodeError:
            continue

        items: list[dict[str, Any]] = []
        if isinstance(payload, list):
            items = [item for item in payload if isinstance(item, dict)]
        elif isinstance(payload, dict):
            nested = payload.get("test_cases")
            if isinstance(nested, list):
                items = [item for item in nested if isinstance(item, dict)]
            else:
                items = [payload]

        for item in items:
            testcase = dict(item)
            testcase["_source_file"] = document.file_name
            testcase["_source_path"] = document.source_path
            parsed.append(testcase)

    return parsed


def build_question_cards(
    testcases: list[dict[str, Any]],
    ontology_summary: dict[str, Any],
) -> list[dict[str, Any]]:
    """从测试用例构建对话窗口展示的考题卡片。"""
    scoring_hint = _build_scoring_hint(ontology_summary)
    cards: list[dict[str, Any]] = []
    for index, testcase in enumerate(testcases, start=1):
        steps = testcase.get("expected_behavior_sequence") or []
        normalized_steps: list[dict[str, Any]] = []
        required_tools: list[str] = []
        if isinstance(steps, list):
            for step in steps:
                if not isinstance(step, dict):
                    continue
                tools = step.get("required_tools")
                tool_list = [str(item).strip() for item in tools] if isinstance(tools, list) else []
                required_tools.extend([item for item in tool_list if item])
                normalized_steps.append(
                    {
                        "step": step.get("step") or len(normalized_steps) + 1,
                        "action": step.get("action") or "",
                        "criteria": step.get("criteria") or "",
                        "required_tools": tool_list,
                    }
                )

        prompt = str((testcase.get("input") or {}).get("user_request") or "").strip()
        cards.append(
            {
                "order": index,
                "testcase_id": str(testcase.get("test_case_id") or testcase.get("testcase_id") or f"TC-{index:03d}"),
                "title": str(testcase.get("scenario_name") or testcase.get("title") or f"场景 {index}"),
                "prompt": prompt,
                "context": (testcase.get("input") or {}).get("context") or {},
                "steps": normalized_steps,
                "required_tools": sorted(set(required_tools)),
                "scoring_hint": scoring_hint,
            }
        )

    return cards


def build_ontology_summary(documents: Iterable[MaterialDocument]) -> dict[str, Any]:
    """汇总 ontology 文件中的维度权重与规则。"""
    dimension_weights: dict[str, float | str] = {}
    dimension_rules: dict[str, Any] = {}

    for document in documents:
        parsed = _try_parse_json(document.content)
        if parsed is None:
            continue
        _extract_ontology_from_payload(parsed, dimension_weights, dimension_rules)

    return {
        "dimension_weights": dimension_weights,
        "dimension_rules": dimension_rules,
    }


def _extract_ontology_from_payload(
    payload: Any,
    dimension_weights: dict[str, float | str],
    dimension_rules: dict[str, Any],
) -> None:
    if isinstance(payload, dict):
        criteria = payload.get("evaluation_criteria")
        if isinstance(criteria, list):
            for item in criteria:
                if not isinstance(item, dict):
                    continue
                name = str(item.get("dimension") or item.get("name") or "").strip()
                if not name:
                    continue
                weight = item.get("weight")
                if isinstance(weight, (int, float, str)) and name not in dimension_weights:
                    dimension_weights[name] = weight
                rule_payload = {
                    key: value
                    for key, value in item.items()
                    if key not in {"dimension", "name", "weight"}
                }
                if rule_payload and name not in dimension_rules:
                    dimension_rules[name] = rule_payload

        dimensions = payload.get("dimensions")
        if isinstance(dimensions, dict):
            for key, value in dimensions.items():
                name = str(key).strip()
                if not name or name in dimension_weights:
                    continue
                if isinstance(value, (int, float, str)):
                    dimension_weights[name] = value
                elif isinstance(value, dict):
                    weight = value.get("weight")
                    if isinstance(weight, (int, float, str)):
                        dimension_weights[name] = weight
                    if name not in dimension_rules:
                        dimension_rules[name] = value
        elif isinstance(dimensions, list):
            for item in dimensions:
                if not isinstance(item, dict):
                    continue
                name = str(item.get("dimension") or item.get("name") or "").strip()
                if not name:
                    continue
                weight = item.get("weight")
                if isinstance(weight, (int, float, str)) and name not in dimension_weights:
                    dimension_weights[name] = weight
                if name not in dimension_rules:
                    dimension_rules[name] = item

        rules = payload.get("rules")
        if isinstance(rules, dict):
            for key, value in rules.items():
                name = str(key).strip()
                if name and name not in dimension_rules:
                    dimension_rules[name] = value


def _build_scoring_hint(ontology_summary: dict[str, Any]) -> str:
    weights = ontology_summary.get("dimension_weights") or {}
    if isinstance(weights, dict) and weights:
        ordered = ", ".join(f"{key}:{value}" for key, value in weights.items())
        return f"评分重点请以本体权重为准：{ordered}"

    return "评分时重点关注功能完整性、交互质量、流程合规、问题解决、工具调用正确性。"


def _read_directory_documents(root: str, material_type: str) -> list[MaterialDocument]:
    root_path = Path(root)
    if not root_path.exists() or not root_path.is_dir():
        return []

    documents: list[MaterialDocument] = []
    for file_path in root_path.rglob("*"):
        if not file_path.is_file():
            continue
        if not _matches_material(file_path, material_type):
            continue
        try:
            content = file_path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue

        documents.append(
            MaterialDocument(
                file_name=file_path.name,
                source_path=str(file_path),
                source_type="directory",
                content=content,
            )
        )
    return documents


def _matches_material(path: Path, material_type: str) -> bool:
    normalized = str(path).replace("\\", "/").lower()
    file_name = path.name.lower()
    suffix = path.suffix.lower()

    if material_type == "testcases":
        return suffix == ".json" and (
            normalized.startswith("testcases/") or
            "/testcases/" in normalized or
            file_name.startswith("testcase") or
            "testcase" in file_name or
            "test-case" in file_name or
            "evaluation-test" in file_name
        )

    if material_type == "ontology":
        if "testcase" in file_name or "test-case" in file_name or "evaluation-test-cases" in file_name:
            return False
        return suffix in {".json", ".md", ".txt"} and (
            normalized.startswith("ontology/") or
            "/ontology/" in normalized or
            "ontology" in file_name or
            "rubric" in file_name or
            "score" in file_name or
            "evaluation" in file_name
        )

    return False


def _serialize_document_meta(document: MaterialDocument) -> dict[str, Any]:
    return {
        "file_name": document.file_name,
        "source_path": document.source_path,
        "source_type": document.source_type,
    }


def _deduplicate_documents(documents: Iterable[MaterialDocument]) -> list[MaterialDocument]:
    deduplicated: dict[str, MaterialDocument] = {}
    for document in documents:
        key = f"{document.source_type}:{document.source_path}".lower()
        deduplicated[key] = document
    return list(deduplicated.values())


def _clean_path(value: Any) -> str | None:
    text = str(value or "").strip()
    return text or None


def _try_parse_json(content: str) -> Any | None:
    try:
        return json.loads(content)
    except json.JSONDecodeError:
        return None
