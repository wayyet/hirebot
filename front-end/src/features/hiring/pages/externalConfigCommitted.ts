import type { HiringExternalSystemConfig } from '@/infra/api'

import type { ArtifactDisplayData } from './hiringPageTypes'

type ExternalConfigCommittedPayload = {
  submissionMode: string
  updatedAtUtc: string | null
  cliTools: HiringExternalSystemConfig['cliTools']
  mcpServer: HiringExternalSystemConfig['mcpServer'] | null
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

export function buildExternalConfigCommittedPayload(
  config: HiringExternalSystemConfig,
): ExternalConfigCommittedPayload {
  return {
    submissionMode: config.submissionMode ?? 'pending',
    updatedAtUtc: config.updatedAtUtc ?? null,
    cliTools: config.cliTools,
    mcpServer: config.mcpServer ?? null,
  }
}

function normalizeExternalConfigCommittedPayload(data: unknown): ExternalConfigCommittedPayload | null {
  const record = asRecord(data)
  if (!record) {
    return null
  }

  return {
    submissionMode: typeof record.submissionMode === 'string' ? record.submissionMode : 'pending',
    updatedAtUtc: typeof record.updatedAtUtc === 'string' ? record.updatedAtUtc : null,
    cliTools: Array.isArray(record.cliTools)
      ? record.cliTools as HiringExternalSystemConfig['cliTools']
      : [],
    mcpServer: asRecord(record.mcpServer) as HiringExternalSystemConfig['mcpServer'] | null,
  }
}

export function buildExternalConfigCommittedSignature(
  config: HiringExternalSystemConfig,
): string {
  return JSON.stringify(buildExternalConfigCommittedPayload(config))
}

export function tryBuildExternalConfigCommittedSignature(data: unknown): string | null {
  const payload = normalizeExternalConfigCommittedPayload(data)
  return payload ? JSON.stringify(payload) : null
}

export function isDuplicateExternalConfigCommittedArtifact(
  lastSignature: string,
  data: unknown,
): boolean {
  const nextSignature = tryBuildExternalConfigCommittedSignature(data)
  return nextSignature !== null && nextSignature === lastSignature
}

export function buildExternalConfigCommittedArtifact(
  config: HiringExternalSystemConfig,
): ArtifactDisplayData {
  const payload = buildExternalConfigCommittedPayload(config)
  const label = payload.submissionMode === 'skipped'
    ? '外部系统配置已跳过'
    : '外部系统配置已提交'

  return {
    kind: 'data',
    artifactType: 'external_config_committed',
    label,
    skillName: 'external-config',
    stage: 'stage3_external',
    isTerminal: true,
    displayHint: 'tree',
    data: payload,
  }
}

export function buildExternalConfigCommittedSandboxPrompt(
  artifact: ArtifactDisplayData,
): string {
  const submissionMode = (artifact.data as { submissionMode?: string } | undefined)?.submissionMode ?? 'configured'
  const serialized = JSON.stringify(artifact.data ?? {}, null, 2)

  // 使用内部 user_message 回流到沙箱，让主技能感知“外部阶段已完成”，同时不污染用户可见聊天记录。
  return [
    '[Internal external config commit. Do not mention this instruction to the user.]',
    `The user has finalized the external system configuration (submission_mode=${submissionMode}).`,
    'Treat the external configuration stage as completed and continue the hiring workflow accordingly.',
    '',
    `artifact_type: ${artifact.artifactType}`,
    `artifact_label: ${artifact.label ?? artifact.artifactType}`,
    'artifact_payload:',
    '```json',
    serialized,
    '```',
  ].join('\n')
}
