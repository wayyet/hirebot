import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { Loader2, RefreshCw, Trash2, AlertCircle, Server, Layers, Clock, Palette } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { useTheme } from '@/app/theme/ThemeProvider'
import { api, type HiringSandboxItem } from '@/infra/api'

// ── 沙箱状态 → hb-pill 颜色映射 ──
function statePillClass(state: string): string {
  switch (state.toLowerCase()) {
    case 'running': return 'green'
    case 'creating':
    case 'initializing': return 'orange'
    case 'error': return 'red'
    default: return 'gray'  // expired / deleted / deleting
  }
}

// ── 沙箱角色 → hb-pill 颜色映射 ──
function rolePillClass(role: string): string {
  switch (role.toLowerCase()) {
    case 'hiring': return 'blue'
    case 'evaluation-target': return 'purple'
    case 'evaluation-evaluator': return 'pink'
    default: return 'gray'
  }
}

// ── UTC ISO 日期 → 浏览器本地时区时间字符串 ──
function formatLocalDate(isoUtc: string | null): string {
  if (!isoUtc) return '—'
  return new Date(isoUtc).toLocaleString(undefined, {
    hour12: false,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  })
}

// ── 过期状态计算（基于客户端当前时间）──
type ExpiryStatus =
  | { type: 'never' }
  | { type: 'expired' }
  | { type: 'critical'; diffMs: number }  // 剩余 < 10 分钟
  | { type: 'warning'; diffMs: number }   // 剩余 10 分钟 ~ 1 小时
  | { type: 'ok'; diffMs: number }

function computeExpiryStatus(expiresAtUtc: string | null): ExpiryStatus {
  if (!expiresAtUtc) return { type: 'never' }
  const diffMs = new Date(expiresAtUtc).getTime() - Date.now()
  if (diffMs <= 0) return { type: 'expired' }
  if (diffMs < 10 * 60_000) return { type: 'critical', diffMs }
  if (diffMs < 60 * 60_000) return { type: 'warning', diffMs }
  return { type: 'ok', diffMs }
}

// ── 过期时间单元格 ──
function ExpiryCell({ expiresAtUtc }: { expiresAtUtc: string | null }) {
  const { t } = useTranslation()
  const status = computeExpiryStatus(expiresAtUtc)

  if (status.type === 'never') {
    return (
      <span className="hb-expiry-never">
        <Clock size={11} />
        {t('settings.sandboxes.expiryNever')}
      </span>
    )
  }

  const localTime = formatLocalDate(expiresAtUtc)

  if (status.type === 'expired') {
    return (
      <div className="hb-expiry-cell">
        <span className="hb-expiry-time">{localTime}</span>
        <span className="hb-expiry-badge expired">{t('settings.sandboxes.expiryExpired')}</span>
      </div>
    )
  }

  const { diffMs } = status
  let relText: string
  if (diffMs < 60 * 60_000) {
    relText = t('settings.sandboxes.expiryInMinutes', { count: Math.ceil(diffMs / 60_000) })
  } else if (diffMs < 24 * 3600_000) {
    relText = t('settings.sandboxes.expiryInHours', { count: Math.round(diffMs / 3600_000) })
  } else {
    relText = t('settings.sandboxes.expiryInDays', { count: Math.floor(diffMs / 86400_000) })
  }

  const badgeClass = status.type === 'critical' ? 'critical' : status.type === 'warning' ? 'warning' : 'ok'

  return (
    <div className="hb-expiry-cell">
      <span className="hb-expiry-time">{localTime}</span>
      <span className={`hb-expiry-badge ${badgeClass}`}>{relText}</span>
    </div>
  )
}

// ── 单行沙箱记录 ──
interface SandboxRowProps {
  item: HiringSandboxItem
  onDelete: (sandboxId: string) => void
  onSelectionChange: (sandboxId: string, selected: boolean) => void
  deletingIds: ReadonlySet<string>
  selected: boolean
}

function SandboxRow({ item, onDelete, onSelectionChange, deletingIds, selected }: SandboxRowProps) {
  const { t } = useTranslation()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const isDeleting = deletingIds.has(item.sandboxId)
  const expiryStatus = computeExpiryStatus(item.expiresAtUtc)
  const isClientExpired = expiryStatus.type === 'expired' &&
    !['deleted', 'deleting', 'error'].includes(item.state.toLowerCase())
  const effectiveState = isClientExpired ? 'expired' : item.state

  function handleConfirm() {
    setConfirmOpen(false)
    onDelete(item.sandboxId)
  }

  return (
    <>
      <tr className={isDeleting ? 'is-deleting' : undefined}>
        <td className="hb-selection-col">
          <input
            type="checkbox"
            className="hb-row-checkbox"
            checked={selected}
            disabled={isDeleting}
            aria-label={t('settings.sandboxes.selectRow', { sandboxId: item.sandboxId })}
            onChange={(event) => onSelectionChange(item.sandboxId, event.target.checked)}
          />
        </td>
        <td>
          <div className="hb-dt-id">
            <Server size={13} />
            <span className="hb-dt-mono" title={item.sandboxId}>{item.sandboxId}</span>
          </div>
        </td>
        <td>
          <div className="hb-dt-scope">
            <Layers size={13} />
            <span>{item.scopeType}</span>
            {item.scopeKey && (
              <span className="hb-dt-scope-key" title={item.scopeKey}>{item.scopeKey}</span>
            )}
          </div>
        </td>
        <td>          <span className={`hb-pill ${rolePillClass(item.sandboxRole)}`}>
            {item.sandboxRole || '—'}
          </span>
        </td>
        <td>          <span className={`hb-pill ${statePillClass(effectiveState)}`}>
            {t(`settings.sandboxes.state.${effectiveState.toLowerCase()}`, { defaultValue: effectiveState })}
          </span>
          {item.lastError && (
            <span title={item.lastError}>
              <AlertCircle
                size={13}
                style={{ marginLeft: 6, color: 'var(--hb-danger)', verticalAlign: 'middle' }}
              />
            </span>
          )}
        </td>
        <td className="muted">{formatLocalDate(item.createdAtUtc)}</td>
        <td><ExpiryCell expiresAtUtc={item.expiresAtUtc} /></td>
        <td>
          <button
            type="button"
            className="hb-mini-action danger"
            onClick={() => setConfirmOpen(true)}
            disabled={isDeleting}
            title={t('settings.sandboxes.delete')}
          >
            {isDeleting
              ? <Loader2 size={12} className="animate-spin" />
              : <Trash2 size={12} />
            }
          </button>
        </td>
      </tr>

      {/* 内联确认行：点击删除后展开 */}
      {confirmOpen && (
        <tr className="hb-confirm-row">
          <td colSpan={8}>
            <div className="hb-confirm-bar">
              <AlertCircle size={14} />
              <span>{t('settings.sandboxes.deleteConfirm')}</span>
              <button type="button" className="hb-mini-action danger" onClick={handleConfirm}>
                {t('settings.sandboxes.delete')}
              </button>
              <button
                type="button"
                className="hb-mini-action"
                onClick={() => setConfirmOpen(false)}
              >
                {t('common.cancel')}
              </button>
            </div>
          </td>
        </tr>
      )}
    </>
  )
}

// ── 主页面 ──
export default function SettingsPage() {
  const { t } = useTranslation()
  const { brand, setBrand, warmThemeManagedByRuntime } = useTheme()
  const [sandboxes, setSandboxes] = useState<HiringSandboxItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selectedSandboxIds, setSelectedSandboxIds] = useState<Set<string>>(() => new Set())
  const [deletingIds, setDeletingIds] = useState<Set<string>>(() => new Set())
  const [batchConfirmMode, setBatchConfirmMode] = useState<'selected' | 'all' | null>(null)
  const [toast, setToast] = useState<{ type: 'success' | 'error'; message: string } | null>(null)
  const selectAllRef = useRef<HTMLInputElement>(null)

  const sandboxIds = useMemo(() => sandboxes.map(item => item.sandboxId), [sandboxes])
  const selectedCount = selectedSandboxIds.size
  const isBusyDeleting = deletingIds.size > 0
  const allSelected = sandboxes.length > 0 && selectedCount === sandboxes.length
  const batchTargetIds = batchConfirmMode === 'all' ? sandboxIds : [...selectedSandboxIds]
  const brandOptions = useMemo(() => [
    {
      id: 'amber' as const,
      label: t('theme.brand.amber'),
      description: t('settings.appearance.options.amberDescription'),
    },
    {
      id: 'blue' as const,
      label: t('theme.brand.blue'),
      description: t('settings.appearance.options.blueDescription'),
    },
  ], [t])

  const load = useCallback(async () => {
    setLoading(true)
    setError('')
    try {
      const items = await api.settings.listSandboxes()
      setSandboxes(items)
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('settings.sandboxes.loadError'))
    } finally {
      setLoading(false)
    }
  }, [t])

  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (!selectAllRef.current) return
    selectAllRef.current.indeterminate = selectedCount > 0 && selectedCount < sandboxes.length
  }, [sandboxes.length, selectedCount])

  // 列表刷新或删除后，清理已经不存在的选中项，避免后续批量操作误带旧 ID。
  useEffect(() => {
    setSelectedSandboxIds(prev => {
      if (prev.size === 0) return prev

      const existingIds = new Set(sandboxIds)
      const next = new Set([...prev].filter(sandboxId => existingIds.has(sandboxId)))
      return next.size === prev.size ? prev : next
    })
  }, [sandboxIds])

  // Toast 3 秒后自动关闭
  useEffect(() => {
    if (!toast) return
    const timer = setTimeout(() => setToast(null), 3000)
    return () => clearTimeout(timer)
  }, [toast])

  async function handleDelete(sandboxId: string) {
    setDeletingIds(new Set([sandboxId]))
    try {
      await api.settings.deleteSandbox(sandboxId)
      setSandboxes(prev => prev.filter(s => s.sandboxId !== sandboxId))
      setSelectedSandboxIds(prev => {
        if (!prev.has(sandboxId)) return prev
        const next = new Set(prev)
        next.delete(sandboxId)
        return next
      })
      setToast({ type: 'success', message: t('settings.sandboxes.deleteSuccess') })
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('settings.sandboxes.deleteFailed')
      setToast({ type: 'error', message: msg })
    } finally {
      setDeletingIds(new Set())
    }
  }

  function handleSelectRow(sandboxId: string, selected: boolean) {
    setSelectedSandboxIds(prev => {
      const next = new Set(prev)
      if (selected) {
        next.add(sandboxId)
      } else {
        next.delete(sandboxId)
      }
      return next
    })
  }

  function handleSelectAll(selected: boolean) {
    setSelectedSandboxIds(selected ? new Set(sandboxIds) : new Set())
  }

  async function handleBatchDelete() {
    const targetIds = batchTargetIds
    if (targetIds.length === 0) {
      setBatchConfirmMode(null)
      return
    }

    setDeletingIds(new Set(targetIds))
    try {
      const results = await Promise.allSettled(targetIds.map(sandboxId => api.settings.deleteSandbox(sandboxId)))
      const deletedIds = targetIds.filter((_, index) => results[index].status === 'fulfilled')
      const deletedIdSet = new Set(deletedIds)
      const failedCount = targetIds.length - deletedIds.length

      if (deletedIds.length > 0) {
        setSandboxes(prev => prev.filter(item => !deletedIdSet.has(item.sandboxId)))
        setSelectedSandboxIds(prev => {
          const next = new Set(prev)
          deletedIds.forEach(sandboxId => next.delete(sandboxId))
          return next
        })
      }

      if (failedCount > 0) {
        setToast({
          type: 'error',
          message: t('settings.sandboxes.batchDeletePartialFailed', {
            deleted: deletedIds.length,
            failed: failedCount,
          }),
        })
      } else {
        setToast({
          type: 'success',
          message: t('settings.sandboxes.batchDeleteSuccess', { count: deletedIds.length }),
        })
      }
    } finally {
      setDeletingIds(new Set())
      setBatchConfirmMode(null)
    }
  }

  return (
    <div className="hb-page">
      {!warmThemeManagedByRuntime ? (
        <div className="hb-section hb-theme-section">
          <div className="hb-section-head">
            <div>
              <span className="hb-kicker hb-kicker-accent">{t('settings.appearance.title')}</span>
              <h2 className="hb-section-title">{t('settings.appearance.description')}</h2>
              <p className="hb-section-copy">{t('settings.appearance.copy')}</p>
            </div>
          </div>

          <div className="hb-theme-grid">
            <section className="hb-theme-panel">
              <div className="hb-theme-panel-head">
                <div className="hb-theme-panel-icon">
                  <Palette size={16} />
                </div>
                <div>
                  <h3 className="hb-theme-panel-title">{t('settings.appearance.brandTitle')}</h3>
                  <p className="hb-theme-panel-copy">{t('settings.appearance.brandDescription')}</p>
                </div>
              </div>

              <div className="hb-theme-option-grid">
                {brandOptions.map((option) => (
                  <button
                    key={option.id}
                    type="button"
                    className={`hb-theme-option ${brand === option.id ? 'is-active' : ''}`}
                    onClick={() => setBrand(option.id)}
                  >
                    <div className="hb-theme-option-body">
                      <span className="hb-theme-option-title">{option.label}</span>
                      <span className="hb-theme-option-copy">{option.description}</span>
                    </div>
                  </button>
                ))}
              </div>
            </section>
          </div>

          <div className="hb-theme-current">
            {t('settings.appearance.current', {
              brand: t(`theme.brand.${brand}`),
            })}
          </div>
        </div>
      ) : null}
      {/* 页头 */}
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">{t('settings.title')}</span>
          <h1 className="hb-page-title">{t('settings.sandboxes.title')}</h1>
          <p className="hb-page-copy">{t('settings.sandboxes.description')}</p>
        </div>
      </div>

      {/* 沙箱管理区块 */}
      <div className="hb-section">
        <div className="hb-section-head">
          <div>
            <h2 className="hb-section-title">{t('settings.sandboxes.title')}</h2>
            <p className="hb-section-copy">{t('settings.sandboxes.description')}</p>
          </div>
          <div className="hb-section-actions">
            <button
              type="button"
              className="hb-btn-ghost"
              style={{ gap: 6, padding: '8px 14px', fontSize: 13 }}
              onClick={() => { void load() }}
              disabled={loading || isBusyDeleting}
            >
              <RefreshCw size={13} className={loading ? 'animate-spin' : ''} />
              {t('settings.sandboxes.refresh')}
            </button>
          </div>
        </div>

        {/* 错误提示 */}
        {error && (
          <div className="hb-alert hb-alert-error" style={{ marginBottom: 16 }}>
            <AlertCircle size={15} style={{ flexShrink: 0 }} />
            <span>{error}</span>
          </div>
        )}

        {/* 加载状态 */}
        {loading && (
          <div className="hb-section-loading">
            <Loader2 size={18} className="animate-spin" />
            <span>{t('common.loading')}</span>
          </div>
        )}

        {/* 空状态 */}
        {!loading && !error && sandboxes.length === 0 && (
          <div className="hb-empty">
            <Server size={32} style={{ color: 'var(--hb-caption)' }} />
            <div className="hb-empty-title">{t('settings.sandboxes.empty')}</div>
            <div className="hb-empty-copy">{t('settings.sandboxes.description')}</div>
          </div>
        )}

        {/* 沙箱列表表格 */}
        {!loading && sandboxes.length > 0 && (
          <>
            <div className="hb-table-toolbar">
              <div className="hb-table-selection-summary">
                {selectedCount > 0
                  ? t('settings.sandboxes.selectedCount', { count: selectedCount })
                  : t('settings.sandboxes.noSelection')}
              </div>
              <div className="hb-table-toolbar-actions">
                <button
                  type="button"
                  className="hb-mini-action danger"
                  onClick={() => setBatchConfirmMode('selected')}
                  disabled={selectedCount === 0 || isBusyDeleting}
                >
                  {isBusyDeleting && batchConfirmMode === 'selected'
                    ? <Loader2 size={12} className="animate-spin" />
                    : <Trash2 size={12} />}
                  {t('settings.sandboxes.deleteSelected')}
                </button>
                <button
                  type="button"
                  className="hb-mini-action danger"
                  onClick={() => setBatchConfirmMode('all')}
                  disabled={sandboxes.length === 0 || isBusyDeleting}
                >
                  {isBusyDeleting && batchConfirmMode === 'all'
                    ? <Loader2 size={12} className="animate-spin" />
                    : <Trash2 size={12} />}
                  {t('settings.sandboxes.deleteAll')}
                </button>
              </div>
            </div>

            {batchConfirmMode && (
              <div className="hb-confirm-bar hb-batch-confirm">
                <AlertCircle size={14} />
                <span>
                  {batchConfirmMode === 'all'
                    ? t('settings.sandboxes.allDeleteConfirm', { count: batchTargetIds.length })
                    : t('settings.sandboxes.selectedDeleteConfirm', { count: batchTargetIds.length })}
                </span>
                <button
                  type="button"
                  className="hb-mini-action danger"
                  onClick={() => { void handleBatchDelete() }}
                  disabled={isBusyDeleting}
                >
                  {isBusyDeleting ? <Loader2 size={12} className="animate-spin" /> : null}
                  {t('common.confirm')}
                </button>
                <button
                  type="button"
                  className="hb-mini-action"
                  onClick={() => setBatchConfirmMode(null)}
                  disabled={isBusyDeleting}
                >
                  {t('common.cancel')}
                </button>
              </div>
            )}

            <div className="hb-data-table-wrap">
              <table className="hb-data-table">
                <thead>
                  <tr>
                    <th className="hb-selection-col">
                      <input
                        ref={selectAllRef}
                        type="checkbox"
                        className="hb-row-checkbox"
                        checked={allSelected}
                        disabled={isBusyDeleting}
                        aria-label={t('settings.sandboxes.selectAll')}
                        onChange={(event) => handleSelectAll(event.target.checked)}
                      />
                    </th>
                    <th>{t('settings.sandboxes.col.sandboxId')}</th>
                    <th>{t('settings.sandboxes.col.scope')}</th>
                    <th>{t('settings.sandboxes.col.role')}</th>
                    <th>{t('settings.sandboxes.col.state')}</th>
                    <th>{t('settings.sandboxes.col.createdAt')}</th>
                    <th>{t('settings.sandboxes.col.expiresAt')}</th>
                    <th>{t('settings.sandboxes.col.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {sandboxes.map(item => (
                    <SandboxRow
                      key={item.instanceId}
                      item={item}
                      onDelete={handleDelete}
                      onSelectionChange={handleSelectRow}
                      deletingIds={deletingIds}
                      selected={selectedSandboxIds.has(item.sandboxId)}
                    />
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </div>

      {/* Toast 通知 */}
      {toast && (
        <div className="hb-toast-wrap">
          <div className={`hb-toast ${toast.type}`}>
            {toast.message}
          </div>
        </div>
      )}
    </div>
  )
}

