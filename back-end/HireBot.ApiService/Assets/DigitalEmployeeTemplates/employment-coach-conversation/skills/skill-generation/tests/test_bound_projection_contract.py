import json
import unittest
from pathlib import Path


SKILL_ROOT = Path(__file__).resolve().parents[1]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class BoundProjectionContractTests(unittest.TestCase):
    def test_skill_generation_requires_bound_projection_contracts(self) -> None:
        skill = read_text(SKILL_ROOT / "SKILL.md")

        for phrase in [
            "本 skill 只支持该绑定模式",
            "Contract Check（强制）",
            "Contract Check 失败必须阻断本轮 `skill-generation`",
            "不得发出 `skill_generation_done`",
            "不得声明“基础 skill 已成功生成”",
            "不得把只有 Step 1 基础文件的结果视为成功",
            "open_questions` 非空只表示源 projection 为 WARNING",
        ]:
            self.assertIn(phrase, skill)

    def test_unbound_or_optional_projection_language_is_removed(self) -> None:
        files = [
            SKILL_ROOT / "SKILL.md",
            SKILL_ROOT / "metadata.json",
            SKILL_ROOT / "references" / "generated-skill-template.md",
            SKILL_ROOT / "references" / "projection-contract-template.md",
            SKILL_ROOT / "references" / "quality-checklist.md",
        ]
        combined = "\n".join(read_text(path) for path in files)

        for forbidden in [
            "未绑定模式",
            "未要求绑定 projection",
            "Projection 契约生成（可选）",
            "optional contracts",
            "optional_projection_artifact",
            "write_draft_notes_without_blocking_base_skill",
            "do not block the base skill write",
            "不阻断基础业务 skill 落盘",
            "不影响已通过 Base File Check",
        ]:
            self.assertNotIn(forbidden, combined)

    def test_metadata_declares_required_projection_policy(self) -> None:
        metadata = json.loads(read_text(SKILL_ROOT / "metadata.json"))

        self.assertEqual(
            metadata["quality_gates"]["projection_contract_policy"],
            "required_bound_projection_contract_warning_on_open_questions_block_on_missing_invalid_unwritable_or_slug_mismatch",
        )

        render_capability = next(
            capability
            for capability in metadata["capabilities"]
            if capability["id"] == "render_and_validate_skill_package"
        )
        self.assertIn("required consumer contracts", render_capability["goal"])
        self.assertIn(
            "contracts/projections/ontology_extraction/contract-index.json",
            render_capability["outputs"],
        )


if __name__ == "__main__":
    unittest.main()
