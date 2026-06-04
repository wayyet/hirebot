import i18n from '@/i18n'
import { HiringCollectionStage } from '@/infra/api'
import type { HiringCollectionStageType, HiringConversationMaterial } from '@/infra/api'
import { mkId } from './hiringPageHelpers'
import type { ChatFile } from '../hiringPageTypes'

export const EXTERNAL_CONFIG_REPACKAGE_NOTICE = '外部系统配置已更新，旧产物包已失效。请重新生成实例包后再继续导入。'

export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const anchor = document.createElement('a')
  anchor.href = url
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(url)
}

const MAX_MATERIAL_CHARS = 120_000

export async function fileToChatFile(file: File, type: 'file' | 'skill' = 'file', metadata?: Record<string, string>): Promise<ChatFile> {
  const content = type === 'file' ? await readFileText(file) : undefined
  return {
    id: mkId(),
    name: file.name,
    size: file.size,
    status: i18n.t('hiring.file.parsed') as '已解析',
    type,
    mimeType: file.type || undefined,
    content,
    metadata,
    rawFile: file,
  }
}

export function readFileText(file: File): Promise<string | undefined> {
  if (file.size > MAX_MATERIAL_CHARS * 4) {
    return Promise.resolve(i18n.t('hiring.file.tooLarge', { name: file.name, size: file.size }))
  }

  return new Promise(resolve => {
    const reader = new FileReader()
    reader.onload = () => {
      const value = typeof reader.result === 'string' ? reader.result : undefined
      resolve(value && value.length > MAX_MATERIAL_CHARS ? `${value.slice(0, MAX_MATERIAL_CHARS)}\n...[truncated]` : value)
    }
    reader.onerror = () => resolve(i18n.t('hiring.file.readFailed', { name: file.name }))
    reader.readAsText(file)
  })
}

export function toConversationMaterials(files?: ChatFile[]): HiringConversationMaterial[] | undefined {
  if (!files?.length) return undefined

  return files.map(file => ({
    type: file.type ?? 'file',
    name: file.name,
    content: file.content,
    size: file.size,
    mimeType: file.mimeType,
    metadata: {
      status: file.status,
      ...(file.metadata ?? {}),
    },
  }))
}

export function normalizeCollectionStage(value: string): HiringCollectionStageType {
  if (value === HiringCollectionStage.Material) return HiringCollectionStage.Material
  if (value === HiringCollectionStage.Skill) return HiringCollectionStage.Skill
  if (value === HiringCollectionStage.External) return HiringCollectionStage.External
  if (value === HiringCollectionStage.ReadyForPackaging) return HiringCollectionStage.ReadyForPackaging
  return HiringCollectionStage.Material
}

export function formatFileSize(bytes: number) {
  return bytes < 1048576 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1048576).toFixed(1)} MB`
}
