import i18n from '@/i18n'
import type { EmployeeDetail, EmployeeSummary } from '@/infra/api'

export type HirebotLifecycle = 'hired' | 'interning_ai' | 'interning_human' | 'live' | 'failed' | 'retired'
export type EmployeeOwnership = 'department' | 'personal_clone' | 'private_branch'

type EmployeeLike = Pick<EmployeeSummary, 'nickname' | 'roleName' | 'sourceTemplate' | 'owningTeam' | 'instanceType' | 'status' | 'lifecycleStatus'>

export type EmployeeWithView<T extends EmployeeLike> = T & {
  mappedStatus: HirebotLifecycle
  ownership: EmployeeOwnership
}

function mapLegacyLifecycle(value: string): HirebotLifecycle {
  const lifecycle = value.trim()

  if (lifecycle === '待AI评估') return 'interning_ai'
  if (lifecycle === '待人工评估' || lifecycle === '实习中') return 'interning_human'
  if (lifecycle === '已转正') return 'live'
  if (lifecycle === '离职中' || lifecycle === '已归档') return 'retired'
  if (lifecycle.includes('失败') || lifecycle.includes('异常')) return 'failed'
  return 'hired'
}

function normalizeStatus(value?: string | null): HirebotLifecycle | null {
  if (!value) return null

  const status = value.trim().toLowerCase()
  if (status === 'hired') return 'hired'
  if (status === 'interning_ai') return 'interning_ai'
  if (status === 'interning_human') return 'interning_human'
  if (status === 'live') return 'live'
  if (status === 'failed') return 'failed'
  if (status === 'retired') return 'retired'
  return null
}

export function mapLifecycle(status?: string | null, lifecycleStatus?: string | null): HirebotLifecycle {
  const normalized = normalizeStatus(status)
  if (normalized) {
    return normalized
  }

  if (!lifecycleStatus) {
    return 'hired'
  }

  return mapLegacyLifecycle(lifecycleStatus)
}

export function inferOwnership(employee: EmployeeLike): EmployeeOwnership {
  const type = employee.instanceType?.trim().toLowerCase()
  if (type === 'private_branch') return 'private_branch'
  if (type === 'personal_clone') return 'personal_clone'
  return 'department'
}

export function withEmployeeView<T extends EmployeeLike>(employee: T): EmployeeWithView<T> {
  return {
    ...employee,
    mappedStatus: mapLifecycle(employee.status, employee.lifecycleStatus),
    ownership: inferOwnership(employee),
  }
}

function isPendingOnboarding(lifecycleStatus?: string | null) {
  if (!lifecycleStatus) return false
  const value = lifecycleStatus.trim().toLowerCase()
  return value.includes('待上岗') || value.includes('pending onboarding') || value.includes('pending_onboarding')
}

export function statusLabel(status: HirebotLifecycle, lifecycleStatus?: string | null) {
  if (status === 'live') return i18n.t('employees.status.live')
  if (status === 'interning_human' && isPendingOnboarding(lifecycleStatus)) return i18n.t('employees.status.pendingOnboarding')
  if (status === 'interning_ai') return i18n.t('employees.status.interningAi')
  if (status === 'interning_human') return i18n.t('employees.status.interningHuman')
  if (status === 'failed') return i18n.t('employees.status.failed')
  if (status === 'retired') return i18n.t('employees.status.retired')
  return i18n.t('employees.status.hired')
}

export function statusClass(status: HirebotLifecycle, lifecycleStatus?: string | null) {
  if (status === 'live') return 'green'
  if (status === 'interning_human' && isPendingOnboarding(lifecycleStatus)) return 'blue'
  if (status === 'interning_ai') return 'blue'
  if (status === 'interning_human') return 'orange'
  if (status === 'failed') return 'orange'
  return 'gray'
}

export function ownershipLabel(ownership: EmployeeOwnership) {
  if (ownership === 'private_branch') return i18n.t('employees.ownership.privateBranch')
  if (ownership === 'personal_clone') return i18n.t('employees.ownership.personalClone')
  return i18n.t('employees.ownership.department')
}

export function ownershipClass(ownership: EmployeeOwnership) {
  if (ownership === 'private_branch') return 'pink'
  if (ownership === 'personal_clone') return 'purple'
  return 'blue'
}

export function firstCharacter(value: string) {
  return value.trim().slice(0, 1) || 'E'
}

export function isEvaluating(status: HirebotLifecycle) {
  return status === 'interning_ai' || status === 'interning_human'
}

export function extractCardIntroHeadline(cardIntro: string | null | undefined): string | null {
  if (!cardIntro) return null
  const lines = cardIntro.split('\n')
  for (const line of lines) {
    const t = line.trim()
    if (!t) continue
    if (t.startsWith('#')) continue
    if (t.startsWith('|')) continue
    if (t === '---') continue
    return t.replace(/\*\*/g, '')
  }
  return null
}

export function toEmployeeDetailSummary(detail: EmployeeDetail): EmployeeSummary {
  return {
    employeeId: detail.employeeId,
    nickname: detail.nickname,
    roleName: detail.roleName,
    sourceTemplate: detail.sourceTemplate,
    sourceTemplateId: detail.sourceTemplateId,
    instanceType: detail.instanceType,
    status: detail.status,
    basedOnTemplateId: detail.basedOnTemplateId,
    fromInstanceId: detail.fromInstanceId,
    ownerUserId: detail.ownerUserId,
    departmentId: detail.departmentId,
    lifecycleStatus: detail.lifecycleStatus,
    stageSummary: detail.stageSummary,
    primarySignal: detail.primarySignal,
    signalLevel: detail.signalLevel,
    owningTeam: detail.owningTeam,
    createdAt: detail.createdAt,
    tasksDone: detail.tasksDone,
    tasksTotal: detail.tasksTotal,
    pendingActions: detail.pendingActions,
    isConfigured: detail.isConfigured,
    cardIntro: detail.cardIntro,
  }
}
