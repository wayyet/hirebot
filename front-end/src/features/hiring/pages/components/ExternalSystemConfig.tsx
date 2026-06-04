/**
 * ExternalSystemConfig.tsx - 外部系统配置组件(CLI 工具 + MCP 服务)
 * 
 * 用于配置员工模板的外部系统集成：
 * - CLI 工具配置（命令、参数、执行模式）
 * - MCP 服务配置（传输方式、环境变量、请求头等）
 */

import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Eye, EyeOff, Loader2, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import i18n from '@/i18n'

import { api } from '@/infra/api'
import type { HiringExternalSystemConfig } from '@/infra/api'
import type { ExternalConfigChangeSource } from '../externalPackagingState'
import type {
  PendingStageAdvanceConfirmation,
  StageAdvanceIntent,
} from '../stageAdvanceConfirmation'

// ── 类型定义 ──────────────────────────────────────────────────────────────────

// SSE 和 Streamable HTTP 是目前支持的两种远程 MCP 传输方式
type McpTransport = 'sse' | 'streamable-http'

interface McpKeyValueEntry {
  id: string
  key: string
  value: string
}

// 仅保留 URL 相关字段，移除 stdio 本地进程字段
interface McpConfigDraft {
  transport: McpTransport
  name: string
  url: string
  bearerTokenEnv: string
  headerEntries: McpKeyValueEntry[]
}

type ExternalConfigModalType = 'cli' | 'mcp'

// ── 常量 ──────────────────────────────────────────────────────────────────────

const MCP_TRANSPORT_LABELS: Record<McpTransport, string> = {
  sse: 'SSE',
  'streamable-http': 'Streamable HTTP',
}

// ── 辅助函数 ──────────────────────────────────────────────────────────────────

function cloneMcpConfig(config: McpConfigDraft): McpConfigDraft {
  return {
    ...config,
    headerEntries: config.headerEntries.map(entry => ({ ...entry })),
  }
}

function createMcpConfigDraft(): McpConfigDraft {
  return {
    transport: 'streamable-http',
    name: '',
    url: '',
    bearerTokenEnv: '',
    headerEntries: [],
  }
}

function createEmptyKeyValueEntry(): McpKeyValueEntry {
  return { id: `kv-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`, key: '', value: '' }
}

function hasMeaningfulMcpConfig(config: McpConfigDraft): boolean {
  if (!config.name.trim()) return false
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

// 将旧传输类型映射到新类型：http/stdio 均视为 streamable-http
function resolveTransport(raw?: string | null): McpTransport {
  if (raw === 'sse') return 'sse'
  return 'streamable-http'
}

function createMcpConfigDraftFromConfig(mcpConfig?: HiringExternalSystemConfig['mcpServer'] | null): McpConfigDraft {
  if (!mcpConfig) {
    return createMcpConfigDraft()
  }
  return {
    transport: resolveTransport(mcpConfig.transport),
    name: mcpConfig.name ?? '',
    url: mcpConfig.url ?? '',
    bearerTokenEnv: mcpConfig.bearerTokenEnv ?? '',
    headerEntries: recordToEntries(mcpConfig.headers),
  }
}

// ── 组件定义 ──────────────────────────────────────────────────────────────────

export interface ExternalCardBodyProps {
  hireId: string
  isUnlocked: boolean
  onAfterSave: (summary: string, intent: StageAdvanceIntent) => void
  onConfigChange?: (config: HiringExternalSystemConfig | null, source?: ExternalConfigChangeSource) => void
  pendingConfirmation: PendingStageAdvanceConfirmation | null
  stageConfirmationBusy: boolean
  onContinueCollection?: () => void
  onConfirmAdvance?: () => void
}


export function ExternalCardBody({

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
  const [mcpConfig, setMcpConfig] = useState<McpConfigDraft>(createMcpConfigDraft())
  const [activeModal, setActiveModal] = useState<ExternalConfigModalType | null>(null)
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

  const hasMcpConfig = hasMeaningfulMcpConfig(mcpConfig)

  useEffect(() => {
    if (!persistedExternalConfig || hasHydratedExternalConfigRef.current) {
      return
    }

    setMcpConfig(createMcpConfigDraftFromConfig(persistedExternalConfig.mcpServer))
    setMcpDraftConfig(createMcpConfigDraftFromConfig(persistedExternalConfig.mcpServer))
    onConfigChange?.(persistedExternalConfig, 'hydrate')

    hasHydratedExternalConfigRef.current = true
  }, [onConfigChange, persistedExternalConfig])

  // 判断 MCP 弹窗是否有未保存的草稿修改
  function hasDraftChanges(): boolean {
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

  // 重新配置：将已跳过状态重置为 pending
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
      setMcpConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      setMcpDraftConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
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
      setMcpConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      setMcpDraftConfig(createMcpConfigDraftFromConfig(savedConfig.mcpServer))
      onConfigChange?.(savedConfig, 'skip')
      onAfterSave(i18n.t('hiring.todo.external.skipMessage'), 'skip')
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '外部系统配置跳过失败')
    } finally {
      setIsSaving(false)
    }
  }

  // 继续：触发阶段推进确认流程
  function handleContinue() {
    const transportLabel = MCP_TRANSPORT_LABELS[mcpConfig.transport]
    const summary = `MCP ${mcpConfig.name.trim()}（${transportLabel}）URL: ${mcpConfig.url.trim()} 已配置，外部阶段完成，请继续下一步。`
    onAfterSave(summary, 'ready_to_advance')
  }

  function handleOpenMcpModal() {
    setMcpDraftConfig(cloneMcpConfig(mcpConfig))
    setSaveError('')
    setActiveModal('mcp')
  }

  // MCP 弹窗内的保存：调用接口后关闭弹窗
  async function handleSaveMcpToApi() {
    const url = mcpDraftConfig.url.trim()
    const name = mcpDraftConfig.name.trim()
    if (!name) {
      setFieldErrors(prev => ({ ...prev, mcpName: '请填写名称' }))
      return
    }
    if (!url) {
      setFieldErrors(prev => ({ ...prev, mcpUrl: t('hiring.todo.external.urlRequired') || '请填写 URL' }))
      return
    }
    if (!/^https?:\/\/.+/.test(url)) {
      setFieldErrors(prev => ({ ...prev, mcpUrl: t('hiring.todo.external.urlInvalid') }))
      return
    }

    setSaveError('')
    setIsSaving(true)
    try {
      const savedConfig = await api.hiringWorkflow.saveExternalConfig(hireId, {
        submissionMode: 'configured',
        cliTools: persistedExternalConfig?.cliTools ?? [],
        mcpServer: {
          transport: mcpDraftConfig.transport,
          name,
          url,
          bearerTokenEnv: mcpDraftConfig.bearerTokenEnv.trim() || undefined,
          headers: mcpDraftConfig.headerEntries.length > 0
            ? entriesToRecord(mcpDraftConfig.headerEntries)
            : undefined,
        },
      })
      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setMcpConfig(cloneMcpConfig(mcpDraftConfig))
      setActiveModal(null)
      setShowDiscardConfirm(false)
      onConfigChange?.(savedConfig, 'save')
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '保存失败')
    } finally {
      setIsSaving(false)
    }
  }

  function handleAddHeaderEntry() {
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

  function buildSaveSummary() {
    if (hasMcpConfig) {
      const transportLabel = MCP_TRANSPORT_LABELS[mcpConfig.transport]
      return `MCP ${mcpConfig.name.trim()}（${transportLabel}）URL: ${mcpConfig.url.trim()} 已配置，外部阶段完成，请继续下一步。`
    }
    return '外部阶段完成，请继续下一步。'
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
      ) : (
        <>
          {/* MCP 配置入口：始终显示，点击按钮进入配置弹窗 */}
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
                          {`」（${MCP_TRANSPORT_LABELS[mcpConfig.transport]}）${mcpConfig.url.trim()}`}
                        </>
                      )
                      : t('hiring.todo.external.mcpDescription')}
                  </p>
                </div>
                <span className="hb-todo-external-type-pill">MCP</span>
              </div>
              <button type="button" className="hb-todo-row-btn is-primary" onClick={handleOpenMcpModal}>
                {hasMcpConfig ? t('hiring.todo.external.editConfig') : '配置'}
              </button>
            </div>
          </section>

          {/* 外层操作行：只有跳过和继续，不含保存 */}
          <div className="hb-todo-actions-row">
            <button
              type="button"
              className="hb-todo-row-btn is-ghost"
              disabled={isSaving}
              onClick={() => { void handleSkip() }}
            >
              {t('hiring.todo.external.skip')}
            </button>
            <button
              type="button"
              className="hb-todo-row-btn is-primary"
              disabled={!hasMcpConfig}
              onClick={handleContinue}
            >
              继续
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

          {/* MCP 配置弹窗 */}
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
                      className={`hb-todo-input${fieldErrors['mcpName'] ? ' is-error' : ''}`}
                      value={mcpDraftConfig.name}
                      onChange={e => {
                        setMcpDraftConfig(prev => ({ ...prev, name: e.target.value }))
                        if (fieldErrors['mcpName']) clearFieldError('mcpName')
                      }}
                      placeholder="MCP server name"
                    />
                    {fieldErrors['mcpName'] && <p className="hb-todo-field-error">{fieldErrors['mcpName']}</p>}
                  </label>

                  {/* 传输方式 Tab 切换（仅支持 SSE 和 Streamable HTTP） */}
                  <div className="hb-todo-mcp-tabs" role="tablist" aria-label="MCP 传输方式">
                    {(['streamable-http', 'sse'] as const).map(transport => {
                      const selected = mcpDraftConfig.transport === transport
                      return (
                        <button
                          key={transport}
                          type="button"
                          role="tab"
                          aria-selected={selected}
                          className={clsx('hb-todo-mcp-tab', selected && 'is-active')}
                          onClick={() => setMcpDraftConfig(prev => ({ ...prev, transport }))}
                        >
                          {MCP_TRANSPORT_LABELS[transport]}
                        </button>
                      )
                    })}
                  </div>

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

                  {saveError && <p className="hb-todo-field-error" style={{ marginTop: 4 }}>{saveError}</p>}

                  <div className="hb-todo-mcp-footer">
                    <button
                      type="button"
                      className="hb-todo-mcp-save-btn"
                      disabled={isSaving}
                      onClick={() => { void handleSaveMcpToApi() }}
                    >
                      {isSaving
                        ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><Loader2 size={13} style={{ animation: 'spin 1s linear infinite' }} />保存中…</span>
                        : '保存'}
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
      {saveError && !activeModal && <p className="hb-todo-error">{saveError}</p>}
    </div>
  )
}

// ── StageAdvanceConfirmationPanel（本地私有副本，与 MaterialCard.tsx 保持一致）──

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
