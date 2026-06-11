import { describe, expect, it } from 'vitest'

import type { DownstreamRunsSnapshot } from '../hiringPageTypes'
import {
  hasPendingDownstreamRuns,
  hasPendingRequiredDownstreamRuns,
} from './hiringCacheNormalizers'

describe('hasPendingRequiredDownstreamRuns', () => {
  it('不把可选评估测试用例确认门当作最终包自动导入阻塞项', () => {
    const runs: DownstreamRunsSnapshot = {
      'packaging-test-cases': {
        key: 'packaging-test-cases',
        status: 'waiting_confirm',
        artifactType: 'packaging_testcases_ready',
        updatedAt: new Date(0).toISOString(),
      },
    }

    expect(hasPendingDownstreamRuns(runs)).toBe(true)
    expect(hasPendingRequiredDownstreamRuns(runs)).toBe(false)
  })

  it('仍然阻止必需下游运行中时导入最终包', () => {
    const runs: DownstreamRunsSnapshot = {
      'skill-generation': {
        key: 'skill-generation',
        status: 'running',
        artifactType: 'skill_generation_progress',
        updatedAt: new Date(0).toISOString(),
      },
    }

    expect(hasPendingRequiredDownstreamRuns(runs)).toBe(true)
  })
})
