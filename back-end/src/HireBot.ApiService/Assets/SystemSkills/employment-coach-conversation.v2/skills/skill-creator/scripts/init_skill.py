#!/usr/bin/env python3
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

SLUG_RE = re.compile(r"^[a-z0-9-]+$")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Initialize a HireBot skill directory inside a template package."
    )
    parser.add_argument("skill_name", help="Folder name for the new skill, e.g. followup-writer")
    parser.add_argument(
        "--package-root",
        default=None,
        help="Template package root. Defaults to the active employment-coach-conversation.v2 package.",
    )
    parser.add_argument(
        "--consumer",
        action="store_true",
        help="Seed the new skill from the ontology consumer scaffold instead of the standard template.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite an existing SKILL.md if the target directory already exists.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    skill_name = args.skill_name.strip()
    if not SLUG_RE.fullmatch(skill_name):
        print(
            "skill_name must use lowercase letters, digits, and hyphens only.",
            file=sys.stderr,
        )
        return 1

    script_dir = Path(__file__).resolve().parent
    skill_creator_dir = script_dir.parent
    default_package_root = skill_creator_dir.parent.parent
    package_root = (
        Path(args.package_root).resolve()
        if args.package_root
        else default_package_root
    )
    skills_root = package_root / "skills"
    if not skills_root.is_dir():
        print(f"Package root does not contain a skills directory: {skills_root}", file=sys.stderr)
        return 1

    target_dir = skills_root / skill_name
    target_dir.mkdir(parents=True, exist_ok=True)
    target_skill = target_dir / "SKILL.md"
    if target_skill.exists() and not args.force:
        print(f"Target already exists: {target_skill}", file=sys.stderr)
        return 1

    template_text = load_template(skill_creator_dir, args.consumer)
    rendered = render_template(template_text, skill_name)
    target_skill.write_text(rendered, encoding="utf-8")

    if args.consumer:
        (target_dir / "contracts" / "projections" / "ontology-extraction").mkdir(
            parents=True, exist_ok=True
        )

    print(target_skill)
    return 0


def load_template(skill_creator_dir: Path, consumer: bool) -> str:
    if consumer:
        consumer_path = (
            skill_creator_dir.parent / "ontology-extraction" / "templates" / "CONSUMER_SKILL_SCAFFOLD.md"
        )
        return extract_fenced_block(consumer_path, "```md")

    template_path = skill_creator_dir / "references" / "skill-template.md"
    return extract_fenced_block(template_path, "```md")


def extract_fenced_block(path: Path, opening_fence: str) -> str:
    content = path.read_text(encoding="utf-8")
    start = content.find(opening_fence)
    if start < 0:
        raise ValueError(f"Opening fence {opening_fence!r} not found in {path}")
    start += len(opening_fence)
    end = content.find("```", start)
    if end < 0:
        raise ValueError(f"Closing fence not found in {path}")
    return content[start:end].strip() + "\n"


def render_template(template_text: str, skill_name: str) -> str:
    skill_title = skill_name.replace("-", " ").title()
    return (
        template_text
        .replace("<skill-slug>", skill_name)
        .replace("<consumer-skill-name>", skill_name)
        .replace("<skill-title>", skill_title)
    )


if __name__ == "__main__":
    raise SystemExit(main())
