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

  it('hides a leaked downstream trigger prompt that starts from the skill switch line', () => {
    const content = [
      'Switch to skill `skill-generation` now. source_skill: employment-coach-conversation trigger_reason: user_confirmed_skill_generation',
      'Use the payload below exactly as the trigger input for this run. Follow `skill-generation/SKILL.md` exactly.',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('')
  })

  it('hides a leaked downstream trigger payload when Markdown code markers are stripped', () => {
    const content = [
      'Switch to skill skill-generation now. source_skill: employment-coach-conversation trigger_reason: user_confirmed_skill_generation',
      'Use the payload below exactly as the trigger input for this run. Follow skill-generation/SKILL.md exactly.',
      'required_artifacts:',
      'skill_generation_progress',
      'skill_generation_done return_to: employment-coach-conversation',
      'artifact_payload:',
      '{',
      '  "trigger_mode": "skill_generation",',
      '  "workspace_root": "/workspace/template-20260625153339"',
      '}',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('')
  })

  it('removes leaked downstream payload markers when the skill switch line is missing', () => {
    const content = [
      '技能实现生成已启动。',
      'required_artifacts:',
      'skill_generation_progress',
      'skill_generation_done return_to: employment-coach-conversation',
      'artifact_payload:',
      '{',
      '  "trigger_mode": "skill_generation"',
      '}',
    ].join('\n')

    expect(normalizeAssistantReply(content)).toBe('技能实现生成已启动。')
  })

  it('hides a leaked artifact payload that starts at the payload marker', () => {
    const content = [
      'artifact_payload:',
      '{',
      '  "trigger_mode": "skill_generation",',
      '  "workspace_root": "/workspace/template-20260625153339"',
      '}',
    ].join('\n')

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

  it('hides a streaming preview that starts from the skill switch line', () => {
    const content = [
      'Switch to skill `skill-generation` now.',
      'source_skill: employment-coach-conversation',
      'trigger_reason: user_confirmed_skill_generation',
    ].join('\n')

    expect(normalizeAssistantStreamingPreview(content)).toBe('')
  })

  it('hides a streaming preview without Markdown code markers around the skill name', () => {
    const content = [
      'Switch to skill skill-generation now.',
      'source_skill: employment-coach-conversation',
      'trigger_reason: user_confirmed_skill_generation',
      'artifact_payload:',
    ].join('\n')

    expect(normalizeAssistantStreamingPreview(content)).toBe('')
  })

  it('hides a streaming preview that starts from downstream payload markers', () => {
    const content = [
      'required_artifacts:',
      'skill_generation_progress',
      'skill_generation_done return_to: employment-coach-conversation',
      'artifact_payload:',
    ].join('\n')

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
