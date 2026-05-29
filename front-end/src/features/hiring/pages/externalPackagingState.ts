import type { HiringExternalSystemConfig } from '@/infra/api'

import { buildExternalConfigCommittedSignature } from './externalConfigCommitted'

export type ExternalConfigChangeSource = 'hydrate' | 'save' | 'skip' | 'clear'

function getExternalConfigSignature(config: HiringExternalSystemConfig | null): string {
  return config ? buildExternalConfigCommittedSignature(config) : ''
}

export function shouldRequireFreshPackagingAfterExternalConfigChange(
  previousConfig: HiringExternalSystemConfig | null,
  nextConfig: HiringExternalSystemConfig | null,
  source: ExternalConfigChangeSource,
  instanceCreated: boolean,
): boolean {
  if (source === 'hydrate' || instanceCreated) {
    return false
  }

  return getExternalConfigSignature(previousConfig) !== getExternalConfigSignature(nextConfig)
}
