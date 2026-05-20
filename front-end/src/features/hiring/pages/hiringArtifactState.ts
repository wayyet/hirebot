import { HiringCollectionStage } from '@/infra/api'
import type { SandboxMessage, SandboxToolCall } from '@/infra/sandbox/sandbox-api'

import type {
  ArtifactDisplayData,
  ChatMessage,
  DownstreamRunKey,
  DownstreamRunsSnapshot,
  DownstreamRunState,
  DownstreamRunStatus,
} from './hiringPageTypes'
import { extractLatestMaterialRequestedCategories } from './materialRequestedCategories'
import type { HiringUiStage } from './hiringWorkflowViewModel'

function mkHistoricalId(prefix: string, index: number) {
  return `historical_${prefix}_${index}`
}

function asPlainObject(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function tryParseJsonRecord(value: unknown): Record<string, unknown> | null {
  if (typeof value === 'string') {
    try {
      return asPlainObject(JSON.parse(value))
    } catch {
      return null
    }
  }

  return asPlainObject(value)
}

export const DOWNSTREAM_ARTIFACT_TRACKS: Record<string, { key: DownstreamRunKey; status: DownstreamRunStatus }> = {
  ontology_extraction_progress: { key: 'ontology-extraction', status: 'running' },
  ontology_extraction_done: { key: 'ontology-extraction', status: 'completed' },
  skill_generation_ready: { key: 'skill-generation', status: 'waiting_confirm' },
  skill_generation_progress: { key: 'skill-generation', status: 'running' },
  skill_generation_done: { key: 'skill-generation', status: 'completed' },
  external_config_progress: { key: 'external-config', status: 'running' },
  external_config_done: { key: 'external-config', status: 'completed' },
}

export function resolveHiringStageFromWs(
  skillName: string | undefined,
  stageName: string | undefined,
): HiringUiStage | null {
  if (skillName === 'employment-coach-conversation' && stageName) {
    if (stageName.includes('material')) return HiringCollectionStage.Material
    if (stageName.includes('skill')) return HiringCollectionStage.Skill
    if (stageName.includes('external')) return HiringCollectionStage.External
  }

  return null
}

export function resolveDownstreamRunFromArtifact(
  artifactType: string,
): { key: DownstreamRunKey; status: DownstreamRunStatus } | null {
  return DOWNSTREAM_ARTIFACT_TRACKS[artifactType] ?? null
}

export function buildUiStageOverrides(
  rawStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
  _skillGenerationState: DownstreamRunState | null,
  externalConfigState: DownstreamRunState | null,
): Map<HiringUiStage, 'running' | 'completed' | 'failed'> {
  const next = new Map(rawStageOverrides)

  // 技能实现轨是下游执行状态，不能回写主技能定义阶段。
  if (externalConfigState) {
    if (externalConfigState.status === 'completed') {
      next.set(HiringCollectionStage.External, 'completed')
    } else if (externalConfigState.status === 'failed') {
      next.set(HiringCollectionStage.External, 'failed')
    } else {
      next.set(HiringCollectionStage.External, 'running')
    }
  }

  return next
}

function toArtifactDisplayData(raw: Record<string, unknown>): ArtifactDisplayData {
  const kind = (String(raw.kind ?? 'data')) as 'file' | 'data'
  const artifactType = String(raw.artifactType ?? raw.artifact_type ?? 'generic')
  const label = raw.label != null ? String(raw.label) : undefined
  const skillName = raw.skillName != null ? String(raw.skillName) : undefined
  const stage = raw.stage != null ? String(raw.stage) : undefined
  const isTerminal = Boolean(raw.isTerminal ?? raw.is_terminal)
  const displayHint = raw.displayHint != null
    ? String(raw.displayHint)
    : raw.display_hint != null
      ? String(raw.display_hint)
      : undefined

  const artifactData: ArtifactDisplayData = {
    kind,
    artifactType,
    label,
    skillName,
    stage,
    isTerminal,
    displayHint,
  }

  if (kind === 'file') {
    artifactData.fileUrl = String(raw.fileUrl ?? raw.file_url ?? '')
    artifactData.fileName = String(raw.fileName ?? raw.file_name ?? label ?? 'file')
    artifactData.mimeType = String(raw.mimeType ?? raw.mime_type ?? '')
  } else {
    artifactData.data = typeof raw.data === 'string' ? JSON.parse(raw.data) : raw.data
  }

  return artifactData
}

function extractArtifactFromToolCall(toolCall: SandboxToolCall): ArtifactDisplayData | null {
  if (!toolCall.toolName || !toolCall.toolName.endsWith('emit_artifact')) {
    return null
  }

  const payload = tryParseJsonRecord(toolCall.arguments)
  if (!payload) {
    return null
  }

  try {
    return toArtifactDisplayData(payload)
  } catch {
    return null
  }
}

function extractLatestArtifactData(messages: ChatMessage[], artifactType: string): unknown | null {
  for (let index = messages.length - 1; index >= 0; index -= 1) {
    const artifact = messages[index].artifact
    if (artifact?.artifactType === artifactType) {
      return artifact.data ?? null
    }
  }

  return null
}

export interface HistoricalHiringConversationState {
  messages: ChatMessage[]
  materialRequestedCategories: ReturnType<typeof extractLatestMaterialRequestedCategories>
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>
  downstreamRuns: DownstreamRunsSnapshot
  latestMaterialSummary: unknown | null
  latestSkillSummary: unknown | null
  latestExternalSummary: unknown | null
}

export function buildHistoricalHiringConversationState(
  sandboxMessages: SandboxMessage[],
  normalizeAssistantReply: (content: string) => string,
): HistoricalHiringConversationState {
  const messages: ChatMessage[] = []
  const wsStageOverrides = new Map<HiringUiStage, 'running' | 'completed' | 'failed'>()
  const downstreamRuns: DownstreamRunsSnapshot = {}
  let artifactIndex = 0

  for (const message of sandboxMessages) {
    if (message.type === 'user_message') {
      const content = String(message.text ?? '').trim()
      if (content.length > 0) {
        messages.push({
          id: mkHistoricalId('user', messages.length),
          role: 'user',
          content,
        })
      }
      continue
    }

    if (message.type !== 'assistant_message') {
      continue
    }

    for (const toolCall of message.toolCalls ?? []) {
      const artifact = extractArtifactFromToolCall(toolCall)
      if (!artifact) {
        continue
      }

      messages.push({
        id: mkHistoricalId('artifact', artifactIndex),
        role: 'artifact',
        content: artifact.label ?? artifact.artifactType,
        artifact,
      })
      artifactIndex += 1

      const downstreamRun = resolveDownstreamRunFromArtifact(artifact.artifactType)
      if (downstreamRun) {
        downstreamRuns[downstreamRun.key] = {
          key: downstreamRun.key,
          status: downstreamRun.status,
          artifactType: artifact.artifactType,
          label: artifact.label,
          displayHint: artifact.displayHint,
          updatedAt: String(message.createdAt ?? new Date().toISOString()),
          data: artifact.data,
        }
        continue
      }

      const hiringStage = resolveHiringStageFromWs(artifact.skillName, artifact.stage)
      if (!hiringStage) {
        continue
      }

      if (artifact.isTerminal) {
        wsStageOverrides.set(hiringStage, 'completed')
      } else if (wsStageOverrides.get(hiringStage) !== 'completed') {
        wsStageOverrides.set(hiringStage, 'running')
      }
    }

    const assistantContent = normalizeAssistantReply(String(message.content ?? ''))
    if (assistantContent.length > 0) {
      messages.push({
        id: mkHistoricalId('assistant', messages.length),
        role: 'bot',
        content: assistantContent,
      })
    }
  }

  return {
    messages,
    materialRequestedCategories: extractLatestMaterialRequestedCategories(messages),
    wsStageOverrides,
    downstreamRuns,
    latestMaterialSummary: extractLatestArtifactData(messages, 'material_handoff_summary'),
    latestSkillSummary: extractLatestArtifactData(messages, 'skill_workorder_summary'),
    latestExternalSummary: extractLatestArtifactData(messages, 'external_workorder_summary'),
  }
}
