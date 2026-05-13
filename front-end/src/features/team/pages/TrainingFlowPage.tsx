import { useEffect, useMemo, useState } from 'react'
import { AlertCircle, BadgeCheck, Loader2, Sparkles, XCircle } from 'lucide-react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { api, type EmployeeDetail, type TrainingCheckpoint, type TrainingState } from '@/infra/api'
import { Breadcrumb } from '@/shared/components/Breadcrumb'
import { instanceBasePath } from '@/shared/utils/instancePath'

function checkpointClass(checkpoint: TrainingCheckpoint) {
  const normalized = checkpoint.status.toLowerCase()
  if (normalized.includes('pass') || normalized.includes('done') || normalized.includes('ok') || normalized.includes('完成')) {
    return 'green'
  }
  if (normalized.includes('fail') || normalized.includes('error') || normalized.includes('异常')) {
    return 'orange'
  }
  return 'gray'
}

function checkpointLabel(checkpoint: TrainingCheckpoint) {
  const normalized = checkpoint.status.toLowerCase()
  if (normalized.includes('pass') || normalized.includes('done') || normalized.includes('ok')) {
    return '已通过'
  }
  if (normalized.includes('fail') || normalized.includes('error')) {
    return '未通过'
  }
  return checkpoint.status || '处理中'
}

export default function TrainingFlowPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [state, setState] = useState<TrainingState | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  const location = useLocation();

  async function loadData() {
    if (!id) return
    setLoading(true)
    setError('')
    try {
      const [employeeData, trainingState] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getTrainingState(id),
      ])
      setEmployee(employeeData)
      setState(trainingState)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '加载培训状态失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData()
  }, [id])

  async function submitDecision(decision: 'APPROVE' | 'REJECT') {
    if (!id) return
    setSubmitting(true)
    setError('')
    try {
      const updated = await api.employeeRuntime.submitTrainingDecision(id, { decision })
      setEmployee(updated)
      await loadData()
      if (decision === 'APPROVE') {
        navigate('/department-employees')
      }
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '提交决策失败')
    } finally {
      setSubmitting(false)
    }
  }

  const checkpointSummary = useMemo(() => {
    if (!state) {
      return { passed: 0, failed: 0, pending: 0 }
    }

    const passed = state.checkpoints.filter((item) => checkpointClass(item) === 'green').length
    const failed = state.checkpoints.filter((item) => checkpointClass(item) === 'orange').length
    const pending = state.checkpoints.length - passed - failed

    return { passed, failed, pending }
  }, [state])

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '员工详情', to: id ? instanceBasePath(location.pathname, id) : '/department-employees' }, { label: '训练流程' }]} />

      {error && (
        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          <span className="inline-flex items-center gap-2">
            <AlertCircle size={14} />
            {error}
          </span>
        </div>
      )}

      {loading ? (
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载培训流程...
        </div>
      ) : !employee || !state ? (
        <div className="hb-card p-8 text-sm text-[#737373]">培训数据不存在</div>
      ) : (
        <>
          <section className="hb-hero">
            <div className="hb-hero-grid">
              <div className="hb-toolbar">
                <div>
                  <div className="hb-hero-eyebrow">培训流程</div>
                  <h1 className="hb-hero-title">培训流程 · {employee.nickname}</h1>
                  <p className="hb-hero-copy">
                    当前阶段：{state.phase} · 演化轮次：{state.evolutionRound}
                  </p>
                </div>
                <span className={`hb-pill ${state.aiPassed ? 'green' : 'orange'}`}>
                  {state.aiPassed ? 'AI 判定通过' : 'AI 判定待改进'}
                </span>
              </div>
              <div className="hb-hero-metrics">
                <div className="hb-metric-card">
                  <div className="hb-metric-label">检查点总数</div>
                  <div className="hb-metric-value">{state.checkpoints.length}</div>
                </div>
                <div className="hb-metric-card">
                  <div className="hb-metric-label">已通过</div>
                  <div className="hb-metric-value text-[#15803d]">{checkpointSummary.passed}</div>
                </div>
                <div className="hb-metric-card">
                  <div className="hb-metric-label">未通过</div>
                  <div className="hb-metric-value text-[#b3263c]">{checkpointSummary.failed}</div>
                </div>
                <div className="hb-metric-card">
                  <div className="hb-metric-label">考试得分</div>
                  <div className="hb-metric-value">{state.examScore}</div>
                </div>
              </div>
            </div>
          </section>

          <section className="grid gap-3 md:grid-cols-4">
            <div className="hb-card p-4">
              <div className="flex items-center gap-2 text-xs text-[#737373]"><Sparkles size={14} /> 检查点总数</div>
              <div className="mt-2 text-2xl font-semibold text-[#0a0a0a]">{state.checkpoints.length}</div>
            </div>
            <div className="hb-card p-4">
              <div className="flex items-center gap-2 text-xs text-[#737373]"><BadgeCheck size={14} /> 已通过</div>
              <div className="mt-2 text-2xl font-semibold text-[#15803d]">{checkpointSummary.passed}</div>
            </div>
            <div className="hb-card p-4">
              <div className="flex items-center gap-2 text-xs text-[#737373]"><XCircle size={14} /> 未通过</div>
              <div className="mt-2 text-2xl font-semibold text-[#b3263c]">{checkpointSummary.failed}</div>
            </div>
            <div className="hb-card p-4">
              <div className="text-xs text-[#737373]">考试得分</div>
              <div className="mt-2 text-2xl font-semibold text-[#0a0a0a]">{state.examScore}</div>
            </div>
          </section>

          <section className="hb-card p-6">
            <h2 className="text-base font-semibold text-[#0a0a0a]">训练检查点</h2>
            <div className="mt-4 space-y-3">
              {state.checkpoints.map((item) => (
                <div key={item.key} className="rounded-2xl border border-[#ececec] bg-white px-4 py-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="text-sm font-semibold text-[#0a0a0a]">{item.label}</div>
                    <span className={`hb-pill ${checkpointClass(item)}`}>
                      {checkpointLabel(item)}
                    </span>
                  </div>
                  {item.detail && (
                    <div className="mt-2 rounded-xl border border-[#f3f4f6] bg-[#fafafa] px-3 py-2 text-xs leading-relaxed text-[#404040]">
                      {item.detail}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </section>

          <section className="hb-card p-6">
            <h2 className="mb-3 text-base font-semibold text-[#0a0a0a]">评估决策</h2>
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                disabled={submitting}
                onClick={() => void submitDecision('APPROVE')}
                className="hb-btn-primary"
              >
                通过并进入实习
              </button>
              <button
                type="button"
                disabled={submitting}
                onClick={() => void submitDecision('REJECT')}
                className="rounded-full border border-[#fde2e2] bg-white px-4 py-2 text-sm font-medium text-[#be3a4a] hover:bg-[#fff5f5] disabled:opacity-50"
              >
                驳回并继续训练
              </button>
            </div>
            <p className="mt-3 text-xs text-[#737373]">
              通过后将进入后续实习阶段；驳回则保持当前实例，继续训练迭代。
            </p>
          </section>
        </>
      )}
    </div>
  )
}
