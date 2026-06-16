import { describe, expect, it } from 'vitest'

import { normalizeAssistantReply, normalizeAssistantStreamingPreview } from './hiringPageHelpers'

describe('tool-ban diagnostic cleanup', () => {
  it('hides a reply that only contains a raw tool-ban diagnostic', () => {
    const content = '[TOOL BAN] Refused to call session_search: matches banned pattern *session*'

    expect(normalizeAssistantReply(content)).toBe('')
  })

  it('removes leaked tool-ban diagnostic lines while keeping business text', () => {
    const content = [
      '技能数据已匹配完成。',
      '[TOOL BAN] Refused to call session_search: matches banned pattern *session*',
      '是否开始生成技能实现？',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('技能数据已匹配完成。\n是否开始生成技能实现？')
  })

  it('hides raw tool-ban diagnostics during streaming preview', () => {
    const content = '[TOOL BAN] Refused to call session_search: matches banned pattern *session*'

    expect(normalizeAssistantStreamingPreview(content)).toBe('')
  })

  it('removes leaked artifact protocol diagnostics', () => {
    const content = [
      '这不是这个阶段该发的 artifact（skill_generation_trigger 不在允许清单里）；我会改为按流程通过下游生成步骤产出技能实现。',
      '技能实现生成已启动。',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('技能实现生成已启动。')
    expect(normalizeAssistantStreamingPreview(content)).toBe('技能实现生成已启动。')
  })
})
