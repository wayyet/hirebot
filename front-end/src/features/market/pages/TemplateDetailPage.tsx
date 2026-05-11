import { useEffect, useState } from 'react'
import { ArrowLeft, Loader2 } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type EmployeeTemplateDetail } from '@/infra/api'

export default function TemplateDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [template, setTemplate] = useState<EmployeeTemplateDetail | null>(null)
  const [fixtureHiring, setFixtureHiring] = useState(false)

  useEffect(() => {
    if (!id) {
      setLoading(false)
      setError('模板 ID 无效')
      return
    }

    let cancelled = false
    setLoading(true)
    setError('')

    api.employeeTemplate.getDetail(id)
      .then((data) => {
        if (!cancelled) {
          setTemplate(data)
        }
      })
      .catch((requestError: unknown) => {
        if (!cancelled) {
          setTemplate(null)
          setError(requestError instanceof Error ? requestError.message : '模板详情加载失败')
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false)
        }
      })

    return () => {
      cancelled = true
    }
  }, [id])

  async function hireByFixture() {
    if (!template || fixtureHiring) return

    setFixtureHiring(true)
    setError('')
    try {
      const result = await api.employeeTemplate.fixtureHire(template.templateId)
      if (result.status === 'interning_ai' || result.status === 'interning_human') {
        navigate(`/instances/${result.employeeId}/evaluation`)
        return
      }

      navigate(`/instances/${result.employeeId}`)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '使用 Fixture 承接实例失败')
    } finally {
      setFixtureHiring(false)
    }
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[var(--hb-soft)]">
          <Loader2 size={16} className="animate-spin" />
          正在加载模板详情...
        </div>
      </div>
    )
  }

  if (error || !template) {
    return (
      <div className="hb-page space-y-4">
        <button type="button" onClick={() => navigate('/template-pool')} className="hb-btn-ghost">
          <ArrowLeft size={14} />
          返回模板池
        </button>
        <div className="rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900/40 dark:bg-red-950/30 dark:text-red-400">
          {error || '模板不存在'}
        </div>
      </div>
    )
  }

  return (
    <div className="hb-page space-y-5">
      <button type="button" onClick={() => navigate('/template-pool')} className="hb-btn-ghost">
        <ArrowLeft size={14} />
        返回模板池
      </button>

      <div className="hb-card p-6">
        <div className="flex flex-wrap items-start gap-4">
          <span className="hb-squircle h-16 w-16 bg-blue-100 text-2xl text-blue-600 dark:bg-blue-900/30 dark:text-blue-400">
            {template.name.slice(0, 1)}
          </span>
          <div className="min-w-0 flex-1">
            <h1 className="text-[30px] font-semibold leading-tight text-[var(--hb-near-black)]">{template.name}</h1>
            <p className="mt-2 text-sm text-[var(--hb-soft)]">{template.tagline}</p>
            <p className="mt-3 text-sm leading-relaxed text-[var(--hb-body)]">{template.description || '暂无模板说明'}</p>
          </div>
          <div className="flex flex-col gap-2">
            <button type="button" className="hb-btn-primary" onClick={() => navigate(`/hiring/${template.templateId}`)}>
              {template.cta.label || '发起标准雇佣'}
            </button>
            <button type="button" className="hb-btn-ghost" onClick={() => void hireByFixture()} disabled={fixtureHiring}>
              {fixtureHiring ? '承接中...' : '使用 Fixture 数据'}
            </button>
          </div>
        </div>
      </div>

      {template.detailDoc.trim() ? (
        <div className="hb-card p-6">
          <h2 className="text-base font-semibold text-[var(--hb-near-black)]">详细说明</h2>
          <div className="hb-template-doc mt-4">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>
              {template.detailDoc}
            </ReactMarkdown>
          </div>
        </div>
      ) : null}
    </div>
  )
}
