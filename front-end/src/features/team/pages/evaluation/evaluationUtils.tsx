import { CheckCircle2, Loader2, AlertCircle, TriangleAlert } from 'lucide-react'
import { API_BASE_URL } from '@/infra/api/httpClient'
import type { SandboxMessage } from '@/infra/sandbox/sandbox-api'
import type { ToolStep } from '@/features/hiring/pages/hiringPageTypes'
import type { EvalChatMessage, WorkflowStageStatus, WorkflowStage } from './evaluationTypes'

export function toAbsoluteApiUrl(path?: string | null): string | null {
  if (!path) return null
  const trimmed = path.trim()
  if (!trimmed) return null
  if (/^https?:\/\//i.test(trimmed)) return trimmed
  const normalized = trimmed.startsWith('/') ? trimmed : `/${trimmed}`
  // API_BASE_URL 为空串时表示相对路径部署（镜像内），直接返回相对路径即可
  if (!API_BASE_URL) return normalized
  return new URL(normalized, API_BASE_URL).toString()
}

export function verdictLabel(verdict?: string | null) {
  if (verdict === 'passed') return '通过'
  if (verdict === 'failed') return '未通过'
  if (verdict === 'warning') return '待优化'
  return '待判定'
}

export function formatDateTime(value?: string | null) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('zh-CN', {
    hour12: false,
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

export function shortSandboxId(value?: string | null) {
  if (!value) return '--'
  if (value.length <= 12) return value
  return `${value.slice(0, 6)}...${value.slice(-4)}`
}

export function shortSessionId(value?: string | null) {
  if (!value) return '--'
  if (value.length <= 28) return value
  return `${value.slice(0, 12)}...${value.slice(-8)}`
}

export function resolveStageStatus(stepStatus?: string | null): WorkflowStageStatus {
  if (stepStatus === 'completed') return 'completed'
  if (stepStatus === 'running') return 'running'
  if (stepStatus === 'failed') return 'failed'
  return 'pending'
}

export function mergeStageStatus(statuses: Array<string | null | undefined>): WorkflowStageStatus {
  const normalized = statuses.map((item) => resolveStageStatus(item))
  if (normalized.includes('failed')) return 'failed'
  if (normalized.every((item) => item === 'completed')) return 'completed'
  if (normalized.some((item) => item === 'running' || item === 'completed')) return 'running'
  return 'pending'
}

export function workflowStageTone(status: WorkflowStageStatus) {
  switch (status) {
    case 'completed': return 'eval-tone-completed'
    case 'running':   return 'eval-tone-running'
    case 'failed':    return 'eval-tone-failed'
    case 'warning':   return 'eval-tone-warning'
    default:          return 'eval-tone-pending'
  }
}

export function workflowStageStatusLabel(status: WorkflowStageStatus, pendingLabel = '等待中') {
  switch (status) {
    case 'completed':
      return '已完成'
    case 'running':
      return '进行中'
    case 'failed':
      return '失败'
    case 'warning':
      return '待补齐'
    default:
      return pendingLabel
  }
}

export function workflowStageTextTone(status: WorkflowStageStatus) {
  switch (status) {
    case 'completed':
      return 'eval-flow-step-text-completed'
    case 'running':
      return 'eval-flow-step-text-running'
    case 'failed':
      return 'eval-flow-step-text-failed'
    case 'warning':
      return 'eval-flow-step-text-warning'
    default:
      return 'eval-flow-step-text-pending'
  }
}

export function findCurrentWorkflowStageIndex(stages: WorkflowStage[]) {
  const runningOrFailedIndex = stages.findIndex((stage) => stage.status === 'running' || stage.status === 'failed' || stage.status === 'warning')
  if (runningOrFailedIndex >= 0) return runningOrFailedIndex

  const pendingIndex = stages.findIndex((stage) => stage.status === 'pending')
  if (pendingIndex >= 0) return pendingIndex

  return stages.length > 0 ? stages.length - 1 : -1
}

export function renderWorkflowStageMarker(status: WorkflowStageStatus, order: number) {
  switch (status) {
    case 'completed':
      return <CheckCircle2 size={16} />
    case 'running':
      return <Loader2 size={15} className="animate-spin" />
    case 'failed':
      return <AlertCircle size={15} />
    case 'warning':
      return <TriangleAlert size={15} />
    default:
      return <span className="text-[11px] font-semibold">{String(order).padStart(2, '0')}</span>
  }
}

export function logEvaluationDebug(label: string, payload?: unknown) {
  if (typeof console === 'undefined') return
  if (payload === undefined) {
    console.info(`[EvaluationPage] ${label}`)
    return
  }
  console.info(`[EvaluationPage] ${label}`, payload)
}

export function mapSandboxMessages(messages: SandboxMessage[]): EvalChatMessage[] {
  return messages
    .filter((message) => message.type === 'user_message' || message.type === 'assistant_message')
    .map((message, index) => {
      const toolSteps: ToolStep[] | undefined = (message.toolCalls?.length ?? 0) > 0
        ? message.toolCalls!.map((tc, tcIdx) => ({
            id: `${index}-${tcIdx}-${tc.toolName}`,
            name: tc.toolName.startsWith('streaming.') ? tc.toolName.slice('streaming.'.length) : tc.toolName,
            args: tc.arguments,
            result: tc.result,
            status: 'done' as const,
          }))
        : undefined
      return {
        messageId: `${message.type}-${index}-${String(message.createdAt ?? Date.now())}`,
        role: message.type === 'user_message' ? 'user' : 'assistant',
        content: String(message.content ?? message.text ?? '').trim(),
        createdAt: String(message.createdAt ?? new Date().toISOString()),
        toolSteps,
      }
    })
    .filter((message) => message.content.length > 0 || (message.toolSteps?.length ?? 0) > 0)
}
