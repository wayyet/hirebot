import { FileText, Zap, Rocket } from 'lucide-react'
import type { LucideIcon } from 'lucide-react'
import type { HiringConversationMessage } from '@/infra/api'
import type { ToolStep } from '@/features/hiring/pages/hiringPageTypes'

/** 评估页面本地消息类型（在 HiringConversationMessage 基础上增加工具调用步骤） */
export type EvalChatMessage = HiringConversationMessage & { toolSteps?: ToolStep[] }

/** 执行轨迹 JSON 的事件条目 */
export interface TraceLogEntry {
  type: string
  timestamp?: string
  timestamp_start?: string
  timestamp_end?: string
  text?: string
  state?: string        // for state_change
  name?: string         // for tool_use
  input?: unknown       // for tool_use
  content?: unknown     // for tool_result
}

export interface TraceTurnSummary {
  total_messages?: number
  total_tool_calls?: number
  has_thought?: boolean
  think_count?: number
  execution_time_seconds?: number
  tool_calls_list?: string[]
}

export interface TraceExecutionTrace {
  logs: TraceLogEntry[]
  assembled_assistant_text?: string
  think_blocks?: unknown[]
  summary?: TraceTurnSummary
}

/** 执行轨迹 JSON 的单轮结构 */
export interface TraceTurn {
  turn_index: number
  test_case_id?: string
  user_input?: string
  execution_trace: TraceExecutionTrace
}

export interface TraceMeta {
  total_turns?: number
  session_id?: string
  employee_name?: string
  iteration?: number
  collected_at?: string
  target_sandbox_id?: string
}

export interface TraceProviderUsage {
  providerId?: string
  modelId?: string
  requests?: number
  inputTokens?: number
  outputTokens?: number
  cacheReadTokens?: number
}

/** 执行轨迹 JSON 文件的顶层结构 */
export interface TraceJsonData {
  meta?: TraceMeta
  status?: string
  turns: TraceTurn[]
  http_supplement?: {
    dashboard?: {
      providers?: {
        usage?: TraceProviderUsage[]
      }
    }
  }
}

export type ArtifactTab = 'overview' | 'testcase' | 'trace' | 'report'

export type WorkflowStageStatus = 'pending' | 'running' | 'completed' | 'failed'

export type WorkflowStage = {
  key: string
  title: string
  detail: string
  status: WorkflowStageStatus
  pendingLabel?: string
}

export type EvaluationStarterAction = {
  key: string
  title: string
  description: string
  prompt: string
  icon: LucideIcon
}

export const evaluationStarterActions: EvaluationStarterAction[] = [
  {
    key: 'explain-card',
    title: '解释当前题卡',
    description: '先说明本轮评估的目标、关键维度和判断重点。',
    prompt: '请先解释当前题卡的评估目标、关键维度、预期输出，以及我接下来应该重点关注什么。',
    icon: FileText,
  },
  {
    key: 'execution-plan',
    title: '给出执行计划',
    description: '把评估拆成清晰步骤，先看路径再正式开始。',
    prompt: '请基于当前题卡和已有素材，给出本轮评估的执行计划，按步骤列出每一步要验证什么。',
    icon: Zap,
  },
  {
    key: 'start-evaluation',
    title: '开始完整评估',
    description: '直接发起本轮评估，并持续同步结论和轨迹。',
    prompt: '评估材料已就绪，请开始执行 AI 评估，并持续同步关键结论、风险点和评分依据。',
    icon: Rocket,
  },
]

export const evaluationSuggestionPrompts = [
  '对比上一轮评估，这次哪些维度有改善？',
  '把评分标准里的「合规性」拆成更细的指标。',
  '如果某个场景失败，我们应该优先追问什么？',
  '请先给出本轮评估最值得关注的风险点。',
]
