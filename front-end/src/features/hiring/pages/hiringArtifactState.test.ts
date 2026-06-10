import { describe, expect, it } from 'vitest'
import { HiringCollectionStage } from '@/infra/api'

import type { DownstreamRunState } from './hiringPageTypes'
import { buildCoachResumePrompt, buildUiStageOverrides } from './hiringArtifactState'

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
