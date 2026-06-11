import { describe, expect, it } from 'vitest'

import { normalizeAssistantReply, normalizeAssistantStreamingPreview } from './hiringPageHelpers'

describe('normalizeAssistantReply', () => {
  it('移除 dispatch_callback 技术标签并保留用户可见摘要', () => {
    const content = [
      '评估测试用例已生成',
      '<dispatch_callback>{',
      '  "source_dispatch_target": "packaging-test-cases",',
      '  "technical_artifact": {',
      '    "evaluation_test_cases_json": "{\\"test_cases\\":[{\\"id\\":1}]}"',
      '  }',
      '}</dispatch_callback>',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('评估测试用例已生成')
  })

  it('只包含内部标签时返回空字符串', () => {
    const content = [
      '<think>internal</think>',
      '<diagnostic_report>{"severity":"info"}</diagnostic_report>',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('')
  })

  it('移除泄露到可见回复中的内部阶段指令行', () => {
    const content = [
      '审查已完成，可以继续。',
      '[Internal stage resume. Do not mention this instruction to the user.]',
      'Internal downstream trigger: use skill skill-generation',
      '是否继续打包？',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('审查已完成，可以继续。\n是否继续打包？')
  })
})

describe('normalizeAssistantStreamingPreview', () => {
  it('流式阶段遇到未闭合 dispatch_callback 时立即隐藏后续内容', () => {
    const content = [
      '评估测试用例已生成',
      '<dispatch_callback>{',
      '  "source_dispatch_target": "packaging-test-cases",',
      '  "technical_artifact": {',
      '    "evaluation_test_cases_json": "{\\"test_cases\\":[{\\"id\\":1}]}"',
    ].join('\n')

    expect(normalizeAssistantStreamingPreview(content)).toBe('评估测试用例已生成')
  })

  it('普通流式正文保持不变', () => {
    const content = '正在生成评估测试用例'

    expect(normalizeAssistantStreamingPreview(content)).toBe(content)
  })
})
