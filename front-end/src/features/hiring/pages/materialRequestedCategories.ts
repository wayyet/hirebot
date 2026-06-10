import type { ChatMessage, MaterialRequestedCategory } from './hiringPageTypes'

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function normalizeString(value: unknown, maxLength: number) {
  if (typeof value !== 'string') return ''
  const trimmed = value.trim()
  return trimmed.length <= maxLength ? trimmed : trimmed.slice(0, maxLength)
}

export function normalizeMaterialRequestedCategories(data: unknown): MaterialRequestedCategory[] {
  const rec = asRecord(data)
  const raw = rec?.requested_categories ?? rec?.requestedCategories
  if (!Array.isArray(raw)) return []

  const seen = new Set<string>()
  const result: MaterialRequestedCategory[] = []
  for (const item of raw) {
    if (typeof item === 'string') {
      const title = normalizeString(item, 80)
      const key = title.toLowerCase()
      if (!title || seen.has(key)) continue
      seen.add(key)
      result.push({ title })
      if (result.length >= 3) break
      continue
    }

    const itemRec = asRecord(item)
    if (!itemRec) continue

    const title = normalizeString(itemRec.title ?? itemRec.name, 80)
    if (!title) continue

    const key = title.toLowerCase()
    if (seen.has(key)) continue
    seen.add(key)

    const examples = Array.isArray(itemRec.examples)
      ? itemRec.examples
        .map(example => normalizeString(example, 80))
        .filter(Boolean)
        .slice(0, 2)
      : undefined

    result.push({
      title,
      description: normalizeString(itemRec.description, 160) || undefined,
      examples: examples && examples.length > 0 ? examples : undefined,
    })
    if (result.length >= 3) break
  }

  return result
}

export function extractLatestMaterialRequestedCategories(messages: ChatMessage[]): MaterialRequestedCategory[] {
  for (let i = messages.length - 1; i >= 0; i -= 1) {
    const artifact = messages[i].artifact
    if (artifact?.artifactType !== 'material_collection_progress') continue

    const categories = normalizeMaterialRequestedCategories(artifact.data)
    if (categories.length > 0) return categories
  }

  return []
}

export function buildFallbackMaterialRequestedCategories(
  templateName: string,
  useCases: string[] = [],
): MaterialRequestedCategory[] {
  const normalizedName = normalizeString(templateName, 40) || '目标员工'
  const exampleUseCases = useCases
    .map(item => normalizeString(item, 80))
    .filter(Boolean)
    .slice(0, 2)

  return [
    {
      title: `${normalizedName}历史案例与样例输入`,
      description: '已发生过的真实业务案例、典型输入、期望输出和异常处理记录，用于提炼工作方式与判断标准',
      examples: exampleUseCases.length > 0 ? exampleUseCases : ['历史工单/对话记录', '样例输入输出', '异常复盘材料'],
    },
    {
      title: `${normalizedName}流程规则与边界条件`,
      description: '该岗位需要遵守的业务流程、审批规则、质量标准、不可违反的红线和人工介入条件',
      examples: ['SOP/操作手册', '业务规则清单', '审批与升级规则'],
    },
    {
      title: `${normalizedName}外部数据与系统接口`,
      description: '执行任务时需要读取或写入的系统、字段口径、接口文档、权限范围和数据样例',
      examples: ['系统字段说明', 'API/导出表样例', '权限与账号范围'],
    },
  ]
}
