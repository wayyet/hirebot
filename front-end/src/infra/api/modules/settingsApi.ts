import { httpClient } from '../httpClient'

export interface HiringSandboxItem {
  instanceId: string
  sandboxId: string
  scopeType: string
  scopeKey: string
  sandboxRole: string
  provisioningMode: string
  ownerSubject: string
  state: string
  gatewayEndpoint: string | null
  expiresAtUtc: string | null
  lastError: string | null
  useCase: string | null
  templateId: string | null
  isInitialized: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export const settingsApi = {
  /** 获取当前用户的所有活跃雇佣沙箱 */
  listSandboxes(): Promise<HiringSandboxItem[]> {
    return httpClient.get<HiringSandboxItem[]>('/api/v1/settings/sandboxes')
  },

  /** 删除指定沙箱 */
  deleteSandbox(sandboxId: string): Promise<boolean> {
    return httpClient.delete<boolean>(`/api/v1/settings/sandboxes/${encodeURIComponent(sandboxId)}`)
  },
}
