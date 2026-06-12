import { describe, expect, it } from 'vitest'

import {
  buildDownstreamPrompt,
  buildPackageReviewPrompt,
  buildPackageReviewSkipPackagingPrompt,
  buildPackagingRequestPrompt,
  buildProjectionPassPayload,
  buildSkillDefinitionConfirmationPrompt,
  buildSkillGenerationPayload,
  isPackageReviewApprovalMessage,
  isPackageReviewSkipMessage,
  isSkillDefinitionApprovalMessage,
  isOntologyProjectionApprovalMessage,
  isSkillGenerationApprovalMessage,
  isPackagingRequestMessage,
  isPackagingTestCasesApprovalMessage,
  isPackagingTestCasesSkipMessage,
  resolveActiveSkillStageRun,
  resolvePackageReviewDecisionRoute,
  resolvePackagingRequestRoute,
  resolveSkillStageApprovalRoute,
} from './hiringDownstreamTriggers'
import type { DownstreamRunState } from '../hiringPageTypes'

describe('buildProjectionPassPayload', () => {
  it('从有效技能定义摘要构造匹配技能数据 payload', () => {
    const payload = buildProjectionPassPayload({
      workspace_root: '/workspace/template-20260611090000',
      template_slug: 'template',
      items: [
        {
          name: 'order-risk-check',
          display_name: '订单风险检查',
          description: '检查订单风险并输出处置建议',
          trigger: '用户提交订单异常线索',
          expected_output: '输出风险等级和处置清单',
          generation_action: 'generate_new',
        },
      ],
    })

    expect(payload).toEqual({
      trigger_mode: 'projection_pass',
      workspace_root: '/workspace/template-20260611090000',
      template_slug: 'template',
      skills: [
        {
          skill_slug: 'order-risk-check',
          skill_name: '订单风险检查',
          triggers: ['用户提交订单异常线索'],
          description: '检查订单风险并输出处置建议',
          expected_output: '输出风险等级和处置清单',
          generation_action: 'generate_new',
        },
      ],
    })
  })

  it('缺少 workspace_root 时不构造匹配技能数据 payload', () => {
    expect(buildProjectionPassPayload({
      template_slug: 'template',
      items: [
        {
          name: 'order-risk-check',
          display_name: '订单风险检查',
          description: '检查订单风险并输出处置建议',
          trigger: '用户提交订单异常线索',
          expected_output: '输出风险等级和处置清单',
          generation_action: 'generate_new',
        },
      ],
    })).toBeNull()
  })
})

describe('buildSkillGenerationPayload', () => {
  it('projection 目录 slug 与已确认技能 slug 不一致时不启动技能生成', () => {
    const payload = buildSkillGenerationPayload({
      workspace_root: '/workspace/template-1',
      items: [
        { name: 'order-insertion-feasibility', display_name: '插单可行性评估' },
      ],
    }, {
      projected_count: 1,
      projection_paths: [
        'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
      ],
    })

    expect(payload).toBeNull()
  })

  it('projection 目录 slug 属于已确认技能时附带稳定 slug 清单', () => {
    const payload = buildSkillGenerationPayload({
      workspace_root: '/workspace/template-1',
      items: [
        { name: 'insert-order-feasibility', display_name: '插单可行性评估' },
      ],
    }, {
      projected_count: 1,
      projection_paths: [
        'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
      ],
    })

    expect(payload).toMatchObject({
      confirmed_skill_slugs: ['insert-order-feasibility'],
      projection_skill_slugs: ['insert-order-feasibility'],
      projection_binding_confirmed: true,
      projection_contract_mode: 'required',
    })
  })

  it('缺少 projected_count 但有 projection_paths 时仍可兼容启动', () => {
    const payload = buildSkillGenerationPayload({
      workspace_root: '/workspace/template-1',
      items: [
        { name: 'insert-order-feasibility', display_name: '插单可行性评估' },
      ],
    }, {
      projection_paths: [
        'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
      ],
    })

    expect(payload).toMatchObject({
      confirmed_skill_slugs: ['insert-order-feasibility'],
      projection_skill_slugs: ['insert-order-feasibility'],
    })
  })

  it('只有 slice_paths 时不启动技能生成', () => {
    const payload = buildSkillGenerationPayload({
      workspace_root: '/workspace/template-1',
      items: [
        { name: 'insert-order-feasibility', display_name: '插单可行性评估' },
      ],
    }, {
      projected_count: 1,
      slice_paths: [
        'ontology/scheduling-and-insertion-evaluation.slice.json',
      ],
      validation: 'NOT_RUN',
    })

    expect(payload).toBeNull()
  })
})

describe('isSkillGenerationApprovalMessage', () => {
  it('识别采用并继续和继续类确认', () => {
    expect(isSkillGenerationApprovalMessage('采用并继续')).toBe(true)
    expect(isSkillGenerationApprovalMessage('继续')).toBe(true)
    expect(isSkillGenerationApprovalMessage('开始生成')).toBe(true)
  })
})

describe('skill stage approval messages', () => {
  it('分别识别技能定义确认和匹配技能数据确认', () => {
    expect(isSkillDefinitionApprovalMessage('确认技能清单')).toBe(true)
    expect(isSkillDefinitionApprovalMessage('没问题，继续')).toBe(true)
    expect(isOntologyProjectionApprovalMessage('开始匹配技能数据')).toBe(true)
    expect(isOntologyProjectionApprovalMessage('继续匹配数据')).toBe(true)
  })
})

describe('resolveSkillStageApprovalRoute', () => {
  const skillDefinitionReady: DownstreamRunState = {
    key: 'skill-generation',
    status: 'waiting_confirm',
    artifactType: 'skill_definition_ready',
    updatedAt: '2026-06-11T00:00:00.000Z',
  }
  const projectionReady: DownstreamRunState = {
    key: 'ontology-projection',
    status: 'waiting_confirm',
    artifactType: 'ontology_projection_ready',
    updatedAt: '2026-06-11T00:00:01.000Z',
  }
  const skillGenerationReady: DownstreamRunState = {
    key: 'skill-generation',
    status: 'waiting_confirm',
    artifactType: 'skill_generation_ready',
    updatedAt: '2026-06-11T00:00:02.000Z',
  }

  it('projection 确认门优先于残留的技能定义确认门', () => {
    expect(resolveSkillStageApprovalRoute({
      text: '继续',
      incomingFileCount: 0,
      skillGenerationState: skillDefinitionReady,
      ontologyProjectionState: projectionReady,
      hasSkillSummary: true,
      hasProjectionResult: false,
    })).toBe('launch_projection_pass')
  })

  it('技能生成确认门优先于残留的匹配技能数据确认门', () => {
    expect(resolveSkillStageApprovalRoute({
      text: '继续',
      incomingFileCount: 0,
      skillGenerationState: skillGenerationReady,
      ontologyProjectionState: projectionReady,
      hasSkillSummary: true,
      hasProjectionResult: true,
    })).toBe('launch_skill_generation')
  })

  it('右侧技能卡展示当前有效确认门，不被旧匹配技能数据门覆盖', () => {
    expect(resolveActiveSkillStageRun(skillGenerationReady, projectionReady)?.artifactType)
      .toBe('skill_generation_ready')
    expect(resolveActiveSkillStageRun(skillDefinitionReady, projectionReady)?.artifactType)
      .toBe('ontology_projection_ready')
  })
})

describe('isPackagingTestCasesSkipMessage', () => {
  it('把生成实例包请求识别为跳过测试用例并直接打包', () => {
    expect(isPackagingTestCasesSkipMessage('三个阶段均已确认完成，请生成实例包并打成 ZIP')).toBe(true)
    expect(isPackagingTestCasesSkipMessage('All three stages are confirmed. Please generate the instance package as a ZIP.')).toBe(true)
  })
})

describe('isPackagingTestCasesApprovalMessage', () => {
  it('treats short generation approvals as testcase confirmation only when exact', () => {
    expect(isPackagingTestCasesApprovalMessage('生成')).toBe(true)
    expect(isPackagingTestCasesApprovalMessage('开始生成')).toBe(true)
    expect(isPackagingTestCasesApprovalMessage('生成实例包')).toBe(false)
    expect(isPackagingTestCasesApprovalMessage('生成数字员工包')).toBe(false)
  })
})

describe('isPackagingRequestMessage', () => {
  it('识别生成数字员工和继续类打包请求', () => {
    expect(isPackagingRequestMessage('三个阶段均已确认完成，请开始生成数字员工')).toBe(true)
    expect(isPackagingRequestMessage('生成实例包')).toBe(true)
    expect(isPackagingRequestMessage('直接打包')).toBe(true)
    expect(isPackagingRequestMessage('继续')).toBe(true)
    expect(isPackagingRequestMessage('Please generate the instance package.')).toBe(true)
  })
})

describe('resolvePackagingRequestRoute', () => {
  const baseInput = {
    text: 'continue packaging',
    incomingFileCount: 0,
    isBlockedByRequiredConfirmation: false,
    isBlockedByPackagingTestCaseGeneration: false,
    hasPendingPackageReviewDecision: false,
    hasPendingPackageArtifact: false,
    packagingInProgress: false,
    hasReviewReport: false,
    hasPackagingContext: false,
    hasCompletedCoreSummaries: false,
  }

  it('routes review_report + continue packaging to the R6 packaging prompt', () => {
    expect(resolvePackagingRequestRoute({
      ...baseInput,
      hasReviewReport: true,
    })).toBe('launch_packaging_request')
  })

  it('prefers importing an existing package over starting another packaging run', () => {
    expect(resolvePackagingRequestRoute({
      ...baseInput,
      hasPendingPackageArtifact: true,
      hasReviewReport: true,
    })).toBe('import_existing_package')
  })

  it('does not steal confirmation messages from required skill gates', () => {
    expect(resolvePackagingRequestRoute({
      ...baseInput,
      hasReviewReport: true,
      isBlockedByRequiredConfirmation: true,
    })).toBe('none')
  })

  it('does not steal messages while package review decision is pending', () => {
    expect(resolvePackagingRequestRoute({
      ...baseInput,
      hasPendingPackageReviewDecision: true,
      hasPackagingContext: true,
    })).toBe('none')
  })
})

describe('buildPackagingRequestPrompt', () => {
  it('首次打包请求只推进到 review_readiness 并停止', () => {
    const prompt = buildPackagingRequestPrompt('继续打包')

    expect(prompt).toContain('review decision gate')
    expect(prompt).toContain('review_readiness')
    expect(prompt).toContain('Stop immediately after `review_readiness`')
    expect(prompt).toContain('Do not emit `review_progress`')
    expect(prompt).toContain('do not emit `template_package`')
  })

  it('review_report 已存在时要求直接进入 R6 打包', () => {
    const prompt = buildPackagingRequestPrompt('继续打包', {
      status: 'PASS_WITH_CONCERNS',
      p1_warnings: ['manifest.entry_skill.missing'],
    })

    expect(prompt).toContain('A `review_report` already exists')
    expect(prompt).toContain('Do not rerun review')
    expect(prompt).toContain('package the current employee package workspace now')
    expect(prompt).toContain('packaging_progress')
    expect(prompt).toContain('review_risk_summary')
    expect(prompt).toContain('workspace_root')
    expect(prompt).toContain('coach_runtime_root')
    expect(prompt).toContain('employee_package_root')
    expect(prompt).toContain('must never be packaged')
    expect(prompt).toContain('not `/workspace`')
    expect(prompt).toContain('skills/employment-coach-conversation/')
    expect(prompt).toContain('stop and report the concrete root-resolution problem')
    expect(prompt).toContain('use a zip tool to package that directory')
    expect(prompt).toContain('template_package')
  })
})

describe('package review decision routing', () => {
  const baseInput = {
    text: '审查',
    incomingFileCount: 0,
    hasPendingPackageReviewDecision: true,
    isBlockedByRequiredConfirmation: false,
    isBlockedByPackagingTestCaseGeneration: false,
  }

  it('识别审查确认和跳过审查', () => {
    expect(isPackageReviewApprovalMessage('审查')).toBe(true)
    expect(isPackageReviewApprovalMessage('开始审查')).toBe(true)
    expect(isPackageReviewApprovalMessage('跳过审查，直接打包')).toBe(false)
    expect(isPackageReviewSkipMessage('跳过审查，直接打包')).toBe(true)
    expect(isPackageReviewSkipMessage('生成并打包')).toBe(true)
  })

  it('review_readiness 后用户确认审查时触发审查分支', () => {
    expect(resolvePackageReviewDecisionRoute(baseInput)).toBe('launch_package_review')
  })

  it('review_readiness 后用户要求打包时触发跳过审查打包分支', () => {
    expect(resolvePackageReviewDecisionRoute({
      ...baseInput,
      text: '生成并打包',
    })).toBe('skip_review_and_package')
  })

  it('没有待确认审查门时不消费用户消息', () => {
    expect(resolvePackageReviewDecisionRoute({
      ...baseInput,
      hasPendingPackageReviewDecision: false,
    })).toBe('none')
  })

  it('审查触发提示只运行 review，不打包', () => {
    const prompt = buildPackageReviewPrompt('审查')

    expect(prompt).toContain('review_progress')
    expect(prompt).toContain('digital-employee-package-completeness-review')
    expect(prompt).toContain('review_report')
    expect(prompt).toContain('Do not invoke package/export/archive tools')
    expect(prompt).toContain('do not emit `template_package`')
  })

  it('跳过审查提示直接打包且禁止 review_progress', () => {
    const prompt = buildPackageReviewSkipPackagingPrompt('跳过审查，直接打包')

    expect(prompt).toContain('explicitly skipped package completeness review')
    expect(prompt).toContain('Do not run `digital-employee-package-completeness-review`')
    expect(prompt).toContain('do not emit `review_progress`')
    expect(prompt).toContain('packaging_progress')
    expect(prompt).toContain('template_package')
  })
})

describe('buildSkillDefinitionConfirmationPrompt', () => {
  it('确认技能定义后只要求收口 summary 和匹配技能数据确认门', () => {
    const prompt = buildSkillDefinitionConfirmationPrompt('确认技能清单', {
      items: [{ name: 'insert-order-feasibility' }],
    })

    expect(prompt).toContain('skill_workorder_summary')
    expect(prompt).toContain('ontology_projection_ready')
    expect(prompt).toContain('workspace_root')
    expect(prompt).toContain('template_slug')
    expect(prompt).toContain('items[]')
    expect(prompt).toContain('expected_output')
    expect(prompt).toContain('generation_action')
    expect(prompt).toContain('do not emit `skill_workorder_summary`')
    expect(prompt).toContain('Do not trigger ontology projection')
    expect(prompt).not.toContain('skill_generation_progress')
  })
})

describe('buildDownstreamPrompt', () => {
  const samplePayload = { workspace_root: '/workspace/template-1' }

  it('ontology-slice-extraction prompt 包含 use skill ontology-slice-extraction 触发块', () => {
    const prompt = buildDownstreamPrompt('ontology-slice-extraction', samplePayload)

    expect(prompt).toContain('use skill ontology-slice-extraction')
    expect(prompt).toContain('Switch to skill `ontology-slice-extraction` now.')
    expect(prompt).toContain('[Internal downstream trigger: use skill ontology-slice-extraction]')
    expect(prompt).toContain('trigger_reason: material_handoff_summary_completed')
    expect(prompt).toContain('stage=`stage1_material`')
    expect(prompt).toContain('artifact_payload:')
    expect(prompt).toContain('required_artifacts:')
    expect(prompt).toContain('ontology_slice_extraction_progress')
    expect(prompt).toContain('ontology_slice_extraction_done')
  })

  it('ontology-projection prompt 包含 use skill ontology-projection 触发块', () => {
    const prompt = buildDownstreamPrompt('ontology-projection', samplePayload)

    expect(prompt).toContain('use skill ontology-projection')
    expect(prompt).toContain('Switch to skill `ontology-projection` now.')
    expect(prompt).toContain('[Internal downstream trigger: use skill ontology-projection]')
    expect(prompt).toContain('trigger_reason: user_confirmed_ontology_projection')
    expect(prompt).toContain('stage=`stage2_skill`')
    expect(prompt).toContain('artifact_payload:')
    expect(prompt).toContain('required_artifacts:')
    expect(prompt).toContain('ontology_projection_progress')
    expect(prompt).toContain('ontology_projection_done')
    expect(prompt).toContain('Scan slices from `<workspace_root>/ontology/`')
  })

  it('skill-generation prompt 包含 use skill skill-generation 触发块', () => {
    const prompt = buildDownstreamPrompt('skill-generation', samplePayload)

    expect(prompt).toContain('use skill skill-generation')
    expect(prompt).toContain('Switch to skill `skill-generation` now.')
    expect(prompt).toContain('[Internal downstream trigger: use skill skill-generation]')
    expect(prompt).toContain('stage=`stage2_skill`')
    expect(prompt).toContain('artifact_payload:')
    expect(prompt).toContain('required_artifacts:')
    expect(prompt).toContain('skill_generation_progress')
    expect(prompt).toContain('skill_generation_done')
  })

  it('packaging-test-cases prompt 包含 use skill packaging-test-cases 触发块', () => {
    const prompt = buildDownstreamPrompt('packaging-test-cases', samplePayload)

    expect(prompt).toContain('use skill packaging-test-cases')
    expect(prompt).toContain('Switch to skill `packaging-test-cases` now.')
    expect(prompt).toContain('[Internal downstream trigger: use skill packaging-test-cases]')
    expect(prompt).toContain('stage=`stage4_packaging`')
    expect(prompt).toContain('artifact_payload:')
    expect(prompt).toContain('required_artifacts:')
    expect(prompt).toContain('packaging_testcases_progress')
    expect(prompt).toContain('packaging_testcases_done')
  })
})
