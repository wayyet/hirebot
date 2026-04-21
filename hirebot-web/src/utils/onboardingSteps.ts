import type { DigitalEmployee, EvalPhase } from '../mock/data'
import type { OnboardingStep } from '../components/OnboardingProgress'

const PHASE_ORDER: EvalPhase[] = [
  'pending_materials',
  'generating_cases',
  'executing',
  'evaluating',
  'retraining',
  'pending_review',
  'passed',
  'eval_failed',
]

function phaseIndex(phase: EvalPhase): number {
  return PHASE_ORDER.indexOf(phase)
}

function inferEvalPhase(employee: DigitalEmployee): EvalPhase {
  if (employee.evalPhase) return employee.evalPhase
  if (employee.lifecycleStatus === '已转正' || employee.lifecycleStatus === '离职中') return 'passed'
  if (employee.lifecycleStatus === '实习中') return 'pending_review'
  return 'pending_materials'
}

function stepStatus(
  phase: EvalPhase,
  activePhases: EvalPhase[],
  completedBelow: EvalPhase[],
  blockedPhases?: EvalPhase[],
): OnboardingStep['status'] {
  if (blockedPhases?.includes(phase)) return 'blocked'
  if (completedBelow.some(p => phaseIndex(phase) < phaseIndex(p))) return 'completed'
  if (activePhases.includes(phase)) return 'current'
  return 'pending'
}

export function generateOnboardingSteps(employee: DigitalEmployee): OnboardingStep[] {
  const phase = inferEvalPhase(employee)
  const iteration = employee.evalIteration ?? 0
  const maxIterations = employee.evalMaxIterations ?? 30
  const isFailed = phase === 'eval_failed'

  const isDone = (threshold: EvalPhase) =>
    phaseIndex(phase) > phaseIndex(threshold) && phase !== 'retraining'

  const isCurrent = (...phases: EvalPhase[]) => phases.includes(phase)

  return [
    // ── Step 1: 提交评估资料 ──────────────────────────────────────────
    {
      id: 'submit-materials',
      title: '提交评估资料',
      description: '上传员工模板切片、技能描述及真实工作案例场景，供 Planner-Agent 解析',
      status: isDone('pending_materials')
        ? 'completed'
        : isCurrent('pending_materials')
        ? 'current'
        : 'pending',
    },

    // ── Step 2: AI 生成测试用例 ──────────────────────────────────────
    {
      id: 'generate-cases',
      title: 'AI 生成测试用例',
      description: 'Planner-Agent 解析资料，生成结构化测试用例（输入 + 预期行为序列 + 评估维度）',
      status: isDone('generating_cases') || (isDone('retraining') && phase !== 'retraining' && phaseIndex(phase) > phaseIndex('generating_cases'))
        ? 'completed'
        : isCurrent('generating_cases', 'retraining')
        ? 'current'
        : phase === 'pending_materials'
        ? 'pending'
        : isDone('pending_materials')
        ? 'pending'
        : 'pending',
      iterationLabel: isCurrent('generating_cases', 'retraining') && iteration > 0
        ? `第 ${iteration + 1} 轮 / ${maxIterations}`
        : undefined,
    },

    // ── Step 3: 沙箱执行测试 ─────────────────────────────────────────
    {
      id: 'execute',
      title: '沙箱执行测试',
      description: '在隔离沙箱中运行测试用例，执行采集器捕获思考链路、工具调用与对话全程',
      status: isDone('executing')
        ? 'completed'
        : isCurrent('executing')
        ? 'current'
        : 'pending',
      iterationLabel: isCurrent('executing') && iteration > 0
        ? `第 ${iteration} 轮 / ${maxIterations}`
        : undefined,
    },

    // ── Step 4: AI 评分 ───────────────────────────────────────────────
    {
      id: 'evaluate',
      title: 'AI 评估打分',
      description: 'Evaluator-Agent 对比执行过程与预期标准，按 5 个维度自动打分并生成改进建议',
      status: isDone('evaluating') && !isCurrent('retraining')
        ? 'completed'
        : isCurrent('evaluating', 'retraining')
        ? 'current'
        : 'pending',
      iterationLabel: isCurrent('evaluating', 'retraining') && iteration > 0
        ? `第 ${iteration} 轮 / ${maxIterations}`
        : undefined,
    },

    // ── Step 5: 人工审核 ──────────────────────────────────────────────
    {
      id: 'review',
      title: '人工审核',
      description: '确认 AI 判定结果，或进行 Override（强制通过 / 打回重训）；不合格则触发重训循环',
      status: isFailed
        ? 'blocked'
        : isDone('pending_review')
        ? 'completed'
        : isCurrent('pending_review')
        ? 'current'
        : 'pending',
      iterationLabel: isCurrent('pending_review') && iteration > 0
        ? `第 ${iteration} 轮 / ${maxIterations}`
        : undefined,
      blockedReason: isFailed
        ? `已达到最大训练轮次（${maxIterations} 轮），训练终止`
        : undefined,
    },

    // ── Step 6: 转正上岗 ──────────────────────────────────────────────
    {
      id: 'onboard',
      title: '转正上岗',
      description: '评估通过，正式上岗，进入生产环境持续创造价值',
      status: phase === 'passed' && employee.lifecycleStatus === '已转正'
        ? 'completed'
        : phase === 'passed'
        ? 'current'
        : 'pending',
      completedAt: employee.graduatedAt,
    },
  ]
}

export function getCurrentStepId(employee: DigitalEmployee): string {
  const steps = generateOnboardingSteps(employee)
  return (
    steps.find(s => s.status === 'current' || s.status === 'blocked')?.id ??
    steps.findLast(s => s.status === 'completed')?.id ??
    steps[0].id
  )
}

export function getOnboardingProgress(employee: DigitalEmployee): number {
  const steps = generateOnboardingSteps(employee)
  const completed = steps.filter(s => s.status === 'completed').length
  return Math.round((completed / steps.length) * 100)
}
