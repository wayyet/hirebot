import type { ChatFile, ChatMessage, MaterialRequestedCategory } from './hiringPageTypes'

interface MaterialUploadCandidate {
  key?: string
  name?: string
  relativePath?: string
  originalFileName?: string
  sizeBytes?: number | null
  requestedCategoryTitle?: string | null
}

export interface UnmatchedMaterialUpload {
  key: string
  name: string
  sizeBytes: number | null
}

function getCandidateName(candidate: MaterialUploadCandidate) {
  return candidate.name || candidate.originalFileName || candidate.relativePath || ''
}

function stripExtension(fileName: string) {
  const dotIndex = fileName.lastIndexOf('.')
  return dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName
}

function normalizeForMatch(value: string) {
  return value
    .trim()
    .toLowerCase()
    .replace(/\.[a-z0-9]{1,8}$/i, '')
    .replace(/[\s_\-./\\()[\]{}<>|,:;'"`~!@#$%^&*+=?，。；：、】【（）《》、]+/g, '')
}

function buildUploadKey(candidate: MaterialUploadCandidate) {
  if (candidate.key?.trim()) return candidate.key.trim()
  const normalizedName = normalizeForMatch(getCandidateName(candidate))
  const sizePart = candidate.sizeBytes ?? ''
  return `${normalizedName}::${sizePart}`
}

function matchesRequestedCategory(fileName: string, category: MaterialRequestedCategory) {
  const normalizedName = normalizeForMatch(stripExtension(fileName))
  if (!normalizedName) return false

  const candidates = [category.title, ...(category.examples ?? [])]
    .map(normalizeForMatch)
    .filter(Boolean)

  return candidates.some(candidate =>
    normalizedName.includes(candidate) || candidate.includes(normalizedName),
  )
}

export function extractConversationMaterialFiles(messages: ChatMessage[]): ChatFile[] {
  const files: ChatFile[] = []
  const seen = new Set<string>()

  for (const message of messages) {
    if (!message.files || message.files.length === 0) continue

    for (const file of message.files) {
      if (file.type === 'skill') continue

      const key = `${normalizeForMatch(file.name)}::${file.size}`
      if (seen.has(key)) continue

      seen.add(key)
      files.push(file)
    }
  }

  return files
}

export function countDistinctMaterialUploads(
  persistedFiles: ReadonlyArray<MaterialUploadCandidate>,
  conversationFiles: ReadonlyArray<ChatFile>,
) {
  const seen = new Set<string>()

  for (const file of persistedFiles) {
    seen.add(buildUploadKey(file))
  }

  for (const file of conversationFiles) {
    seen.add(buildUploadKey({
      name: file.name,
      sizeBytes: file.size,
    }))
  }

  return seen.size
}

export function buildUploadedCountByCategory(
  categories: ReadonlyArray<MaterialRequestedCategory>,
  persistedFiles: ReadonlyArray<MaterialUploadCandidate>,
  conversationFiles: ReadonlyArray<ChatFile>,
) {
  const result = new Map<string, number>()
  const seen = new Set<string>()

  const addCount = (title: string) => {
    result.set(title, (result.get(title) ?? 0) + 1)
  }

  const matchCategory = (fileName: string) =>
    categories.find(category => matchesRequestedCategory(fileName, category))?.title

  for (const file of persistedFiles) {
    const key = buildUploadKey(file)
    if (seen.has(key)) continue
    seen.add(key)

    const explicitTitle = file.requestedCategoryTitle?.trim()
    const matchedTitle = explicitTitle || matchCategory(getCandidateName(file))
    if (matchedTitle) {
      addCount(matchedTitle)
    }
  }

  for (const file of conversationFiles) {
    const key = buildUploadKey({
      name: file.name,
      sizeBytes: file.size,
    })
    if (seen.has(key)) continue
    seen.add(key)

    const matchedTitle = matchCategory(file.name)
    if (matchedTitle) {
      addCount(matchedTitle)
    }
  }

  return result
}

export function listUnmatchedMaterialUploads(
  categories: ReadonlyArray<MaterialRequestedCategory>,
  persistedFiles: ReadonlyArray<MaterialUploadCandidate>,
  conversationFiles: ReadonlyArray<ChatFile>,
) {
  const result: UnmatchedMaterialUpload[] = []
  const seen = new Set<string>()

  const matchCategory = (fileName: string) =>
    categories.find(category => matchesRequestedCategory(fileName, category))?.title

  for (const file of persistedFiles) {
    const key = buildUploadKey(file)
    if (seen.has(key)) continue
    seen.add(key)

    const explicitTitle = file.requestedCategoryTitle?.trim()
    const matchedTitle = explicitTitle || matchCategory(getCandidateName(file))
    if (matchedTitle) continue

    result.push({
      key,
      name: getCandidateName(file),
      sizeBytes: file.sizeBytes ?? null,
    })
  }

  for (const file of conversationFiles) {
    const key = buildUploadKey({
      name: file.name,
      sizeBytes: file.size,
    })
    if (seen.has(key)) continue
    seen.add(key)

    const matchedTitle = matchCategory(file.name)
    if (matchedTitle) continue

    result.push({
      key,
      name: file.name,
      sizeBytes: file.size,
    })
  }

  return result
}
