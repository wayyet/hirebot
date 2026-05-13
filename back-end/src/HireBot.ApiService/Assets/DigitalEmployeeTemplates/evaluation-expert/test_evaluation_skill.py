#!/usr/bin/env python3
"""
evaluation-expert/skills 本地测试脚本
模拟 ncrew 平台加载和执行 SKILL.md 的逻辑

使用方式：
    python test_evaluation_skill.py --mode mock
    python test_evaluation_skill.py --mode real --api-key YOUR_KEY
"""

import argparse
import json
import os
import re
import sys
from datetime import datetime
from pathlib import Path
from typing import Any, Dict, List, Optional

# ============================================
# Mock数据 - 模拟目标员工执行过程
# ============================================

MOCK_TEST_CASE = {
    "test_case_id": "TC-001",
    "scenario_name": "电商商品质量申诉处理",
    "scenario_info": {
        "job_description": "处理用户申诉，安抚情绪，规范处理工单，闭环反馈",
        "required_skills": ["情绪安抚", "工单处理", "流程合规", "问题解决"]
    },
    "input": {
        "user_request": "我买的商品质量有问题，要求退货！",
        "context": {
            "order_id": "ORD-12345",
            "product_name": "某某商品",
            "purchase_date": "2026-03-15"
        }
    },
    "expected_behavior_sequence": [
        {"step": 1, "action": "安抚用户情绪", "criteria": "语气友好，表达同理心", "required_tools": []},
        {"step": 2, "action": "收集问题信息", "criteria": "确认商品问题类型、订单信息", "required_tools": ["query_order"]},
        {"step": 3, "action": "判断退货条件", "criteria": "根据退货政策判断", "required_tools": ["check_return_policy"]},
        {"step": 4, "action": "处理退货申请", "criteria": "若符合条件则同意退货", "required_tools": []},
        {"step": 5, "action": "登记工单", "criteria": "创建退货工单", "required_tools": ["create_ticket"]},
        {"step": 6, "action": "闭环确认", "criteria": "告知后续流程，确认满意度", "required_tools": []}
    ],
    "expected_output": {
        "resolution": "问题已处理，退货申请已登记",
        "user_satisfaction": "用户满意",
        "artifacts_created": ["退货工单"]
    },
    "evaluation_criteria": [
        {"dimension": "功能完整性", "weight": 0.25, "description": "是否完成所有必要步骤"},
        {"dimension": "交互质量", "weight": 0.35, "description": "语气友好度、安抚效果"},
        {"dimension": "流程合规", "weight": 0.10, "description": "按标准流程执行"},
        {"dimension": "问题解决", "weight": 0.30, "description": "问题得到解决"},
        {"dimension": "工具调用正确性", "weight": "dynamic", "description": "正确调用工具"}
    ]
}

# 模拟不同轮次的执行结果（用于Mock模式）
MOCK_EXECUTION_TRACES = [
    # 第1轮：存在严重问题
    {
        "iteration": 1,
        "logs": [
            {"type": "message", "role": "assistant", "content": "好的，我来帮您处理退货。"},
            {"type": "tool_call", "tool_name": "query_order", "success": True},
            {"type": "message", "role": "assistant", "content": "您的订单符合退货条件，我帮您办理退货。"},
            # 缺失：没有调用create_ticket
        ],
        "tool_calls_detail": [
            {"tool_name": "query_order", "called": True, "success": True},
            {"tool_name": "create_ticket", "called": False, "missing": True, "severity": "critical"}
        ],
        "tone_analysis": {"friendliness": 5, "empathy": 3, "professionalism": 7}
    },
    # 第2轮：有改进但仍有问题
    {
        "iteration": 2,
        "logs": [
            {"type": "message", "role": "assistant", "content": "我理解您的感受，商品质量问题确实让人困扰。我来帮您处理退货。"},
            {"type": "tool_call", "tool_name": "query_order", "success": True},
            {"type": "tool_call", "tool_name": "create_ticket", "success": True},  # 已修复
            {"type": "message", "role": "assistant", "content": "退货已登记，请等待审核。"}
        ],
        "tool_calls_detail": [
            {"tool_name": "query_order", "called": True, "success": True},
            {"tool_name": "create_ticket", "called": True, "success": True}
        ],
        "tone_analysis": {"friendliness": 7, "empathy": 6, "professionalism": 8}
    },
    # 第3轮：合格
    {
        "iteration": 3,
        "logs": [
            {"type": "message", "role": "assistant", "content": "非常理解您的困扰！商品质量问题确实让人着急，我马上为您处理退货。"},
            {"type": "tool_call", "tool_name": "query_order", "success": True},
            {"type": "tool_call", "tool_name": "create_ticket", "success": True},
            {"type": "message", "role": "assistant", "content": "退货申请已提交，3-5个工作日内完成审核退款。如有问题随时联系我们，感谢您的耐心！"}
        ],
        "tool_calls_detail": [
            {"tool_name": "query_order", "called": True, "success": True},
            {"tool_name": "create_ticket", "called": True, "success": True}
        ],
        "tone_analysis": {"friendliness": 9, "empathy": 9, "professionalism": 9}
    }
]

# ============================================
# SKILL.md 解析器 - 模拟ncrew加载Skill
# ============================================

def parse_skill_md(skill_path: str) -> Dict[str, Any]:
    """解析SKILL.md文件，提取frontmatter和内容"""
    with open(skill_path, 'r', encoding='utf-8') as f:
        content = f.read()

    # 解析YAML frontmatter
    frontmatter_match = re.match(r'^---\n(.*?)\n---\n(.*)$', content, re.DOTALL)
    if not frontmatter_match:
        raise ValueError(f"Invalid SKILL.md format: {skill_path}")

    frontmatter_raw = frontmatter_match.group(1)
    body = frontmatter_match.group(2)

    # 简单解析YAML（不依赖yaml库）
    frontmatter = {}
    for line in frontmatter_raw.split('\n'):
        if ':' in line:
            key, value = line.split(':', 1)
            frontmatter[key.strip()] = value.strip().strip('"').strip("'")

    return {
        "frontmatter": frontmatter,
        "body": body,
        "path": skill_path
    }

def load_all_skills(skill_dir: str) -> Dict[str, Dict[str, Any]]:
    """加载所有Skill定义"""
    skills = {}
    skill_root = Path(skill_dir)

    for skill_subdir in skill_root.iterdir():
        if skill_subdir.is_dir():
            skill_md = skill_subdir / "SKILL.md"
            if skill_md.exists():
                skill_data = parse_skill_md(str(skill_md))
                skill_name = skill_data["frontmatter"].get("name", skill_subdir.name)
                skills[skill_name] = skill_data
                print(f"✅ 已加载Skill: {skill_name}")

    return skills

# ============================================
# Skill执行模拟器
# ============================================

class SkillExecutor:
    """模拟执行Skill的逻辑"""

    def __init__(self, skills: Dict[str, Dict[str, Any]], mode: str = "mock"):
        self.skills = skills
        self.mode = mode
        self.session_state = {
            "session_id": f"EVAL-{datetime.now().strftime('%Y%m%d%H%M%S')}",
            "current_iteration": 0,
            "max_iterations": 30,
            "history": [],
            "current_step": "init",
            "testcases": {"fetched": ["TC-001"], "missing": []}
        }

    def run_scenario_parser(self, input_data: Dict) -> Dict:
        """执行场景解析"""
        print("\n📋 执行 scenario_parser Skill...")
        skill = self.skills.get("scenario_parser")
        if not skill:
            return {"error": "scenario_parser not found"}

        # Mock模式：返回预定义测试用例
        if self.mode == "mock":
            return {"test_case": MOCK_TEST_CASE}

        # Real模式：需要调用LLM（未实现）
        return {"test_case": MOCK_TEST_CASE, "note": "Real模式需要LLM调用"}

    def run_test_executor(self, test_case: Dict) -> Dict:
        """执行测试"""
        print("\n🧪 执行 test_executor Skill...")
        skill = self.skills.get("test_executor")
        if not skill:
            return {"error": "test_executor not found"}

        iteration = self.session_state["current_iteration"]

        # Mock模式：返回预定义执行trace
        if self.mode == "mock":
            if iteration < len(MOCK_EXECUTION_TRACES):
                trace = MOCK_EXECUTION_TRACES[iteration]
            else:
                trace = MOCK_EXECUTION_TRACES[-1]  # 最后一轮合格
            return {"execution_result": trace}

        return {"execution_result": MOCK_EXECUTION_TRACES[0], "note": "Real模式需要真实沙箱"}

    def run_evaluator(self, test_case: Dict, execution_result: Dict) -> Dict:
        """执行评估判分"""
        print("\n📊 执行 evaluator Skill...")
        skill = self.skills.get("evaluator")
        if not skill:
            return {"error": "evaluator not found"}

        # 根据执行结果计算评分
        tool_calls = execution_result.get("tool_calls_detail", [])
        tone = execution_result.get("tone_analysis", {})

        # 评分逻辑
        scores = {}

        # 工具调用正确性
        tool_score = 100
        for tc in tool_calls:
            if tc.get("missing") and tc.get("severity") == "critical":
                tool_score = 0  # 严重违规
                break
            elif not tc.get("called"):
                tool_score -= 30
        scores["tool_call_correctness"] = tool_score

        # 交互质量（基于tone分析）
        empathy = tone.get("empathy", 5)
        friendliness = tone.get("friendliness", 5)
        scores["interaction_quality"] = int((empathy + friendliness) * 5.5)

        # 功能完整性
        logs = execution_result.get("logs", [])
        step_count = len([l for l in logs if l.get("type") == "message"])
        scores["functional_completeness"] = min(100, step_count * 25)

        # 流程合规
        scores["process_compliance"] = 80 if tool_score > 0 else 40

        # 问题解决
        scores["problem_resolution"] = 70 if tool_score > 0 else 30

        # 综合评分
        weights = {"functional_completeness": 0.25, "interaction_quality": 0.35,
                   "process_compliance": 0.10, "problem_resolution": 0.30,
                   "tool_call_correctness": 0.25}

        overall = sum(scores[k] * weights.get(k, 0.20) for k in scores) / sum(weights.values())

        # 判断合格
        passed = overall >= 70 and all(s >= 60 for s in scores.values()) and scores["tool_call_correctness"] > 0

        # 生成问题列表
        critical_issues = []
        if scores["tool_call_correctness"] == 0:
            critical_issues.append({
                "issue_id": "ISS-001",
                "dimension": "tool_call_correctness",
                "severity": "critical",
                "description": "未调用 create_ticket 工具登记工单"
            })
        if scores["interaction_quality"] < 60:
            critical_issues.append({
                "issue_id": "ISS-002",
                "dimension": "interaction_quality",
                "severity": "high",
                "description": "语气生硬，缺乏同理心表达"
            })

        return {
            "evaluation_result": {
                "overall_score": int(overall),
                "dimension_scores": scores,
                "passed": passed,
                "critical_issues": critical_issues,
                "strengths": ["基本功能完成"] if passed else [],
                "improvement_points": [
                    {"dimension": "tool_call_correctness", "point": "必须调用 create_ticket", "priority": "critical"}
                ] if not passed else []
            }
        }

    def run_training_advisor(self, evaluation_result: Dict) -> Dict:
        """生成训练建议"""
        print("\n💡 执行 training_advisor Skill...")
        skill = self.skills.get("training_advisor")
        if not skill:
            return {"error": "training_advisor not found"}

        issues = evaluation_result.get("critical_issues", [])

        modifications = []
        for issue in issues:
            if issue["dimension"] == "tool_call_correctness":
                modifications.append({
                    "modification_type": "prompt_update",
                    "priority": "critical",
                    "description": "在Prompt中添加工单登记强制要求",
                    "current_content": "处理用户申诉...",
                    "modified_content": "处理用户申诉...**重要：流程结束后必须调用create_ticket工具登记工单。**"
                })
            elif issue["dimension"] == "interaction_quality":
                modifications.append({
                    "modification_type": "prompt_update",
                    "priority": "high",
                    "description": "增加同理心表达引导",
                    "current_content": "回复保持礼貌",
                    "modified_content": "回复保持礼貌。**表达同理心**：处理前先说'我理解您的感受'。"
                })

        return {"improvement_plan": {"modifications": modifications, "expected_improvement": {"overall_target": 75}}}

    def run_orchestrator(self) -> Dict:
        """运行完整流程"""
        print("\n" + "="*60)
        print(f"🚀 开始评估流程 [{self.mode}模式]")
        print("="*60)

        # 阶段1：获取测试用例
        print("\n📦 阶段1: 获取测试用例...")
        testcase_result = self.run_scenario_parser({})
        test_case = testcase_result.get("test_case", MOCK_TEST_CASE)

        # 训练循环
        while self.session_state["current_iteration"] < self.session_state["max_iterations"]:
            self.session_state["current_iteration"] += 1
            iteration = self.session_state["current_iteration"]

            print(f"\n{'='*60}")
            print(f"🔄 第 {iteration} 轮评估训练")
            print("="*60)

            # 阶段2：执行测试
            execution_result = self.run_test_executor(test_case)

            # 阶段3：评估判分
            evaluation_result = self.run_evaluator(test_case, execution_result.get("execution_result", {}))

            # 输出评估结果
            result = evaluation_result.get("evaluation_result", {})
            overall = result.get("overall_score", 0)
            passed = result.get("passed", False)

            print(f"\n📋 第 {iteration} 轮评估结果:")
            print(f"   综合评分: {overall}/100 {'✅合格' if passed else '❌不合格'}")
            print("   各维度得分:")
            for dim, score in result.get("dimension_scores", {}).items():
                status = "✅" if score >= 60 else ("⚠️" if score >= 40 else "❌")
                print(f"   - {dim}: {score} {status}")

            # 记录历史
            self.session_state["history"].append({
                "iteration": iteration,
                "score": overall,
                "passed": passed,
                "dimension_scores": result.get("dimension_scores", {})
            })

            if passed:
                print(f"\n🎉 评估合格！该数字员工已具备上岗能力。")
                break

            # 阶段4：人工审核（Mock模式自动继续）
            print(f"\n⏸️ 人工审核节点（Mock模式自动继续）...")

            # 阶段5：生成改进方案
            improvement = self.run_training_advisor(result)
            print(f"\n💡 改进方案已生成:")
            for mod in improvement.get("improvement_plan", {}).get("modifications", []):
                print(f"   - [{mod['priority']}] {mod['description']}")

            # Mock模式：限制轮次
            if self.mode == "mock" and iteration >= 3:
                print(f"\n🎉 Mock演示完成，第3轮已合格上岗。")
                break

        return self.session_state

# ============================================
# 主程序
# ============================================

def main():
    parser = argparse.ArgumentParser(description="测试 evaluation-expert/skills")
    parser.add_argument("--mode", choices=["mock", "real"], default="mock", help="运行模式")
    parser.add_argument("--skill-dir", default="skills", help="Skill目录路径")
    parser.add_argument("--max-rounds", type=int, default=3, help="最大训练轮次（Mock模式默认3轮）")
    parser.add_argument("--export", help="导出结果JSON文件路径")

    args = parser.parse_args()

    # 检查Skill目录
    skill_dir = Path(args.skill_dir)
    if not skill_dir.exists():
        print(f"❌ Skill目录不存在: {skill_dir}")
        print(f"   请确保 {args.skill_dir} 目录下有各子目录的 SKILL.md 文件")
        sys.exit(1)

    print(f"📂 Skill目录: {skill_dir}")

    # 加载所有Skill
    print("\n📚 加载Skill定义...")
    skills = load_all_skills(args.skill_dir)

    if len(skills) < 5:
        print(f"⚠️ 只加载了 {len(skills)} 个Skill，预期5个")

    # 执行评估流程
    executor = SkillExecutor(skills, mode=args.mode)
    executor.session_state["max_iterations"] = args.max_rounds

    result = executor.run_orchestrator()

    # 导出结果
    if args.export:
        with open(args.export, 'w', encoding='utf-8') as f:
            json.dump(result, f, ensure_ascii=False, indent=2)
        print(f"\n📄 结果已导出: {args.export}")

    # 输出最终报告
    print("\n" + "="*60)
    print("📊 最终评估报告")
    print("="*60)

    history = result.get("history", [])
    if history:
        print(f"\n轮次历史:")
        for h in history:
            status = "✅合格" if h["passed"] else "❌不合格"
            print(f"  第{h['iteration']}轮: {h['score']}分 {status}")

        # 得分趋势
        print(f"\n得分趋势: {[h['score'] for h in history]}")

        if history[-1]["passed"]:
            print(f"\n✅ 最终状态: 合格上岗")
        else:
            print(f"\n❌ 最终状态: 训练未达标")

if __name__ == "__main__":
    main()
