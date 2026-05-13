import { useEffect, useMemo, useState } from 'react'
import { AlertTriangle, Loader2 } from 'lucide-react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { api, type EmployeeDetail } from '@/infra/api'
import { ownershipLabel, toEmployeeDetailSummary, withEmployeeView } from './employeeView'
import { Breadcrumb } from '@/shared/components/Breadcrumb'
import { instanceBasePath } from '@/shared/utils/instancePath'

export default function ReviewPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)

  async function loadEmployee() {
    if (!id) {
      setError('实例 ID 缺失')
      setLoading(false)
      return
    }

    setLoading(true)
    setError('')
    try {
      const detail = await api.employeeRuntime.getEmployee(id)
      setEmployee(detail)
    } catch (requestError: unknown) {
      setEmployee(null)
      setError(requestError instanceof Error ? requestError.message : '加载 Review 数据失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadEmployee()
  }, [id])

  const employeeView = useMemo(() => (
    employee ? withEmployeeView(toEmployeeDetailSummary(employee)) : null
  ), [employee])

  const location = useLocation();

  async function rollbackToHired() {
    if (!id) return
    setSubmitting(true)
    setError('')
    try {
      const updated = await api.employeeRuntime.updateLifecycle(id, {
        status: 'hired',
        stageSummary: 'Review 回退到已雇佣，等待重新发起 AI 评估',
        primarySignal: '待操作：重新进入 AI 评估',
        signalLevel: 'warn',
      })
      setEmployee(updated)
      navigate(`${instanceBasePath(location.pathname, id!)}/evaluation`)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '回退到已雇佣失败')
    } finally {
      setSubmitting(false)
    }
  }

  async function rollbackToAi() {
    if (!id) return
    setSubmitting(true)
    setError('')
    try {
      const updated = await api.employeeRuntime.submitAiEvaluationDecision(id, { decision: 'START' })
      setEmployee(updated)
      navigate(`${instanceBasePath(location.pathname, id!)}/evaluation`)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '回退到 AI 评估失败')
    } finally {
      setSubmitting(false)
    }
  }

  async function continueHiring() {
    if (!employee) return
    const templateId = employee.basedOnTemplateId || employee.sourceTemplateId
    if (!templateId) {
      setError('缺少模板 ID，无法继续雇佣')
      return
    }

    setSubmitting(true)
    setError('')
    try {
      const result = await api.employeeTemplate.fixtureHire(templateId)
      navigate(`${instanceBasePath(location.pathname, result.employeeId)}/evaluation`)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '继续雇佣失败')
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载 Review 页面...
        </div>
      </div>
    )
  }

  if (!employee || !employeeView) {
    return (
      <div className="hb-page space-y-4">
        <Breadcrumb items={[{ label: '部门数字员工', to: '/department-employees' }, { label: 'Review' }]} />
        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          {error || '未找到实例数据'}
        </div>
      </div>
    )
  }

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '实例详情', to: instanceBasePath(location.pathname, employee.employeeId) }, { label: '回退与重试' }]} />

      <div className="hb-hero">
        <div className="hb-hero-grid">
          <div className="hb-toolbar">
            <div>
              <div className="hb-hero-eyebrow">回退与重试</div>
              <h1 className="hb-hero-title">Review · {employee.nickname}</h1>
              <p className="hb-hero-copy">
                评估失败后，在这里选择标准化回退入口，或继续雇佣新实例。
              </p>
            </div>
          </div>
          <div className="hb-hero-metrics">
            <div className="hb-metric-card">
              <div className="hb-metric-label">当前状态</div>
              <div className="hb-metric-value">{employee.lifecycleStatus}</div>
            </div>
            <div className="hb-metric-card">
              <div className="hb-metric-label">实例类型</div>
              <div className="hb-metric-value">{ownershipLabel(employeeView.ownership)}</div>
            </div>
            <div className="hb-metric-card">
              <div className="hb-metric-label">主信号</div>
              <div className="hb-metric-value text-[15px] leading-6">{employee.primarySignal || '暂无'}</div>
            </div>
          </div>
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">失败摘要</h2>
        <div className="mt-4 grid gap-3 md:grid-cols-3">
          <div className="rounded-xl border border-[#f3f4f6] bg-[#fafafa] p-3">
            <div className="text-xs text-[#737373]">当前状态</div>
            <div className="mt-1 text-lg font-semibold text-[#0a0a0a]">{employee.lifecycleStatus}</div>
          </div>
          <div className="rounded-xl border border-[#f3f4f6] bg-[#fafafa] p-3">
            <div className="text-xs text-[#737373]">实例类型</div>
            <div className="mt-1 text-lg font-semibold text-[#0a0a0a]">{ownershipLabel(employeeView.ownership)}</div>
          </div>
          <div className="rounded-xl border border-[#f3f4f6] bg-[#fafafa] p-3">
            <div className="text-xs text-[#737373]">主信号</div>
            <div className="mt-1 text-sm font-medium text-[#0a0a0a]">{employee.primarySignal || '暂无'}</div>
          </div>
        </div>
        <div className="mt-4 flex items-start gap-2 rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          <AlertTriangle size={16} className="mt-0.5 shrink-0" />
          <span>{employee.stageSummary || '当前实例需要回退后继续评估。'}</span>
        </div>
      </div>

      {error && (
        <div className="rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          {error}
        </div>
      )}

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">标准化回退入口</h2>
        <div className="mt-4 grid gap-3 md:grid-cols-3">
          <button
            type="button"
            disabled={submitting}
            onClick={() => void rollbackToHired()}
            className="rounded-2xl border border-[#ececec] bg-white px-4 py-4 text-left hover:border-[#d4d4d8] disabled:opacity-60"
          >
            <div className="text-sm font-semibold text-[#0a0a0a]">回退到已雇佣</div>
            <p className="mt-2 text-xs leading-relaxed text-[#737373]">
              回到 hired，重新走 AI 评估入口。
            </p>
          </button>

          <button
            type="button"
            disabled={submitting}
            onClick={() => void rollbackToAi()}
            className="rounded-2xl border border-[#ececec] bg-white px-4 py-4 text-left hover:border-[#d4d4d8] disabled:opacity-60"
          >
            <div className="text-sm font-semibold text-[#0a0a0a]">直接回退到 AI 评估</div>
            <p className="mt-2 text-xs leading-relaxed text-[#737373]">
              跳过中间步骤，直接回到 interning_ai。
            </p>
          </button>

          <button
            type="button"
            disabled={submitting}
            onClick={() => void continueHiring()}
            className="rounded-2xl border border-[#0a0a0a] bg-[#0a0a0a] px-4 py-4 text-left text-white hover:bg-[#222] disabled:opacity-60"
          >
            <div className="text-sm font-semibold">继续雇佣（新实例）</div>
            <p className="mt-2 text-xs leading-relaxed text-white/80">
              基于模板创建新的可评估实例并进入评估页。
            </p>
          </button>
        </div>
      </div>
    </div>
  )
}
