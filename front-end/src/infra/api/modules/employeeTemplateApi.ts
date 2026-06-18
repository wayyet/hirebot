import { createRawHttpClient, httpClient } from '../httpClient'
import { tokenService } from '@/infra/auth/token-service'

// 从运行时配置读取模板池独立服务地址，未配置时回退到主服务（空字符串）
const templateApiBase =
  (typeof window !== 'undefined' ? window.__AUTH_CONFIG__?.TemplateApiBase : undefined) ??
  (import.meta.env.VITE_TEMPLATE_API_BASE_URL as string | undefined) ??
  ''

// 模板池专用裸响应客户端（BuildService 响应不含 {code,success,data} 包装）
const templateRawClient = createRawHttpClient(templateApiBase)

export interface TemplateLatestVersion {
  id: string
  version: string
  changeLog: string
  publishedAt: string
  packageUrl: string
  hasPackage: boolean
  packageStatus: string
  unavailableReason: string | null
}

export interface TemplateCreatorRef {
  username?: string | null
  displayName?: string | null
  familyName?: string | null
  givenName?: string | null
}

export interface EmployeeTemplateCard {
  id: string
  name: string
  displayName: string
  positioning: string
  description: string
  currentVersion: string | null
  installCount: number
  updatedAt: string
  status: string
  /** JSON 字符串，需要 JSON.parse 后使用 */
  useCases: string[]
  tags: string[]
  skillCount: number
  requiredSkillCount: number
  createdByUserId?: string | null
  createdBy?: TemplateCreatorRef | null
  hasPackage: boolean
  packageStatus: string
  unavailableReason: string | null
  latestVersion: TemplateLatestVersion | null
}

export interface EmployeeTemplateListData {
  page: number
  pageSize: number
  total: number
  items: EmployeeTemplateCard[]
}

export interface TemplateResponsibilityBoundary {
  inScope: string[]
  outOfScope: string[]
}

export interface TemplatePrerequisite {
  systemName: string
  permissionName: string
  requiredLevel: string
  purpose: string
}

export interface TemplateCta {
  label: string
  action: string
}

export interface EmployeeTemplatePackageSkill {
  name: string
  relativePath: string
  required: boolean
}

export interface EmployeeTemplateDetail {
  templateId: string
  iconUrl: string
  name: string
  tagline: string
  description: string
  detailDoc: string
  coreAbilities: string[]
  responsibilityBoundary: TemplateResponsibilityBoundary
  prerequisites: TemplatePrerequisite[]
  successCases: string[]
  packageSkills: EmployeeTemplatePackageSkill[]
  cta: TemplateCta
}

export interface StoreTemplateDetail {
  id: string
  name: string
  currentVersion?: string
  useCases?: string[]
  hasPackage?: boolean
  packageStatus?: string
  latestVersion?: TemplateLatestVersion | null
}

export interface TemplatePackageDownloadData {
  fileName: string
  blob: Blob
}

function buildTemplateApiUrl(path: string): string {
  const effectiveBase = templateApiBase.trim()
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  const locationBase = typeof window !== 'undefined' ? window.location.origin : 'http://localhost'
  const base = effectiveBase || ''
  return new URL(`${base}${normalizedPath}`, locationBase).toString()
}

function parseFileName(contentDisposition?: string | null): string | null {
  if (!contentDisposition) {
    return null
  }

  const match = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(contentDisposition)
  if (!match) {
    return null
  }

  const encoded = match[1] ?? match[2]
  if (!encoded) {
    return null
  }

  try {
    return decodeURIComponent(encoded)
  } catch {
    return encoded
  }
}

export interface HireTemplateRequest {
  tenantId?: string
  operatorId?: string
  useCase?: string
}

export interface HireTemplateResult {
  hireId: string
  sandboxId: string
  status: string
  nextAction: string
  // 沙箱处于 Running+Initialized 时后端直接返回，前端可跳过额外的状态轮询
  gatewayEndpoint?: string | null
  // 已有会话时后端直接返回，前端可跳过 startConversation() 调用
  sessionId?: string | null
}

export interface HiringStatusResult {
  hireId: string
  sandboxId: string
  status: string
  gatewayEndpoint?: string | null
  errorCode?: string | null
  errorMessage?: string | null
}

export interface FixtureTemplateHireResult {
  employeeId: string
  templateId: string
  instanceType: 'department' | 'personal_clone' | 'private_branch'
  status: 'hiring' | 'hired' | 'interning_ai' | 'interning_human' | 'live' | 'failed' | 'retired'
  createdByFixtureFallback: boolean
}

export const employeeTemplateApi = {
  // --- BuildService 接口（裸响应）---
  getList(params: {
    q?: string
    tag?: string
    page?: number
    pageSize?: number
  }, signal?: AbortSignal) {
    return templateRawClient.get<EmployeeTemplateListData>('/api/store/templates', params, signal)
  },

  getTags(signal?: AbortSignal) {
    return templateRawClient.get<string[]>('/api/store/templates/tags', undefined, signal)
  },

  getStoreDetail(templateId: string) {
    return templateRawClient.get<StoreTemplateDetail>(`/api/store/templates/${templateId}`)
  },

  async downloadTemplatePackage(templateId: string, versionId: string): Promise<TemplatePackageDownloadData> {
    const accessToken = await tokenService.ensureFresh()
    const url = buildTemplateApiUrl(
      `/api/store/templates/${encodeURIComponent(templateId)}/versions/${encodeURIComponent(versionId)}/download`,
    )
    const response = await fetch(url, {
      method: 'GET',
      headers: accessToken
        ? {
          Authorization: `Bearer ${accessToken}`,
        }
        : undefined,
    })

    if (!response.ok) {
      throw new Error(`模板下载失败（HTTP ${response.status}）`)
    }

    const fallbackName = `template_${templateId}_${versionId}.zip`
    const fileName = parseFileName(response.headers.get('content-disposition')) ?? fallbackName
    const blob = await response.blob()
    return { fileName, blob }
  },

  // --- 内部 HireBot API（带 envelope）---
  getDetail(templateId: string, signal?: AbortSignal) {
    return httpClient.get<EmployeeTemplateDetail>(`/api/v1/employee-templates/${templateId}`, undefined, signal)
  },

  hire(templateId: string, payload: HireTemplateRequest) {
    return httpClient.post<HireTemplateResult, HireTemplateRequest>(
      `/api/v1/employee-templates/${templateId}/hire`,
      payload,
    )
  },

  fixtureHire(templateId: string) {
    return httpClient.post<FixtureTemplateHireResult>(`/api/v1/employee-templates/${templateId}/fixture-hire`)
  },

  getHiringStatus(hireId: string) {
    return httpClient.get<HiringStatusResult>(`/api/v1/hirings/${hireId}`)
  },
}
