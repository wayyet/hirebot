/**
 * MaterialCard.tsx - 资料卡组件（文件夹上传 .md/.json）
 * 
 * 用于雇佣流程的资料收集阶段：
 * - 支持文件夹和文件上传
 * - 按分类组织资料
 * - 显示上传进度和状态
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import { FileText, Upload } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import i18n from '@/i18n'

import { api } from '@/infra/api'
import type { ChatFile, MaterialRequestedCategory } from '../hiringPageTypes'
import type {
  PendingStageAdvanceConfirmation,
} from '../stageAdvanceConfirmation'
import {
  buildUploadedCountByCategory,
  countDistinctMaterialUploads,
  listUnmatchedMaterialUploads,
} from '../materialUploadMatching'

// ── 类型定义 ──────────────────────────────────────────────────────────────────

interface UploadedFileMeta {
  materialFileId?: string
  relativePath: string
  originalFileName?: string
  sizeBytes: number
  format: string
  requestedCategoryTitle?: string | null
  workspaceRelativePath?: string | null
}

interface MaterialCategoryCard {
  title: string
  description: string
  formatLabel: string
  contextLabel?: string
  examplesLabel?: string
}

// ── 常量 ──────────────────────────────────────────────────────────────────────

const ALLOWED_EXTS = new Set(['.md', '.json'])

const MATERIAL_FORMAT_HINTS: Array<{ label: string; pattern: RegExp }> = [
  { label: 'PDF', pattern: /\bpdf\b/i },
  { label: 'DOCX', pattern: /\bdocx\b|\bdoc\b|\bword\b/i },
  { label: 'XLSX', pattern: /\bxlsx\b|\bxls\b|\bexcel\b/i },
  { label: 'JSON', pattern: /\bjson\b/i },
  { label: 'MD', pattern: /\bmarkdown\b|\bmd\b/i },
]

const MATERIAL_CONTEXT_HINTS = [
  '知识库',
  '政策',
  '工单',
  'FAQ',
  '流程',
  '规范',
  '话术',
  '表单',
  '模板',
]

// ── 辅助函数 ──────────────────────────────────────────────────────────────────

function fileExt(name: string): string {
  const idx = name.lastIndexOf('.')
  return idx < 0 ? '' : name.slice(idx).toLowerCase()
}

function formatMaterialFileSize(bytes: number | null): string {
  if (!bytes || bytes <= 0) return ''
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1048576).toFixed(1)} MB`
}

function inferMaterialFormatLabel(category: MaterialRequestedCategory): string {
  const haystack = [category.title, category.description, ...(category.examples ?? [])]
    .filter(Boolean)
    .join(' ')

  for (const item of MATERIAL_FORMAT_HINTS) {
    if (item.pattern.test(haystack)) return item.label
  }

  return '资料'
}

function inferMaterialContextLabel(category: MaterialRequestedCategory): string | undefined {
  const haystack = [category.title, category.description, ...(category.examples ?? [])]
    .filter(Boolean)
    .join(' ')

  for (const keyword of MATERIAL_CONTEXT_HINTS) {
    if (haystack.includes(keyword)) return keyword
  }

  return undefined
}

function buildMaterialCategoryCards(requestedCategories: MaterialRequestedCategory[]): MaterialCategoryCard[] {
  return requestedCategories.map(category => ({
    title: category.title,
    description: category.description?.trim() || i18n.t('hiring.todo.material.defaultDescription'),
    formatLabel: inferMaterialFormatLabel(category),
    contextLabel: inferMaterialContextLabel(category),
    examplesLabel: category.examples && category.examples.length > 0
      ? category.examples.slice(0, 2).join(' / ')
      : undefined,
  }))
}

function deriveFolderFromWebkitPath(file: File): string | undefined {
  // <input webkitdirectory> 上传时 webkitRelativePath 形如 "folder/sub/file.md"
  const rel = (file as File & { webkitRelativePath?: string }).webkitRelativePath
  if (!rel) return undefined
  const segs = rel.split('/')
  segs.pop()
  return segs.length > 0 ? segs.join('/') : undefined
}

// ── 组件定义 ──────────────────────────────────────────────────────────────────

interface StageAdvanceConfirmationPanelProps {
  pendingConfirmation: PendingStageAdvanceConfirmation
  busy: boolean
  onContinueCollection?: () => void
  onConfirmAdvance?: () => void
}

function StageAdvanceConfirmationPanel({
  pendingConfirmation,
  busy,
  onContinueCollection,
  onConfirmAdvance,
}: StageAdvanceConfirmationPanelProps) {
  const { t } = useTranslation()
  return (
    <section className="hb-todo-confirmation-panel" aria-label="阶段推进确认">
      <p className="hb-todo-confirmation-text">{pendingConfirmation.prompt}</p>
      <div className="hb-todo-confirmation-actions">
        <button
          type="button"
          className="hb-todo-row-btn is-ghost"
          disabled={busy}
          onClick={onContinueCollection}
        >
          {t('hiring.todo.confirmation.continueCollection')}
        </button>
        <button
          type="button"
          className="hb-todo-row-btn is-primary"
          disabled={busy}
          onClick={onConfirmAdvance}
        >
          {t('hiring.todo.confirmation.confirmAdvance')}
        </button>
      </div>
    </section>
  )
}

export interface MaterialCardBodyProps {
  hireId: string
  sessionId: string
  requestedCategories: MaterialRequestedCategory[]
  uploadedConversationFiles: readonly ChatFile[]
  pendingConfirmation: PendingStageAdvanceConfirmation | null
  stageConfirmationBusy: boolean
  onContinueCollection?: () => void
  onConfirmAdvance?: () => void
  onAfterUpload: (summary: string) => void
}

export function MaterialCardBody({
  hireId,
  sessionId,
  requestedCategories,
  uploadedConversationFiles,
  pendingConfirmation,
  stageConfirmationBusy,
  onContinueCollection,
  onConfirmAdvance,
  onAfterUpload,
}: MaterialCardBodyProps) {
  const folderInputRef = useRef<HTMLInputElement | null>(null)
  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const categoryUploadRef = useRef<string | null>(null)
  const [collapsed, setCollapsed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [uploaded, setUploaded] = useState<UploadedFileMeta[]>([])
  const [uploadingCategoryTitle, setUploadingCategoryTitle] = useState<string | null>(null)
  const [persistedCategories, setPersistedCategories] = useState<MaterialRequestedCategory[]>([])
  const { t } = useTranslation()

  const buildUploadStageSummary = useCallback((
    files: UploadedFileMeta[],
    requestedCategoryTitle?: string | null,
  ) => {
    const total = files.length
    const names = files.map(item => item.originalFileName || item.relativePath)
    const preview = names.slice(0, 5).join('、')
    const suffix = names.length > 5 ? t('hiring.todo.material.uploadSuffix', { count: names.length }) : ''
    const categoryPrefix = requestedCategoryTitle ? t('hiring.todo.material.categoryPrefix', { category: requestedCategoryTitle }) : ''
    const summaryLines = [
      t('hiring.todo.material.uploadSummary', { categoryPrefix, total, preview, suffix }),
    ]

    const sourcePathLines = files
      .slice(0, 8)
      .map(item => {
        const name = item.originalFileName || item.relativePath
        const path = item.workspaceRelativePath?.trim() || item.relativePath
        return t('hiring.todo.material.uploadSourcePathItem', { name, path })
      })

    if (sourcePathLines.length > 0) {
      summaryLines.push(t('hiring.todo.material.uploadSourcePathLead'))
      summaryLines.push(...sourcePathLines)
    }

    return summaryLines.join('\n')
  }, [t])

  const refresh = useCallback(async (): Promise<UploadedFileMeta[]> => {
    if (!hireId || !sessionId) return []
    try {
      const items = await api.hiringWorkflow.listMaterialFiles(hireId, sessionId)
      setUploaded(items)
      return items
    } catch {
      // 列表刷新失败不阻断资料上传主流程。
      return []
    }
  }, [hireId, sessionId])

  useEffect(() => { void refresh() }, [refresh])

  // 合并外部传入的分类到本地持久化列表，避免后续无 requested_categories 的 artifact 导致卡片消失
  const displayCategories = useMemo(() => {
    if (requestedCategories.length === 0) return persistedCategories
    const existingKeys = new Set(persistedCategories.map(c => c.title))
    const additions = requestedCategories.filter(c => !existingKeys.has(c.title))
    if (additions.length === 0) return persistedCategories
    return [...persistedCategories, ...additions]
  }, [persistedCategories, requestedCategories])

  useEffect(() => {
    if (requestedCategories.length === 0) return
    setPersistedCategories(prev => {
      const existingKeys = new Set(prev.map(c => c.title))
      const additions = requestedCategories.filter(c => !existingKeys.has(c.title))
      return additions.length === 0 ? prev : [...prev, ...additions]
    })
  }, [requestedCategories])

  const materialCards = useMemo(
    () => buildMaterialCategoryCards(displayCategories),
    [displayCategories],
  )

  const handleFiles = useCallback(async (files: FileList | File[], requestedCategoryTitle?: string | null) => {
    if (!hireId || !sessionId) {
      setError(t('hiring.todo.material.errorNotReady'))
      return
    }

    const arr = Array.from(files)
    if (arr.length === 0) return

    const invalid = arr.filter(file => !ALLOWED_EXTS.has(fileExt(file.name)))
    if (invalid.length > 0) {
      const preview = invalid.slice(0, 3).map(file => file.name).join('、')
      const more = invalid.length > 3 ? t('hiring.todo.material.errorInvalidExtMore') : ''
      setError(t('hiring.todo.material.errorInvalidExt', { preview }) + more)
      return
    }

    setError('')
    setBusy(true)
    setUploadingCategoryTitle(requestedCategoryTitle ?? null)

    try {
      const groups = new Map<string, File[]>()
      for (const file of arr) {
        const folder = deriveFolderFromWebkitPath(file) ?? ''
        const list = groups.get(folder) ?? []
        list.push(file)
        groups.set(folder, list)
      }

      for (const [folder, filesInFolder] of groups.entries()) {
        await api.hiringWorkflow.uploadMaterialFiles(hireId, sessionId, filesInFolder, {
          folder: folder || undefined,
          requestedCategoryTitle: requestedCategoryTitle || undefined,
        })
      }

      // 基于全量已上传文件构建摘要，避免多次上传时只发送最后一批的信息给 AI
      const allFiles = await refresh()
      onAfterUpload(buildUploadStageSummary(allFiles, requestedCategoryTitle))
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : t('hiring.todo.material.errorUploadFailed'))
    } finally {
      setBusy(false)
      setUploadingCategoryTitle(null)
    }
  }, [hireId, sessionId, refresh, onAfterUpload, buildUploadStageSummary, t])

  const uploadedCountByCategory = useMemo(
    () => buildUploadedCountByCategory(displayCategories, uploaded, uploadedConversationFiles),
    [displayCategories, uploaded, uploadedConversationFiles],
  )
  const totalUploadedCount = useMemo(
    () => countDistinctMaterialUploads(uploaded, uploadedConversationFiles),
    [uploaded, uploadedConversationFiles],
  )
  const unmatchedUploads = useMemo(
    () => listUnmatchedMaterialUploads(displayCategories, uploaded, uploadedConversationFiles),
    [displayCategories, uploaded, uploadedConversationFiles],
  )
  const completedCardCount = materialCards.reduce(
    (count, item) => count + ((uploadedCountByCategory.get(item.title) ?? 0) > 0 ? 1 : 0),
    0,
  )
  const shellStatusLabel = materialCards.length > 0
    ? completedCardCount >= materialCards.length
      ? t('hiring.todo.material.statusUploaded', { count: completedCardCount })
      : t('hiring.todo.material.statusPendingCount', { count: materialCards.length - completedCardCount })
    : totalUploadedCount > 0
      ? t('hiring.todo.material.statusUploaded', { count: totalUploadedCount })
    : t('hiring.todo.material.statusPending')
  return (
    <div className="hb-todo-mat">
      <div
        className={clsx(
          'hb-todo-material-shell',
          busy && 'is-busy',
          totalUploadedCount > 0 && 'is-filled',
          collapsed && 'is-collapsed',
        )}
        onDragOver={event => { event.preventDefault() }}
        onDrop={event => {
          event.preventDefault()
          if (busy) return
          if (event.dataTransfer.files?.length) void handleFiles(event.dataTransfer.files)
        }}
      >
        <button
          type="button"
          className="hb-todo-material-head"
          aria-expanded={!collapsed}
          onClick={() => setCollapsed(prev => !prev)}
        >
          <div className="hb-todo-material-head-copy">
            <div className="hb-todo-material-head-title-row">
              <span className="hb-todo-stage-num">1</span>
              <span className="hb-todo-stage-title">{t('hiring.todo.material.title')}</span>
            </div>
          </div>
          <div className="hb-todo-material-head-actions">
            <span className="hb-todo-material-head-pill">{shellStatusLabel}</span>
            <span className={clsx('hb-todo-material-chevron', collapsed && 'is-collapsed')} aria-hidden="true">
              ▾
            </span>
          </div>
        </button>

        {!collapsed && materialCards.length > 0 ? (
          <div className="hb-todo-category-list" aria-label="建议优先上传的资料分类">
            {materialCards.map(card => {
              const uploadedCount = uploadedCountByCategory.get(card.title) ?? 0
              const isUploadingCurrentCard = busy && uploadingCategoryTitle === card.title

              return (
                <div key={card.title} className="hb-todo-category-item">
                  <div className="hb-todo-category-icon" aria-hidden="true">
                    <FileText size={18} strokeWidth={2.1} />
                  </div>
                  <div className="hb-todo-category-copy">
                    <div className="hb-todo-category-title-row">
                      <strong title={card.title}>{card.title}</strong>
                      <div className="hb-todo-category-chips">
                        <span className="hb-todo-category-chip is-format">{card.formatLabel}</span>
                      </div>
                    </div>
                    {(isUploadingCurrentCard || uploadedCount > 0) ? (
                      <em className={clsx('hb-todo-category-status', uploadedCount > 0 && 'is-complete', isUploadingCurrentCard && 'is-busy')}>
                        {isUploadingCurrentCard ? t('hiring.todo.material.uploading') : t('hiring.todo.material.uploadedCount', { count: uploadedCount })}
                      </em>
                    ) : null}
                  </div>
                  <div className="hb-todo-category-actions">
                    <button
                      type="button"
                      className="hb-todo-row-btn is-primary"
                      disabled={busy}
                      onClick={() => {
                        categoryUploadRef.current = card.title
                        fileInputRef.current?.click()
                      }}
                    >
                      <Upload size={14} strokeWidth={2.1} />
                      {t('hiring.todo.material.uploadBtn')}
                    </button>
                  </div>
                </div>
              )
            })}
          </div>
        ) : null}

        {!collapsed && busy ? (
          <div className="hb-todo-upload-sync" aria-live="polite">
            <span className="hb-todo-upload-sync-dot" />
            {t('hiring.todo.material.syncing')}
          </div>
        ) : null}

        {!collapsed && unmatchedUploads.length > 0 ? (
          <div className="hb-todo-category-list" aria-label="已上传但未匹配建议分类的资料">
            <div className="hb-todo-category-item">
              <div className="hb-todo-category-icon" aria-hidden="true">
                <FileText size={18} strokeWidth={2.1} />
              </div>
              <div className="hb-todo-category-copy">
                <div className="hb-todo-category-title-row">
                  {t('hiring.todo.material.unmatchedTitle')}
                  <div className="hb-todo-category-chips">
                    <span className="hb-todo-category-chip">{t('hiring.todo.material.unmatchedCount', { count: unmatchedUploads.length })}</span>
                  </div>
                </div>
                <div className="flex flex-wrap gap-2 pt-2">
                  {unmatchedUploads.map(file => (
                    <span key={file.key} className="hb-todo-category-chip">
                      {file.name}
                      {file.sizeBytes ? ` · ${formatMaterialFileSize(file.sizeBytes)}` : ''}
                    </span>
                  ))}
                </div>
              </div>
            </div>
          </div>
        ) : null}

        <input
          ref={folderInputRef}
          type="file"
          hidden
          multiple
          // @ts-expect-error webkitdirectory 为浏览器扩展属性，React 类型未声明。
          webkitdirectory=""
          directory=""
          accept=".md,.json,application/json,text/markdown"
          onChange={event => {
            if (event.target.files) void handleFiles(event.target.files)
            event.target.value = ''
          }}
        />
        <input
          ref={fileInputRef}
          type="file"
          hidden
          multiple
          accept=".md,.json,application/json,text/markdown"
          onChange={event => {
            if (event.target.files) void handleFiles(event.target.files, categoryUploadRef.current)
            categoryUploadRef.current = null
            event.target.value = ''
          }}
        />
      </div>

      {pendingConfirmation && (
        <StageAdvanceConfirmationPanel
          pendingConfirmation={pendingConfirmation}
          busy={stageConfirmationBusy}
          onContinueCollection={onContinueCollection}
          onConfirmAdvance={onConfirmAdvance}
        />
      )}

      {error && <p className="hb-todo-error">{error}</p>}
    </div>
  )
}
