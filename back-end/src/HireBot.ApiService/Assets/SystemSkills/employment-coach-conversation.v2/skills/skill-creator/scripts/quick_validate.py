#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

SLUG_RE = re.compile(r"^[a-z0-9-]+$")
PLACEHOLDER_PATTERNS = [
    re.compile(r"<[^>\n]+>"),
    re.compile(r"\{\{[^}\n]+\}\}"),
]
PROJECTION_HEADING_RE = re.compile(r"(?m)^##\s+Projection Contracts\b")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Quick validation for a HireBot skill directory."
    )
    parser.add_argument("skill_path", help="Path to the skill directory")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    skill_dir = Path(args.skill_path).resolve()
    errors: list[str] = []
    warnings: list[str] = []

    if not skill_dir.is_dir():
        errors.append(f"Skill directory not found: {skill_dir}")
        return print_result(errors, warnings)

    skill_file = skill_dir / "SKILL.md"
    if not skill_file.is_file():
        errors.append(f"SKILL.md not found: {skill_file}")
        return print_result(errors, warnings)

    content = skill_file.read_text(encoding="utf-8")
    frontmatter, body = split_frontmatter(content)
    if frontmatter is None:
        errors.append("SKILL.md is missing YAML frontmatter wrapped by ---")
        return print_result(errors, warnings)

    fields = parse_simple_frontmatter(frontmatter)
    folder_name = skill_dir.name
    name = fields.get("name", "").strip()
    description = fields.get("description", "").strip().strip('"')

    if not name:
        errors.append("Frontmatter is missing `name`.")
    elif name != folder_name:
        errors.append(f"Frontmatter name `{name}` does not match folder `{folder_name}`.")
    elif not SLUG_RE.fullmatch(name):
        errors.append("Frontmatter `name` must use lowercase letters, digits, and hyphens only.")

    if not description:
        errors.append("Frontmatter is missing `description`.")
    elif len(description) < 12:
        warnings.append("Description looks very short; make sure it is discoverable.")

    if not body.strip():
        errors.append("SKILL.md body is empty.")

    placeholders = find_placeholders(content)
    if placeholders:
        errors.append("Unresolved placeholders found: " + ", ".join(sorted(placeholders)))

    has_projection_heading = bool(PROJECTION_HEADING_RE.search(body))
    if has_projection_heading:
        contracts_dir = skill_dir / "contracts" / "projections" / "ontology-extraction"
        if not contracts_dir.exists():
            warnings.append(
                "Projection Contracts section exists, but local ontology-extraction contracts directory was not found."
            )

    if (skill_dir / "contracts").exists() and not has_projection_heading:
        warnings.append(
            "contracts/ exists, but SKILL.md does not mention Projection Contracts."
        )

    return print_result(errors, warnings)


def split_frontmatter(content: str) -> tuple[str | None, str]:
    content = content.removeprefix("\ufeff")
    if not content.startswith("---"):
        return None, content

    parts = content.split("---", 2)
    if len(parts) < 3:
        return None, content

    return parts[1].strip(), parts[2]


def parse_simple_frontmatter(frontmatter: str) -> dict[str, str]:
    fields: dict[str, str] = {}
    for raw_line in frontmatter.splitlines():
        line = raw_line.strip()
        if not line or ":" not in line:
            continue
        key, value = line.split(":", 1)
        fields[key.strip()] = value.strip()
    return fields


def find_placeholders(content: str) -> set[str]:
    sanitized = strip_code(content)
    matches: set[str] = set()
    for pattern in PLACEHOLDER_PATTERNS:
        for match in pattern.findall(sanitized):
            matches.add(match)
    return matches


def strip_code(content: str) -> str:
    without_fenced = re.sub(r"```.*?```", "", content, flags=re.DOTALL)
    without_inline = re.sub(r"`[^`\n]+`", "", without_fenced)
    return without_inline


def print_result(errors: list[str], warnings: list[str]) -> int:
    if not errors and not warnings:
        print("PASS")
        return 0

    if errors:
        print("ERRORS:")
        for item in errors:
            print(f"- {item}")

    if warnings:
        print("WARNINGS:")
        for item in warnings:
            print(f"- {item}")

    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
