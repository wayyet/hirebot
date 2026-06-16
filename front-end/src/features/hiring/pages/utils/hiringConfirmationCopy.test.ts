import { describe, expect, it } from 'vitest'

import type { DownstreamRunKey, DownstreamRunState } from '../hiringPageTypes'
import { getConfirmationActionCopy, getStageAdvanceConfirmationCopy } from './hiringConfirmationCopy'

function waitingRun(key: DownstreamRunKey, artifactType: string, data?: unknown): DownstreamRunState {
  return {
    key,
    status: 'waiting_confirm',
    artifactType,
    updatedAt: '2026-06-16T00:00:00.000Z',
    data,
  }
}

describe('hiring confirmation copy', () => {
  it('uses user-facing words for the main confirmation gates', () => {
    const materialCopy = getConfirmationActionCopy(waitingRun('material-handoff', 'material_handoff_ready'))
    const copies = [
      materialCopy,
      getConfirmationActionCopy(waitingRun('skill-definition-entry', 'skill_definition_entry_ready')),
      getConfirmationActionCopy(waitingRun('skill-generation', 'skill_definition_ready')),
      getConfirmationActionCopy(waitingRun('ontology-projection', 'ontology_projection_ready')),
      getConfirmationActionCopy(waitingRun('skill-generation', 'skill_generation_ready')),
      getConfirmationActionCopy(waitingRun('external-system-entry', 'external_system_entry_ready')),
      getConfirmationActionCopy(waitingRun('packaging-test-cases', 'packaging_testcases_ready')),
    ]

    expect(copies.map(copy => copy.button)).toEqual([
      '开始分析资料',
      '进入技能定义',
      '确认技能清单',
      '匹配技能资料',
      '生成技能实现',
      '进入外部配置',
      '生成测试用例',
    ])

    const userFacingText = copies.flatMap(copy => [copy.text, copy.button, copy.visibleMessage]).join('\n')
    expect(userFacingText).not.toMatch(/artifact|workorder|handoff|ontology|projection|stage\d|R\d|实例包|产物包/i)
    expect(materialCopy.text).toBe('资料已经整理好。是否开始分析这批业务资料？')
    expect(materialCopy.text).not.toContain('技能')
  })

  it('adjusts skill generation copy when generation needs business confirmation items', () => {
    const copy = getConfirmationActionCopy(waitingRun('skill-generation', 'skill_generation_ready', {
      open_questions: ['确认是否按直播插单优先级生成。'],
    }))

    expect(copy.button).toBe('确认口径并生成')
    expect(copy.visibleMessage).toBe('确认生成前业务口径并生成技能实现')
  })

  it('provides visible messages for legacy stage confirmation buttons', () => {
    expect(getStageAdvanceConfirmationCopy('material')).toMatchObject({
      prompt: '当前资料已整理好。是否开始分析这批业务资料？如果还想补充，可以先继续上传。',
      confirmLabel: '开始分析资料',
      visibleMessage: '开始分析这批业务资料',
    })
    expect(getStageAdvanceConfirmationCopy('external')).toMatchObject({
      confirmLabel: '确认外部配置',
      visibleMessage: '确认外部配置并继续',
    })
  })
})
