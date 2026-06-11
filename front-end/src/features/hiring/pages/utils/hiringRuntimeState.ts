import {
  HiringCollectionStage,
  type PersistedChatFile,
  type PersistedPackageStructure,
  type RuntimeStateSaveRequest,
  type RuntimeStateStage,
} from '@/infra/api'
import type { ChatFile, DownstreamRunKey, DownstreamRunsSnapshot } from '../hiringPageTypes'
import type { HiringUiStage } from '../hiringWorkflowViewModel'

export const RUNTIME_STATE_STAGE_SEQUENCE: readonly RuntimeStateStage[] = [
  HiringCollectionStage.Material,
  HiringCollectionStage.Skill,
  HiringCollectionStage.External,
  HiringCollectionStage.ReadyForPackaging,
]

const DOWNSTREAM_RUN_STAGE_KEYS: Record<RuntimeStateStage, readonly DownstreamRunKey[]> = {
  [HiringCollectionStage.Material]: ['ontology-slice-extraction', 'ontology-projection'],
  [HiringCollectionStage.Skill]: ['skill-generation'],
  [HiringCollectionStage.External]: [],
  [HiringCollectionStage.ReadyForPackaging]: ['packaging-test-cases'],
}

function buildPersistedFiles(allFiles: ChatFile[]): PersistedChatFile[] | undefined {
  if (allFiles.length === 0) {
    return undefined
  }

  return allFiles.map(file => ({
    id: file.id,
    name: file.name,
    size: file.size,
    status: file.status,
    type: file.type,
    mimeType: file.mimeType,
    metadata: file.metadata,
  }))
}

function pickStageOverrides(
  stage: RuntimeStateStage,
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
): Record<string, unknown> | undefined {
  const status = wsStageOverrides.get(stage)
  return status ? { [stage]: status } : undefined
}

function pickDownstreamRuns(
  stage: RuntimeStateStage,
  downstreamRuns: DownstreamRunsSnapshot,
): DownstreamRunsSnapshot | undefined {
  const keys = DOWNSTREAM_RUN_STAGE_KEYS[stage]
  if (keys.length === 0) {
    return undefined
  }

  const picked = keys.reduce<DownstreamRunsSnapshot>((acc, key) => {
    const value = downstreamRuns[key]
    if (value) {
      acc[key] = value
    }
    return acc
  }, {})

  return Object.keys(picked).length > 0 ? picked : undefined
}

export function buildRuntimeStatePayloadByStage(
  stage: RuntimeStateStage,
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
  downstreamRuns: DownstreamRunsSnapshot,
  allFiles: ChatFile[],
  packageStructure?: PersistedPackageStructure,
): RuntimeStateSaveRequest {
  return {
    stageOverrides: pickStageOverrides(stage, wsStageOverrides),
    downstreamRuns: pickDownstreamRuns(stage, downstreamRuns),
    uploadedFiles: stage === HiringCollectionStage.Material ? buildPersistedFiles(allFiles) : undefined,
    packageStructure: stage === HiringCollectionStage.ReadyForPackaging ? packageStructure : undefined,
  }
}

export function hasRuntimeStatePayloadContent(payload: RuntimeStateSaveRequest): boolean {
  return Boolean(
    (payload.stageOverrides && Object.keys(payload.stageOverrides).length > 0)
    || (payload.downstreamRuns && Object.keys(payload.downstreamRuns).length > 0)
    || (payload.uploadedFiles && payload.uploadedFiles.length > 0)
    || payload.packageStructure?.fileName,
  )
}
