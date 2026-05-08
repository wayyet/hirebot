import { httpClient } from '../httpClient'
import type { QueryParams } from '../types'

export interface TeamImItem {
  itemId: string
  employeeId: string
  employeeName: string
  category: string
  content: string
  source: string
  receivedAt: string
  status: 'pending' | 'confirmed'
  confirmedAt?: string | null
}

export interface TeamImQuery extends QueryParams {
  employeeId?: string
  category?: string
  status?: 'pending' | 'confirmed' | 'all'
  source?: string
  page?: number
  pageSize?: number
}

export interface ConfirmTeamImItemRequest {
  requestId?: string
}

export const teamImApi = {
  getItems(query: TeamImQuery = {}) {
    return httpClient.get<TeamImItem[]>('/api/v1/team/im-items', query)
  },

  confirmItem(itemId: string, payload: ConfirmTeamImItemRequest = {}) {
    return httpClient.post<TeamImItem, ConfirmTeamImItemRequest>(
      `/api/v1/team/im-items/${encodeURIComponent(itemId)}/confirm`,
      payload,
    )
  },
}
