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
})
