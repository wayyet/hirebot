import { createRawHttpClient, httpClient } from '../httpClient'

// 模板池 / Skills Hub 共享同一服务端，从运行时配置读取基址（与 employeeTemplateApi 保持一致）
const storeApiBase =
  (typeof window !== 'undefined' ? window.__AUTH_CONFIG__?.TemplateApiBase : undefined) ??
  (import.meta.env.VITE_TEMPLATE_API_BASE_URL as string | undefined) ??
  ''

// Skills Hub 裸响应客户端（与模板池接口同源，响应不带 {code,success,data} 包装）
const storeRawClient = createRawHttpClient(storeApiBase)

/** Skills Hub（模板池同源）返回的技能列表项。字段命名与 /api/store/skills 对齐。 */
export interface StoreSkillItem {
  id: string
  name: string
  displayName?: string
  description?: string
  currentVersion?: string
  level?: string
  status?: string
  tags?: string[]
  updatedAt?: string
}

export interface StoreSkillListData {
  page: number
  pageSize: number
  total: number
  items: StoreSkillItem[]
}

export interface RecommendedStoreSkillItem extends StoreSkillItem {
  score: number
  matchedKeywords: string[]
  reason: string
  canDownload: boolean
}

interface RecommendedStoreSkillResponseItem {
  skill_id: string
  name: string
  display_name: string
  description: string
  current_version: string
  tags: string[]
  score: number
  matched_keywords: string[]
  reason: string
  can_download: boolean
}

function mapRecommendedSkill(item: RecommendedStoreSkillResponseItem): RecommendedStoreSkillItem {
  return {
    id: item.skill_id,
    name: item.name,
    displayName: item.display_name,
    description: item.description,
    currentVersion: item.current_version,
    tags: item.tags ?? [],
    score: item.score,
    matchedKeywords: item.matched_keywords ?? [],
    reason: item.reason,
    canDownload: item.can_download,
  }
}

export const skillCatalogApi = {
  /**
   * 从模板池/Skills Hub 搜索技能（与模板池接口同源）。
   * 端点：GET {TemplateApiBase}/api/store/skills?q=&page=&pageSize=
   */
  searchStoreSkills(
    params: { q?: string; page?: number; pageSize?: number },
    signal?: AbortSignal,
  ) {
    return storeRawClient.get<StoreSkillListData>('/api/store/skills', params, signal)
  },

  async getRecommendedStoreSkills(
    templateId: string,
    params: { limit?: number } = {},
    signal?: AbortSignal,
  ) {
    const items = await httpClient.get<RecommendedStoreSkillResponseItem[]>(
      `/api/v1/employee-templates/${encodeURIComponent(templateId)}/recommended-skills`,
      { limit: params.limit ?? 5 },
      signal,
    )
    return (items ?? []).map(mapRecommendedSkill)
  },
}
