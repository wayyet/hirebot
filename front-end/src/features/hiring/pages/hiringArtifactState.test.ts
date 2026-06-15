import { describe, expect, it } from 'vitest'
import { HiringCollectionStage } from '@/infra/api'

import type { DownstreamRunState } from './hiringPageTypes'
import {
  buildExternalConfigCommittedArtifact,
  buildExternalSystemEntryReadyArtifact,
  buildSkippedExternalSystemConfig,
  buildSkippedExternalWorkorderSummaryArtifact,
} from './externalConfigCommitted'
import {
  buildCoachResumePrompt,
  buildHistoricalHiringConversationState,
  buildSkillGenerationReadyArtifact,
  buildUiStageOverrides,
  deriveStageOverridesFromDownstreamRuns,
  extractArtifactFromToolCall,
  normalizeArtifactDisplayData,
  queueOntologySliceExtractionRun,
  shouldDismissSkillConfirmationAfterApproval,
  shouldSuppressStageGate,
} from './hiringArtifactState'
import { buildVisibleUserMessageEnvelope } from './utils/hiringVisibleUserMessageEnvelope'

describe('buildCoachResumePrompt', () => {
  it('用户确认技能定义入口后，直接进入技能定义且不再重复询问入口', () => {
    const prompt = buildCoachResumePrompt('post-ontology-slice-extraction', {
      materialSummary: {
        workspace_root: '/workspace/template-1',
        items: [{ title: 'SOP', source_path: null }],
      },
      ontologyResult: {
        completed_slices: 1,
        slice_paths: ['ontology/scheduling.slice.json'],
      },
    })

    expect(prompt).toContain('The user has already confirmed entering skill definition through `skill_definition_entry_ready`.')
    expect(prompt).toContain('Do not ask whether to enter skill definition again.')
    expect(prompt).toContain('Emit non-terminal `skill_workorder_progress`')
    expect(prompt).toContain('Never emit `stage1_material_done`')
    expect(prompt).toContain('emit non-terminal `skill_definition_ready`')
    expect(prompt).not.toContain('First give a short transition')
    expect(prompt).not.toContain('explicitly ask whether to enter skill definition now')
  })

  it('评估测试用例生成完成后，应回到打包审查门引导', () => {
    const prompt = buildCoachResumePrompt('post-packaging-test-cases', {
      packagingTestCasesResult: {
        generated_count: 4,
      },
    })

    expect(prompt).toContain('The optional evaluation test case generation has completed.')
    expect(prompt).toContain('review_readiness')
    expect(prompt).toContain('entry_skill')
    expect(prompt).toContain('manifest.skills')
    expect(prompt).toContain('manifest.ontology_slices')
    expect(prompt).toContain('do not emit `review_readiness`')
    expect(prompt).toContain('Do not emit review_progress or template_package before review_readiness')
    expect(prompt).toContain('Do not regenerate evaluation test cases in this turn.')
    expect(prompt).toContain('The testcase output contains 4 generated cases.')
  })

  it('projection 完成后的 resume prompt 不再重复发技能生成确认门', () => {
    const prompt = buildCoachResumePrompt('post-ontology-projection', {
      skillSummary: {
        workspace_root: '/workspace/template-1',
        items: [{ name: 'insert-order-feasibility' }],
      },
      projectionResult: {
        projected_count: 1,
        projection_paths: [
          'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
        ],
      },
    })

    expect(prompt).toContain('The system layer owns the `skill_generation_ready` confirmation gate')
    expect(prompt).toContain('do not emit or duplicate it')
    expect(prompt).toContain('ask the exact option-style question')
    expect(prompt).toContain('do not tell the user to rerun business-information preparation')
    expect(prompt).toContain('Do not trigger `skill-generation` in this turn')
  })

  it('系统层根据可消费 projection 构造 skill_generation_ready 确认门', () => {
    const artifact = buildSkillGenerationReadyArtifact({
      workspace_root: '/workspace/template-1',
      template_slug: 'template',
      items: [
        {
          name: '插单可行性评估与快速重排建议',
          skill_slug: 'scheduling_replan_advisor',
          display_name: '插单可行性评估与快速重排建议',
        },
      ],
    }, {
      projected_count: 1,
      projection_paths: [
        'ontology/projections/scheduling_replan_advisor/cosmetics-production-scheduling.workflow-contract.projection.json',
      ],
    })

    expect(artifact).toMatchObject({
      artifactType: 'skill_generation_ready',
      isTerminal: false,
      data: {
        status: 'waiting_confirm',
        workspace_root: '/workspace/template-1',
        template_slug: 'template',
        pending_skill_count: 1,
        skill_names: ['scheduling_replan_advisor'],
        projected_count: 1,
        projection_paths: [
          'ontology/projections/scheduling_replan_advisor/cosmetics-production-scheduling.workflow-contract.projection.json',
        ],
      },
    })
  })
})

describe('buildUiStageOverrides', () => {
  it('技能定义完成但技能生成未完成时，主技能阶段保持进行中', () => {
    const rawOverrides = new Map([
      [HiringCollectionStage.Material, 'completed' as const],
      [HiringCollectionStage.Skill, 'completed' as const],
      [HiringCollectionStage.External, 'running' as const],
    ])
    const skillGenerationState: DownstreamRunState = {
      key: 'skill-generation',
      status: 'waiting_confirm',
      artifactType: 'skill_generation_ready',
      updatedAt: new Date(0).toISOString(),
    }

    const overrides = buildUiStageOverrides(rawOverrides, null, skillGenerationState, true)

    expect(overrides.get(HiringCollectionStage.Material)).toBe('completed')
    expect(overrides.get(HiringCollectionStage.Skill)).toBe('running')
    expect(overrides.get(HiringCollectionStage.External)).toBeUndefined()
  })

  it('外部配置已提交或跳过时，外部阶段与前序阶段都标记完成', () => {
    const overrides = buildUiStageOverrides(new Map(), null, null, false, true)

    expect(overrides.get(HiringCollectionStage.Material)).toBe('completed')
    expect(overrides.get(HiringCollectionStage.Skill)).toBe('completed')
    expect(overrides.get(HiringCollectionStage.External)).toBe('completed')
  })
})

describe('extractArtifactFromToolCall', () => {
  it('从实时 emit_artifact 工具参数中解析 artifact', () => {
    const artifact = extractArtifactFromToolCall({
      toolName: 'streaming.emit_artifact',
      arguments: JSON.stringify({
        kind: 'data',
        artifactType: 'skill_generation_done',
        label: '技能实现已生成',
        skillName: 'skill-generation',
        stage: 'stage2_skill',
        isTerminal: true,
        data: { generated_count: 5 },
      }),
      result: 'ok',
    })

    expect(artifact).toMatchObject({
      kind: 'data',
      artifactType: 'skill_generation_done',
      label: '技能实现已生成',
      skillName: 'skill-generation',
      stage: 'stage2_skill',
      isTerminal: true,
      data: { generated_count: 5 },
    })
  })

  it('兼容 parameters 包装且工具名缺失的实时工具结果', () => {
    const artifact = extractArtifactFromToolCall({
      toolName: '',
      arguments: JSON.stringify({
        name: 'emit_artifact',
        parameters: {
          kind: 'data',
          artifactType: 'skill_workorder_progress',
          label: '准备开始技能定义',
          skillName: 'employment-coach-conversation',
          stage: 'stage2_skill',
          isTerminal: false,
          data: {
            baseline_skill_count: 5,
          },
        },
      }),
      result: 'ok',
    })

    expect(artifact).toMatchObject({
      kind: 'data',
      artifactType: 'skill_workorder_progress',
      label: '准备开始技能定义',
      skillName: 'employment-coach-conversation',
      stage: 'stage2_skill',
      isTerminal: false,
      data: { baseline_skill_count: 5 },
    })
  })
})

describe('normalizeArtifactDisplayData', () => {
  it('direct artifact event with terminal flag is parsed as terminal', () => {
    const artifact = normalizeArtifactDisplayData({
      kind: 'data',
      artifactType: 'skill_generation_done',
      label: 'Skill generation done',
      skillName: 'skill-generation',
      stage: 'stage2_skill',
      terminal: true,
      generated_count: 1,
    })

    expect(artifact).toMatchObject({
      kind: 'data',
      artifactType: 'skill_generation_done',
      isTerminal: true,
      data: { generated_count: 1 },
    })
  })

  it('资料已收口但业务资料仍在分析时，资料阶段保持进行中且技能阶段不启动', () => {
    const rawOverrides = new Map([
      [HiringCollectionStage.Material, 'completed' as const],
      [HiringCollectionStage.Skill, 'running' as const],
    ])
    const ontologyExtractionState: DownstreamRunState = {
      key: 'ontology-slice-extraction',
      status: 'running',
      artifactType: 'ontology_slice_extraction_progress',
      updatedAt: new Date(0).toISOString(),
    }

    const overrides = buildUiStageOverrides(rawOverrides, ontologyExtractionState, null, false)

    expect(overrides.get(HiringCollectionStage.Material)).toBe('running')
    expect(overrides.get(HiringCollectionStage.Skill)).toBeUndefined()
  })

  it('业务资料分析完成后，资料阶段标记完成且技能阶段自动进入进行中', () => {
    const ontologyExtractionState: DownstreamRunState = {
      key: 'ontology-slice-extraction',
      status: 'completed',
      artifactType: 'ontology_slice_extraction_done',
      updatedAt: new Date(0).toISOString(),
    }

    const overrides = buildUiStageOverrides(new Map(), ontologyExtractionState, null, false)

    expect(overrides.get(HiringCollectionStage.Material)).toBe('completed')
    expect(overrides.get(HiringCollectionStage.Skill)).toBe('running')
  })
})

describe('shouldSuppressStageGate', () => {
  it('业务资料分析完成前抑制资料到技能的阶段推进卡片', () => {
    expect(shouldSuppressStageGate({
      skillName: 'employment-coach-conversation',
      completedStage: 'stage1_material',
      nextStage: 'stage2_skill',
      canProceed: true,
    }, {
      'ontology-slice-extraction': {
        key: 'ontology-slice-extraction',
        status: 'running',
        artifactType: 'ontology_slice_extraction_progress',
        updatedAt: new Date(0).toISOString(),
      },
    })).toBe(true)
  })

  it('业务资料分析完成后允许资料到技能的阶段推进卡片', () => {
    expect(shouldSuppressStageGate({
      skillName: 'employment-coach-conversation',
      completedStage: 'stage1_material',
      nextStage: 'stage2_skill',
      canProceed: true,
    }, {
      'ontology-slice-extraction': {
        key: 'ontology-slice-extraction',
        status: 'completed',
        artifactType: 'ontology_slice_extraction_done',
        updatedAt: new Date(0).toISOString(),
      },
    })).toBe(false)
  })
})

describe('external system entry gate artifacts', () => {
  it('builds deterministic entry, skip summary, and skipped config artifacts', () => {
    const skillGenerationDone = {
      workspace_root: '/workspace/template-1',
      skill_slugs: ['insert-order-feasibility'],
    }

    const gate = buildExternalSystemEntryReadyArtifact(skillGenerationDone)
    const skipSummary = buildSkippedExternalWorkorderSummaryArtifact(skillGenerationDone)
    const skippedConfig = buildSkippedExternalSystemConfig('2026-06-15T00:00:00.000Z')
    const committed = buildExternalConfigCommittedArtifact(skippedConfig)

    expect(gate).toMatchObject({
      artifactType: 'external_system_entry_ready',
      isTerminal: false,
      data: {
        status: 'waiting_confirm',
        trigger_after: 'skill_generation_done',
        options: ['enter_external_system', 'skip_external_system'],
      },
    })
    expect(skipSummary).toMatchObject({
      artifactType: 'external_workorder_summary',
      isTerminal: true,
      data: {
        skip: true,
        submissionMode: 'skipped',
        total_capabilities: 0,
        external_capabilities: [],
        trigger_after: 'external_system_entry_ready',
      },
    })
    expect((skipSummary.data as { context_signature: string }).context_signature)
      .toBe((gate.data as { context_signature: string }).context_signature)
    expect(committed).toMatchObject({
      artifactType: 'external_config_committed',
      isTerminal: true,
      data: {
        submissionMode: 'skipped',
        updatedAtUtc: '2026-06-15T00:00:00.000Z',
        cliTools: [],
        mcpServer: null,
      },
    })
  })
})

describe('queueOntologySliceExtractionRun', () => {
  it('首次资料收口时启动业务资料分析运行轨道', () => {
    const materialSummary = {
      workspace_root: '/workspace/template-1',
      items: [{ title: 'SOP', source_path: null }],
    }

    const result = queueOntologySliceExtractionRun({}, materialSummary, '2026-06-15T10:00:00.000Z')

    expect(result.queued).toBe(true)
    expect(result.signature).toBe(JSON.stringify(materialSummary))
    expect(result.nextRuns['ontology-slice-extraction']).toMatchObject({
      key: 'ontology-slice-extraction',
      status: 'running',
      artifactType: 'ontology_slice_extraction_progress',
      data: materialSummary,
    })
  })

  it('业务资料分析已在执行时不重复启动', () => {
    const runningRun: DownstreamRunState = {
      key: 'ontology-slice-extraction',
      status: 'running',
      artifactType: 'ontology_slice_extraction_progress',
      updatedAt: new Date(0).toISOString(),
    }

    const result = queueOntologySliceExtractionRun({
      'ontology-slice-extraction': runningRun,
    }, { workspace_root: '/workspace/template-1' }, '2026-06-15T10:00:00.000Z')

    expect(result.queued).toBe(false)
    expect(result.nextRuns['ontology-slice-extraction']).toBe(runningRun)
  })

  it('业务资料分析已完成后允许新资料摘要重新启动', () => {
    const completedRun: DownstreamRunState = {
      key: 'ontology-slice-extraction',
      status: 'completed',
      artifactType: 'ontology_slice_extraction_done',
      updatedAt: new Date(0).toISOString(),
    }

    const result = queueOntologySliceExtractionRun({
      'ontology-slice-extraction': completedRun,
    }, { workspace_root: '/workspace/template-1' }, '2026-06-15T10:00:00.000Z')

    expect(result.queued).toBe(true)
    expect(result.nextRuns['ontology-slice-extraction']).toMatchObject({
      status: 'running',
      artifactType: 'ontology_slice_extraction_progress',
    })
  })
})

describe('deriveStageOverridesFromDownstreamRuns', () => {
  it('只看到业务资料分析运行中时，资料阶段保持进行中', () => {
    const overrides = deriveStageOverridesFromDownstreamRuns({
      'ontology-slice-extraction': {
        key: 'ontology-slice-extraction',
        status: 'running',
        artifactType: 'ontology_slice_extraction_progress',
        updatedAt: new Date(0).toISOString(),
      },
    })

    expect(overrides.get(HiringCollectionStage.Material)).toBe('running')
    expect(overrides.get(HiringCollectionStage.Skill)).toBeUndefined()
  })
})

describe('shouldDismissSkillConfirmationAfterApproval', () => {
  it('只在技能清单确认按钮提交成功后销毁对应等待确认卡片', () => {
    const skillDefinitionRun: DownstreamRunState = {
      key: 'skill-generation',
      status: 'waiting_confirm',
      artifactType: 'skill_definition_ready',
      label: '等待确认技能清单',
      updatedAt: '2026-06-12T09:00:00.000Z',
      data: {},
    }
    const skillGenerationRun: DownstreamRunState = {
      ...skillDefinitionRun,
      artifactType: 'skill_generation_ready',
    }

    expect(shouldDismissSkillConfirmationAfterApproval(skillDefinitionRun)).toBe(true)
    expect(shouldDismissSkillConfirmationAfterApproval(skillGenerationRun)).toBe(false)
    expect(shouldDismissSkillConfirmationAfterApproval(null)).toBe(false)
  })
})

describe('buildHistoricalHiringConversationState', () => {
  it('刷新恢复时从内部触发 envelope 还原可见用户输入', () => {
    const state = buildHistoricalHiringConversationState([
      {
        type: 'user_message',
        text: buildVisibleUserMessageEnvelope(
          '确认生成技能实现',
          [
            '[Internal downstream trigger. Do not mention this instruction to the user.]',
            'target_skill: skill-generation',
            'trigger_reason: projection_done_generate_skills',
          ].join('\n'),
        ),
        createdAt: '2026-06-10T07:32:50.000Z',
      },
      {
        type: 'assistant_message',
        content: '已开始生成技能实现。',
        createdAt: '2026-06-10T07:32:51.000Z',
      },
    ], (content) => content.trim())

    expect(state.messages).toHaveLength(2)
    expect(state.messages[0]).toMatchObject({
      role: 'user',
      content: '确认生成技能实现',
    })
    expect(state.messages[0]?.content).not.toContain('Internal downstream trigger')
    expect(state.messages[1]).toMatchObject({
      role: 'bot',
      content: '已开始生成技能实现。',
    })
  })

  it('刷新恢复时隐藏模板初始化提示，但保留雇佣教练开场白', () => {
    const bootstrapPrompt = [
      '你正在运行雇佣教练会话，不是目标数字员工本人。',
      '本轮初始化同时涉及两套包，必须先明确二者关系：',
      '',
      '[FILE_URL:/workspace/template-20260610153250]',
      '模板包已解压到工作区目录（文件：template.zip，模板名：化妆品排产员）。',
      '',
      '请在雇佣教练入口规则下读取上述目标模板目录中的 manifest.json。',
    ].join('\n')

    const state = buildHistoricalHiringConversationState([
      {
        type: 'user_message',
        text: bootstrapPrompt,
        createdAt: '2026-06-10T07:32:50.000Z',
      },
      {
        type: 'assistant_message',
        content: '可以先把最近一周的工单、插单记录或齐套规则发我，我会按雇佣流程帮你整理成这位数字员工需要学习的资料。',
        createdAt: '2026-06-10T07:32:51.000Z',
      },
    ], (content) => content.trim())

    expect(state.messages).toHaveLength(1)
    expect(state.messages[0]).toMatchObject({
      role: 'bot',
      content: '可以先把最近一周的工单、插单记录或齐套规则发我，我会按雇佣流程帮你整理成这位数字员工需要学习的资料。',
    })
  })

  it('刷新恢复时丢弃契约外 artifact', () => {
    const state = buildHistoricalHiringConversationState([
      {
        type: 'assistant_message',
        content: '',
        createdAt: '2026-06-10T07:32:51.000Z',
        toolCalls: [
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_generation_trigger',
              label: '开始生成技能',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: false,
              data: { action: 'start' },
            }),
            result: 'ok',
          },
        ],
      },
    ], (content) => content.trim())

    expect(state.messages).toHaveLength(0)
    expect(state.downstreamRuns['skill-generation']).toBeUndefined()
  })

  it('刷新恢复时资料收口后仍等待业务资料分析完成，不提前进入技能阶段', () => {
    const state = buildHistoricalHiringConversationState([
      {
        type: 'assistant_message',
        content: '',
        createdAt: '2026-06-10T07:32:51.000Z',
        toolCalls: [
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'material_handoff_summary',
              label: '资料已收口',
              skillName: 'employment-coach-conversation',
              stage: 'stage1_material',
              isTerminal: true,
              data: {
                workspace_root: '/workspace/template-1',
                template_slug: 'template-1',
                items: [{ title: 'SOP', source_path: null }],
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'ontology_slice_extraction_progress',
              label: '正在分析业务资料',
              skillName: 'ontology-slice-extraction',
              stage: 'stage1_material',
              isTerminal: false,
              data: { total_sources: 1, completed_slices: 0 },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_workorder_progress',
              label: '错误提前进入技能阶段',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: false,
              data: { pending_skill_count: 1 },
            }),
            result: 'ok',
          },
        ],
      },
    ], (content) => content.trim())

    expect(state.wsStageOverrides.get(HiringCollectionStage.Material)).toBe('running')
    expect(state.wsStageOverrides.get(HiringCollectionStage.Skill)).toBeUndefined()
    expect(state.messages.some(message => message.artifact?.artifactType === 'skill_workorder_progress')).toBe(false)
  })

  it('刷新恢复时阻止只有 slice_paths 的投影结果启动技能生成状态', () => {
    const state = buildHistoricalHiringConversationState([
      {
        type: 'assistant_message',
        content: '',
        createdAt: '2026-06-10T07:32:51.000Z',
        toolCalls: [
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_workorder_summary',
              label: '技能定义已确认',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: true,
              data: {
                workspace_root: '/workspace/template-1',
                template_slug: 'template-1',
                items: [
                  {
                    name: 'insert-order-feasibility',
                    display_name: '插单可行性评估',
                    description: '判断插单请求是否满足产能和物料约束',
                    trigger: '用户提交插单请求',
                    expected_output: '输出可行性结论和风险说明',
                    generation_action: 'generate_new',
                  },
                ],
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'ontology_projection_done',
              label: '匹配技能数据结果',
              skillName: 'ontology-slice-extraction',
              stage: 'stage2_skill',
              isTerminal: true,
              data: {
                projected_count: 1,
                slice_paths: ['ontology/scheduling-and-insertion-evaluation.slice.json'],
                status: 'done',
                validation: 'NOT_RUN',
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_generation_progress',
              label: '开始生成技能',
              skillName: 'skill-generation',
              stage: 'stage2_skill',
              isTerminal: false,
              data: { generated_count: 0 },
            }),
            result: 'ok',
          },
        ],
      },
    ], (content) => content.trim())

    expect(state.messages.some(message => message.artifact?.artifactType === 'skill_generation_progress')).toBe(false)
    expect(state.downstreamRuns['skill-generation']).toBeUndefined()
  })

  it('刷新恢复时保留三个技能阶段确认门的等待状态', () => {
    const state = buildHistoricalHiringConversationState([
      {
        type: 'assistant_message',
        content: '',
        createdAt: '2026-06-10T07:32:51.000Z',
        toolCalls: [
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'material_handoff_summary',
              label: '资料已确认',
              skillName: 'employment-coach-conversation',
              stage: 'stage1_material',
              isTerminal: true,
              data: {
                workspace_root: '/workspace/template-1',
                template_slug: 'template-1',
                items: [{ title: 'SOP', source_path: null }],
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'ontology_slice_extraction_done',
              label: '业务资料分析完成',
              skillName: 'ontology-slice-extraction',
              stage: 'stage1_material',
              isTerminal: true,
              data: {
                total_sources: 1,
                completed_slices: 1,
                slice_paths: ['ontology/scheduling.slice.json'],
                validation: 'PASS',
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_definition_ready',
              label: '等待确认技能清单',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: false,
              data: { pending_skill_count: 1, skill_names: ['insert-order-feasibility'] },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_workorder_summary',
              label: '技能定义已确认',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: true,
              data: {
                workspace_root: '/workspace/template-1',
                template_slug: 'template-1',
                items: [
                  {
                    name: 'insert-order-feasibility',
                    display_name: '插单可行性评估',
                    description: '判断插单请求是否满足产能和物料约束',
                    trigger: '用户提交插单请求',
                    expected_output: '输出可行性结论和风险说明',
                    generation_action: 'generate_new',
                  },
                ],
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'ontology_projection_ready',
              label: '等待确认匹配技能数据',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: false,
              data: { pending_skill_count: 1, skill_names: ['insert-order-feasibility'] },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'ontology_projection_done',
              label: '技能数据已匹配',
              skillName: 'ontology-projection',
              stage: 'stage2_skill',
              isTerminal: true,
              data: {
                projected_count: 1,
                projection_paths: [
                  'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
                ],
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_generation_ready',
              label: '等待确认生成技能',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: false,
              data: {
                pending_skill_count: 1,
                projected_count: 1,
                projection_paths: [
                  'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
                ],
              },
            }),
            result: 'ok',
          },
        ],
      },
    ], (content) => content.trim())

    expect(state.downstreamRuns['ontology-projection']).toMatchObject({
      status: 'completed',
      artifactType: 'ontology_projection_done',
    })
    expect(state.downstreamRuns['skill-generation']).toMatchObject({
      status: 'waiting_confirm',
      artifactType: 'skill_generation_ready',
    })
  })

  it('deduplicates repeated packaging_testcases_ready artifacts during restore', () => {
    const readyArtifact = {
      kind: 'data',
      artifactType: 'packaging_testcases_ready',
      label: 'Await Test Case Generation Approval',
      skillName: 'employment-coach-conversation',
      stage: 'stage4_packaging',
      isTerminal: false,
      displayHint: 'badge',
      data: {
        workspace_root: '/workspace/template-20260611090000',
        template_slug: 'template',
        next_step: '等待用户确认是否生成评估测试用例',
      },
    }
    const state = buildHistoricalHiringConversationState([
      {
        type: 'assistant_message',
        content: '',
        createdAt: '2026-06-10T07:32:51.000Z',
        toolCalls: [
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'external_config_committed',
              label: 'External Config Committed',
              skillName: 'external-config',
              stage: 'stage3_external',
              isTerminal: true,
              data: { submission_mode: 'skipped' },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify(readyArtifact),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              ...readyArtifact,
              data: {
                ...readyArtifact.data,
                emitted_at: '2026-06-10T07:32:52.000Z',
              },
            }),
            result: 'ok',
          },
        ],
      },
    ], (content) => content.trim())

    expect(state.messages.filter(message => message.artifact?.artifactType === 'packaging_testcases_ready')).toHaveLength(1)
    expect(state.downstreamRuns['packaging-test-cases']?.artifactType).toBe('packaging_testcases_ready')
  })

  it('deduplicates repeated skill_definition_entry_ready artifacts by context signature during restore', () => {
    const readyArtifact = {
      kind: 'data',
      artifactType: 'skill_definition_entry_ready',
      label: '等待确认是否进入技能定义',
      skillName: 'employment-coach-conversation',
      stage: 'stage2_skill',
      isTerminal: false,
      displayHint: 'badge',
      data: {
        context_signature: 'material-1:ontology-1',
        status: 'waiting_confirm',
        message: '业务资料分析完成，请确认是否进入技能定义。',
      },
    }
    const state = buildHistoricalHiringConversationState([
      {
        type: 'assistant_message',
        content: '',
        createdAt: '2026-06-10T07:32:51.000Z',
        toolCalls: [
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'material_handoff_summary',
              label: '资料已确认',
              skillName: 'employment-coach-conversation',
              stage: 'stage1_material',
              isTerminal: true,
              data: {
                workspace_root: '/workspace/template-1',
                template_slug: 'template-1',
                items: [{ title: 'SOP', source_path: null }],
              },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'ontology_slice_extraction_done',
              label: '业务资料分析完成',
              skillName: 'ontology-slice-extraction',
              stage: 'stage1_material',
              isTerminal: true,
              data: { completed_slices: 1 },
            }),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify(readyArtifact),
            result: 'ok',
          },
          {
            toolName: 'streaming.emit_artifact',
            arguments: JSON.stringify({
              ...readyArtifact,
              data: {
                ...readyArtifact.data,
                emitted_at: '2026-06-10T07:33:01.000Z',
              },
            }),
            result: 'ok',
          },
        ],
      },
    ], (content) => content.trim())

    expect(state.messages.filter(message => message.artifact?.artifactType === 'skill_definition_entry_ready')).toHaveLength(1)
    expect(state.downstreamRuns['skill-definition-entry']).toMatchObject({
      status: 'waiting_confirm',
      artifactType: 'skill_definition_entry_ready',
    })
  })
})
