from __future__ import annotations

import argparse
import json
import os
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


VIEW_SPECS = [
    ("domain-model", "domain_model_projection", "domain model"),
    ("json-schema", "json_schema_projection", "JSON schema"),
    ("prompt-constraint", "prompt_constraint_projection", "prompt constraints"),
    ("workflow-contract", "workflow_contract_projection", "workflow contract"),
]

VIEW_BY_PROJECTION_TYPE = {projection_type: view for view, projection_type, _ in VIEW_SPECS}


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def write_text(path: Path, value: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(value, encoding="utf-8")


def as_list(value: Any) -> list[Any]:
    if value is None:
        return []
    if isinstance(value, list):
        return value
    return [value]


def as_object(value: Any) -> dict[str, Any]:
    return value if isinstance(value, dict) else {}


def slugify(value: str) -> str:
    normalized = re.sub(r"[^a-z0-9_]+", "-", value.lower()).strip("-")
    return normalized or "ontology-topic"


def strip_projection_suffix(path: Path) -> str:
    name = path.name
    if name.endswith(".projection.json"):
        name = name[: -len(".projection.json")]
    elif name.endswith("-projection.json"):
        name = name[: -len("-projection.json")]

    for view, _, _ in VIEW_SPECS:
        suffix = f".{view}"
        if name.endswith(suffix):
            return name[: -len(suffix)]

    return name


def find_projection_files(source_dir: Path) -> list[Path]:
    candidates = {*source_dir.glob("*.projection.json"), *source_dir.glob("*-projection.json")}
    return sorted(candidates)


def default_mapping_policy() -> dict[str, Any]:
    return {
        "preserve_source_trace": True,
        "preserve_constraints": True,
        "relation_flattening_policy": "disallow_by_default",
        "unresolved_item_policy": "block_or_escalate",
        "dropped_item_policy": "record_with_reason",
        "prompt_assumption_policy": "disallow_unmapped_terms",
    }


def default_prompt_projection(source_path: str) -> dict[str, Any]:
    return {
        "required": [],
        "constraints": [],
        "forbidden_assumptions": ["Do not invent ontology terms that are absent from the selected projection."],
        "source_digest": [f"Derived from source projection: {source_path}"],
    }


def resolve_possible_path(workspace_root: Path, source_file: Path, path_text: str) -> Path:
    raw_path = Path(path_text)
    if raw_path.is_absolute():
        return raw_path

    workspace_candidate = workspace_root / raw_path
    if workspace_candidate.exists() or path_text.replace("\\", "/").startswith("ontology/"):
        return workspace_candidate

    source_candidate = source_file.parent / raw_path
    if source_candidate.exists():
        return source_candidate

    return workspace_candidate


def relative_path(from_dir: Path, target: Path) -> str:
    return Path(os.path.relpath(target, from_dir)).as_posix()


def normalize_source_slice(source_slice: Any, workspace_root: Path, skill_dir: Path, source_file: Path) -> dict[str, Any]:
    normalized = dict(as_object(source_slice))
    path_text = str(normalized.get("path") or normalized.get("slice_path") or "").strip()

    if path_text:
        resolved = resolve_possible_path(workspace_root, source_file, path_text)
        normalized["path"] = relative_path(skill_dir, resolved)

    if "topic" not in normalized and "slice_topic" in normalized:
        normalized["topic"] = normalized["slice_topic"]

    return normalized


def extract_projection_body(document: dict[str, Any]) -> tuple[dict[str, Any], bool]:
    projection = as_object(document.get("projection"))
    if projection:
        return projection, True

    return document, False


def is_valid_source(document: Any) -> bool:
    if not isinstance(document, dict):
        return False

    projection, is_nested = extract_projection_body(document)
    if is_nested:
        return all(key in projection for key in ("projection_type", "source_slice", "intended_consumers")) and "concept_mappings" in document

    return all(key in document for key in ("projection_type", "source_slice", "intended_consumers", "concept_mappings"))


def normalize_source(source_file: Path, workspace_root: Path, skill_dir: Path) -> dict[str, Any] | None:
    document = read_json(source_file)
    if not is_valid_source(document):
        return None

    projection, _ = extract_projection_body(document)
    projection_type = str(projection.get("projection_type", "")).strip()
    source_view = VIEW_BY_PROJECTION_TYPE.get(projection_type, "workflow-contract")
    source_path = relative_path(skill_dir, source_file)
    open_questions = as_list(document.get("open_questions"))

    domain_source = strip_projection_suffix(source_file)
    source_slice = as_object(projection.get("source_slice"))
    slice_topic = str(source_slice.get("topic") or source_slice.get("slice_topic") or "").strip()
    domain_slug = slugify(domain_source or slice_topic)

    return {
        "source_file": source_file,
        "source_path": source_path,
        "source_view": source_view,
        "source_projection_type": projection_type,
        "domain_slug": domain_slug,
        "status": "WARNING" if open_questions else "READY",
        "source_slice": normalize_source_slice(projection.get("source_slice"), workspace_root, skill_dir, source_file),
        "intended_consumers": as_list(projection.get("intended_consumers")),
        "mapping_policy": as_object(document.get("mapping_policy")) or default_mapping_policy(),
        "concept_mappings": as_list(document.get("concept_mappings")),
        "relation_mappings": as_list(document.get("relation_mappings")),
        "constraint_mappings": as_list(document.get("constraint_mappings")),
        "prompt_projection": as_object(document.get("prompt_projection")),
        "delivery_artifacts": as_list(document.get("delivery_artifacts")),
        "dropped_items": as_list(document.get("dropped_items")),
        "open_questions": open_questions,
    }


def select_source_for_view(sources: list[dict[str, Any]], view: str) -> dict[str, Any]:
    for source in sources:
        if source["source_view"] == view:
            return source

    for source in sources:
        if source["source_view"] == "workflow-contract":
            return source

    return sources[0]


def build_delivery_artifacts(
    source: dict[str, Any],
    domain_slug: str,
    view: str,
    output_relative_path: str,
) -> list[Any]:
    source_artifacts = source["delivery_artifacts"]
    if source_artifacts:
        return source_artifacts

    return [
        {
            "artifact_name": f"{domain_slug}.{view}.projection",
            "artifact_type": "projection_contract_view",
            "path": output_relative_path,
            "status": source["status"].lower(),
        }
    ]


def build_projection_document(
    source: dict[str, Any],
    skill_slug: str,
    domain_slug: str,
    view: str,
    projection_type: str,
    output_relative_path: str,
) -> dict[str, Any]:
    prompt_projection = source["prompt_projection"] or default_prompt_projection(source["source_path"])
    intended_consumers = list(dict.fromkeys([skill_slug, *[str(item) for item in source["intended_consumers"]]]))

    return {
        "projection_id": f"{domain_slug}-{view}-v1",
        "projection_type": projection_type,
        "target_view": view,
        "target_name": f"{skill_slug}:{domain_slug}:{view}",
        "target_format": "consumer_projection_contract",
        "target_runtime": "hirebot_skill_runtime",
        "source_slice": source["source_slice"],
        "intended_consumers": intended_consumers,
        "status": source["status"],
        "mapping_policy": source["mapping_policy"],
        "concept_mappings": source["concept_mappings"],
        "relation_mappings": source["relation_mappings"],
        "constraint_mappings": source["constraint_mappings"],
        "prompt_projection": prompt_projection,
        "delivery_artifacts": build_delivery_artifacts(source, domain_slug, view, output_relative_path),
        "dropped_items": source["dropped_items"],
        "open_questions": source["open_questions"],
        "meta": {
            "generated_at": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
            "generated_by": "skill-generation",
            "source_projection_paths": [source["source_path"]],
            "source_projection_type": source["source_projection_type"],
            "notes": "Materialized deterministically from ontology/projections into the local consumer contract layout.",
        },
    }


def build_readme(skill_slug: str, topics: list[dict[str, Any]]) -> str:
    lines = [
        "# Ontology Extraction Projection Contracts",
        "",
        f"Consumer skill: `{skill_slug}`",
        "",
        "This namespace contains local consumer projection contracts materialized from `ontology/projections/<skill-slug>/`.",
        "",
        "## Topics",
        "",
    ]

    for topic in topics:
        lines.append(f"- `{topic['domain_slug']}`: default view `{topic['default_target_view']}`")

    lines.extend(["", "Read `contract-index.json` first, then open the selected topic view file."])
    return "\n".join(lines) + "\n"


def validate_output(output_dir: Path, index: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    topics = index.get("topics")
    if not isinstance(topics, list) or not topics:
        errors.append("contract-index.json must contain at least one topic.")
        return errors

    for topic in topics:
        domain_slug = topic.get("domain_slug")
        views = topic.get("views")
        if not isinstance(domain_slug, str) or not domain_slug:
            errors.append("Each topic must contain domain_slug.")
            continue
        if not isinstance(views, list) or len(views) != len(VIEW_SPECS):
            errors.append(f"Topic {domain_slug} must contain exactly {len(VIEW_SPECS)} views.")
            continue

        expected_views = {view for view, _, _ in VIEW_SPECS}
        actual_views = {str(view.get("target_view")) for view in views if isinstance(view, dict)}
        if actual_views != expected_views:
            errors.append(f"Topic {domain_slug} must contain views: {', '.join(sorted(expected_views))}.")

        for view in views:
            path_text = str(view.get("path") or "").strip()
            projection_path = output_dir / path_text
            if not projection_path.exists():
                errors.append(f"Missing projection file: {path_text}")
                continue

            document = read_json(projection_path)
            required = [
                "projection_type",
                "source_slice",
                "intended_consumers",
                "concept_mappings",
                "mapping_policy",
                "prompt_projection",
                "delivery_artifacts",
                "dropped_items",
                "open_questions",
            ]
            missing = [name for name in required if name not in document]
            if missing:
                errors.append(f"{path_text} is missing fields: {', '.join(missing)}")

    return errors


def materialize(args: argparse.Namespace) -> int:
    workspace_root = Path(args.workspace_root).resolve()
    skill_slug = args.skill_slug
    skill_name = args.skill_name or skill_slug
    skill_dir = workspace_root / "skills" / skill_slug
    source_dir = Path(args.source_dir).resolve() if args.source_dir else workspace_root / "ontology" / "projections" / skill_slug
    output_dir = Path(args.output_dir).resolve() if args.output_dir else skill_dir / "contracts" / "projections" / "ontology_extraction"

    if not source_dir.exists():
        print(json.dumps({"status": "skipped", "reason": "source_dir_not_found", "source_dir": str(source_dir)}, ensure_ascii=False))
        return 2

    sources: list[dict[str, Any]] = []
    invalid_sources: list[str] = []
    for source_file in find_projection_files(source_dir):
        try:
            source = normalize_source(source_file, workspace_root, skill_dir)
        except Exception as exc:
            invalid_sources.append(f"{source_file.name}: {exc}")
            continue
        if source is None:
            invalid_sources.append(f"{source_file.name}: invalid_or_stub_projection")
            continue
        sources.append(source)

    if not sources:
        print(
            json.dumps(
                {
                    "status": "skipped",
                    "reason": "no_valid_projection_sources",
                    "source_dir": str(source_dir),
                    "invalid_sources": invalid_sources,
                },
                ensure_ascii=False,
            )
        )
        return 3

    grouped: dict[str, list[dict[str, Any]]] = {}
    for source in sources:
        grouped.setdefault(source["domain_slug"], []).append(source)

    topics: list[dict[str, Any]] = []
    for domain_slug, domain_sources in sorted(grouped.items()):
        views: list[dict[str, Any]] = []
        for view, projection_type, _ in VIEW_SPECS:
            source = select_source_for_view(domain_sources, view)
            file_name = f"{domain_slug}.{view}.projection.json"
            output_relative_path = f"{domain_slug}/{file_name}"
            document = build_projection_document(source, skill_slug, domain_slug, view, projection_type, output_relative_path)
            write_json(output_dir / output_relative_path, document)
            views.append(
                {
                    "target_view": view,
                    "projection_type": projection_type,
                    "status": source["status"],
                    "path": output_relative_path,
                }
            )

        topics.append(
            {
                "domain_slug": domain_slug,
                "intent_keywords": [domain_slug, skill_name],
                "default_target_view": "workflow-contract",
                "views": views,
            }
        )

    index = {
        "producer_skill": "ontology_extraction",
        "consumer_skill": skill_slug,
        "default_selection_policy": {
            "prefer_ready_only": True,
            "block_on_open_questions": True,
        },
        "topics": topics,
    }
    write_json(output_dir / "contract-index.json", index)
    write_text(output_dir / "README.md", build_readme(skill_slug, topics))

    errors = validate_output(output_dir, index)
    if errors:
        print(json.dumps({"status": "failed", "errors": errors}, ensure_ascii=False, indent=2))
        return 4

    generated_paths = [str(output_dir / "contract-index.json"), str(output_dir / "README.md")]
    for topic in topics:
        for view in topic["views"]:
            generated_paths.append(str(output_dir / view["path"]))

    print(
        json.dumps(
            {
                "status": "done",
                "skill_slug": skill_slug,
                "source_dir": str(source_dir),
                "output_dir": str(output_dir),
                "generated_paths": generated_paths,
                "invalid_sources": invalid_sources,
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Materialize local consumer projection contracts for a generated skill.")
    parser.add_argument("--workspace-root", required=True, help="Workspace root that contains ontology/ and skills/.")
    parser.add_argument("--skill-slug", required=True, help="Generated skill slug.")
    parser.add_argument("--skill-name", help="Display or canonical generated skill name.")
    parser.add_argument("--source-dir", help="Optional source projection directory. Defaults to ontology/projections/<skill-slug>.")
    parser.add_argument("--output-dir", help="Optional consumer contract output directory.")
    return parser


def main() -> int:
    parser = build_parser()
    return materialize(parser.parse_args())


if __name__ == "__main__":
    sys.exit(main())
