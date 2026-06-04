/**
 * artifactHelpers.ts - Artifact 工具函数
 * 
 * 包含 artifact 数据处理、类型检查、转换等通用工具函数
 */

/**
 * 检查值是否为 Record 对象（非数组的普通对象）
 */
export function isRecord(v: unknown): v is Record<string, unknown> {
  return !!v && typeof v === 'object' && !Array.isArray(v)
}

/**
 * 将值转换为 Record 对象，失败返回 null
 */
export function asRecord(v: unknown): Record<string, unknown> | null {
  return isRecord(v) ? v : null
}

/**
 * 从 record 中提取数组字段（按 keys 顺序尝试），返回过滤后的 Record 数组
 */
export function getRecordArray(record: Record<string, unknown>, ...keys: string[]): Record<string, unknown>[] {
  for (const key of keys) {
    const value = record[key]
    if (Array.isArray(value)) {
      return value.filter(isRecord)
    }
  }

  return []
}

/**
 * 从多个值中返回第一个非空字符串（trim后）
 */
export function firstString(...values: unknown[]): string {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) {
      return value.trim()
    }
  }

  return ''
}

/**
 * 将值转换为字符串列表文本（数组用分号连接，非数组返回 firstString）
 */
export function stringListText(value: unknown): string {
  if (Array.isArray(value)) {
    return value
      .filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
      .join('；')
  }

  return firstString(value)
}

/**
 * 检查 record 是否具有技能工作单（skill workorder）的结构特征
 */
export function hasSkillWorkorderShape(record: Record<string, unknown>): boolean {
  const items = getRecordArray(record, 'items', 'skills')
  return items.some(item =>
    item.generation_action != null ||
    item.generationAction != null ||
    item.expected_output != null ||
    item.expected_outputs != null ||
    item.trigger != null ||
    item.triggers != null ||
    item.skill_slug != null ||
    item.skill_name != null,
  )
}

/**
 * 检查 record 是否具有外部工作单（external workorder）的结构特征
 */
export function hasExternalWorkorderShape(record: Record<string, unknown>): boolean {
  const items = getRecordArray(record, 'external_capabilities', 'items')
  return items.some(item =>
    item.target_system != null ||
    item.auth_kind != null ||
    item.linked_skills != null ||
    item.required_fields != null ||
    item.integration_methods != null,
  )
}

/**
 * 从文件路径或 FILE_URL 标记中提取文件名（只返回最后一段）
 */
export function toPublicPathLabel(value: string): string {
  const trimmed = value.trim()
  if (!trimmed) return ''

  const markerMatch = /\[FILE_URL:([^\]]+)\]/.exec(trimmed)
  const pathLike = markerMatch?.[1]?.trim() || trimmed
  const parts = pathLike.split(/[\\/]/).filter(Boolean)
  return parts.at(-1) ?? trimmed
}

/**
 * 将任意值转换为字符串（用于显示）
 */
export function stringify(v: unknown): string {
  if (v == null) return ''
  if (typeof v === 'string') return v
  if (typeof v === 'number' || typeof v === 'boolean') return String(v)
  return JSON.stringify(v)
}

/**
 * 限制数值在 0-100 范围内（用于进度条百分比）
 */
export function clamp(v: number): number {
  return Math.max(0, Math.min(100, Math.round(Number.isFinite(v) ? v : 0)))
}
