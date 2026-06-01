import { describe, expect, it } from 'vitest'

import type { HiringExternalSystemConfig } from '@/infra/api'

import {
  buildExternalConfigCommittedArtifact,
  buildExternalConfigCommittedSandboxPrompt,
  buildExternalConfigCommittedSignature,
  isDuplicateExternalConfigCommittedArtifact,
  tryBuildExternalConfigCommittedSignature,
} from './externalConfigCommitted'

const SAMPLE_CONFIG: HiringExternalSystemConfig = {
  submissionMode: 'configured',
  updatedAtUtc: '2026-05-29T07:13:53Z',
  cliTools: [],
  mcpServer: {
    transport: 'http',
    name: '测试',
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

describe('externalConfigCommitted helpers', () => {
  it('builds a stable signature from artifact data', () => {
    const artifact = buildExternalConfigCommittedArtifact(SAMPLE_CONFIG)
    const configSignature = buildExternalConfigCommittedSignature(SAMPLE_CONFIG)
    const artifactSignature = tryBuildExternalConfigCommittedSignature(artifact.data)

    expect(artifactSignature).toBe(configSignature)
  })

  it('detects duplicate external_config_committed artifacts', () => {
    const artifact = buildExternalConfigCommittedArtifact(SAMPLE_CONFIG)
    const signature = buildExternalConfigCommittedSignature(SAMPLE_CONFIG)

    expect(isDuplicateExternalConfigCommittedArtifact(signature, artifact.data)).toBe(true)
    expect(isDuplicateExternalConfigCommittedArtifact('', artifact.data)).toBe(false)
  })

  it('builds an internal sandbox prompt with artifact payload', () => {
    const artifact = buildExternalConfigCommittedArtifact(SAMPLE_CONFIG)
    const prompt = buildExternalConfigCommittedSandboxPrompt(artifact)

    expect(prompt).toContain('[Internal external config commit.')
    expect(prompt).toContain('submission_mode=configured')
    expect(prompt).toContain('artifact_type: external_config_committed')
    expect(prompt).toContain('"url": "https://learn.microsoft.com/api/mcp"')
  })
})
