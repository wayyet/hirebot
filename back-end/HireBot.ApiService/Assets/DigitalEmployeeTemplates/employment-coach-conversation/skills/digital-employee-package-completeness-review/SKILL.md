---
name: digital-employee-package-completeness-review
description: Use when evaluating a digital employee package, GoodCrew template, agent template, skill bundle, ontology slice package, or uploaded workspace package for completeness, installability, runtime readiness, or release acceptance
---

# Digital Employee Package Completeness Review

## Overview

Use this skill to evaluate whether a digital employee package is “complete, installable, runnable, and acceptable” (完整、可安装、可运行、可验收).

Core rule: **run the validator first, then manually review only what automation cannot decide.** Do not start with a hand-written checklist. The script catches path drift, installability failures, stale metadata projection paths, projection route errors, stale evaluation docs, missing `SKILL.md`, and basic rule/security red flags.

Validator script:

```text
scripts/validate_digital_employee_package.py
```

The package is complete only when both are true:

```text
automated report has no P0 blockers
+ manual review resolves semantic conflicts and business-rule ambiguity
```

## When to Use

Use when the user asks to:

- 评估数字员工模板
- 校验数字员工包完整性
- 验证 GoodCrew / NCrew 生成的 Agent 模板
- 检查 skill bundle 是否可安装
- 检查 ontology slice 和 skill 是否匹配
- 验收一个业务数字员工是否可发布
- 排查数字员工上传后 skill、本体、配置未生效
- 做 release 前模板质量检查

Do not use for:

- 单独评审一个普通 `SKILL.md`
- 单独评审普通业务文档
- 不含 `manifest.json`、`config/`、`skills/`、`ontology/` 的普通项目目录

## Required Input

You need one package root directory:

```text
<digital-employee-package>/
├── manifest.json
├── config/{AGENTS,SOUL,IDENTITY,MEMORY}.md
├── config/workspace.json                      (optional)
├── config/rule-patterns.json                  (optional)
├── ontology/*.{json,md}
├── skills/<skill-name>/{SKILL.md,SKILL.*.md,metadata.json,contracts/projections/**}
├── evaluation.md
└── evaluation/testcases.json or testcases/*.json
```

If the platform has custom upload/install rules, identify them before final judgment. For OpenClaw, ontology installability depends on uploader support for `.md` / `.json` under `ontology/`.

## Mandatory Workflow

### Step 1: Run the validator

From this skill directory:

```bash
python scripts/validate_digital_employee_package.py "<package-root>" --format markdown
```

For JSON output:

```bash
python scripts/validate_digital_employee_package.py "<package-root>" --format json
```

Write a report file:

```bash
python scripts/validate_digital_employee_package.py "<package-root>" --output package-completeness-report.md
```

Simulate platform-specific ontology install rules:

```bash
python scripts/validate_digital_employee_package.py "<package-root>" --ontology-extensions .md
python scripts/validate_digital_employee_package.py "<package-root>" --ontology-extensions .md,.json
```

Use `.md` only when validating older OpenClaw upload behavior. Use `.md,.json` when validating current packages that support JSON ontology slices.

### Step 2: Treat script output as the baseline

Read these fields first:

| Field | Meaning |
|---|---|
| `status` | `PASS`, `PASS_WITH_CONCERNS`, or `FAIL` |
| `release_readiness` | release-ready / beta-ready / not-production-ready / incomplete |
| `p0_blockers` | hard release blockers |
| `findings` | warnings and blockers |
| `skills` | per-skill status matrix |
| `score` | 10-dimension score |
| `recommended_fix_order` | fix order generated from findings |

Rules:

- Any `p0_blockers` means `FAIL`, even if average score is high.
- `PASS_WITH_CONCERNS` is not production-ready until warnings are accepted or fixed.
- Do not hide validator warnings. If a warning is acceptable, explain why.

### Step 3: Manually review automation blind spots

The script does not fully understand business semantics. Manually verify:

1. Business rule conflicts
   - Example: SOUL says唛头差异 is warning, ontology says阻断.
   - Example: testcases allow `0.01 KG`毛重差异 as warning while ontology requires exact equality.

2. Field-count claims
   - If docs claim 102/30/62/32 fields, require machine-readable field definitions or a traceable source digest.
   - Do not accept field counts based only on prose.

3. Workflow semantics
   - Inputs from one skill must be consumable by the next skill.
   - Human review must gate irreversible actions such as downstream push.
   - Amendment flow must re-enter validation before push.

4. Security and authority boundaries
   - No fabricated field values.
   - No secrets in chat, notification links, logs, test data, or reports.
   - No downstream push before human confirmation.
   - Audit logs exist for review, push, retry, and amendment.

5. Evaluation relevance
   - `evaluation.md` must match actual manifest-bound skills.
   - Test cases must cover happy path, missing data, conflict, compliance block, human reject, retry, and amendment.

### Step 4: Produce the final report

Use the script report as the structure. Add manual findings under the relevant sections.

Required final sections:

```markdown
# Digital Employee Package Completeness Review

## Verdict
Status:
Release readiness:
One-line summary:

## Automated Validator Result
- Command:
- Exit code:
- P0 blockers:
- Findings:
- Score average:

## Package Surface
[copy or summarize validator surface]

## P0 Blockers
[script P0 + manual P0]

## Skill Matrix
[script skill matrix + manual notes]

## Ontology and Projection Findings
[path, installability, projection, source_slice, field schema]

## Workflow Closure
[happy path and failure path]

## Rule Consistency
[severity conflicts and required decisions]

## Evaluation Coverage
[stale docs, missing cases, metrics]

## Security and Authority Boundaries
[human confirmation, secrets, audit]

## Score
[validator score + any manual adjustment]

## Recommended Fix Order
[highest impact first]
```

## Validator Checks

The Python validator currently checks:

| Area | Automated checks |
|---|---|
| package root | exists and is a directory |
| manifest | exists, valid JSON, identity fields, `entry_skill` resolves to existing file |
| config | declared config files and required config whitelist, optional `workspace.json` |
| ontology | manifest paths exist, extension installability, convention-mode .md/.json risk, JSON components, `NOT_RUN`, field-count-without-schema |
| skills | declared paths, `SKILL.md` (with fallback to `SKILL.zh.md` / `SKILL.en.md` / `SKILL.*.md`), frontmatter, `metadata.json` |
| metadata | stale `source_projection_paths` |
| projection contracts | `contract-index.json`, consumer match, default view, view paths, JSON parse, open questions, `source_slice.path` |
| evaluation | evaluation files, stale “no skills bound” text (Chinese + English) |
| workflow | workflow closure derived from manifest `stage_rules` skill names or `--expected-skills` CLI flag |
| rules | severity conflicts between SOUL.md and ontology (configurable via `config/rule-patterns.json`) |
| security | human confirmation and secret-boundary detection with expanded keyword/pattern matching |
| scoring | 10 dimensions and release readiness |

## Common Validator Findings

| Code | Meaning | Usual fix |
|---|---|---|
| `manifest.ontology.not_installable` | manifest ontology file exists but uploader would drop its extension | update uploader rules or change ontology file extension |
| `skill.metadata_projection_path.missing` | `metadata.json` points to stale projection path | point to `skills/<skill>/contracts/projections/...` or remove stale metadata |
| `projection.view_path.missing` | `contract-index.json` view path has no file | add projection file or fix index path |
| `projection.source_slice.unresolved` | projection `source_slice.path` cannot resolve | use package-root-relative or correct projection-relative path |
| `ontology.field_count_without_schema` | package claims field counts without field definitions | add machine-readable field catalog/schema |
| `evaluation.stale_skill_binding` | evaluation doc says no skills bound while manifest declares skills | update evaluation doc |
| `rule.<keyword>.severity_conflict` | inconsistent severity for a business term between SOUL.md (warning) and ontology (block) | choose one severity and update all sources |

Configurable rule patterns are defined in `config/rule-patterns.json` (see below). Without this file, the validator runs no rule checks.
| `security.secret_boundary.missing` | config lacks no-secrets boundary | add explicit credential/token prohibition |

## Rule Conflict Patterns (config/rule-patterns.json)

To enable business-rule conflict detection, add a `config/rule-patterns.json` file to the package:

```json
[
  {
    "keyword": "唛头",
    "warning_indicator": "警告",
    "block_indicator": "阻断",
    "code": "rule.shipping_marks.severity_conflict",
    "label": "shipping marks"
  }
]
```

Each entry defines:
- `keyword`: the business term to search for in both SOUL.md and ontology files
- `warning_indicator`: text that indicates a "warning" severity in SOUL.md
- `block_indicator`: text that indicates a "blocking" severity in ontology
- `code` (optional): finding code, defaults to `rule.<keyword>.severity_conflict`
- `label` (optional): human-readable label for the finding message

Without this file, no rule checks are performed.

## Release Verdict Rules

| Condition | Verdict |
|---|---|
| Any P0 blocker | FAIL / not-production-ready |
| No P0, many warnings | PASS_WITH_CONCERNS / beta-ready |
| No P0, warnings manually accepted | PASS_WITH_CONCERNS with acceptance notes |
| No P0, no meaningful warnings, workflow and tests complete | PASS / release-ready |

Never mark release-ready if:

- `manifest.json` paths cannot be installed.
- Required `SKILL.md` files are missing.
- Projection view files are missing.
- Business rule severity conflicts are unresolved.
- Human confirmation boundary is missing for downstream push.
- The package claims field coverage without field definitions and the core task depends on those fields.

## Quick Commands

Run validator tests after changing the script:

```bash
python tests/test_validate_digital_employee_package.py
```

Validate a package with explicit workflow skills and write a report:

```bash
python scripts/validate_digital_employee_package.py "<package-root>" \
  --expected-skills contract-parsing,field-mapping,three-doc-generation \
  --format markdown \
  --output package-completeness-report.md
```

Override expected workflow skills:

```bash
python scripts/validate_digital_employee_package.py "<package-root>" --expected-skills skill-a,skill-b,skill-c
```

Inspect summary as JSON:

```bash
python scripts/validate_digital_employee_package.py "<package-root>" --format json
```

## Common Mistakes

### Mistake: Starting with manual review

Wrong:

```text
Read manifest, inspect every skill by hand, then maybe run the tool.
```

Correct:

```text
Run scripts/validate_digital_employee_package.py first. Use manual review to explain and complete the script findings.
```

### Mistake: Trusting manifest paths without install rules

Wrong:

```text
manifest points to ontology/purchase-doc-domain.slice.json, so ontology is installed.
```

Correct:

```text
manifest path exists, then verify uploader accepts `.json` under `ontology/` or run with --ontology-extensions.
```

### Mistake: Treating validator warnings as noise

Wrong:

```text
The script says PASS_WITH_CONCERNS, but there are no P0 blockers, so ship.
```

Correct:

```text
List every warning. Mark each as fixed, accepted with reason, or release-blocking after manual review.
```

### Mistake: Accepting field-count claims without schemas

Wrong:

```text
The package says 102 fields, so field coverage is complete.
```

Correct:

```text
Require machine-readable field definitions or a traceable source digest for all claimed fields.
```

## Search Keywords

digital employee package, GoodCrew template, NCrew template, agent template, skill bundle, ontology slice, projection contract, contract-index.json, manifest.json, SKILL.md, metadata.json, config/AGENTS.md, SOUL.md, IDENTITY.md, MEMORY.md, evaluation/testcases.json, scripts/validate_digital_employee_package.py, --ontology-extensions, installability, runtime readiness, package completeness, template validation, skill completeness, ontology projection, business rule consistency, human review boundary, downstream push, audit log
