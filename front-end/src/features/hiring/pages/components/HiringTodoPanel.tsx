/**
 * HiringTodoPanel — 雇佣 TODO 交互面板（artifact 驱动版）
 *
 * 设计要点：
 * - 不再依赖 HandoffItem 列表，3 个阶段卡片始终常驻渲染。
 * - 阶段亮灯/完成态完全由 `wsStageOverrides`（artifact / skill_stage_gate WS 事件聚合）控制。
 * - 资料卡：仅接受 .md / .json 的文件夹/文件上传，落盘到 wwwroot/resources/todo-files/{sessionId}/{folder?}/。
 * - 技能卡：调用内部 Skills Catalog 搜索并关联，外部系统配置作为可选项。
 * - 资料/外部阶段在用户完成一次上传或保存后，先进入待确认，再由用户明确选择继续补充或推进阶段。
 * - 含 200% 缩放（Surface Pro 8 类窄屏）适配。
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import { ArrowRight, Eye, EyeOff, FileText, Loader2, Trash2, Upload } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import Editor from '@monaco-editor/react'
import i18n from '@/i18n'

import { api, HiringCollectionStage } from '@/infra/api'
import type {
  EmployeeTemplatePackageSkill,
  HiringCollectionStageType,
  HiringExternalSystemConfig,
  StoreSkillItem,
} from '@/infra/api'
import type {
  ChatFile,
  DefinedSkillItem,
  DownstreamRunState,
  MaterialRequestedCategory,
} from '../hiringPageTypes'
import type { ExternalConfigChangeSource } from '../externalPackagingState'
import type {
  PendingStageAdvanceConfirmation,
  StageAdvanceIntent,
} from '../stageAdvanceConfirmation'
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
  workspaceRelativePath?: string | null
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
  /** 子卡片产出阶段摘要后回调；资料/外部阶段会先等待用户确认是否推进 */
  onAfterStageMessage?: (stage: StageKey, summary: string, intent?: StageAdvanceIntent) => void
  /** 触发生成实例包 */
  onGenerate?: () => void
  generated?: boolean
  /** 生成完成后跳转 AI 评估页 */
  onEnterEvaluation?: () => void
  /** 用户关联的 store skill UUID 列表变化时回调；用于在导入产物包时一并提交给后端。 */
  onLinkedSkillIdsChange?: (skillIds: string[]) => void
  templatePackageSkills?: EmployeeTemplatePackageSkill[]
  requestedMaterialCategories?: MaterialRequestedCategory[]
  uploadedConversationFiles?: ChatFile[]
  skillDefinitionStageStatus?: StageStatus | null
  skillGenerationState?: DownstreamRunState | null
  definedSkills?: DefinedSkillItem[]
  onExternalConfigChange?: (config: HiringExternalSystemConfig | null, source?: ExternalConfigChangeSource) => void
  pendingStageConfirmation?: PendingStageAdvanceConfirmation | null
  onContinueStageCollection?: () => void
  onConfirmStageAdvance?: () => void
  stageConfirmationBusy?: boolean
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
  onEnterEvaluation,
  onLinkedSkillIdsChange,
  templatePackageSkills = [],
  requestedMaterialCategories = [],
  uploadedConversationFiles = [],
  skillDefinitionStageStatus = null,
  skillGenerationState = null,
  definedSkills = [],
  onExternalConfigChange,
  pendingStageConfirmation = null,
  onContinueStageCollection,
  onConfirmStageAdvance,
  stageConfirmationBusy = false,
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

  const isExternalStageUnlocked = useMemo(
    () => activeStageKey === HiringCollectionStage.External
      || activeStageKey === 'final'
      || wsStageOverrides.has(HiringCollectionStage.External),
    [activeStageKey, wsStageOverrides],
  )

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
            pendingConfirmation={pendingStageConfirmation?.stage === HiringCollectionStage.Material ? pendingStageConfirmation : null}
            stageConfirmationBusy={stageConfirmationBusy}
            onContinueCollection={onContinueStageCollection}
            onConfirmAdvance={onConfirmStageAdvance}
            onAfterUpload={summary => onAfterStageMessage?.(HiringCollectionStage.Material, summary, 'ready_to_advance')}
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
                onAfterLink={summary => onAfterStageMessage?.(HiringCollectionStage.Skill, summary, 'collecting')}
                onLinkedIdsChange={onLinkedSkillIdsChange}
                definitionStageStatus={skillDefinitionStageStatus}
                skillGenerationState={skillGenerationState}
                definedSkills={definedSkills}
              />
            )}
            {stage.key === HiringCollectionStage.External && (
              <ExternalCardBody
                hireId={hireId}
                isUnlocked={isExternalStageUnlocked}
                pendingConfirmation={pendingStageConfirmation?.stage === HiringCollectionStage.External ? pendingStageConfirmation : null}
                stageConfirmationBusy={stageConfirmationBusy}
                onContinueCollection={onContinueStageCollection}
                onConfirmAdvance={onConfirmStageAdvance}
                onAfterSave={(summary, intent) => onAfterStageMessage?.(HiringCollectionStage.External, summary, intent)}
                onConfigChange={onExternalConfigChange}
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
          onEnterEvaluation={onEnterEvaluation}
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

function StageAdvanceConfirmationPanel({
  pendingConfirmation,
  busy,
  onContinueCollection,
  onConfirmAdvance,
}: {
  pendingConfirmation: PendingStageAdvanceConfirmation
  busy: boolean
  onContinueCollection?: () => void
  onConfirmAdvance?: () => void
}) {
  return (
    <section className="hb-todo-external-card is-list-card">
      <div className="hb-todo-external-card-head">
        <div>
          <div className="hb-todo-external-card-title">{pendingConfirmation.title}</div>
          <p className="hb-todo-external-card-copy">{pendingConfirmation.prompt}</p>
        </div>
      </div>
      <div className="hb-todo-actions-row">
        <button
          type="button"
          className="hb-todo-row-btn is-ghost"
          disabled={busy}
          onClick={onContinueCollection}
        >
          {pendingConfirmation.continueLabel}
        </button>
        <button
          type="button"
          className="hb-todo-row-btn is-primary"
          disabled={busy}
          onClick={onConfirmAdvance}
        >
          {pendingConfirmation.confirmLabel}
        </button>
      </div>
    </section>
  )
}

function MaterialCardBody({
  hireId,
  sessionId,
  requestedCategories,
  uploadedConversationFiles,
  pendingConfirmation,
  stageConfirmationBusy,
  onContinueCollection,
  onConfirmAdvance,
  onAfterUpload,
}: {
  hireId: string
  sessionId: string
  requestedCategories: MaterialRequestedCategory[]
  uploadedConversationFiles: ChatFile[]
  pendingConfirmation: PendingStageAdvanceConfirmation | null
  stageConfirmationBusy: boolean
  onContinueCollection?: () => void
  onConfirmAdvance?: () => void
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

function ExternalCardBody({
  hireId,
  isUnlocked,
  onAfterSave,
  onConfigChange,
  pendingConfirmation,
  stageConfirmationBusy,
  onContinueCollection,
  onConfirmAdvance,
}: {
  hireId: string
  isUnlocked: boolean
  onAfterSave: (summary: string, intent: StageAdvanceIntent) => void
  onConfigChange?: (config: HiringExternalSystemConfig | null, source?: ExternalConfigChangeSource) => void
  pendingConfirmation: PendingStageAdvanceConfirmation | null
  stageConfirmationBusy: boolean
  onContinueCollection?: () => void
  onConfirmAdvance?: () => void
}) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [isConfiguring, setIsConfiguring] = useState(false)
  const [cliTools, setCliTools] = useState<CliToolDraft[]>([createCliToolDraft()])
  const [mcpConfig, setMcpConfig] = useState<McpConfigDraft>(createMcpConfigDraft())
  const [activeModal, setActiveModal] = useState<ExternalConfigModalType | null>(null)
  const [cliDraftTools, setCliDraftTools] = useState<CliToolDraft[]>([createCliToolDraft()])
  const [mcpDraftConfig, setMcpDraftConfig] = useState<McpConfigDraft>(createMcpConfigDraft())
  const [saveError, setSaveError] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [visibleSecrets, setVisibleSecrets] = useState<Record<string, boolean>>({})
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false)
  const hasHydratedExternalConfigRef = useRef(false)
  const externalConfigQueryKey = ['hiring-external-config', hireId] as const

  const {
    data: persistedExternalConfig,
    error: persistedExternalConfigError,
    isLoading: isExternalConfigLoading,
    refetch: refetchExternalConfig,
  } = useQuery({
    queryKey: externalConfigQueryKey,
    queryFn: () => api.hiringWorkflow.getExternalConfig(hireId),
    enabled: Boolean(hireId),
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  })

  const toggleSecretVisibility = (key: string) => {
    setVisibleSecrets(prev => ({ ...prev, [key]: !prev[key] }))
  }

  const clearFieldError = (key: string) => {
    setFieldErrors(prev => {
      const next = { ...prev }
      delete next[key]
      return next
    })
  }

  const cliConfiguredTools = cliTools.filter(tool => tool.name.trim().length > 0 && tool.command.trim().length > 0)
  const hasMcpConfig = hasMeaningfulMcpConfig(mcpConfig)
  const hasAnyConfig = cliConfiguredTools.length > 0 || hasMcpConfig

  useEffect(() => {
    if (!persistedExternalConfig || hasHydratedExternalConfigRef.current) {
      return
    }

    setCliTools(createCliToolDraftsFromConfig(persistedExternalConfig.cliTools))
    setCliDraftTools(createCliToolDraftsFromConfig(persistedExternalConfig.cliTools))
    setMcpConfig(createMcpConfigDraftFromConfig(persistedExternalConfig.mcpServer))
    setMcpDraftConfig(createMcpConfigDraftFromConfig(persistedExternalConfig.mcpServer))
    if ((persistedExternalConfig.submissionMode ?? 'pending') === 'configured' && hasPersistedExternalConfig(persistedExternalConfig)) {
      setIsConfiguring(true)
    }
    onConfigChange?.(persistedExternalConfig, 'hydrate')

    hasHydratedExternalConfigRef.current = true
  }, [onConfigChange, persistedExternalConfig])

  useEffect(() => {
    if (!persistedExternalConfigError) {
      return
    }

    // 加载错误通过专用 UI 块展示（带重试按钮），避免与保存/跳过错误混淆
  }, [persistedExternalConfigError])

  // 判断当前打开的模态框是否有未保存的草稿修改
  function hasDraftChanges(): boolean {
    if (activeModal === 'cli') {
      return JSON.stringify(cliDraftTools) !== JSON.stringify(cliTools.map(tool => ({ ...tool })))
    }
    if (activeModal === 'mcp') {
      return JSON.stringify(mcpDraftConfig) !== JSON.stringify(mcpConfig)
    }
    return false
  }

  // 统一的模态框关闭入口：有草稿变化时先弹出丢弃确认条
  function handleCloseModal() {
    if (hasDraftChanges()) {
      setShowDiscardConfirm(true)
    } else {
      setActiveModal(null)
      setShowDiscardConfirm(false)
    }
  }

  function confirmDiscard() {
    setActiveModal(null)
    setShowDiscardConfirm(false)
  }

  // 重新配置：将已跳过状态重置为 pending，以便用户重新进入配置流程
  async function handleReconfigure() {
    setSaveError('')
    setIsSaving(true)
    try {
      const savedConfig = await api.hiringWorkflow.saveExternalConfig(hireId, {
        submissionMode: 'pending',
        cliTools: [],
        mcpServer: null,
      })
      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setCliTools(createCliToolDraftsFromConfig(savedConfig.cliTools))
      setCliDraftTools(createCliToolDraftsFromConfig(savedConfig.cliTools))
      setMcpConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      setMcpDraftConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      setIsConfiguring(false)
      onConfigChange?.(null, 'clear')
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '重置外部系统配置失败')
    } finally {
      setIsSaving(false)
    }
  }

  async function handleSkip() {
    setSaveError('')
    setIsSaving(true)
    try {
      const savedConfig = await api.hiringWorkflow.saveExternalConfig(hireId, {
        submissionMode: 'skipped',
        cliTools: [],
        mcpServer: null,
      })
      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setCliTools(createCliToolDraftsFromConfig(savedConfig.cliTools))
      setCliDraftTools(createCliToolDraftsFromConfig(savedConfig.cliTools))
      setMcpConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      setMcpDraftConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      setIsConfiguring(false)
      onConfigChange?.(savedConfig, 'skip')
      onAfterSave(i18n.t('hiring.todo.external.skipMessage'), 'skip')
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '外部系统配置跳过失败')
    } finally {
      setIsSaving(false)
    }
  }

  function handleStartConfig() {
    setSaveError('')
    setIsConfiguring(true)
    onAfterSave(EXTERNAL_CONFIG_START_MESSAGE, 'collecting')
  }

  function handleOpenCliModal() {
    setCliDraftTools(cloneCliTools(cliTools))
    setActiveModal('cli')
  }

  function handleAddCliDraftTool() {
    setCliDraftTools(prev => [...prev, createCliToolDraft()])
  }

  function handleUpdateCliDraftTool(id: string, patch: Partial<CliToolDraft>) {
    setCliDraftTools(prev => prev.map(tool => tool.id === id ? { ...tool, ...patch } : tool))
  }

  function handleRemoveCliDraftTool(id: string) {
    setCliDraftTools(prev => {
      if (prev.length === 1) {
        return [{ ...createCliToolDraft(), id }]
      }

      return prev.filter(tool => tool.id !== id)
    })
  }

  function handleSaveCliConfig() {
    setCliTools(cloneCliTools(cliDraftTools))
    setActiveModal(null)
    setShowDiscardConfirm(false)
  }

  function handleOpenMcpModal() {
    setMcpDraftConfig(cloneMcpConfig(mcpConfig))
    setActiveModal('mcp')
  }

  function handleSaveMcpConfig() {
    setMcpConfig(cloneMcpConfig(mcpDraftConfig))
    setActiveModal(null)
    setShowDiscardConfirm(false)
  }

  function handleAddEnvEntry() {
    setMcpDraftConfig(prev => ({
      ...prev,
      envEntries: [...prev.envEntries, createEmptyKeyValueEntry()],
    }))
  }

  function handleUpdateEnvEntry(id: string, patch: Partial<McpKeyValueEntry>) {
    setMcpDraftConfig(prev => ({
      ...prev,
      envEntries: prev.envEntries.map(e => e.id === id ? { ...e, ...patch } : e),
    }))
  }

  function handleRemoveEnvEntry(id: string) {
    setMcpDraftConfig(prev => ({
      ...prev,
      envEntries: prev.envEntries.filter(e => e.id !== id),
    }))
  }

  function handleAddHeaderEntry() {
    setMcpDraftConfig(prev => ({
      ...prev,
      headerEntries: [...prev.headerEntries, createEmptyKeyValueEntry()],
    }))
  }

  function handleUpdateHeaderEntry(id: string, patch: Partial<McpKeyValueEntry>) {
    setMcpDraftConfig(prev => ({
      ...prev,
      headerEntries: prev.headerEntries.map(e => e.id === id ? { ...e, ...patch } : e),
    }))
  }

  function handleRemoveHeaderEntry(id: string) {
    setMcpDraftConfig(prev => ({
      ...prev,
      headerEntries: prev.headerEntries.filter(e => e.id !== id),
    }))
  }

  function handleAddArg() {
    setMcpDraftConfig(prev => ({ ...prev, args: [...prev.args, ''] }))
  }

  function handleUpdateArg(index: number, value: string) {
    setMcpDraftConfig(prev => ({
      ...prev,
      args: prev.args.map((a, i) => i === index ? value : a),
    }))
  }

  function handleRemoveArg(index: number) {
    setMcpDraftConfig(prev => ({
      ...prev,
      args: prev.args.filter((_, i) => i !== index),
    }))
  }

  function handleAddEnvPassThrough() {
    setMcpDraftConfig(prev => ({ ...prev, envPassThrough: [...prev.envPassThrough, ''] }))
  }

  function handleUpdateEnvPassThrough(index: number, value: string) {
    setMcpDraftConfig(prev => ({
      ...prev,
      envPassThrough: prev.envPassThrough.map((v, i) => i === index ? value : v),
    }))
  }

  function handleRemoveEnvPassThrough(index: number) {
    setMcpDraftConfig(prev => ({
      ...prev,
      envPassThrough: prev.envPassThrough.filter((_, i) => i !== index),
    }))
  }

  function handleAddHeadersFromEnvEntry() {
    setMcpDraftConfig(prev => ({
      ...prev,
      headersFromEnvEntries: [...prev.headersFromEnvEntries, createEmptyKeyValueEntry()],
    }))
  }

  function handleUpdateHeadersFromEnvEntry(id: string, patch: Partial<McpKeyValueEntry>) {
    setMcpDraftConfig(prev => ({
      ...prev,
      headersFromEnvEntries: prev.headersFromEnvEntries.map(e => e.id === id ? { ...e, ...patch } : e),
    }))
  }

  function handleRemoveHeadersFromEnvEntry(id: string) {
    setMcpDraftConfig(prev => ({
      ...prev,
      headersFromEnvEntries: prev.headersFromEnvEntries.filter(e => e.id !== id),
    }))
  }

  function buildSaveSummary() {
    const parts: string[] = []

    if (cliConfiguredTools.length > 0) {
      const cliSummary = cliConfiguredTools
        .map(tool => `${tool.name.trim()}（${tool.executionMode === 'sandbox' ? '沙箱执行' : '直接执行'}）`)
        .join('、')
      parts.push(`CLI 工具 ${cliConfiguredTools.length} 项：${cliSummary}`)
    }

    if (hasMcpConfig) {
      const transportLabel = MCP_TRANSPORT_LABELS[mcpConfig.transport]
      const detail = mcpConfig.transport === 'stdio'
        ? `命令: ${mcpConfig.command.trim()}`
        : `URL: ${mcpConfig.url.trim()}`
      parts.push(`MCP ${mcpConfig.name.trim()}（${transportLabel}）${detail}`)
    }

    return `外部系统配置已保存：${parts.join('；')}。外部阶段已完成，请继续下一步。`
  }

  async function handleSave() {
    if (!hasAnyConfig) return

    setSaveError('')
    setIsSaving(true)
    try {
      const savedConfig = await api.hiringWorkflow.saveExternalConfig(hireId, {
        submissionMode: 'configured',
        cliTools: cliConfiguredTools.map(tool => {
          let parameters: Record<string, unknown> = {}
          try {
            parameters = parseParameters(tool.parameters)
          } catch {
            throw new Error(`CLI 工具 "${tool.name.trim()}" 的 JSON Schema 格式无效`)
          }
          return {
            name: tool.name.trim(),
            command: tool.command.trim(),
            description: tool.description.trim(),
            executionMode: tool.executionMode,
            parameters,
          }
        }),
        mcpServer: hasMcpConfig
          ? {
            transport: mcpConfig.transport,
            name: mcpConfig.name.trim(),
            command: mcpConfig.transport === 'stdio' ? mcpConfig.command.trim() : undefined,
            args: mcpConfig.transport === 'stdio' && mcpConfig.args.length > 0
              ? mcpConfig.args.filter(Boolean)
              : undefined,
            env: mcpConfig.transport === 'stdio' && mcpConfig.envEntries.length > 0
              ? entriesToRecord(mcpConfig.envEntries)
              : undefined,
            envPassThrough: mcpConfig.transport === 'stdio' && mcpConfig.envPassThrough.length > 0
              ? mcpConfig.envPassThrough.filter(Boolean)
              : undefined,
            cwd: mcpConfig.transport === 'stdio' ? (mcpConfig.cwd.trim() || undefined) : undefined,
            url: mcpConfig.transport === 'http' ? mcpConfig.url.trim() : undefined,
            bearerTokenEnv: mcpConfig.transport === 'http' ? (mcpConfig.bearerTokenEnv.trim() || undefined) : undefined,
            headers: mcpConfig.transport === 'http' && mcpConfig.headerEntries.length > 0
              ? entriesToRecord(mcpConfig.headerEntries)
              : undefined,
            headersFromEnv: mcpConfig.transport === 'http' && mcpConfig.headersFromEnvEntries.length > 0
              ? entriesToRecord(mcpConfig.headersFromEnvEntries)
              : undefined,
          }
          : null,
      })

      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setCliTools(createCliToolDraftsFromConfig(savedConfig.cliTools))
      setCliDraftTools(createCliToolDraftsFromConfig(savedConfig.cliTools))
      setMcpConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      setMcpDraftConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      onConfigChange?.(savedConfig, 'save')
      onAfterSave(buildSaveSummary(), 'ready_to_advance')
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '外部系统配置保存失败')
    } finally {
      setIsSaving(false)
    }
  }

  if (isExternalConfigLoading) {
    return (
      <div className="hb-todo-external">
        <p className="hb-todo-hint-muted">{t('hiring.todo.external.hint')}</p>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10, padding: '12px 0' }}>
          <div className="hb-todo-skeleton-bar is-wide" />
          <div className="hb-todo-skeleton-bar is-mid" />
          <div className="hb-todo-skeleton-bar is-short" />
        </div>
      </div>
    )
  }

  return (
    <div className="hb-todo-external">
      <p className="hb-todo-hint-muted">{t('hiring.todo.external.hint')}</p>
      {persistedExternalConfigError && (
        <div className="hb-todo-external-error" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span>{persistedExternalConfigError instanceof Error ? persistedExternalConfigError.message : '加载失败'}</span>
          <button type="button" className="hb-todo-row-btn is-ghost" onClick={() => { void refetchExternalConfig() }}>
            {t('hiring.todo.external.retryLoad')}
          </button>
        </div>
      )}
      {!isUnlocked ? (
        <div className="hb-todo-external-locked">
          {t('hiring.todo.external.stageLockedMessage')}
        </div>
      ) : persistedExternalConfig?.submissionMode === 'skipped' ? (
        <>
          <div className="hb-todo-external-locked">
            <p>{i18n.t('hiring.todo.external.skipMessage')}</p>
            <button
              type="button"
              className="hb-todo-row-btn is-ghost"
              style={{ marginTop: 8 }}
              disabled={isSaving}
              onClick={() => { void handleReconfigure() }}
            >
              {t('hiring.todo.external.reconfigure')}
            </button>
          </div>
          {pendingConfirmation && (
            <StageAdvanceConfirmationPanel
              pendingConfirmation={pendingConfirmation}
              busy={stageConfirmationBusy}
              onContinueCollection={onContinueCollection}
              onConfirmAdvance={onConfirmAdvance}
            />
          )}
        </>
      ) : !isConfiguring ? (
        <>
          <div className="hb-todo-external-choice-grid">
            <article className="hb-todo-external-card is-preview">
              <div className="hb-todo-external-card-head">
                <div>
                  <div className="hb-todo-external-card-title">{t('hiring.todo.external.cliTitle')}</div>
                  <p className="hb-todo-external-card-copy">{t('hiring.todo.external.cliDescription')}</p>
                </div>
                <span className="hb-todo-external-type-pill">CLI</span>
              </div>
            </article>
            <article className="hb-todo-external-card is-preview">
              <div className="hb-todo-external-card-head">
                <div>
                  <div className="hb-todo-external-card-title">{t('hiring.todo.external.mcpTitle')}</div>
                  <p className="hb-todo-external-card-copy">{t('hiring.todo.external.mcpDescription')}</p>
                </div>
                <span className="hb-todo-external-type-pill">MCP</span>
              </div>
            </article>
          </div>
          <div className="hb-todo-actions-row">
            <button type="button" className="hb-todo-row-btn is-ghost" onClick={handleSkip}>{t('hiring.todo.external.skip')}</button>
            <button type="button" className="hb-todo-row-btn is-primary" onClick={handleStartConfig}>{t('hiring.todo.external.continueConfig')}</button>
          </div>
          {pendingConfirmation && (
            <StageAdvanceConfirmationPanel
              pendingConfirmation={pendingConfirmation}
              busy={stageConfirmationBusy}
              onContinueCollection={onContinueCollection}
              onConfirmAdvance={onConfirmAdvance}
            />
          )}
        </>
      ) : (
        <>
          <section className="hb-todo-external-card is-list-card">
            <div className="hb-todo-external-row">
              <div className="hb-todo-external-card-head">
                <div>
                  <div className="hb-todo-external-card-title">{t('hiring.todo.external.cliTitle')}</div>
                  <p className="hb-todo-external-card-copy">
                    {cliConfiguredTools.length > 0
                      ? (
                        <>
                          {`已配置 ${cliConfiguredTools.length} 个 CLI 工具：`}
                          {cliConfiguredTools.map((tool, idx) => (
                            <span key={tool.id}>
                              {idx > 0 && '、'}
                              <span className="hb-todo-truncate" title={tool.name.trim()} style={{ display: 'inline-block', verticalAlign: 'bottom' }}>{tool.name.trim()}</span>
                            </span>
                          ))}
                        </>
                      )
                      : t('hiring.todo.external.cliDescription')}
                  </p>
                </div>
                <span className="hb-todo-external-type-pill">CLI</span>
              </div>
              <button type="button" className="hb-todo-row-btn is-primary" onClick={handleOpenCliModal}>
                {t('hiring.todo.external.editConfig')}
              </button>
            </div>
          </section>

          <section className="hb-todo-external-card is-list-card">
            <div className="hb-todo-external-row">
              <div className="hb-todo-external-card-head">
                <div>
                  <div className="hb-todo-external-card-title">{t('hiring.todo.external.mcpTitle')}</div>
                  <p className="hb-todo-external-card-copy">
                    {hasMcpConfig
                      ? (
                        <>
                          {'已配置 MCP「'}
                          <span className="hb-todo-truncate" title={mcpConfig.name.trim()} style={{ display: 'inline-block', verticalAlign: 'bottom' }}>{mcpConfig.name.trim()}</span>
                          {`」（${MCP_TRANSPORT_LABELS[mcpConfig.transport]}）`}
                        </>
                      )
                      : t('hiring.todo.external.mcpDescription')}
                  </p>
                </div>
                <span className="hb-todo-external-type-pill">MCP</span>
              </div>
              <button type="button" className="hb-todo-row-btn is-primary" onClick={handleOpenMcpModal}>
                {t('hiring.todo.external.editConfig')}
              </button>
            </div>
          </section>

          <div className="hb-todo-actions-row">
            <button type="button" className="hb-todo-row-btn is-ghost" onClick={handleSkip}>{t('hiring.todo.external.skip')}</button>
            <button type="button" className="hb-todo-row-btn is-primary" disabled={!hasAnyConfig || isSaving} onClick={() => { void handleSave() }}>
              {isSaving ? (
                <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                  <Loader2 size={13} style={{ animation: 'spin 1s linear infinite' }} />
                  {t('hiring.todo.external.saving')}
                </span>
              ) : t('hiring.todo.external.save')}
            </button>
          </div>
          {pendingConfirmation && (
            <StageAdvanceConfirmationPanel
              pendingConfirmation={pendingConfirmation}
              busy={stageConfirmationBusy}
              onContinueCollection={onContinueCollection}
              onConfirmAdvance={onConfirmAdvance}
            />
          )}

          {activeModal === 'cli' && (
            <div
              className="hb-todo-modal-backdrop"
              role="presentation"
              onClick={() => { setActiveModal(null); setShowDiscardConfirm(false) }}
            >
              <div
                className="hb-todo-modal hb-todo-mcp-modal"
                role="dialog"
                aria-modal="true"
                aria-label="CLI 配置"
                onClick={e => e.stopPropagation()}
              >
                <div className="hb-todo-mcp-form">
                  {cliDraftTools.map((tool, index) => (
                    <div key={tool.id} className="hb-todo-cli-tool">
                      {cliDraftTools.length > 1 && (
                        <div className="hb-todo-cli-tool-head">
                          <span className="hb-todo-cli-tool-label">{`工具 ${index + 1}`}</span>
                          <button
                            type="button"
                            className="hb-todo-mcp-icon-btn"
                            aria-label="删除工具"
                            onClick={() => handleRemoveCliDraftTool(tool.id)}
                          >
                            <Trash2 size={14} />
                          </button>
                        </div>
                      )}

                      <label className="hb-todo-field hb-todo-mcp-field">
                        <span>工具标识</span>
                        <input
                          type="text"
                          className="hb-todo-input"
                          value={tool.name}
                          onChange={e => handleUpdateCliDraftTool(tool.id, { name: e.target.value })}
                          placeholder="例如：jq / ffmpeg / python"
                        />
                      </label>

                      <label className="hb-todo-field hb-todo-mcp-field">
                        <span>可执行文件路径</span>
                        <input
                          type="text"
                          className="hb-todo-input hb-todo-input-mono"
                          value={tool.command}
                          onChange={e => handleUpdateCliDraftTool(tool.id, { command: e.target.value })}
                          placeholder="例如：/usr/bin/jq 或 npx"
                        />
                      </label>

                      <label className="hb-todo-field hb-todo-mcp-field">
                        <span>描述</span>
                        <textarea
                          className="hb-todo-input hb-todo-textarea"
                          value={tool.description}
                          onChange={e => handleUpdateCliDraftTool(tool.id, { description: e.target.value })}
                          placeholder="这个工具做什么，AI 何时应该调用它"
                        />
                      </label>

                      {/* 执行方式 Tab 切换，复用 MCP 弹窗的 tabs 样式 */}
                      <div className="hb-todo-field hb-todo-mcp-field">
                        <span>执行方式</span>
                        <div className="hb-todo-mcp-tabs" role="tablist" aria-label="CLI 执行方式">
                          {(['direct', 'sandbox'] as const).map(mode => {
                            const selected = tool.executionMode === mode
                            const label = mode === 'direct' ? '直接执行' : '沙箱执行'
                            return (
                              <button
                                key={mode}
                                type="button"
                                role="tab"
                                aria-selected={selected}
                                className={clsx('hb-todo-mcp-tab', selected && 'is-active')}
                                onClick={() => handleUpdateCliDraftTool(tool.id, { executionMode: mode })}
                              >
                                {label}
                              </button>
                            )
                          })}
                        </div>
                      </div>

                      {/* 参数 JSON Schema：保留 Monaco 编辑器，去除多余说明文字 */}
                      <div className="hb-todo-field hb-todo-mcp-field">
                        <span>参数 JSON Schema</span>
                        <div className={`hb-todo-monaco-wrap${fieldErrors[`schema-${tool.id}`] ? ' is-error' : ''}`}>
                          <Editor
                            height="200px"
                            language="json"
                            theme="vs-light"
                            value={tool.parameters || ''}
                            onChange={(value) => handleUpdateCliDraftTool(tool.id, { parameters: value || '' })}
                            onValidate={(markers) => {
                              if (markers.length > 0) {
                                setFieldErrors(prev => ({ ...prev, [`schema-${tool.id}`]: t('hiring.todo.external.jsonSchemaInvalid') }))
                              } else {
                                clearFieldError(`schema-${tool.id}`)
                              }
                            }}
                            options={{
                              minimap: { enabled: false },
                              scrollBeyondLastLine: false,
                              lineNumbers: 'on',
                              automaticLayout: true,
                              fontSize: 12,
                              fontFamily: 'JetBrains Mono, Consolas, monospace',
                              tabSize: 2,
                              wordWrap: 'on',
                              renderLineHighlight: 'none',
                              overviewRulerLanes: 0,
                              hideCursorInOverviewRuler: true,
                              scrollbar: {
                                vertical: 'auto',
                                horizontal: 'auto',
                                verticalScrollbarSize: 8,
                                horizontalScrollbarSize: 8,
                              },
                              padding: { top: 8, bottom: 8 },
                            }}
                          />
                        </div>
                        {fieldErrors[`schema-${tool.id}`] && <p className="hb-todo-field-error">{fieldErrors[`schema-${tool.id}`]}</p>}
                      </div>
                    </div>
                  ))}

                  <button type="button" className="hb-todo-mcp-add-btn" onClick={handleAddCliDraftTool}>
                    + 添加工具
                  </button>

                  <div className="hb-todo-mcp-footer">
                    <button type="button" className="hb-todo-mcp-save-btn" onClick={handleSaveCliConfig}>
                      保存
                    </button>
                  </div>
                </div>
              </div>
            </div>
          )}

          {activeModal === 'mcp' && (
            <div className="hb-todo-modal-backdrop" role="presentation" onClick={handleCloseModal}>
              <div
                className="hb-todo-modal hb-todo-mcp-modal"
                role="dialog"
                aria-modal="true"
                aria-label="MCP 配置"
                onClick={e => e.stopPropagation()}
              >
                <div className="hb-todo-mcp-form">
                  {/* 名称 */}
                  <label className="hb-todo-field hb-todo-mcp-field">
                    <span>名称</span>
                    <input
                      type="text"
                      className="hb-todo-input"
                      value={mcpDraftConfig.name}
                      onChange={e => setMcpDraftConfig(prev => ({ ...prev, name: e.target.value }))}
                      placeholder="MCP server name"
                    />
                  </label>

                  {/* 传输方式 Tab 切换 */}
                  <div className="hb-todo-mcp-tabs" role="tablist" aria-label="MCP 传输方式">
                    {(['stdio', 'http'] as const).map(transport => {
                      const selected = mcpDraftConfig.transport === transport
                      const label = transport === 'stdio' ? 'STDIO' : '流式 HTTP'
                      return (
                        <button
                          key={transport}
                          type="button"
                          role="tab"
                          aria-selected={selected}
                          className={clsx('hb-todo-mcp-tab', selected && 'is-active')}
                          onClick={() => setMcpDraftConfig(prev => ({ ...prev, transport }))}
                        >
                          {label}
                        </button>
                      )
                    })}
                  </div>

                  {mcpDraftConfig.transport === 'stdio' ? (
                    <>
                      {/* 启动命令 */}
                      <label className="hb-todo-field hb-todo-mcp-field">
                        <span>启动命令</span>
                        <input
                          type="text"
                          className="hb-todo-input hb-todo-input-mono"
                          value={mcpDraftConfig.command}
                          onChange={e => setMcpDraftConfig(prev => ({ ...prev, command: e.target.value }))}
                          placeholder="openai-dev-mcp serve-sqlite"
                        />
                      </label>

                      {/* 参数 */}
                      <div className="hb-todo-field hb-todo-mcp-field">
                        <span>参数</span>
                        {mcpDraftConfig.args.length > 0 && (
                          <div className="hb-todo-mcp-list">
                            {mcpDraftConfig.args.map((arg, index) => (
                              <div key={index} className="hb-todo-mcp-row">
                                <input
                                  type="text"
                                  className="hb-todo-input hb-todo-input-mono"
                                  value={arg}
                                  onChange={e => handleUpdateArg(index, e.target.value)}
                                  placeholder="参数值"
                                />
                                <button
                                  type="button"
                                  className="hb-todo-mcp-icon-btn"
                                  aria-label="删除参数"
                                  onClick={() => handleRemoveArg(index)}
                                >
                                  <Trash2 size={14} />
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                        <button type="button" className="hb-todo-mcp-add-btn" onClick={handleAddArg}>
                          + 添加参数
                        </button>
                      </div>

                      {/* 环境变量 */}
                      <div className="hb-todo-field hb-todo-mcp-field">
                        <span>环境变量</span>
                        {mcpDraftConfig.envEntries.length > 0 && (
                          <div className="hb-todo-mcp-list">
                            {mcpDraftConfig.envEntries.map(entry => (
                              <div key={entry.id} className="hb-todo-mcp-row hb-todo-mcp-row-kv">
                                <input
                                  type="text"
                                  className="hb-todo-input hb-todo-input-mono"
                                  value={entry.key}
                                  onChange={e => handleUpdateEnvEntry(entry.id, { key: e.target.value })}
                                  placeholder="键"
                                />
                                <div className="hb-todo-input-toggle-wrap">
                                  <input
                                    type={visibleSecrets[`env-${entry.id}`] ? 'text' : 'password'}
                                    className="hb-todo-input hb-todo-input-mono"
                                    value={entry.value}
                                    onChange={e => handleUpdateEnvEntry(entry.id, { value: e.target.value })}
                                    placeholder="值"
                                  />
                                  <button
                                    type="button"
                                    className="hb-todo-input-toggle-btn"
                                    onClick={() => toggleSecretVisibility(`env-${entry.id}`)}
                                    aria-label={visibleSecrets[`env-${entry.id}`] ? '隐藏' : '显示'}
                                  >
                                    {visibleSecrets[`env-${entry.id}`] ? <EyeOff size={14} /> : <Eye size={14} />}
                                  </button>
                                </div>
                                <button
                                  type="button"
                                  className="hb-todo-mcp-icon-btn"
                                  aria-label="删除环境变量"
                                  onClick={() => handleRemoveEnvEntry(entry.id)}
                                >
                                  <Trash2 size={14} />
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                        <button type="button" className="hb-todo-mcp-add-btn" onClick={handleAddEnvEntry}>
                          + 添加环境变量
                        </button>
                      </div>

                      {/* 环境变量传递 */}
                      <div className="hb-todo-field hb-todo-mcp-field">
                        <span>环境变量传递</span>
                        {mcpDraftConfig.envPassThrough.length > 0 && (
                          <div className="hb-todo-mcp-list">
                            {mcpDraftConfig.envPassThrough.map((name, index) => (
                              <div key={index} className="hb-todo-mcp-row">
                                <input
                                  type="text"
                                  className="hb-todo-input hb-todo-input-mono"
                                  value={name}
                                  onChange={e => handleUpdateEnvPassThrough(index, e.target.value)}
                                  placeholder="例如：OPENAI_API_KEY"
                                />
                                <button
                                  type="button"
                                  className="hb-todo-mcp-icon-btn"
                                  aria-label="删除变量"
                                  onClick={() => handleRemoveEnvPassThrough(index)}
                                >
                                  <Trash2 size={14} />
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                        <button type="button" className="hb-todo-mcp-add-btn" onClick={handleAddEnvPassThrough}>
                          + 添加变量
                        </button>
                      </div>

                      {/* 工作目录 */}
                      <label className="hb-todo-field hb-todo-mcp-field">
                        <span>工作目录</span>
                        <input
                          type="text"
                          className="hb-todo-input hb-todo-input-mono"
                          value={mcpDraftConfig.cwd}
                          onChange={e => setMcpDraftConfig(prev => ({ ...prev, cwd: e.target.value }))}
                          placeholder="~/code"
                        />
                      </label>
                    </>
                  ) : (
                    <>
                      {/* URL */}
                      <label className="hb-todo-field hb-todo-mcp-field">
                        <span>URL</span>
                        <input
                          type="text"
                          className={`hb-todo-input hb-todo-input-mono${fieldErrors['mcpUrl'] ? ' is-error' : ''}`}
                          value={mcpDraftConfig.url}
                          onChange={e => {
                            setMcpDraftConfig(prev => ({ ...prev, url: e.target.value }))
                            if (fieldErrors['mcpUrl']) clearFieldError('mcpUrl')
                          }}
                          onBlur={e => {
                            const val = e.target.value.trim()
                            if (val && !/^https?:\/\/.+/.test(val)) {
                              setFieldErrors(prev => ({ ...prev, mcpUrl: t('hiring.todo.external.urlInvalid') }))
                            }
                          }}
                          placeholder="https://mcp.example.com/mcp"
                        />
                        {fieldErrors['mcpUrl'] && <p className="hb-todo-field-error">{fieldErrors['mcpUrl']}</p>}
                      </label>

                      {/* Bearer 令牌环境变量 */}
                      <label className="hb-todo-field hb-todo-mcp-field">
                        <span>Bearer 令牌环境变量</span>
                        <input
                          type="text"
                          className="hb-todo-input hb-todo-input-mono"
                          value={mcpDraftConfig.bearerTokenEnv}
                          onChange={e => setMcpDraftConfig(prev => ({ ...prev, bearerTokenEnv: e.target.value }))}
                          placeholder="例如：MCP_BEARER_TOKEN"
                        />
                      </label>

                      {/* 固定 Header */}
                      <div className="hb-todo-field hb-todo-mcp-field">
                        <span>固定 Header</span>
                        {mcpDraftConfig.headerEntries.length > 0 && (
                          <div className="hb-todo-mcp-list">
                            {mcpDraftConfig.headerEntries.map(entry => (
                              <div key={entry.id} className="hb-todo-mcp-row hb-todo-mcp-row-kv">
                                <input
                                  type="text"
                                  className="hb-todo-input hb-todo-input-mono"
                                  value={entry.key}
                                  onChange={e => handleUpdateHeaderEntry(entry.id, { key: e.target.value })}
                                  placeholder="Header 名"
                                />
                                <div className="hb-todo-input-toggle-wrap">
                                  <input
                                    type={visibleSecrets[`header-${entry.id}`] ? 'text' : 'password'}
                                    className="hb-todo-input"
                                    value={entry.value}
                                    onChange={e => handleUpdateHeaderEntry(entry.id, { value: e.target.value })}
                                    placeholder="值"
                                  />
                                  <button
                                    type="button"
                                    className="hb-todo-input-toggle-btn"
                                    onClick={() => toggleSecretVisibility(`header-${entry.id}`)}
                                    aria-label={visibleSecrets[`header-${entry.id}`] ? '隐藏' : '显示'}
                                  >
                                    {visibleSecrets[`header-${entry.id}`] ? <EyeOff size={14} /> : <Eye size={14} />}
                                  </button>
                                </div>
                                <button
                                  type="button"
                                  className="hb-todo-mcp-icon-btn"
                                  aria-label="删除 Header"
                                  onClick={() => handleRemoveHeaderEntry(entry.id)}
                                >
                                  <Trash2 size={14} />
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                        <button type="button" className="hb-todo-mcp-add-btn" onClick={handleAddHeaderEntry}>
                          + 添加 Header
                        </button>
                      </div>

                      {/* 来自环境变量的 Header */}
                      <div className="hb-todo-field hb-todo-mcp-field">
                        <span>来自环境变量的 Header</span>
                        {mcpDraftConfig.headersFromEnvEntries.length > 0 && (
                          <div className="hb-todo-mcp-list">
                            {mcpDraftConfig.headersFromEnvEntries.map(entry => (
                              <div key={entry.id} className="hb-todo-mcp-row hb-todo-mcp-row-kv">
                                <input
                                  type="text"
                                  className="hb-todo-input hb-todo-input-mono"
                                  value={entry.key}
                                  onChange={e => handleUpdateHeadersFromEnvEntry(entry.id, { key: e.target.value })}
                                  placeholder="Header 名"
                                />
                                <input
                                  type="text"
                                  className="hb-todo-input hb-todo-input-mono"
                                  value={entry.value}
                                  onChange={e => handleUpdateHeadersFromEnvEntry(entry.id, { value: e.target.value })}
                                  placeholder="环境变量名"
                                />
                                <button
                                  type="button"
                                  className="hb-todo-mcp-icon-btn"
                                  aria-label="删除映射"
                                  onClick={() => handleRemoveHeadersFromEnvEntry(entry.id)}
                                >
                                  <Trash2 size={14} />
                                </button>
                              </div>
                            ))}
                          </div>
                        )}
                        <button type="button" className="hb-todo-mcp-add-btn" onClick={handleAddHeadersFromEnvEntry}>
                          + 添加映射
                        </button>
                      </div>
                    </>
                  )}

                  <div className="hb-todo-mcp-footer">
                    <button type="button" className="hb-todo-mcp-save-btn" onClick={handleSaveMcpConfig}>
                      保存
                    </button>
                  </div>
                </div>
                {showDiscardConfirm && (
                  <div className="hb-todo-discard-confirm">
                    <span style={{ flex: 1 }}>{t('hiring.todo.external.discardDraftMessage')}</span>
                    <button type="button" className="hb-todo-row-btn is-ghost" onClick={() => setShowDiscardConfirm(false)}>
                      {t('hiring.todo.external.discardDraftCancel')}
                    </button>
                    <button type="button" className="hb-todo-row-btn is-primary" onClick={confirmDiscard}>
                      {t('hiring.todo.external.discardDraftConfirm')}
                    </button>
                  </div>
                )}
              </div>
            </div>
          )}
        </>
      )}
      {saveError && <p className="hb-todo-error">{saveError}</p>}
    </div>
  )
}

type CliExecutionMode = 'direct' | 'sandbox'
type McpTransport = 'stdio' | 'http'

interface McpKeyValueEntry {
  id: string
  key: string
  value: string
}

interface CliToolDraft {
  id: string
  name: string
  command: string
  description: string
  executionMode: CliExecutionMode
  parameters: string
}

interface McpConfigDraft {
  transport: McpTransport
  name: string
  // stdio
  command: string
  args: string[]
  envEntries: McpKeyValueEntry[]
  envPassThrough: string[]
  cwd: string
  // http
  url: string
  bearerTokenEnv: string
  headerEntries: McpKeyValueEntry[]
  headersFromEnvEntries: McpKeyValueEntry[]
}

type ExternalConfigModalType = 'cli' | 'mcp'

const MCP_TRANSPORT_LABELS: Record<McpTransport, string> = {
  stdio: 'STDIO（本地进程）',
  http: 'HTTP（远程服务）',
}
const EXTERNAL_CONFIG_START_MESSAGE = '我选择继续配置外部系统。请先帮我梳理应该配置哪些 CLI 工具和 MCP 服务，再逐项确认。'

let cliDraftSeed = 0

function createCliToolDraft(): CliToolDraft {
  cliDraftSeed += 1
  return {
    id: `cli-tool-${cliDraftSeed}`,
    name: '',
    command: '',
    description: '',
    executionMode: 'direct',
    parameters: '',
  }
}

function cloneCliTools(tools: CliToolDraft[]): CliToolDraft[] {
  return tools.map(tool => ({ ...tool, parameters: tool.parameters }))
}

function cloneMcpConfig(config: McpConfigDraft): McpConfigDraft {
  return {
    ...config,
    args: [...config.args],
    envEntries: config.envEntries.map(entry => ({ ...entry })),
    envPassThrough: [...config.envPassThrough],
    headerEntries: config.headerEntries.map(entry => ({ ...entry })),
    headersFromEnvEntries: config.headersFromEnvEntries.map(entry => ({ ...entry })),
  }
}

function createMcpConfigDraft(): McpConfigDraft {
  return {
    transport: 'http',
    name: '',
    command: '',
    args: [],
    envEntries: [],
    envPassThrough: [],
    cwd: '',
    url: '',
    bearerTokenEnv: '',
    headerEntries: [],
    headersFromEnvEntries: [],
  }
}

function createEmptyKeyValueEntry(): McpKeyValueEntry {
  return { id: `kv-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`, key: '', value: '' }
}

function hasMeaningfulMcpConfig(config: McpConfigDraft): boolean {
  if (!config.name.trim()) return false
  if (config.transport === 'stdio') return config.command.trim().length > 0
  return config.url.trim().length > 0
}

function recordToEntries(record?: Record<string, string> | null): McpKeyValueEntry[] {
  if (!record) return []
  return Object.entries(record).map(([key, value]) => ({
    id: `kv-${key}`,
    key,
    value,
  }))
}

function entriesToRecord(entries: McpKeyValueEntry[]): Record<string, string> {
  const result: Record<string, string> = {}
  for (const e of entries) {
    if (e.key.trim()) result[e.key.trim()] = e.value
  }
  return result
}

function parseParameters(raw: string): Record<string, unknown> {
  if (!raw.trim()) return {}
  const parsed = JSON.parse(raw)
  return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>) : {}
}

function createCliToolDraftsFromConfig(cliTools: HiringExternalSystemConfig['cliTools']): CliToolDraft[] {
  if (!cliTools || cliTools.length === 0) {
    return [createCliToolDraft()]
  }
  return cliTools.map(tool => ({
    id: createCliToolDraft().id,
    name: tool.name ?? '',
    command: tool.command ?? '',
    description: tool.description ?? '',
    executionMode: tool.executionMode === 'sandbox' ? 'sandbox' : 'direct',
    parameters: tool.parameters && Object.keys(tool.parameters).length > 0
      ? JSON.stringify(tool.parameters, null, 2)
      : '',
  }))
}

function createMcpConfigDraftFromConfig(mcpConfig?: HiringExternalSystemConfig['mcpServer'] | null): McpConfigDraft {
  if (!mcpConfig) {
    return createMcpConfigDraft()
  }
  return {
    transport: mcpConfig.transport === 'stdio' ? 'stdio' : 'http',
    name: mcpConfig.name ?? '',
    command: mcpConfig.command ?? '',
    args: mcpConfig.args ?? [],
    envEntries: recordToEntries(mcpConfig.env),
    envPassThrough: mcpConfig.envPassThrough ?? [],
    cwd: mcpConfig.cwd ?? '',
    url: mcpConfig.url ?? '',
    bearerTokenEnv: mcpConfig.bearerTokenEnv ?? '',
    headerEntries: recordToEntries(mcpConfig.headers),
    headersFromEnvEntries: recordToEntries(mcpConfig.headersFromEnv),
  }
}

function hasPersistedExternalConfig(config?: HiringExternalSystemConfig | null): boolean {
  if (!config) return false
  if (config.submissionMode === 'skipped') return true
  const mcp = config.mcpServer
  return config.cliTools.length > 0
    || Boolean(mcp?.command?.trim())
    || Boolean(mcp?.url?.trim())
}

// ── Final 卡片（生成实例包） ──────────────────────────────────────────────────

function FinalCard({
  canGenerate, generated, expanded, isFocus, onToggle, onGenerate, onEnterEvaluation,
}: {
  canGenerate: boolean
  generated: boolean
  expanded: boolean
  isFocus: boolean
  onToggle: () => void
  onGenerate?: () => void
  onEnterEvaluation?: () => void
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
          {generated && onEnterEvaluation && (
            <button
              type="button"
              className="hb-todo-row-btn is-primary"
              onClick={onEnterEvaluation}
            >
              <ArrowRight size={14} strokeWidth={2.2} />
              {t('hiring.todo.final.enterEvaluationBtn')}
            </button>
          )}
        </div>
      )}
    </div>
  )
}
