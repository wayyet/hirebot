import { httpClient } from '../httpClient'

export interface LocalStateEmployeeMigrationItem {
  employeeId: string
  nickname: string
  roleName: string
  sourceTemplate: string
  sourceTemplateId: string
  lifecycleStatus: string
  stageSummary: string
  primarySignal: string
  signalLevel: string
  owningTeam: string
  createdAt: string
  internshipStartAt?: string
  graduatedAt?: string
  tasksDone: number
  tasksTotal: number
  pendingActions: string[]
  capabilityNames: string[]
  isConfigured: boolean
}

export interface LocalStateMigrationRequest {
  employees?: LocalStateEmployeeMigrationItem[]
  archivedGroupIds?: string[]
}

export interface LocalStateMigrationResult {
  importedEmployees: number
  skippedEmployees: number
  archivedGroups: number
}

export const migrationApi = {
  importFixtureInstances() {
    return httpClient.post<{
      ownerSubject: string
      fixtureDirectories: number
      importedEmployees: number
      importedImItems: number
      employeeIds: string[]
    }>('/api/v1/migrations/fixture-instances')
  },

  migrateLocalState(payload: LocalStateMigrationRequest) {
    return httpClient.post<LocalStateMigrationResult, LocalStateMigrationRequest>(
      '/api/v1/migrations/local-state',
      payload,
    )
  },
}
