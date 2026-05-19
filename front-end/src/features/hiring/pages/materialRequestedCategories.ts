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
