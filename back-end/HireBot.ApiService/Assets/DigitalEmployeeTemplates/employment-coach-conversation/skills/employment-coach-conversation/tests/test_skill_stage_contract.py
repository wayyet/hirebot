import json
import unittest
from pathlib import Path


SKILL_ROOT = Path(__file__).resolve().parents[1]
TEMPLATE_ROOT = SKILL_ROOT.parents[1]

SKILL_STAGE_SEQUENCE = [
    "skill_definition_ready",
    "skill_workorder_summary",
    "ontology_projection_ready",
    "skill_generation_ready",
]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def assert_tokens_in_order(test_case: unittest.TestCase, text: str, tokens: list[str], source: str) -> None:
    positions: list[int] = []
    for token in tokens:
        position = text.find(token)
        test_case.assertGreaterEqual(position, 0, f"{token} missing from {source}")
        positions.append(position)

    test_case.assertEqual(positions, sorted(positions), f"tokens are out of order in {source}")


class SkillStageContractTests(unittest.TestCase):
    def test_artifact_contract_declares_three_explicit_skill_gates_in_order(self) -> None:
        artifacts_path = SKILL_ROOT / "contracts" / "artifacts.json"
        contract = json.loads(read_text(artifacts_path))
        stage2 = next(stage for stage in contract["stages"] if stage["name"] == "stage2_skill")
        artifact_types = [artifact["type"] for artifact in stage2["artifacts"]]

        assert_tokens_in_order(self, "\n".join(artifact_types), SKILL_STAGE_SEQUENCE, str(artifacts_path))

        for gate in ("skill_definition_ready", "ontology_projection_ready", "skill_generation_ready"):
            artifact = next(item for item in stage2["artifacts"] if item["type"] == gate)
            self.assertFalse(artifact["terminal"], f"{gate} must remain a non-terminal confirmation gate")

    def test_skill_stage_schema_and_handoff_registry_keep_the_same_gate_order(self) -> None:
        stage_schema = read_text(SKILL_ROOT / "references" / "stage-data-schema.md")
        handoff_registry = read_text(SKILL_ROOT / "references" / "downstream-handoff-registry.md")

        assert_tokens_in_order(self, stage_schema, SKILL_STAGE_SEQUENCE, "stage-data-schema.md")
        assert_tokens_in_order(self, handoff_registry, SKILL_STAGE_SEQUENCE, "downstream-handoff-registry.md")

    def test_agents_and_manifest_do_not_reintroduce_single_gate_or_auto_skill_generation_wording(self) -> None:
        combined = "\n".join([
            read_text(TEMPLATE_ROOT / "config" / "AGENTS.md"),
            read_text(TEMPLATE_ROOT / "manifest.json"),
        ])
        forbidden_phrases = [
            "阶段 2 唯一确认门",
            "唯一的用户确认门",
            "projection 后自动",
            "projection 完成后自动",
            "projection 后直接",
            "projection 完成后直接",
        ]

        for phrase in forbidden_phrases:
            self.assertNotIn(phrase, combined)


if __name__ == "__main__":
    unittest.main()
