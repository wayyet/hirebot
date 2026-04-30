# System Skills

`Assets/SystemSkills` is the single source of truth for project-level workflow skills.

Repository vs runtime:

- This directory is the only repository source of truth for project-level skills.
- The repository root `workspace/skills` directory is not a canonical source and should not be used to add or update project skills.
- Runtime sandbox paths such as `/workspace/skills/<name>/` are install targets created by service uploads and are not edited as source assets.
- The evaluation workflow may explicitly pass an external `skillRootPath` for manual upload or testing, but that package must still follow the same `manifest.json` + `SKILL.md` contract.

Current packages:

- `digital-employee-discovery`: hiring/discovery workflow
- `evaluation-expert`: AI evaluation workflow

Package rules:

1. Each system skill package lives in its own directory directly under this root or in a nested domain directory.
2. Each package must contain both `manifest.json` and `SKILL.md`.
3. `manifest.json` is the machine-readable contract for other modules.
4. `SKILL.md` remains the human-readable entry prompt and may reference sibling sub-skills.
5. Workflow stage rules must be declared in `manifest.json`, not hard-coded in consumers.

Minimal package layout:

```text
Assets/SystemSkills/<skill-id>/
  manifest.json
  SKILL.md
  <sub-skill-a>/SKILL.md
  <sub-skill-b>/SKILL.md
```

Manifest fields:

- `skill_id`: stable package id
- `display_name`: UI-friendly name
- `description`: short package description
- `version`: package version
- `level`: skill level, for example `system`
- `status`: package status, for example `active`
- `entry_skill`: runtime entry skill name
- `tags`: search tags
- `input_example`: representative invocation
- `output_example`: representative result
- `bound_templates`: template ids that explicitly bind to the package
- `stage_rules`: workflow stage metadata exposed to other modules

Non-source-of-truth copies, test fixtures, or exported workspace snapshots should not be edited as the primary package.
