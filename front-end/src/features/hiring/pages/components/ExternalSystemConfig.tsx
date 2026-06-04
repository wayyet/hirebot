/**
 * ExternalSystemConfig.tsx - 外部系统配置组件(CLI 工具 + MCP 服务)
 * 
 * 用于配置员工模板的外部系统集成：
 * - CLI 工具配置（命令、参数、执行模式）
 * - MCP 服务配置（传输方式、环境变量、请求头等）
 */

import { useCallback, useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Eye, EyeOff, Trash2 } from 'lucide-react'
import i18n from '@/i18n'

import { api } from '@/infra/api'
import type { HiringExternalSystemConfig } from '@/infra/api'
import type { ExternalConfigChangeSource } from '../externalPackagingState'
import type {
  PendingStageAdvanceConfirmation,
  StageAdvanceIntent,
} from '../stageAdvanceConfirmation'

// ── 类型定义 ──────────────────────────────────────────────────────────────────

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
  command: string
  args: string[]
  envEntries: McpKeyValueEntry[]
  envPassThrough: string[]
  cwd: string
  url: string
  bearerTokenEnv: string
  headerEntries: McpKeyValueEntry[]
  headersFromEnvEntries: McpKeyValueEntry[]
}

type ExternalConfigModalType = 'cli' | 'mcp'

// ── 常量 ──────────────────────────────────────────────────────────────────────

const MCP_TRANSPORT_LABELS: Record<McpTransport, string> = {
  stdio: 'STDIO（本地进程）',
  http: 'HTTP（远程服务）',
}

export const EXTERNAL_CONFIG_START_MESSAGE = '我选择继续配置外部系统。请先帮我梳理应该配置哪些 CLI 工具和 MCP 服务，再逐项确认。'

// ── 辅助函数 ──────────────────────────────────────────────────────────────────

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

