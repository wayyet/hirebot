import { useEffect, useMemo, useState } from 'react'
import { AlertCircle, ArrowLeft, CheckCircle2, Loader2, ShieldAlert, ShieldCheck } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type EmployeeDetail, type EvaluationState } from '@/infra/api'

function verdictLabel(verdict?: string | null) {
  if (verdict === 'passed') return '通过'
  if (verdict === 'failed') return '不通过'
  if (verdict === 'warning') return '待优化'
  return '待判定'
}

function verdictClass(verdict?: string | null) {
  if (verdict === 'passed') return 'green'
  if (verdict === 'failed') return 'orange'
  if (verdict === 'warning') return 'orange'
  return 'gray'
}

export default function HumanEvaluationPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [evaluation, setEvaluation] = useState<EvaluationState | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)

  async function loadData() {
    if (!id) return
    setLoading(true)
    setError('')
    try {
      const [employeeData, evaluationData] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getEvaluationState(id),
      ])
      setEmployee(employeeData)
      setEvaluation(evaluationData)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '加载人工评估数据失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    void loadData()
  }, [id])

  const passedCount = useMemo(() => {
    return evaluation?.scenarios.filter((scenario) => scenario.verdict === 'passed').length ?? 0
  }, [evaluation])

  const failedCount = useMemo(() => {
    return evaluation?.scenarios.filter((scenario) => scenario.verdict === 'failed').length ?? 0
  }, [evaluation])

  async function submitDecision(decision: 'ONBOARD' | 'REJECT' | 'FORCE') {
    if (!id) return
    setSubmitting(true)
    setError('')
    try {
      const updated = await api.employeeRuntime.submitOnboardingDecision(id, { decision })
      setEmployee(updated)

      if (decision === 'REJECT') {
        navigate(`/instances/${id}/review`)
        return
      }

      navigate(`/instances/${id}`)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '提交人工评估结论失败')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="hb-page space-y-5">
      <button
        type="button"
        onClick={() => navigate(id ? `/instances/${id}` : '/department-employees')}
        className="hb-btn-ghost"
      >
        <ArrowLeft size={14} />
        返回实例
      </button>

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
          正在加载人工评估...
        </div>
      ) : !employee || !evaluation ? (
        <div className="hb-card p-8 text-sm text-[#737373]">人工评估数据不存在</div>
      ) : (
        <>
          <section className="hb-hero">
            <div className="hb-hero-grid">
              <div className="hb-toolbar">
                <div>
                  <div className="hb-hero-eyebrow">人工评估</div>
                  <h1 className="hb-hero-title">人工评估 · {employee.nickname}</h1>
                  <p className="hb-hero-copy">
                    场景通过：{passedCount}/{evaluation.scenarios.length} · AI 建议：{evaluation.recommendation}
                  </p>
                </div>
                <span className={`hb-pill ${failedCount > 0 ? 'orange' : 'green'}`}>
                  {failedCount > 0 ? '存在未通过场景' : '可进入待上岗'}
                </span>
              </div>
              <div className="hb-hero-metrics">
                <div className="hb-metric-card">
                  <div className="hb-metric-label">通过</div>
                  <div className="hb-metric-value">{passedCount}</div>
                </div>
                <div className="hb-metric-card">
                  <div className="hb-metric-label">未通过</div>
                  <div className="hb-metric-value">{failedCount}</div>
                </div>
                <div className="hb-metric-card">
                  <div className="hb-metric-label">总场景</div>
                  <div className="hb-metric-value">{evaluation.scenarios.length}</div>
                </div>
              </div>
            </div>
          </section>

          <section className="hb-card p-6">
            <h2 className="text-base font-semibold text-[#0a0a0a]">评估场景明细</h2>
            <div className="mt-4 space-y-3">
              {evaluation.scenarios.map((scenario) => (
                <div key={scenario.scenarioId} className="rounded-2xl border border-[#ececec] bg-white px-4 py-3">
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="text-sm font-semibold text-[#0a0a0a]">{scenario.scenarioName}</div>
                    <span className={`hb-pill ${verdictClass(scenario.verdict)}`}>
                      {verdictLabel(scenario.verdict)}
                    </span>
                  </div>
                  <div className="mt-1 text-xs text-[#737373]">状态：{scenario.status}</div>
                  {scenario.verdictComment && (
                    <div className="mt-3 rounded-xl border border-[#f3f4f6] bg-[#fafafa] px-3 py-2 text-xs leading-relaxed text-[#404040]">
                      {scenario.verdictComment}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </section>

          <section className="hb-card p-6">
            <h2 className="mb-3 text-base font-semibold text-[#0a0a0a]">人工评估结论</h2>
            <div className="flex flex-wrap items-center gap-2">
              <button
                type="button"
                disabled={submitting}
                onClick={() => void submitDecision('ONBOARD')}
                className="hb-btn-primary"
              >
                <ShieldCheck size={14} />
                通过并置为待上岗
              </button>
              <button
                type="button"
                disabled={submitting}
                onClick={() => void submitDecision('REJECT')}
                className="rounded-full border border-[#fde2e2] bg-white px-4 py-2 text-sm font-medium text-[#be3a4a] hover:bg-[#fff5f5] disabled:opacity-50"
              >
                <ShieldAlert size={14} />
                不通过并进入 Review
              </button>
              <button
                type="button"
                disabled={submitting}
                onClick={() => void submitDecision('FORCE')}
                className="hb-btn-ghost"
              >
                <CheckCircle2 size={14} />
                强制通过（待上岗）
              </button>
            </div>
            <p className="mt-3 text-xs text-[#737373]">
              通过后状态会进入“待上岗”，后续由飞书身份配置与上岗流程切换为 live。
            </p>
          </section>
        </>
      )}
    </div>
  )
}
