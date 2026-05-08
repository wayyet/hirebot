import { httpClient } from '../httpClient'

export interface SkillSummary {
  skillId: string
  name: string
  description: string
  level: string
  status: string
  version: string
  updatedAt: string
}

export interface SkillDetail extends SkillSummary {
  inputExample: string
  outputExample: string
  tags: string[]
  boundTemplates: string[]
  files: string[]
}

export const skillCatalogApi = {
  getSkills(params: {
    q?: string
    level?: string
    status?: string
  }) {
    return httpClient.get<SkillSummary[]>('/api/v1/skills', params)
  },

  getSkill(skillId: string) {
    return httpClient.get<SkillDetail>(`/api/v1/skills/${skillId}`)
  },
}
