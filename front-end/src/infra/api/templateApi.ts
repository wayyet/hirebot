import { api } from './index'

export type {
  TemplateTrustProof,
  EmployeeTemplateCard,
  EmployeeTemplateListData,
  TemplateResponsibilityBoundary,
  TemplatePrerequisite,
  TemplateCta,
  EmployeeTemplateDetail,
  HireTemplateRequest,
  HireTemplateResult,
  HiringStatusResult,
} from './index'

export const getEmployeeTemplates = api.employeeTemplate.getList
export const getEmployeeTemplateDetail = api.employeeTemplate.getDetail
export const hireEmployeeTemplate = api.employeeTemplate.hire
export const getHiringStatus = api.employeeTemplate.getHiringStatus
