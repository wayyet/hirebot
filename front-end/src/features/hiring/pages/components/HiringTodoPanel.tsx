/**
 * HiringTodoPanel — 雇佣 TODO 交互面板（artifact 驱动版）
 *
 * 设计要点：
 * - 不再依赖 HandoffItem 列表，3 个阶段卡片始终常驻渲染。
 * - 阶段亮灯/完成态完全由 `wsStageOverrides`（artifact / skill_stage_gate WS 事件聚合）控制。
 * - 资料卡：仅接受 .md / .json 的文件夹/文件上传，落盘到 wwwroot/resources/todo-files/{sessionId}/{folder?}/。
 * - 技能卡：调用内部 Skills Catalog 搜索并关联，外部系统配置作为可选项。
 * - 上传完成后通过 onAfterStageMessage 回调，模拟用户消息驱动 AI 推进下一阶段。
 * - 含 200% 缩放（Surface Pro 8 类窄屏）适配。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import { FileText, Upload } from 'lucide-react'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import i18n from '@/i18n'

import { api, HiringCollectionStage } from '@/infra/api'
import type { EmployeeTemplatePackageSkill, HiringCollectionStageType, StoreSkillItem } from '@/infra/api'
import type {
  ChatFile,
  DefinedSkillItem,
  DownstreamRunState,
  MaterialRequestedCategory,
} from '../hiringPageTypes'
import {
  buildUploadedCountByCategory,
  countDistinctMaterialUploads,
  listUnmatchedMaterialUploads,
} from '../materialUploadMatching'

// ── 类型 ──────────────────────────────────────────────────────────────────────

export type StageStatus = 'running' | 'completed' | 'failed'
export type StageKey = HiringCollectionStageType

interface UploadedFileMeta {
  materialFileId?: string
  relativePath: string
  originalFileName?: string
  sizeBytes: number
  format: string
  requestedCategoryTitle?: string | null
}

interface LinkedSkill {
  skillId: string
  name: string
  version: string
}

export interface HiringTodoPanelProps {
  hireId: string
  /** 当前雇佣会话 ID（用于上传/解析 todo 文件） */
  sessionId: string
  /** WS 阶段覆盖状态：由 HiringPage 聚合 artifact / skill_stage_gate 事件得到 */
  wsStageOverrides: Map<StageKey, StageStatus>
  /** 上传完成后回调（模拟用户消息驱动 AI 进入下一阶段） */
  onAfterStageMessage?: (stage: StageKey, summary: string) => void
  /** 触发生成实例包 */
  onGenerate?: () => void
  generated?: boolean
  /** 用户关联的 store skill UUID 列表变化时回调；用于在导入产物包时一并提交给后端。 */
  onLinkedSkillIdsChange?: (skillIds: string[]) => void
  templatePackageSkills?: EmployeeTemplatePackageSkill[]
  requestedMaterialCategories?: MaterialRequestedCategory[]
  uploadedConversationFiles?: ChatFile[]
  skillDefinitionStageStatus?: StageStatus | null
  skillGenerationState?: DownstreamRunState | null
  definedSkills?: DefinedSkillItem[]
}

interface StageConfig {
  key: StageKey
  num: string
  title: string
  hint: string
}

interface MaterialCategoryCard {
  title: string
  description: string
  formatLabel: string
  contextLabel?: string
  examplesLabel?: string
}

const EMPTY_STORE_SKILL_LIST = {
  page: 1,
  pageSize: 3,
  total: 0,
  items: [] as StoreSkillItem[],
}

const STAGES: StageConfig[] = [
  { key: HiringCollectionStage.Material, num: '1', title: i18n.t('hiring.todo.stage.material'), hint: i18n.t('hiring.todo.stage.materialHint') },
  { key: HiringCollectionStage.Skill,    num: '2', title: i18n.t('hiring.todo.stage.skill'), hint: '' },
  { key: HiringCollectionStage.External, num: '3', title: i18n.t('hiring.todo.stage.external'), hint: i18n.t('hiring.todo.stage.externalHint') },
]

// ── 工具方法 ─────────────────────────────────────────────────────────────────

const ALLOWED_EXTS = new Set(['.md', '.json'])
const MATERIAL_FORMAT_HINTS: Array<{ label: string; pattern: RegExp }> = [
  { label: 'PDF', pattern: /\bpdf\b/i },
  { label: 'DOCX', pattern: /\bdocx\b|\bdoc\b|\bword\b/i },
  { label: 'XLSX', pattern: /\bxlsx\b|\bxls\b|\bexcel\b/i },
  { label: 'JSON', pattern: /\bjson\b/i },
  { label: 'MD', pattern: /\bmarkdown\b|\bmd\b/i },
]
const MATERIAL_CONTEXT_HINTS = [
  '\u77e5\u8bc6\u5e93',
  '\u653f\u7b56',
  '\u5de5\u5355',
  'FAQ',
  '\u6d41\u7a0b',
  '\u89c4\u8303',
  '\u8bdd\u672f',
  '\u8868\u5355',
  '\u6a21\u677f',
]

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

function readArtifactNumber(data: unknown, key: string): number | null {
  if (!data || typeof data !== 'object') return null
  const value = (data as Record<string, unknown>)[key]
  return typeof value === 'number' && Number.isFinite(value) ? value : null
}

function getSkillDefinitionStatusMeta(status: StageStatus | null): { label: string; tone: string } {
  if (status === 'completed') return { label: i18n.t('hiring.todo.status.confirmed'), tone: 'is-completed' }
  if (status === 'running') return { label: i18n.t('hiring.todo.status.collecting'), tone: 'is-running' }
  if (status === 'failed') return { label: i18n.t('hiring.todo.status.failed'), tone: 'is-failed' }
  return { label: i18n.t('hiring.todo.status.waiting'), tone: 'is-idle' }
}

function getSkillImplementationMeta(run: DownstreamRunState | null): {
  label: string
  tone: string
  description: string
} {
  if (!run) {
    return {
      label: i18n.t('hiring.todo.status.notStarted'),
      tone: 'is-idle',
      description: i18n.t('hiring.todo.skill.notStartedDesc'),
    }
  }

  if (run.status === 'waiting_confirm') {
    return {
      label: i18n.t('hiring.todo.status.pendingConfirm'),
      tone: 'is-waiting',
      description: i18n.t('hiring.todo.skill.pendingConfirmDesc'),
    }
  }

  if (run.status === 'running') {
    const total = readArtifactNumber(run.data, 'total_skills')
    const completed = readArtifactNumber(run.data, 'completed_skills')
    if (total !== null && completed !== null) {
      return {
        label: i18n.t('hiring.todo.status.generating'),
        tone: 'is-running',
        description: i18n.t('hiring.todo.skill.generationProgress', { completed, total }),
      }
    }

    return {
      label: i18n.t('hiring.todo.status.generating'),
      tone: 'is-running',
      description: run.label ?? i18n.t('hiring.todo.skill.generatingDesc'),
    }
  }

  if (run.status === 'completed') {
    const total = readArtifactNumber(run.data, 'total_skills')
    const generated = readArtifactNumber(run.data, 'generated_count')
    if (total !== null && generated !== null) {
      return {
        label: i18n.t('hiring.todo.status.completed'),
        tone: 'is-completed',
        description: i18n.t('hiring.todo.skill.generationComplete', { total, generated }),
      }
    }

    return {
      label: i18n.t('hiring.todo.status.completed'),
      tone: 'is-completed',
      description: i18n.t('hiring.todo.skill.generationCompleteDesc'),
    }
  }

  return {
    label: i18n.t('hiring.todo.status.failed'),
    tone: 'is-failed',
    description: run.label ?? i18n.t('hiring.todo.skill.generationFailed'),
  }
}

function getDefinedSkillGenerationMeta(
  skill: DefinedSkillItem,
  run: DownstreamRunState | null,
): { label: string; tone: string } {
  if (!run) return { label: i18n.t('hiring.todo.status.notStarted'), tone: 'is-idle' }
  if (run.status === 'waiting_confirm') return { label: i18n.t('hiring.todo.status.pendingConfirm'), tone: 'is-waiting' }
  if (run.status === 'running') return { label: i18n.t('hiring.todo.status.generating'), tone: 'is-running' }
  if (run.status === 'completed') {
    if (skill.generationAction && skill.generationAction !== 'generate_new') {
      return { label: i18n.t('hiring.todo.skill.reused'), tone: 'is-completed' }
    }

    return { label: i18n.t('hiring.todo.skill.generated'), tone: 'is-completed' }
  }

  return { label: i18n.t('hiring.todo.status.failed'), tone: 'is-failed' }
}

function deriveFolderFromWebkitPath(file: File): string | undefined {
  // <input webkitdirectory> 上传时 webkitRelativePath 形如 "folder/sub/file.md"
  const rel = (file as File & { webkitRelativePath?: string }).webkitRelativePath
  if (!rel) return undefined
  const segs = rel.split('/')
  segs.pop()
  return segs.length > 0 ? segs.join('/') : undefined
}

// ── 主组件 ────────────────────────────────────────────────────────────────────

function inferMaterialFormatLabel(category: MaterialRequestedCategory): string {
  const haystack = [category.title, category.description, ...(category.examples ?? [])]
    .filter(Boolean)
    .join(' ')

  for (const item of MATERIAL_FORMAT_HINTS) {
    if (item.pattern.test(haystack)) return item.label
  }

  return '\u8d44\u6599'
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
export function HiringTodoPanel({
  hireId,
  sessionId,
  wsStageOverrides,
  onAfterStageMessage,
  onGenerate,
  generated = false,
  onLinkedSkillIdsChange,
  templatePackageSkills = [],
  requestedMaterialCategories = [],
  uploadedConversationFiles = [],
  skillDefinitionStageStatus = null,
  skillGenerationState = null,
  definedSkills = [],
}: HiringTodoPanelProps) {
  const { t } = useTranslation()
  // 用户是否手动覆盖了某张卡片的展开状态；未手动覆盖的走"活跃阶段自动展开"逻辑
  const [userToggled, setUserToggled] = useState<Record<string, boolean>>({})
  const [manualExpanded, setManualExpanded] = useState<Record<string, boolean>>({})
  const [toggleScopeKey, setToggleScopeKey] = useState<StageKey | 'final' | null>(null)

  const allDone = useMemo(
    () => STAGES.every(s => wsStageOverrides.get(s.key) === 'completed'),
    [wsStageOverrides],
  )

  // 当前活跃阶段：优先取 running，没有 running 时取第一个未完成的阶段；全部完成则无活跃阶段
  const activeStageKey = useMemo<StageKey | 'final' | null>(() => {
    const running = STAGES.find(s => wsStageOverrides.get(s.key) === 'running')
    if (running) return running.key
    if (allDone) return 'final'
    const pending = STAGES.find(s => wsStageOverrides.get(s.key) !== 'completed')
    return pending?.key ?? null
  }, [wsStageOverrides, allDone])

  const scopedUserToggled = useMemo(
    () => (toggleScopeKey === activeStageKey ? userToggled : {}),
    [toggleScopeKey, activeStageKey, userToggled],
  )
  const scopedManualExpanded = useMemo(
    () => (toggleScopeKey === activeStageKey ? manualExpanded : {}),
    [toggleScopeKey, activeStageKey, manualExpanded],
  )

  // 计算每张卡片的展开态：用户手动覆盖优先，否则只展开活跃阶段
  const isExpanded = useCallback((key: string) => {
    if (scopedUserToggled[key]) return scopedManualExpanded[key]
    return key === activeStageKey
  }, [scopedUserToggled, scopedManualExpanded, activeStageKey])

  const toggle = (key: string) => {
    const shouldResetScope = toggleScopeKey !== activeStageKey
    const nextExpanded = !isExpanded(key)

    setToggleScopeKey(activeStageKey)
    setUserToggled(prev => shouldResetScope ? { [key]: true } : { ...prev, [key]: true })
    setManualExpanded(prev => shouldResetScope ? { [key]: nextExpanded } : { ...prev, [key]: nextExpanded })
  }

  return (
    <div className="hb-todo-panel">
      <div className="hb-todo-panel-head hb-todo-panel-head--compact">
        <h3 className="hb-todo-panel-title">{t('hiring.todo.panelTitle')}</h3>
      </div>

      <div className="hb-todo-panel-body">
        <div className="hb-todo-material-section">
          <MaterialCardBody
            hireId={hireId}
            sessionId={sessionId}
            requestedCategories={requestedMaterialCategories}
            uploadedConversationFiles={uploadedConversationFiles}
            onAfterUpload={summary => onAfterStageMessage?.(HiringCollectionStage.Material, summary)}
          />
        </div>

        {STAGES.filter(stage => stage.key !== HiringCollectionStage.Material).map(stage => (
          <StageCard
            key={stage.key}
            stage={stage}
            status={wsStageOverrides.get(stage.key) ?? null}
            expanded={isExpanded(stage.key)}
            isFocus={activeStageKey === stage.key}
            onToggle={() => toggle(stage.key)}
          >
            {stage.key === HiringCollectionStage.Skill && (
              <SkillCardBody
                templatePackageSkills={templatePackageSkills}
                onAfterLink={summary => onAfterStageMessage?.(HiringCollectionStage.Skill, summary)}
                onLinkedIdsChange={onLinkedSkillIdsChange}
                definitionStageStatus={skillDefinitionStageStatus}
                skillGenerationState={skillGenerationState}
                definedSkills={definedSkills}
              />
            )}
            {stage.key === HiringCollectionStage.External && (
              <ExternalCardBody
                onAfterSave={summary => onAfterStageMessage?.(HiringCollectionStage.External, summary)}
              />
            )}
          </StageCard>
        ))}

        <FinalCard
          canGenerate={allDone}
          generated={generated}
          expanded={isExpanded('final')}
          isFocus={activeStageKey === 'final'}
          onToggle={() => toggle('final')}
          onGenerate={onGenerate}
        />
      </div>
    </div>
  )
}

// ── 阶段卡片外壳 ──────────────────────────────────────────────────────────────

function StageCard({
  stage, status, expanded, isFocus, onToggle, children,
}: {
  stage: StageConfig
  status: StageStatus | null
  expanded: boolean
  isFocus: boolean
  onToggle: () => void
  children: React.ReactNode
}) {
  const { t } = useTranslation()
  const isComplete = status === 'completed'
  const isActive = status === 'running'
  const isFailed = status === 'failed'
  return (
    <div className={clsx(
      'hb-todo-stage-card',
      (stage.key === HiringCollectionStage.Skill || stage.key === HiringCollectionStage.External) && 'is-accent-shell',
      isComplete && 'is-complete',
      isActive && 'is-active',
      isFailed && 'is-failed',
      // 当前关注的阶段卡片：占据右侧剩余高度，让里面的上传/搜索/表单区域尽量铺开
      isFocus && expanded && 'is-focus',
    )}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">{stage.num}</span>
        <span className="hb-todo-stage-title">{stage.title}</span>
        <span className={clsx('hb-todo-stage-badge', isComplete ? 'is-complete' : isActive ? 'is-active' : isFailed ? 'is-failed' : '')}>
          {isComplete ? t('hiring.todo.status.completed') : isActive ? t('hiring.todo.status.inProgress') : isFailed ? t('hiring.todo.status.failed') : t('hiring.todo.status.waiting')}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className="hb-todo-stage-body">
          {stage.hint && <p className="hb-todo-stage-hint">{stage.hint}</p>}
          {children}
        </div>
      )}
    </div>
  )
}

// ── 资料卡（文件夹上传 .md/.json） ─────────────────────────────────────────────

function MaterialCardBody({
  hireId, sessionId, requestedCategories, uploadedConversationFiles, onAfterUpload,
}: {
  hireId: string
  sessionId: string
  requestedCategories: MaterialRequestedCategory[]
  uploadedConversationFiles: ChatFile[]
  onAfterUpload: (summary: string) => void
}) {
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

  const refresh = useCallback(async () => {
    if (!hireId || !sessionId) return
    try {
      const items = await api.hiringWorkflow.listMaterialFiles(hireId, sessionId)
      setUploaded(items)
    } catch {
      // 列表刷新失败不阻断资料上传主流程。
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

      let total = 0
      const names: string[] = []
      for (const [folder, filesInFolder] of groups.entries()) {
        const saved = await api.hiringWorkflow.uploadMaterialFiles(hireId, sessionId, filesInFolder, {
          folder: folder || undefined,
          requestedCategoryTitle: requestedCategoryTitle || undefined,
        })
        total += saved.length
        names.push(...saved.map(item => item.relativePath))
      }

      await refresh()
      const preview = names.slice(0, 5).join('、')
      const suffix = names.length > 5 ? t('hiring.todo.material.uploadSuffix', { count: names.length }) : ''
      const categoryPrefix = requestedCategoryTitle ? t('hiring.todo.material.categoryPrefix', { category: requestedCategoryTitle }) : ''
      onAfterUpload(t('hiring.todo.material.uploadSummary', { categoryPrefix, total, preview, suffix }))
    } catch (uploadError) {
      setError(uploadError instanceof Error ? uploadError.message : t('hiring.todo.material.errorUploadFailed'))
    } finally {
      setBusy(false)
      setUploadingCategoryTitle(null)
    }
  }, [hireId, sessionId, refresh, onAfterUpload])

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
              {t('hiring.todo.material.title')}
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

      {error && <p className="hb-todo-error">{error}</p>}
    </div>
  )
}

function SkillCardBody({
  templatePackageSkills,
  onAfterLink,
  onLinkedIdsChange,
  definitionStageStatus,
  skillGenerationState,
  definedSkills,
}: {
  templatePackageSkills: EmployeeTemplatePackageSkill[]
  onAfterLink: (summary: string) => void
  onLinkedIdsChange?: (skillIds: string[]) => void
  definitionStageStatus: StageStatus | null
  skillGenerationState: DownstreamRunState | null
  definedSkills: DefinedSkillItem[]
}) {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [searchResults, setSearchResults] = useState<StoreSkillItem[]>([])
  const [searchTotal, setSearchTotal] = useState(0)
  const [linked, setLinked] = useState<LinkedSkill[]>([])
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState('')
  const trimmedQuery = query.trim()

  const {
    data: defaultSkillData = EMPTY_STORE_SKILL_LIST,
    isLoading: isDefaultLoading,
    error: defaultSkillError,
  } = useQuery({
    queryKey: ['hiring-default-store-skills'],
    queryFn: ({ signal }) => api.skillCatalog.searchStoreSkills({ page: 1, pageSize: 3 }, signal),
    enabled: trimmedQuery.length === 0,
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  })

  // 每次 linked 变更都向父组件同步 store skill UUID 列表，供 import-package 请求使用
  useEffect(() => {
    onLinkedIdsChange?.(linked.map(l => l.skillId))
  }, [linked, onLinkedIdsChange])

  // 防抖搜索：对接模板池同源接口 /api/store/skills?page=1&pageSize=12&q=...
  useEffect(() => {
    if (!trimmedQuery) {
      setSearchResults([])
      setSearchTotal(0)
      setSearchError('')
      setSearching(false)
      return
    }

    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      setSearching(true)
      try {
        const data = await api.skillCatalog.searchStoreSkills(
          { q: trimmedQuery, page: 1, pageSize: 12 },
          controller.signal,
        )
        setSearchResults(data?.items ?? [])
        setSearchTotal(data?.total ?? data?.items?.length ?? 0)
        setSearchError('')
      } catch (e) {
        if ((e as { name?: string })?.name === 'AbortError') return
        setSearchError(e instanceof Error ? e.message : t('hiring.todo.skill.errorSearchFailed'))
      } finally {
        setSearching(false)
      }
    }, 300)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [trimmedQuery])

  const isLinked = useCallback((id: string) => linked.some(l => l.skillId === id), [linked])
  const currentResults = trimmedQuery ? searchResults : defaultSkillData.items
  const currentTotal = trimmedQuery ? searchTotal : (defaultSkillData.total ?? defaultSkillData.items.length)
  const currentSearching = trimmedQuery ? searching : isDefaultLoading
  const currentError = trimmedQuery
    ? searchError
    : defaultSkillError instanceof Error
      ? defaultSkillError.message
      : defaultSkillError
        ? t('hiring.todo.skill.errorSearchFailed')
        : ''

  const searchStatusLabel = currentSearching
    ? t('hiring.todo.skill.statusSearching')
    : linked.length > 0
      ? t('hiring.todo.skill.statusLinkedCount', { count: linked.length })
      : !trimmedQuery
        ? t('hiring.todo.skill.statusDefaultCount')
        : currentResults.length > 0
          ? t('hiring.todo.skill.statusResultCount', { count: currentResults.length })
      : t('hiring.todo.skill.statusPending')
  const definitionMeta = useMemo(
    () => getSkillDefinitionStatusMeta(definitionStageStatus),
    [definitionStageStatus],
  )
  const implementationMeta = useMemo(
    () => getSkillImplementationMeta(skillGenerationState),
    [skillGenerationState],
  )

  function handleLink(s: StoreSkillItem) {
    if (isLinked(s.id)) return
    const next = [...linked, { skillId: s.id, name: s.displayName ?? s.name, version: s.currentVersion ?? '' }]
    setLinked(next)
    const names = next.map(l => l.name).join('、')
    onAfterLink(`已关联技能：${names}。请继续。`)
  }
  function handleUnlink(id: string) {
    setLinked(prev => prev.filter(l => l.skillId !== id))
  }

  return (
    <div className="hb-todo-skill">
      <section className="hb-todo-skill-section is-progress" aria-label="技能定义与实现状态">
        <div className="hb-todo-skill-section-head">
          {t('hiring.todo.skill.currentStatus')}
        </div>
        <div className="hb-todo-skill-linked-item">
          <div className="hb-todo-skill-main">
            <div className="hb-todo-skill-title-row">
              {t('hiring.todo.skill.definitionStatus')}
              <div className="hb-todo-skill-chips">
                <span className={clsx('hb-todo-skill-chip', definitionMeta.tone)}>{definitionMeta.label}</span>
              </div>
            </div>
          </div>
        </div>
        <div className="hb-todo-skill-linked-item">
          <div className="hb-todo-skill-main">
            <div className="hb-todo-skill-title-row">
              {t('hiring.todo.skill.implementationStatus')}
              <div className="hb-todo-skill-chips">
                <span className={clsx('hb-todo-skill-chip', implementationMeta.tone)}>{implementationMeta.label}</span>
              </div>
            </div>
            <p className="hb-todo-skill-desc">{implementationMeta.description}</p>
          </div>
        </div>
      </section>

      <div className="hb-todo-skill-toolbar">
        <label className="hb-todo-skill-search-field">
          <input
            type="text"
            className="hb-todo-input"
            placeholder={t('hiring.todo.skill.searchPlaceholder')}
            value={query}
            onChange={e => setQuery(e.target.value)}
          />
        </label>
        <span
          className={clsx(
            'hb-todo-skill-status-pill',
            searching && 'is-searching',
            !searching && linked.length > 0 && 'is-linked',
          )}
        >
          {searchStatusLabel}
        </span>
      </div>

      {definedSkills.length > 0 && (
        <section className="hb-todo-skill-section is-defined" aria-label="已定义技能">
          <div className="hb-todo-skill-section-head">
            {t('hiring.todo.skill.definedTitle')}
            <span className="hb-todo-skill-section-pill">{t('hiring.todo.skill.countLabel', { count: definedSkills.length })}</span>
          </div>
          <ul className="hb-todo-skill-list is-template">
            {definedSkills.map(skill => {
              const generationMeta = getDefinedSkillGenerationMeta(skill, skillGenerationState)

              return (
                <li key={skill.skillName} className="hb-todo-skill-item is-static">
                  <div className="hb-todo-skill-main">
                    <div className="hb-todo-skill-title-row">
                      <strong>{skill.skillName}</strong>
                      <div className="hb-todo-skill-chips">
                        <span className={clsx('hb-todo-skill-chip', generationMeta.tone)}>{generationMeta.label}</span>
                      </div>
                    </div>
                    {skill.description && <p className="hb-todo-skill-desc">{skill.description}</p>}
                    {skill.expectedOutput && (
                      <p className="hb-todo-skill-inline-meta">{t('hiring.todo.skill.expectedOutput', { value: skill.expectedOutput })}</p>
                    )}
                    {skill.triggers.length > 0 && (
                      <p className="hb-todo-skill-inline-meta">{t('hiring.todo.skill.triggers', { value: skill.triggers.join('、') })}</p>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        </section>
      )}

      <section className="hb-todo-skill-section is-search" aria-label="搜索与推荐技能">
        {currentSearching && <p className="hb-todo-hint-muted">{t('hiring.todo.skill.searchingHint')}</p>}
        {currentError && <p className="hb-todo-error">{currentError}</p>}
        {!currentSearching && !currentError && currentResults.length === 0 && (
          <p className="hb-todo-skill-empty">{trimmedQuery ? t('hiring.todo.skill.noResults') : t('hiring.todo.skill.noRecommended')}</p>
        )}

        {currentResults.length > 0 && (
          <>
            {trimmedQuery && currentTotal > currentResults.length && (
              <p className="hb-todo-hint-muted">{t('hiring.todo.skill.resultsSummary', { total: currentTotal, count: currentResults.length })}</p>
            )}
            <ul className="hb-todo-skill-list">
              {currentResults.map(s => {
                const displayName = s.displayName ?? s.name
                const linkedNow = isLinked(s.id)

                return (
                  <li key={s.id} className="hb-todo-skill-item">
                    <div className="hb-todo-skill-main">
                      <div className="hb-todo-skill-title-row">
                        <strong>{displayName}</strong>
                        <div className="hb-todo-skill-chips">
                          {s.currentVersion && <span className="hb-todo-skill-chip is-meta">{`v${s.currentVersion}`}</span>}
                          {s.level && <span className="hb-todo-skill-chip is-meta">{s.level}</span>}
                        </div>
                      </div>
                      {s.description && <p className="hb-todo-skill-desc">{s.description}</p>}
                      {s.tags && s.tags.length > 0 && (
                        <ul className="hb-todo-tag-list">
                          {s.tags.slice(0, 5).map(t => <li key={t} className="hb-todo-tag is-mini">{t}</li>)}
                        </ul>
                      )}
                    </div>
                    <div className="hb-todo-skill-actions">
                      <button
                        type="button"
                        className={clsx('hb-todo-row-btn', linkedNow ? 'is-ghost' : 'is-primary')}
                        disabled={linkedNow}
                        onClick={() => handleLink(s)}
                      >
                        {linkedNow ? t('hiring.todo.skill.linked') : t('hiring.todo.skill.link')}
                      </button>
                    </div>
                  </li>
                )
              })}
            </ul>
          </>
        )}
      </section>

      {linked.length > 0 && (
        <section className="hb-todo-skill-section is-linked-summary" aria-label="已关联技能">
          <div className="hb-todo-skill-section-head">
            {t('hiring.todo.skill.linkedTitle')}
            <span className="hb-todo-skill-section-pill is-linked">{t('hiring.todo.skill.countLabel', { count: linked.length })}</span>
          </div>
          <p className="hb-todo-hint-muted">{t('hiring.todo.skill.linkedDesc')}</p>
          <ul className="hb-todo-skill-linked-list">
            {linked.map(l => (
              <li key={l.skillId} className="hb-todo-skill-linked-item">
                <div className="hb-todo-skill-main">
                  <div className="hb-todo-skill-title-row">
                    <strong>{l.name}</strong>
                    <div className="hb-todo-skill-chips">
                      {l.version && <span className="hb-todo-skill-chip is-meta">{`v${l.version}`}</span>}
                    </div>
                  </div>
                </div>
                <div className="hb-todo-skill-actions">
                  <button
                    type="button"
                    className="hb-todo-row-btn is-ghost"
                    onClick={() => handleUnlink(l.skillId)}
                  >
                    {t('hiring.todo.skill.unlink')}
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {templatePackageSkills.length > 0 && (
        <section className="hb-todo-skill-section is-template" aria-label="模板内置技能">
          <div className="hb-todo-skill-section-head">
            {t('hiring.todo.skill.templateTitle')}
            <span className="hb-todo-skill-section-pill">{t('hiring.todo.skill.countLabel', { count: templatePackageSkills.length })}</span>
          </div>
          <p className="hb-todo-hint-muted">
            {t('hiring.todo.skill.templateDesc', { count: templatePackageSkills.length })}
          </p>
          <ul className="hb-todo-skill-list is-template">
            {templatePackageSkills.map(skill => (
              <li key={skill.relativePath} className="hb-todo-skill-item is-static">
                <div className="hb-todo-skill-main">
                  <div className="hb-todo-skill-title-row">
                    <strong>{skill.name}</strong>
                    <div className="hb-todo-skill-chips">
                      <span className={clsx('hb-todo-skill-chip', skill.required ? 'is-required' : 'is-optional')}>
                        {skill.required ? t('hiring.todo.skill.templateRequired') : t('hiring.todo.skill.templateOptional')}
                      </span>
                    </div>
                  </div>
                  <p className="hb-todo-skill-desc">{skill.relativePath}</p>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

// ── 外部系统卡（可选配置） ────────────────────────────────────────────────────

function ExternalCardBody({ onAfterSave }: { onAfterSave: (summary: string) => void }) {
  const { t } = useTranslation()
  const [systemName, setSystemName] = useState('')
  const [apiUrl, setApiUrl] = useState('')
  const [token, setToken] = useState('')

  function handleSkip() {
    onAfterSave(i18n.t('hiring.todo.external.skipMessage'))
  }
  function handleSave() {
    const name = systemName.trim()
    if (!name) return
    onAfterSave(i18n.t('hiring.todo.external.saveMessage', { name, url: apiUrl || i18n.t('hiring.todo.external.apiUrlEmpty') }))
  }

  return (
    <div className="hb-todo-external">
      <p className="hb-todo-hint-muted">{t('hiring.todo.external.hint')}</p>
      <label className="hb-todo-field">
        {t('hiring.todo.external.systemName')}
        <input type="text" className="hb-todo-input" value={systemName}
          onChange={e => setSystemName(e.target.value)} placeholder={t('hiring.todo.external.systemNamePlaceholder')} />
      </label>
      <label className="hb-todo-field">
        {t('hiring.todo.external.apiUrl')}
        <input type="text" className="hb-todo-input" value={apiUrl}
          onChange={e => setApiUrl(e.target.value)} placeholder="https://..." />
      </label>
      <label className="hb-todo-field">
        {t('hiring.todo.external.token')}
        <input type="password" className="hb-todo-input" value={token}
          onChange={e => setToken(e.target.value)} placeholder={t('hiring.todo.external.tokenPlaceholder')} />
      </label>
      <div className="hb-todo-actions-row">
        <button type="button" className="hb-todo-row-btn is-ghost" onClick={handleSkip}>{t('hiring.todo.external.skip')}</button>
        <button type="button" className="hb-todo-row-btn is-primary"
          disabled={!systemName.trim()} onClick={handleSave}>{t('hiring.todo.external.save')}</button>
      </div>
    </div>
  )
}

// ── Final 卡片（生成实例包） ──────────────────────────────────────────────────

function FinalCard({
  canGenerate, generated, expanded, isFocus, onToggle, onGenerate,
}: {
  canGenerate: boolean
  generated: boolean
  expanded: boolean
  isFocus: boolean
  onToggle: () => void
  onGenerate?: () => void
}) {
  const { t } = useTranslation()
  return (
    <div className={clsx(
      'hb-todo-stage-card',
      'is-accent-shell',
      generated && 'is-complete',
      !generated && canGenerate && 'is-active',
      isFocus && expanded && 'is-focus',
    )}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">4</span>
        <span className="hb-todo-stage-title">{t('hiring.todo.final.title')}</span>
        <span className={clsx('hb-todo-stage-badge', generated ? 'is-complete' : canGenerate ? 'is-active' : '')}>
          {generated ? t('hiring.todo.final.generatedBadge') : canGenerate ? t('hiring.todo.final.canGenerateBadge') : t('hiring.todo.status.waiting')}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className="hb-todo-stage-body">
          <p className="hb-todo-stage-hint">
            {t('hiring.todo.final.hint')}
          </p>
          <button type="button"
            className={clsx('hb-todo-row-btn', canGenerate && !generated ? 'is-primary' : 'is-ghost')}
            disabled={!canGenerate || generated}
            onClick={onGenerate}>
            {generated ? t('hiring.todo.final.generatedBtn') : t('hiring.todo.final.generateBtn')}
          </button>
        </div>
      )}
    </div>
  )
}
