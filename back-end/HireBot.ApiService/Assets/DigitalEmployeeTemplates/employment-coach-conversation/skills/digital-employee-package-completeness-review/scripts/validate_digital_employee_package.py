#!/usr/bin/env python3
"""Validate digital employee package completeness.

This script implements the checklist from the
`digital-employee-package-completeness-review` skill. It is intentionally
stdlib-only so it can run in bare CI or local template folders.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

DEFAULT_ONTOLOGY_EXTENSIONS = {".md", ".json"}
REQUIRED_CONFIG_FILES = ["AGENTS.md", "SOUL.md", "IDENTITY.md", "MEMORY.md"]
OPTIONAL_CONFIG_FILES = ["workspace.json"]

# Patterns used for business-rule conflict detection.
# Each entry maps a config keyword to (warning_indicator, block_indicator).
# The validator reads config/SOUL.md for the keyword; if the keyword appears
# with the warning indicator in SOUL and with the block indicator in any
# ontology file, a severity conflict is reported.
DEFAULT_RULE_CONFLICT_PATTERNS: list[dict[str, Any]] = []

# Patterns used for human-confirmation boundary detection.
HUMAN_CONFIRMATION_PATTERNS = [
    re.compile(r"人工", re.IGNORECASE),
    re.compile(r"确认", re.IGNORECASE),
    re.compile(r"human.?in.?the.?loop", re.IGNORECASE),
    re.compile(r"人工确认"),
    re.compile(r"manual\s*(review|approval|confirmation)", re.IGNORECASE),
    re.compile(r"human\s*(review|approval|confirmation)", re.IGNORECASE),
    re.compile(r"审批"),
    re.compile(r"不可逆"),
    re.compile(r"irreversible", re.IGNORECASE),
]

# Patterns used for secret-boundary detection.
SECRET_BOUNDARY_PATTERNS = [
    re.compile(r"token", re.IGNORECASE),
    re.compile(r"密码"),
    re.compile(r"密钥"),
    re.compile(r"凭据"),
    re.compile(r"凭证"),
    re.compile(r"secret", re.IGNORECASE),
    re.compile(r"credential", re.IGNORECASE),
    re.compile(r"api.?key", re.IGNORECASE),
    re.compile(r"access.?key", re.IGNORECASE),
    re.compile(r"禁止.*泄露"),
    re.compile(r"do\s*not\s*(expose|leak|print|log)", re.IGNORECASE),
    re.compile(r"不得.*(出现|明文|暴露|泄露)"),
]

# Skill file filename patterns to look for, in priority order.
SKILL_MD_CANDIDATES = ["SKILL.md", "SKILL.zh.md", "SKILL.en.md"]


def finding(
    severity: str,
    code: str,
    message: str,
    path: str | None = None,
    fix: str | None = None,
) -> dict[str, Any]:
    item: dict[str, Any] = {"severity": severity, "code": code, "message": message}
    if path:
        item["path"] = path
    if fix:
        item["fix"] = fix
    return item


def rel_path(root: Path, path: Path) -> str:
    try:
        return path.relative_to(root).as_posix()
    except ValueError:
        return path.as_posix()


def read_json(path: Path) -> tuple[Any | None, str | None]:
    try:
        return json.loads(path.read_text(encoding="utf-8-sig")), None
    except Exception as exc:  # noqa: BLE001
        return None, str(exc)


def has_frontmatter(path: Path) -> bool:
    try:
        text = path.read_text(encoding="utf-8-sig")
    except Exception:
        return False
    return text.startswith("---") and "\n---" in text[3:]


def path_from_manifest(root: Path, value: Any) -> Path | None:
    if not isinstance(value, str) or not value.strip():
        return None
    candidate = root / value
    try:
        candidate.resolve().relative_to(root.resolve())
    except ValueError:
        return None
    return candidate


def find_skill_md(skill_dir: Path) -> tuple[Path | None, str | None]:
    """Find the primary SKILL.md file in a skill directory.

    Returns (path, matched_filename).  Checks canonical names first,
    then falls back to any SKILL*.md file in the directory.
    """
    for name in SKILL_MD_CANDIDATES:
        candidate = skill_dir / name
        if candidate.is_file():
            return candidate, name

    # Fallback: any SKILL*.md (e.g. SKILL.ja.md, SKILL.fr.md)
    candidates = sorted(skill_dir.glob("SKILL*.md"))
    if candidates:
        return candidates[0], candidates[0].name

    return None, None


def collect_skill_dirs(root: Path) -> list[Path]:
    skills_dir = root / "skills"
    if not skills_dir.is_dir():
        return []
    return sorted(
        [p for p in skills_dir.iterdir() if p.is_dir()],
        key=lambda p: p.name.lower(),
    )


def load_rule_conflict_patterns(root: Path) -> list[dict[str, Any]]:
    """Load business-rule conflict patterns from config/rule-patterns.json if present."""
    patterns_path = root / "config" / "rule-patterns.json"
    if patterns_path.exists():
        data, error = read_json(patterns_path)
        if not error and isinstance(data, list):
            return data
    return list(DEFAULT_RULE_CONFLICT_PATTERNS)


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------


def validate_package(
    package_root: str | Path,
    ontology_extensions: set[str] | None = None,
    expected_skills: list[str] | None = None,
) -> dict[str, Any]:
    root = Path(package_root).resolve()
    ontology_extensions = {
        ext.lower() for ext in (ontology_extensions or DEFAULT_ONTOLOGY_EXTENSIONS)
    }

    report: dict[str, Any] = {
        "package_root": root.as_posix(),
        "status": "PASS",
        "release_readiness": "release-ready",
        "surface": {
            "manifest": False,
            "config_files": [],
            "skills_declared": 0,
            "skills_found": 0,
            "ontology_slices": 0,
            "projection_contract_indexes": 0,
            "evaluation_files": [],
        },
        "p0_blockers": [],
        "findings": [],
        "skills": {},
        "score": {},
        "recommended_fix_order": [],
    }

    if not root.exists() or not root.is_dir():
        add_blocker(
            report,
            "package_root.missing",
            f"Package root does not exist: {root}",
            root.as_posix(),
            "Pass an existing digital employee package directory.",
        )
        finalize_report(report)
        return report

    manifest_path = root / "manifest.json"
    if not manifest_path.exists():
        add_blocker(
            report,
            "manifest.missing",
            "manifest.json is missing.",
            "manifest.json",
            "Add manifest.json at the package root.",
        )
        manifest: dict[str, Any] = {}
    else:
        report["surface"]["manifest"] = True
        manifest_obj, error = read_json(manifest_path)
        if error or not isinstance(manifest_obj, dict):
            add_blocker(
                report,
                "manifest.invalid_json",
                f"manifest.json is not valid JSON: {error}",
                "manifest.json",
                "Fix manifest.json syntax.",
            )
            manifest = {}
        else:
            manifest = manifest_obj
            validate_manifest_identity(report, manifest)
            validate_entry_skill(report, root, manifest)

    validate_config(report, root, manifest)
    validate_manifest_ontology(report, root, manifest, ontology_extensions)
    declared_skills = validate_manifest_skills(report, root, manifest)
    validate_skill_directories(report, root, declared_skills)
    validate_ontology_slices(report, root, ontology_extensions)
    validate_evaluation_materials(report, root, manifest)

    # Workflow closure: prefer explicit --expected-skills, then fall back to
    # skill names extracted from manifest stage_rules.
    expected = expected_skills or derive_expected_skills_from_manifest(manifest)
    validate_workflow_closure(report, declared_skills, expected)

    rule_patterns = load_rule_conflict_patterns(root)
    validate_basic_rule_consistency(report, root, rule_patterns)
    validate_security_boundaries(report, root)
    score_report(report)
    finalize_report(report)
    return report


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def add_finding(
    report: dict[str, Any],
    severity: str,
    code: str,
    message: str,
    path: str | None = None,
    fix: str | None = None,
) -> None:
    item = finding(severity, code, message, path, fix)
    report["findings"].append(item)
    if severity == "P0":
        report["p0_blockers"].append(item)


def add_blocker(
    report: dict[str, Any],
    code: str,
    message: str,
    path: str | None = None,
    fix: str | None = None,
) -> None:
    add_finding(report, "P0", code, message, path, fix)


# ---------------------------------------------------------------------------
# Validators
# ---------------------------------------------------------------------------


def validate_manifest_identity(
    report: dict[str, Any], manifest: dict[str, Any]
) -> None:
    for field in ["name", "display_name", "version", "description"]:
        if not manifest.get(field):
            add_finding(
                report,
                "WARN",
                f"manifest.identity.{field}.missing",
                f"manifest.json is missing `{field}`.",
                "manifest.json",
                f"Add `{field}` to manifest.json.",
            )


def validate_entry_skill(
    report: dict[str, Any], root: Path, manifest: dict[str, Any]
) -> None:
    raw = manifest.get("entry_skill")
    if not raw:
        add_finding(
            report,
            "WARN",
            "manifest.entry_skill.missing",
            "manifest.json is missing `entry_skill`.",
            "manifest.json",
            "Add `entry_skill` pointing to the runtime entry skill.",
        )
        return
    if not isinstance(raw, str):
        add_finding(
            report,
            "WARN",
            "manifest.entry_skill.invalid",
            f"manifest.json `entry_skill` is not a string: {type(raw).__name__}",
            "manifest.json",
            "Set `entry_skill` to a package-relative path like `skills/<name>/SKILL.md`.",
        )
        return

    # entry_skill is typically "skills/<skill-name>/SKILL.md" or "skills/<skill-name>".
    normalized = raw.strip().replace("\\", "/").rstrip("/")
    candidates: list[Path] = [root / normalized]
    if not normalized.endswith(".md"):
        candidates.append(root / f"{normalized}/SKILL.md")

    resolved = next((c for c in candidates if c.is_file()), None)

    # If entry_skill points to a directory, try SKILL.*.md variants inside it.
    if resolved is None:
        dir_candidate = root / normalized
        if dir_candidate.is_dir():
            variant_md, _ = find_skill_md(dir_candidate)
            if variant_md is not None:
                resolved = variant_md

    if resolved is None:
        add_finding(
            report,
            "WARN",
            "manifest.entry_skill.unresolved",
            f"entry_skill `{raw}` does not resolve to an existing file.",
            "manifest.json",
            "Ensure entry_skill points to a valid SKILL.md inside the package.",
        )


def validate_config(
    report: dict[str, Any], root: Path, manifest: dict[str, Any]
) -> None:
    config = manifest.get("config") if isinstance(manifest.get("config"), dict) else {}
    expected_paths: set[str] = set()
    for value in config.values():
        if isinstance(value, str):
            expected_paths.add(value)
    for file_name in REQUIRED_CONFIG_FILES:
        expected_paths.add(f"config/{file_name}")

    for config_path in sorted(expected_paths):
        path = path_from_manifest(root, config_path)
        if path is None:
            add_blocker(
                report,
                "config.path.invalid",
                f"Config path escapes package root or is invalid: {config_path}",
                config_path,
                "Use a package-root-relative config path.",
            )
            continue
        if path.exists():
            report["surface"]["config_files"].append(rel_path(root, path))
        else:
            add_finding(
                report,
                "WARN",
                "config.file.missing",
                f"Config file is missing: {config_path}",
                config_path,
                "Add the missing config file or remove it from manifest.json.",
            )

    # Optional config files (workspace.json etc.)
    for file_name in OPTIONAL_CONFIG_FILES:
        path = root / "config" / file_name
        if path.exists():
            if rel_path(root, path) not in report["surface"]["config_files"]:
                report["surface"]["config_files"].append(rel_path(root, path))
        else:
            add_finding(
                report,
                "WARN",
                "config.optional_file.missing",
                f"Optional config file is missing: config/{file_name}",
                f"config/{file_name}",
                f"Consider adding config/{file_name} for workspace path bindings.",
            )


def validate_manifest_ontology(
    report: dict[str, Any],
    root: Path,
    manifest: dict[str, Any],
    ontology_extensions: set[str],
) -> None:
    slices = (
        manifest.get("ontology_slices")
        if isinstance(manifest.get("ontology_slices"), list)
        else []
    )
    for item in slices:
        if not isinstance(item, dict):
            add_finding(
                report,
                "WARN",
                "manifest.ontology.invalid_entry",
                "ontology_slices contains a non-object entry.",
                "manifest.json",
                "Use objects with name/path/required fields.",
            )
            continue
        slice_path = item.get("path")
        path = path_from_manifest(root, slice_path)
        if path is None:
            add_blocker(
                report,
                "manifest.ontology.invalid_path",
                f"Ontology slice path is invalid: {slice_path}",
                str(slice_path),
                "Use a package-root-relative ontology path.",
            )
            continue
        if not path.exists():
            add_blocker(
                report,
                "manifest.ontology.missing",
                f"Manifest ontology slice does not exist: {slice_path}",
                str(slice_path),
                "Add the ontology slice or update manifest.json.",
            )
            continue
        report["surface"]["ontology_slices"] += 1
        if path.suffix.lower() not in ontology_extensions:
            add_blocker(
                report,
                "manifest.ontology.not_installable",
                f"Manifest ontology slice exists but extension `{path.suffix}` is not accepted by install rules.",
                rel_path(root, path),
                f"Allow `{path.suffix}` in the uploader or change the package to one of: {sorted(ontology_extensions)}.",
            )


def validate_manifest_skills(
    report: dict[str, Any], root: Path, manifest: dict[str, Any]
) -> dict[str, dict[str, Any]]:
    declared: dict[str, dict[str, Any]] = {}
    skills = (
        manifest.get("skills") if isinstance(manifest.get("skills"), list) else []
    )
    report["surface"]["skills_declared"] = len(skills)
    for item in skills:
        if not isinstance(item, dict):
            add_finding(
                report,
                "WARN",
                "manifest.skill.invalid_entry",
                "manifest skills contains a non-object entry.",
                "manifest.json",
                "Use objects with name/path/required fields.",
            )
            continue
        name = item.get("name")
        skill_path = item.get("path")
        if isinstance(name, str):
            declared[name] = item
        path = path_from_manifest(root, skill_path)
        if path is None:
            add_blocker(
                report,
                "manifest.skill.invalid_path",
                f"Skill path is invalid: {skill_path}",
                str(skill_path),
                "Use a package-root-relative skill path.",
            )
            continue
        if not path.exists():
            severity = "P0" if item.get("required") else "WARN"
            add_finding(
                report,
                severity,
                "manifest.skill.missing",
                f"Manifest skill path does not exist: {skill_path}",
                str(skill_path),
                "Add SKILL.md or update manifest.json.",
            )
    return declared


def validate_skill_directories(
    report: dict[str, Any],
    root: Path,
    declared_skills: dict[str, dict[str, Any]],
) -> None:
    skill_dirs = collect_skill_dirs(root)
    report["surface"]["skills_found"] = len(skill_dirs)
    for skill_dir in skill_dirs:
        validate_one_skill(report, root, skill_dir, declared_skills)

    for name in sorted(set(declared_skills) - {d.name for d in skill_dirs}):
        add_blocker(
            report,
            "manifest.skill.dir_missing",
            f"Manifest declares skill `{name}` but no matching skills/{name}/ directory exists.",
            f"skills/{name}",
            "Add the skill directory or update manifest.json.",
        )


def validate_one_skill(
    report: dict[str, Any],
    root: Path,
    skill_dir: Path,
    declared_skills: dict[str, dict[str, Any]],
) -> None:
    name = skill_dir.name
    skill_report = {
        "exists": True,
        "metadata": False,
        "projection": False,
        "workflow_role": infer_workflow_role(name, declared_skills),
        "status": "PASS",
        "notes": [],
    }
    report["skills"][name] = skill_report

    # Find the SKILL.md file (with fallback variants)
    skill_md, matched_name = find_skill_md(skill_dir)
    if skill_md is None:
        add_blocker(
            report,
            "skill.skill_md.missing",
            f"Skill `{name}` is missing SKILL.md (checked: {', '.join(SKILL_MD_CANDIDATES)}).",
            rel_path(root, skill_dir / "SKILL.md"),
            "Add SKILL.md with frontmatter and instructions.",
        )
        skill_report["status"] = "FAIL"
        skill_report["notes"].append("Missing SKILL.md")
    else:
        if matched_name != "SKILL.md":
            skill_report["notes"].append(f"SKILL.md variant: {matched_name}")
        if not has_frontmatter(skill_md):
            add_finding(
                report,
                "WARN",
                "skill.skill_md.frontmatter_missing",
                f"Skill `{name}` {matched_name} lacks YAML frontmatter.",
                rel_path(root, skill_md),
                "Add YAML frontmatter with name and description.",
            )
            skill_report["status"] = "PASS_WITH_CONCERNS"
            skill_report["notes"].append(f"{matched_name} frontmatter missing")

        # Check manifest path alignment with the resolved SKILL.md
        _check_manifest_skill_path_alignment(
            report, root, name, skill_md, declared_skills
        )

    metadata_path = skill_dir / "metadata.json"
    metadata: dict[str, Any] = {}
    if metadata_path.exists():
        metadata_obj, error = read_json(metadata_path)
        if error or not isinstance(metadata_obj, dict):
            add_finding(
                report,
                "WARN",
                "skill.metadata.invalid_json",
                f"Skill `{name}` metadata.json is invalid: {error}",
                rel_path(root, metadata_path),
                "Fix metadata.json syntax.",
            )
            skill_report["status"] = "PASS_WITH_CONCERNS"
        else:
            metadata = metadata_obj
            skill_report["metadata"] = True
            if metadata.get("name") and metadata.get("name") != name:
                add_finding(
                    report,
                    "WARN",
                    "skill.metadata.name_mismatch",
                    f"Skill `{name}` metadata name is `{metadata.get('name')}`.",
                    rel_path(root, metadata_path),
                    "Set metadata.name to the skill directory name.",
                )
            validate_metadata_projection_paths(
                report, root, skill_report, metadata, metadata_path
            )
    else:
        add_finding(
            report,
            "WARN",
            "skill.metadata.missing",
            f"Skill `{name}` is missing metadata.json.",
            rel_path(root, metadata_path),
            "Add metadata.json with triggers, capabilities, and boundaries.",
        )
        skill_report["status"] = "PASS_WITH_CONCERNS"

    contract_indexes = sorted(
        skill_dir.glob("contracts/projections/**/contract-index.json")
    )
    if contract_indexes:
        skill_report["projection"] = True
        report["surface"]["projection_contract_indexes"] += len(contract_indexes)
        for index_path in contract_indexes:
            validate_contract_index(report, root, skill_report, name, index_path)
    elif metadata.get("sources"):
        add_finding(
            report,
            "WARN",
            "skill.projection_index.missing",
            f"Skill `{name}` has metadata sources but no projection contract-index.json.",
            rel_path(root, skill_dir),
            "Add contracts/projections/<producer>/contract-index.json or remove projection metadata.",
        )
        skill_report["status"] = "PASS_WITH_CONCERNS"


def _check_manifest_skill_path_alignment(
    report: dict[str, Any],
    root: Path,
    name: str,
    skill_md: Path,
    declared_skills: dict[str, dict[str, Any]],
) -> None:
    """Check that the manifest-declared path for a skill points to its actual SKILL.md."""
    if name not in declared_skills:
        return
    declared_path = declared_skills[name].get("path")
    if not isinstance(declared_path, str):
        return
    manifest_target = path_from_manifest(root, declared_path)
    if manifest_target is None:
        return
    if not manifest_target.exists():
        # Manifest path doesn't exist at all — report mismatch since we
        # found a SKILL.*.md at a different name.
        add_finding(
            report,
            "WARN",
            "manifest.skill.path_mismatch",
            f"Manifest path for `{name}` points to a non-existent file, but {skill_md.name} exists.",
            declared_path,
            "Update manifest skill path to point to the actual file.",
        )
        return
    if not manifest_target.samefile(skill_md):
        add_finding(
            report,
            "WARN",
            "manifest.skill.path_mismatch",
            f"Manifest path for `{name}` does not point to its SKILL.md.",
            declared_path,
            "Update manifest skill path.",
        )


def validate_metadata_projection_paths(
    report: dict[str, Any],
    root: Path,
    skill_report: dict[str, Any],
    metadata: dict[str, Any],
    metadata_path: Path,
) -> None:
    sources = (
        metadata.get("sources")
        if isinstance(metadata.get("sources"), list)
        else []
    )
    for source in sources:
        if not isinstance(source, dict):
            continue
        projection_paths = (
            source.get("source_projection_paths")
            if isinstance(source.get("source_projection_paths"), list)
            else []
        )
        for raw_path in projection_paths:
            if not isinstance(raw_path, str):
                continue
            path = path_from_manifest(root, raw_path)
            if path is None or not path.exists():
                add_finding(
                    report,
                    "WARN",
                    "skill.metadata_projection_path.missing",
                    f"metadata.json references a projection path that does not exist: {raw_path}",
                    rel_path(root, metadata_path),
                    "Point source_projection_paths at skills/<skill>/contracts/projections/... or remove stale metadata.",
                )
                skill_report["status"] = "PASS_WITH_CONCERNS"
                skill_report["notes"].append(
                    f"Stale metadata projection path: {raw_path}"
                )


def validate_contract_index(
    report: dict[str, Any],
    root: Path,
    skill_report: dict[str, Any],
    skill_name: str,
    index_path: Path,
) -> None:
    index, error = read_json(index_path)
    if error or not isinstance(index, dict):
        add_blocker(
            report,
            "projection.index.invalid_json",
            f"Projection contract index is invalid JSON: {error}",
            rel_path(root, index_path),
            "Fix contract-index.json syntax.",
        )
        skill_report["status"] = "FAIL"
        return

    if (
        index.get("consumer_skill")
        and index.get("consumer_skill") != skill_name
    ):
        add_finding(
            report,
            "WARN",
            "projection.consumer_mismatch",
            f"contract-index consumer_skill is `{index.get('consumer_skill')}` but directory is `{skill_name}`.",
            rel_path(root, index_path),
            "Set consumer_skill to the skill directory name.",
        )
        skill_report["status"] = "PASS_WITH_CONCERNS"

    topics = (
        index.get("topics") if isinstance(index.get("topics"), list) else []
    )
    if not topics:
        add_finding(
            report,
            "WARN",
            "projection.topics.missing",
            "contract-index.json has no topics.",
            rel_path(root, index_path),
            "Add topics with views.",
        )
        skill_report["status"] = "PASS_WITH_CONCERNS"
    for topic in topics:
        if not isinstance(topic, dict):
            continue
        default_view = topic.get("default_target_view")
        views = (
            topic.get("views")
            if isinstance(topic.get("views"), list)
            else []
        )
        view_names = {
            view.get("target_view")
            for view in views
            if isinstance(view, dict)
        }
        if default_view and default_view not in view_names:
            add_blocker(
                report,
                "projection.default_view.missing",
                f"Default target view `{default_view}` has no matching view entry.",
                rel_path(root, index_path),
                "Add the default view or update default_target_view.",
            )
            skill_report["status"] = "FAIL"
        for view in views:
            if not isinstance(view, dict):
                continue
            raw_path = view.get("path")
            if not isinstance(raw_path, str):
                add_blocker(
                    report,
                    "projection.view_path.invalid",
                    "Projection view lacks a string path.",
                    rel_path(root, index_path),
                    "Add a relative projection file path.",
                )
                skill_report["status"] = "FAIL"
                continue
            view_path = index_path.parent / raw_path
            if not view_path.exists():
                add_blocker(
                    report,
                    "projection.view_path.missing",
                    f"Projection view path does not exist: {raw_path}",
                    rel_path(root, index_path),
                    "Add the projection file or update contract-index.json.",
                )
                skill_report["status"] = "FAIL"
                continue
            projection, projection_error = read_json(view_path)
            if projection_error or not isinstance(projection, dict):
                add_blocker(
                    report,
                    "projection.view.invalid_json",
                    f"Projection file is invalid JSON: {projection_error}",
                    rel_path(root, view_path),
                    "Fix the projection JSON file.",
                )
                skill_report["status"] = "FAIL"
                continue
            validate_projection_document(
                report, root, skill_report, view_path, projection
            )


def validate_projection_document(
    report: dict[str, Any],
    root: Path,
    skill_report: dict[str, Any],
    view_path: Path,
    projection: dict[str, Any],
) -> None:
    open_questions = projection.get("open_questions")
    if isinstance(open_questions, list) and open_questions:
        add_finding(
            report,
            "WARN",
            "projection.open_questions.present",
            "Projection has open_questions and should be reviewed before release.",
            rel_path(root, view_path),
            "Resolve or explicitly accept open_questions before production rollout.",
        )
        if skill_report["status"] == "PASS":
            skill_report["status"] = "PASS_WITH_CONCERNS"
    source_slice = (
        projection.get("source_slice")
        if isinstance(projection.get("source_slice"), dict)
        else {}
    )
    raw_source_path = source_slice.get("path")
    if isinstance(raw_source_path, str):
        candidates = [
            root / raw_source_path,
            view_path.parent / raw_source_path,
        ]
        if not any(candidate.exists() for candidate in candidates):
            add_finding(
                report,
                "WARN",
                "projection.source_slice.unresolved",
                f"Projection source_slice.path does not resolve: {raw_source_path}",
                rel_path(root, view_path),
                "Use a package-root-relative path or a correct projection-relative path.",
            )
            if skill_report["status"] == "PASS":
                skill_report["status"] = "PASS_WITH_CONCERNS"


def validate_ontology_slices(
    report: dict[str, Any],
    root: Path,
    ontology_extensions: set[str],
) -> None:
    ontology_dir = root / "ontology"
    if not ontology_dir.exists():
        add_finding(
            report,
            "WARN",
            "ontology.dir.missing",
            "ontology/ directory is missing.",
            "ontology/",
            "Add ontology slices if this package uses domain knowledge.",
        )
        return
    for path in sorted(ontology_dir.iterdir()):
        if path.is_dir():
            add_finding(
                report,
                "WARN",
                "ontology.subdir.ignored",
                f"Ontology subdirectory may not be installable: {path.name}",
                rel_path(root, path),
                "Keep ontology files at top-level unless the uploader supports subdirectories.",
            )
            continue
        if path.suffix.lower() not in ontology_extensions:
            add_finding(
                report,
                "WARN",
                "ontology.file.not_installable",
                f"Ontology file extension may not be installable: {path.name}",
                rel_path(root, path),
                f"Use one of {sorted(ontology_extensions)} or update install rules.",
            )
            continue
        if path.suffix.lower() == ".json":
            data, error = read_json(path)
            if error or not isinstance(data, dict):
                add_finding(
                    report,
                    "WARN",
                    "ontology.invalid_json",
                    f"Ontology JSON is invalid: {error}",
                    rel_path(root, path),
                    "Fix ontology JSON syntax.",
                )
                continue
            for key in ["sources", "concepts", "relations", "constraints"]:
                if key not in data:
                    add_finding(
                        report,
                        "WARN",
                        f"ontology.{key}.missing",
                        f"Ontology slice is missing `{key}`.",
                        rel_path(root, path),
                        f"Add `{key}` to the ontology slice.",
                    )
            validation = data.get("validation") or (
                data.get("meta") if isinstance(data.get("meta"), dict) else {}
            ).get("validation")
            if validation == "NOT_RUN":
                add_finding(
                    report,
                    "WARN",
                    "ontology.validation.not_run",
                    "Ontology validation status is NOT_RUN.",
                    rel_path(root, path),
                    "Run validation and update status before release.",
                )
            if has_field_count_claim(data) and not has_machine_readable_field_definitions(
                data
            ):
                add_finding(
                    report,
                    "WARN",
                    "ontology.field_count_without_schema",
                    "Ontology claims field counts but lacks machine-readable field definitions.",
                    rel_path(root, path),
                    "Add field definitions with type, required flag, source, precision, mapping, and constraints.",
                )


def has_field_count_claim(data: dict[str, Any]) -> bool:
    text = json.dumps(data, ensure_ascii=False)
    return bool(
        re.search(r"\b\d+\s*个字段|\b\d+\s*fields", text, flags=re.IGNORECASE)
    )


def has_machine_readable_field_definitions(data: dict[str, Any]) -> bool:
    for key in ["fields", "field_definitions", "field_catalog", "schemas"]:
        value = data.get(key)
        if isinstance(value, list) and value:
            return True
        if isinstance(value, dict) and value:
            return True
    return False


def validate_evaluation_materials(
    report: dict[str, Any],
    root: Path,
    manifest: dict[str, Any],
) -> None:
    eval_files = []
    for path in [root / "evaluation.md", root / "evaluation" / "testcases.json"]:
        if path.exists():
            eval_files.append(rel_path(root, path))
    testcases_dir = root / "testcases"
    if testcases_dir.exists():
        eval_files.extend(
            rel_path(root, p) for p in sorted(testcases_dir.glob("*.json"))
        )
    report["surface"]["evaluation_files"] = eval_files
    if not eval_files:
        add_finding(
            report,
            "WARN",
            "evaluation.missing",
            "No evaluation files found.",
            None,
            "Add evaluation.md and testcases JSON files.",
        )

    evaluation_md = root / "evaluation.md"
    if evaluation_md.exists() and manifest.get("skills"):
        try:
            text = evaluation_md.read_text(encoding="utf-8-sig")
        except Exception:
            return
        text_lower = text.lower()
        stale_patterns = [
            "没有绑定技能",
            "还没有绑定技能",
            "no skills bound",
            "skills are not bound",
        ]
        if any(pattern.lower() in text_lower for pattern in stale_patterns):
            add_finding(
                report,
                "WARN",
                "evaluation.stale_skill_binding",
                "evaluation.md states skills are not bound, but manifest declares skills.",
                rel_path(root, evaluation_md),
                "Update evaluation.md to validate actual bound skills.",
            )


def derive_expected_skills_from_manifest(
    manifest: dict[str, Any],
) -> list[str]:
    """Extract expected workflow skill names from manifest stage_rules."""
    stage_rules = (
        manifest.get("stage_rules")
        if isinstance(manifest.get("stage_rules"), list)
        else []
    )
    names: list[str] = []
    for rule in stage_rules:
        if isinstance(rule, dict):
            skill_name = rule.get("skill_name")
            if isinstance(skill_name, str) and skill_name.strip():
                names.append(skill_name.strip())
    return names


def validate_workflow_closure(
    report: dict[str, Any],
    declared_skills: dict[str, dict[str, Any]],
    expected_skills: list[str],
) -> None:
    """Validate that all expected workflow skills are declared.

    When expected_skills is empty, skip the check (the package may not use a
    fixed workflow model, or workflow skills are not expressed as stage_rules).
    """
    if not expected_skills or not declared_skills:
        return
    declared = set(declared_skills)
    missing = [skill for skill in expected_skills if skill not in declared]
    if missing:
        add_finding(
            report,
            "WARN",
            "workflow.expected_skill.missing",
            f"Workflow may be incomplete. Missing expected skills: {', '.join(missing)}",
            "manifest.json",
            "Add missing workflow skills or document why this package does not need them.",
        )


def validate_basic_rule_consistency(
    report: dict[str, Any],
    root: Path,
    rule_patterns: list[dict[str, Any]],
) -> None:
    """Check for severity conflicts between SOUL.md and ontology files.

    Uses configurable rule_patterns. Each pattern is a dict with:
      - keyword:  str (required) the business term to search for
      - warning_indicator:  str (required) marks a "warning" severity in SOUL
      - block_indicator:    str (required) marks a "block"  severity in ontology
      - code: str (optional) finding code, defaults to rule.<keyword>.severity_conflict
      - label: str (optional) human-readable label for the finding message
    """
    if not rule_patterns:
        return

    soul = root / "config" / "SOUL.md"
    if not soul.exists():
        return

    ontology_text = ""
    ontology_dir = root / "ontology"
    if ontology_dir.exists():
        for path in ontology_dir.glob("*"):
            if path.is_file() and path.suffix.lower() in {".md", ".json"}:
                try:
                    ontology_text += "\n" + path.read_text(encoding="utf-8-sig")
                except Exception:
                    pass
    if not ontology_text:
        return

    soul_text = soul.read_text(encoding="utf-8-sig")

    for pattern in rule_patterns:
        keyword = pattern.get("keyword")
        warning_indicator = pattern.get("warning_indicator")
        block_indicator = pattern.get("block_indicator")
        if not all(
            isinstance(x, str) and x for x in [keyword, warning_indicator, block_indicator]
        ):
            continue

        if (
            keyword in soul_text
            and warning_indicator in soul_text
            and keyword in ontology_text
            and block_indicator in ontology_text
        ):
            code = pattern.get("code") or f"rule.{keyword}.severity_conflict"
            label = pattern.get("label") or keyword
            add_finding(
                report,
                "WARN",
                code,
                f"Severity conflict: `{label}` appears as `{warning_indicator}` in SOUL.md but `{block_indicator}` in ontology.",
                rel_path(root, soul),
                "Choose one severity and update SOUL, ontology, skills, projections, and testcases.",
            )


def validate_security_boundaries(
    report: dict[str, Any],
    root: Path,
) -> None:
    text = ""
    for path in [root / "config" / "IDENTITY.md", root / "config" / "SOUL.md"]:
        if path.exists():
            try:
                text += "\n" + path.read_text(encoding="utf-8-sig")
            except Exception:
                pass

    if text:
        if not any(p.search(text) for p in HUMAN_CONFIRMATION_PATTERNS):
            add_finding(
                report,
                "WARN",
                "security.human_confirmation.missing",
                "Config does not clearly require human confirmation for irreversible actions.",
                "config/",
                "Add explicit human confirmation boundary for downstream push and irreversible actions.",
            )
        if not any(p.search(text) for p in SECRET_BOUNDARY_PATTERNS):
            add_finding(
                report,
                "WARN",
                "security.secret_boundary.missing",
                "Config does not clearly forbid secrets in chat, notifications, or logs.",
                "config/",
                "Add a no-secrets boundary (e.g. prohibit tokens, passwords, credentials, API keys).",
            )


def infer_workflow_role(
    skill_name: str, declared_skills: dict[str, dict[str, Any]]
) -> str:
    """Infer a workflow role label from manifest stage_rules or return 'custom'."""
    # First, try to find a stage_rule that references this skill by name
    for item in declared_skills.values():
        stage_rule = item.get("_stage")
        if isinstance(stage_rule, str) and item.get("name") == skill_name:
            return stage_rule
    return "custom"


def score_report(report: dict[str, Any]) -> None:
    p0 = len(report["p0_blockers"])
    skills = report["skills"]
    skill_failures = len(
        [s for s in skills.values() if s["status"] == "FAIL"]
    )
    skill_concerns = len(
        [s for s in skills.values() if s["status"] == "PASS_WITH_CONCERNS"]
    )

    score = {
        "package_structure": 10 if report["surface"]["manifest"] else 3,
        "manifest_path_correctness": max(0, 10 - p0 * 3),
        "config_consistency": max(0, 10 - count_code_prefix(report, "config.") * 2),
        "skill_completeness": max(0, 10 - skill_failures * 4 - skill_concerns),
        "projection_runtime_readiness": max(
            0,
            10
            - count_code_prefix(report, "projection.") * 3
            - count_code_prefix(report, "skill.metadata_projection_path") * 2,
        ),
        "ontology_completeness": max(
            0,
            10
            - count_code_prefix(report, "ontology.") * 2
            - count_code_prefix(report, "manifest.ontology") * 3,
        ),
        "workflow_closure": max(0, 10 - count_code_prefix(report, "workflow.") * 2),
        "evaluation_coverage": max(
            0, 10 - count_code_prefix(report, "evaluation.") * 2
        ),
        "rule_consistency": max(0, 10 - count_code_prefix(report, "rule.") * 3),
        "security_authority_boundary": max(
            0, 10 - count_code_prefix(report, "security.") * 2
        ),
    }
    report["score"] = score
    average = sum(score.values()) / len(score)
    report["score_average"] = round(average, 1)
    if p0:
        report["release_readiness"] = "not-production-ready"
    elif average >= 9:
        report["release_readiness"] = "release-ready"
    elif average >= 7:
        report["release_readiness"] = "beta-ready"
    elif average >= 5:
        report["release_readiness"] = "not-production-ready"
    else:
        report["release_readiness"] = "incomplete"


def count_code_prefix(report: dict[str, Any], prefix: str) -> int:
    return len(
        [
            finding
            for finding in report["findings"]
            if finding["code"].startswith(prefix)
        ]
    )


def finalize_report(report: dict[str, Any]) -> None:
    if report["p0_blockers"]:
        report["status"] = "FAIL"
    elif any(f["severity"] == "WARN" for f in report["findings"]):
        report["status"] = "PASS_WITH_CONCERNS"
    else:
        report["status"] = "PASS"

    fixes = []
    for item in report["p0_blockers"] + report["findings"]:
        fix = item.get("fix")
        if fix and fix not in fixes:
            fixes.append(fix)
    report["recommended_fix_order"] = fixes[:10]


# ---------------------------------------------------------------------------
# Output rendering
# ---------------------------------------------------------------------------


def render_markdown(report: dict[str, Any]) -> str:
    lines: list[str] = []
    lines.append("# Digital Employee Package Completeness Review")
    lines.append("")
    lines.append("## Verdict")
    lines.append("")
    lines.append(f"Status: {report['status']}")
    lines.append(f"Release readiness: {report['release_readiness']}")
    lines.append(f"Score average: {report.get('score_average', 0)}")
    lines.append("")
    lines.append("## Package Surface")
    lines.append("")
    for key, value in report["surface"].items():
        lines.append(f"- {key}: {value}")
    lines.append("")
    lines.append("## P0 Blockers")
    lines.append("")
    if report["p0_blockers"]:
        for item in report["p0_blockers"]:
            lines.append(f"- {item['code']}: {item['message']}")
            if item.get("path"):
                lines.append(f"  - Evidence: {item['path']}")
            if item.get("fix"):
                lines.append(f"  - Fix: {item['fix']}")
    else:
        lines.append("None")
    lines.append("")
    lines.append("## Skill Matrix")
    lines.append("")
    lines.append(
        "| Skill | Metadata | Projection | Workflow role | Status | Notes |"
    )
    lines.append("|---|---:|---:|---|---|---|")
    for skill_name, skill in sorted(report["skills"].items()):
        notes = "; ".join(skill.get("notes", []))
        lines.append(
            f"| {skill_name} | {skill['metadata']} | {skill['projection']} | {skill['workflow_role']} | {skill['status']} | {notes} |"
        )
    lines.append("")
    lines.append("## Findings")
    lines.append("")
    for item in report["findings"]:
        lines.append(f"- {item['severity']} {item['code']}: {item['message']}")
    if not report["findings"]:
        lines.append("None")
    lines.append("")
    lines.append("## Score")
    lines.append("")
    lines.append("| Dimension | Score |")
    lines.append("|---|---:|")
    for key, value in report.get("score", {}).items():
        lines.append(f"| {key} | {value} |")
    lines.append("")
    lines.append("## Recommended Fix Order")
    lines.append("")
    if report["recommended_fix_order"]:
        for index, fix in enumerate(report["recommended_fix_order"], 1):
            lines.append(f"{index}. {fix}")
    else:
        lines.append("None")
    return "\n".join(lines) + "\n"


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def parse_extensions(raw: str | None) -> set[str]:
    if not raw:
        return set(DEFAULT_ONTOLOGY_EXTENSIONS)
    result = set()
    for part in raw.split(","):
        value = part.strip().lower()
        if not value:
            continue
        if not value.startswith("."):
            value = "." + value
        result.add(value)
    return result or set(DEFAULT_ONTOLOGY_EXTENSIONS)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Validate digital employee package completeness."
    )
    parser.add_argument(
        "package_root", help="Path to the digital employee package root"
    )
    parser.add_argument(
        "--ontology-extensions",
        default=None,
        help="Comma-separated installable ontology extensions, e.g. .md,.json",
    )
    parser.add_argument(
        "--expected-skills",
        default=None,
        help="Comma-separated list of expected workflow skill names. "
        "If omitted, derived from manifest stage_rules.",
    )
    parser.add_argument(
        "--format",
        choices=["markdown", "json"],
        default="markdown",
        help="Output format",
    )
    parser.add_argument(
        "--output",
        help="Optional output file path (resolved relative to current working directory).",
    )
    args = parser.parse_args(argv)

    expected_skills = None
    if args.expected_skills:
        expected_skills = [
            name.strip()
            for name in args.expected_skills.split(",")
            if name.strip()
        ]

    report = validate_package(
        args.package_root,
        ontology_extensions=parse_extensions(args.ontology_extensions),
        expected_skills=expected_skills,
    )
    if args.format == "json":
        output = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    else:
        output = render_markdown(report)

    if args.output:
        output_path = Path(args.output)
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(output, encoding="utf-8")
    else:
        sys.stdout.write(output)

    return 1 if report["status"] == "FAIL" else 0


if __name__ == "__main__":
    raise SystemExit(main())
