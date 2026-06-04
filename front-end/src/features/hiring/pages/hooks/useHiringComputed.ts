import { useMemo } from 'react'
import { HiringCollectionStage, type HiringExternalSystemConfig } from '@/infra/api'
import type {
  ArtifactArchive,
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
    if (artifact.artifactType !== 'skill_workorder_summary' && artifact.artifactType !== 'skill_workorder_progress') {
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
  workflowHireId: string
  instanceCreated: boolean
  artifactArchive: ArtifactArchive | null
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
  uploadedConversationFiles: { name: string; size: number }[]
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
    workflowHireId,
    instanceCreated,
    artifactArchive,
    typing,
    workflowBooting,
    submittingMessage,
    resetting,
    allFiles,
    pendingPackageArtifact,
  } = props

  const skillGenerationState = downstreamRuns['skill-generation'] ?? null
  const holdExternalStage = shouldHoldExternalStageUntilSkillImplementation(
    latestSkillSummary,
    skillGenerationState,
  )

  const uiStageOverrides = useMemo(
    () => buildUiStageOverrides(wsStageOverrides, skillGenerationState, holdExternalStage),
    [wsStageOverrides, skillGenerationState, holdExternalStage],
  )

  const definedSkills = useMemo(
    () => extractLatestDefinedSkills(messages),
    [messages],
  )

  const viewModel = buildHiringWorkflowViewModel(null, focusedStage, t)
  
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

  const finalPackageFileName = useMemo(() => {
    if (artifactArchive?.fileName) {
      return artifactArchive.fileName
    }
    if (workflowHireId) {
      return `${workflowHireId}_final_package.zip`
    }
    return ''
  }, [artifactArchive?.fileName, workflowHireId])

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
