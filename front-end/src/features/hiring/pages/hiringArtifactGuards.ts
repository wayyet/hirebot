export interface IncomingArtifactGateState {
  hasMaterialSummary: boolean
  hasSkillSummary: boolean
  hasProjectionResult: boolean
  canUseProjectionForSkillGeneration?: boolean
  hasExternalConfigCommitted: boolean
}

export const KNOWN_HIRING_ARTIFACT_TYPES = [
  'material_collection_progress',
  'material_handoff_summary',
  'skill_workorder_progress',
  'skill_definition_ready',
  'skill_workorder_summary',
  'ontology_projection_ready',
  'skill_generation_ready',
  'skill_projection_binding_ready',
  'external_workorder_progress',
  'external_workorder_summary',
  'external_config_committed',
  'ontology_slice_extraction_progress',
  'ontology_slice_extraction_done',
  'ontology_projection_progress',
  'ontology_projection_done',
  'skill_generation_progress',
  'skill_generation_done',
  'packaging_testcases_ready',
  'packaging_testcases_progress',
  'packaging_testcases_done',
  'packaging_progress',
  'review_readiness',
  'review_progress',
  'review_report',
  'template_package',
] as const

const KNOWN_ARTIFACT_TYPES = new Set<string>(KNOWN_HIRING_ARTIFACT_TYPES)

const NON_TERMINAL_ARTIFACT_TYPES = new Set([
  'material_collection_progress',
  'skill_workorder_progress',
  'skill_definition_ready',
  'ontology_projection_ready',
  'skill_generation_ready',
  'skill_projection_binding_ready',
  'external_workorder_progress',
  'ontology_slice_extraction_progress',
  'ontology_projection_progress',
  'skill_generation_progress',
  'packaging_testcases_ready',
  'packaging_testcases_progress',
  'packaging_progress',
  'review_readiness',
  'review_progress',
])

const TERMINAL_ARTIFACT_TYPES = new Set([
  'material_handoff_summary',
  'skill_workorder_summary',
  'external_workorder_summary',
  'external_config_committed',
  'ontology_slice_extraction_done',
  'ontology_projection_done',
  'skill_generation_done',
  'packaging_testcases_done',
  'review_report',
  'template_package',
])

const ONTOLOGY_SLICE_EXTRACTION_ARTIFACTS = new Set([
  'ontology_slice_extraction_progress',
  'ontology_slice_extraction_done',
])

const ONTOLOGY_PROJECTION_ARTIFACTS = new Set([
  'ontology_projection_progress',
  'ontology_projection_done',
])

const ONTOLOGY_PROJECTION_CONFIRMATION_ARTIFACTS = new Set([
  'ontology_projection_ready',
])

const SKILL_GENERATION_ARTIFACTS = new Set([
  'skill_generation_progress',
  'skill_generation_done',
])

const PACKAGING_TESTCASE_ARTIFACTS = new Set([
  'packaging_testcases_ready',
  'packaging_testcases_progress',
  'packaging_testcases_done',
])

const PACKAGING_PROGRESS_STATUSES = new Set([
  'waiting_downstream',
  'packing',
])

const REVIEW_REPORT_STATUSES = new Set([
  'PASS',
  'PASS_WITH_CONCERNS',
  'FAIL',
])

const DATA_STATUS_ALLOWED_ARTIFACT_TYPES = new Set([
  'packaging_progress',
  'packaging_testcases_progress',
  'packaging_testcases_done',
  'review_readiness',
  'review_progress',
  'review_report',
])

const WORKSPACE_ROOT_REQUIRED_ARTIFACT_TYPES = new Set([
  'material_handoff_summary',
  'skill_workorder_summary',
])

function asPlainObject(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function hasOwn(record: Record<string, unknown>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(record, key)
}

function isValidWorkspaceRoot(value: unknown): value is string {
  if (typeof value !== 'string') return false

  const normalized = value.trim().replace(/\/+$/g, '')
  if (!normalized.startsWith('/workspace/')) return false
  if (normalized.length <= '/workspace/'.length) return false
  if (normalized.includes('<') || normalized.includes('>')) return false
  if (normalized.includes('\\')) return false

  return true
}

function getWorkspaceRootBlockReason(artifactType: string, data: Record<string, unknown> | null): string | null {
  const requiresWorkspaceRoot = WORKSPACE_ROOT_REQUIRED_ARTIFACT_TYPES.has(artifactType)
  if (!data) {
    return requiresWorkspaceRoot ? 'workspace_root is required' : null
  }

  if (!hasOwn(data, 'workspace_root')) {
    return requiresWorkspaceRoot ? 'workspace_root is required' : null
  }

  return isValidWorkspaceRoot(data.workspace_root)
    ? null
    : 'workspace_root must be a session workspace path'
}

function getMaterialSourcePathBlockReason(data: Record<string, unknown> | null): string | null {
  if (!data) return 'material_handoff_summary.items[] is required'

  const items = data.items
  if (!Array.isArray(items)) return 'material_handoff_summary.items[] is required'

  for (const item of items) {
    const record = asPlainObject(item)
    if (!record) return 'material_handoff_summary.items[] must contain objects'
    if (!hasOwn(record, 'source_path')) return 'material_handoff_summary.items[].source_path is required'

    const sourcePath = record.source_path
    if (sourcePath !== null && typeof sourcePath !== 'string') {
      return 'material_handoff_summary.items[].source_path must be string or null'
    }
    if (typeof sourcePath === 'string') {
      const trimmed = sourcePath.trim()
      if (!trimmed) return 'material_handoff_summary.items[].source_path must be non-empty or null'
      if (trimmed.startsWith('[FILE_URL:') || trimmed.startsWith('/media/')) {
        return 'material_handoff_summary.items[].source_path must be a workspace-readable path'
      }
    }
  }

  return null
}

export function normalizeIncomingArtifactTerminal(artifactType: string, isTerminal: boolean): boolean {
  // 进度/确认门 artifact 如果被模型误标成终态，按协议降级为非终态，
  // 避免右侧阶段被错误置为 completed。
  if (NON_TERMINAL_ARTIFACT_TYPES.has(artifactType)) {
    return false
  }

  return isTerminal
}

export function shouldDisplayArtifactInConversation(artifactType: string, isTerminal?: boolean): boolean {
  if (artifactType === 'packaging_progress' && isTerminal !== true) {
    return false
  }

  return true
}

export function getBlockedIncomingArtifactReason(
  artifactType: string,
  state: IncomingArtifactGateState,
  options: { isTerminal?: boolean; kind?: 'file' | 'data'; data?: unknown } = {},
): string | null {
  if (!KNOWN_ARTIFACT_TYPES.has(artifactType)) {
    return 'unknown hiring artifact type'
  }

  if (TERMINAL_ARTIFACT_TYPES.has(artifactType) && options.isTerminal === false) {
    return `${artifactType} must be terminal`
  }

  if (artifactType === 'template_package' && options.kind && options.kind !== 'file') {
    return 'template_package must be file artifact'
  }

  if (artifactType !== 'template_package' && options.kind === 'file') {
    return 'file artifacts are only allowed for template_package'
  }

  const data = asPlainObject(options.data)
  if (data && hasOwn(data, 'status') && !DATA_STATUS_ALLOWED_ARTIFACT_TYPES.has(artifactType)) {
    return 'data.status is only allowed for packaging and review artifacts'
  }

  const workspaceRootBlockReason = getWorkspaceRootBlockReason(artifactType, data)
  if (workspaceRootBlockReason) {
    return workspaceRootBlockReason
  }

  if (artifactType === 'material_handoff_summary') {
    const sourcePathBlockReason = getMaterialSourcePathBlockReason(data)
    if (sourcePathBlockReason) return sourcePathBlockReason
  }

  if (artifactType === 'packaging_progress') {
    const status = typeof data?.status === 'string' ? data.status : ''
    if (!PACKAGING_PROGRESS_STATUSES.has(status)) {
      return 'packaging_progress.status must be waiting_downstream or packing'
    }
  }

  if (artifactType === 'review_report') {
    const status = typeof data?.status === 'string' ? data.status : ''
    if (!REVIEW_REPORT_STATUSES.has(status)) {
      return 'review_report.status must be PASS, PASS_WITH_CONCERNS, or FAIL'
    }
    if (typeof data?.release_readiness !== 'string' || !data.release_readiness.trim()) {
      return 'review_report.release_readiness must be a non-empty string'
    }
    if (typeof data?.summary !== 'string' || !data.summary.trim()) {
      return 'review_report.summary must be a non-empty string'
    }
    if (typeof data?.score_average !== 'number' || !Number.isFinite(data.score_average)) {
      return 'review_report.score_average must be a finite number'
    }
    if (!Array.isArray(data?.p0_blockers)) {
      return 'review_report.p0_blockers must be an array'
    }
    if (!Array.isArray(data?.p1_warnings)) {
      return 'review_report.p1_warnings must be an array'
    }
  }

  if (ONTOLOGY_SLICE_EXTRACTION_ARTIFACTS.has(artifactType) && !state.hasMaterialSummary) {
    return 'ontology slice extraction requires material_handoff_summary'
  }

  if ((ONTOLOGY_PROJECTION_ARTIFACTS.has(artifactType) || ONTOLOGY_PROJECTION_CONFIRMATION_ARTIFACTS.has(artifactType)) && !state.hasSkillSummary) {
    return 'ontology projection requires skill_workorder_summary'
  }

  if (artifactType === 'skill_definition_ready' && !state.hasMaterialSummary) {
    return 'skill definition confirmation requires material_handoff_summary'
  }

  if (artifactType === 'skill_generation_ready') {
    if (!state.hasSkillSummary) {
      return 'skill generation confirmation requires skill_workorder_summary'
    }
    if (!state.hasProjectionResult) {
      return 'skill generation confirmation requires ontology_projection_done'
    }
    if (state.canUseProjectionForSkillGeneration !== true) {
      return 'skill generation confirmation requires consumable ontology projection'
    }
  }

  if (artifactType === 'skill_projection_binding_ready') {
    if (!state.hasSkillSummary) {
      return 'projection binding progress requires skill_workorder_summary'
    }
    if (!state.hasProjectionResult) {
      return 'projection binding progress requires ontology_projection_done'
    }
    if (state.canUseProjectionForSkillGeneration !== true) {
      return 'projection binding progress requires consumable ontology projection'
    }
  }

  if (SKILL_GENERATION_ARTIFACTS.has(artifactType)) {
    if (!state.hasSkillSummary) {
      return 'skill generation requires skill_workorder_summary'
    }
    if (!state.hasProjectionResult) {
      return 'skill generation requires ontology_projection_done'
    }
    if (state.canUseProjectionForSkillGeneration !== true) {
      return 'skill generation requires consumable ontology projection'
    }
  }

  if (PACKAGING_TESTCASE_ARTIFACTS.has(artifactType) && !state.hasExternalConfigCommitted) {
    return 'packaging testcases require external_config_committed'
  }

  return null
}
