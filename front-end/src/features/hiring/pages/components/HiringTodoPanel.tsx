/**
 * HiringTodoPanel — 雇佣 TODO 交互面板（重构版）
 *
 * 按阶段（资料 / 技能 / 外部系统 / 生成实例包）展示 AI 通过 MCP 工具创建的 handoff 事项。
 * 每个 TodoRow 按 kind 内联展开对应交互区，无弹窗遮挡：
 *   - file_request    → 内联拖拽上传区（文档资料）
 *   - skill_upload    → 内联技能包上传区（.zip + 版本说明）
 *   - external_config → 内联配置表单（API URL / 密钥等）
 *   - handoff_todo    → 普通确认/忽略
 */
import { useRef, useState } from 'react'
import clsx from 'clsx'
import type { HandoffItem } from '@/infra/api'

// ── 阶段配置 ──────────────────────────────────────────────────────────────────

interface StageConfig {
  key: string
  num: string
  title: string
  emptyHint: string
}

const STAGES: StageConfig[] = [
  { key: 'material', num: '①', title: '资料',     emptyHint: '等待 AI 分析业务场景后生成…' },
  { key: 'skill',    num: '②', title: '技能',     emptyHint: '等待资料阶段确认后推断…'      },
  { key: 'external', num: '③', title: '外部系统', emptyHint: '等待 AI 识别外部系统依赖…'   },
]

// ── 辅助判断 ──────────────────────────────────────────────────────────────────

function isConfirmed(item: HandoffItem) { return item.status === 'confirmed' }
function isDismissed(item: HandoffItem) { return item.status === 'dismissed' }

function todoKind(item: HandoffItem): 'file_request' | 'skill_upload' | 'external_config' | 'handoff_todo' {
  if (item.kind === 'file_request')    return 'file_request'
  if (item.kind === 'skill_upload')    return 'skill_upload'
  if (item.kind === 'external_config') return 'external_config'
  // 兼容旧数据：category / payload 推断
  if (item.category === 'material' || (item.payload?.['_upload'] as boolean | undefined) === true)
    return 'file_request'
  if (item.category === 'skill_upload') return 'skill_upload'
  if (item.stage === 'external' && item.payload?.['form_fields']) return 'external_config'
  return 'handoff_todo'
}

function stageLabelShort(stage: string) {
  return ({ material: '资料', skill: '技能', external: '外部系统', ready_for_packaging: '打包' } as Record<string, string>)[stage] ?? stage
}

function kindLabel(kind: string) {
  return (
    { file_request: '文件上传', skill_upload: '技能包', external_config: '外部配置', handoff_todo: '待确认' } as Record<string, string>
  )[kind] ?? kind
}

function actionLabel(kind: string) {
  return (
    { file_request: '上传文件 ▾', skill_upload: '上传技能包 ▾', external_config: '填写配置 ▾' } as Record<string, string>
  )[kind] ?? '操作 ▾'
}

function doneTotalByStage(items: HandoffItem[], key: string) {
  const s = items.filter(i => i.stage === key && !isDismissed(i))
  return { done: s.filter(isConfirmed).length, total: s.length }
}

function allDone(items: HandoffItem[]) {
  const active = items.filter(i => !isDismissed(i) && i.stage !== 'ready_for_packaging')
  return active.length > 0 && active.every(isConfirmed)
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1048576).toFixed(1)} MB`
}

// ── 表单字段类型（外部系统配置） ───────────────────────────────────────────────

interface FormField {
  id: string
  label: string
  type: 'text' | 'password' | 'number' | 'select'
  required?: boolean
  default?: string
  placeholder?: string
  hint?: string
  options?: string[]
}

// ── Props ─────────────────────────────────────────────────────────────────────

export interface HiringTodoPanelProps {
  handoffItems: HandoffItem[]
  /** 新到达的 handoffId 集合，用于入场 flash 动画（父组件维护，约 800ms 后清除） */
  newHandoffIds?: Set<string>
  onConfirmTodo: (handoffId: string) => Promise<void>
  onDismissTodo: (handoffId: string) => Promise<void>
  /** 文件上传（file_request）：handoffId + 文件 */
  onUploadFile: (handoffId: string, file: File) => Promise<void>
  /** 技能包上传（skill_upload）：handoffId + 文件 + 元数据 */
  onUploadSkill: (handoffId: string, file: File, meta: { name: string; releaseNote: string; description: string }) => Promise<void>
  /** 外部系统配置保存（external_config） */
  onSaveExternalConfig: (handoffId: string, config: Record<string, string>) => Promise<void>
  onGenerate?: () => void
  generated?: boolean
}

// ── 主组件 ────────────────────────────────────────────────────────────────────

export function HiringTodoPanel({
  handoffItems,
  newHandoffIds = new Set(),
  onConfirmTodo,
  onDismissTodo,
  onUploadFile,
  onUploadSkill,
  onSaveExternalConfig,
  onGenerate,
  generated = false,
}: HiringTodoPanelProps) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({
    material: true, skill: true, external: true, final: true,
  })
  const toggle = (key: string) => setExpanded(prev => ({ ...prev, [key]: !prev[key] }))

  const stats = STAGES.map(s => doneTotalByStage(handoffItems, s.key))
  const canGenerate = allDone(handoffItems)

  return (
    <div className="hb-todo-panel">
      <div className="hb-todo-panel-head">
        <p className="hb-hiring-eyebrow">TODO PANEL</p>
        <h3 className="hb-hiring-panel-title">待办事项</h3>
      </div>

      <div className="hb-todo-panel-body">
        {STAGES.map((stage, idx) => {
          const items = handoffItems.filter(i => i.stage === stage.key)
          const stat = stats[idx]
          const locked = stage.key === 'external' ? stats[0].done === 0 && stats[1].done === 0 : false
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
              {items.map(item => (
                <TodoRow
                  key={item.handoff_id}
                  item={item}
                  isNew={newHandoffIds.has(item.handoff_id)}
                  onConfirm={() => onConfirmTodo(item.handoff_id)}
                  onDismiss={() => onDismissTodo(item.handoff_id)}
                  onUploadFile={file => onUploadFile(item.handoff_id, file)}
                  onUploadSkill={(file, meta) => onUploadSkill(item.handoff_id, file, meta)}
                  onSaveConfig={cfg => onSaveExternalConfig(item.handoff_id, cfg)}
                />
              ))}
            </StageCard>
          )
        })}

        <FinalCard
          stats={stats}
          canGenerate={canGenerate}
          generated={generated}
          expanded={expanded['final']}
          onToggle={() => toggle('final')}
          onGenerate={onGenerate}
        />
      </div>
    </div>
  )
}

// ── 阶段卡片 ──────────────────────────────────────────────────────────────────

function StageCard({
  num, title, stat, locked, isComplete, expanded, onToggle, emptyHint, children,
}: {
  num: string; title: string; stat: { done: number; total: number }
  locked: boolean; isComplete: boolean; expanded: boolean
  onToggle: () => void; emptyHint: string; children: React.ReactNode
}) {
  const hasAny = stat.total > 0
  return (
    <div className={clsx('hb-todo-stage-card', isComplete && 'is-complete', !isComplete && hasAny && 'is-active', locked && 'is-locked')}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">{num}</span>
        <span className="hb-todo-stage-title">{title}</span>
        <span className={clsx('hb-todo-stage-badge', isComplete ? 'is-complete' : hasAny ? 'is-active' : '')}>
          {isComplete ? '已完成' : hasAny ? `${stat.done}/${stat.total}` : '等待'}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className={clsx('hb-todo-stage-body', !hasAny && 'is-empty')}>
          {!hasAny
            ? <p className="hb-todo-stage-empty">{locked ? '等待前序阶段确认后自动生成…' : emptyHint}</p>
            : children}
        </div>
      )}
    </div>
  )
}

// ── TodoRow：按 kind 路由到对应内联交互区 ────────────────────────────────────

interface TodoRowProps {
  item: HandoffItem
  isNew: boolean
  onConfirm: () => Promise<void>
  onDismiss: () => Promise<void>
  onUploadFile: (file: File) => Promise<void>
  onUploadSkill: (file: File, meta: { name: string; releaseNote: string; description: string }) => Promise<void>
  onSaveConfig: (cfg: Record<string, string>) => Promise<void>
}

function TodoRow({ item, isNew, onConfirm, onDismiss, onUploadFile, onUploadSkill, onSaveConfig }: TodoRowProps) {
  const [inlineOpen, setInlineOpen] = useState(false)
  const confirmed = isConfirmed(item)
  const dismissed = isDismissed(item)
  const kind = todoKind(item)
  const hasInline = kind !== 'handoff_todo'

  return (
    <div className={clsx('hb-todo-row', isNew && 'is-new', confirmed && 'is-confirmed', dismissed && 'is-dismissed')}>
      {/* 行头 */}
      <div className="hb-todo-row-main">
        <span className={clsx('hb-todo-row-dot', confirmed ? 'is-done' : dismissed ? 'is-dismissed' : 'is-open')} />

        <div className="hb-todo-row-content">
          <div className="hb-todo-row-title-row">
            <strong className="hb-todo-row-title">{item.title}</strong>
            <div className="hb-todo-row-tags">
              {item.stage && <span className="hb-todo-row-tag">{stageLabelShort(item.stage)}</span>}
              <span className={clsx('hb-todo-row-tag', `is-kind-${kind}`)}>{kindLabel(kind)}</span>
            </div>
          </div>
          {item.intent && <p className="hb-todo-row-desc">{item.intent}</p>}
          {/* 验收条件 */}
          {item.acceptance && !confirmed && (
            <p className="hb-todo-row-acceptance">验收：{item.acceptance}</p>
          )}
        </div>

        {/* 右侧操作按钮 */}
        <div className="hb-todo-row-actions">
          {confirmed ? (
            <>
              <span className="hb-todo-row-confirmed-badge">✓ 已完成</span>
              <InlineBtn variant="ghost" onClick={onDismiss}>撤销</InlineBtn>
            </>
          ) : dismissed ? (
            <InlineBtn variant="ghost" onClick={onConfirm}>重新确认</InlineBtn>
          ) : kind === 'handoff_todo' ? (
            <>
              <InlineBtn variant="ghost" onClick={onDismiss}>忽略</InlineBtn>
              <InlineBtn variant="primary" onClick={onConfirm}>确认可用</InlineBtn>
            </>
          ) : (
            <>
              <InlineBtn variant="ghost" onClick={onDismiss}>忽略</InlineBtn>
              <button
                type="button"
                className={clsx('hb-todo-row-btn is-secondary', inlineOpen && 'is-expanded')}
                onClick={() => setInlineOpen(v => !v)}
              >
                {inlineOpen ? '收起 ▴' : actionLabel(kind)}
              </button>
            </>
          )}
        </div>
      </div>

      {/* 内联交互区 */}
      {hasInline && !confirmed && !dismissed && inlineOpen && (
        <div className="hb-todo-inline-body">
          {kind === 'file_request' && (
            <FileUploadInline
              item={item}
              onSave={async file => { await onUploadFile(file); setInlineOpen(false) }}
              onCancel={() => setInlineOpen(false)}
            />
          )}
          {kind === 'skill_upload' && (
            <SkillUploadInline
              item={item}
              onSave={async (file, meta) => { await onUploadSkill(file, meta); setInlineOpen(false) }}
              onCancel={() => setInlineOpen(false)}
            />
          )}
          {kind === 'external_config' && (
            <ExternalConfigInline
              item={item}
              onSave={async cfg => { await onSaveConfig(cfg); setInlineOpen(false) }}
              onCancel={() => setInlineOpen(false)}
            />
          )}
        </div>
      )}
    </div>
  )
}

// ── InlineBtn：带 loading 状态的小按钮 ────────────────────────────────────────

function InlineBtn({ variant, onClick, children, disabled }: {
  variant: 'primary' | 'ghost' | 'secondary'
  onClick: () => Promise<void>
  children: React.ReactNode
  disabled?: boolean
}) {
  const [busy, setBusy] = useState(false)
  return (
    <button
      type="button"
      className={clsx('hb-todo-row-btn', `is-${variant}`)}
      disabled={busy || disabled}
      onClick={async () => {
        setBusy(true)
        try { await onClick() } finally { setBusy(false) }
      }}
    >
      {busy ? '…' : children}
    </button>
  )
}

// ── 内联文件上传（file_request） ──────────────────────────────────────────────

function FileUploadInline({ item, onSave, onCancel }: {
  item: HandoffItem
  onSave: (file: File) => Promise<void>
  onCancel: () => void
}) {
  const [file, setFile] = useState<File | null>(null)
  const [busy, setBusy] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const guidance = (item.payload?.['guidance'] as string | undefined)
    ?? (item.payload?.['description'] as string | undefined)

  return (
    <div className="hb-todo-inline-section">
      {guidance && <p className="hb-todo-inline-guide">{guidance}</p>}
      <div
        className={clsx('hb-todo-drop-zone', dragOver && 'is-over', file && 'has-file')}
        onDragOver={e => { e.preventDefault(); setDragOver(true) }}
        onDragLeave={() => setDragOver(false)}
        onDrop={e => { e.preventDefault(); setDragOver(false); const f = e.dataTransfer.files[0]; if (f) setFile(f) }}
        onClick={() => !file && inputRef.current?.click()}
      >
        {file ? (
          <div className="hb-todo-drop-selected">
            <span className="hb-todo-drop-icon">📄</span>
            <div className="hb-todo-drop-info">
              <span className="hb-todo-drop-name">{file.name}</span>
              <span className="hb-todo-drop-size">{formatSize(file.size)}</span>
            </div>
            <button type="button" className="hb-todo-drop-remove" onClick={e => { e.stopPropagation(); setFile(null) }}>✕</button>
          </div>
        ) : (
          <>
            <span className="hb-todo-drop-icon">⬆</span>
            <span className="hb-todo-drop-hint">拖拽文件到此，或点击选择</span>
            <span className="hb-todo-drop-types">PDF · DOCX · XLSX · MD · TXT · ≤ 50 MB</span>
          </>
        )}
        <input ref={inputRef} type="file" style={{ display: 'none' }}
          accept=".pdf,.docx,.doc,.xlsx,.xls,.md,.txt"
          onChange={e => { const f = e.target.files?.[0]; if (f) setFile(f) }} />
      </div>
      <div className="hb-todo-inline-foot">
        <button type="button" className="hb-todo-row-btn is-ghost" onClick={onCancel}>取消</button>
        <button type="button" className="hb-todo-row-btn is-primary" disabled={!file || busy}
          onClick={async () => {
            if (!file) return
            setBusy(true)
            try { await onSave(file) } finally { setBusy(false) }
          }}>
          {busy ? '上传中…' : '确认入库'}
        </button>
      </div>
    </div>
  )
}

// ── 内联技能包上传（skill_upload） ────────────────────────────────────────────

function SkillUploadInline({ item, onSave, onCancel }: {
  item: HandoffItem
  onSave: (file: File, meta: { name: string; releaseNote: string; description: string }) => Promise<void>
  onCancel: () => void
}) {
  const defaultName = (item.payload?.['skill_name'] as string | undefined) ?? item.target_skill ?? ''
  const [file, setFile] = useState<File | null>(null)
  const [name, setName] = useState(defaultName)
  const [releaseNote, setReleaseNote] = useState('')
  const [description, setDescription] = useState(
    (item.payload?.['description'] as string | undefined) ?? item.intent ?? ''
  )
  const [busy, setBusy] = useState(false)
  const [dragOver, setDragOver] = useState(false)
  const inputRef = useRef<HTMLInputElement>(null)

  const canSubmit = file && name.trim()

  return (
    <div className="hb-todo-inline-section">
      <div className="hb-todo-inline-fields">
        <div className="hb-todo-inline-field">
          <label className="hb-todo-inline-label">技能名称 <span className="hb-config-required">*</span></label>
          <input className="hb-config-input" value={name} onChange={e => setName(e.target.value)} placeholder="如 material-ingestion" />
        </div>
        <div className="hb-todo-inline-field">
          <label className="hb-todo-inline-label">版本说明</label>
          <input className="hb-config-input" value={releaseNote} onChange={e => setReleaseNote(e.target.value)} placeholder="如 v1.0 初始版本" />
        </div>
        <div className="hb-todo-inline-field is-full">
          <label className="hb-todo-inline-label">技能描述</label>
          <textarea className="hb-config-input is-textarea" rows={2} value={description}
            onChange={e => setDescription(e.target.value)} placeholder="简要描述技能包的功能和用途" />
        </div>
      </div>
      <div
        className={clsx('hb-todo-drop-zone', dragOver && 'is-over', file && 'has-file')}
        onDragOver={e => { e.preventDefault(); setDragOver(true) }}
        onDragLeave={() => setDragOver(false)}
        onDrop={e => { e.preventDefault(); setDragOver(false); const f = e.dataTransfer.files[0]; if (f) setFile(f) }}
        onClick={() => !file && inputRef.current?.click()}
      >
        {file ? (
          <div className="hb-todo-drop-selected">
            <span className="hb-todo-drop-icon">📦</span>
            <div className="hb-todo-drop-info">
              <span className="hb-todo-drop-name">{file.name}</span>
              <span className="hb-todo-drop-size">{formatSize(file.size)}</span>
            </div>
            <button type="button" className="hb-todo-drop-remove" onClick={e => { e.stopPropagation(); setFile(null) }}>✕</button>
          </div>
        ) : (
          <>
            <span className="hb-todo-drop-icon">📦</span>
            <span className="hb-todo-drop-hint">拖拽 .zip 技能包到此，或点击选择</span>
            <span className="hb-todo-drop-types">仅支持 .zip 格式 · ≤ 100 MB</span>
          </>
        )}
        <input ref={inputRef} type="file" style={{ display: 'none' }} accept=".zip"
          onChange={e => { const f = e.target.files?.[0]; if (f) setFile(f) }} />
      </div>
      <div className="hb-todo-inline-foot">
        <button type="button" className="hb-todo-row-btn is-ghost" onClick={onCancel}>取消</button>
        <button type="button" className="hb-todo-row-btn is-primary" disabled={!canSubmit || busy}
          onClick={async () => {
            if (!file) return
            setBusy(true)
            try { await onSave(file, { name: name.trim(), releaseNote: releaseNote.trim(), description: description.trim() }) }
            finally { setBusy(false) }
          }}>
          {busy ? '上传中…' : '上传技能包'}
        </button>
      </div>
    </div>
  )
}

// ── 内联外部系统配置（external_config） ──────────────────────────────────────

function ExternalConfigInline({ item, onSave, onCancel }: {
  item: HandoffItem
  onSave: (cfg: Record<string, string>) => Promise<void>
  onCancel: () => void
}) {
  const rawFields = item.payload?.['form_fields']
  const formFields: FormField[] = Array.isArray(rawFields) ? (rawFields as FormField[]) : []

  const [values, setValues] = useState<Record<string, string>>(() => {
    const init: Record<string, string> = {}
    formFields.forEach(f => { init[f.id] = f.default ?? '' })
    return init
  })
  // 无 form_fields 时允许自由文本输入
  const [freeText, setFreeText] = useState('')
  const [busy, setBusy] = useState(false)

  const isValid = formFields.length === 0
    ? freeText.trim().length > 0
    : formFields.filter(f => f.required).every(f => values[f.id]?.trim())

  const systemName = (item.payload?.['system_name'] as string | undefined) ?? item.title

  return (
    <div className="hb-todo-inline-section">
      {formFields.length === 0 ? (
        <div className="hb-todo-inline-field is-full">
          <label className="hb-todo-inline-label">
            {systemName} 接入配置
            <span className="hb-config-hint">（AI 未提供结构化字段，请填写关键配置信息）</span>
          </label>
          <textarea className="hb-config-input is-textarea" rows={3} value={freeText}
            onChange={e => setFreeText(e.target.value)}
            placeholder="如：API URL、密钥、服务名等配置信息" />
        </div>
      ) : (
        <div className="hb-todo-inline-fields">
          {formFields.map(field => (
            <div key={field.id} className="hb-todo-inline-field">
              <label className="hb-todo-inline-label">
                {field.label}
                {field.required && <span className="hb-config-required"> *</span>}
                {field.hint && <span className="hb-config-hint"> {field.hint}</span>}
              </label>
              {field.type === 'select' ? (
                <select className="hb-config-input" value={values[field.id] ?? ''}
                  onChange={e => setValues(v => ({ ...v, [field.id]: e.target.value }))}>
                  <option value="">请选择…</option>
                  {field.options?.map(opt => <option key={opt} value={opt}>{opt}</option>)}
                </select>
              ) : (
                <input className="hb-config-input" type={field.type}
                  placeholder={field.placeholder}
                  value={values[field.id] ?? ''}
                  onChange={e => setValues(v => ({ ...v, [field.id]: e.target.value }))} />
              )}
            </div>
          ))}
        </div>
      )}
      <div className="hb-todo-inline-foot">
        <button type="button" className="hb-todo-row-btn is-ghost" onClick={onCancel}>取消</button>
        <button type="button" className="hb-todo-row-btn is-primary" disabled={!isValid || busy}
          onClick={async () => {
            setBusy(true)
            try {
              const cfg = formFields.length === 0 ? { _free_text: freeText.trim() } : values
              await onSave(cfg)
            } finally { setBusy(false) }
          }}>
          {busy ? '保存中…' : '保存配置'}
        </button>
      </div>
    </div>
  )
}

// ── 生成实例包卡片 ─────────────────────────────────────────────────────────────

function FinalCard({ stats, canGenerate, generated, expanded, onToggle, onGenerate }: {
  stats: Array<{ done: number; total: number }>
  canGenerate: boolean; generated: boolean; expanded: boolean
  onToggle: () => void; onGenerate?: () => void
}) {
  return (
    <div className={clsx('hb-todo-stage-card', generated ? 'is-complete' : canGenerate ? 'is-active' : '')}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">④</span>
        <span className="hb-todo-stage-title">生成实例包</span>
        <span className={clsx('hb-todo-stage-badge', generated ? 'is-complete' : canGenerate ? 'is-active' : '')}>
          {generated ? '已生成' : canGenerate ? '可生成' : '等待前序'}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className="hb-todo-stage-body hb-todo-final-body">
          <div className="hb-todo-final-stats">
            {stats.map((stat, idx) => (
              <div key={idx} className={clsx('hb-todo-final-stat', stat.total > 0 && stat.done === stat.total && 'is-ok')}>
                <span className="hb-todo-final-stat-num">{stat.done}<em>/{stat.total || 0}</em></span>
                <span className="hb-todo-final-stat-lbl">{['① 资料', '② 技能', '③ 外部'][idx]}</span>
              </div>
            ))}
          </div>
          {generated ? (
            <div className="hb-todo-final-hint is-success">✓ 实例包已生成，可进入沙箱测试或部署生产</div>
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
