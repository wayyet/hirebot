import type { DownstreamRunState } from '../hiringPageTypes'

import { asPlainObject, asStringArray } from './hiringCacheNormalizers'

export type DownstreamTarget = 'ontology-slice-extraction' | 'ontology-projection' | 'skill-generation' | 'packaging-test-cases'

export type SkillStageApprovalRoute =
  | 'none'
  | 'enter_skill_definition'
  | 'confirm_skill_definition'
  | 'launch_projection_pass'
  | 'launch_skill_generation'

export type PackagingRequestRoute =
  | 'none'
  | 'import_existing_package'
  | 'wait_for_active_packaging'
  | 'launch_packaging_request'

export type PackageReviewDecisionRoute =
  | 'none'
  | 'launch_package_review'
  | 'skip_review_and_package'

export type ExternalSystemEntryRoute =
  | 'none'
  | 'enter_external_system'
  | 'skip_external_system'

export interface SkillStageApprovalRouteInput {
  text: string
  incomingFileCount: number
  skillGenerationState: DownstreamRunState | null
  ontologyProjectionState: DownstreamRunState | null
  skillDefinitionEntryState?: DownstreamRunState | null
  hasSkillSummary: boolean
  hasProjectionResult: boolean
}

export interface PackagingRequestRouteInput {
  text: string
  incomingFileCount: number
  isBlockedByRequiredConfirmation: boolean
  isBlockedByPackagingTestCaseGeneration: boolean
  hasPendingPackageReviewDecision: boolean
  hasPendingPackageArtifact: boolean
  packagingInProgress: boolean
  hasReviewReport: boolean
  hasPackagingContext: boolean
  hasCompletedCoreSummaries: boolean
}

export interface PackageReviewDecisionRouteInput {
  text: string
  incomingFileCount: number
  hasPendingPackageReviewDecision: boolean
  isBlockedByRequiredConfirmation: boolean
  isBlockedByPackagingTestCaseGeneration: boolean
}

export interface ExternalSystemEntryRouteInput {
  text: string
  incomingFileCount: number
  externalSystemEntryState: DownstreamRunState | null
}

export interface SkillDefinitionConfirmationScope {
  confirmedSkillSlugs: string[]
  confirmedItems: Record<string, unknown>[]
  draftItemCount: number
  selectionMode: 'all' | 'selected' | 'excluded'
}

function isWaitingArtifact(
  run: DownstreamRunState | null,
  artifactType: string,
): boolean {
  return run?.status === 'waiting_confirm' && run.artifactType === artifactType
}

function isCompletedArtifact(
  run: DownstreamRunState | null,
  artifactType: string,
): boolean {
  return run?.status === 'completed' && run.artifactType === artifactType
}

function isSkillImplementationArtifact(artifactType: string | undefined): boolean {
  return artifactType === 'skill_generation_ready' ||
    artifactType === 'skill_projection_binding_ready' ||
    artifactType === 'skill_generation_progress' ||
    artifactType === 'skill_generation_done'
}

function isOntologyProjectionArtifact(artifactType: string | undefined): boolean {
  return artifactType === 'ontology_projection_ready' ||
    artifactType === 'ontology_projection_progress' ||
    artifactType === 'ontology_projection_done'
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

function normalizeSelectionText(value: string): string {
  return value
    .trim()
    .toLowerCase()
    .replace(/[\s\p{P}\p{S}]+/gu, '')
}

function readSkillDisplayName(item: Record<string, unknown>): string {
  const candidates = [
    item.display_name,
    item.displayName,
    item.skill_name,
    item.skillName,
    item.title,
    item.name,
  ]

  for (const candidate of candidates) {
    if (typeof candidate !== 'string') continue
    const text = candidate.trim()
    if (text) return text
  }

  return ''
}

function isSkillSlug(value: string): boolean {
  return /^[a-z0-9][a-z0-9_-]*$/.test(value)
}

function readSkillSlug(item: Record<string, unknown>): string {
  const candidates = [
    item.skill_slug,
    item.skillSlug,
    item.name,
    item.skillName,
  ]

  for (const candidate of candidates) {
    if (typeof candidate !== 'string') continue
    const slug = candidate.trim()
    if (slug && isSkillSlug(slug)) {
      return slug
    }
  }

  return ''
}

function parseChineseSelectionNumber(value: string): number | null {
  const normalized = value.trim()
  if (/^\d+$/.test(normalized)) {
    const number = Number(normalized)
    return Number.isFinite(number) ? number : null
  }

  const digitMap: Record<string, number> = {
    一: 1,
    二: 2,
    两: 2,
    三: 3,
    四: 4,
    五: 5,
    六: 6,
    七: 7,
    八: 8,
    九: 9,
  }

  if (normalized === '十') return 10
  if (normalized.startsWith('十')) {
    const ones = digitMap[normalized.slice(1)] ?? 0
    return 10 + ones
  }
  if (normalized.endsWith('十')) {
    const tens = digitMap[normalized.slice(0, -1)]
    return tens ? tens * 10 : null
  }
  if (normalized.includes('十')) {
    const [tensText, onesText] = normalized.split('十')
    const tens = digitMap[tensText]
    const ones = digitMap[onesText] ?? 0
    return tens ? tens * 10 + ones : null
  }

  return digitMap[normalized] ?? null
}

function extractReferencedSkillIndexes(text: string, maxCount: number): Set<number> {
  const indexes = new Set<number>()
  const normalized = text.trim()
  const addIndex = (value: number | null) => {
    if (value !== null && value >= 1 && value <= maxCount) {
      indexes.add(value - 1)
    }
  }

  const leadingCountMatch = /前\s*([0-9一二两三四五六七八九十]+)\s*(?:个|项|条|个技能|项技能|条技能)?/.exec(normalized)
  if (leadingCountMatch?.[1]) {
    const count = parseChineseSelectionNumber(leadingCountMatch[1])
    if (count !== null && count > 0) {
      for (let i = 0; i < Math.min(count, maxCount); i += 1) {
        indexes.add(i)
      }
    }
  }

  const explicitPattern = /(?:第|#|编号|序号)?\s*([0-9一二两三四五六七八九十]+)\s*(?:个|项|条|号)?/g
  let match: RegExpExecArray | null
  while ((match = explicitPattern.exec(normalized)) !== null) {
    const before = normalized.slice(Math.max(0, match.index - 4), match.index)
    const after = normalized.slice(explicitPattern.lastIndex, explicitPattern.lastIndex + 4)
    const hasSelectionContext = /第|#|编号|序号|选|选择|只要|保留|采用|确认|不要|不用|排除|去掉|删掉|删除|和|、|,|，/.test(`${before}${after}`)
    if (hasSelectionContext) {
      addIndex(parseChineseSelectionNumber(match[1]))
    }
  }

  return indexes
}

function isExcludeSelection(text: string): boolean {
  return /(不要|不用|排除|去掉|删掉|删除|去除)/.test(text)
}

function isAllSelection(text: string): boolean {
  return /(全部|全都|所有|全选|都可以|都确认|当前技能清单|当前清单|整个清单)/.test(text)
}

export function resolveSkillDefinitionConfirmationScope(
  userRequest: string,
  summaryDraft: unknown,
): SkillDefinitionConfirmationScope {
  const draftItems = extractSkillWorkorderItems(summaryDraft)
    .map((item, index) => {
      const slug = readSkillSlug(item)
      if (!slug) return null

      return {
        index,
        slug,
        item: {
          ...item,
          name: slug,
          skill_slug: slug,
        },
        normalizedSlug: normalizeSelectionText(slug),
        normalizedDisplayName: normalizeSelectionText(readSkillDisplayName(item)),
      }
    })
    .filter((item): item is NonNullable<typeof item> => item !== null)

  const requestText = userRequest.trim()
  const normalizedRequest = normalizeSelectionText(requestText)
  const referencedIndexes = extractReferencedSkillIndexes(requestText, draftItems.length)
  const referencedSlugs = new Set<string>()

  for (const draft of draftItems) {
    const displayHit = draft.normalizedDisplayName && normalizedRequest.includes(draft.normalizedDisplayName)
    const slugHit = draft.normalizedSlug && normalizedRequest.includes(draft.normalizedSlug)
    if (displayHit || slugHit || referencedIndexes.has(draft.index)) {
      referencedSlugs.add(draft.slug)
    }
  }

  const excludeMode = isExcludeSelection(requestText)
  const explicitAll = isAllSelection(requestText)
  const selectedItems = draftItems.filter(draft => {
    if (referencedSlugs.size === 0 || (explicitAll && !excludeMode)) {
      return true
    }

    const referenced = referencedSlugs.has(draft.slug)
    return excludeMode ? !referenced : referenced
  })

  const selectionMode = referencedSlugs.size === 0 || (explicitAll && !excludeMode)
    ? 'all'
    : excludeMode
      ? 'excluded'
      : 'selected'

  return {
    confirmedSkillSlugs: selectedItems.map(item => item.slug),
    confirmedItems: selectedItems.map(item => item.item),
    draftItemCount: draftItems.length,
    selectionMode,
  }
}

export function getSkillWorkorderSummaryConfirmationMismatchReason(
  summary: unknown,
  confirmedSkillSlugs: string[],
): string | null {
  if (confirmedSkillSlugs.length === 0) return null

  const expected = new Set(confirmedSkillSlugs)
  const actual = extractConfirmedSkillSlugs(summary)
  if (actual.length === 0) {
    return 'skill_workorder_summary confirmed items are missing'
  }

  for (const slug of actual) {
    if (!expected.has(slug)) {
      return `skill_workorder_summary contains unconfirmed skill: ${slug}`
    }
  }

  for (const slug of expected) {
    if (!actual.includes(slug)) {
      return `skill_workorder_summary is missing confirmed skill: ${slug}`
    }
  }

  return null
}

function extractConfirmedSkillSlugs(summary: unknown): string[] {
  return extractSkillWorkorderItems(summary)
    .map(readSkillSlug)
    .filter(slug => slug.length > 0)
}

function normalizeSkillWorkorderItems(summary: unknown): Record<string, unknown>[] {
  return extractSkillWorkorderItems(summary)
    .map((item): Record<string, unknown> | null => {
      const slug = readSkillSlug(item)
      if (!slug) return null

      return {
        ...item,
        name: slug,
        skill_slug: slug,
      }
    })
    .filter((item): item is Record<string, unknown> => item !== null)
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
      const skillSlug = readSkillSlug(item)
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

      if (typeof item.generation_action === 'string' && item.generation_action.trim()) {
        normalized.generation_action = item.generation_action.trim()
      } else if (typeof item.generationAction === 'string' && item.generationAction.trim()) {
        normalized.generation_action = item.generationAction.trim()
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

  const businessRules = record.business_rules_captured_so_far ?? record.business_rules
  if (businessRules != null) {
    payload.business_rules = businessRules
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
    items: normalizeSkillWorkorderItems(summary),
    confirmed_skill_slugs: extractConfirmedSkillSlugs(summary),
    projection_skill_slugs: extractProjectionSkillSlugs(projectionResult),
    projection_binding_confirmed: true,
    projection_contract_mode: 'required',
    projection_result: projectionResult,
  }
}

export function buildSkillDefinitionConfirmationPrompt(
  userRequest: string,
  summaryDraft: unknown,
  confirmationScope = resolveSkillDefinitionConfirmationScope(userRequest, summaryDraft),
): string {
  const normalizedRequest = userRequest.trim() || 'confirm skill definition'
  const serialized = JSON.stringify({
    selection_mode: confirmationScope.selectionMode,
    confirmed_skill_slugs: confirmationScope.confirmedSkillSlugs,
    confirmed_items: confirmationScope.confirmedItems,
    draft_item_count: confirmationScope.draftItemCount,
    original_skill_definition_draft: summaryDraft ?? {},
  }, null, 2)

  return [
    '[Internal skill definition confirmation. Do not mention this instruction to the user.]',
    `The visible user request was: ${JSON.stringify(normalizedRequest)}.`,
    'The user has confirmed the current skill definition draft.',
    'Continue under `employment-coach-conversation` stage2_skill rules.',
    'Emit the terminal `skill_workorder_summary` for the confirmed skill list.',
    'Only include `confirmed_items` in `skill_workorder_summary.data.items`; draft items outside `confirmed_skill_slugs` are not confirmed.',
    'Copy each confirmed item field from `confirmed_items` unless a required field is missing and must be recovered from the original draft.',
    'Include `confirmed_skill_slugs` in `skill_workorder_summary.data` and make it exactly match `items[].name`.',
    '`skill_workorder_summary.data` must contain top-level `workspace_root`, `template_slug`, and a non-empty `items` array.',
    'Each `items[]` entry must contain `name`, `display_name`, `description`, `trigger`, `expected_output`, and `generation_action` as non-empty strings.',
    'For every `items[]`, set `name` and `skill_slug` to the same stable lowercase ASCII skill slug used for directories; put the Chinese/user-facing label only in `display_name`.',
    'If the real `workspace_root` or `template_slug` is missing, do not emit `skill_workorder_summary`; recover those session constants first.',
    'Immediately after that, emit non-terminal `ontology_projection_ready` to ask whether to prepare business information for these skills.',
    'Do not trigger ontology projection, skill-generation, external configuration, review, or packaging in this turn.',
    '',
    'confirmed_skill_definition_context:',
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
      'Keep executing this downstream task in this turn until the required files are written and `ontology_slice_extraction_done` is emitted; do not stop after a status check or a waiting/progress-only reply.',
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
      'This message supersedes any earlier template bootstrap or stage1 initialization prompt in the conversation history.',
      'Do not interpret earlier bootstrap text as a new user request to reinitialize or return to stage1.',
      'The user has already confirmed the current `ontology_projection_ready` gate; do not ask them to reply with another confirmation phrase.',
      'In user-visible text, do not say "投影", "投影绑定", or "projection"; say "匹配技能数据".',
      '',
      'Use the payload below exactly as the trigger input for this run.',
      'Follow `ontology-projection/SKILL.md` exactly.',
      'Treat every `artifact_payload.skills[].skill_slug` as an immutable identifier: projection files must be written under exactly `ontology/projections/<skill_slug>/`; do not rename or synonym-normalize skill slugs.',
      'Emit `ontology_projection_progress` with stage=`stage2_skill` before generating any projection files.',
      'Scan slices from `<workspace_root>/ontology/`.',
      'For each generated projection JSON, call the sandbox file-writing tool (`write_file` preferred; otherwise the available `create_file`/`save_file` equivalent) to write the file. Do not use shell, Python here-docs, echo, or narrative-only output to create projection files.',
      'After writing each projection file, call `read_file` on that exact path and verify the JSON is complete with top-level `projection_type`, `source_slice`, `intended_consumers`, and `concept_mappings` before counting it as projected.',
      'A written projection file is not enough by itself: the terminal `ontology_projection_done.data` must be the aggregate handoff object that includes `projected_count` and every verified relative path in `projection_paths`.',
      'Do not put the projection file JSON itself, a `read_file` result, or an empty object in `ontology_projection_done.data`; the main hiring flow only consumes the aggregate handoff object.',
      'If the file-writing tool is unavailable or read-back verification fails after bounded retry, mark the skill skipped with `slices_not_ready`; do not emit a successful `ontology_projection_done` for an unwritten or stub projection.',
      'If a valid projection still has `open_questions`, keep it as a projected WARNING result and surface those questions precisely; do not ask the user to rerun the same projection pass just because questions remain.',
      'For a projected WARNING result with consumable `projection_paths`, never say the business information is insufficient or not directly implementable. Describe it as matched skill data with pre-generation confirmation items.',
      'Finish with `ontology_projection_done` using stage=`stage2_skill` only after file write and read-back verification.',
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
      '`projection_binding_confirmed` is already set to true by the system in `artifact_payload`; it is not a user-facing confirmation step.',
      'Do not ask the user to confirm projection binding again, and do not ask for another phrase such as "开始生成技能实现".',
      'In user-visible text, do not say "投影", "投影绑定", or "projection"; say "匹配技能数据".',
      'Use the enriched `skill_workorder_summary` payload below as the upstream workorder.',
      'Treat `artifact_payload.confirmed_skill_slugs` as the only allowed generated skill directory set; do not create or keep alternate/stale skill directories.',
      'Projection consumer contracts are mandatory for this run. Do not silently downgrade to a base skill package without contracts.',
      'If `artifact_payload.projection_result.open_questions` is non-empty, treat it as pre-generation confirmation context that has already been carried through the confirmation gate. It is not a business-information insufficiency signal.',
      'For a consumable projection with `projected_count > 0` and matching `projection_paths`, continue skill generation and materialize WARNING consumer contracts instead of asking the user to "补资料", rerun business-information extraction, or rerun skill-data matching.',
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

function buildPackageZipInstructionLines(): string[] {
  return [
    'Before invoking the package/export/archive tool, emit `packaging_progress` with data.status=`packing`.',
    '`coach_runtime_root` is `/workspace`; it contains the employment-coach system package and must never be packaged.',
    'Resolve `employee_package_root` from the latest terminal artifact `data.workspace_root` or the first workspace FILE_URL. It must look like `/workspace/<template_slug>-<timestamp>`, not `/workspace`.',
    'If `employee_package_root` is missing, equals `/workspace`, or contains `skills/employment-coach-conversation/`, stop and report the concrete root-resolution problem instead of packaging.',
    'Use the available packaging/export/archive tool first. If no dedicated tool exists, change into `employee_package_root` and use a zip tool to package that directory.',
    'The ZIP must be written from inside `employee_package_root` so the archive root contains `manifest.json`, `config/`, `skills/`, `ontology/`, `external/`, and optional `testcases/` directly.',
    'The ZIP must be a real downloadable instance package file and must be emitted as a terminal file artifact with artifactType=`template_package`.',
    'Never emit a data-only template_package, never use `/workspace` as a placeholder workspace_root or package root, and never ask the user to provide an unavailable trigger.',
    'If a downloadable ZIP cannot be produced after trying the available tool path and shell ZIP path, emit the protocol failure fallback with the concrete blocking reason.',
  ]
}

function buildManifestSyncInstructionLines(nextBlockedArtifact: 'review_readiness' | 'review_progress' | 'template_package'): string[] {
  return [
    'Before any package review or package/export/archive operation, synchronize `<employee_package_root>/manifest.json` and verify it by reading it back.',
    'Resolve the current generated business skill whitelist from the latest `skill_generation_done.data.skill_slugs`; if unavailable, fall back to the latest `skill_workorder_summary.data.items[].name`.',
    'Set or update `manifest.entry_skill` to `skills/<first-current-business-skill-slug>/SKILL.md`; if no current business skill exists or the file is missing, stop before review or packaging.',
    'Synchronize `manifest.skills` so it includes exactly the current generated business skill entries plus built-in template skills, updating each current skill path to `skills/<slug>/SKILL.md` and removing stale generated business skill entries that are not in the current whitelist.',
    'Synchronize `manifest.ontology_slices` from top-level runtime files matching `<employee_package_root>/ontology/*.slice.json`; preserve existing ontology convention docs such as `ontology/ontology-slice.md` and append missing runtime slice entries.',
    'Write the updated manifest as valid JSON, then read it back and verify: `entry_skill` resolves to an existing file, every current skill is declared in `manifest.skills`, and every top-level runtime `*.slice.json` is declared in `manifest.ontology_slices`.',
    `If manifest read-back verification fails, do not emit \`${nextBlockedArtifact}\`, do not start review, and do not package; explain the concrete manifest field that could not be synchronized.`,
  ]
}

export function buildPackagingRequestPrompt(userRequest: string, reviewReport?: unknown): string {
  const normalizedRequest = userRequest.trim() || 'continue packaging'
  if (reviewReport == null) {
    return [
      '[Internal packaging review gate trigger. Do not mention this instruction to the user.]',
      `The visible user request was: ${JSON.stringify(normalizedRequest)}.`,
      'The user has authorized entering stage4 packaging, but the required package review decision has not been collected yet.',
      'Continue under `employment-coach-conversation` stage4_packaging rules only until the review decision gate.',
      'Run the mandatory pre-package sequence before the review gate: emit `packaging_progress` with data.status=`packing`, perform projection-consumer consistency precheck, then sync `manifest.json`.',
      ...buildManifestSyncInstructionLines('review_readiness'),
      'After manifest sync, emit non-terminal `review_readiness` with data.status=`ready_for_review_decision` and ask the user whether to run completeness review or skip review and package directly.',
      'Stop immediately after `review_readiness`. Do not emit `review_progress`, do not emit `review_report`, do not invoke package/export/archive tools, and do not emit `template_package` until the user answers the review decision.',
      '`coach_runtime_root` is `/workspace`; it contains the employment-coach system package and must never be packaged.',
      'Resolve `employee_package_root` from the latest terminal artifact `data.workspace_root` or the first workspace FILE_URL. It must look like `/workspace/<template_slug>-<timestamp>`, not `/workspace`.',
      'If `employee_package_root` is missing, equals `/workspace`, or contains `skills/employment-coach-conversation/`, stop and report the concrete root-resolution problem instead of continuing.',
    ].join('\n')
  }

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
    'The package review gate is already satisfied by the provided `review_report`; package the current employee package workspace now.',
    ...buildManifestSyncInstructionLines('template_package'),
    ...buildPackageZipInstructionLines(),
  ].join('\n')
}

export function buildPackageReviewPrompt(userRequest: string): string {
  const normalizedRequest = userRequest.trim() || 'run package completeness review'

  return [
    '[Internal package review trigger. Do not mention this instruction to the user.]',
    `The visible user request was: ${JSON.stringify(normalizedRequest)}.`,
    'The user has explicitly chosen to run package completeness review after `review_readiness`.',
    'Do not invoke package/export/archive tools and do not emit `template_package` in this turn.',
    ...buildManifestSyncInstructionLines('review_progress'),
    'Emit `review_progress` with stage=`stage4_packaging` and data.status=`running` before starting the review.',
    'Switch to skill `digital-employee-package-completeness-review` now.',
    'source_skill: employment-coach-conversation',
    'trigger_reason: user_confirmed_package_review',
    'Do not call sessions, dispatch, handoff, spawn, or yield tools for this trigger.',
    'Resolve `package_root` from the current employee package workspace, not from `/workspace` and not from the employment-coach system package.',
    'Follow `digital-employee-package-completeness-review/SKILL.md` exactly.',
    'Finish with terminal `review_report` using stage=`stage4_packaging`.',
    'After emitting `review_report`, stop. Do not ask whether to fix blockers, rerun review, continue packaging, or choose a next step in the same turn; the next user message will be routed deterministically from the `review_report` artifact.',
    '',
    'required_artifacts:',
    '- review_progress',
    '- review_report',
    'return_to: employment-coach-conversation',
  ].join('\n')
}

export function buildPackageReviewSkipPackagingPrompt(userRequest: string): string {
  const normalizedRequest = userRequest.trim() || 'skip review and package'

  return [
    '[Internal package review skip trigger. Do not mention this instruction to the user.]',
    `The visible user request was: ${JSON.stringify(normalizedRequest)}.`,
    'The user has explicitly skipped package completeness review after `review_readiness` and wants to package directly.',
    'Do not run `digital-employee-package-completeness-review`, do not emit `review_progress`, and do not emit `review_report`.',
    'Continue under `employment-coach-conversation` stage4_packaging rules from the post-review-decision packaging step.',
    ...buildManifestSyncInstructionLines('template_package'),
    ...buildPackageZipInstructionLines(),
  ].join('\n')
}

function compactUserText(text: string): string {
  return text
    .trim()
    .toLowerCase()
    .replace(/[\s\p{P}\p{S}]+/gu, '')
}

export function isSkillDefinitionApprovalMessage(text: string): boolean {
  const compact = compactUserText(text)
  if (!compact) return false

  const keywords = [
    '确认技能',
    '确认技能清单',
    '技能清单确认',
    '技能定义确认',
    '没问题',
    '可以',
    '确认',
    '采用',
    '选择',
    '选',
    '选第',
    '只要',
    '保留',
    '不要第',
    '不用第',
    '排除第',
    '去掉第',
    '通过',
    '继续',
    'yes',
    'ok',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isOntologyProjectionApprovalMessage(text: string): boolean {
  const compact = compactUserText(text)
  if (!compact) return false

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
  const compact = compactUserText(text)
  if (!compact) return false

  const keywords = [
    '采用',
    '采用并继续',
    '确认采用',
    '使用这些资料',
    '用这些资料',
    '继续',
    '生成',
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

  // 阶段 2 的确认门分布在多个下游轨道里；后续确认门优先消费“继续”这类通用确认词。
  if (
    isWaitingArtifact(input.skillGenerationState, 'skill_generation_ready') &&
    input.hasSkillSummary &&
    input.hasProjectionResult &&
    isSkillGenerationApprovalMessage(input.text)
  ) {
    return 'launch_skill_generation'
  }

  if (
    isCompletedArtifact(input.ontologyProjectionState, 'ontology_projection_done') &&
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

  if (
    isWaitingArtifact(input.skillDefinitionEntryState ?? null, 'skill_definition_entry_ready') &&
    isSkillDefinitionEntryApprovalMessage(input.text)
  ) {
    return 'enter_skill_definition'
  }

  return 'none'
}
export function resolveExternalSystemEntryRoute(input: ExternalSystemEntryRouteInput): ExternalSystemEntryRoute {
  if (input.incomingFileCount > 0) {
    return 'none'
  }

  if (!isWaitingArtifact(input.externalSystemEntryState, 'external_system_entry_ready')) {
    return 'none'
  }

  if (isExternalSystemSkipMessage(input.text)) {
    return 'skip_external_system'
  }

  if (isExternalSystemEntryMessage(input.text)) {
    return 'enter_external_system'
  }

  return 'none'
}

export function resolveActiveSkillStageRun(
  skillGenerationState: DownstreamRunState | null,
  ontologyProjectionState: DownstreamRunState | null,
  skillDefinitionEntryState: DownstreamRunState | null = null,
): DownstreamRunState | null {
  if (isWaitingArtifact(skillGenerationState, 'skill_generation_ready')) {
    return skillGenerationState
  }

  if (
    isSkillImplementationArtifact(skillGenerationState?.artifactType) &&
    (
      skillGenerationState?.status === 'running' ||
      skillGenerationState?.status === 'completed' ||
      skillGenerationState?.status === 'failed'
    )
  ) {
    return skillGenerationState
  }

  if (ontologyProjectionState && isCompletedArtifact(ontologyProjectionState, 'ontology_projection_done')) {
    return {
      key: 'skill-generation',
      status: 'waiting_confirm',
      artifactType: 'skill_generation_ready',
      label: '等待确认生成技能实现',
      displayHint: 'badge',
      updatedAt: ontologyProjectionState.updatedAt,
      data: ontologyProjectionState.data,
    }
  }

  if (
    isOntologyProjectionArtifact(ontologyProjectionState?.artifactType) &&
    ontologyProjectionState?.status !== 'completed'
  ) {
    return ontologyProjectionState
  }

  if (isWaitingArtifact(skillGenerationState, 'skill_definition_ready')) {
    return skillGenerationState
  }

  if (skillGenerationState?.status === 'running' && skillGenerationState.artifactType === 'skill_workorder_progress') {
    return skillGenerationState
  }

  if (isWaitingArtifact(skillDefinitionEntryState, 'skill_definition_entry_ready')) {
    return skillDefinitionEntryState
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

export function isPackageReviewApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const negativeKeywords = [
    '跳过',
    '不用',
    '不需要',
    '不审查',
    '直接打包',
    'skip',
    'no',
  ]
  if (negativeKeywords.some(keyword => compact.includes(keyword))) {
    return false
  }

  const exactApprovals = new Set([
    '审查',
    '检查',
    '开始审查',
    '开始检查',
    '好',
    '好的',
    '开始',
    '需要',
    '要',
    'yes',
    'y',
    'ok',
    'review',
  ])
  if (exactApprovals.has(compact)) return true

  const keywords = [
    '完整性审查',
    '进行审查',
    '做审查',
    '先审查',
    '检查完整性',
    'packagecompletenessreview',
    'runreview',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isMaterialHandoffApprovalMessage(text: string): boolean {
  const compact = compactUserText(text)
  if (!compact) return false

  const keywords = [
    '开始分析业务资料',
    '分析业务资料',
    '资料收口',
    '确认资料',
    '按当前资料',
    '开始分析',
    '可以推进',
    '推进到下一步',
    '下一步',
    '可以',
    '确认',
    '继续',
    'yes',
    'ok',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}
export function isSkillDefinitionEntryApprovalMessage(text: string): boolean {
  const compact = compactUserText(text)
  if (!compact) return false

  const keywords = [
    '进入技能定义',
    '开始技能定义',
    '定义技能',
    '技能定义',
    '确认进入',
    '可以推进',
    '推进到下一步',
    '可以',
    '确认',
    '继续',
    'yes',
    'ok',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}
export function isExternalSystemEntryMessage(text: string): boolean {
  const compact = compactUserText(text)
  if (!compact) return false

  const keywords = [
    '进入外部系统',
    '配置外部系统',
    '外部系统配置',
    '进入外部配置',
    '开始外部系统',
    '需要外部系统',
    '配置mcp',
    'mcp',
    'external',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isExternalSystemSkipMessage(text: string): boolean {
  const compact = compactUserText(text)
  if (!compact) return false

  const keywords = [
    '跳过外部系统',
    '跳过外部配置',
    '不需要外部系统',
    '不用外部系统',
    '无外部系统',
    '直接跳过',
    '跳过',
    'skip',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}

export function isPackageReviewSkipMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const keywords = [
    '跳过',
    '跳过审查',
    '不用',
    '不用审查',
    '不需要',
    '不需要审查',
    '不审查',
    '直接打包',
    '继续打包',
    '继续',
    '打包',
    '生成并打包',
    '生成实例包',
    '生成数字员工',
    '生成数字员工包',
    '打成zip',
    'skipreview',
    'skip',
    'noreview',
    'no',
    'package',
    'continuepackaging',
    'continue',
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

export function resolvePackageReviewDecisionRoute(input: PackageReviewDecisionRouteInput): PackageReviewDecisionRoute {
  if (
    input.incomingFileCount > 0 ||
    !input.hasPendingPackageReviewDecision ||
    input.isBlockedByRequiredConfirmation ||
    input.isBlockedByPackagingTestCaseGeneration
  ) {
    return 'none'
  }

  if (isPackageReviewSkipMessage(input.text) || isPackagingRequestMessage(input.text)) {
    return 'skip_review_and_package'
  }

  if (isPackageReviewApprovalMessage(input.text)) {
    return 'launch_package_review'
  }

  return 'none'
}

export function resolvePackagingRequestRoute(input: PackagingRequestRouteInput): PackagingRequestRoute {
  if (input.incomingFileCount > 0 || !isPackagingRequestMessage(input.text)) {
    return 'none'
  }

  if (input.hasPendingPackageArtifact) {
    return 'import_existing_package'
  }

  if (input.hasPendingPackageReviewDecision) {
    return 'none'
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
