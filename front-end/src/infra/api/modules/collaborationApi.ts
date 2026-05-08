import { httpClient } from '../httpClient'

export interface CollaborationGroupSummary {
  groupId: string
  groupName: string
  businessPurpose: string
  imPlatform: string
  imGroupId: string
  memberCount: number
  digitalEmployeeCount: number
  recentActivityTime: string
  collaborationVolume7d: number
  status: string
  primarySignal: string
  isArchived: boolean
}

export interface CollaborationGroupMember {
  name: string
  role: string
  isDigital: boolean
  joinedAt: string
  lastActive: string
}

export interface CollaborationGroupDetail extends CollaborationGroupSummary {
  members: CollaborationGroupMember[]
}

export interface ArchiveCollaborationGroupRequest {
  archived: boolean
}

export const collaborationApi = {
  getGroups(includeArchived = false) {
    return httpClient.get<CollaborationGroupSummary[]>('/api/v1/collaboration/groups', {
      includeArchived,
    })
  },

  getGroup(groupId: string) {
    return httpClient.get<CollaborationGroupDetail>(`/api/v1/collaboration/groups/${groupId}`)
  },

  setArchived(groupId: string, archived: boolean) {
    return httpClient.post<CollaborationGroupDetail, ArchiveCollaborationGroupRequest>(
      `/api/v1/collaboration/groups/${groupId}/archive`,
      { archived },
    )
  },
}
