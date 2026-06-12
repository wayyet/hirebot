import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import path from 'node:path'
import {
  getBlockedIncomingArtifactReason,
  KNOWN_HIRING_ARTIFACT_TYPES,
  normalizeIncomingArtifactTerminal,
  shouldDisplayArtifactInConversation,
} from './hiringArtifactGuards'

const emptyState = {
  hasMaterialSummary: false,
  hasOntologyExtractionDone: false,
  hasSkillSummary: false,
  hasProjectionResult: false,
  hasExternalConfigCommitted: false,
}

const validSkillWorkorderSummaryData = {
  workspace_root: '/workspace/template-20260611090000',
  template_slug: 'template',
  total_items: 1,
  items: [
    {
      name: 'order-risk-check',
      display_name: '订单风险检查',
      description: '检查订单风险并输出处置建议',
      trigger: '用户提交订单异常线索',
      expected_output: '输出风险等级和处置清单',
      generation_action: 'generate_new',
      status: 'ready',
    },
  ],
}

function collectContractArtifactTypes(value: unknown, target = new Set<string>()): Set<string> {
  if (!value || typeof value !== 'object') {
    return target
  }

  if (Array.isArray(value)) {
    for (const item of value) {
      collectContractArtifactTypes(item, target)
    }
    return target
  }

  const record = value as Record<string, unknown>
  if (typeof record.type === 'string') {
    target.add(record.type)
  }
  for (const nested of Object.values(record)) {
    collectContractArtifactTypes(nested, target)
  }

  return target
}

describe('getBlockedIncomingArtifactReason', () => {
  it('前端 artifact 白名单与 contracts/artifacts.json 保持一致', () => {
    const contractPath = path.resolve(
      process.cwd(),
      '../back-end/HireBot.ApiService/Assets/DigitalEmployeeTemplates/employment-coach-conversation/skills/employment-coach-conversation/contracts/artifacts.json',
    )
    const contract = JSON.parse(readFileSync(contractPath, 'utf8')) as unknown
    const contractTypes = Array.from(collectContractArtifactTypes(contract)).sort()
    const frontendTypes = [...KNOWN_HIRING_ARTIFACT_TYPES].sort()

    expect(frontendTypes).toEqual(contractTypes)
  })

  it('阻止非协议 artifact 类型', () => {
    expect(getBlockedIncomingArtifactReason('stage2_analysis', emptyState))
      .toBe('unknown hiring artifact type')
    expect(getBlockedIncomingArtifactReason('skill_generation_trigger', emptyState))
      .toBe('unknown hiring artifact type')
    expect(getBlockedIncomingArtifactReason('stage1_material_done', emptyState))
      .toBe('unknown hiring artifact type')
  })

  it('要求 terminal summary 必须携带终态标记', () => {
    expect(getBlockedIncomingArtifactReason('material_handoff_summary', {
      ...emptyState,
      hasMaterialSummary: true,
    }, { isTerminal: false, kind: 'data' }))
      .toBe('material_handoff_summary must be terminal')
  })

  it('阻止没有资料阶段收口的本体抽取进度', () => {
    expect(getBlockedIncomingArtifactReason('ontology_slice_extraction_progress', emptyState))
      .toBe('ontology slice extraction requires material_handoff_summary')
  })

  it('允许资料阶段收口后的本体抽取进度', () => {
    expect(getBlockedIncomingArtifactReason('ontology_slice_extraction_progress', {
      ...emptyState,
      hasMaterialSummary: true,
    })).toBeNull()
  })

  it('阻止没有投影结果的技能生成进度', () => {
    expect(getBlockedIncomingArtifactReason('skill_generation_progress', {
      ...emptyState,
      hasSkillSummary: true,
    })).toBe('skill generation requires ontology_projection_done')
  })

  it('允许完成技能定义和投影后的技能生成进度', () => {
    expect(getBlockedIncomingArtifactReason('skill_generation_progress', {
      ...emptyState,
      hasSkillSummary: true,
      hasProjectionResult: true,
      canUseProjectionForSkillGeneration: true,
    })).toBeNull()
  })

  it('技能定义确认门必须在资料阶段收口后出现', () => {
    expect(getBlockedIncomingArtifactReason('skill_definition_ready', emptyState))
      .toBe('skill definition requires material_handoff_summary')
    expect(getBlockedIncomingArtifactReason('skill_definition_ready', {
      ...emptyState,
      hasMaterialSummary: true,
    }, { isTerminal: false, kind: 'data' })).toBe('skill definition requires ontology_slice_extraction_done')
    expect(getBlockedIncomingArtifactReason('skill_definition_ready', {
      ...emptyState,
      hasMaterialSummary: true,
      hasOntologyExtractionDone: true,
    }, { isTerminal: false, kind: 'data' })).toBeNull()
  })

  it('技能阶段进度和技能定义收口必须等待业务资料分析完成', () => {
    for (const artifactType of ['skill_workorder_progress', 'skill_workorder_summary']) {
      const options = artifactType === 'skill_workorder_summary'
        ? { isTerminal: true, kind: 'data' as const, data: validSkillWorkorderSummaryData }
        : { isTerminal: false, kind: 'data' as const }

      expect(getBlockedIncomingArtifactReason(artifactType, {
        ...emptyState,
        hasMaterialSummary: true,
      }, options))
        .toBe('skill definition requires ontology_slice_extraction_done')

      expect(getBlockedIncomingArtifactReason(artifactType, {
        ...emptyState,
        hasMaterialSummary: true,
        hasOntologyExtractionDone: true,
      }, options))
        .toBeNull()
    }
  })

  it('匹配技能数据确认门必须在技能定义收口后出现', () => {
    expect(getBlockedIncomingArtifactReason('ontology_projection_ready', {
      ...emptyState,
      hasMaterialSummary: true,
    })).toBe('ontology projection requires skill_workorder_summary')
    expect(getBlockedIncomingArtifactReason('ontology_projection_ready', {
      ...emptyState,
      hasMaterialSummary: true,
      hasSkillSummary: true,
    }, { isTerminal: false, kind: 'data' })).toBeNull()
  })

  it('技能生成确认门必须等待可消费 projection', () => {
    expect(getBlockedIncomingArtifactReason('skill_generation_ready', {
      ...emptyState,
      hasSkillSummary: true,
      hasProjectionResult: true,
      canUseProjectionForSkillGeneration: false,
    })).toBe('skill generation confirmation requires consumable ontology projection')
    expect(getBlockedIncomingArtifactReason('skill_generation_ready', {
      ...emptyState,
      hasSkillSummary: true,
      hasProjectionResult: true,
      canUseProjectionForSkillGeneration: true,
    }, { isTerminal: false, kind: 'data' })).toBeNull()
  })

  it('阻止投影不可消费时进入技能生成', () => {
    expect(getBlockedIncomingArtifactReason('skill_generation_progress', {
      ...emptyState,
      hasSkillSummary: true,
      hasProjectionResult: true,
      canUseProjectionForSkillGeneration: false,
    })).toBe('skill generation requires consumable ontology projection')
  })

  it('阻止投影不可消费时展示资料采用进度', () => {
    expect(getBlockedIncomingArtifactReason('skill_projection_binding_ready', {
      ...emptyState,
      hasSkillSummary: true,
      hasProjectionResult: true,
      canUseProjectionForSkillGeneration: false,
    })).toBe('projection binding progress requires consumable ontology projection')
  })

  it('阻止不符合 schema 的打包进度 artifact', () => {
    expect(getBlockedIncomingArtifactReason('packaging_progress', emptyState, {
      isTerminal: false,
      kind: 'data',
      data: { action: 'start_packaging' },
    })).toBe('packaging_progress.status must be waiting_downstream or packing')
  })

  it('允许协议内的打包进度状态', () => {
    expect(getBlockedIncomingArtifactReason('packaging_progress', emptyState, {
      isTerminal: false,
      kind: 'data',
      data: { status: 'packing' },
    })).toBeNull()
  })

  it('blocks terminal material summary when source_path is missing from items', () => {
    expect(getBlockedIncomingArtifactReason('material_handoff_summary', {
      ...emptyState,
      hasMaterialSummary: true,
    }, {
      isTerminal: true,
      kind: 'data',
      data: {
        workspace_root: '/workspace/template-20260611090000',
        template_slug: 'template',
        items: [
          {
            title: 'SOP',
            source_hint: '用户上传：sop.docx',
            category: '业务流程',
            objective: '抽取流程',
            status: 'ready',
          },
        ],
      },
    })).toBe('material_handoff_summary.items[].source_path is required')
  })

  it('allows explicit null source_path for pure described material items', () => {
    expect(getBlockedIncomingArtifactReason('material_handoff_summary', {
      ...emptyState,
      hasMaterialSummary: true,
    }, {
      isTerminal: true,
      kind: 'data',
      data: {
        workspace_root: '/workspace/template-20260611090000',
        template_slug: 'template',
        items: [
          {
            title: '口头说明',
            source_hint: '用户描述',
            source_path: null,
            category: '其他',
            objective: '抽取边界',
            status: 'ready',
          },
        ],
      },
    })).toBeNull()
  })

  it('blocks skill summary when projection-pass contract fields are missing', () => {
    const readyState = {
      ...emptyState,
      hasMaterialSummary: true,
      hasOntologyExtractionDone: true,
    }

    expect(getBlockedIncomingArtifactReason('skill_workorder_summary', readyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        workspace_root: '/workspace/template-20260611090000',
        items: validSkillWorkorderSummaryData.items,
      },
    })).toBe('skill_workorder_summary.template_slug is required')

    expect(getBlockedIncomingArtifactReason('skill_workorder_summary', readyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        workspace_root: '/workspace/template-20260611090000',
        template_slug: 'template',
        items: [
          {
            name: 'order-risk-check',
            display_name: '订单风险检查',
            description: '检查订单风险并输出处置建议',
            trigger: '用户提交订单异常线索',
          },
        ],
      },
    })).toBe('skill_workorder_summary.items[].expected_output is required')

    expect(getBlockedIncomingArtifactReason('skill_workorder_summary', readyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        workspace_root: '/workspace/template-20260611090000',
        template_slug: 'template',
        items: [
          {
            name: 'order-risk-check',
            display_name: '订单风险检查',
            description: '检查订单风险并输出处置建议',
            trigger: '用户提交订单异常线索',
            expected_output: '输出风险等级和处置清单',
          },
        ],
      },
    })).toBe('skill_workorder_summary.items[].generation_action is required')
  })

  it('blocks placeholder workspace_root values', () => {
    expect(getBlockedIncomingArtifactReason('skill_workorder_summary', {
      ...emptyState,
      hasMaterialSummary: true,
      hasOntologyExtractionDone: true,
    }, {
      isTerminal: true,
      kind: 'data',
      data: {
        workspace_root: '/workspace',
        template_slug: 'template',
        items: [],
      },
    })).toBe('workspace_root must be a session workspace path')
  })

  it('blocks top-level status outside packaging and review artifacts', () => {
    expect(getBlockedIncomingArtifactReason('ontology_projection_done', {
      ...emptyState,
      hasSkillSummary: true,
    }, {
      isTerminal: true,
      kind: 'data',
      data: {
        status: 'done',
        projection_paths: ['ontology/projections/skill-a/contract.projection.json'],
      },
    })).toBe('data.status is only allowed for packaging and review artifacts')
  })

  it('blocks invalid review report status', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: { status: 'DONE' },
    })).toBe('review_report.status must be PASS, PASS_WITH_CONCERNS, or FAIL')
  })

  it('blocks review_report missing status', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        release_readiness: 'release-ready',
        score_average: 9,
        p0_blockers: [],
        p1_warnings: [],
        summary: 'All checks passed',
      },
    })).toBe('review_report.status must be PASS, PASS_WITH_CONCERNS, or FAIL')
  })

  it('blocks review_report missing summary', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        status: 'PASS',
        release_readiness: 'release-ready',
        score_average: 9,
        p0_blockers: [],
        p1_warnings: [],
      },
    })).toBe('review_report.summary must be a non-empty string')
  })

  it('blocks review_report with p0_blockers not array', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        status: 'PASS',
        release_readiness: 'release-ready',
        score_average: 9,
        p0_blockers: 'none',
        p1_warnings: [],
        summary: 'OK',
      },
    })).toBe('review_report.p0_blockers must be an array')
  })

  it('blocks review_report missing release_readiness', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        status: 'PASS',
        score_average: 9,
        p0_blockers: [],
        p1_warnings: [],
        summary: 'OK',
      },
    })).toBe('review_report.release_readiness must be a non-empty string')
  })

  it('allows review_report with valid PASS status', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        status: 'PASS',
        release_readiness: 'release-ready',
        score_average: 9,
        p0_blockers: [],
        p1_warnings: [],
        summary: 'All checks passed',
      },
    })).toBeNull()
  })

  it('allows review_report with valid PASS_WITH_CONCERNS status and all fields', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        status: 'PASS_WITH_CONCERNS',
        release_readiness: 'beta-ready',
        score_average: 7.5,
        p0_blockers: [],
        p1_warnings: ['skill.missing_metadata'],
        summary: 'One warning found',
      },
    })).toBeNull()
  })

  it('allows review_report with valid FAIL status', () => {
    expect(getBlockedIncomingArtifactReason('review_report', emptyState, {
      isTerminal: true,
      kind: 'data',
      data: {
        status: 'FAIL',
        release_readiness: 'not-production-ready',
        score_average: 3,
        p0_blockers: ['config.missing_soul'],
        p1_warnings: [],
        summary: 'Critical issues found',
      },
    })).toBeNull()
  })

  it('blocks ontology_slice_extraction_progress with top-level status', () => {
    expect(getBlockedIncomingArtifactReason('ontology_slice_extraction_progress', {
      ...emptyState,
      hasMaterialSummary: true,
    }, {
      isTerminal: false,
      kind: 'data',
      data: {
        total_sources: 2,
        completed_slices: 0,
        status: 'running',
      },
    })).toBe('data.status is only allowed for packaging and review artifacts')
  })

  it('allows ontology_slice_extraction_progress without status', () => {
    expect(getBlockedIncomingArtifactReason('ontology_slice_extraction_progress', {
      ...emptyState,
      hasMaterialSummary: true,
    }, {
      isTerminal: false,
      kind: 'data',
      data: {
        total_sources: 2,
        completed_slices: 0,
      },
    })).toBeNull()
  })

  it('blocks ontology_slice_extraction_done with top-level status', () => {
    expect(getBlockedIncomingArtifactReason('ontology_slice_extraction_done', {
      ...emptyState,
      hasMaterialSummary: true,
    }, {
      isTerminal: true,
      kind: 'data',
      data: {
        total_sources: 1,
        completed_slices: 1,
        slice_paths: ['ontology/sample.slice.json'],
        validation: 'PASS',
        status: 'done',
      },
    })).toBe('data.status is only allowed for packaging and review artifacts')
  })
})

describe('normalizeIncomingArtifactTerminal', () => {
  it('将误标为终态的资料进度 artifact 降级为非终态', () => {
    expect(normalizeIncomingArtifactTerminal('material_collection_progress', true)).toBe(false)
  })

  it('保留 terminal summary 的终态标记', () => {
    expect(normalizeIncomingArtifactTerminal('material_handoff_summary', true)).toBe(true)
  })

  it('缺少 isTerminal 的终端 artifact 自动升级为终态', () => {
    expect(normalizeIncomingArtifactTerminal('material_handoff_summary', false)).toBe(true)
    expect(normalizeIncomingArtifactTerminal('ontology_slice_extraction_done', false)).toBe(true)
    expect(normalizeIncomingArtifactTerminal('skill_generation_done', false)).toBe(true)
  })
})

describe('shouldDisplayArtifactInConversation', () => {
  it('不在聊天区展示非终态打包进度 artifact', () => {
    expect(shouldDisplayArtifactInConversation('packaging_progress', false)).toBe(false)
  })

  it('保留最终数字员工包文件 artifact', () => {
    expect(shouldDisplayArtifactInConversation('template_package', true)).toBe(true)
  })
})
