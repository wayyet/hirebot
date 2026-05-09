import { httpClient } from '../httpClient'

export interface TemplateTrustProof {
  hiredCount: number
  successRate: number
  avgRating: number
}

export interface EmployeeTemplateCard {
  templateId: string
  iconUrl: string
  name: string
  tagline: string
  coreAbilityTags: string[]
  trustProof: TemplateTrustProof
  isAvailable: boolean
}

export interface EmployeeTemplateListData {
  page: number
  pageSize: number
  totalCount: number
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
  cta: TemplateCta
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
  status: 'hired' | 'interning_ai' | 'interning_human' | 'live' | 'failed' | 'retired'
  createdByFixtureFallback: boolean
}

export const employeeTemplateApi = {
  getList(params: {
    q?: string
    page?: number
    pageSize?: number
  }) {
    return httpClient.get<EmployeeTemplateListData>('/api/v1/employee-templates', params)
  },

  getDetail(templateId: string) {
    return httpClient.get<EmployeeTemplateDetail>(`/api/v1/employee-templates/${templateId}`)
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
