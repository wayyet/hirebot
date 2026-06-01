import { describe, expect, it } from 'vitest'

import { HiringCollectionStage } from '@/infra/api'

import {
  buildPendingStageAdvanceConfirmation,
  shouldRequireStageAdvanceConfirmation,
} from './stageAdvanceConfirmation'

describe('stageAdvanceConfirmation', () => {
  it('requires explicit confirmation before advancing the material stage', () => {
    expect(
      shouldRequireStageAdvanceConfirmation(
        HiringCollectionStage.Material,
        'ready_to_advance',
      ),
    ).toBe(true)
  })

  it('requires explicit confirmation before advancing the external stage', () => {
    expect(
      shouldRequireStageAdvanceConfirmation(
        HiringCollectionStage.External,
        'ready_to_advance',
      ),
    ).toBe(true)
  })

  it('does not require explicit confirmation for collecting-only stage messages', () => {
    expect(
      shouldRequireStageAdvanceConfirmation(
        HiringCollectionStage.External,
        'collecting',
      ),
    ).toBe(false)
  })

  it('does not require confirmation when user explicitly skips external config', () => {
    expect(
      shouldRequireStageAdvanceConfirmation(
        HiringCollectionStage.External,
        'skip',
      ),
    ).toBe(false)
  })

  it('builds a material-stage confirmation prompt', () => {
    const pending = buildPendingStageAdvanceConfirmation(
      HiringCollectionStage.Material,
      '已上传 2 份资料',
    )

    expect(pending).not.toBeNull()
    expect(pending?.stage).toBe(HiringCollectionStage.Material)
    expect(pending?.summary).toBe('已上传 2 份资料')
    expect(pending?.confirmLabel).toContain('确认推进')
  })
})
