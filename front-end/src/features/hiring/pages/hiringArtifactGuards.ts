export interface IncomingArtifactGateState {
  hasMaterialSummary: boolean
  hasSkillSummary: boolean
  hasProjectionResult: boolean
  hasExternalConfigCommitted: boolean
}

const KNOWN_ARTIFACT_TYPES = new Set([
  'material_collection_progress',
  'material_handoff_summary',
  'skill_workorder_progress',
  'skill_workorder_summary',
  'skill_generation_ready',
  'skill_projection_binding_ready',
  'external_workorder_progress',
  'external_workorder_summary',
  'external_config_committed',
  'ontology_extraction_progress',
  'ontology_extraction_done',
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
])

const NON_TERMINAL_ARTIFACT_TYPES = new Set([
  'material_collection_progress',
  'skill_workorder_progress',
  'skill_generation_ready',
  'skill_projection_binding_ready',
  'external_workorder_progress',
  'ontology_extraction_progress',
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
  'ontology_extraction_done',
  'ontology_projection_done',
  'skill_generation_done',
  'packaging_testcases_done',
  'review_report',
  'template_package',
])

const ONTOLOGY_EXTRACTION_ARTIFACTS = new Set([
  'ontology_extraction_progress',
  'ontology_extraction_done',
])

const ONTOLOGY_PROJECTION_ARTIFACTS = new Set([
  'ontology_projection_progress',
  'ontology_projection_done',
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

export function normalizeIncomingArtifactTerminal(artifactType: string, isTerminal: boolean): boolean {
  // 进度/确认门 artifact 如果被模型误标成终态，按协议降级为非终态，
  // 避免右侧阶段被错误置为 completed。
  if (NON_TERMINAL_ARTIFACT_TYPES.has(artifactType)) {
    return false
  }

  return isTerminal
}

export function getBlockedIncomingArtifactReason(
  artifactType: string,
  state: IncomingArtifactGateState,
  options: { isTerminal?: boolean; kind?: 'file' | 'data' } = {},
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

  if (ONTOLOGY_EXTRACTION_ARTIFACTS.has(artifactType) && !state.hasMaterialSummary) {
    return 'ontology extraction requires material_handoff_summary'
  }

  if (ONTOLOGY_PROJECTION_ARTIFACTS.has(artifactType) && !state.hasSkillSummary) {
    return 'ontology projection requires skill_workorder_summary'
  }

  if (artifactType === 'skill_generation_ready' && !state.hasSkillSummary) {
    return 'skill generation confirmation requires skill_workorder_summary'
  }

  if (artifactType === 'skill_projection_binding_ready') {
    if (!state.hasSkillSummary) {
      return 'projection binding confirmation requires skill_workorder_summary'
    }
    if (!state.hasProjectionResult) {
      return 'projection binding confirmation requires ontology_projection_done'
    }
  }

  if (SKILL_GENERATION_ARTIFACTS.has(artifactType)) {
    if (!state.hasSkillSummary) {
      return 'skill generation requires skill_workorder_summary'
    }
    if (!state.hasProjectionResult) {
      return 'skill generation requires ontology_projection_done'
    }
  }

  if (PACKAGING_TESTCASE_ARTIFACTS.has(artifactType) && !state.hasExternalConfigCommitted) {
    return 'packaging testcases require external_config_committed'
  }

  return null
}
