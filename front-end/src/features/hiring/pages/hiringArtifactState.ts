import { HiringCollectionStage } from '@/infra/api'
import type { SandboxMessage, SandboxToolCall } from '@/infra/sandbox/sandbox-api'

import type {
  ArtifactDisplayData,
  ChatFile,
  ChatMessage,
  DownstreamRunKey,
  DownstreamRunsSnapshot,
  DownstreamRunState,
  DownstreamRunStatus,
  StageGateData,
  ToolStep,
} from './hiringPageTypes'
import {
  getBlockedIncomingArtifactReason,
  normalizeIncomingArtifactTerminal,
  shouldDisplayArtifactInConversation,
} from './hiringArtifactGuards'
import { extractLatestMaterialRequestedCategories } from './materialRequestedCategories'
import {
  buildConfirmationGateContextSignature,
  buildConfirmationGateEventSignature,
  isConfirmationGateArtifactType,
} from './utils/hiringConfirmationArtifacts'
import { buildSkillGenerationPayload } from './utils/hiringDownstreamTriggers'
import { extractVisibleUserMessageFromEnvelope } from './utils/hiringVisibleUserMessageEnvelope'
import type { HiringUiStage } from './hiringWorkflowViewModel'

function mkHistoricalId(prefix: string, index: number) {
  return `historical_${prefix}_${index}`
}

function normalizeHistoricalToolName(toolName: string): string {
  const trimmed = toolName.trim()
  if (!trimmed) {
    return 'tool'
  }

  return trimmed.startsWith('streaming.') ? trimmed.slice('streaming.'.length) : trimmed
}

function buildHistoricalToolSteps(
  toolCalls: SandboxToolCall[] | null | undefined,
  messageIndex: number,
): ToolStep[] | undefined {
  if (!Array.isArray(toolCalls) || toolCalls.length === 0) {
    return undefined
  }

  const steps = toolCalls
    .map((toolCall, toolIndex): ToolStep | null => {
      const name = normalizeHistoricalToolName(String(toolCall.toolName ?? ''))
      if (!name) {
        return null
      }

      return {
        id: `historical_tool_${messageIndex}_${toolIndex}`,
        name,
        status: 'done',
        args: toolCall.arguments,
        result: toolCall.result,
      }
    })
    .filter((step): step is ToolStep => step !== null)

  return steps.length > 0 ? steps : undefined
}

const HISTORICAL_FILE_ATTACHMENT_PATTERN =
  /\[FILE_URL:[^\]]+\]\s*\r?\nAttached file:\s*(.+?)\s*\(([^)\r\n]+)\)\s*/gi

const ARTIFACT_META_KEYS = new Set([
  'kind',
  'artifactType',
  'artifact_type',
  'label',
  'skillName',
  'skill_name',
  'stage',
  'isTerminal',
  'is_terminal',
  'terminal',
  'displayHint',
  'display_hint',
  'fileUrl',
  'file_url',
  'fileName',
  'file_name',
  'display_name',
  'mimeType',
  'mime_type',
  'fileSizeBytes',
  'file_size_bytes',
])

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

function firstNonEmptyString(...values: unknown[]): string {
  for (const value of values) {
    if (typeof value !== 'string') {
      continue
    }

    const trimmed = value.trim()
    if (trimmed) {
      return trimmed
    }
  }

  return ''
}

function normalizeArtifactResultKey(key: string): string {
  return key.trim().toLowerCase().replace(/-/g, '_')
}

function assignArtifactResultValue(
  result: Record<string, unknown>,
  key: string,
  value: string,
): void {
  const normalizedKey = normalizeArtifactResultKey(key)
  const trimmedValue = value.trim()
  if (!trimmedValue) {
    return
  }

  switch (normalizedKey) {
    case 'type':
    case 'artifact_type':
      result.artifactType = trimmedValue
      break
    case 'kind':
      result.kind = trimmedValue
      break
    case 'stage':
      result.stage = trimmedValue
      break
    case 'terminal':
    case 'is_terminal':
      result.isTerminal = trimmedValue.toLowerCase() === 'true'
      break
    case 'file_url':
    case 'fileurl':
      result.fileUrl = trimmedValue
      break
    case 'file_name':
    case 'filename':
    case 'display_name':
      result.fileName = trimmedValue
      break
    case 'mime_type':
    case 'mimetype':
      result.mimeType = trimmedValue
      break
    case 'label':
      result.label = trimmedValue
      break
    case 'skill_name':
    case 'skillname':
      result.skillName = trimmedValue
      break
    case 'display_hint':
    case 'displayhint':
      result.displayHint = trimmedValue
      break
    default:
      result[normalizedKey] = trimmedValue
      break
  }
}

function parseSpaceSeparatedArtifactResult(inner: string): Record<string, unknown> | null {
  const result: Record<string, unknown> = {}
  const pairRegex = /(\w+)=(\S+)/g
  let pairMatch: RegExpExecArray | null
  while ((pairMatch = pairRegex.exec(inner)) !== null) {
    assignArtifactResultValue(result, pairMatch[1], pairMatch[2])
  }

  return result.artifactType ? result : null
}

function parseTaggedArtifactResult(text: string): Record<string, unknown> {
  const result: Record<string, unknown> = {}
  const tagRegex = /\[([A-Za-z_][A-Za-z0-9_-]*)\s*[:=]\s*([^\]]*)\]/g
  let tagMatch: RegExpExecArray | null
  while ((tagMatch = tagRegex.exec(text)) !== null) {
    assignArtifactResultValue(result, tagMatch[1], tagMatch[2])
  }

  return result
}

function isImportableTemplatePackageUrl(value: unknown): value is string {
  if (typeof value !== 'string') {
    return false
  }

  const trimmed = value.trim()
  if (!trimmed) {
    return false
  }

  if (trimmed.startsWith('/app/memory/media-cache/') || trimmed.startsWith('/workspace/')) {
    return false
  }

  return /^https?:\/\//i.test(trimmed) || trimmed.startsWith('/media/') || trimmed.startsWith('media/')
}

function inferArtifactResultFromText(text: string): Record<string, unknown> | null {
  const result = parseTaggedArtifactResult(text)

  if (!result.artifactType && /\btemplate_package\b/i.test(text)) {
    result.artifactType = 'template_package'
  }

  if (!result.artifactType) {
    const typeMatch = /\b(?:artifactType|artifact_type|type|TYPE)\s*[:=]\s*["'`]?([A-Za-z][A-Za-z0-9_-]*)/i.exec(text)
    if (typeMatch?.[1]) {
      result.artifactType = typeMatch[1]
    }
  }

  if (!result.fileUrl) {
    const fileUrlMatch = /\[FILE_URL:([^\]|]+)(?:\|[^\]]+)?\]/i.exec(text)
      ?? /\bFILE_URL\s*[:=]\s*(\S+)/i.exec(text)
      ?? /(\/media\/[A-Za-z0-9_.-]+)/.exec(text)
    if (fileUrlMatch?.[1]) {
      result.fileUrl = fileUrlMatch[1].trim()
    }
  }

  const publishedNameMatch = /Artifact published:\s*([^\r\n\[]+)/i.exec(text)
    ?? /Published artifact:\s*([^\r\n\[]+)/i.exec(text)
  const zipNameMatch = /([^\\/\s\]]+\.zip)\b/i.exec(text)
  const publishedName = publishedNameMatch?.[1]?.trim() ?? zipNameMatch?.[1]?.trim()
  if (publishedName && !result.fileName) {
    result.fileName = publishedName
  }

  if (result.artifactType === 'template_package' && result.fileUrl) {
    if (!isImportableTemplatePackageUrl(result.fileUrl)) {
      return null
    }

    result.kind = 'file'
    result.isTerminal = result.isTerminal ?? true
  }

  return result
}

export function parseArtifactFromToolResultText(text: string): Record<string, unknown> | null {
  const dataArtifactMatch = /Data artifact emitted:\s*\[([^\]]*)\]/i.exec(text)
  if (dataArtifactMatch?.[1]?.trim()) {
    return parseSpaceSeparatedArtifactResult(dataArtifactMatch[1].trim())
  }

  const inferred = inferArtifactResultFromText(text)
  if (inferred?.artifactType) {
    return inferred
  }

  return null
}

function parseHistoricalFileSize(label: string): number {
  const match = /([\d.]+)\s*(B|KB|MB|GB)?/i.exec(label.trim())
  if (!match) {
    return 0
  }

  const value = Number.parseFloat(match[1])
  if (!Number.isFinite(value) || value <= 0) {
    return 0
  }

  const unit = (match[2] ?? 'B').toUpperCase()
  if (unit === 'GB') return Math.round(value * 1024 * 1024 * 1024)
  if (unit === 'MB') return Math.round(value * 1024 * 1024)
  if (unit === 'KB') return Math.round(value * 1024)
  return Math.round(value)
}

function hasHistoricalFileAttachment(content: string): boolean {
  return /\[FILE_URL:[^\]]+\]\s*\r?\nAttached file:\s*(.+?)\s*\(([^)\r\n]+)\)\s*/i.test(content)
}

function isTemplateBootstrapPrompt(content: string): boolean {
  if (!content.includes('[FILE_URL:')) {
    return false
  }

  const hasBootstrapInstruction =
    content.includes('请在雇佣教练入口规则下读取上述目标模板目录中的 manifest.json') ||
    content.includes('请读取上述工作区目录中的 manifest.json')

  return content.includes('模板包已解压到工作区目录') && hasBootstrapInstruction
}

function shouldHideHistoricalUserMessage(content: string): boolean {
  const trimmed = content.trim()
  if (!trimmed) {
    return true
  }

  if (trimmed.startsWith('[Internal ') || trimmed.startsWith('[System ')) {
    return true
  }

  if (isTemplateBootstrapPrompt(trimmed)) {
    return true
  }

  if (!trimmed.startsWith('[FILE_URL:')) {
    return false
  }

  return !hasHistoricalFileAttachment(trimmed)
}

function shouldSuppressAssistantAfterHistoricalUserMessage(content: string): boolean {
  const trimmed = content.trim()
  if (!trimmed) {
    return true
  }

  return false
}

function normalizeHistoricalUserMessage(content: string): { content: string; files?: ChatFile[] } | null {
  const trimmed = content.trim()
  if (!trimmed) {
    return null
  }

  const visibleEnvelopeContent = extractVisibleUserMessageFromEnvelope(trimmed)
  if (visibleEnvelopeContent) {
    return { content: visibleEnvelopeContent }
  }

  if (shouldHideHistoricalUserMessage(trimmed)) {
    return null
  }

  const files: ChatFile[] = []
  const visibleContent = trimmed
    .replace(HISTORICAL_FILE_ATTACHMENT_PATTERN, (_match, fileName: string, sizeLabel: string) => {
      const name = fileName.trim()
      if (name) {
        files.push({
          id: mkHistoricalId('file', files.length),
          name,
          size: parseHistoricalFileSize(sizeLabel),
          status: '已解析',
          type: 'file',
        })
      }

      return '\n'
    })
    .trim()

  // 模板自动引导也以 [FILE_URL:] 开头，但不会携带 Attached file 元数据，仍应视为内部消息。
  if (trimmed.startsWith('[FILE_URL:') && files.length === 0) {
    return null
  }

  if (!visibleContent && files.length === 0) {
    return null
  }

  return {
    content: visibleContent,
    files: files.length > 0 ? files : undefined,
  }
}

export const DOWNSTREAM_ARTIFACT_TRACKS: Record<string, { key: DownstreamRunKey; status: DownstreamRunStatus }> = {
  material_handoff_ready: { key: 'material-handoff', status: 'waiting_confirm' },
  ontology_slice_extraction_progress: { key: 'ontology-slice-extraction', status: 'running' },
  ontology_slice_extraction_done: { key: 'ontology-slice-extraction', status: 'completed' },
  skill_definition_entry_ready: { key: 'skill-definition-entry', status: 'waiting_confirm' },
  skill_definition_ready: { key: 'skill-generation', status: 'waiting_confirm' },
  ontology_projection_ready: { key: 'ontology-projection', status: 'waiting_confirm' },
  ontology_projection_progress: { key: 'ontology-projection', status: 'running' },
  ontology_projection_done: { key: 'ontology-projection', status: 'completed' },
  skill_generation_ready: { key: 'skill-generation', status: 'waiting_confirm' },
  skill_projection_binding_ready: { key: 'skill-generation', status: 'running' },
  skill_generation_progress: { key: 'skill-generation', status: 'running' },
  skill_generation_done: { key: 'skill-generation', status: 'completed' },
  external_system_entry_ready: { key: 'external-system-entry', status: 'waiting_confirm' },
  packaging_testcases_ready: { key: 'packaging-test-cases', status: 'waiting_confirm' },
  packaging_testcases_progress: { key: 'packaging-test-cases', status: 'running' },
  packaging_testcases_done: { key: 'packaging-test-cases', status: 'completed' },
}

export function isCompletedOntologySliceExtractionResult(value: unknown): boolean {
  const record = asPlainObject(value)
  if (!record) {
    return false
  }

  if (record.status === 'blocked') {
    return false
  }

  const completedSlices = record.completed_slices ?? record.completedSlices
  if (typeof completedSlices === 'number') {
    return Number.isFinite(completedSlices) && completedSlices > 0
  }

  const slicePaths = record.slice_paths ?? record.slicePaths
  return Array.isArray(slicePaths) && slicePaths.length > 0
}

export function isBlockedOntologySliceExtractionResult(value: unknown): boolean {
  const record = asPlainObject(value)
  if (!record) {
    return false
  }

  if (record.status === 'blocked') {
    return true
  }

  const completedSlices = record.completed_slices ?? record.completedSlices
  return typeof completedSlices === 'number' && Number.isFinite(completedSlices) && completedSlices <= 0
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

const CONFIRMATION_GATE_COMPLETIONS: Partial<Record<string, DownstreamRunKey[]>> = {
  material_handoff_summary: ['material-handoff'],
  ontology_slice_extraction_progress: ['material-handoff'],
  ontology_slice_extraction_done: ['material-handoff'],
  skill_workorder_progress: ['skill-definition-entry'],
  skill_definition_ready: ['skill-definition-entry'],
  skill_workorder_summary: ['skill-definition-entry'],
  ontology_projection_ready: ['skill-definition-entry'],
  ontology_projection_progress: ['skill-definition-entry'],
  ontology_projection_done: ['skill-definition-entry'],
  skill_generation_ready: ['skill-definition-entry'],
  skill_generation_progress: ['skill-definition-entry'],
  skill_generation_done: ['skill-definition-entry'],
  external_workorder_progress: ['external-system-entry'],
  external_workorder_summary: ['external-system-entry'],
  external_config_committed: ['external-system-entry'],
}

export function applyDownstreamConfirmationCompletions(
  runs: DownstreamRunsSnapshot,
  artifact: ArtifactDisplayData,
  updatedAt: string,
): DownstreamRunsSnapshot {
  const completedKeys = CONFIRMATION_GATE_COMPLETIONS[artifact.artifactType]
  if (!completedKeys || completedKeys.length === 0) {
    return runs
  }

  let nextRuns = runs
  for (const key of completedKeys) {
    const currentRun = nextRuns[key]
    if (currentRun?.status !== 'waiting_confirm') {
      continue
    }

    if (nextRuns === runs) {
      nextRuns = { ...runs }
    }

    nextRuns[key] = {
      key,
      status: 'completed',
      artifactType: artifact.artifactType,
      label: artifact.label,
      displayHint: artifact.displayHint,
      updatedAt,
      data: artifact.data,
    }
  }

  return nextRuns
}

export function shouldDismissSkillConfirmationAfterApproval(run: DownstreamRunState | null): boolean {
  return run?.status === 'waiting_confirm' && run.artifactType === 'skill_definition_ready'
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

function toOpenQuestionText(value: unknown): string | null {
  if (typeof value === 'string') {
    const text = value.trim()
    return text ? text : null
  }

  const record = asPlainObject(value)
  if (!record) {
    return null
  }

  const question = firstStringValue(
    record.question,
    record.title,
    record.missing_rule,
    record.missingRule,
    record.summary,
  )
  if (!question) {
    return null
  }

  const options = Array.isArray(record.options)
    ? record.options
        .map(option => {
          if (typeof option === 'string') return option.trim()

          const optionRecord = asPlainObject(option)
          return firstStringValue(optionRecord?.label, optionRecord?.value, optionRecord?.description)
        })
        .filter((option): option is string => Boolean(option))
    : []

  return options.length > 0 ? `${question}（选项：${options.join(' / ')}）` : question
}

function firstStringValue(...values: unknown[]): string {
  for (const value of values) {
    if (typeof value !== 'string') continue
    const text = value.trim()
    if (text) return text
  }

  return ''
}

const MATERIAL_HANDOFF_CONFIRMATION_KEYS = new Set([
  'context_signature',
  'contextSignature',
  'status',
  'message',
  'next_artifact',
  'nextArtifact',
])

function hasStructuredMaterialHandoffFields(record: Record<string, unknown> | null): boolean {
  if (!record) {
    return false
  }

  if (Array.isArray(record.items) && record.items.length > 0) {
    return true
  }

  if (typeof record.total_items === 'number' || typeof record.totalItems === 'number') {
    return true
  }

  return firstStringValue(record.workspace_root, record.workspaceRoot, record.template_slug, record.templateSlug) !== ''
}

function hasMaterialHandoffPayloadShape(record: Record<string, unknown> | null): record is Record<string, unknown> {
  if (hasStructuredMaterialHandoffFields(record)) {
    return true
  }

  return record !== null && firstStringValue(record.summary) !== ''
}

function stripMaterialHandoffConfirmationFields(record: Record<string, unknown>): Record<string, unknown> {
  const payload: Record<string, unknown> = {}
  for (const [key, value] of Object.entries(record)) {
    if (MATERIAL_HANDOFF_CONFIRMATION_KEYS.has(key)) {
      continue
    }
    payload[key] = value
  }

  return payload
}

function getMaterialHandoffPayloadRecord(value: unknown): Record<string, unknown> | null {
  const record = asPlainObject(value)
  if (!record) {
    return null
  }

  const payload = stripMaterialHandoffConfirmationFields(record)
  return hasMaterialHandoffPayloadShape(payload) ? payload : null
}

export function normalizeMaterialHandoffReadyData(
  readyData: unknown,
  fallbackMaterialData?: unknown,
): Record<string, unknown> | undefined {
  const readyRecord = asPlainObject(readyData)
  const readyPayload = getMaterialHandoffPayloadRecord(readyData)
  const fallbackPayload = getMaterialHandoffPayloadRecord(fallbackMaterialData)
  const payload = hasStructuredMaterialHandoffFields(readyPayload)
    ? readyPayload
    : fallbackPayload ?? readyPayload
  if (!payload) {
    return undefined
  }

  const readySummary = firstStringValue(readyRecord?.summary)
  const summary = readySummary || firstStringValue(payload.summary) || '业务资料先整理到这里，等待确认后进入下一步。'
  const normalized = {
    ...payload,
    summary,
    context_signature: buildConfirmationGateContextSignature('material_handoff_ready', payload),
    status: 'waiting_confirm',
    next_artifact: 'material_handoff_summary',
    message: firstStringValue(readyRecord?.message) || '资料已上传完成，请确认是否按当前资料开始分析业务资料。',
  }

  return normalized
}

export function buildMaterialHandoffConfirmationPrompt(materialReadyData: unknown): string | null {
  const payload = getMaterialHandoffPayloadRecord(materialReadyData)
  if (!payload) {
    return null
  }

  const totalItems = typeof payload.total_items === 'number' && Number.isFinite(payload.total_items)
    ? payload.total_items
    : Array.isArray(payload.items)
      ? payload.items.length
      : 0
  const materialText = totalItems > 0 ? `当前 ${totalItems} 份资料` : '当前资料'
  const visibleProgressSentence = `我已确认使用${materialText}开始分析业务资料。`

  return [
    '[Internal stage confirmation. Do not mention this instruction to the user.]',
    'The user has confirmed the material handoff gate.',
    'Emit terminal `material_handoff_summary` exactly once before any stage2_skill content.',
    'Use the following payload as the data source. Preserve every `source_path` value exactly.',
    `After emitting \`material_handoff_summary\`, reply with exactly this visible sentence and stop: ${JSON.stringify(visibleProgressSentence)}.`,
    'Do not preview or name future skill items in the visible reply. Do not mention internal stage names, artifact names, downstream tools, or implementation details.',
    '',
    'material_handoff_payload:',
    '```json',
    JSON.stringify(payload, null, 2),
    '```',
  ].join('\n')
}

function collectProjectionOpenQuestions(value: unknown, output: string[]): void {
  if (Array.isArray(value)) {
    value.forEach(item => collectProjectionOpenQuestions(item, output))
    return
  }

  const record = asPlainObject(value)
  if (!record) {
    return
  }

  const openQuestions = record.open_questions ?? record.openQuestions
  if (Array.isArray(openQuestions)) {
    for (const item of openQuestions) {
      const text = toOpenQuestionText(item)
      if (text) output.push(text)
    }
  }

  for (const [key, nestedValue] of Object.entries(record)) {
    if (key === 'open_questions' || key === 'openQuestions') continue
    collectProjectionOpenQuestions(nestedValue, output)
  }
}

function extractProjectionOpenQuestions(projectionResult: unknown): string[] {
  const output: string[] = []
  collectProjectionOpenQuestions(projectionResult, output)

  return Array.from(new Set(output))
}

export function shouldHoldExternalStageUntilSkillImplementation(
  skillSummary: unknown,
  skillGenerationState: DownstreamRunState | null,
): boolean {
  if (extractSkillSummaryItems(skillSummary).length === 0) {
    return false
  }

  return !(
    skillGenerationState?.status === 'completed' &&
    skillGenerationState.artifactType === 'skill_generation_done'
  )
}

export function buildUiStageOverrides(
  rawStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
  ontologyExtractionState: DownstreamRunState | null,
  skillGenerationState: DownstreamRunState | null,
  holdExternalStage: boolean,
  externalConfigCommitted = false,
): Map<HiringUiStage, 'running' | 'completed' | 'failed'> {
  const next = new Map(rawStageOverrides)

  if (
    ontologyExtractionState?.status === 'completed' &&
    isCompletedOntologySliceExtractionResult(ontologyExtractionState.data)
  ) {
    next.set(HiringCollectionStage.Material, 'completed')
    // 本体切片抽取完成意味着可以进入技能阶段了；将 Skill 置为 running，
    // 使右侧卡片 badge 与顶部阶段胶囊保持一致，避免卡片仍显示"等待中"。
    // skillGenerationState 后续到达时会再修正为 completed / failed。
    if (next.get(HiringCollectionStage.Skill) !== 'completed') {
      next.set(HiringCollectionStage.Skill, 'running')
    }
  } else if (
    ontologyExtractionState?.status === 'completed' &&
    isBlockedOntologySliceExtractionResult(ontologyExtractionState.data)
  ) {
    next.set(HiringCollectionStage.Material, 'running')
    if (!skillGenerationState && next.get(HiringCollectionStage.Skill) !== 'completed') {
      next.delete(HiringCollectionStage.Skill)
    }
  } else if (ontologyExtractionState?.status === 'running') {
    next.set(HiringCollectionStage.Material, 'running')
    if (!skillGenerationState && next.get(HiringCollectionStage.Skill) !== 'completed') {
      next.delete(HiringCollectionStage.Skill)
    }
  }

  // 阶段 2 现在覆盖“技能定义 + 技能生成”两个子步骤。
  // 因此只要技能生成尚未完成，主技能阶段就必须保持进行中；
  // 同时外部阶段也不能抢先成为当前活跃阶段。
  if (holdExternalStage) {
    next.set(HiringCollectionStage.Skill, 'running')

    if (next.get(HiringCollectionStage.External) !== 'completed') {
      next.delete(HiringCollectionStage.External)
    }
  }

  if (
    skillGenerationState?.status === 'completed' &&
    skillGenerationState.artifactType === 'skill_generation_done'
  ) {
    next.set(HiringCollectionStage.Skill, 'completed')
    // 技能生成已完成且外部尚未保存/跳过：将 External 阶段置为 running，
    // 因为 external_system_entry_ready 确认门已由系统层发出。
    if (!externalConfigCommitted && next.get(HiringCollectionStage.External) !== 'completed') {
      next.set(HiringCollectionStage.External, 'running')
    }
  } else if (skillGenerationState?.status === 'failed') {
    next.set(HiringCollectionStage.Skill, 'failed')
  }

  if (externalConfigCommitted) {
    // external_config_committed 是外部配置保存/跳过的终态信号。
    // 能走到这里说明前序资料与技能阶段已经完成，允许补齐丢失的 WS 阶段事件。
    next.set(HiringCollectionStage.Material, 'completed')
    next.set(HiringCollectionStage.Skill, 'completed')
    next.set(HiringCollectionStage.External, 'completed')
  }

  return next
}

export function shouldSuppressStageGate(
  stageGate: StageGateData,
  downstreamRuns: DownstreamRunsSnapshot,
): boolean {
  if (!stageGate.canProceed) {
    return false
  }

  const completedHiringStage = resolveHiringStageFromWs(stageGate.skillName, stageGate.completedStage)
  const nextHiringStage = resolveHiringStageFromWs(stageGate.skillName, stageGate.nextStage)

  if (
    completedHiringStage === HiringCollectionStage.Material
    && nextHiringStage === HiringCollectionStage.Skill
    && (
      downstreamRuns['ontology-slice-extraction']?.status !== 'completed' ||
      !isCompletedOntologySliceExtractionResult(downstreamRuns['ontology-slice-extraction']?.data)
    )
  ) {
    return true
  }

  if (
    completedHiringStage === HiringCollectionStage.Skill
    && nextHiringStage === HiringCollectionStage.External
    && downstreamRuns['skill-generation']?.status !== 'completed'
  ) {
    return true
  }

  return false
}

export function queueOntologySliceExtractionRun(
  downstreamRuns: DownstreamRunsSnapshot,
  materialSummary: unknown,
  nowIso: string,
): { queued: boolean; nextRuns: DownstreamRunsSnapshot; signature: string } {
  const signature = JSON.stringify(materialSummary ?? {})
  const currentRun = downstreamRuns['ontology-slice-extraction']
  if (currentRun?.status === 'running') {
    return { queued: false, nextRuns: downstreamRuns, signature }
  }

  return {
    queued: true,
    signature,
    nextRuns: {
      ...downstreamRuns,
      'ontology-slice-extraction': {
        key: 'ontology-slice-extraction',
        status: 'running',
        artifactType: 'ontology_slice_extraction_progress',
        label: '正在分析业务资料，等待下游进度更新。',
        displayHint: 'progress',
        updatedAt: nowIso,
        data: materialSummary,
      },
    },
  }
}

export function buildMaterialHandoffReadyArtifact(summary: string, materialData?: unknown): ArtifactDisplayData {
  const normalizedSummary = summary.trim()
  const data = normalizeMaterialHandoffReadyData({ summary: normalizedSummary }, materialData)
    ?? {
      context_signature: buildConfirmationGateContextSignature(
        'material_handoff_ready',
        { summary: normalizedSummary },
      ),
      status: 'waiting_confirm',
      summary: normalizedSummary,
      next_artifact: 'material_handoff_summary',
      message: '资料已上传完成，请确认是否按当前资料开始分析业务资料。',
    }

  return {
    kind: 'data',
    artifactType: 'material_handoff_ready',
    label: '等待确认是否开始分析业务资料',
    skillName: 'employment-coach-conversation',
    stage: 'stage1_material',
    isTerminal: false,
    displayHint: 'badge',
    data,
  }
}

export function buildSkillDefinitionEntryReadyArtifact(
  materialSummary: unknown,
  ontologyResult: unknown,
): ArtifactDisplayData {
  const contextSignature = buildConfirmationGateContextSignature(
    'skill_definition_entry_ready',
    { materialSummary, ontologyResult },
  )

  return {
    kind: 'data',
    artifactType: 'skill_definition_entry_ready',
    label: '业务资料分析完成，是否进入技能定义？',
    skillName: 'employment-coach-conversation',
    stage: 'stage2_skill',
    isTerminal: false,
    displayHint: 'badge',
    data: {
      context_signature: contextSignature,
      status: 'waiting_confirm',
      trigger_after: 'ontology_slice_extraction_done',
      message: '业务资料分析完成，是否进入技能定义？',
    },
  }
}

export function buildSkillGenerationReadyArtifact(
  skillSummary: unknown,
  projectionResult: unknown,
): ArtifactDisplayData | null {
  const payload = buildSkillGenerationPayload(skillSummary, projectionResult)
  const projectionRecord = asPlainObject(projectionResult)
  if (!payload || !projectionRecord) {
    return null
  }

  const projectionPaths = Array.isArray(projectionRecord.projection_paths)
    ? projectionRecord.projection_paths.filter((path): path is string => typeof path === 'string' && path.trim().length > 0)
    : Array.isArray(projectionRecord.projectionPaths)
      ? projectionRecord.projectionPaths.filter((path): path is string => typeof path === 'string' && path.trim().length > 0)
      : []
  const confirmedSkillSlugs = Array.isArray(payload.confirmed_skill_slugs)
    ? payload.confirmed_skill_slugs.filter((slug): slug is string => typeof slug === 'string' && slug.trim().length > 0)
    : []
  const projectedCount = typeof projectionRecord.projected_count === 'number'
    ? projectionRecord.projected_count
    : typeof projectionRecord.projectedCount === 'number'
      ? projectionRecord.projectedCount
      : projectionPaths.length
  const openQuestions = extractProjectionOpenQuestions(projectionResult)
  const contextSignature = buildConfirmationGateContextSignature(
    'skill_generation_ready',
    { skillSummary, projectionResult },
  )
  const summary = openQuestions.length > 0
    ? `技能数据已匹配完成，仍有 ${openQuestions.length} 个生成前确认项；确认口径后可直接生成技能实现。`
    : '技能数据已匹配完成，等待确认开始生成技能实现。'

  return {
    kind: 'data',
    artifactType: 'skill_generation_ready',
    label: openQuestions.length > 0 ? '等待确认生成技能实现（含生成前确认项）' : '等待确认生成技能实现',
    skillName: 'employment-coach-conversation',
    stage: 'stage2_skill',
    isTerminal: false,
    displayHint: 'badge',
    data: {
      context_signature: contextSignature,
      status: 'waiting_confirm',
      workspace_root: typeof payload.workspace_root === 'string' ? payload.workspace_root : undefined,
      template_slug: typeof payload.template_slug === 'string' ? payload.template_slug : undefined,
      pending_skill_count: confirmedSkillSlugs.length,
      skill_names: confirmedSkillSlugs,
      projected_count: projectedCount,
      projection_paths: projectionPaths,
      readiness_status: openQuestions.length > 0 ? 'ready_with_confirmation_items' : 'ready',
      open_questions: openQuestions.length > 0 ? openQuestions : undefined,
      summary,
      next_step: openQuestions.length > 0
        ? '等待用户确认生成前业务口径，然后开始生成技能实现'
        : '等待用户确认开始生成技能实现',
    },
  }
}

export function normalizeArtifactDisplayData(raw: Record<string, unknown>): ArtifactDisplayData {
  const parameters = asPlainObject(raw.parameters)
  const artifactPayload = parameters && (parameters.artifactType || parameters.artifact_type)
    ? parameters
    : raw
  const artifactType = String(artifactPayload.artifactType ?? artifactPayload.artifact_type ?? 'generic')
  const artifactPayloadData = tryParseJsonRecord(artifactPayload.data)
  const inferredFileArtifact = artifactType === 'template_package' && (
    artifactPayload.fileUrl != null ||
    artifactPayload.file_url != null ||
    artifactPayload.url != null ||
    artifactPayload.downloadUrl != null ||
    artifactPayload.download_url != null ||
    artifactPayloadData?.fileUrl != null ||
    artifactPayloadData?.file_url != null ||
    artifactPayloadData?.url != null ||
    artifactPayloadData?.downloadUrl != null ||
    artifactPayloadData?.download_url != null
  )
  const artifactKind = (inferredFileArtifact
    ? 'file'
    : String(artifactPayload.kind ?? 'data')) as 'file' | 'data'
  const label = artifactPayload.label != null ? String(artifactPayload.label) : undefined
  const skillName = (artifactPayload.skillName ?? artifactPayload.skill_name) != null ? String(artifactPayload.skillName ?? artifactPayload.skill_name) : undefined
  const stage = artifactPayload.stage != null ? String(artifactPayload.stage) : undefined
  // 兼容三种字段名：isTerminal（WS 实时）、is_terminal（snake_case）、terminal（历史 tool call arguments）
  const isTerminal = Boolean(artifactPayload.isTerminal ?? artifactPayload.is_terminal ?? artifactPayload.terminal)
  const displayHint = artifactPayload.displayHint != null
    ? String(artifactPayload.displayHint)
    : artifactPayload.display_hint != null
      ? String(artifactPayload.display_hint)
      : undefined

  const artifactData: ArtifactDisplayData = {
    kind: artifactKind,
    artifactType,
    label,
    skillName,
    stage,
    isTerminal,
    displayHint,
  }

  if (artifactKind === 'file') {
    artifactData.fileUrl = firstNonEmptyString(
      artifactPayload.fileUrl,
      artifactPayload.file_url,
      artifactPayload.url,
      artifactPayload.downloadUrl,
      artifactPayload.download_url,
      artifactPayloadData?.fileUrl,
      artifactPayloadData?.file_url,
      artifactPayloadData?.url,
      artifactPayloadData?.downloadUrl,
      artifactPayloadData?.download_url,
    )
    // 兼容历史 tool call 中的 display_name 字段（WS 实时用 fileName/file_name）
    artifactData.fileName = firstNonEmptyString(
      artifactPayload.fileName,
      artifactPayload.file_name,
      artifactPayload.display_name,
      artifactPayloadData?.fileName,
      artifactPayloadData?.file_name,
      artifactPayloadData?.display_name,
      label,
    ) || 'file'
    artifactData.mimeType = String(artifactPayload.mimeType ?? artifactPayload.mime_type ?? '')
  } else {
    if (artifactPayload.data != null) {
      artifactData.data = typeof artifactPayload.data === 'string' ? JSON.parse(artifactPayload.data) : artifactPayload.data
    } else {
      const fallback: Record<string, unknown> = {}
      for (const [key, value] of Object.entries(artifactPayload)) {
        if (!ARTIFACT_META_KEYS.has(key)) fallback[key] = value
      }
      artifactData.data = Object.keys(fallback).length > 0 ? fallback : undefined
    }
  }

  return artifactData
}

export function extractArtifactFromToolCall(toolCall: SandboxToolCall): ArtifactDisplayData | null {
  const payload = tryParseJsonRecord(toolCall.arguments)
    ?? (toolCall.result ? parseArtifactFromToolResultText(toolCall.result) : null)
  if (!payload) {
    return null
  }

  const parameters = asPlainObject(payload.parameters)
  const looksLikeArtifactPayload = Boolean(
    payload.artifactType
    || payload.artifact_type
    || parameters?.artifactType
    || parameters?.artifact_type,
  )
  if (!toolCall.toolName?.endsWith('emit_artifact') && !looksLikeArtifactPayload) {
    return null
  }

  try {
    const artifact = normalizeArtifactDisplayData(payload)
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
  latestMaterialDraft: unknown | null
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
  let downstreamRuns: DownstreamRunsSnapshot = {}
  let artifactIndex = 0
  let suppressNextAssistantVisibleMessage = false
  let latestMaterialDraft: unknown | null = null
  let latestMaterialSummary: unknown | null = null
  let hasOntologyExtractionDone = false
  let latestSkillSummary: unknown | null = null
  let latestProjectionResult: unknown | null = null
  let hasSkillGenerationDone = false
  let hasExternalSystemEntryConfirmed = false
  let hasExternalConfigCommitted = false
  const historicalGateSignatures = new Set<string>()
  let pendingHistoricalToolSteps: ToolStep[] = []

  for (const [sandboxMessageIndex, message] of sandboxMessages.entries()) {
    if (message.type === 'user_message') {
      pendingHistoricalToolSteps = []
      const historicalUserText = String(message.text ?? '')
      suppressNextAssistantVisibleMessage = shouldSuppressAssistantAfterHistoricalUserMessage(historicalUserText)
      const userMessage = normalizeHistoricalUserMessage(historicalUserText)
      if (userMessage) {
        messages.push({
          id: mkHistoricalId('user', messages.length),
          role: 'user',
          content: userMessage.content,
          files: userMessage.files,
        })
      }
      continue
    }

    if (message.type !== 'assistant_message') {
      continue
    }

    const currentToolSteps = buildHistoricalToolSteps(message.toolCalls, sandboxMessageIndex)
    if (currentToolSteps) {
      pendingHistoricalToolSteps = [...pendingHistoricalToolSteps, ...currentToolSteps]
    }

    for (const toolCall of message.toolCalls ?? []) {
      const artifact = extractArtifactFromToolCall(toolCall)
      if (!artifact) {
        continue
      }

      if (artifact.artifactType === 'material_handoff_ready') {
        artifact.data = normalizeMaterialHandoffReadyData(artifact.data, latestMaterialDraft)
      }
      artifact.isTerminal = normalizeIncomingArtifactTerminal(artifact.artifactType, Boolean(artifact.isTerminal))
      const projectionForGate = artifact.artifactType === 'ontology_projection_done' && artifact.isTerminal
        ? artifact.data ?? null
        : latestProjectionResult
      const blockedArtifactReason = getBlockedIncomingArtifactReason(artifact.artifactType, {
        hasMaterialSummary: latestMaterialSummary !== null,
        hasOntologyExtractionDone,
        hasSkillSummary: latestSkillSummary !== null,
        hasProjectionResult: projectionForGate !== null,
        canUseProjectionForSkillGeneration: buildSkillGenerationPayload(
          latestSkillSummary,
          projectionForGate,
        ) !== null,
        hasSkillGenerationDone,
        hasExternalSystemEntryConfirmed,
        hasExternalConfigCommitted,
      }, {
        isTerminal: artifact.isTerminal,
        kind: artifact.kind,
        data: artifact.data,
      })
      if (blockedArtifactReason) {
        continue
      }

      if (isConfirmationGateArtifactType(artifact.artifactType)) {
        const gateSignature = buildConfirmationGateEventSignature(artifact)
        if (historicalGateSignatures.has(gateSignature)) {
          continue
        }
        historicalGateSignatures.add(gateSignature)
      }

      const shouldDisplayArtifact = shouldDisplayArtifactInConversation(
        artifact.artifactType,
        artifact.isTerminal,
      )
      if (shouldDisplayArtifact) {
        messages.push({
          id: mkHistoricalId('artifact', artifactIndex),
          role: 'artifact',
          content: artifact.label ?? artifact.artifactType,
          artifact,
        })
        artifactIndex += 1
      }

      if (artifact.artifactType === 'material_handoff_summary' && artifact.isTerminal) {
        latestMaterialSummary = artifact.data ?? null
        latestMaterialDraft = artifact.data ?? latestMaterialDraft
      } else if (artifact.artifactType === 'material_collection_progress') {
        latestMaterialDraft = artifact.data ?? latestMaterialDraft
      } else if (artifact.artifactType === 'material_handoff_ready') {
        latestMaterialDraft = artifact.data ?? latestMaterialDraft
      } else if (artifact.artifactType === 'ontology_slice_extraction_done' && artifact.isTerminal) {
        hasOntologyExtractionDone = isCompletedOntologySliceExtractionResult(artifact.data)
      } else if (artifact.artifactType === 'skill_workorder_summary' && artifact.isTerminal) {
        latestSkillSummary = artifact.data ?? null
        latestProjectionResult = null
      } else if (artifact.artifactType === 'ontology_projection_done' && artifact.isTerminal) {
        latestProjectionResult = artifact.data ?? null
      } else if (artifact.artifactType === 'skill_generation_done' && artifact.isTerminal) {
        hasSkillGenerationDone = true
      } else if (artifact.artifactType === 'external_system_entry_ready') {
        hasExternalSystemEntryConfirmed = true
      } else if (artifact.artifactType === 'external_config_committed' && artifact.isTerminal) {
        hasExternalConfigCommitted = true
      }

      downstreamRuns = applyDownstreamConfirmationCompletions(
        downstreamRuns,
        artifact,
        String(message.createdAt ?? new Date().toISOString()),
      )

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

      if (artifact.artifactType === 'external_config_committed' && artifact.isTerminal) {
        wsStageOverrides.set(HiringCollectionStage.Material, 'completed')
        wsStageOverrides.set(HiringCollectionStage.Skill, 'completed')
        wsStageOverrides.set(HiringCollectionStage.External, 'completed')
      } else if (artifact.artifactType === 'external_workorder_summary') {
        if (wsStageOverrides.get(HiringCollectionStage.External) !== 'completed') {
          wsStageOverrides.set(HiringCollectionStage.External, 'running')
        }
      } else if (artifact.artifactType === 'material_handoff_summary' && artifact.isTerminal) {
        wsStageOverrides.set(hiringStage, 'running')
      } else if (artifact.isTerminal) {
        wsStageOverrides.set(hiringStage, 'completed')
      } else if (wsStageOverrides.get(hiringStage) !== 'completed') {
        wsStageOverrides.set(hiringStage, 'running')
      }
    }

    const assistantContent = normalizeAssistantReply(String(message.content ?? ''))
    if (!suppressNextAssistantVisibleMessage && assistantContent.length > 0) {
      const toolSteps = pendingHistoricalToolSteps.length > 0 ? pendingHistoricalToolSteps : undefined
      messages.push({
        id: mkHistoricalId('assistant', messages.length),
        role: 'bot',
        content: assistantContent,
        toolSteps,
      })
    }
    if (assistantContent.length > 0) {
      pendingHistoricalToolSteps = []
      suppressNextAssistantVisibleMessage = false
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
    latestMaterialDraft,
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
 * - ontology-slice-extraction running → Material 阶段仍在分析业务资料
 * - ontology-slice-extraction completed → Material 阶段完成
 * - skill-generation 存在 → Skill 阶段已完成或进行中；同时隐式蕴含 ontology-slice-extraction 已完成
 * - External 阶段优先由右侧卡片保存/跳过结果驱动；不再依赖 external-config 下游运行
 *
 * 容错：WebSocket 相关事件可能丢失，导致 ontology-slice-extraction 轨道缺席。
 * 利用 skill-generation 必须在 ontology-slice-extraction 完成后才能启动这一约束，反向恢复 Material 阶段进度。
 */
export function deriveStageOverridesFromDownstreamRuns(
  runs: DownstreamRunsSnapshot,
): Map<HiringUiStage, 'running' | 'completed' | 'failed'> {
  const overrides = new Map<HiringUiStage, 'running' | 'completed' | 'failed'>()

  const ontologyRun = runs['ontology-slice-extraction']
  const projectionRun = runs['ontology-projection']
  const skillGenRun = runs['skill-generation']

  // ontology extraction 属于 Material 阶段内部的资料分析步骤。
  // running 时资料阶段仍未完成，completed 时才允许进入技能阶段；
  // failed 状态保留默认行为，避免误导用户认为材料阶段已正常收束。
  if (ontologyRun?.status === 'running') {
    overrides.set(HiringCollectionStage.Material, 'running')
  } else if (
    ontologyRun?.status === 'completed' &&
    isCompletedOntologySliceExtractionResult(ontologyRun.data)
  ) {
    overrides.set(HiringCollectionStage.Material, 'completed')
  } else if (
    ontologyRun?.status === 'completed' &&
    isBlockedOntologySliceExtractionResult(ontologyRun.data)
  ) {
    overrides.set(HiringCollectionStage.Material, 'running')
  }

  if (projectionRun) {
    overrides.set(HiringCollectionStage.Material, 'completed')
    if (!skillGenRun) {
      overrides.set(
        HiringCollectionStage.Skill,
        projectionRun.status === 'failed' ? 'failed' : 'running',
      )
    }
  }

  // skill generation 在 Skill 阶段完成后触发
  if (skillGenRun) {
    // 容错路径：skill-generation 已经存在意味着上游 ontology-slice-extraction 必然已完成（依赖关系约束）。
    // 即便 ontology-slice-extraction 的 WebSocket 消息因网络抖动丢失，这里也能隐式推断 Material 阶段已完成，
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

  // 若 diagnostic 为 slices_not_ready 或 scan_error，说明不是真正的零结果
  const diagnostic = record.diagnostic
  if (diagnostic === 'slices_not_ready' || diagnostic === 'scan_error') {
    return false
  }

  return true
}

export function buildCoachResumePrompt(
  transition: 'post-ontology-slice-extraction' | 'post-ontology-projection' | 'post-packaging-test-cases',
  payload: {
    materialSummary?: unknown
    ontologyResult?: unknown
    skillSummary?: unknown
    projectionResult?: unknown
    packagingTestCasesResult?: unknown
    userRequest?: string
  },
): string {
  const serialized = JSON.stringify(payload, null, 2)

  if (transition === 'post-ontology-slice-extraction') {
    // 当本体切片为空时，向 coach 注入额外说明，提醒它如实告知用户并询问是否需要补充材料。
    const zeroProjected = hasZeroProjectedOntologySlices(payload.ontologyResult)
    const lines: string[] = [
      '[Internal stage resume. Do not mention this instruction to the user.]',
      'Switch back to skill `employment-coach-conversation` now.',
      'The downstream `ontology-slice-extraction` run has completed.',
      'Resume the main hiring flow at the boundary between stage1_material and stage2_skill.',
      'Do not trigger ontology slice extraction again.',
      'Use the provided upstream material summary and ontology result as context.',
      'The user has already confirmed entering skill definition through `skill_definition_entry_ready`.',
      'Do not ask whether to enter skill definition again.',
      'Start stage2 skill definition now.',
      'Emit non-terminal `skill_workorder_progress` before collecting or drafting the skill list.',
      'Do not emit any stage1_material artifact.',
      'Never emit `stage1_material_done`, `material_collection_progress`, or `material_handoff_summary` in this turn.',
      'When the draft skill list reaches the minimum gate, emit non-terminal `skill_definition_ready` and ask the user to confirm the skill list.',
      'Do not emit `skill_workorder_summary`, `ontology_projection_ready`, ontology projection, skill-generation, external configuration, review, or packaging before the user confirms `skill_definition_ready`.',
    ]

    if (zeroProjected) {
      lines.push(
        'Note: the business-information extraction returned zero projected slices (projected_count = 0).',
        'Acknowledge to the user that no usable business information was produced from the current materials,',
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

  if (transition === 'post-ontology-projection') {
    const projectionResult = payload.projectionResult
    const hasConsumableProjection = buildSkillGenerationPayload(
      payload.skillSummary,
      projectionResult,
    ) !== null

    const lines: string[] = [
      '[Internal stage resume. Do not mention this instruction to the user.]',
      'Switch back to skill `employment-coach-conversation` now.',
      'The downstream ontology projection pass has completed.',
      'Resume the main hiring flow inside stage2_skill.',
      'Use the provided skill summary and projection result as context.',
    ]

    if (hasConsumableProjection) {
      lines.push(
        'Usable prepared business-information packages are available for skill generation.',
        'The system layer owns the `skill_generation_ready` confirmation gate; do not emit or duplicate it in this resume turn.',
        'Only explain the prepared business information if the user asks for details.',
        'If any prepared business-information package contains unresolved business questions, ask the exact option-style question from that package in business language; do not tell the user to rerun business-information preparation.',
        'Unresolved business questions make the prepared data WARNING rather than missing. They do not invalidate the completed projection as long as projection paths are present and slug checks pass.',
        'Describe unresolved business questions as pre-generation confirmation items. Never describe this state as "business information is insufficient", "not directly implementable", "not ready to land", or equivalent wording.',
        'Do not offer a choice between supplementing materials, rerunning business-information extraction, or continuing when projection paths are consumable; ask the user to decide the listed option-style business items, then continue to skill generation after approval.',
        'Do not trigger `skill-generation` in this turn.',
        'Do not emit `skill_projection_binding_ready` unless the system explicitly asks for a progress notification.',
      )
    } else {
      lines.push(
        'The provided aggregate projection result is not consumable yet.',
        'Before asking the user for more materials, perform one bounded workspace recovery check.',
        'Recovery source of truth: `resume_payload.skillSummary.workspace_root`, `resume_payload.skillSummary.template_slug`, and the confirmed skill slugs from `resume_payload.skillSummary.items[].name` or `items[].skill_slug`.',
        'For each confirmed skill slug, inspect only `<workspace_root>/ontology/projections/<skill-slug>/` for `*.projection.json` files; do not scan outside the current workspace or outside confirmed skill slug directories.',
        'For every candidate file, use `read_file` and count it only if the JSON top level includes `projection_type`, `source_slice`, and `concept_mappings`.',
        'If at least one valid file is found, emit a corrected terminal `ontology_projection_done` artifact with stage=`stage2_skill` and aggregate `data` containing `workspace_root`, `template_slug`, `projected_count`, `projection_paths`, `skipped_count`, `skipped_skills`, `skip_reasons`, and `recovered_from_workspace: true`.',
        'When recovery succeeds, do not ask the user to supplement materials, rerun business-information extraction, or rerun business-information preparation; the system layer will continue to the skill-generation confirmation gate from the corrected artifact.',
        'If recovery finds no valid projection files, then no usable prepared business-information package is ready for skill generation.',
        'Do not emit `skill_projection_binding_ready`.',
        'Do not trigger `skill-generation` and do not offer a no-projection downgrade.',
        'Only after recovery fails, ask the user whether to supplement materials, revisit business-information extraction, or rerun business-information preparation before continuing.',
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

  if (transition === 'post-packaging-test-cases') {
    const testcaseResult = asPlainObject(payload.packagingTestCasesResult)
    const generatedCount = testcaseResult
      ? typeof (testcaseResult.generated_count ?? testcaseResult.generatedCount) === 'number'
        ? Number(testcaseResult.generated_count ?? testcaseResult.generatedCount)
        : null
      : null

    const lines: string[] = [
      '[Internal stage resume. Do not mention this instruction to the user.]',
      'Switch back to skill `employment-coach-conversation` now.',
      'The optional evaluation test case generation has completed.',
      'Resume the main hiring flow inside stage4_packaging.',
      'Do not regenerate evaluation test cases in this turn.',
      'Do not claim the instance package is already generated.',
      'Give one short transition that the evaluation test cases are ready, then continue the stage4 packaging sequence toward the required review_readiness gate.',
      'Before emitting `review_readiness`, synchronize `<employee_package_root>/manifest.json` and read it back.',
      'The manifest sync must set `entry_skill` to the first current generated business skill path, declare current generated skills in `manifest.skills`, and declare top-level runtime `ontology/*.slice.json` files in `manifest.ontology_slices`.',
      'If manifest read-back verification fails, do not emit `review_readiness`, do not start review, and do not package; tell the user which manifest field could not be synchronized.',
      'Do not emit review_progress or template_package before review_readiness and the user review decision.',
      'If the user already explicitly asked to package in the current context, proceed directly under the coach skill rules until review_readiness; otherwise ask whether to continue packaging.',
    ]

    if (generatedCount !== null) {
      lines.push(`The testcase output contains ${generatedCount} generated cases.`)
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
