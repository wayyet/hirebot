import { useMemo } from 'react'
import {
  HiringCollectionPhase,
  HiringCollectionStage,
  HiringStageReadinessStatus,
  type HiringWorkflowState,
  type StageReadiness,
} from '@/infra/api'
import type {
  ChatFile,
  ChatMessage,
  DefinedSkillItem,
  DownstreamRunsSnapshot,
} from '../hiringPageTypes'
import {
  buildUiStageOverrides,
  shouldHoldExternalStageUntilSkillImplementation,
} from '../hiringArtifactState'
import { extractConversationMaterialFiles } from '../materialUploadMatching'
import { buildHiringWorkflowViewModel, type HiringUiStage } from '../hiringWorkflowViewModel'

type UiStageRuntimeStatus = 'running' | 'completed' | 'failed'

const CORE_STAGE_ORDER: HiringUiStage[] = [
  HiringCollectionStage.Material,
  HiringCollectionStage.Skill,
  HiringCollectionStage.External,
]

function buildDerivedStageReadiness(
  stage: HiringUiStage,
  currentStage: HiringUiStage,
  uiStageOverrides: Map<HiringUiStage, UiStageRuntimeStatus>,
  finalized: boolean,
): StageReadiness {
  const runtimeStatus = uiStageOverrides.get(stage)
  const currentStageIndex = [
    ...CORE_STAGE_ORDER,
    HiringCollectionStage.ReadyForPackaging,
  ].indexOf(currentStage)
  const stageIndex = [
    ...CORE_STAGE_ORDER,
    HiringCollectionStage.ReadyForPackaging,
  ].indexOf(stage)

  if (stage === HiringCollectionStage.ReadyForPackaging) {
    if (finalized) {
      return {
        stage,
        status: HiringStageReadinessStatus.Complete,
        reason: '打包已完成，可进入后续训练或评估。',
        blockingTodoIds: [],
      }
    }

    if (currentStage === HiringCollectionStage.ReadyForPackaging) {
      return {
        stage,
        status: HiringStageReadinessStatus.Partial,
        reason: '资料、技能和外部连接已就绪，可以发起打包。',
        blockingTodoIds: [],
      }
    }

    return {
      stage,
      status: HiringStageReadinessStatus.Missing,
      reason: '等待资料、技能和外部连接全部完成后解锁打包。',
      blockingTodoIds: [],
    }
  }

  if (runtimeStatus === 'completed' || stageIndex < currentStageIndex) {
    return {
      stage,
      status: HiringStageReadinessStatus.Complete,
      reason: '当前阶段已产出。',
      blockingTodoIds: [],
    }
  }

  if (stage === currentStage) {
    const reason = runtimeStatus === 'failed'
      ? '当前阶段产出失败，请检查本阶段结果后重试。'
      : runtimeStatus === 'running'
        ? '当前阶段正在产出，请等待结果更新。'
        : '已满足进入当前阶段的条件，可以继续补齐当前内容。'

    return {
      stage,
      status: HiringStageReadinessStatus.Partial,
      reason,
      blockingTodoIds: [],
    }
  }

  return {
    stage,
    status: HiringStageReadinessStatus.Missing,
    reason: '等待前序阶段完成后解锁。',
    blockingTodoIds: [],
  }
}

export function deriveCurrentStageFromOverrides(
  uiStageOverrides: Map<HiringUiStage, UiStageRuntimeStatus>,
  finalized = false,
): HiringUiStage {
  if (finalized) {
    return HiringCollectionStage.ReadyForPackaging
  }

  const runningStage = CORE_STAGE_ORDER.find((stage) => {
    const status = uiStageOverrides.get(stage)
    return status === 'running' || status === 'failed'
  })
  if (runningStage) {
    return runningStage
  }

  const nextIncompleteStage = CORE_STAGE_ORDER.find(stage => uiStageOverrides.get(stage) !== 'completed')
  return nextIncompleteStage ?? HiringCollectionStage.ReadyForPackaging
}

export function buildDerivedWorkflowStateFromStageOverrides(
  uiStageOverrides: Map<HiringUiStage, UiStageRuntimeStatus>,
  finalized = false,
): HiringWorkflowState {
  const currentStage = deriveCurrentStageFromOverrides(uiStageOverrides, finalized)
  const coreStagesCompleted = CORE_STAGE_ORDER.every(stage => uiStageOverrides.get(stage) === 'completed')
  const collectionPhase = finalized
    ? HiringCollectionPhase.Finalized
    : coreStagesCompleted
      ? HiringCollectionPhase.ReadyForFinalize
      : uiStageOverrides.size > 0
        ? HiringCollectionPhase.InProgress
        : HiringCollectionPhase.NotStarted

  const stageReadiness = [
    HiringCollectionStage.Material,
    HiringCollectionStage.Skill,
    HiringCollectionStage.External,
    HiringCollectionStage.ReadyForPackaging,
  ].map(stage => buildDerivedStageReadiness(stage, currentStage, uiStageOverrides, finalized))

  return {
    hireId: '',
    sessionId: '',
    currentStage,
    requiresAudit: false,
    collectionPhase,
    stageSkills: [],
    auditLogs: [],
    stageCompletion: [],
    workflowTodos: [],
    handoffItems: [],
    latestDispatches: [],
    latestDiagnosticReport: coreStagesCompleted || finalized
      ? {
        status: 'pass',
        confidence: 'medium',
        currentStage,
        readyForPackaging: true,
        stageReadiness,
        diagnosticTodos: [],
        todoCorrelation: [],
        openQuestions: [],
        userSummary: finalized
          ? '打包已完成。'
          : '资料、技能与外部连接已就绪，可以继续打包。',
        generatedAtUtc: new Date(0).toISOString(),
      }
      : null,
    configGovernance: null,
    stageReadiness,
    runtimeFacts: null,
    isConversationPaused: false,
    isConversationResponding: false,
  }
}

/**
 * 从消息列表提取已定义的技能列表
 */
function extractLatestDefinedSkills(messages: ChatMessage[]): DefinedSkillItem[] {
  function asPlainObject(value: unknown): Record<string, unknown> | null {
    return value && typeof value === 'object' && !Array.isArray(value)
      ? value as Record<string, unknown>
      : null
  }

  function asStringArray(value: unknown): string[] {
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

  for (let i = messages.length - 1; i >= 0; i -= 1) {
    const artifact = messages[i].artifact
    if (!artifact) continue
    if (artifact.artifactType !== 'skill_workorder_summary' && artifact.artifactType !== 'skill_workorder_progress' && artifact.artifactType !== 'skill_definition_ready') {
      continue
    }
    const payload = asPlainObject(artifact.data)
    const rawSkills = Array.isArray(payload?.skills)
      ? payload.skills
      : Array.isArray(payload?.items)
        ? payload.items
        : null
    if (!rawSkills) return []

    return rawSkills
      .map(item => {
        const record = asPlainObject(item)
        if (!record) return null

        const skillName = typeof record.skill_name === 'string'
          ? record.skill_name.trim()
          : typeof record.skillName === 'string'
            ? record.skillName.trim()
            : typeof record.display_name === 'string'
              ? record.display_name.trim()
              : typeof record.displayName === 'string'
                ? record.displayName.trim()
                : typeof record.title === 'string'
                  ? record.title.trim()
                  : typeof record.skill_id === 'string'
                    ? record.skill_id.trim()
                    : typeof record.name === 'string'
                      ? record.name.trim()
                      : ''
        if (!skillName) return null

        const capabilities = asStringArray(record.capabilities)
        const capabilityText = typeof record.capability === 'string' && record.capability.trim().length > 0
          ? record.capability.trim()
          : ''
        const description = typeof record.description === 'string' && record.description.trim().length > 0
          ? record.description.trim()
          : typeof record.purpose === 'string' && record.purpose.trim().length > 0
            ? record.purpose.trim()
            : typeof record.capability_description === 'string' && record.capability_description.trim().length > 0
              ? record.capability_description.trim()
              : (capabilityText || capabilities[0] || '')

        const skill: DefinedSkillItem = {
          skillName,
          generationAction: typeof record.generation_action === 'string'
            ? record.generation_action
            : typeof record.generationAction === 'string'
              ? record.generationAction
              : undefined,
          description: description || undefined,
          expectedOutput: typeof record.expected_output === 'string'
            ? record.expected_output
            : typeof record.expectedOutput === 'string'
              ? record.expectedOutput
              : typeof record.outputs === 'string'
                ? record.outputs
                : typeof record.output === 'string'
                  ? record.output
                  : undefined,
          triggers: asStringArray(record.trigger ?? record.triggers),
          capabilities: capabilities.length > 0
            ? capabilities
            : capabilityText
              ? [capabilityText]
              : [],
        }

        return skill
      })
      .filter((item): item is NonNullable<typeof item> => item !== null)
  }

  return []
}

function hasTerminalArtifact(messages: ChatMessage[], artifactType: string): boolean {
  return messages.some(message =>
    message.artifact?.artifactType === artifactType &&
    message.artifact.isTerminal === true,
  )
}

/**
 * 雇佣页计算属性 hook
 */
export interface HiringComputedProps {
  messages: ChatMessage[]
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>
  downstreamRuns: DownstreamRunsSnapshot
  latestSkillSummary: unknown
  focusedStage: HiringUiStage | null
  t: (key: string) => string
  templateName?: string | null
  workflowHireId: string
  instanceCreated: boolean
  typing: boolean
  workflowBooting: boolean
  submittingMessage: boolean
  resetting: boolean
  allFiles: unknown[]
  pendingPackageArtifact: unknown | null
}

export interface HiringComputedValues {
  uiStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>
  definedSkills: DefinedSkillItem[]
  finalPackageFileName: string
  hasTemplatePackageArtifact: boolean
  uploadedConversationFiles: ChatFile[]
  uploadedFileCount: number
  isInteractionLocked: boolean
  wsStagesAllCompleted: boolean
  wsCanFinalize: boolean
  canCreate: boolean
  canDownloadFinalPackage: boolean
  viewModel: ReturnType<typeof buildHiringWorkflowViewModel>
  mergedStepPills: ReturnType<typeof buildHiringWorkflowViewModel>['stepPills']
  mergedActionState: ReturnType<typeof buildHiringWorkflowViewModel>['actionState']
}

export function useHiringComputed(props: HiringComputedProps): HiringComputedValues {
  const {
    messages,
    wsStageOverrides,
    downstreamRuns,
    latestSkillSummary,
    focusedStage,
    t,
    templateName,
    workflowHireId,
    instanceCreated,
    typing,
    workflowBooting,
    submittingMessage,
    resetting,
    allFiles,
  } = props

  const skillGenerationState = downstreamRuns['skill-generation'] ?? null
  const ontologyExtractionState = downstreamRuns['ontology-slice-extraction'] ?? null
  const holdExternalStage = shouldHoldExternalStageUntilSkillImplementation(
    latestSkillSummary,
    skillGenerationState,
  )
  const externalConfigCommitted = useMemo(
    () => hasTerminalArtifact(messages, 'external_config_committed'),
    [messages],
  )

  const uiStageOverrides = useMemo(
    () => buildUiStageOverrides(
      wsStageOverrides,
      ontologyExtractionState,
      skillGenerationState,
      holdExternalStage,
      externalConfigCommitted,
    ),
    [wsStageOverrides, ontologyExtractionState, skillGenerationState, holdExternalStage, externalConfigCommitted],
  )

  const derivedWorkflowState = useMemo(
    () => buildDerivedWorkflowStateFromStageOverrides(uiStageOverrides, instanceCreated),
    [instanceCreated, uiStageOverrides],
  )

  const definedSkills = useMemo(
    () => extractLatestDefinedSkills(messages),
    [messages],
  )

  const viewModel = buildHiringWorkflowViewModel(derivedWorkflowState, focusedStage, t)
  
  const mergedStepPills = viewModel.stepPills.map(pill => {
    const wsStatus = uiStageOverrides.get(pill.stage)
    if (!wsStatus) return pill
    return { ...pill, dispatchStatus: wsStatus }
  })

  const wsStagesAllCompleted = (
    uiStageOverrides.get(HiringCollectionStage.Material) === 'completed' &&
    uiStageOverrides.get(HiringCollectionStage.Skill) === 'completed' &&
    uiStageOverrides.get(HiringCollectionStage.External) === 'completed'
  )
  const wsCanFinalize = wsStagesAllCompleted
  const mergedActionState = wsCanFinalize
    ? { ...viewModel.actionState, canFinalize: true }
    : viewModel.actionState

  const canCreate = Boolean(workflowHireId) && !instanceCreated
  const canDownloadFinalPackage = instanceCreated && Boolean(workflowHireId)

  const finalPackageFileName = useMemo(
    () => buildFinalPackageDisplayFileName(templateName),
    [templateName],
  )

  const hasTemplatePackageArtifact = useMemo(
    () => messages.some(message => message.artifact?.artifactType === 'template_package'),
    [messages],
  )

  const isInteractionLocked = typing || workflowBooting || submittingMessage || resetting

  const uploadedConversationFiles = useMemo(
    () => extractConversationMaterialFiles(messages),
    [messages],
  )

  const uploadedFileCount = Math.max(allFiles.length, uploadedConversationFiles.length)

  return {
    uiStageOverrides,
    definedSkills,
    finalPackageFileName,
    hasTemplatePackageArtifact,
    uploadedConversationFiles,
    uploadedFileCount,
    isInteractionLocked,
    wsStagesAllCompleted,
    wsCanFinalize,
    canCreate,
    canDownloadFinalPackage,
    viewModel,
    mergedStepPills,
    mergedActionState,
  }
}

function buildFinalPackageDisplayFileName(templateName?: string | null): string {
  const suffix = '数字员工'
  const fallback = `${suffix}.zip`
  if (!templateName?.trim()) {
    return fallback
  }

  let normalized = ''
  let previousWasSeparator = false
  for (const character of templateName.trim()) {
    if (/[\x00-\x1F<>:"/\\|?*]/.test(character)) {
      continue
    }

    if (/\s/.test(character) || character === '_' || character === '-') {
      if (normalized && !previousWasSeparator) {
        normalized += '-'
        previousWasSeparator = true
      }
      continue
    }

    if (/[\p{L}\p{N}]/u.test(character)) {
      normalized += character
      previousWasSeparator = false
    }
  }

  normalized = normalized.replace(/^[\s.-]+|[\s.-]+$/g, '')
  if (!normalized) {
    return fallback
  }

  const baseName = normalized.endsWith(suffix) ? normalized : `${normalized}-${suffix}`
  return `${baseName}.zip`
}