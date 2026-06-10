import { asPlainObject, asStringArray } from './hiringCacheNormalizers'

export type DownstreamTarget = 'ontology-extraction' | 'ontology-projection' | 'skill-generation' | 'packaging-test-cases'

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

function extractProjectionSkillSlugs(projectionResult: unknown): string[] {
  const record = asPlainObject(projectionResult)
  const paths = Array.isArray(record?.projection_paths)
    ? record.projection_paths
    : Array.isArray(record?.projectionPaths)
      ? record.projectionPaths
      : []

  return Array.from(new Set(
    paths
      .map(path => typeof path === 'string' ? extractProjectionSkillSlug(path) : '')
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
  return typeof projectedCount === 'number' && Number.isFinite(projectedCount)
    ? projectedCount
    : null
}

export function hasConsumableProducerProjection(projectionResult: unknown): boolean {
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

export function buildDownstreamPrompt(target: DownstreamTarget, payload: unknown): string {
  const serialized = JSON.stringify(payload, null, 2)

  if (target === 'ontology-extraction') {
    return [
      '[Internal downstream trigger. Do not mention this instruction to the user.]',
      'Switch to skill `ontology-extraction` now.',
      'Use the terminal `material_handoff_summary` artifact payload below as the upstream summary for this run.',
      'Follow `ontology-extraction/SKILL.md` exactly.',
      'Emit `ontology_extraction_progress` before processing any source.',
      'Read uploaded materials only from each item\'s `source_path` when available.',
      'Write outputs under the provided `workspace_root` and finish with `ontology_extraction_done`.',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  if (target === 'ontology-projection') {
    return [
      '[Internal downstream trigger. Do not mention this instruction to the user.]',
      'Switch to skill `ontology-extraction` now.',
      'Run in Projection Pass mode for the current session.',
      'Use the payload below exactly as the trigger input for this run.',
      'Follow `ontology-extraction/SKILL.md` exactly.',
      'Treat every `artifact_payload.skills[].skill_slug` as an immutable identifier: projection files must be written under exactly `ontology/projections/<skill_slug>/`; do not rename or synonym-normalize skill slugs.',
      'Emit `ontology_projection_progress` before generating any projection files.',
      'Scan slices from `<workspace_root>/ontology/`, then finish with `ontology_projection_done`.',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  if (target === 'skill-generation') {
    return [
      '[Internal downstream trigger. Do not mention this instruction to the user.]',
      'Switch to skill `skill-generation` now.',
      'This is an internal mode switch inside the current session, not a request to discover another tool, spawn another session, or call any dispatch / handoff API.',
      'The user has explicitly approved binding the producer ontology projections into the generated business skills.',
      'Use the enriched `skill_workorder_summary` payload below as the upstream workorder.',
      'Treat `artifact_payload.confirmed_skill_slugs` as the only allowed generated skill directory set; do not create or keep alternate/stale skill directories.',
      'Projection consumer contracts are mandatory for this run. Do not silently downgrade to a base skill package without contracts.',
      'If the provided business-information packages cannot be materialized into `skills/<skill-slug>/contracts/projections/ontology_extraction/`, stop and report the reason instead of continuing without contracts.',
      'Read and follow `skill-generation/SKILL.md` directly in the current session.',
      'Do not use `dispatch`, `dispatch_callback`, `handoff_id`, `sessions_spawn`, or `sessions_yield` for this path.',
      'Follow `skill-generation/SKILL.md` exactly.',
      'Emit `skill_generation_progress` first, write outputs under `workspace_root/skills/`, then finish with `skill_generation_done`.',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  if (target === 'packaging-test-cases') {
    return [
      '[Internal downstream trigger. Do not mention this instruction to the user.]',
      'Switch to skill `packaging-test-cases` now.',
      'The user has explicitly approved generating optional evaluation test cases before instance packaging.',
      'Use the current session history, uploaded materials, and template package snapshot available in the workspace.',
      'Follow `packaging-test-cases/SKILL.md` exactly.',
      'Emit `packaging_testcases_progress` before writing any testcase artifact.',
      'Write `testcases/evaluation-test-cases.json` and related source index files when enough information is available.',
      'Finish with `packaging_testcases_done` and return a `dispatch_callback` with source_dispatch_target=`packaging-test-cases` so the backend can sync the generated files.',
      '',
      'artifact_payload:',
      '```json',
      serialized,
      '```',
    ].join('\n')
  }

  return ''
}

export function isSkillGenerationApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
  const keywords = [
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

export function isPackagingTestCasesApprovalMessage(text: string): boolean {
  const normalized = text.trim().toLowerCase()
  if (!normalized) return false

  const compact = normalized.replace(/[\s,.;:!?'"`~\-_/\\|()[\]{}<>，。！？；：、""'']+/g, '')
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
    '直接生成数字员工',
    'skip',
    'no',
  ]

  return keywords.some(keyword => compact.includes(keyword))
}
