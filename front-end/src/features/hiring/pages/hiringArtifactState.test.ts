import { describe, expect, it } from 'vitest'
import { buildCoachResumePrompt } from './hiringArtifactState'

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
