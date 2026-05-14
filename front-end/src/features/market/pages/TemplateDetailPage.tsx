import { Loader2 } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '@/infra/api'
import { Breadcrumb } from '@/shared/components/Breadcrumb'

export default function TemplateDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const { data: template, isLoading, error } = useQuery({
    queryKey: ['template', id],
    queryFn: ({ signal }) => api.employeeTemplate.getDetail(id!, signal),
    enabled: !!id,
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  })

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

      <section className="hb-card hb-detail-hero">
        <div className="hb-detail-top">
          <div className="hb-detail-main">
            <h1 className="text-[28px] font-semibold leading-tight text-[var(--hb-near-black)]">{template.name}</h1>
            <p className="hb-detail-meta">{template.tagline}</p>
            <p className="hb-detail-desc">{template.description || '暂无模板说明'}</p>
          </div>
          <div className="flex flex-col gap-2">
            <button type="button" className="hb-btn-primary" onClick={() => navigate(`/template-pool/hiring/${template.templateId}`)}>
              {template.cta.label || '发起标准雇佣'}
            </button>
          </div>
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
