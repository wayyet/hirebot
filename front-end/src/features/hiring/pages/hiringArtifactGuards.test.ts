import { describe, expect, it } from 'vitest'
import { getBlockedIncomingArtifactReason, normalizeIncomingArtifactTerminal } from './hiringArtifactGuards'

const emptyState = {
  hasMaterialSummary: false,
  hasSkillSummary: false,
  hasProjectionResult: false,
  hasExternalConfigCommitted: false,
}

describe('getBlockedIncomingArtifactReason', () => {
  it('阻止非协议 artifact 类型', () => {
    expect(getBlockedIncomingArtifactReason('stage2_analysis', emptyState))
      .toBe('unknown hiring artifact type')
  })

  it('要求 terminal summary 必须携带终态标记', () => {
    expect(getBlockedIncomingArtifactReason('material_handoff_summary', {
      ...emptyState,
      hasMaterialSummary: true,
    }, { isTerminal: false, kind: 'data' }))
      .toBe('material_handoff_summary must be terminal')
  })

  it('阻止没有资料阶段收口的本体抽取进度', () => {
    expect(getBlockedIncomingArtifactReason('ontology_extraction_progress', emptyState))
      .toBe('ontology extraction requires material_handoff_summary')
  })

  it('允许资料阶段收口后的本体抽取进度', () => {
    expect(getBlockedIncomingArtifactReason('ontology_extraction_progress', {
      ...emptyState,
      hasMaterialSummary: true,
    })).toBeNull()
  })

  it('阻止没有投影结果的技能生成进度', () => {
    expect(getBlockedIncomingArtifactReason('skill_generation_progress', {
      ...emptyState,
      hasSkillSummary: true,
    })).toBe('skill generation requires ontology_projection_done')
  })

  it('允许完成技能定义和投影后的技能生成进度', () => {
    expect(getBlockedIncomingArtifactReason('skill_generation_progress', {
      ...emptyState,
      hasSkillSummary: true,
      hasProjectionResult: true,
    })).toBeNull()
  })
})

describe('normalizeIncomingArtifactTerminal', () => {
  it('将误标为终态的资料进度 artifact 降级为非终态', () => {
    expect(normalizeIncomingArtifactTerminal('material_collection_progress', true)).toBe(false)
  })

  it('保留 terminal summary 的终态标记', () => {
    expect(normalizeIncomingArtifactTerminal('material_handoff_summary', true)).toBe(true)
  })
})
