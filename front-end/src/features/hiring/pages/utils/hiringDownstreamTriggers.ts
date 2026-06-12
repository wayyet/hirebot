import type { DownstreamRunState } from '../hiringPageTypes'

import { asPlainObject, asStringArray } from './hiringCacheNormalizers'

export type DownstreamTarget = 'ontology-slice-extraction' | 'ontology-projection' | 'skill-generation' | 'packaging-test-cases'

export type SkillStageApprovalRoute =
  | 'none'
  | 'confirm_skill_definition'
  | 'launch_projection_pass'
  | 'launch_skill_generation'

export type PackagingRequestRoute =
  | 'none'
  | 'import_existing_package'
  | 'wait_for_active_packaging'
  | 'launch_packaging_request'

export interface SkillStageApprovalRouteInput {
  text: string
  incomingFileCount: number
  skillGenerationState: DownstreamRunState | null
  ontologyProjectionState: DownstreamRunState | null
  hasSkillSummary: boolean
  hasProjectionResult: boolean
}

export interface PackagingRequestRouteInput {
  text: string
  incomingFileCount: number
  isBlockedByRequiredConfirmation: boolean
  isBlockedByPackagingTestCaseGeneration: boolean
  hasPendingPackageArtifact: boolean
  packagingInProgress: boolean
  hasReviewReport: boolean
  hasPackagingContext: boolean
  hasCompletedCoreSummaries: boolean
}

function isWaitingArtifact(
  run: DownstreamRunState | null,
  artifactType: string,
): boolean {
  return run?.status === 'waiting_confirm' && run.artifactType === artifactType
}

function extractSkillWorkorderItems(summary: unknown): Record<string, unknown>[] {
  const record = asPlainObject(summary)
  if (!record) return []

  const items = Array.isArray(record.items)
    ? record.items
    : Array.isArray(record.skills)
      ? record.skills
      : []

  return items
    .map(item => asPlainObject(item))
    .filter((item): item is Record<string, unknown> => item !== null)
}

function extractConfirmedSkillSlugs(summary: unknown): string[] {
  return extractSkillWorkorderItems(summary)
    .map((item) => {
      if (typeof item.name === 'string' && item.name.trim()) return item.name.trim()
      if (typeof item.skill_slug === 'string' && item.skill_slug.trim()) return item.skill_slug.trim()
      if (typeof item.skillName === 'string' && item.skillName.trim()) return item.skillName.trim()
      return ''
    })
    .filter(slug => slug.length > 0)
}

function extractProjectionSkillSlug(path: string): string {
  const parts = path.split(/[\\/]+/).filter(Boolean)
  const ontologyIndex = parts.findIndex(part => part === 'ontology')
  if (ontologyIndex < 0 || parts[ontologyIndex + 1] !== 'projections') {
    return ''
  }

  return parts[ontologyIndex + 2] ?? ''
}

function readProjectionPaths(projectionResult: unknown): string[] {
  const record = asPlainObject(projectionResult)
  const paths = Array.isArray(record?.projection_paths)
    ? record.projection_paths
    : Array.isArray(record?.projectionPaths)
      ? record.projectionPaths
      : []

  return paths.filter((path): path is string => typeof path === 'string' && path.trim().length > 0)
}

function extractProjectionSkillSlugs(projectionResult: unknown): string[] {
  const paths = readProjectionPaths(projectionResult)

  return Array.from(new Set(
    paths
      .map(path => extractProjectionSkillSlug(path))
      .filter(slug => slug.length > 0),
  ))
}

function projectionSlugsMatchConfirmedSkills(summary: unknown, projectionResult: unknown): boolean {
  const confirmedSlugs = new Set(extractConfirmedSkillSlugs(summary))
  if (confirmedSlugs.size === 0) {
    return false
  }

  const projectionSlugs = extractProjectionSkillSlugs(projectionResult)
  if (projectionSlugs.length === 0) {
    return false
  }

  return projectionSlugs.every(slug => confirmedSlugs.has(slug))
}

export function buildProjectionPassPayload(summary: unknown): Record<string, unknown> | null {
  const record = asPlainObject(summary)
  if (!record) return null

  const workspaceRoot = typeof record.workspace_root === 'string' ? record.workspace_root.trim() : ''
  if (!workspaceRoot) return null

  const skills = extractSkillWorkorderItems(summary)
    .map((item) => {
      const skillSlug = typeof item.name === 'string'
        ? item.name.trim()
        : typeof item.skill_slug === 'string'
          ? item.skill_slug.trim()
          : typeof item.skillName === 'string'
            ? item.skillName.trim()
            : ''
      const skillName = typeof item.display_name === 'string'
        ? item.display_name.trim()
        : typeof item.skill_name === 'string'
          ? item.skill_name.trim()
          : typeof item.title === 'string'
            ? item.title.trim()
            : skillSlug
      const triggers = Array.isArray(item.triggers)
        ? asStringArray(item.triggers)
        : asStringArray(item.trigger)
      const description = typeof item.description === 'string' ? item.description.trim() : ''

      if (!skillSlug || !skillName) {
        return null
      }

      const normalized: Record<string, unknown> = {
        skill_slug: skillSlug,
        skill_name: skillName,
        triggers,
        description,
      }

      if (typeof item.expected_output === 'string' && item.expected_output.trim()) {
        normalized.expected_output = item.expected_output.trim()
      } else if (typeof item.expectedOutput === 'string' && item.expectedOutput.trim()) {
        normalized.expected_output = item.expectedOutput.trim()
      }

      return normalized
    })
    .filter((item): item is Record<string, unknown> => item !== null)

  if (skills.length === 0) return null

  const payload: Record<string, unknown> = {
    trigger_mode: 'projection_pass',
    workspace_root: workspaceRoot,
    skills,
  }

  if (typeof record.template_slug === 'string' && record.template_slug.trim()) {
    payload.template_slug = record.template_slug.trim()
  }

  return payload
}

function readProjectedCount(projectionResult: unknown): number | null {
  const record = asPlainObject(projectionResult)
  if (!record) return null

  const projectedCount = record.projected_count ?? record.projectedCount
  if (typeof projectedCount === 'number' && Number.isFinite(projectedCount)) {
    return projectedCount
  }

  const projectionPaths = readProjectionPaths(projectionResult)
  return projectionPaths.length > 0 ? projectionPaths.length : null
}

export function hasConsumableProducerProjection(projectionResult: unknown): boolean {
  if (readProjectionPaths(projectionResult).length === 0) {
    return false
  }

  const projectedCount = readProjectedCount(projectionResult)
  return projectedCount !== null && projectedCount > 0
}

export function buildSkillGenerationPayload(
  summary: unknown,
  projectionResult: unknown,
): Record<string, unknown> | null {
  const record = asPlainObject(summary)
  if (
    !record ||
    !hasConsumableProducerProjection(projectionResult) ||
    !projectionSlugsMatchConfirmedSkills(summary, projectionResult)
  ) {
    return null
  }

  return {
    ...record,
    confirmed_skill_slugs: extractConfirmedSkillSlugs(summary),
    projection_skill_slugs: extractProjectionSkillSlugs(projectionResult),
    projection_binding_confirmed: true,
    projection_contract_mode: 'required',
    projection_result: projectionResult,
  }
}

export function buildSkillDefinitionConfirmationPrompt(userRequest: string, summaryDraft: unknown): string {
  const normalizedRequest = userRequest.trim() || 'confirm skill definition'
  const serialized = JSON.stringify(summaryDraft ?? {}, null, 2)

  return [
    '[Internal skill definition confirmation. Do not mention this instruction to the user.]',
    `The visible user request was: ${JSON.stringify(normalizedRequest)}.`,
    'The user has confirmed the current skill definition draft.',
    'Continue under `employment-coach-conversation` stage2_skill rules.',
    'Emit the terminal `skill_workorder_summary` for the confirmed skill list.',
    'Immediately after that, emit non-terminal `ontology_projection_ready` to ask whether to prepare business information for these skills.',
    'Do not trigger ontology projection, skill-generation, external configuration, review, or packaging in this turn.',
    '',
    'latest_skill_definition_context:',
    '```json',
    serialized,
    '```',
  ].join('\n')
}

export function buildDownstreamPrompt(target: DownstreamTarget, payload: unknown): string {
  const serialized = JSON.stringify(payload, null, 2)

  if (target === 'ontology-slice-extraction') {
    return [
      '[Internal downstream trigger: use skill ontology-slice-extraction]',
      'Switch to skill `ontology-slice-extraction` now.',
      'source_skill: employment-coach-conversation',
      'trigger_reason: material_handoff_summary_completed',
      'Do not call sessions, dispatch, handoff, spawn, or yield tools for this trigger.',
      '',
      'Use the terminal `material_handoff_summary` artifact payload below as the upstream summary for this run.',
      'Follow `ontology-slice-extraction/SKILL.md` exactly.',
      'Emit `ontology_slice_extraction_progress` with stage=`stage1_material` before processing any source.',
      'Read uploaded materials only from each item\'s `source_path` when available.',
      'Write outputs under the provided `workspace_root` and finish with `ontology_slice_extraction_done` using stage=`stage1_material`.',
      '',
      'required_artifacts:',
      '- ontology_slice_extraction_progress',
      '- ontology_slice_extraction_done',
      'return_to: employment-coach-conversation',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  if (target === 'ontology-projection') {
    return [
      '[Internal downstream trigger: use skill ontology-projection]',
      'Switch to skill `ontology-projection` now.',
      'source_skill: employment-coach-conversation',
      'trigger_reason: user_confirmed_ontology_projection',
      'Do not call sessions, dispatch, handoff, spawn, or yield tools for this trigger.',
      '',
      'Use the payload below exactly as the trigger input for this run.',
      'Follow `ontology-projection/SKILL.md` exactly.',
      'Treat every `artifact_payload.skills[].skill_slug` as an immutable identifier: projection files must be written under exactly `ontology/projections/<skill_slug>/`; do not rename or synonym-normalize skill slugs.',
      'Emit `ontology_projection_progress` with stage=`stage2_skill` before generating any projection files.',
      'Scan slices from `<workspace_root>/ontology/`, then finish with `ontology_projection_done` using stage=`stage2_skill`.',
      '',
      'required_artifacts:',
      '- ontology_projection_progress',
      '- ontology_projection_done',
      'return_to: employment-coach-conversation',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  if (target === 'skill-generation') {
    return [
      '[Internal downstream trigger: use skill skill-generation]',
      'Switch to skill `skill-generation` now.',
      'source_skill: employment-coach-conversation',
      'trigger_reason: projection_done_generate_skills',
      'Do not call sessions, dispatch, handoff, spawn, or yield tools for this trigger.',
      '',
      'This is an internal mode switch inside the current conversation, not a request to discover another tool or start another conversation.',
      'The user has explicitly approved binding the producer ontology projections into the generated business skills.',
      'Use the enriched `skill_workorder_summary` payload below as the upstream workorder.',
      'Treat `artifact_payload.confirmed_skill_slugs` as the only allowed generated skill directory set; do not create or keep alternate/stale skill directories.',
      'Projection consumer contracts are mandatory for this run. Do not silently downgrade to a base skill package without contracts.',
      'If the provided business-information packages cannot be materialized into `skills/<skill-slug>/contracts/projections/ontology_extraction/`, stop and report the reason instead of continuing without contracts.',
      'Read and follow `skill-generation/SKILL.md` directly in the current session.',
      'Do not use dispatch callbacks or handoff APIs for this path.',
      'Follow `skill-generation/SKILL.md` exactly.',
      'Emit `skill_generation_progress` first with stage=`stage2_skill`, write outputs under `workspace_root/skills/`, then finish with `skill_generation_done` using stage=`stage2_skill`.',
      '',
      'required_artifacts:',
      '- skill_generation_progress',
      '- skill_generation_done',
      'return_to: employment-coach-conversation',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  if (target === 'packaging-test-cases') {
    return [
      '[Internal downstream trigger: use skill packaging-test-cases]',
      'Switch to skill `packaging-test-cases` now.',
      'source_skill: employment-coach-conversation',
      'trigger_reason: user_confirmed_packaging_testcases',
      'Do not call sessions, dispatch, handoff, spawn, or yield tools for this trigger.',
      '',
      'The user has explicitly approved generating optional evaluation test cases before instance packaging.',
      'Use the current session history, uploaded materials, and template package snapshot available in the workspace.',
      'Follow `packaging-test-cases/SKILL.md` exactly.',
      'Emit `packaging_testcases_progress` with stage=`stage4_packaging` before writing any testcase artifact.',
      'Write `testcases/evaluation-test-cases.json` and related source index files when enough information is available.',
      'Finish with `packaging_testcases_done` and return a `dispatch_callback` with source_dispatch_target=`packaging-test-cases` so the backend can sync the generated files.',
      '',
      'required_artifacts:',
      '- packaging_testcases_progress',
      '- packaging_testcases_done',
      'return_to: employment-coach-conversation',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  return ''
}

export function buildPackagingRequestPrompt(userRequest: string, reviewReport?: unknown): string {
  const normalizedRequest = userRequest.trim() || 'continue packaging'
  const reviewLines = reviewReport == null
    ? []
    : [
        'A `review_report` already exists for the current workspace.',
        'The user is explicitly choosing to continue after that review. Do not rerun review, return to an earlier stage, or ask for another review/packaging confirmation.',
        'Carry any review warnings or blockers into `packaging_progress.data.review_risk_summary` before packaging.',
        '',
        'review_report_payload:',
        '```json',
        JSON.stringify(reviewReport, null, 2),
        '```',
      ]

  return [
    '[Internal packaging trigger. Do not mention this instruction to the user.]',
    `The visible user request was: ${JSON.stringify(normalizedRequest)}.`,
    'The user has authorized instance packaging. Do not ask for a package trigger, dispatch target, tool name, or another "start generation" confirmation.',
    ...reviewLines,
    'If review_readiness has not been emitted for the current completed material, skill, and external stages, emit review_readiness first and wait only for the required review decision.',
    'If review_readiness/review_report is already satisfied, package the current employee package workspace now.',
    'Before invoking the package/export/archive tool, emit `packaging_progress` with data.status=`packing`.',
    '`coach_runtime_root` is `/workspace`; it contains the employment-coach system package and must never be packaged.',
    'Resolve `employee_package_root` from the latest terminal artifact `data.workspace_root` or the first workspace FILE_URL. It must look like `/workspace/<template_slug>-<timestamp>`, not `/workspace`.',
    'If `employee_package_root` is missing, equals `/workspace`, or contains `skills/employment-coach-conversation/`, stop and report the concrete root-resolution problem instead of packaging.',
    'Use the available packaging/export/archive tool first. If no dedicated tool exists, change into `employee_package_root` and use a zip tool to package that directory.',
    'The ZIP must be written from inside `employee_package_root` so the archive root contains `manifest.json`, `config/`, `skills/`, `ontology/`, `external/`, and optional `testcases/` directly.',
    'The ZIP must be a real downloadable instance package file and must be emitted as a terminal file artifact with artifactType=`template_package`.',
    'Never emit a data-only template_package, never use `/workspace` as a placeholder workspace_root or package root, and never ask the user to provide an unavailable trigger.',
    'If a downloadable ZIP cannot be produced after trying the available tool path and shell ZIP path, emit the protocol failure fallback with the concrete blocking reason.',
  ].join('\n')
}

export function isSkillDefinitionApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const keywords = [
    '确认技能',
    '确认技能清单',
    '技能清单确认',
    '技能定义确认',
    '没问题',
    '可以',
    '确认',
    '通过',
    '继续',
    'yes',
    'ok',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isOntologyProjectionApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const keywords = [
    '匹配技能数据',
    '开始匹配技能数据',
    '匹配数据',
    '开始匹配数据',
    '为技能匹配数据',
    '本体投影',
    '投影',
    '可以',
    '确认',
    '继续',
    'yes',
    'ok',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isSkillGenerationApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const keywords = [
    '采用',
    '采用并继续',
    '确认采用',
    '使用这些资料',
    '用这些资料',
    '继续',
    '开始生成',
    '开始生成吧',
    '生成吧',
    '确认生成',
    '继续生成',
    '开始实现',
    '生成技能',
    '生成技能实现',
    '可以开始生成',
    '可以生成',
    'goahead',
    'startgenerating',
    'yes',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function resolveSkillStageApprovalRoute(input: SkillStageApprovalRouteInput): SkillStageApprovalRoute {
  if (input.incomingFileCount > 0) {
    return 'none'
  }

  // 阶段 2 的确认门会分布在两个下游轨道里；后续确认门必须优先吃掉“继续”这类通用确认词。
  if (
    isWaitingArtifact(input.skillGenerationState, 'skill_generation_ready') &&
    input.hasSkillSummary &&
    input.hasProjectionResult &&
    isSkillGenerationApprovalMessage(input.text)
  ) {
    return 'launch_skill_generation'
  }

  if (
    isWaitingArtifact(input.ontologyProjectionState, 'ontology_projection_ready') &&
    input.hasSkillSummary &&
    isOntologyProjectionApprovalMessage(input.text)
  ) {
    return 'launch_projection_pass'
  }

  if (
    isWaitingArtifact(input.skillGenerationState, 'skill_definition_ready') &&
    isSkillDefinitionApprovalMessage(input.text)
  ) {
    return 'confirm_skill_definition'
  }

  return 'none'
}

export function resolveActiveSkillStageRun(
  skillGenerationState: DownstreamRunState | null,
  ontologyProjectionState: DownstreamRunState | null,
): DownstreamRunState | null {
  if (isWaitingArtifact(skillGenerationState, 'skill_generation_ready')) {
    return skillGenerationState
  }

  if (
    skillGenerationState?.status === 'running' ||
    skillGenerationState?.status === 'completed' ||
    skillGenerationState?.status === 'failed'
  ) {
    return skillGenerationState
  }

  if (isWaitingArtifact(ontologyProjectionState, 'ontology_projection_ready')) {
    return ontologyProjectionState
  }

  if (isWaitingArtifact(skillGenerationState, 'skill_definition_ready')) {
    return skillGenerationState
  }

  return null
}

export function isPackagingTestCasesApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const exactApprovals = new Set([
    '生成',
    '开始生成',
    '生成吧',
    '可以生成',
    '可以',
    '开始',
    '确认',
    '确认生成',
    '好的',
    '好',
    '需要',
    '要',
    'yes',
    'y',
    'ok',
    'go',
  ])
  if (exactApprovals.has(compact)) return true

  const keywords = [
    '生成测试用例',
    '生成评估用例',
    '开始生成测试用例',
    '进行测试用例生成',
    '需要测试用例',
    '可以生成测试用例',
    '确认生成测试用例',
    'testcase',
    'testcases',
    'yes',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isPackagingTestCasesSkipMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const keywords = [
    '跳过',
    '不生成',
    '不用',
    '不需要',
    '先不管',
    '直接打包',
    '直接生成包',
    '生成实例包',
    '生成数字员工',
    '生成数字员工包',
    '直接生成数字员工',
    '打成zip',
    'generateinstancepackage',
    'generatetheinstancepackage',
    'generatethedigitalemployee',
    'skip',
    'no',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isPackagingRequestMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const keywords = [
    '生成产物包',
    '生成实例包',
    '生成数字员工',
    '生成数字员工包',
    '开始生成数字员工',
    '开始打包',
    '直接打包',
    '直接生成包',
    '直接生成实例包',
    '发起打包',
    '继续',
    '继续打包',
    '导出',
    '打成zip',
    '完成打包',
    'generateinstancepackage',
    'generatethedigitalemployee',
    'generatepackage',
    'package',
    'continue',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function resolvePackagingRequestRoute(input: PackagingRequestRouteInput): PackagingRequestRoute {
  if (input.incomingFileCount > 0 || !isPackagingRequestMessage(input.text)) {
    return 'none'
  }

  if (input.hasPendingPackageArtifact) {
    return 'import_existing_package'
  }

  if (input.packagingInProgress) {
    return 'wait_for_active_packaging'
  }

  if (input.isBlockedByRequiredConfirmation || input.isBlockedByPackagingTestCaseGeneration) {
    return 'none'
  }

  return input.hasReviewReport || input.hasPackagingContext || input.hasCompletedCoreSummaries
    ? 'launch_packaging_request'
    : 'none'
}
