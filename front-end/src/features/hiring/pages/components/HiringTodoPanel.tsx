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

import { api, HiringCollectionStage } from '@/infra/api'
import type { HiringCollectionStageType, StoreSkillItem } from '@/infra/api'

// ── 类型 ──────────────────────────────────────────────────────────────────────

export type StageStatus = 'running' | 'completed' | 'failed'
export type StageKey = HiringCollectionStageType

interface UploadedFileMeta {
  relativePath: string
  sizeBytes: number
  format: string
}

interface LinkedSkill {
  skillId: string
  name: string
  version: string
}

export interface HiringTodoPanelProps {
  /** 当前雇佣会话 ID（用于上传/解析 todo 文件） */
  sessionId: string
  /** WS 阶段覆盖状态：由 HiringPage 聚合 artifact / skill_stage_gate 事件得到 */
  wsStageOverrides: Map<StageKey, StageStatus>
  /** 上传完成后回调（模拟用户消息驱动 AI 进入下一阶段） */
  onAfterStageMessage?: (stage: StageKey, summary: string) => void
  /** 触发生成实例包 */
  onGenerate?: () => void
  generated?: boolean
}

interface StageConfig {
  key: StageKey
  num: string
  title: string
  hint: string
}

const STAGES: StageConfig[] = [
  { key: HiringCollectionStage.Material, num: '①', title: '资料', hint: '上传 .md / .json 资料，供 AI 解析作为雇佣依据' },
  { key: HiringCollectionStage.Skill,    num: '②', title: '技能', hint: '从 Skills Hub 搜索并关联技能；外部系统配置为可选项' },
  { key: HiringCollectionStage.External, num: '③', title: '外部系统', hint: '可选：配置外部 API / 系统对接信息' },
]

// ── 工具方法 ─────────────────────────────────────────────────────────────────

const ALLOWED_EXTS = new Set(['.md', '.json'])

function fileExt(name: string): string {
  const idx = name.lastIndexOf('.')
  return idx < 0 ? '' : name.slice(idx).toLowerCase()
}

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1048576).toFixed(1)} MB`
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

export function HiringTodoPanel({
  sessionId,
  wsStageOverrides,
  onAfterStageMessage,
  onGenerate,
  generated = false,
}: HiringTodoPanelProps) {
  const [expanded, setExpanded] = useState<Record<string, boolean>>({
    material: true, skill: true, external: true, final: true,
  })
  const toggle = (key: string) => setExpanded(prev => ({ ...prev, [key]: !prev[key] }))

  const allDone = useMemo(
    () => STAGES.every(s => wsStageOverrides.get(s.key) === 'completed'),
    [wsStageOverrides],
  )

  return (
    <div className="hb-todo-panel">
      <div className="hb-todo-panel-head hb-todo-panel-head--compact">
        <h3 className="hb-todo-panel-title">待办事项</h3>
      </div>

      <div className="hb-todo-panel-body">
        {STAGES.map(stage => (
          <StageCard
            key={stage.key}
            stage={stage}
            status={wsStageOverrides.get(stage.key) ?? null}
            expanded={expanded[stage.key]}
            onToggle={() => toggle(stage.key)}
          >
            {stage.key === HiringCollectionStage.Material && (
              <MaterialCardBody
                sessionId={sessionId}
                onAfterUpload={summary => onAfterStageMessage?.(HiringCollectionStage.Material, summary)}
              />
            )}
            {stage.key === HiringCollectionStage.Skill && (
              <SkillCardBody
                onAfterLink={summary => onAfterStageMessage?.(HiringCollectionStage.Skill, summary)}
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
          expanded={expanded['final']}
          onToggle={() => toggle('final')}
          onGenerate={onGenerate}
        />
      </div>
    </div>
  )
}

// ── 阶段卡片外壳 ──────────────────────────────────────────────────────────────

function StageCard({
  stage, status, expanded, onToggle, children,
}: {
  stage: StageConfig
  status: StageStatus | null
  expanded: boolean
  onToggle: () => void
  children: React.ReactNode
}) {
  const isComplete = status === 'completed'
  const isActive = status === 'running'
  const isFailed = status === 'failed'
  return (
    <div className={clsx(
      'hb-todo-stage-card',
      isComplete && 'is-complete',
      isActive && 'is-active',
      isFailed && 'is-failed',
    )}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">{stage.num}</span>
        <span className="hb-todo-stage-title">{stage.title}</span>
        <span className={clsx('hb-todo-stage-badge', isComplete ? 'is-complete' : isActive ? 'is-active' : isFailed ? 'is-failed' : '')}>
          {isComplete ? '已完成' : isActive ? '进行中' : isFailed ? '失败' : '等待'}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className="hb-todo-stage-body">
          <p className="hb-todo-stage-hint">{stage.hint}</p>
          {children}
        </div>
      )}
    </div>
  )
}

// ── 资料卡（文件夹上传 .md/.json） ─────────────────────────────────────────────

function MaterialCardBody({
  sessionId, onAfterUpload,
}: { sessionId: string; onAfterUpload: (summary: string) => void }) {
  const folderInputRef = useRef<HTMLInputElement | null>(null)
  const fileInputRef = useRef<HTMLInputElement | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [uploaded, setUploaded] = useState<UploadedFileMeta[]>([])

  const refresh = useCallback(async () => {
    if (!sessionId) return
    try {
      const items = await api.hiringWorkflow.listTodoFiles(sessionId)
      setUploaded(items)
    } catch {
      // 忽略：列出失败不影响主流程
    }
  }, [sessionId])

  useEffect(() => { void refresh() }, [refresh])

  const handleFiles = useCallback(async (files: FileList | File[]) => {
    if (!sessionId) {
      setError('会话尚未就绪，请稍后再试')
      return
    }
    const arr = Array.from(files)
    if (arr.length === 0) return

    // 客户端校验：仅 .md / .json
    const invalid = arr.filter(f => !ALLOWED_EXTS.has(fileExt(f.name)))
    if (invalid.length > 0) {
      setError(`仅支持 .md 和 .json：${invalid.slice(0, 3).map(f => f.name).join('，')}${invalid.length > 3 ? '…' : ''}`)
      return
    }

    setError('')
    setBusy(true)
    try {
      // 按 webkitRelativePath 推导子文件夹，分组上传以保留目录结构
      const groups = new Map<string, File[]>()
      for (const f of arr) {
        const folder = deriveFolderFromWebkitPath(f) ?? ''
        const list = groups.get(folder) ?? []
        list.push(f)
        groups.set(folder, list)
      }

      let total = 0
      const names: string[] = []
      for (const [folder, fs] of groups.entries()) {
        const saved = await api.hiringWorkflow.uploadTodoFiles(sessionId, fs, folder || undefined)
        total += saved.length
        names.push(...saved.map(s => s.relativePath))
      }

      await refresh()
      const preview = names.slice(0, 5).join('、') + (names.length > 5 ? `…（共 ${names.length} 份）` : '')
      onAfterUpload(`已上传 ${total} 份资料：${preview}。请基于这些资料继续后续阶段。`)
    } catch (e) {
      setError(e instanceof Error ? e.message : '上传失败')
    } finally {
      setBusy(false)
    }
  }, [sessionId, refresh, onAfterUpload])

  return (
    <div className="hb-todo-mat">
      <div
        className={clsx('hb-todo-dropzone', busy && 'is-busy')}
        onDragOver={e => { e.preventDefault() }}
        onDrop={e => {
          e.preventDefault()
          if (busy) return
          if (e.dataTransfer.files?.length) void handleFiles(e.dataTransfer.files)
        }}
      >
        <p className="hb-todo-dropzone-title">将文件夹或文件拖到此处</p>
        <p className="hb-todo-dropzone-hint">仅支持 .md / .json，单次最大 50MB</p>
        <div className="hb-todo-dropzone-actions">
          <button type="button" className="hb-todo-row-btn is-secondary" disabled={busy}
            onClick={() => folderInputRef.current?.click()}>
            选择文件夹
          </button>
          <button type="button" className="hb-todo-row-btn is-secondary" disabled={busy}
            onClick={() => fileInputRef.current?.click()}>
            选择文件
          </button>
        </div>
        <input
          ref={folderInputRef}
          type="file"
          hidden
          multiple
          // @ts-expect-error webkitdirectory 为浏览器扩展属性，React 类型未声明
          webkitdirectory=""
          directory=""
          accept=".md,.json,application/json,text/markdown"
          onChange={e => { if (e.target.files) void handleFiles(e.target.files); e.target.value = '' }}
        />
        <input
          ref={fileInputRef}
          type="file"
          hidden
          multiple
          accept=".md,.json,application/json,text/markdown"
          onChange={e => { if (e.target.files) void handleFiles(e.target.files); e.target.value = '' }}
        />
      </div>

      {error && <p className="hb-todo-error">{error}</p>}

      {uploaded.length > 0 && (
        <ul className="hb-todo-file-list">
          {uploaded.slice(0, 20).map(f => (
            <li key={f.relativePath} className="hb-todo-file-item">
              <span className={clsx('hb-todo-file-fmt', `is-${f.format}`)}>{f.format}</span>
              <span className="hb-todo-file-path" title={f.relativePath}>{f.relativePath}</span>
              <span className="hb-todo-file-size">{formatSize(f.sizeBytes)}</span>
            </li>
          ))}
          {uploaded.length > 20 && (
            <li className="hb-todo-file-item is-more">…共 {uploaded.length} 份</li>
          )}
        </ul>
      )}
    </div>
  )
}

// ── 技能卡（搜索 Skills Hub） ─────────────────────────────────────────────────

function SkillCardBody({ onAfterLink }: { onAfterLink: (summary: string) => void }) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<StoreSkillItem[]>([])
  const [total, setTotal] = useState(0)
  const [linked, setLinked] = useState<LinkedSkill[]>([])
  const [searching, setSearching] = useState(false)
  const [error, setError] = useState('')

  // 防抖搜索：对接模板池同源接口 /api/store/skills?page=1&pageSize=12&q=...
  useEffect(() => {
    const q = query.trim()
    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      setSearching(true)
      try {
        const data = await api.skillCatalog.searchStoreSkills(
          { q: q || undefined, page: 1, pageSize: 12 },
          controller.signal,
        )
        setResults(data?.items ?? [])
        setTotal(data?.total ?? data?.items?.length ?? 0)
        setError('')
      } catch (e) {
        if ((e as { name?: string })?.name === 'AbortError') return
        setError(e instanceof Error ? e.message : '搜索失败')
      } finally {
        setSearching(false)
      }
    }, 300)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [query])

  const isLinked = useCallback((id: string) => linked.some(l => l.skillId === id), [linked])

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
      <input
        type="text"
        className="hb-todo-input"
        placeholder="搜索技能名称 / 关键字（留空显示推荐技能）"
        value={query}
        onChange={e => setQuery(e.target.value)}
      />
      {searching && <p className="hb-todo-hint-muted">搜索中…</p>}
      {error && <p className="hb-todo-error">{error}</p>}
      {!searching && !error && results.length === 0 && (
        <p className="hb-todo-hint-muted">{query.trim() ? '未找到匹配的技能' : '暂无技能'}</p>
      )}

      {results.length > 0 && (
        <>
          {total > results.length && (
            <p className="hb-todo-hint-muted">共 {total} 个结果，显示前 {results.length} 个</p>
          )}
          <ul className="hb-todo-skill-list">
            {results.map(s => {
              const displayName = s.displayName ?? s.name
              return (
                <li key={s.id} className="hb-todo-skill-item">
                  <div className="hb-todo-skill-info">
                    <strong>{displayName}</strong>
                    <span className="hb-todo-skill-meta">
                      {s.currentVersion ? `v${s.currentVersion}` : ''}{s.level ? ` · ${s.level}` : ''}
                    </span>
                    {s.description && <p className="hb-todo-skill-desc">{s.description}</p>}
                    {s.tags && s.tags.length > 0 && (
                      <ul className="hb-todo-tag-list">
                        {s.tags.slice(0, 5).map(t => <li key={t} className="hb-todo-tag is-mini">{t}</li>)}
                      </ul>
                    )}
                  </div>
                  <button
                    type="button"
                    className={clsx('hb-todo-row-btn', isLinked(s.id) ? 'is-ghost' : 'is-primary')}
                    disabled={isLinked(s.id)}
                    onClick={() => handleLink(s)}
                  >
                    {isLinked(s.id) ? '已关联' : '关联'}
                  </button>
                </li>
              )
            })}
          </ul>
        </>
      )}

      {linked.length > 0 && (
        <div className="hb-todo-skill-linked">
          <p className="hb-todo-hint-muted">已关联 {linked.length} 个技能</p>
          <ul className="hb-todo-tag-list">
            {linked.map(l => (
              <li key={l.skillId} className="hb-todo-tag">
                {l.name}
                <button type="button" className="hb-todo-tag-x" onClick={() => handleUnlink(l.skillId)}>×</button>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}

// ── 外部系统卡（可选配置） ────────────────────────────────────────────────────

function ExternalCardBody({ onAfterSave }: { onAfterSave: (summary: string) => void }) {
  const [systemName, setSystemName] = useState('')
  const [apiUrl, setApiUrl] = useState('')
  const [token, setToken] = useState('')

  function handleSkip() {
    onAfterSave('外部系统配置已跳过（无需对接外部系统）。请继续。')
  }
  function handleSave() {
    const name = systemName.trim()
    if (!name) return
    onAfterSave(`已配置外部系统「${name}」，地址：${apiUrl || '(未填)'}。请继续。`)
  }

  return (
    <div className="hb-todo-external">
      <p className="hb-todo-hint-muted">外部系统对接为可选项，如无需对接可直接跳过。</p>
      <label className="hb-todo-field">
        <span>系统名称</span>
        <input type="text" className="hb-todo-input" value={systemName}
          onChange={e => setSystemName(e.target.value)} placeholder="例如：钉钉 / 飞书 / 内部 HR" />
      </label>
      <label className="hb-todo-field">
        <span>API 地址</span>
        <input type="text" className="hb-todo-input" value={apiUrl}
          onChange={e => setApiUrl(e.target.value)} placeholder="https://..." />
      </label>
      <label className="hb-todo-field">
        <span>访问凭证</span>
        <input type="password" className="hb-todo-input" value={token}
          onChange={e => setToken(e.target.value)} placeholder="可选" />
      </label>
      <div className="hb-todo-actions-row">
        <button type="button" className="hb-todo-row-btn is-ghost" onClick={handleSkip}>跳过</button>
        <button type="button" className="hb-todo-row-btn is-primary"
          disabled={!systemName.trim()} onClick={handleSave}>保存并继续</button>
      </div>
    </div>
  )
}

// ── Final 卡片（生成实例包） ──────────────────────────────────────────────────

function FinalCard({
  canGenerate, generated, expanded, onToggle, onGenerate,
}: {
  canGenerate: boolean
  generated: boolean
  expanded: boolean
  onToggle: () => void
  onGenerate?: () => void
}) {
  return (
    <div className={clsx('hb-todo-stage-card', generated && 'is-complete', !generated && canGenerate && 'is-active')}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">④</span>
        <span className="hb-todo-stage-title">生成实例包</span>
        <span className={clsx('hb-todo-stage-badge', generated ? 'is-complete' : canGenerate ? 'is-active' : '')}>
          {generated ? '已生成' : canGenerate ? '可生成' : '等待'}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className="hb-todo-stage-body">
          <p className="hb-todo-stage-hint">
            前序阶段完成后，将整合资料、技能与可选配置，生成可下发到员工待上岗界面的实例模板包。
          </p>
          <button type="button"
            className={clsx('hb-todo-row-btn', canGenerate && !generated ? 'is-primary' : 'is-ghost')}
            disabled={!canGenerate || generated}
            onClick={onGenerate}>
            {generated ? '已生成' : '生成实例包'}
          </button>
        </div>
      )}
    </div>
  )
}
