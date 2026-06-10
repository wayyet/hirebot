import { describe, expect, it } from 'vitest'

import type { ChatMessage, ToolStep } from '../hiringPageTypes'
import { chatToMarkdown } from './hiringConversationMarkdown'

describe('chatToMarkdown', () => {
  it('导出已完成 bot 消息携带的工具调用参数和返回', () => {
    const messages: ChatMessage[] = [
      { id: 'u1', role: 'user', content: '生成' },
      {
        id: 'b1',
        role: 'bot',
        content: '技能实现内容已经生成。',
        toolSteps: [
          {
            id: 't1',
            name: 'load_skill',
            status: 'done',
            args: '{"skill":"ontology-extraction"}',
            result: '<skill-instructions>\n## Skill: ontology-extraction',
          },
        ],
      },
    ]

    const markdown = chatToMarkdown(messages, '化妆品排产员')

    expect(markdown).toContain('#### 工具调用 (1)')
    expect(markdown).toContain('##### 1. `load_skill`')
    expect(markdown).toContain('- 状态: 完成')
    expect(markdown).toContain('**参数**')
    expect(markdown).toContain('"skill": "ontology-extraction"')
    expect(markdown).toContain('**返回**')
    expect(markdown).toContain('## Skill: ontology-extraction')
  })

  it('导出当前流式轮次中尚未固化到消息里的工具调用', () => {
    const messages: ChatMessage[] = [
      { id: 'u1', role: 'user', content: '生成' },
    ]
    const streamingToolSteps: ToolStep[] = [
      {
        id: 't1',
        name: 'write_file',
        status: 'running',
        args: '{"path":"/workspace/output.json"}',
        result: 'Written 5450 characters to /workspace/output.json',
      },
    ]

    const markdown = chatToMarkdown(messages, '化妆品排产员', {
      streamingToolSteps,
      streamingContent: null,
    })

    expect(markdown).toContain('#### 当前轮次工具调用 (1)')
    expect(markdown).toContain('##### 1. `write_file`')
    expect(markdown).toContain('- 状态: 运行中')
    expect(markdown).toContain('"path": "/workspace/output.json"')
    expect(markdown).toContain('Written 5450 characters to /workspace/output.json')
  })
})
