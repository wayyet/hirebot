import { describe, expect, it } from 'vitest'

import type { HiringExternalSystemConfig } from '@/infra/api'

import { shouldRequireFreshPackagingAfterExternalConfigChange } from './externalPackagingState'

const SAMPLE_CONFIG: HiringExternalSystemConfig = {
  submissionMode: 'configured',
  updatedAtUtc: '2026-05-29T07:47:41Z',
  cliTools: [],
  mcpServer: {
    transport: 'http',
    name: 'learn.microsof',
    url: 'https://learn.microsoft.com/api/mcp',
    command: null,
    args: [],
    env: {},
    envPassThrough: [],
    cwd: null,
    bearerTokenEnv: null,
    headers: {},
    headersFromEnv: {},
  },
}

describe('externalPackagingState', () => {
  it('requires a fresh package after a saved external config changes', () => {
    const nextConfig: HiringExternalSystemConfig = {
      ...SAMPLE_CONFIG,
      updatedAtUtc: '2026-05-29T07:50:00Z',
    }

    expect(
      shouldRequireFreshPackagingAfterExternalConfigChange(
        SAMPLE_CONFIG,
        nextConfig,
        'save',
        false,
      ),
    ).toBe(true)
  })

  it('does not require a fresh package when hydrating persisted config', () => {
    expect(
      shouldRequireFreshPackagingAfterExternalConfigChange(
        null,
        SAMPLE_CONFIG,
        'hydrate',
        false,
      ),
    ).toBe(false)
  })

  it('does not require a fresh package when the config signature stays the same', () => {
    expect(
      shouldRequireFreshPackagingAfterExternalConfigChange(
        SAMPLE_CONFIG,
        SAMPLE_CONFIG,
        'save',
        false,
      ),
    ).toBe(false)
  })

  it('does not require a fresh package after the instance was already created', () => {
    expect(
      shouldRequireFreshPackagingAfterExternalConfigChange(
        SAMPLE_CONFIG,
        {
          ...SAMPLE_CONFIG,
          updatedAtUtc: '2026-05-29T08:00:00Z',
        },
        'save',
        true,
      ),
    ).toBe(false)
  })
})
