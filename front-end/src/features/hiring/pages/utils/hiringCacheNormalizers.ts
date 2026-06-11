import { HiringCollectionStage } from '@/infra/api'
import type {
  ArtifactDisplayData,
  ChatFile,
  ChatMessage,
  DownstreamRunsSnapshot,
  StageGateData,
  ToolStep,
} from '../hiringPageTypes'
import type { HiringUiStage } from '../hiringWorkflowViewModel'

export type CachedStageOverride = [HiringUiStage, 'running' | 'completed' | 'failed']

export function hasPendingDownstreamRuns(runs: DownstreamRunsSnapshot): boolean {
  return Object.values(runs).some(run => run?.status === 'waiting_confirm' || run?.status === 'running')
}

export function hasPendingRequiredDownstreamRuns(runs: DownstreamRunsSnapshot): boolean {
  return Object.entries(runs).some(([key, run]) => (
    key !== 'packaging-test-cases' &&
    (run?.status === 'waiting_confirm' || run?.status === 'running')
  ))
}

export function asPlainObject(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

export function asStringArray(value: unknown): string[] {
  if (Array.isArray(value)) {
    return value
      .map(item => typeof item === 'string' ? item.trim() : '')
      .filter(item => item.length > 0)
  }

  if (typeof value === 'string' && value.trim().length > 0) {
    return [value.trim()]
  }

  return []
}

export function sanitizeFileForCache(file: ChatFile): ChatFile {
  return {
    id: file.id,
    name: file.name,
    size: file.size,
    status: file.status,
    type: file.type,
    mimeType: file.mimeType,
    metadata: file.metadata,
  }
}

export function sanitizeMessagesForCache(messages: ChatMessage[]): ChatMessage[] {
  return messages.map(message => ({
    ...message,
    files: message.files?.map(sanitizeFileForCache),
  }))
}

function normalizeStringRecord(value: unknown): Record<string, string> | undefined {
  const record = asPlainObject(value)
  if (!record) return undefined

  const entries = Object.entries(record)
    .filter((entry): entry is [string, string] => typeof entry[1] === 'string')
  return entries.length > 0 ? Object.fromEntries(entries) : undefined
}

export function normalizeCachedFiles(value: unknown): ChatFile[] | undefined {
  if (!Array.isArray(value)) return undefined

  const files = value
    .map((item, index): ChatFile | null => {
      const record = asPlainObject(item)
      const name = typeof record?.name === 'string' ? record.name.trim() : ''
      if (!name) return null

      const size = typeof record?.size === 'number' && Number.isFinite(record.size)
        ? Math.max(0, record.size)
        : 0
      const rawStatus = record?.status
      const rawType = record?.type
      return {
        id: typeof record?.id === 'string' && record.id ? record.id : `cached_file_${index}`,
        name,
        size,
        status: rawStatus === '解析中' ? '解析中' : '已解析',
        type: rawType === 'skill' ? 'skill' : 'file',
        mimeType: typeof record?.mimeType === 'string' ? record.mimeType : undefined,
        metadata: normalizeStringRecord(record?.metadata),
      }
    })
    .filter((file): file is ChatFile => file !== null)

  return files.length > 0 ? files : undefined
}

function normalizeCachedToolSteps(value: unknown): ToolStep[] | undefined {
  if (!Array.isArray(value)) return undefined

  const steps = value
    .map((item, index): ToolStep | null => {
      const record = asPlainObject(item)
      const name = typeof record?.name === 'string' ? record.name.trim() : ''
      if (!name) return null

      const status = record?.status === 'error'
        ? 'error'
        : record?.status === 'done'
          ? 'done'
          : 'running'
      return {
        id: typeof record?.id === 'string' && record.id ? record.id : `cached_tool_${index}`,
        name,
        status,
        args: typeof record?.args === 'string' ? record.args : undefined,
        result: typeof record?.result === 'string' ? record.result : undefined,
      }
    })
    .filter((step): step is ToolStep => step !== null)

  return steps.length > 0 ? steps : undefined
}

export function normalizeCachedMessages(value: unknown): ChatMessage[] {
  if (!Array.isArray(value)) return []

  return value
    .map((item, index): ChatMessage | null => {
      const record = asPlainObject(item)
      if (!record) return null

      const role = record.role
      if (role !== 'bot' && role !== 'user' && role !== 'artifact' && role !== 'stage_gate') {
        return null
      }

      const message: ChatMessage = {
        id: typeof record.id === 'string' && record.id ? record.id : `cached_message_${index}`,
        role,
        content: typeof record.content === 'string' ? record.content : '',
      }

      const files = normalizeCachedFiles(record.files)
      if (files) message.files = files

      const artifact = asPlainObject(record.artifact)
      if (artifact) message.artifact = artifact as unknown as ArtifactDisplayData

      const stageGate = asPlainObject(record.stageGate)
      if (stageGate) message.stageGate = stageGate as unknown as StageGateData

      const toolSteps = normalizeCachedToolSteps(record.toolSteps)
      if (toolSteps) message.toolSteps = toolSteps

      return message
    })
    .filter((message): message is ChatMessage => message !== null)
}

export function normalizeCachedStageOverrides(value: unknown): CachedStageOverride[] {
  if (!Array.isArray(value)) return []

  return value
    .map((item): CachedStageOverride | null => {
      if (!Array.isArray(item) || item.length < 2) return null
      const stage = item[0]
      const status = item[1]
      const validStage =
        stage === HiringCollectionStage.Material ||
        stage === HiringCollectionStage.Skill ||
        stage === HiringCollectionStage.External ||
        stage === HiringCollectionStage.ReadyForPackaging
      const validStatus = status === 'running' || status === 'completed' || status === 'failed'
      return validStage && validStatus ? [stage, status] : null
    })
    .filter((item): item is CachedStageOverride => item !== null)
}
