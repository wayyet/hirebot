import { useCallback, useEffect, useState } from 'react'
import { Loader2, RefreshCw, Trash2, AlertCircle, Server, Layers } from 'lucide-react'
import { useTranslation } from 'react-i18next'
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

// ── 单行沙箱记录 ──
interface SandboxRowProps {
  item: HiringSandboxItem
  onDelete: (sandboxId: string) => void
  deletingId: string | null
}

function SandboxRow({ item, onDelete, deletingId }: SandboxRowProps) {
  const { t } = useTranslation()
  const [confirmOpen, setConfirmOpen] = useState(false)
  const isDeleting = deletingId === item.sandboxId

  function formatDate(iso: string | null) {
    if (!iso) return '—'
    return new Date(iso).toLocaleString()
  }

  function handleConfirm() {
    setConfirmOpen(false)
    onDelete(item.sandboxId)
  }

  return (
    <>
      <tr className={isDeleting ? 'is-deleting' : undefined}>
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
        <td>
          <span className={`hb-pill ${statePillClass(item.state)}`}>{item.state}</span>
          {item.lastError && (
            <AlertCircle
              size={13}
              style={{ marginLeft: 6, color: 'var(--hb-danger)', verticalAlign: 'middle' }}
              title={item.lastError}
            />
          )}
        </td>
        <td className="muted">{formatDate(item.createdAtUtc)}</td>
        <td className="muted">{formatDate(item.expiresAtUtc)}</td>
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
          <td colSpan={6}>
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
  const [sandboxes, setSandboxes] = useState<HiringSandboxItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [deletingId, setDeletingId] = useState<string | null>(null)
  const [toast, setToast] = useState<{ type: 'success' | 'error'; message: string } | null>(null)

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

  // Toast 3 秒后自动关闭
  useEffect(() => {
    if (!toast) return
    const timer = setTimeout(() => setToast(null), 3000)
    return () => clearTimeout(timer)
  }, [toast])

  async function handleDelete(sandboxId: string) {
    setDeletingId(sandboxId)
    try {
      await api.settings.deleteSandbox(sandboxId)
      setSandboxes(prev => prev.filter(s => s.sandboxId !== sandboxId))
      setToast({ type: 'success', message: t('settings.sandboxes.deleteSuccess') })
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('settings.sandboxes.deleteFailed')
      setToast({ type: 'error', message: msg })
    } finally {
      setDeletingId(null)
    }
  }

  return (
    <div className="hb-page">
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
          <button
            type="button"
            className="hb-btn-ghost"
            style={{ gap: 6, padding: '8px 14px', fontSize: 13 }}
            onClick={() => { void load() }}
            disabled={loading}
          >
            <RefreshCw size={13} className={loading ? 'animate-spin' : ''} />
            {t('settings.sandboxes.refresh')}
          </button>
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
          <div className="hb-data-table-wrap">
            <table className="hb-data-table">
              <thead>
                <tr>
                  <th>{t('settings.sandboxes.col.sandboxId')}</th>
                  <th>{t('settings.sandboxes.col.scope')}</th>
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
                    deletingId={deletingId}
                  />
                ))}
              </tbody>
            </table>
          </div>
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

