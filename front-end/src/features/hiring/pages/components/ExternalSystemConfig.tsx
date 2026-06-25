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
import { Activity, Eye, EyeOff, Loader2, Trash2 } from 'lucide-react'
import clsx from 'clsx'
import i18n from '@/i18n'

import { api } from '@/infra/api'
import type { HiringExternalSystemConfig, HiringMcpConnectivityTestResult } from '@/infra/api'
import type { ExternalConfigChangeSource } from '../externalPackagingState'
import type {
  PendingStageAdvanceConfirmation,
  StageAdvanceIntent,
} from '../stageAdvanceConfirmation'
import { ConfirmationActionPanel } from './ConfirmationActionPanel'

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

type McpServerPayload = NonNullable<HiringExternalSystemConfig['mcpServers']>[number]

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

function buildMcpServerPayload(config: McpConfigDraft): McpServerPayload {
  const headers = entriesToRecord(config.headerEntries)
  return {
    transport: config.transport,
    name: config.name.trim(),
    url: config.url.trim(),
    bearerTokenEnv: config.bearerTokenEnv.trim() || undefined,
    headers: Object.keys(headers).length > 0 ? headers : undefined,
  }
}

function buildMcpServerPayloads(configs: McpConfigDraft[]): McpServerPayload[] {
  return configs.map(buildMcpServerPayload)
}

function getMcpConfigKey(config: McpConfigDraft, index: number | 'draft'): string {
  return `${index}:${config.transport}:${config.name.trim()}:${config.url.trim()}`
}

function getMcpHeaderSummary(config: McpConfigDraft): string {
  const headerNames = config.headerEntries
    .map(entry => entry.key.trim())
    .filter(Boolean)

  if (headerNames.length === 0) {
    return ''
  }

  return headerNames.join('、')
}

function getMcpTestResultClass(result: HiringMcpConnectivityTestResult): string {
  if (result.success) {
    return 'is-success'
  }

  return result.status === 'auth_failed' ? 'is-warning' : 'is-error'
}

function formatMcpTestResult(result: HiringMcpConnectivityTestResult): string {
  return typeof result.latencyMs === 'number'
    ? `${result.message} · ${result.latencyMs}ms`
    : result.message
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

function createMcpConfigDraftsFromPersistedConfig(config?: HiringExternalSystemConfig | null): McpConfigDraft[] {
  // 优先使用新字段 mcpServers；旧数据回退到 mcpServer 单项
  const list = config?.mcpServers && config.mcpServers.length > 0
    ? config.mcpServers
    : config?.mcpServer
      ? [config.mcpServer]
      : []
  return list.map(createMcpConfigDraftFromConfig)
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
  // mcpConfigs: 已保存的 MCP 服务列表（与后端同步）
  const [mcpConfigs, setMcpConfigs] = useState<McpConfigDraft[]>([])
  // editingIndex: 正在编辑列表中第几项；null 表示新增
  const [editingIndex, setEditingIndex] = useState<number | null>(null)
  const [activeModal, setActiveModal] = useState<ExternalConfigModalType | null>(null)
  const [mcpModalView, setMcpModalView] = useState<'history' | 'form'>('history')
  const [mcpDraftConfig, setMcpDraftConfig] = useState<McpConfigDraft>(createMcpConfigDraft())
  const [saveError, setSaveError] = useState('')
  const [isSaving, setIsSaving] = useState(false)
  const [visibleSecrets, setVisibleSecrets] = useState<Record<string, boolean>>({})
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({})
  const [mcpTestResults, setMcpTestResults] = useState<Record<string, HiringMcpConnectivityTestResult>>({})
  const [testingMcpKey, setTestingMcpKey] = useState<string | null>(null)
  const [showDiscardConfirm, setShowDiscardConfirm] = useState(false)
  const [showSkipConfirm, setShowSkipConfirm] = useState(false)
  const hasHydratedExternalConfigRef = useRef(false)
  const externalConfigQueryKey = ['hiring-external-config', hireId] as const

  const {
    data: persistedExternalConfig,
    error: persistedExternalConfigError,
    isLoading: isExternalConfigLoading,
    isFetching: isMcpConfigFetching,
    isFetched: isMcpConfigFetched,
    refetch: refetchExternalConfig,
  } = useQuery({
    queryKey: externalConfigQueryKey,
    queryFn: () => api.hiringWorkflow.getExternalConfig(hireId),
    // 懒加载：不在页面挂载时请求，打开弹窗时才触发
    enabled: false,
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

  const hasMcpConfig = mcpConfigs.length > 0
  const isExternalConfigFinalized = persistedExternalConfig?.submissionMode === 'configured'

  useEffect(() => {
    if (!persistedExternalConfig || hasHydratedExternalConfigRef.current) {
      return
    }

    setMcpConfigs(createMcpConfigDraftsFromPersistedConfig(persistedExternalConfig))
    onConfigChange?.(persistedExternalConfig, 'hydrate')

    hasHydratedExternalConfigRef.current = true
  }, [onConfigChange, persistedExternalConfig])

  // 判断 MCP 弹窗是否有未保存的草稿修改
  function hasDraftChanges(): boolean {
    if (activeModal !== 'mcp') return false
    const base = editingIndex !== null ? mcpConfigs[editingIndex] : createMcpConfigDraft()
    return JSON.stringify(mcpDraftConfig) !== JSON.stringify(base)
  }

  // 统一的模态框关闭入口：处于 form 视图且有草稿变化时先弹出丢弃确认条
  function handleCloseModal() {
    if (mcpModalView === 'form' && hasDraftChanges()) {
      setShowDiscardConfirm(true)
    } else {
      setActiveModal(null)
      setMcpModalView('history')
      setShowDiscardConfirm(false)
    }
  }

  function confirmDiscard() {
    setActiveModal(null)
    setMcpModalView('history')
    setEditingIndex(null)
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
        mcpServers: [],
      })
      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setMcpConfigs([])
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
        mcpServers: [],
      })
      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setMcpConfigs([])
      onConfigChange?.(savedConfig, 'skip')
      onAfterSave(i18n.t('hiring.todo.external.skipMessage'), 'skip')
      setShowSkipConfirm(false)
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '外部系统配置跳过失败')
    } finally {
      setIsSaving(false)
    }
  }

  function handleSkipRequest() {
    if (hasMcpConfig) {
      setShowSkipConfirm(true)
      return
    }

    void handleSkip()
  }

  // 最终确认：只有这里会把外部配置提交为完成态
  async function handleContinue() {
    const firstMcp = mcpConfigs[0]
    if (!firstMcp) {
      return
    }

    setSaveError('')
    setIsSaving(true)
    try {
      const savedConfig = await api.hiringWorkflow.saveExternalConfig(hireId, {
        submissionMode: 'configured',
        cliTools: persistedExternalConfig?.cliTools ?? [],
        mcpServers: buildMcpServerPayloads(mcpConfigs),
      })
      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setMcpConfigs(createMcpConfigDraftsFromPersistedConfig(savedConfig))
      onConfigChange?.(savedConfig, 'save')
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '确认外部系统配置失败')
    } finally {
      setIsSaving(false)
    }
  }

  function handleOpenMcpModal() {
    setSaveError('')
    setMcpModalView('history')
    setEditingIndex(null)
    setActiveModal('mcp')
    // 首次打开时发起请求；后续打开使用缓存（staleTime 内不重复请求）
    if (!isMcpConfigFetched) {
      void refetchExternalConfig()
    }
  }

  // 编辑已有配置：预填对应项的数据
  function handleOpenMcpForm(index: number) {
    setMcpDraftConfig(cloneMcpConfig(mcpConfigs[index]))
    setEditingIndex(index)
    setSaveError('')
    setFieldErrors({})
    setMcpModalView('form')
  }

  // 新增配置：从空白表单开始
  function handleOpenMcpFormEmpty() {
    setMcpDraftConfig(createMcpConfigDraft())
    setEditingIndex(null)
    setSaveError('')
    setFieldErrors({})
    setMcpModalView('form')
  }

  function normalizeMcpDraftForSubmit(): McpConfigDraft | null {
    const url = mcpDraftConfig.url.trim()
    const name = mcpDraftConfig.name.trim()
    if (!name) {
      setFieldErrors(prev => ({ ...prev, mcpName: '请填写名称' }))
      return null
    }
    if (!url) {
      setFieldErrors(prev => ({ ...prev, mcpUrl: t('hiring.todo.external.urlRequired') || '请填写 URL' }))
      return null
    }
    if (!/^https?:\/\/.+/.test(url)) {
      setFieldErrors(prev => ({ ...prev, mcpUrl: t('hiring.todo.external.urlInvalid') }))
      return null
    }

    return {
      ...mcpDraftConfig,
      name,
      url,
      bearerTokenEnv: mcpDraftConfig.bearerTokenEnv.trim(),
    }
  }

  function buildLocalMcpTestResult(
    success: boolean,
    status: string,
    message: string,
  ): HiringMcpConnectivityTestResult {
    return {
      success,
      status,
      message,
      httpStatusCode: null,
      latencyMs: null,
      transport: '',
      testedAtUtc: new Date().toISOString(),
    }
  }

  async function handleTestMcpConfig(config: McpConfigDraft, key: string) {
    const name = config.name.trim()
    const url = config.url.trim()
    if (!name || !/^https?:\/\/.+/.test(url)) {
      setMcpTestResults(prev => ({
        ...prev,
        [key]: buildLocalMcpTestResult(false, 'invalid_config', '请先补全有效的 MCP 名称和 URL。'),
      }))
      return
    }

    setTestingMcpKey(key)
    setSaveError('')
    try {
      const result = await api.hiringWorkflow.testMcpConnectivity(hireId, {
        server: buildMcpServerPayload({
          ...config,
          name,
          url,
          bearerTokenEnv: config.bearerTokenEnv.trim(),
        }),
      })
      setMcpTestResults(prev => ({ ...prev, [key]: result }))
    } catch (error) {
      setMcpTestResults(prev => ({
        ...prev,
        [key]: buildLocalMcpTestResult(
          false,
          'request_failed',
          error instanceof Error ? error.message : 'MCP 连通性测试失败',
        ),
      }))
    } finally {
      setTestingMcpKey(null)
    }
  }

  async function handleTestDraftMcpConfig() {
    const draftItem = normalizeMcpDraftForSubmit()
    if (!draftItem) {
      return
    }

    await handleTestMcpConfig(draftItem, getMcpConfigKey(draftItem, 'draft'))
  }

  // MCP 弹窗内保存成功后回到 history 视图
  async function handleSaveMcpToApi() {
    const draftItem = normalizeMcpDraftForSubmit()
    if (!draftItem) {
      return
    }

    const nextConfigs = editingIndex !== null
      ? mcpConfigs.map((item, i) => i === editingIndex ? draftItem : item)
      : [...mcpConfigs, draftItem]

    setSaveError('')
    setIsSaving(true)
    try {
      const savedConfig = await api.hiringWorkflow.saveExternalConfig(hireId, {
        submissionMode: 'pending',
        cliTools: persistedExternalConfig?.cliTools ?? [],
        mcpServers: buildMcpServerPayloads(nextConfigs),
      })
      queryClient.setQueryData(externalConfigQueryKey, savedConfig)
      setMcpConfigs(createMcpConfigDraftsFromPersistedConfig(savedConfig))
      setEditingIndex(null)
      setMcpModalView('history')
      setShowDiscardConfirm(false)
      onConfigChange?.(savedConfig, 'draft')
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : '保存失败')
    } finally {
      setIsSaving(false)
    }
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

  const draftMcpTestKey = getMcpConfigKey(mcpDraftConfig, 'draft')
  const draftMcpTestResult = mcpTestResults[draftMcpTestKey]

  if (isExternalConfigLoading) {
    // 理论上不会触发（enabled:false），保留作为防御性 fallback
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
                      ? `${isExternalConfigFinalized ? '已确认' : '已保存'} ${mcpConfigs.length} 项 MCP 服务：${mcpConfigs.map(c => c.name.trim()).join('、')}`
                      : t('hiring.todo.external.mcpDescription')}
                  </p>
                  {isExternalConfigFinalized && (
                    <p className="hb-todo-external-card-copy">
                      如需调整，修改配置并保存后需要重新确认外部配置。
                    </p>
                  )}
                </div>
                <span className="hb-todo-external-type-pill">MCP</span>
              </div>
              <button type="button" className="hb-todo-row-btn is-primary" onClick={handleOpenMcpModal}>
                {hasMcpConfig ? (isExternalConfigFinalized ? '查看/修改' : t('hiring.todo.external.editConfig')) : '配置'}
              </button>
            </div>
          </section>

          {!isExternalConfigFinalized && (
            <div className="hb-todo-actions-row">
              <button
                type="button"
                className="hb-todo-row-btn is-ghost"
                disabled={isSaving}
                onClick={handleSkipRequest}
              >
                {hasMcpConfig ? '清空配置并跳过' : t('hiring.todo.external.skip')}
              </button>
              <button
                type="button"
                className="hb-todo-row-btn is-primary"
                disabled={!hasMcpConfig || isSaving}
                onClick={() => { void handleContinue() }}
              >
                确认外部配置
              </button>
            </div>
          )}

          {!isExternalConfigFinalized && showSkipConfirm && (
            <ConfirmationActionPanel
              ariaLabel="跳过外部配置确认"
              message="当前已有 MCP 配置。确认跳过会清空这些配置，并标记为本轮无需外部系统。"
              primaryLabel="清空并跳过"
              onPrimary={() => { void handleSkip() }}
              secondaryLabel="继续配置"
              onSecondary={() => setShowSkipConfirm(false)}
              busy={isSaving}
            />
          )}

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
                {mcpModalView === 'history' ? (
                  /* ── 列表视图：加载完成后显示已保存的配置 ── */
                  <div className="hb-todo-mcp-history">
                    <div className="hb-todo-mcp-history-header">
                      <span className="hb-todo-mcp-history-title">MCP 配置</span>
                      <button type="button" className="hb-todo-mcp-close-btn" aria-label="关闭" onClick={handleCloseModal}>×</button>
                    </div>

                    {isMcpConfigFetching ? (
                      /* 加载中 */
                      <div className="hb-todo-mcp-list-loading">
                        <Loader2 size={20} style={{ animation: 'spin 1s linear infinite', color: 'var(--ink-3, #94a3b8)' }} />
                        <span>加载中…</span>
                      </div>
                    ) : persistedExternalConfigError ? (
                      /* 加载失败 */
                      <div className="hb-todo-mcp-history-empty">
                        <p style={{ color: 'var(--hb-error, #ef4444)' }}>
                          {persistedExternalConfigError instanceof Error ? persistedExternalConfigError.message : '加载失败'}
                        </p>
                        <button
                          type="button"
                          className="hb-todo-row-btn is-ghost"
                          style={{ marginTop: 8 }}
                          onClick={() => { void refetchExternalConfig() }}
                        >
                          重试
                        </button>
                      </div>
                    ) : hasMcpConfig ? (
                      /* 已有配置：以列表条目展示，底部统一显示"添加配置" */
                      <>
                        <ul className="hb-todo-mcp-config-list">
                          {mcpConfigs.map((cfg, idx) => {
                            const configKey = getMcpConfigKey(cfg, idx)
                            const headerSummary = getMcpHeaderSummary(cfg)
                            const testResult = mcpTestResults[configKey]
                            const isTesting = testingMcpKey === configKey
                            return (
                              <li key={`${cfg.name}-${idx}`} className="hb-todo-mcp-config-item">
                                <div className="hb-todo-mcp-config-item-body">
                                  <div className="hb-todo-mcp-config-item-name">{cfg.name.trim()}</div>
                                  <div className="hb-todo-mcp-config-item-meta">
                                    <span className="hb-todo-external-type-pill" style={{ fontSize: 11 }}>{MCP_TRANSPORT_LABELS[cfg.transport]}</span>
                                    <span className="hb-todo-mcp-record-mono" style={{ fontSize: 12, color: 'var(--ink-2, #475569)' }}>{cfg.url.trim()}</span>
                                  </div>
                                  {cfg.bearerTokenEnv.trim() && (
                                    <div className="hb-todo-mcp-config-item-extra">Bearer 令牌：{cfg.bearerTokenEnv.trim()}</div>
                                  )}
                                  {headerSummary && (
                                    <div className="hb-todo-mcp-config-item-extra">固定 Header：{cfg.headerEntries.length} 项（{headerSummary}）</div>
                                  )}
                                  {testResult && (
                                    <div className={clsx('hb-todo-mcp-test-result', getMcpTestResultClass(testResult))}>
                                      {formatMcpTestResult(testResult)}
                                    </div>
                                  )}
                                </div>
                                <div className="hb-todo-mcp-config-actions">
                                  <button
                                    type="button"
                                    className="hb-todo-row-btn is-ghost"
                                    disabled={isTesting || isSaving}
                                    onClick={() => { void handleTestMcpConfig(cfg, configKey) }}
                                  >
                                    {isTesting
                                      ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><Loader2 size={13} style={{ animation: 'spin 1s linear infinite' }} />测试中…</span>
                                      : <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><Activity size={13} />测试</span>}
                                  </button>
                                  <button
                                    type="button"
                                    className="hb-todo-row-btn is-ghost"
                                    onClick={() => handleOpenMcpForm(idx)}
                                  >
                                    编辑
                                  </button>
                                </div>
                              </li>
                            )
                          })}
                        </ul>
                        <button
                          type="button"
                          className="hb-todo-mcp-add-config-btn"
                          onClick={handleOpenMcpFormEmpty}
                        >
                          + 添加配置
                        </button>
                      </>
                    ) : (
                      /* 暂无配置 */
                      <div className="hb-todo-mcp-history-empty">
                        <p>暂无 MCP 配置，点击下方按钮添加。</p>
                        <button
                          type="button"
                          className="hb-todo-mcp-add-config-btn"
                          style={{ marginTop: 12 }}
                          onClick={handleOpenMcpFormEmpty}
                        >
                          + 添加配置
                        </button>
                      </div>
                    )}
                  </div>
                ) : (
                  /* ── 配置表单视图 ── */
                  <div className="hb-todo-mcp-form">
                    <div className="hb-todo-mcp-history-header">
                      <button
                        type="button"
                        className="hb-todo-row-btn is-ghost"
                        style={{ fontSize: 12, padding: '2px 8px' }}
                        onClick={() => { setMcpModalView('history'); setShowDiscardConfirm(false) }}
                      >
                        ← 返回
                      </button>
                      <button type="button" className="hb-todo-mcp-close-btn" aria-label="关闭" onClick={handleCloseModal}>×</button>
                    </div>

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
                    {draftMcpTestResult && (
                      <div className={clsx('hb-todo-mcp-test-result', getMcpTestResultClass(draftMcpTestResult))}>
                        {formatMcpTestResult(draftMcpTestResult)}
                      </div>
                    )}

                    <div className="hb-todo-mcp-footer">
                      <button
                        type="button"
                        className="hb-todo-row-btn is-ghost"
                        disabled={isSaving || testingMcpKey === draftMcpTestKey}
                        onClick={() => { void handleTestDraftMcpConfig() }}
                      >
                        {testingMcpKey === draftMcpTestKey
                          ? <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><Loader2 size={13} style={{ animation: 'spin 1s linear infinite' }} />测试中…</span>
                          : <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}><Activity size={13} />测试连接</span>}
                      </button>
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
  return (
    <ConfirmationActionPanel
      ariaLabel="阶段推进确认"
      message={pendingConfirmation.prompt}
      primaryLabel={pendingConfirmation.confirmLabel}
      onPrimary={onConfirmAdvance}
      secondaryLabel={pendingConfirmation.continueLabel}
      onSecondary={onContinueCollection}
      busy={busy}
    />
  )
}
