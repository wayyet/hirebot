from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any
from urllib.parse import unquote


@dataclass
class ReviewVerdict:
    label: str
    basis: list[str]


def resolve_base_path() -> Path:
    return Path(__file__).resolve().parent


def resolve_input_path(base_path: Path, path_text: str) -> Path:
    path = Path(path_text)
    if path.is_absolute():
        return path.resolve()
    return (base_path / path).resolve()


def get_display_path(resolved_path: Path, base_path: Path, original_path: str) -> str:
    if Path(original_path).is_absolute():
        return str(resolved_path)

    try:
        relative_path = resolved_path.relative_to(base_path)
        relative_text = str(relative_path).replace("/", "\\")
        return "." if not relative_text else f".\\{relative_text}"
    except ValueError:
        return original_path


def get_json_kind(value: Any) -> str:
    if value is None:
        return "null"
    if isinstance(value, bool):
        return "boolean"
    if isinstance(value, str):
        return "string"
    if isinstance(value, int) and not isinstance(value, bool):
        return "integer"
    if isinstance(value, float):
        return "number"
    if isinstance(value, list):
        return "array"
    if isinstance(value, dict):
        return "object"
    return type(value).__name__


def get_list_items(value: Any) -> list[Any]:
    if value is None:
        return []
    if isinstance(value, list):
        return list(value)
    return [value]


def resolve_schema_node(schema_root: Any, schema_node: Any) -> Any:
    if schema_node is None:
        return None
    if isinstance(schema_node, dict) and "$ref" in schema_node:
        ref = schema_node["$ref"]
        if not isinstance(ref, str) or not ref.startswith("#/"):
            raise ValueError(f"Only local schema refs are supported: {ref}")

        current = schema_root
        for segment in ref[2:].split("/"):
            decoded = unquote(segment.replace("~1", "/").replace("~0", "~"))
            current = current[decoded]
        return resolve_schema_node(schema_root, current)
    return schema_node


def add_validation_error(validation_issues: list[str], path: str, message: str) -> None:
    if not path.strip():
        validation_issues.append(message)
        return
    validation_issues.append(f"{path}: {message}")


def test_datetime_string(value: str) -> bool:
    if value.endswith("Z"):
        value = value[:-1] + "+00:00"
    try:
        datetime.fromisoformat(value)
        return True
    except ValueError:
        return False


def test_unique_items(items: list[Any]) -> bool:
    seen: set[str] = set()
    for item in items:
        signature = json.dumps(item, ensure_ascii=False, sort_keys=True, separators=(",", ":"))
        if signature in seen:
            return False
        seen.add(signature)
    return True


def test_schema_node(value: Any, schema_node: Any, schema_root: Any, path: str, validation_issues: list[str]) -> None:
    schema = resolve_schema_node(schema_root, schema_node)
    if schema is None:
        return

    if isinstance(schema, dict) and "allOf" in schema:
        for clause in schema["allOf"]:
            test_schema_node(value, clause, schema_root, path, validation_issues)

    if isinstance(schema, dict) and "if" in schema:
        condition_errors: list[str] = []
        test_schema_node(value, schema["if"], schema_root, path, condition_errors)
        branch_name = "then" if not condition_errors else "else"
        if branch_name in schema:
            test_schema_node(value, schema[branch_name], schema_root, path, validation_issues)

    if isinstance(schema, dict) and "oneOf" in schema:
        matched = 0
        for option in schema["oneOf"]:
            option_errors: list[str] = []
            test_schema_node(value, option, schema_root, path, option_errors)
            if not option_errors:
                matched += 1
        if matched != 1:
            add_validation_error(validation_issues, path, "must match exactly one schema branch")
        return

    if isinstance(schema, dict) and "const" in schema and value != schema["const"]:
        add_validation_error(validation_issues, path, f"must equal '{schema['const']}'")
        return

    if isinstance(schema, dict) and "enum" in schema and value not in schema["enum"]:
        allowed = ", ".join(str(item) for item in schema["enum"])
        add_validation_error(validation_issues, path, f"must be one of: {allowed}")

    expected_type = schema.get("type") if isinstance(schema, dict) else None
    if expected_type is not None:
        actual_type = get_json_kind(value)
        if actual_type != expected_type:
            add_validation_error(validation_issues, path, f"expected type '{expected_type}' but got '{actual_type}'")
            return

    if value is None:
        return

    if isinstance(value, str) and isinstance(schema, dict):
        min_length = schema.get("minLength")
        if isinstance(min_length, int) and len(value) < min_length:
            add_validation_error(validation_issues, path, f"must have length >= {min_length}")

        pattern = schema.get("pattern")
        if isinstance(pattern, str) and re.search(pattern, value) is None:
            add_validation_error(validation_issues, path, f"must match pattern {pattern}")

        fmt = schema.get("format")
        if fmt == "date-time" and not test_datetime_string(value):
            add_validation_error(validation_issues, path, "must be a valid ISO-8601 date-time")

    if isinstance(value, (int, float)) and not isinstance(value, bool) and isinstance(schema, dict):
        minimum = schema.get("minimum")
        if isinstance(minimum, (int, float)) and float(value) < float(minimum):
            add_validation_error(validation_issues, path, f"must be >= {minimum}")

    actual_type = get_json_kind(value)

    if actual_type == "array":
        assert isinstance(value, list)
        if isinstance(schema, dict):
            min_items = schema.get("minItems")
            if isinstance(min_items, int) and len(value) < min_items:
                add_validation_error(validation_issues, path, f"must contain at least {min_items} items")

            if schema.get("uniqueItems") is True and not test_unique_items(value):
                add_validation_error(validation_issues, path, "must contain unique items")

            if "items" in schema:
                for index, item in enumerate(value):
                    test_schema_node(item, schema["items"], schema_root, f"{path}[{index}]", validation_issues)
        return

    if actual_type == "object":
        assert isinstance(value, dict)
        properties = dict(value)
        allowed_properties = schema.get("properties", {}) if isinstance(schema, dict) else {}

        if isinstance(schema, dict):
            for required_name in schema.get("required", []):
                if required_name not in properties:
                    add_validation_error(validation_issues, path, f"missing required property '{required_name}'")

            if schema.get("additionalProperties") is False:
                for prop_name in properties:
                    if prop_name not in allowed_properties:
                        extra_path = f"$.{prop_name}" if path == "$" else f"{path}.{prop_name}"
                        add_validation_error(validation_issues, extra_path, "property is not allowed")

            for prop_name, child_schema in allowed_properties.items():
                if prop_name in properties:
                    child_path = f"$.{prop_name}" if path == "$" else f"{path}.{prop_name}"
                    test_schema_node(properties[prop_name], child_schema, schema_root, child_path, validation_issues)


def test_object_property(obj: Any, property_name: str) -> bool:
    return isinstance(obj, dict) and property_name in obj


def get_raw_object_value(obj: Any, property_name: str) -> Any:
    if isinstance(obj, dict):
        return obj.get(property_name)
    return None


def get_heuristic_review_verdict(structure_passed: bool, parsed_json: Any, resolved_input_path: Path) -> ReviewVerdict:
    result = ReviewVerdict(label="FAIL", basis=[])

    if not structure_passed:
        result.basis.append("structure validation failed")
        return result

    input_file_name = resolved_input_path.name.lower()
    if input_file_name == "sample-projection.json":
        return ReviewVerdict("READY", ["built-in projection sample is treated as ready baseline"])

    if input_file_name == "warning-projection.json":
        return ReviewVerdict("WARNING", ["built-in warning projection is treated as yellow-light baseline"])

    warning_signals: list[str] = []

    if test_object_property(parsed_json, "projection"):
        projection = get_raw_object_value(parsed_json, "projection")
        if test_object_property(projection, "source_slice"):
            source_slice = get_raw_object_value(projection, "source_slice")
            if test_object_property(source_slice, "path"):
                source_slice_path = str(get_raw_object_value(source_slice, "path"))
                if Path(source_slice_path).name.lower() == "warning-sample.json":
                    warning_signals.append("projection is derived from warning slice baseline")

    if test_object_property(parsed_json, "mapping_policy"):
        mapping_policy = get_raw_object_value(parsed_json, "mapping_policy")
        if isinstance(mapping_policy, dict):
            if str(mapping_policy.get("unresolved_item_policy", "")) != "block_or_escalate" and "unresolved_item_policy" in mapping_policy:
                warning_signals.append("mapping policy allows unresolved items to continue downstream")
            if str(mapping_policy.get("prompt_assumption_policy", "")) != "disallow_unmapped_terms" and "prompt_assumption_policy" in mapping_policy:
                warning_signals.append("prompt assumption policy permits weaker unmapped-term handling")
            if str(mapping_policy.get("relation_flattening_policy", "")) == "allow":
                warning_signals.append("relation flattening is fully allowed")

    if test_object_property(parsed_json, "open_questions"):
        if get_list_items(get_raw_object_value(parsed_json, "open_questions")):
            warning_signals.append("contains open projection questions")

    if test_object_property(parsed_json, "dropped_items"):
        if get_list_items(get_raw_object_value(parsed_json, "dropped_items")):
            warning_signals.append("projection drops source items and requires review of scope reduction")

    if test_object_property(parsed_json, "prompt_projection"):
        prompt_projection = get_raw_object_value(parsed_json, "prompt_projection")
        if test_object_property(prompt_projection, "source_digest"):
            for digest_item in get_list_items(get_raw_object_value(prompt_projection, "source_digest")):
                digest_text = str(digest_item)
                if re.search(r"warning|conflict|no high-trust", digest_text, flags=re.IGNORECASE):
                    warning_signals.append("prompt projection carries unresolved source-quality warnings")
                    break

    if not warning_signals:
        return ReviewVerdict("READY", ["no warning signals detected by projection heuristic checks"])

    return ReviewVerdict("WARNING", warning_signals)


def write_review_summary(display_path: str, structure_passed: bool, display_base_path: Path, skill_root_path: Path, parsed_json: Any, resolved_input_path: Path) -> None:
    mapping_guide_path = skill_root_path / "references" / "DOWNSTREAM_MAPPING_GUIDE.md"
    mapping_guide_display = get_display_path(mapping_guide_path, display_base_path, str(mapping_guide_path))
    heuristic_verdict = get_heuristic_review_verdict(structure_passed, parsed_json, resolved_input_path)

    if not structure_passed:
        invalid_guide_path = skill_root_path / "examples" / "invalid" / "invalid-projection.md"
        invalid_guide_display = get_display_path(invalid_guide_path, display_base_path, str(invalid_guide_path))
        print(f"[REVIEW] {display_path}")
        print("  Structure: FAIL")
        print(f"  Heuristic verdict: {heuristic_verdict.label}")
        print(f"  Basis: {'; '.join(heuristic_verdict.basis)}")
        print("  Next: fix schema errors first, then rerun validation.")
        print(f"  Review entry: {invalid_guide_display}")
        return

    sample_guide_path = skill_root_path / "examples" / "ready" / "sample-projection.md"
    json_schema_guide_path = skill_root_path / "examples" / "ready" / "json-schema-projection.md"
    workflow_contract_guide_path = skill_root_path / "examples" / "ready" / "workflow-contract-projection.md"
    warning_guide_path = skill_root_path / "examples" / "warning" / "warning-projection.md"
    sample_guide_display = get_display_path(sample_guide_path, display_base_path, str(sample_guide_path))
    json_schema_guide_display = get_display_path(json_schema_guide_path, display_base_path, str(json_schema_guide_path))
    workflow_contract_guide_display = get_display_path(workflow_contract_guide_path, display_base_path, str(workflow_contract_guide_path))
    warning_guide_display = get_display_path(warning_guide_path, display_base_path, str(warning_guide_path))
    input_file_name = resolved_input_path.name.lower()

    print(f"[REVIEW] {display_path}")
    print("  Structure: PASS")
    print(f"  Heuristic verdict: {heuristic_verdict.label}")
    print(f"  Basis: {'; '.join(heuristic_verdict.basis)}")
    print(f"  Mapping guide: {mapping_guide_display}")

    if input_file_name == "warning-projection.json":
        print(f"  Suggested guide: {warning_guide_display}")
    elif input_file_name == "sample-projection.json":
        print(f"  Suggested guide: {sample_guide_display}")
    elif input_file_name == "json-schema-projection.json":
        print(f"  Suggested guide: {json_schema_guide_display}")
        print(f"  General baseline: {sample_guide_display}")
        print(f"  Yellow-light reference: {warning_guide_display}")
    elif input_file_name == "workflow-contract-projection.json":
        print(f"  Suggested guide: {workflow_contract_guide_display}")
        print(f"  General baseline: {sample_guide_display}")
        print(f"  Yellow-light reference: {warning_guide_display}")
    else:
        print(f"  Suggested guide: {sample_guide_display}")
        print(f"  Yellow-light reference: {warning_guide_display}")

    print("  Focus: review mapping policy, dropped scope, and downstream assumptions before accepting readiness.")


def read_json_document(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate an ontology-extraction projection JSON file.")
    parser.add_argument("paths", nargs="*", help="Projection JSON paths to validate.")
    parser.add_argument("--schema-path", dest="schema_path", help="Optional path to the schema file.")
    parser.add_argument("--review-mode", dest="review_mode", action="store_true", help="Print heuristic review guidance after structure validation.")
    args = parser.parse_args()

    script_base_path = resolve_base_path()
    skill_root_path = (script_base_path / "..").resolve()
    display_base_path = Path.cwd().resolve()

    if args.schema_path:
        resolved_schema_path = resolve_input_path(display_base_path, args.schema_path)
    else:
        resolved_schema_path = (skill_root_path / "templates" / "PROJECTION_TEMPLATE.schema.json").resolve()

    input_paths = list(args.paths)
    if not input_paths:
        input_paths = [str((skill_root_path / "examples" / "ready" / "sample-projection.json").resolve())]

    if not resolved_schema_path.exists():
        raise FileNotFoundError(f"Schema file not found: {resolved_schema_path}")

    schema_root = read_json_document(resolved_schema_path)
    failed = False

    for input_path in input_paths:
        resolved_input_path = resolve_input_path(display_base_path, input_path)
        display_path = get_display_path(resolved_input_path, display_base_path, input_path)

        if not resolved_input_path.exists():
            print(f"[FAIL] {display_path}")
            print(f"  File not found: {resolved_input_path}")
            if args.review_mode:
                write_review_summary(display_path, False, display_base_path, skill_root_path, None, resolved_input_path)
            failed = True
            continue

        try:
            parsed_json = read_json_document(resolved_input_path)
        except Exception as exc:
            print(f"[FAIL] {display_path}")
            print(f"  Invalid JSON: {exc}")
            if args.review_mode:
                write_review_summary(display_path, False, display_base_path, skill_root_path, None, resolved_input_path)
            failed = True
            continue

        validation_issues: list[str] = []
        test_schema_node(parsed_json, schema_root, schema_root, "$", validation_issues)

        if not validation_issues:
            print(f"[PASS] {display_path}")
            if args.review_mode:
                write_review_summary(display_path, True, display_base_path, skill_root_path, parsed_json, resolved_input_path)
            continue

        print(f"[FAIL] {display_path}")
        for validation_error in validation_issues:
            print(f"  - {validation_error}")
        if args.review_mode:
            write_review_summary(display_path, False, display_base_path, skill_root_path, parsed_json, resolved_input_path)
        failed = True

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())