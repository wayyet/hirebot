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


def get_string_set(items: Any, property_name: str) -> set[str]:
    result: set[str] = set()
    for item in get_list_items(items):
        if not isinstance(item, dict):
            continue
        value = item.get(property_name)
        if isinstance(value, str) and value:
            result.add(value)
    return result


def validate_known_ids(parsed_json: Any, validation_issues: list[str]) -> None:
    if not isinstance(parsed_json, dict):
        return

    source_ids = get_string_set(parsed_json.get("sources"), "id")
    concept_ids = get_string_set(parsed_json.get("concepts"), "id")
    relation_ids = get_string_set(parsed_json.get("relations"), "id")
    constraint_ids = get_string_set(parsed_json.get("constraints"), "id")

    def require_known(values: Any, known: set[str], path: str, kind: str) -> None:
        for index, value in enumerate(get_list_items(values)):
            if isinstance(value, str) and value not in known:
                add_validation_error(validation_issues, f"{path}[{index}]", f"references unknown {kind} '{value}'")

    for index, conflict in enumerate(get_list_items(parsed_json.get("conflicts"))):
        if isinstance(conflict, dict):
            require_known(conflict.get("source_ids"), source_ids, f"$.conflicts[{index}].source_ids", "source id")

    for index, include in enumerate(get_list_items(get_raw_object_value(parsed_json.get("scope"), "include"))):
        if not isinstance(include, dict):
            continue
        item_type = include.get("type")
        item_id = include.get("id")
        if not isinstance(item_id, str):
            continue
        if item_type == "concept" and item_id not in concept_ids:
            add_validation_error(validation_issues, f"$.scope.include[{index}].id", f"references unknown concept id '{item_id}'")
        if item_type == "relation" and item_id not in relation_ids:
            add_validation_error(validation_issues, f"$.scope.include[{index}].id", f"references unknown relation id '{item_id}'")
        if item_type == "constraint" and item_id not in constraint_ids:
            add_validation_error(validation_issues, f"$.scope.include[{index}].id", f"references unknown constraint id '{item_id}'")

    for index, concept in enumerate(get_list_items(parsed_json.get("concepts"))):
        if not isinstance(concept, dict):
            continue
        parent_id = concept.get("parent_concept_id")
        if isinstance(parent_id, str) and parent_id not in concept_ids:
            add_validation_error(validation_issues, f"$.concepts[{index}].parent_concept_id", f"references unknown concept id '{parent_id}'")
        require_known(concept.get("source_ids"), source_ids, f"$.concepts[{index}].source_ids", "source id")

    for index, relation in enumerate(get_list_items(parsed_json.get("relations"))):
        if not isinstance(relation, dict):
            continue
        subject_id = relation.get("subject_concept_id")
        object_id = relation.get("object_concept_id")
        if isinstance(subject_id, str) and subject_id not in concept_ids:
            add_validation_error(validation_issues, f"$.relations[{index}].subject_concept_id", f"references unknown concept id '{subject_id}'")
        if isinstance(object_id, str) and object_id not in concept_ids:
            add_validation_error(validation_issues, f"$.relations[{index}].object_concept_id", f"references unknown concept id '{object_id}'")
        require_known(relation.get("source_ids"), source_ids, f"$.relations[{index}].source_ids", "source id")

    for index, constraint in enumerate(get_list_items(parsed_json.get("constraints"))):
        if not isinstance(constraint, dict):
            continue
        applies_to = constraint.get("applies_to")
        require_known(get_raw_object_value(applies_to, "concept_ids"), concept_ids, f"$.constraints[{index}].applies_to.concept_ids", "concept id")
        require_known(get_raw_object_value(applies_to, "relation_ids"), relation_ids, f"$.constraints[{index}].applies_to.relation_ids", "relation id")
        require_known(constraint.get("source_ids"), source_ids, f"$.constraints[{index}].source_ids", "source id")

    for index, mapping in enumerate(get_list_items(parsed_json.get("term_mappings"))):
        if not isinstance(mapping, dict):
            continue
        require_known(mapping.get("candidate_concept_ids"), concept_ids, f"$.term_mappings[{index}].candidate_concept_ids", "concept id")
        selected_id = mapping.get("selected_concept_id")
        if isinstance(selected_id, str) and selected_id not in concept_ids:
            add_validation_error(validation_issues, f"$.term_mappings[{index}].selected_concept_id", f"references unknown concept id '{selected_id}'")

    todo_context = get_raw_object_value(parsed_json.get("meta"), "todo_context")
    if isinstance(todo_context, dict):
        todo_ids = set(value for value in get_list_items(todo_context.get("todo_ids")) if isinstance(value, str))
        result_ids = get_string_set(todo_context.get("todos"), "id")
        for todo_id in sorted(todo_ids - result_ids):
            add_validation_error(validation_issues, "$.meta.todo_context.todos", f"missing todo '{todo_id}' listed in todo_ids")
        for todo_id in sorted(result_ids - todo_ids):
            add_validation_error(validation_issues, "$.meta.todo_context.todo_ids", f"missing todo id '{todo_id}' listed in todos")


def get_heuristic_review_verdict(structure_passed: bool, parsed_json: Any, resolved_input_path: Path) -> ReviewVerdict:
    result = ReviewVerdict(label="FAIL", basis=[])

    if not structure_passed:
        result.basis.append("structure validation failed")
        return result

    input_file_name = resolved_input_path.name.lower()
    if input_file_name == "sample.json":
        return ReviewVerdict("READY", ["built-in reference sample is treated as ready baseline"])

    if input_file_name == "warning-sample.json":
        return ReviewVerdict("WARNING", ["built-in warning sample is treated as yellow-light baseline"])

    warning_signals: list[str] = []

    if test_object_property(parsed_json, "sources"):
        high_trust_count = 0
        low_trust_count = 0
        for source in get_list_items(get_raw_object_value(parsed_json, "sources")):
            if not isinstance(source, dict):
                continue
            trust_level = str(source.get("trust_level", ""))
            if trust_level == "high":
                high_trust_count += 1
            if trust_level == "low":
                low_trust_count += 1
        if high_trust_count == 0:
            warning_signals.append("no high-trust source found")
        if low_trust_count > 0:
            warning_signals.append("contains low-trust sources")

    if test_object_property(parsed_json, "conflicts"):
        for conflict in get_list_items(get_raw_object_value(parsed_json, "conflicts")):
            if isinstance(conflict, dict) and str(conflict.get("status", "")) in {"open", "deferred"}:
                warning_signals.append("contains unresolved conflicts")
                break

    if test_object_property(parsed_json, "ambiguities"):
        for ambiguity in get_list_items(get_raw_object_value(parsed_json, "ambiguities")):
            if isinstance(ambiguity, dict) and str(ambiguity.get("status", "")) in {"open", "deferred"}:
                warning_signals.append("contains unresolved ambiguities")
                break

    if test_object_property(parsed_json, "uncertainties"):
        uncertainties = get_list_items(get_raw_object_value(parsed_json, "uncertainties"))
        if uncertainties:
            warning_signals.append("contains explicit uncertainties")

    if not warning_signals:
        return ReviewVerdict("READY", ["no warning signals detected by heuristic checks"])

    return ReviewVerdict("WARNING", warning_signals)


def write_review_summary(display_path: str, structure_passed: bool, display_base_path: Path, skill_root_path: Path, parsed_json: Any, resolved_input_path: Path) -> None:
    review_checklist_path = skill_root_path / "references" / "REVIEW_CHECKLIST.md"
    review_checklist_display = get_display_path(review_checklist_path, display_base_path, str(review_checklist_path))
    heuristic_verdict = get_heuristic_review_verdict(structure_passed, parsed_json, resolved_input_path)

    if not structure_passed:
        invalid_guide_path = skill_root_path / "examples" / "invalid" / "invalid-sample.md"
        invalid_guide_display = get_display_path(invalid_guide_path, display_base_path, str(invalid_guide_path))
        print(f"[REVIEW] {display_path}")
        print("  Structure: FAIL")
        print(f"  Heuristic verdict: {heuristic_verdict.label}")
        print(f"  Basis: {'; '.join(heuristic_verdict.basis)}")
        print("  Next: fix schema errors first, then rerun validation.")
        print(f"  Review entry: {invalid_guide_display}")
        return

    sample_guide_path = skill_root_path / "examples" / "ready" / "sample.md"
    warning_guide_path = skill_root_path / "examples" / "warning" / "warning-sample.md"
    sample_guide_display = get_display_path(sample_guide_path, display_base_path, str(sample_guide_path))
    warning_guide_display = get_display_path(warning_guide_path, display_base_path, str(warning_guide_path))
    input_file_name = resolved_input_path.name.lower()

    print(f"[REVIEW] {display_path}")
    print("  Structure: PASS")
    print(f"  Heuristic verdict: {heuristic_verdict.label}")
    print(f"  Basis: {'; '.join(heuristic_verdict.basis)}")
    print(f"  Review entry: {review_checklist_display}")

    if input_file_name == "warning-sample.json":
        print(f"  Suggested guide: {warning_guide_display}")
    elif input_file_name == "sample.json":
        print(f"  Suggested guide: {sample_guide_display}")
    else:
        print(f"  Suggested guide: {sample_guide_display}")
        print(f"  Yellow-light reference: {warning_guide_display}")

    print("  Focus: review source quality, concept boundaries, and relation precision before deciding readiness.")


def read_json_document(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def main() -> int:
    parser = argparse.ArgumentParser(description="Validate an ontology-extraction slice JSON file.")
    parser.add_argument("paths", nargs="*", help="Slice JSON paths to validate.")
    parser.add_argument("--schema-path", dest="schema_path", help="Optional path to the schema file.")
    parser.add_argument("--review-mode", dest="review_mode", action="store_true", help="Print heuristic review guidance after structure validation.")
    args = parser.parse_args()

    script_base_path = resolve_base_path()
    skill_root_path = (script_base_path / "..").resolve()
    display_base_path = Path.cwd().resolve()

    if args.schema_path:
        resolved_schema_path = resolve_input_path(display_base_path, args.schema_path)
    else:
        resolved_schema_path = (skill_root_path / "templates" / "TEMPLATE.schema.json").resolve()

    input_paths = list(args.paths)
    if not input_paths:
        input_paths = [str((skill_root_path / "examples" / "ready" / "sample.json").resolve())]

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
            validate_known_ids(parsed_json, validation_issues)

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