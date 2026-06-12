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
    search_from = 0
    for token in tokens:
        position = text.find(token, search_from)
        test_case.assertGreaterEqual(position, 0, f"{token} missing from {source}")
        positions.append(position)
        search_from = position + len(token)

    test_case.assertEqual(positions, sorted(positions), f"tokens are out of order in {source}")


def section_between(text: str, start_token: str, end_token: str) -> str:
    start = text.find(start_token)
    if start < 0:
        raise AssertionError(f"{start_token} missing")

    end = text.find(end_token, start)
    if end < 0:
        raise AssertionError(f"{end_token} missing")

    return text[start:end]


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

    def test_material_to_skill_transition_requires_machine_signals_before_skill_dialog(self) -> None:
        skill = read_text(SKILL_ROOT / "SKILL.md")
        handoff_registry = read_text(SKILL_ROOT / "references" / "downstream-handoff-registry.md")
        transition_gate = section_between(skill, "跨阶段硬门", "详细字段协议")
        stage1_closure = section_between(skill, "阶段 1 完成后的强制动作", "### 阶段 2：技能")

        assert_tokens_in_order(self, transition_gate, [
            "material_handoff_summary",
            "ontology-slice-extraction",
            "ontology_slice_extraction_done",
            "skill_workorder_progress",
        ], "SKILL.md 跨阶段硬门")

        required_phrases = [
            "资料收集开始前必须通过 `load_skill` 加载 `ontology-slice-extraction`",
            "自然语言只能解释进展，不能替代阶段完成事件",
            "禁止出现\"对话已经进入技能阶段，但右侧 UI 仍停留在资料阶段\"的状态分叉",
            "资料阶段整体完成条件",
            "给出技能建议",
            "收到 `ontology_slice_extraction_done` 后，才读取 S1",
            "任何具体技能建议或技能反问，必须排在 `ontology_slice_extraction_done` 之后",
            "R1 属于资料阶段，S1 只能在 R1 的 `ontology_slice_extraction_done` 到达后执行",
            "披露的技能与参考文件（LLM 必须在 `ontology_slice_extraction_done` 后读取）",
        ]
        for phrase in required_phrases:
            self.assertIn(phrase, "\n".join([skill, handoff_registry]))

        self.assertIn("只说明阻断原因并留在资料阶段", transition_gate)
        self.assertIn("只说明缺口并继续资料阶段", stage1_closure)

    def test_material_stage_loads_ontology_skill_before_collecting_materials(self) -> None:
        skill = read_text(SKILL_ROOT / "SKILL.md")
        flow_constraints = read_text(SKILL_ROOT / "references" / "flow-constraints.md")
        stage_schema = read_text(SKILL_ROOT / "references" / "stage-data-schema.md")
        emit_protocol = read_text(SKILL_ROOT / "references" / "emit-artifact-protocol.md")

        startup = section_between(skill, "#### 步骤 4：通知用户开场 + 进入阶段 1", "#### ⛔ 路径反伪造红线")
        assert_tokens_in_order(self, startup, [
            "material_collection_progress",
            "调用 `load_skill`",
            "ontology-slice-extraction",
            "资料收集开始前预加载到上下文",
            "邀请用户开始介绍业务场景或直接上传资料",
        ], "SKILL.md stage1 startup")

        routing = section_between(skill, "资料阶段的强制预加载", "### 各阶段入场需加载的 skill")
        self.assertIn("发出 `material_handoff_summary` 或构造 R1 内部触发块前", routing)
        self.assertIn("若上下文曾被裁剪，立即重新调用 `load_skill`", routing)

        combined = "\n".join([flow_constraints, stage_schema, emit_protocol])
        for phrase in [
            "资料收集开始前，是否已调用 `load_skill` 加载 `ontology-slice-extraction`",
            "资料阶段整体完成还需要收到 `ontology_slice_extraction_done`",
            "在 `ontology_slice_extraction_done` 到达前，不得发 `skill_workorder_progress`",
        ]:
            self.assertIn(phrase, combined)

    def test_skill_implementation_subflow_keeps_three_user_confirmation_gates(self) -> None:
        skill = read_text(SKILL_ROOT / "SKILL.md")
        handoff_registry = read_text(SKILL_ROOT / "references" / "downstream-handoff-registry.md")
        emit_protocol = read_text(SKILL_ROOT / "references" / "emit-artifact-protocol.md")
        stage_schema = read_text(SKILL_ROOT / "references" / "stage-data-schema.md")
        flow_constraints = read_text(SKILL_ROOT / "references" / "flow-constraints.md")
        combined = "\n".join([skill, handoff_registry, emit_protocol, stage_schema, flow_constraints])

        for text, source in [
            (skill, "SKILL.md"),
            (handoff_registry, "downstream-handoff-registry.md"),
            (emit_protocol, "emit-artifact-protocol.md"),
            (stage_schema, "stage-data-schema.md"),
            (flow_constraints, "flow-constraints.md"),
        ]:
            self.assertIn("技能实现子流程", text, f"{source} must name the skill implementation subflow")

        assert_tokens_in_order(self, combined, [
            "skill_definition_ready",
            "skill_workorder_summary",
            "ontology_projection_ready",
            "skill_generation_ready",
        ], "skill implementation confirmation flow")

        required_phrases = [
            "三个显式确认门",
            "用户确认后，才按 R2 触发匹配技能数据",
            "匹配技能数据已完成，等待用户确认是否开始生成技能实现",
            "不得向用户暴露 `slice`、`projection`、`projection_paths`、R1/R2/R3、结构化文件等内部术语",
            "你只要回我一句",
        ]
        for phrase in required_phrases:
            self.assertIn(phrase, combined)

        forbidden_phrases = [
            "立即自动触发 R2 projection pass",
            "无需 `ontology_projection_ready` 确认门",
            "匹配技能数据：技能定义完成后**自动触发**",
            "技能阶段有两个显式确认门",
            "projection pass 自动启动",
        ]
        for phrase in forbidden_phrases:
            self.assertNotIn(phrase, combined)

    def test_package_root_terms_distinguish_coach_runtime_from_employee_package_root(self) -> None:
        skill = read_text(SKILL_ROOT / "SKILL.md")
        sandbox_path_facts = section_between(skill, "沙箱真实路径事实", "#### 步骤 1")
        path_guard = section_between(skill, "路径反伪造红线", "#### 失败兜底")
        packaging_rules = section_between(skill, "### 3. 调用打包工具", "**正确示例")

        self.assertIn("coach_runtime_root", sandbox_path_facts)
        self.assertIn("employee_package_root", sandbox_path_facts)
        self.assertIn("/workspace/<template_slug>-<yyyymmddHHmmss>", sandbox_path_facts)
        self.assertIn("绝不能作为本次数字员工的工作目录、manifest 同步目录、审查目录或打包目录", sandbox_path_facts)

        self.assertIn("workspace_root", path_guard)
        self.assertIn("打包根目录", path_guard)
        self.assertIn("会把雇佣教练系统 skill 混入数字员工包", path_guard)
        self.assertIn("skills/employment-coach-conversation/SKILL.md", path_guard)

        for system_skill_path in [
            "skills/employment-coach-conversation/SKILL.md",
            "skills/ontology-slice-extraction/SKILL.md",
            "skills/skill-generation/SKILL.md",
        ]:
            self.assertIn(system_skill_path, packaging_rules)

        self.assertIn("cd \"<employee_package_root>\"", packaging_rules)
        self.assertIn("不得继续打包", packaging_rules)

    def test_agents_and_manifest_do_not_reintroduce_single_gate_or_auto_skill_generation_wording(self) -> None:
        combined = "\n".join([
            read_text(TEMPLATE_ROOT / "config" / "AGENTS.md"),
            read_text(TEMPLATE_ROOT / "manifest.json"),
        ])
        required_phrases = [
            "资料收集开始前必须先通过 `load_skill` 加载该 skill",
            "等待 `ontology_slice_extraction_done`，随后才允许进入技能定义",
            "资料收口后先驱动 ontology-slice-extraction，并等待 ontology_slice_extraction_done 后才进入技能定义",
            "完成资料收口，随后触发 R1 并等待 ontology_slice_extraction_done 后才进入技能阶段",
        ]
        forbidden_phrases = [
            "阶段 2 唯一确认门",
            "唯一的用户确认门",
            "projection 后自动",
            "projection 完成后自动",
            "projection 后直接",
            "projection 完成后直接",
            "通过 emit_artifact（material_handoff_summary, isTerminal: true）完成阶段",
        ]

        for phrase in required_phrases:
            self.assertIn(phrase, combined)

        for phrase in forbidden_phrases:
            self.assertNotIn(phrase, combined)


if __name__ == "__main__":
    unittest.main()
