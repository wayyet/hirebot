import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = (
    Path(__file__).resolve().parents[1]
    / "scripts"
    / "validate_digital_employee_package.py"
)


def load_validator_module():
    spec = importlib.util.spec_from_file_location(
        "validate_digital_employee_package", SCRIPT_PATH
    )
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class DigitalEmployeePackageValidatorTests(unittest.TestCase):
    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def write_json(self, path: Path, data: dict) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8"
        )

    def write_text(self, path: Path, text: str = "ok") -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def create_minimal_package(self, root: Path) -> None:
        """Create a clean, correct minimal package that should PASS validation."""
        self.write_json(root / "manifest.json", {
            "name": "demo-agent",
            "display_name": "Demo Agent",
            "version": "1.0.0",
            "description": "demo",
            "entry_skill": "skills/field-mapping/SKILL.md",
            "config": {
                "agents": "config/AGENTS.md",
                "soul": "config/SOUL.md",
                "identity": "config/IDENTITY.md",
                "memory": "config/MEMORY.md",
            },
            "ontology_slices": [
                {
                    "name": "demo-domain",
                    "path": "ontology/demo-domain.slice.json",
                    "required": True,
                }
            ],
            "skills": [
                {
                    "name": "field-mapping",
                    "path": "skills/field-mapping/SKILL.md",
                    "required": True,
                }
            ],
            "stage_rules": [
                {
                    "stage": "material",
                    "skill_name": "field-mapping",
                    "description": "Map fields from source to target.",
                }
            ],
        })

        # Config files with security boundaries
        for name in ["AGENTS.md", "SOUL.md", "IDENTITY.md", "MEMORY.md"]:
            text = "# config\n"
            if name in ("SOUL.md", "IDENTITY.md"):
                text += "\n人工确认 required for downstream push.\n"
                text += "Never expose API key or 凭据 in chat or logs.\n"
            self.write_text(root / "config" / name, text)

        # Workspace config
        self.write_json(root / "config" / "workspace.json", {
            "workspace_root": "/workspace",
            "skills_root": "/workspace/skills",
        })

        # Ontology slice
        self.write_json(root / "ontology/demo-domain.slice.json", {
            "scope": {"in_scope": [], "out_of_scope": []},
            "sources": [],
            "concepts": [],
            "relations": [],
            "constraints": [],
            "ambiguities": [],
            "meta": {"validation": "READY"},
        })

        # Skill with SKILL.md
        self.write_text(
            root / "skills/field-mapping/SKILL.md",
            "---\nname: field-mapping\ndescription: Use when mapping fields\n---\n# Field Mapping\n",
        )

        # metadata.json with projection sources pointing to the actual location
        self.write_json(root / "skills/field-mapping/metadata.json", {
            "name": "field-mapping",
            "sources": [
                {
                    "type": "projection",
                    "source_projection_paths": [
                        "skills/field-mapping/contracts/projections/ontology_extraction/demo-domain/demo-domain.workflow-contract.projection.json"
                    ],
                }
            ],
        })

        # Contract index + projection (clean, no stale paths)
        self.write_json(
            root
            / "skills/field-mapping/contracts/projections/ontology_extraction/contract-index.json",
            {
                "producer_skill": "ontology_extraction",
                "consumer_skill": "field-mapping",
                "default_selection_policy": {
                    "prefer_ready_only": True,
                    "block_on_open_questions": True,
                },
                "topics": [
                    {
                        "domain_slug": "demo-domain",
                        "default_target_view": "workflow-contract",
                        "views": [
                            {
                                "target_view": "workflow-contract",
                                "status": "READY",
                                "path": "demo-domain/demo-domain.workflow-contract.projection.json",
                            }
                        ],
                    }
                ],
            },
        )
        self.write_json(
            root
            / "skills/field-mapping/contracts/projections/ontology_extraction/demo-domain/demo-domain.workflow-contract.projection.json",
            {
                "projection_type": "workflow_contract_projection",
                "source_slice": {
                    "path": "ontology/demo-domain.slice.json",
                    "topic": "demo-domain",
                },
                "intended_consumers": ["field-mapping"],
                "open_questions": [],
            },
        )

        # Evaluation files
        self.write_text(root / "evaluation.md", "# Evaluation Report\nSkills are bound and validated.\n")
        self.write_json(root / "evaluation/testcases.json", {"test_cases": []})

    # ------------------------------------------------------------------
    # PASS case
    # ------------------------------------------------------------------

    def test_clean_minimal_package_passes(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            report = validator.validate_package(root)

            self.assertEqual("PASS", report["status"])
            self.assertEqual(0, len(report["p0_blockers"]))

    # ------------------------------------------------------------------
    # Package root
    # ------------------------------------------------------------------

    def test_missing_package_root(self):
        validator = load_validator_module()
        report = validator.validate_package("/nonexistent/path/12345")
        self.assertEqual("FAIL", report["status"])
        self.assertIn(
            "package_root.missing",
            {f["code"] for f in report["p0_blockers"]},
        )

    # ------------------------------------------------------------------
    # Manifest
    # ------------------------------------------------------------------

    def test_missing_manifest(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            report = validator.validate_package(root)
            self.assertIn(
                "manifest.missing",
                {f["code"] for f in report["p0_blockers"]},
            )

    def test_invalid_manifest_json(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "manifest.json").write_text("{not valid json}", encoding="utf-8")
            report = validator.validate_package(root)
            self.assertIn(
                "manifest.invalid_json",
                {f["code"] for f in report["p0_blockers"]},
            )

    def test_manifest_missing_identity_fields(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.write_json(root / "manifest.json", {"name": "bare"})
            report = validator.validate_package(root)
            codes = {f["code"] for f in report["findings"]}
            self.assertTrue(
                any(c.startswith("manifest.identity.") for c in codes)
            )

    # ------------------------------------------------------------------
    # entry_skill
    # ------------------------------------------------------------------

    def test_entry_skill_missing(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Remove entry_skill
            manifest = json.loads(
                (root / "manifest.json").read_text(encoding="utf-8")
            )
            del manifest["entry_skill"]
            self.write_json(root / "manifest.json", manifest)

            report = validator.validate_package(root)
            self.assertIn(
                "manifest.entry_skill.missing",
                {f["code"] for f in report["findings"]},
            )

    def test_entry_skill_unresolved(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Point entry_skill to non-existent file
            manifest = json.loads(
                (root / "manifest.json").read_text(encoding="utf-8")
            )
            manifest["entry_skill"] = "skills/nonexistent/SKILL.md"
            self.write_json(root / "manifest.json", manifest)

            report = validator.validate_package(root)
            self.assertIn(
                "manifest.entry_skill.unresolved",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Ontology
    # ------------------------------------------------------------------

    def test_manifest_ontology_not_installable(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            report = validator.validate_package(root, ontology_extensions={".md"})

            codes = {f["code"] for f in report["p0_blockers"]}
            self.assertIn("manifest.ontology.not_installable", codes)
            self.assertEqual("FAIL", report["status"])

    def test_json_ontology_accepted_with_json_extensions(self):
        """JSON ontology slices should be accepted when .json is in ontology_extensions."""
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            report = validator.validate_package(
                root, ontology_extensions={".md", ".json"}
            )

            codes = {f["code"] for f in report["p0_blockers"]}
            self.assertNotIn("manifest.ontology.not_installable", codes)

    def test_manifest_ontology_missing_path(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Point to non-existent ontology
            manifest = json.loads(
                (root / "manifest.json").read_text(encoding="utf-8")
            )
            manifest["ontology_slices"][0]["path"] = "ontology/nonexistent.slice.json"
            self.write_json(root / "manifest.json", manifest)

            report = validator.validate_package(root)
            self.assertIn(
                "manifest.ontology.missing",
                {f["code"] for f in report["p0_blockers"]},
            )

    # ------------------------------------------------------------------
    # Config
    # ------------------------------------------------------------------

    def test_workspace_json_missing_warns(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            (root / "config" / "workspace.json").unlink()

            report = validator.validate_package(root)
            self.assertIn(
                "config.optional_file.missing",
                {f["code"] for f in report["findings"]},
            )

    def test_config_file_missing(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            (root / "config" / "SOUL.md").unlink()

            report = validator.validate_package(root)
            self.assertIn(
                "config.file.missing",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Skills
    # ------------------------------------------------------------------

    def test_skill_md_missing(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            (root / "skills/field-mapping/SKILL.md").unlink()

            report = validator.validate_package(root)
            self.assertIn(
                "skill.skill_md.missing",
                {f["code"] for f in report["p0_blockers"]},
            )
            self.assertEqual("FAIL", report["skills"]["field-mapping"]["status"])

    def test_skill_zh_md_fallback(self):
        """SKILL.zh.md should be accepted as a valid SKILL.md variant."""
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Replace SKILL.md with SKILL.zh.md
            (root / "skills/field-mapping/SKILL.md").unlink()
            self.write_text(
                root / "skills/field-mapping/SKILL.zh.md",
                "---\nname: field-mapping\ndescription: 字段映射\n---\n# 字段映射\n",
            )

            report = validator.validate_package(root)

            # Should NOT have skill_md_missing
            codes = {f["code"] for f in report["p0_blockers"]}
            self.assertNotIn("skill.skill_md.missing", codes)
            self.assertEqual("PASS", report["skills"]["field-mapping"]["status"])

    def test_skill_frontmatter_missing(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            (root / "skills/field-mapping/SKILL.md").write_text(
                "# No frontmatter here\n", encoding="utf-8"
            )

            report = validator.validate_package(root)
            self.assertIn(
                "skill.skill_md.frontmatter_missing",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Metadata / Projection
    # ------------------------------------------------------------------

    def test_stale_metadata_projection_paths(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Replace metadata with a stale projection path
            self.write_json(root / "skills/field-mapping/metadata.json", {
                "name": "field-mapping",
                "sources": [
                    {
                        "type": "projection",
                        "source_projection_paths": [
                            "ontology/projections/field-mapping/stale.projection.json"
                        ],
                    }
                ],
            })

            report = validator.validate_package(
                root, ontology_extensions={".md", ".json"}
            )

            codes = {f["code"] for f in report["findings"]}
            self.assertIn("skill.metadata_projection_path.missing", codes)
            skill = report["skills"]["field-mapping"]
            self.assertEqual("PASS_WITH_CONCERNS", skill["status"])

    def test_contract_index_view_path_missing(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            missing_proj = (
                root
                / "skills/field-mapping/contracts/projections/ontology_extraction/demo-domain/demo-domain.workflow-contract.projection.json"
            )
            missing_proj.unlink()

            report = validator.validate_package(
                root, ontology_extensions={".md", ".json"}
            )

            codes = {f["code"] for f in report["p0_blockers"]}
            self.assertIn("projection.view_path.missing", codes)
            self.assertEqual("FAIL", report["status"])

    def test_projection_consumer_mismatch(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Change consumer_skill in contract-index
            index_path = (
                root
                / "skills/field-mapping/contracts/projections/ontology_extraction/contract-index.json"
            )
            index = json.loads(index_path.read_text(encoding="utf-8"))
            index["consumer_skill"] = "other-skill"
            self.write_json(index_path, index)

            report = validator.validate_package(root)

            self.assertIn(
                "projection.consumer_mismatch",
                {f["code"] for f in report["findings"]},
            )

    def test_projection_open_questions_blocks_release(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            proj_path = (
                root
                / "skills/field-mapping/contracts/projections/ontology_extraction/demo-domain/demo-domain.workflow-contract.projection.json"
            )
            proj = json.loads(proj_path.read_text(encoding="utf-8"))
            proj["open_questions"] = ["Is this correct?"]
            self.write_json(proj_path, proj)

            report = validator.validate_package(root)

            self.assertIn(
                "projection.open_questions.present",
                {f["code"] for f in report["p0_blockers"]},
            )

    def test_projection_source_slice_unresolved(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            proj_path = (
                root
                / "skills/field-mapping/contracts/projections/ontology_extraction/demo-domain/demo-domain.workflow-contract.projection.json"
            )
            proj = json.loads(proj_path.read_text(encoding="utf-8"))
            proj["source_slice"]["path"] = "../../../nonexistent/slice.json"
            self.write_json(proj_path, proj)

            report = validator.validate_package(root)

            self.assertIn(
                "projection.source_slice.unresolved",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Workflow closure
    # ------------------------------------------------------------------

    def test_workflow_closure_with_expected_skills(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            report = validator.validate_package(
                root, expected_skills=["field-mapping", "missing-skill"]
            )

            self.assertIn(
                "workflow.expected_skill.missing",
                {f["code"] for f in report["findings"]},
            )

    def test_workflow_closure_derived_from_stage_rules(self):
        """When no --expected-skills is given, workflow closure uses stage_rules."""
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Add a stage_rule referencing a non-existent skill
            manifest = json.loads(
                (root / "manifest.json").read_text(encoding="utf-8")
            )
            manifest["stage_rules"].append({
                "stage": "external",
                "skill_name": "external-config",
                "description": "External config stage.",
            })
            self.write_json(root / "manifest.json", manifest)

            report = validator.validate_package(root)

            self.assertIn(
                "workflow.expected_skill.missing",
                {f["code"] for f in report["findings"]},
            )

    def test_workflow_closure_skipped_when_no_stage_rules(self):
        """Without stage_rules and without --expected-skills, workflow check is skipped."""
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Remove stage_rules
            manifest = json.loads(
                (root / "manifest.json").read_text(encoding="utf-8")
            )
            del manifest["stage_rules"]
            self.write_json(root / "manifest.json", manifest)

            report = validator.validate_package(root)

            codes = {f["code"] for f in report["findings"]}
            self.assertFalse(
                any(c.startswith("workflow.") for c in codes),
                "workflow checks should be skipped when no stage_rules or expected_skills",
            )

    # ------------------------------------------------------------------
    # Rule consistency
    # ------------------------------------------------------------------

    def test_rule_consistency_no_patterns_no_warnings(self):
        """Without rule-patterns.json, no rule findings should appear."""
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            report = validator.validate_package(root)

            codes = {f["code"] for f in report["findings"]}
            self.assertFalse(
                any(c.startswith("rule.") for c in codes),
                "no rule checks when no rule-patterns.json",
            )

    def test_rule_consistency_detects_conflict(self):
        """With a rule-patterns.json, severity conflicts are detected."""
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            # Add a rule-patterns.json
            self.write_json(root / "config" / "rule-patterns.json", [
                {
                    "keyword": "testterm",
                    "warning_indicator": "warning",
                    "block_indicator": "block",
                    "code": "rule.testterm.severity_conflict",
                    "label": "test term",
                }
            ])

            # SOUL.md already has generic text; add specific keywords
            soul_path = root / "config" / "SOUL.md"
            soul_path.write_text(
                "# SOUL\ntestterm should be a warning.\n人工确认 required.\nNever expose API key.\n",
                encoding="utf-8",
            )

            # Ontology has block indicator
            ont_path = root / "ontology/demo-domain.slice.json"
            ont = json.loads(ont_path.read_text(encoding="utf-8"))
            ont["constraints"] = ["testterm is a block condition"]
            self.write_json(ont_path, ont)

            report = validator.validate_package(root)

            self.assertIn(
                "rule.testterm.severity_conflict",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Security boundaries
    # ------------------------------------------------------------------

    def test_human_confirmation_detected(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            report = validator.validate_package(root)

            codes = {f["code"] for f in report["findings"]}
            self.assertNotIn(
                "security.human_confirmation.missing", codes,
                "SOUL.md and IDENTITY.md contain 人工确认",
            )

    def test_human_confirmation_missing_detected(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Strip security language from config
            for fname in ["SOUL.md", "IDENTITY.md"]:
                (root / "config" / fname).write_text("# generic\n", encoding="utf-8")

            report = validator.validate_package(root)

            self.assertIn(
                "security.human_confirmation.missing",
                {f["code"] for f in report["findings"]},
            )

    def test_secret_boundary_detected(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)

            report = validator.validate_package(root)

            codes = {f["code"] for f in report["findings"]}
            self.assertNotIn(
                "security.secret_boundary.missing", codes,
                "SOUL.md contains 'API key' and '凭据'",
            )

    def test_secret_boundary_missing_detected(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            for fname in ["SOUL.md", "IDENTITY.md"]:
                (root / "config" / fname).write_text("# generic\n", encoding="utf-8")

            report = validator.validate_package(root)

            self.assertIn(
                "security.secret_boundary.missing",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Evaluation
    # ------------------------------------------------------------------

    def test_evaluation_stale_skill_binding_chinese(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            (root / "evaluation.md").write_text(
                "# 评估\n当前还没有绑定技能。\n", encoding="utf-8"
            )

            report = validator.validate_package(root)

            self.assertIn(
                "evaluation.stale_skill_binding",
                {f["code"] for f in report["findings"]},
            )

    def test_evaluation_stale_skill_binding_english(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            (root / "evaluation.md").write_text(
                "# Evaluation\nNo skills bound yet.\n", encoding="utf-8"
            )

            report = validator.validate_package(root)

            self.assertIn(
                "evaluation.stale_skill_binding",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Ontology (file-level)
    # ------------------------------------------------------------------

    def test_ontology_validation_not_run(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            ont_path = root / "ontology/demo-domain.slice.json"
            ont = json.loads(ont_path.read_text(encoding="utf-8"))
            ont["meta"]["validation"] = "NOT_RUN"
            self.write_json(ont_path, ont)

            report = validator.validate_package(root)

            self.assertIn(
                "ontology.validation.not_run",
                {f["code"] for f in report["findings"]},
            )

    def test_ontology_field_count_without_schema(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            ont_path = root / "ontology/demo-domain.slice.json"
            ont = json.loads(ont_path.read_text(encoding="utf-8"))
            ont["description"] = "This domain defines 102 个字段 for purchase docs."
            self.write_json(ont_path, ont)

            report = validator.validate_package(root)

            self.assertIn(
                "ontology.field_count_without_schema",
                {f["code"] for f in report["findings"]},
            )

    # ------------------------------------------------------------------
    # Manifest skill path mismatch
    # ------------------------------------------------------------------

    def test_manifest_skill_path_mismatch(self):
        validator = load_validator_module()
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            self.create_minimal_package(root)
            # Change manifest skill path to point elsewhere
            manifest = json.loads(
                (root / "manifest.json").read_text(encoding="utf-8")
            )
            manifest["skills"][0]["path"] = "skills/field-mapping/other.md"
            self.write_json(root / "manifest.json", manifest)
            self.write_text(
                root / "skills/field-mapping/other.md",
                "---\nname: field-mapping\n---\n# alt\n",
            )

            report = validator.validate_package(root)

            self.assertIn(
                "manifest.skill.path_mismatch",
                {f["code"] for f in report["findings"]},
            )


if __name__ == "__main__":
    unittest.main()
