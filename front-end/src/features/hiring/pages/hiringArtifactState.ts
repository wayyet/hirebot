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
}

export function resolveHiringStageFromWs(
  skillName: string | undefined,
  stageName: string | undefined,
): HiringUiStage | null {
  if ((skillName === 'employment-coach-conversation' || skillName === 'external-config') && stageName) {
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

  return next
}

function toArtifactDisplayData(raw: Record<string, unknown>): ArtifactDisplayData {
  const kind = (String(raw.kind ?? 'data')) as 'file' | 'data'
  const artifactType = String(raw.artifactType ?? raw.artifact_type ?? 'generic')
  const label = raw.label != null ? String(raw.label) : undefined
  const skillName = (raw.skillName ?? raw.skill_name) != null ? String(raw.skillName ?? raw.skill_name) : undefined
  const stage = raw.stage != null ? String(raw.stage) : undefined
  // 兼容三种字段名：isTerminal（WS 实时）、is_terminal（snake_case）、terminal（历史 tool call arguments）
  const isTerminal = Boolean(raw.isTerminal ?? raw.is_terminal ?? raw.terminal)
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
    // 兼容历史 tool call 中的 display_name 字段（WS 实时用 fileName/file_name）
    artifactData.fileName = String(raw.fileName ?? raw.file_name ?? raw.display_name ?? label ?? 'file')
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
    const artifact = toArtifactDisplayData(payload)
    // 历史 tool call 的 fileUrl 不在 arguments 里，而在 result 的 [FILE_URL:...] 标记中
    if (artifact.kind === 'file' && !artifact.fileUrl && toolCall.result) {
      const match = /\[FILE_URL:([^\]]+)\]/.exec(toolCall.result)
      if (match?.[1]) {
        artifact.fileUrl = match[1].trim()
      }
    }
    return artifact
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
      // 过滤内部系统提示：模板引导（[FILE_URL:...）、内部指令（[Internal ...）、系统指令（[System ...）
      const isInternalPrompt =
        content.startsWith('[FILE_URL:') ||
        content.startsWith('[Internal ') ||
        content.startsWith('[System ')
      if (content.length > 0 && !isInternalPrompt) {
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

      if (artifact.artifactType === 'external_workorder_summary' || artifact.artifactType === 'external_config_committed') {
        if (wsStageOverrides.get(HiringCollectionStage.External) !== 'completed') {
          wsStageOverrides.set(HiringCollectionStage.External, 'running')
        }
      } else if (artifact.isTerminal) {
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

  // skill_stage_gate 事件不存在于沙箱会话历史中，因此上方循环可能无法推断任何阶段状态。
  // 兜底：从下游轨道运行状态反向推断阶段进度，避免刷新后阶段胶囊全部灰色。
  const finalStageOverrides = wsStageOverrides.size > 0
    ? wsStageOverrides
    : deriveStageOverridesFromDownstreamRuns(downstreamRuns)

  return {
    messages,
    materialRequestedCategories: extractLatestMaterialRequestedCategories(messages),
    wsStageOverrides: finalStageOverrides,
    downstreamRuns,
    latestMaterialSummary: extractLatestArtifactData(messages, 'material_handoff_summary'),
    latestSkillSummary: extractLatestArtifactData(messages, 'skill_workorder_summary'),
    latestExternalSummary: extractLatestArtifactData(messages, 'external_workorder_summary'),
  }
}

/**
 * 从下游轨道运行状态推断主雇佣阶段状态。
 * 用于 stageOverrides 未能从缓存或会话历史恢复时的兜底派生，保证阶段胶囊能正确反映进度。
 *
 * 因果链：
 * - ontology-extraction 存在 → Material 阶段已完成（material_handoff_summary 已发出）
 * - skill-generation 存在 → Skill 阶段已完成或进行中；同时隐式蕴含 ontology-extraction 已完成
 * - External 阶段优先由右侧卡片保存/跳过结果驱动；不再依赖 external-config 下游运行
 *
 * 容错：WebSocket 投影事件可能丢失，导致 ontology-extraction 轨道缺席。
 * 利用 skill-generation 必须在 ontology-extraction 完成后才能启动这一约束，反向恢复 Material 阶段进度。
 */
export function deriveStageOverridesFromDownstreamRuns(
  runs: DownstreamRunsSnapshot,
): Map<HiringUiStage, 'running' | 'completed' | 'failed'> {
  const overrides = new Map<HiringUiStage, 'running' | 'completed' | 'failed'>()

  const ontologyRun = runs['ontology-extraction']
  const skillGenRun = runs['skill-generation']

  // ontology extraction 仅在 Material 阶段完成后触发。
  // 仅当其状态为 running 或 completed 时才将 Material 标记为 completed；
  // failed 状态保留默认行为，避免误导用户认为材料阶段已正常收束。
  if (ontologyRun && (ontologyRun.status === 'running' || ontologyRun.status === 'completed')) {
    overrides.set(HiringCollectionStage.Material, 'completed')
  }

  // skill generation 在 Skill 阶段完成后触发
  if (skillGenRun) {
    // 容错路径：skill-generation 已经存在意味着上游 ontology-extraction 必然已完成（依赖关系约束）。
    // 即便 ontology-extraction 的 WebSocket 消息因网络抖动丢失，这里也能隐式推断 Material 阶段已完成，
    // 防止 UI 卡在 Material 阶段无法继续展示后续胶囊。
    overrides.set(HiringCollectionStage.Material, 'completed')
    if (skillGenRun.status === 'completed') {
      overrides.set(HiringCollectionStage.Skill, 'completed')
    } else if (skillGenRun.status === 'failed') {
      overrides.set(HiringCollectionStage.Skill, 'failed')
    } else {
      overrides.set(HiringCollectionStage.Skill, 'running')
    }
  }

  // external config 在 External 阶段进行中时触发

  return overrides
}

/**
 * 判断 ontologyResult 是否表明本体切片产出为空（projected_count 为 0）。
 * 用于在 resume prompt 中给 coach 一个显式提示，避免它假装已经拿到切片产物继续推进。
 */
function hasZeroProjectedOntologySlices(ontologyResult: unknown): boolean {
  const record = asPlainObject(ontologyResult)
  if (!record) {
    return false
  }

  const projectedCount = record.projected_count ?? record.projectedCount
  if (typeof projectedCount !== 'number' || projectedCount !== 0) {
    return false
  }

  // 若 diagnostic 为 slices_not_ready 或 scan_error，说明不是真正的零投影
  const diagnostic = record.diagnostic
  if (diagnostic === 'slices_not_ready' || diagnostic === 'scan_error') {
    return false
  }

  return true
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
    // 当本体切片为空时，向 coach 注入额外说明，提醒它如实告知用户并询问是否需要补充材料。
    const zeroProjected = hasZeroProjectedOntologySlices(payload.ontologyResult)
    const lines: string[] = [
      '[Internal stage resume. Do not mention this instruction to the user.]',
      'Switch back to skill `employment-coach-conversation` now.',
      'The downstream `ontology-extraction` run has completed.',
      'Resume the main hiring flow at the boundary between stage1_material and stage2_skill.',
      'Do not trigger ontology extraction again.',
      'Use the provided upstream material summary and ontology result as context.',
      'First give a short transition that the ontology slices are ready, then explicitly ask whether to enter skill definition now.',
      'If the user already explicitly asked to continue into skill definition in the current context, proceed directly under the coach skill rules; otherwise ask the confirmation question only.',
    ]

    if (zeroProjected) {
      lines.push(
        'Note: the ontology extraction returned zero projected slices (projected_count = 0).',
        'Acknowledge to the user that no usable ontology slices were produced from the current materials,',
        'and ask whether to supplement additional materials before proceeding to skill definition.',
      )
    }

    lines.push(
      '',
      'resume_payload:',
      '```json',
      serialized,
      '```',
    )

    return lines.join('\n')
  }

  return serialized
}
