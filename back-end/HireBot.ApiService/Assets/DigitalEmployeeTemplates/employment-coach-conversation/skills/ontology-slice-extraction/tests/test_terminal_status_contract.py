import unittest
from pathlib import Path


SKILL_ROOT = Path(__file__).resolve().parents[1]


def read_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


class OntologySliceExtractionTerminalStatusTests(unittest.TestCase):
    def test_done_artifact_has_completed_and_blocked_terminal_shapes(self) -> None:
        skill = read_text(SKILL_ROOT / "SKILL.md")

        for phrase in [
            '"status": "completed"',
            '"status": "blocked"',
            '"diagnostic": "insufficient_material"',
            "仍必须发出 `ontology_slice_extraction_done`",
            "不得提示进入技能定义",
        ]:
            self.assertIn(phrase, skill)

    def test_old_no_done_on_insufficient_material_rule_is_removed(self) -> None:
        skill = read_text(SKILL_ROOT / "SKILL.md")

        self.assertNotIn("不发 `ontology_slice_extraction_done`", skill)
        self.assertNotIn("改为在对话中向用户说明缺口", skill)


if __name__ == "__main__":
    unittest.main()
