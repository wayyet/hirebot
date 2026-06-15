import type { ArtifactDisplayData, ChatMessage, DownstreamRunsSnapshot } from '../hiringPageTypes'

export const CONFIRMATION_GATE_ARTIFACT_TYPES = [
  'material_handoff_ready',
  'skill_definition_entry_ready',
  'skill_definition_ready',
  'ontology_projection_ready',
  'skill_generation_ready',
  'external_system_entry_ready',
  'packaging_testcases_ready',
  'review_readiness',
] as const

export type ConfirmationGateArtifactType = typeof CONFIRMATION_GATE_ARTIFACT_TYPES[number]

const CONFIRMATION_GATE_ARTIFACT_TYPE_SET = new Set<string>(CONFIRMATION_GATE_ARTIFACT_TYPES)

const VOLATILE_SIGNATURE_KEYS = new Set([
  'created_at',
  'createdAt',
  'emitted_at',
  'emittedAt',
  'generated_at',
  'generatedAt',
  'label',
  'message',
  'prompt',
  'status',
  'updated_at',
  'updatedAt',
  'updatedAtUtc',
])

export function isConfirmationGateArtifactType(
  artifactType: string,
): artifactType is ConfirmationGateArtifactType {
  return CONFIRMATION_GATE_ARTIFACT_TYPE_SET.has(artifactType)
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function stableNormalizeForSignature(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(stableNormalizeForSignature)
  }

  const record = asRecord(value)
  if (!record) {
    return value
  }

  const normalized: Record<string, unknown> = {}
  for (const key of Object.keys(record).sort()) {
    if (VOLATILE_SIGNATURE_KEYS.has(key)) {
      continue
    }
    normalized[key] = stableNormalizeForSignature(record[key])
  }

  return normalized
}

export function buildConfirmationGateContextSignature(
  _artifactType: string,
  data: unknown,
): string {
  const record = asRecord(data)
  const explicitSignature = record?.context_signature ?? record?.contextSignature
  if (typeof explicitSignature === 'string' && explicitSignature.trim()) {
    return explicitSignature.trim()
  }

  return JSON.stringify(stableNormalizeForSignature(data ?? {}))
}

export function buildConfirmationGateEventSignature(artifact: ArtifactDisplayData): string {
  return `${artifact.artifactType}:${buildConfirmationGateContextSignature(artifact.artifactType, artifact.data)}`
}

export function hasConfirmationGateArtifact(
  messages: ChatMessage[],
  artifact: ArtifactDisplayData,
): boolean {
  if (!isConfirmationGateArtifactType(artifact.artifactType)) {
    return false
  }

  const signature = buildConfirmationGateEventSignature(artifact)
  return messages.some(message => {
    const existing = message.artifact
    return existing
      ? isConfirmationGateArtifactType(existing.artifactType) &&
        buildConfirmationGateEventSignature(existing) === signature
      : false
  })
}

export function hasActiveConfirmationGateRun(
  downstreamRuns: DownstreamRunsSnapshot,
  artifact: ArtifactDisplayData,
): boolean {
  if (!isConfirmationGateArtifactType(artifact.artifactType)) {
    return false
  }

  const nextSignature = buildConfirmationGateContextSignature(artifact.artifactType, artifact.data)
  return Object.values(downstreamRuns).some(run => {
    if (!run || run.artifactType !== artifact.artifactType) {
      return false
    }
    if (run.status !== 'waiting_confirm' && run.status !== 'running' && run.status !== 'completed') {
      return false
    }

    return buildConfirmationGateContextSignature(run.artifactType, run.data) === nextSignature
  })
}

export function shouldAcceptConfirmationGateArtifact(
  messages: ChatMessage[],
  downstreamRuns: DownstreamRunsSnapshot,
  artifact: ArtifactDisplayData,
): boolean {
  if (!isConfirmationGateArtifactType(artifact.artifactType)) {
    return true
  }

  return !hasConfirmationGateArtifact(messages, artifact) &&
    !hasActiveConfirmationGateRun(downstreamRuns, artifact)
}
