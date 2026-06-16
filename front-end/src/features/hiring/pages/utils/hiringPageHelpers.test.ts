import { describe, expect, it } from 'vitest'

import { normalizeAssistantReply, normalizeAssistantStreamingPreview } from './hiringPageHelpers'
import { buildDownstreamPrompt } from './hiringDownstreamTriggers'

describe('normalizeAssistantReply', () => {
  it('hides a full internal downstream trigger prompt', () => {
    const content = buildDownstreamPrompt('skill-generation', {
      workspace_root: '/workspace/template-20260611202657',
      template_slug: 'cosmetics-scheduler',
      confirmed_skill_slugs: ['live-insertion-feasibility-assessment'],
      projection_binding_confirmed: true,
    })

    expect(normalizeAssistantReply(content)).toBe('')
  })

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

  it('移除泄露到助手正文中的资料收口状态 JSON', () => {
    const content = [
      '业务资料已够用，是否开始分析业务资料并进入技能定义？',
      '{"total_items":1,"summary":"已整理 1 份资料，建议开始分析业务资料并进入技能定义阶段。","next_step":"客户信息收集规则与字段口径","category":"客户信息收集规则与字段口径","objective":"抽取客户画像字段结构","status":"ready"}',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('业务资料已够用，是否开始分析业务资料并进入技能定义？')
  })
})

describe('normalizeAssistantStreamingPreview', () => {
  it('hides a streaming preview that starts with an internal downstream trigger prompt', () => {
    const content = buildDownstreamPrompt('ontology-projection', {
      workspace_root: '/workspace/template-20260611202657',
      skills: [{ skill_slug: 'live-insertion-feasibility-assessment' }],
    })

    expect(normalizeAssistantStreamingPreview(content)).toBe('')
  })

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

  it('流式预览中移除已闭合的资料收口状态 JSON', () => {
    const content = [
      '业务资料已够用，是否开始分析业务资料并进入技能定义？',
      '{"total_items":1,"summary":"已整理 1 份资料。","next_step":"客户信息收集规则与字段口径","status":"ready"}',
    ].join('\n')

    expect(normalizeAssistantStreamingPreview(content)).toBe('业务资料已够用，是否开始分析业务资料并进入技能定义？')
  })
})
