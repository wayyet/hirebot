import { useState } from 'react'
import { Check, Loader2, X } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/infra/api'
import { Breadcrumb } from '@/shared/components/Breadcrumb'

export default function TemplateDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const [fixtureHiring, setFixtureHiring] = useState(false)
  const [actionError, setActionError] = useState('')

  const { data: template, isLoading, error } = useQuery({
    queryKey: ['template', id],
    queryFn: ({ signal }) => api.employeeTemplate.getDetail(id!, signal),
    enabled: !!id,
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  })

  async function hireByFixture() {
    if (!template || fixtureHiring) return

    setFixtureHiring(true)
    setActionError('')
    try {
      const result = await api.employeeTemplate.fixtureHire(template.templateId)
      if (result.status === 'interning_ai' || result.status === 'interning_human') {
        navigate(`/instances/${result.employeeId}/evaluation`)
        return
      }

      navigate(`/instances/${result.employeeId}`)
    } catch (requestError: unknown) {
      setActionError(requestError instanceof Error ? requestError.message : '使用 Fixture 承接实例失败')
    } finally {
      setFixtureHiring(false)
    }
  }

  if (isLoading) {
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
        <Breadcrumb items={[{ label: '模板池', to: '/template-pool' }, { label: '模板详情' }]} />
        <div className="rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900/40 dark:bg-red-950/30 dark:text-red-400">
          {error?.message || '模板不存在'}
        </div>
      </div>
    )
  }

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '模板池', to: '/template-pool' }, { label: template.name }]} />

      {actionError ? (
        <div className="rounded-2xl border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700 dark:border-red-900/40 dark:bg-red-950/30 dark:text-red-400">
          {actionError}
        </div>
      ) : null}

      <section className="hb-card hb-detail-hero">
        <div className="hb-detail-top">
          {template.iconUrl ? (
            <img
              src={template.iconUrl}
              alt={template.name}
              className="h-16 w-16 flex-none rounded-[18px] object-cover"
            />
          ) : (
            <span className="hb-detail-avatar">
              {template.name.slice(0, 1)}
            </span>
          )}

          <div className="hb-detail-main">
            <h1 className="text-[28px] font-semibold leading-tight text-[var(--hb-near-black)]">{template.name}</h1>
            <p className="hb-detail-meta">{template.tagline}</p>
            <p className="hb-detail-desc">{template.description || '暂无模板说明'}</p>
          </div>

          <div className="hb-detail-actions">
            <button type="button" className="hb-btn-primary" onClick={() => navigate(`/hiring/${template.templateId}`)}>
              {template.cta.label || '发起标准雇佣'}
            </button>
            <button type="button" className="hb-btn-ghost" onClick={() => void hireByFixture()} disabled={fixtureHiring}>
              {fixtureHiring ? '承接中...' : '使用 Fixture 数据'}
            </button>
          </div>
        </div>
      </section>

      <section className="hb-detail-split">
        <div className="hb-card hb-detail-panel">
          <h2 className="hb-section-heading">核心能力</h2>
          {template.coreAbilities.length > 0 ? (
            <div className="hb-cap-list">
              {template.coreAbilities.map((ability) => (
                <div key={ability} className="hb-cap">
                  <span className="hb-cap-check"><Check size={12} /></span>
                  <span>{ability}</span>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-[var(--hb-soft)]">暂无</p>
          )}
        </div>

        <div className="hb-card hb-detail-panel">
          <h2 className="hb-section-heading">职责边界</h2>

          {template.responsibilityBoundary.inScope.length > 0 ? (
            <div className="hb-cap-list">
              {template.responsibilityBoundary.inScope.map((item) => (
                <div key={item} className="hb-cap">
                  <span className="hb-cap-check"><Check size={12} /></span>
                  <span>{item}</span>
                </div>
              ))}
            </div>
          ) : (
            <p className="text-sm text-[var(--hb-soft)]">暂无</p>
          )}

          {template.responsibilityBoundary.outOfScope.length > 0 && (
            <>
              <div className="hb-divider" />
              <h3 className="mb-3 text-sm font-medium text-[var(--hb-near-black)]">不可实现</h3>
              <div className="hb-cap-list">
                {template.responsibilityBoundary.outOfScope.map((item) => (
                  <div key={item} className="hb-cap is-muted">
                    <span className="hb-cap-check"><X size={12} /></span>
                    <span>{item}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </div>
      </section>

      {template.prerequisites.length > 0 && (
        <div className="hb-card p-6">
          <h2 className="hb-section-heading">前置准备</h2>
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-[var(--hb-border)] text-left text-[var(--hb-soft)]">
                  <th className="pb-2 pr-4 font-medium">系统</th>
                  <th className="pb-2 pr-4 font-medium">权限</th>
                  <th className="pb-2 pr-4 font-medium">级别</th>
                  <th className="pb-2 font-medium">用途</th>
                </tr>
              </thead>
              <tbody>
                {template.prerequisites.map((prereq) => (
                  <tr key={`${prereq.systemName}-${prereq.permissionName}`} className="border-b border-[var(--hb-border)] last:border-0">
                    <td className="py-3 pr-4 text-[var(--hb-body)]">{prereq.systemName}</td>
                    <td className="py-3 pr-4 text-[var(--hb-body)]">{prereq.permissionName}</td>
                    <td className="py-3 pr-4 text-[var(--hb-body)]">{prereq.requiredLevel}</td>
                    <td className="py-3 text-[var(--hb-body)]">{prereq.purpose}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {template.detailDoc.trim() ? (
        <div className="hb-card p-6">
          <h2 className="hb-section-heading">详细说明</h2>
          <div className="hb-divider" />
          <div className="hb-template-doc">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>
              {template.detailDoc}
            </ReactMarkdown>
          </div>
        </div>
      ) : null}
    </div>
  )
}
