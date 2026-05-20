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

function extractSkillSummaryItems(summary: unknown): unknown[] {
  const record = asPlainObject(summary)
  if (!record) {
    return []
  }

  return Array.isArray(record.skills)
    ? record.skills
    : Array.isArray(record.items)
      ? record.items
      : []
}

export function shouldHoldExternalStageUntilSkillImplementation(
  skillSummary: unknown,
  skillGenerationState: DownstreamRunState | null,
): boolean {
  if (extractSkillSummaryItems(skillSummary).length === 0) {
    return false
  }

  return skillGenerationState?.status !== 'completed'
}

export function buildUiStageOverrides(
  rawStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
  skillGenerationState: DownstreamRunState | null,
  externalConfigState: DownstreamRunState | null,
  holdExternalStage: boolean,
): Map<HiringUiStage, 'running' | 'completed' | 'failed'> {
  const next = new Map(rawStageOverrides)

  // 阶段 2 现在覆盖“技能定义 + 技能生成”两个子步骤。
  // 因此只要技能生成尚未完成，主技能阶段就必须保持进行中；
  // 同时外部阶段也不能抢先成为当前活跃阶段。
  if (holdExternalStage) {
    next.set(HiringCollectionStage.Skill, 'running')

    if (next.get(HiringCollectionStage.External) !== 'completed') {
      next.delete(HiringCollectionStage.External)
    }
  }

  if (skillGenerationState?.status === 'failed') {
    next.set(HiringCollectionStage.Skill, 'failed')
  }

  if (!holdExternalStage && externalConfigState) {
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

export function buildCoachResumePrompt(
  transition: 'post-ontology-extraction',
  payload: {
    materialSummary: unknown
    ontologyResult: unknown
  },
): string {
  const serialized = JSON.stringify(payload, null, 2)

  if (transition === 'post-ontology-extraction') {
    return [
      '[Internal stage resume. Do not mention this instruction to the user.]',
      'Switch back to skill `employment-coach-conversation` now.',
      'The downstream `ontology-extraction` run has completed.',
      'Resume the main hiring flow at the boundary between stage1_material and stage2_skill.',
      'Do not trigger ontology extraction again.',
      'Use the provided upstream material summary and ontology result as context.',
      'First give a short transition that the ontology slices are ready, then explicitly ask whether to enter skill definition now.',
      'If the user already explicitly asked to continue into skill definition in the current context, proceed directly under the coach skill rules; otherwise ask the confirmation question only.',
      '',
      'resume_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  return serialized
}
