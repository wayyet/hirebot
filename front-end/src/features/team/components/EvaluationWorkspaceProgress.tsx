import { AlertCircle, CheckCircle2, Loader2, XCircle } from 'lucide-react'
import type { EvaluationWorkspaceStatus } from '@/infra/api'

const STEP_LABELS: Record<string, string> = {
  target_sandbox: '创建目标沙箱',
  evaluator_sandbox: '创建评估沙箱',
  upload_skill: '上传评估技能包',
  upload_employee_template: '上传员工模板',
  upload_artifacts: '上传员工产物包',
  materials: '加载评估素材',
}

function stepIcon(status: string) {
  if (status === 'completed') return <CheckCircle2 size={14} className="text-[#15803d]" />
  if (status === 'running') return <Loader2 size={14} className="animate-spin text-[#4a6cf7]" />
  if (status === 'failed') return <XCircle size={14} className="text-[#b3263c]" />
  return <span className="h-3.5 w-3.5 rounded-full border border-[#d4d4d4]" />
}

function stepPillClass(status: string) {
  switch (status) {
    case 'completed':
      return 'hb-pill green'
    case 'running':
      return 'hb-pill blue'
    case 'failed':
      return 'hb-pill pink'
    default:
      return 'hb-pill gray'
  }
}

function stepPillLabel(status: string) {
  switch (status) {
    case 'completed':
      return '已完成'
    case 'running':
      return '进行中'
    case 'failed':
      return '失败'
    default:
      return '等待中'
  }
}

interface Props {
  status: EvaluationWorkspaceStatus | null
  polling?: boolean
}

export function EvaluationWorkspaceProgress({ status, polling = false }: Props) {
  if ((!status || status.overallStatus === 'not_started') && !polling) return null

  if (!status || status.overallStatus === 'not_started') {
    return (
      <div className="rounded-xl border border-[#ececec] bg-[#fafafa] p-3">
        <div className="flex items-center gap-2 rounded-xl bg-[#e8edff] px-3 py-2 text-xs font-medium text-[#4a6cf7]">
          <Loader2 size={14} className="animate-spin" />
          正在创建评估环境...
        </div>
      </div>
    )
  }

  const isReady = status.overallStatus === 'ready'
  const isFailed = status.overallStatus === 'failed'
  const isCreating = status.overallStatus === 'creating'
  const steps = status.steps ?? []
  const completedCount = steps.filter((step) => step.status === 'completed').length
  const totalSteps = Math.max(steps.length, 1)
  const progressPercent = Math.round((completedCount / totalSteps) * 100)

  return (
    <div className="rounded-xl border border-[#ececec] bg-[#fafafa] p-3">
      <div
        className={`flex items-center gap-2 rounded-xl px-3 py-2 text-xs font-medium ${
          isReady
            ? 'bg-[#e6f5ec] text-[#15803d]'
            : isFailed
              ? 'bg-[#fff1f2] text-[#b3263c]'
              : 'bg-[#e8edff] text-[#4a6cf7]'
        }`}
      >
        {isReady && <CheckCircle2 size={14} />}
        {isFailed && <AlertCircle size={14} />}
        {isCreating && <Loader2 size={14} className="animate-spin" />}
        {isReady && '评估环境已就绪'}
        {isFailed && `评估环境创建失败${status.errorMessage ? `：${status.errorMessage}` : ''}`}
        {isCreating && '正在创建评估环境...'}
      </div>

      {(status.targetSandboxId || status.evaluatorSandboxId) && (
        <div className="mt-2 flex flex-wrap gap-2 text-[11px] text-[#737373]">
          {status.targetSandboxId && (
            <span className="rounded-full border border-[#ececec] bg-white px-2 py-0.5">
              目标沙箱: <span className="font-mono text-[#404040]">{status.targetSandboxId.slice(0, 12)}...</span>
            </span>
          )}
          {status.evaluatorSandboxId && (
            <span className="rounded-full border border-[#ececec] bg-white px-2 py-0.5">
              评估沙箱: <span className="font-mono text-[#404040]">{status.evaluatorSandboxId.slice(0, 12)}...</span>
            </span>
          )}
        </div>
      )}

      <div className="mt-3 space-y-1.5">
        {steps.length === 0 ? (
          <div className="flex items-center gap-2 rounded-lg border border-[#f3f4f6] bg-white px-2.5 py-1.5 text-xs text-[#737373]">
            <Loader2 size={12} className="animate-spin" />
            正在准备步骤详情...
          </div>
        ) : (
          steps.map((step) => (
            <div
              key={step.step}
              className="flex items-center gap-2 rounded-lg border border-[#f3f4f6] bg-white px-2.5 py-1.5 text-xs"
            >
              {stepIcon(step.status)}
              <span className="flex-1 font-medium text-[#404040]">{STEP_LABELS[step.step] ?? step.step}</span>
              {step.detail && <span className="max-w-[150px] truncate text-[10px] text-[#9ca3af]">{step.detail}</span>}
              <span className={stepPillClass(step.status)}>{stepPillLabel(step.status)}</span>
            </div>
          ))
        )}
      </div>

      <div className="mt-3 h-1.5 w-full rounded-full bg-[#efefef]">
        <div
          className={`h-1.5 rounded-full transition-all duration-700 ${isFailed ? 'bg-[#b3263c]' : 'bg-[#4a6cf7]'}`}
          style={{ width: `${isFailed ? 100 : Math.max(progressPercent, 5)}%` }}
        />
      </div>
    </div>
  )
}