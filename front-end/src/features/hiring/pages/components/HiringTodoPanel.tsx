/**
 * HiringTodoPanel — 雇佣 TODO 交互面板
 *
 * 展示 AI 在对话过程中通过 MCP 工具创建的 handoff todo 事项，
 * 按阶段（资料 / 技能 / 外部系统 / 生成实例包）分卡展示。
 * 支持用户确认/撤销/上传文件/填写外部系统配置，
 * 操作完成后通过回调通知父组件（父组件负责调用后端 API + 模拟发消息）。
 */
import { useRef, useState } from 'react'
import clsx from 'clsx'
import type { HandoffItem } from '@/infra/api'

// ── 阶段定义 ──────────────────────────────────────────────────────────────────

interface StageConfig {
  key: string
  num: string
  title: string
  emptyHint: string
}

const STAGES: StageConfig[] = [
  { key: 'material',  num: '①', title: '资料',        emptyHint: '等待 AI 分析业务场景后生成…' },
  { key: 'skill',     num: '②', title: '技能',        emptyHint: '等待资料阶段确认后推断…'       },
  { key: 'external',  num: '③', title: '外部系统',    emptyHint: '等待 AI 识别外部系统依赖…'      },
]

// ── 工具函数 ──────────────────────────────────────────────────────────────────

function isConfirmed(item: HandoffItem): boolean {
  return item.status === 'confirmed'
}

function isDismissed(item: HandoffItem): boolean {
  return item.status === 'dismissed'
}

/** 判断是否为文件上传类型 todo */
function isUploadTodo(item: HandoffItem): boolean {
  return (
    item.kind === 'file_request' ||
    item.category === 'material' ||
    (item.payload?.['_upload'] as boolean | undefined) === true
  )
}

/** 判断是否为外部系统配置类型 todo */
function isExternalTodo(item: HandoffItem): boolean {
  return item.stage === 'external' && Boolean(item.payload?.['form_fields'])
}

function doneTotalByStage(items: HandoffItem[], stageKey: string) {
  const stageItems = items.filter((i) => i.stage === stageKey && !isDismissed(i))
  return { done: stageItems.filter(isConfirmed).length, total: stageItems.length }
}

function allDone(items: HandoffItem[]): boolean {
  const active = items.filter((i) => !isDismissed(i) && i.stage !== 'ready_for_packaging')
  return active.length > 0 && active.every(isConfirmed)
}

// ── 外部系统表单字段定义 ────────────────────────────────────────────────────────

interface FormField {
  id: string
  label: string
  type: 'text' | 'password' | 'number' | 'select'
  required?: boolean
  default?: string
  placeholder?: string
  hint?: string
  options?: string[]
  col?: 'half'
}

// ── Props ─────────────────────────────────────────────────────────────────────

export interface HiringTodoPanelProps {
  handoffItems: HandoffItem[]
  /** 新到达的 handoffId 集合，用于入场 flash 动画（父组件维护，约 800ms 后清除） */
  newHandoffIds?: Set<string>
  onConfirmTodo: (handoffId: string) => Promise<void>
  onDismissTodo: (handoffId: string) => Promise<void>
  /** 文件上传类型 todo：用户选择文件后调用，传入 handoffId + 文件对象 */
  onUploadFile: (handoffId: string, file: File) => Promise<void>
  /** 外部系统配置类型 todo：用户填写配置后调用，传入 handoffId + 配置 map */
  onSaveExternalConfig: (handoffId: string, config: Record<string, string>) => Promise<void>
  /** 用户点击"生成实例包"按钮 */
  onGenerate?: () => void
  /** 实例包是否已生成 */
  generated?: boolean
}

// ── 主组件 ────────────────────────────────────────────────────────────────────

export function HiringTodoPanel({
  handoffItems,
  newHandoffIds = new Set(),
  onConfirmTodo,
  onDismissTodo,
  onUploadFile,
  onSaveExternalConfig,
  onGenerate,
  generated = false,
}: HiringTodoPanelProps) {
  // 各阶段折叠状态：默认全部展开
  const [expanded, setExpanded] = useState<Record<string, boolean>>({
    material: true,
    skill: true,
    external: true,
    final: true,
  })

  // 正在打开的弹窗
  const [uploadModalItem, setUploadModalItem] = useState<HandoffItem | null>(null)
  const [configModalItem, setConfigModalItem] = useState<HandoffItem | null>(null)

  const toggle = (key: string) =>
    setExpanded((prev) => ({ ...prev, [key]: !prev[key] }))

  const canGenerate = allDone(handoffItems)

  const stats = STAGES.map((s) => doneTotalByStage(handoffItems, s.key))

  return (
    <div className="hb-todo-panel">
      <div className="hb-todo-panel-head">
        <p className="hb-hiring-eyebrow">TODO PANEL</p>
        <h3 className="hb-hiring-panel-title">待办事项</h3>
      </div>

      <div className="hb-todo-panel-body">
        {/* 三个阶段卡片 */}
        {STAGES.map((stage, idx) => {
          const items = handoffItems.filter((i) => i.stage === stage.key)
          const stat = stats[idx]

          // 若技能/外部阶段无资料阶段已确认条目，则锁定
          const locked = stage.key === 'external'
            ? stats[0].done === 0 && stats[1].done === 0
            : false

          const isComplete = stat.total > 0 && stat.done === stat.total

          return (
            <StageCard
              key={stage.key}
              num={stage.num}
              title={stage.title}
              stat={stat}
              locked={locked}
              isComplete={isComplete}
              expanded={expanded[stage.key]}
              onToggle={() => toggle(stage.key)}
              emptyHint={stage.emptyHint}
            >
              {items.map((item) => (
                <TodoRow
                  key={item.handoff_id}
                  item={item}
                  isNew={newHandoffIds.has(item.handoff_id)}
                  onConfirm={() => onConfirmTodo(item.handoff_id)}
                  onDismiss={() => onDismissTodo(item.handoff_id)}
                  onUpload={() => setUploadModalItem(item)}
                  onOpenConfig={() => setConfigModalItem(item)}
                />
              ))}
            </StageCard>
          )
        })}

        {/* ④ 生成实例包卡片 */}
        <FinalCard
          stats={stats}
          canGenerate={canGenerate}
          generated={generated}
          expanded={expanded['final']}
          onToggle={() => toggle('final')}
          onGenerate={onGenerate}
        />
      </div>

      {/* 文件上传弹窗 */}
      {uploadModalItem && (
        <UploadModal
          item={uploadModalItem}
          onClose={() => setUploadModalItem(null)}
          onSave={async (file) => {
            await onUploadFile(uploadModalItem.handoff_id, file)
            setUploadModalItem(null)
          }}
        />
      )}

      {/* 外部系统配置弹窗 */}
      {configModalItem && (
        <ConfigModal
          item={configModalItem}
          onClose={() => setConfigModalItem(null)}
          onSave={async (config) => {
            await onSaveExternalConfig(configModalItem.handoff_id, config)
            setConfigModalItem(null)
          }}
        />
      )}
    </div>
  )
}

// ── 阶段卡片 ──────────────────────────────────────────────────────────────────

interface StageCardProps {
  num: string
  title: string
  stat: { done: number; total: number }
  locked: boolean
  isComplete: boolean
  expanded: boolean
  onToggle: () => void
  emptyHint: string
  children: React.ReactNode
}

function StageCard({
  num, title, stat, locked, isComplete, expanded, onToggle, emptyHint, children,
}: StageCardProps) {
  const hasAny = stat.total > 0

  return (
    <div className={clsx(
      'hb-todo-stage-card',
      isComplete && 'is-complete',
      !isComplete && hasAny && 'is-active',
      locked && 'is-locked',
    )}>
      <button
        type="button"
        className="hb-todo-stage-head"
        onClick={onToggle}
        aria-expanded={expanded}
      >
        <span className="hb-todo-stage-num">{num}</span>
        <span className="hb-todo-stage-title">{title}</span>
        <span className={clsx(
          'hb-todo-stage-badge',
          isComplete ? 'is-complete' : hasAny ? 'is-active' : '',
        )}>
          {isComplete ? '已完成' : hasAny ? `${stat.done}/${stat.total}` : '等待'}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>

      {expanded && (
        <div className={clsx('hb-todo-stage-body', !hasAny && 'is-empty')}>
          {!hasAny ? (
            <p className="hb-todo-stage-empty">
              {locked ? '等待前序阶段确认后自动生成…' : emptyHint}
            </p>
          ) : children}
        </div>
      )}
    </div>
  )
}

// ── TODO 行 ───────────────────────────────────────────────────────────────────

interface TodoRowProps {
  item: HandoffItem
  isNew: boolean
  onConfirm: () => void
  onDismiss: () => void
  onUpload: () => void
  onOpenConfig: () => void
}

function TodoRow({ item, isNew, onConfirm, onDismiss, onUpload, onOpenConfig }: TodoRowProps) {
  const [busy, setBusy] = useState(false)
  const confirmed = isConfirmed(item)
  const dismissed = isDismissed(item)
  const showUpload = isUploadTodo(item) && !confirmed
  const showConfig = isExternalTodo(item) && !confirmed

  const wrap = (fn: () => void) => async () => {
    setBusy(true)
    try { fn() } finally { setBusy(false) }
  }

  return (
    <div className={clsx(
      'hb-todo-row',
      isNew && 'is-new',
      confirmed && 'is-confirmed',
      dismissed && 'is-dismissed',
    )}>
      <div className="hb-todo-row-main">
        {/* 状态指示点 */}
        <span className={clsx(
          'hb-todo-row-dot',
          confirmed ? 'is-done' : dismissed ? 'is-dismissed' : 'is-open',
        )} />

        <div className="hb-todo-row-content">
          <strong className="hb-todo-row-title">{item.title}</strong>
          {item.intent && <p className="hb-todo-row-desc">{item.intent}</p>}
          <div className="hb-todo-row-meta">
            {item.stage && (
              <span className="hb-todo-row-tag">{stageLabelShort(item.stage)}</span>
            )}
            {item.category && (
              <span className="hb-todo-row-tag">{item.category}</span>
            )}
          </div>
        </div>
      </div>

      <div className="hb-todo-row-actions">
        {confirmed ? (
          <>
            <span className="hb-todo-row-confirmed-badge">已确认</span>
            <button
              type="button"
              className="hb-todo-row-btn is-ghost"
              disabled={busy}
              onClick={wrap(onDismiss)}
            >
              撤销
            </button>
          </>
        ) : dismissed ? (
          <button
            type="button"
            className="hb-todo-row-btn is-ghost"
            disabled={busy}
            onClick={wrap(onConfirm)}
          >
            重新确认
          </button>
        ) : (
          <>
            {showUpload && (
              <button
                type="button"
                className="hb-todo-row-btn is-secondary"
                onClick={onUpload}
              >
                上传文件
              </button>
            )}
            {showConfig && (
              <button
                type="button"
                className="hb-todo-row-btn is-secondary"
                onClick={onOpenConfig}
              >
                填写配置
              </button>
            )}
            <button
              type="button"
              className="hb-todo-row-btn is-primary"
              disabled={busy}
              onClick={wrap(onConfirm)}
            >
              确认可用
            </button>
          </>
        )}
      </div>
    </div>
  )
}

function stageLabelShort(stage: string): string {
  const map: Record<string, string> = {
    material: '资料',
    skill: '技能',
    external: '外部系统',
    ready_for_packaging: '打包',
  }
  return map[stage] ?? stage
}

// ── 生成实例包卡片 ─────────────────────────────────────────────────────────────

interface FinalCardProps {
  stats: Array<{ done: number; total: number }>
  canGenerate: boolean
  generated: boolean
  expanded: boolean
  onToggle: () => void
  onGenerate?: () => void
}

function FinalCard({ stats, canGenerate, generated, expanded, onToggle, onGenerate }: FinalCardProps) {
  return (
    <div className={clsx(
      'hb-todo-stage-card',
      generated ? 'is-complete' : canGenerate ? 'is-active' : '',
    )}>
      <button
        type="button"
        className="hb-todo-stage-head"
        onClick={onToggle}
        aria-expanded={expanded}
      >
        <span className="hb-todo-stage-num">④</span>
        <span className="hb-todo-stage-title">生成实例包</span>
        <span className={clsx(
          'hb-todo-stage-badge',
          generated ? 'is-complete' : canGenerate ? 'is-active' : '',
        )}>
          {generated ? '已生成' : canGenerate ? '可生成' : '等待前序'}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>

      {expanded && (
        <div className="hb-todo-stage-body hb-todo-final-body">
          {/* 三阶段统计 */}
          <div className="hb-todo-final-stats">
            {stats.map((stat, idx) => (
              <div
                key={idx}
                className={clsx(
                  'hb-todo-final-stat',
                  stat.total > 0 && stat.done === stat.total ? 'is-ok' : '',
                )}
              >
                <span className="hb-todo-final-stat-num">
                  {stat.done}
                  <em>/{stat.total || 0}</em>
                </span>
                <span className="hb-todo-final-stat-lbl">
                  {['① 资料', '② 技能', '③ 外部系统'][idx]}
                </span>
              </div>
            ))}
          </div>

          {generated ? (
            <div className="hb-todo-final-hint is-success">
              ✓ 实例包已生成，可进入沙箱测试或部署到生产环境
            </div>
          ) : (
            <button
              type="button"
              className={clsx('hb-todo-final-btn', !canGenerate && 'is-disabled')}
              disabled={!canGenerate}
              onClick={canGenerate ? onGenerate : undefined}
            >
              {canGenerate ? '生成实例包' : '完成前序待办后可用'}
            </button>
          )}
        </div>
      )}
    </div>
  )
}

// ── 文件上传弹窗 ───────────────────────────────────────────────────────────────

interface UploadModalProps {
  item: HandoffItem
  onClose: () => void
  onSave: (file: File) => Promise<void>
}

function UploadModal({ item, onClose, onSave }: UploadModalProps) {
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setDragOver(false)
    const dropped = e.dataTransfer.files[0]
    if (dropped) setFile(dropped)
  }

  const handleSave = async () => {
    if (!file) return
    setBusy(true)
    try { await onSave(file) } finally { setBusy(false) }
  }

  return (
    <div className="hb-modal-bg" onClick={onClose} role="dialog" aria-modal="true">
      <div className="hb-modal" onClick={(e) => e.stopPropagation()}>
        <div className="hb-modal-head">
          <div>
            <h3>上传 · {item.title}</h3>
            <p>将文件拖入下方区域，或选择本地文件</p>
          </div>
          <button type="button" className="hb-modal-close" onClick={onClose}>✕</button>
        </div>

        <div className="hb-modal-body">
          <div
            className={clsx('hb-upload-zone', dragOver && 'is-drag-over')}
            onDragOver={(e) => { e.preventDefault(); setDragOver(true) }}
            onDragLeave={() => setDragOver(false)}
            onDrop={handleDrop}
          >
            <span className="hb-upload-icon">↑</span>
            <p>拖拽文件到此处</p>
            <small>支持 PDF · DOCX · XLSX · MD · TXT，单文件 ≤ 50 MB</small>
            <button
              type="button"
              className="hb-todo-row-btn is-secondary"
              onClick={() => fileInputRef.current?.click()}
            >
              选择本地文件
            </button>
            <input
              ref={fileInputRef}
              type="file"
              style={{ display: 'none' }}
              accept=".pdf,.docx,.doc,.xlsx,.xls,.md,.txt"
              onChange={(e) => {
                const f = e.target.files?.[0]
                if (f) setFile(f)
              }}
            />
            {file && (
              <div className="hb-upload-selected">
                📄 {file.name} · {formatSize(file.size)}
              </div>
            )}
          </div>
        </div>

        <div className="hb-modal-foot">
          <button type="button" className="hb-todo-row-btn is-ghost" onClick={onClose}>取消</button>
          <button
            type="button"
            className="hb-todo-row-btn is-primary"
            disabled={!file || busy}
            onClick={handleSave}
          >
            {busy ? '处理中…' : '确认入库'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── 外部系统配置弹窗 ──────────────────────────────────────────────────────────

interface ConfigModalProps {
  item: HandoffItem
  onClose: () => void
  onSave: (config: Record<string, string>) => Promise<void>
}

function ConfigModal({ item, onClose, onSave }: ConfigModalProps) {
  // 从 payload.form_fields 读取字段定义（由 AI 写入）
  const formFields = (item.payload?.['form_fields'] as FormField[] | undefined) ?? []

  const [values, setValues] = useState<Record<string, string>>(() => {
    const init: Record<string, string> = {}
    formFields.forEach((f) => { init[f.id] = f.default ?? '' })
    return init
  })
  const [busy, setBusy] = useState(false)

  const isValid = formFields
    .filter((f) => f.required)
    .every((f) => values[f.id]?.trim())

  const handleSave = async () => {
    if (!isValid) return
    setBusy(true)
    try { await onSave(values) } finally { setBusy(false) }
  }

  return (
    <div className="hb-modal-bg" onClick={onClose} role="dialog" aria-modal="true">
      <div className="hb-modal" onClick={(e) => e.stopPropagation()}>
        <div className="hb-modal-head">
          <div>
            <h3>配置 · {item.title}</h3>
            <p>{(item.payload?.['subtitle'] as string | undefined) ?? '填写外部系统接入信息'}</p>
          </div>
          <button type="button" className="hb-modal-close" onClick={onClose}>✕</button>
        </div>

        <div className="hb-modal-body">
          {formFields.length === 0 ? (
            <p style={{ color: 'var(--ink-3)', fontSize: 13 }}>
              暂无配置字段（payload.form_fields 未设置）
            </p>
          ) : (
            <div className="hb-config-form">
              {formFields.map((field) => (
                <div
                  key={field.id}
                  className={clsx('hb-config-field', field.col === 'half' && 'is-half')}
                >
                  <label className="hb-config-label">
                    {field.label}
                    {field.required && <span className="hb-config-required">*</span>}
                    {field.hint && <small className="hb-config-hint">{field.hint}</small>}
                  </label>

                  {field.type === 'select' ? (
                    <select
                      className="hb-config-input"
                      value={values[field.id] ?? ''}
                      onChange={(e) => setValues((v) => ({ ...v, [field.id]: e.target.value }))}
                    >
                      <option value="">请选择…</option>
                      {field.options?.map((opt) => (
                        <option key={opt} value={opt}>{opt}</option>
                      ))}
                    </select>
                  ) : (
                    <input
                      className="hb-config-input"
                      type={field.type}
                      placeholder={field.placeholder}
                      value={values[field.id] ?? ''}
                      onChange={(e) => setValues((v) => ({ ...v, [field.id]: e.target.value }))}
                    />
                  )}
                </div>
              ))}
            </div>
          )}
        </div>

        <div className="hb-modal-foot">
          <button type="button" className="hb-todo-row-btn is-ghost" onClick={onClose}>取消</button>
          <button
            type="button"
            className="hb-todo-row-btn is-primary"
            disabled={!isValid || busy || formFields.length === 0}
            onClick={handleSave}
          >
            {busy ? '保存中…' : '保存配置'}
          </button>
        </div>
      </div>
    </div>
  )
}

// ── 工具函数 ──────────────────────────────────────────────────────────────────

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
