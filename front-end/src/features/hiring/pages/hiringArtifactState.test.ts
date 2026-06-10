import { describe, expect, it } from 'vitest'
import { HiringCollectionStage } from '@/infra/api'

import type { DownstreamRunState } from './hiringPageTypes'
import {
  buildCoachResumePrompt,
  buildHistoricalHiringConversationState,
  buildUiStageOverrides,
  extractArtifactFromToolCall,
} from './hiringArtifactState'

describe('buildCoachResumePrompt', () => {
  it('评估测试用例生成完成后，应回到打包确认引导', () => {
    const prompt = buildCoachResumePrompt('post-packaging-test-cases', {
      packagingTestCasesResult: {
        generated_count: 4,
      },
    })

    expect(prompt).toContain('The optional evaluation test case generation has completed.')
    expect(prompt).toContain('explicitly ask whether to generate the instance package now')
    expect(prompt).toContain('Do not regenerate evaluation test cases in this turn.')
    expect(prompt).toContain('The testcase output contains 4 generated cases.')
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

    const overrides = buildUiStageOverrides(rawOverrides, skillGenerationState, true)

    expect(overrides.get(HiringCollectionStage.Material)).toBe('completed')
    expect(overrides.get(HiringCollectionStage.Skill)).toBe('running')
    expect(overrides.get(HiringCollectionStage.External)).toBeUndefined()
  })

  it('外部配置已提交或跳过时，外部阶段与前序阶段都标记完成', () => {
    const overrides = buildUiStageOverrides(new Map(), null, false, true)

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

describe('buildHistoricalHiringConversationState', () => {
  it('刷新恢复时隐藏模板初始化提示，但保留雇佣教练开场白', () => {
    const bootstrapPrompt = [
      '你正在运行 HireBot 雇佣教练会话，不是目标数字员工本人。',
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
})
