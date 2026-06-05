import { describe, expect, it } from 'vitest'
import { HiringCollectionStage } from '@/infra/api'
import { buildHiringWorkflowViewModel } from '../hiringWorkflowViewModel'
import { buildDerivedWorkflowStateFromStageOverrides } from './useHiringComputed'

describe('buildDerivedWorkflowStateFromStageOverrides', () => {
  it('资料阶段已产出后，顶部步骤应推进到技能阶段', () => {
    const workflowState = buildDerivedWorkflowStateFromStageOverrides(new Map([
      [HiringCollectionStage.Material, 'completed'],
    ]))

    const viewModel = buildHiringWorkflowViewModel(workflowState, null)

    expect(viewModel.uiCurrentStage).toBe(HiringCollectionStage.Skill)
    expect(viewModel.stepPills[0]?.status).toBe('complete')
    expect(viewModel.stepPills[1]?.status).toBe('active')
    expect(viewModel.stepPills[2]?.status).toBe('pending')
  })

  it('技能阶段完成后，顶部步骤应推进到外部连接阶段', () => {
    const workflowState = buildDerivedWorkflowStateFromStageOverrides(new Map([
      [HiringCollectionStage.Material, 'completed'],
      [HiringCollectionStage.Skill, 'completed'],
    ]))

    const viewModel = buildHiringWorkflowViewModel(workflowState, null)

    expect(viewModel.uiCurrentStage).toBe(HiringCollectionStage.External)
    expect(viewModel.stepPills[0]?.status).toBe('complete')
    expect(viewModel.stepPills[1]?.status).toBe('complete')
    expect(viewModel.stepPills[2]?.status).toBe('active')
  })
})
