import { useEffect, useMemo, useState } from 'react'
import { ArrowLeft, Loader2 } from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type EmployeeTemplateDetail, type TemplatePrerequisite } from '@/infra/api'

type ScenarioSections = {
  companyScale: string[]
  useCases: string[]
  teamSetup: string[]
  others: string[]
}

function splitLabeledText(value: string): { label: string; content: string } | null {
  const match = /^([^:：]+)\s*[:：]\s*(.+)$/.exec(value.trim())
  if (!match) {
    return null
  }

  return {
    label: match[1].trim(),
    content: match[2].trim(),
  }
}

function buildScenarioSections(items: string[]): ScenarioSections {
  const sections: ScenarioSections = {
    companyScale: [],
    useCases: [],
    teamSetup: [],
    others: [],
  }

  items.forEach((item) => {
    const normalized = item.trim()
    if (!normalized) {
      return
    }

    const labeledText = splitLabeledText(normalized)
    if (!labeledText) {
      sections.useCases.push(normalized)
      return
    }

    if (labeledText.label === '企业规模') {
      sections.companyScale.push(labeledText.content)
      return
    }

    if (labeledText.label === '使用场景' || labeledText.label === '适用场景') {
      sections.useCases.push(labeledText.content)
      return
    }

    if (labeledText.label === '团队配置') {
      sections.teamSetup.push(labeledText.content)
      return
    }

    sections.others.push(normalized)
  })

  return sections
}

function normalizeRequiredLevel(value: string) {
  return value.trim().toLowerCase()
}

function isRequired(value: string) {
  const normalized = normalizeRequiredLevel(value)
  return normalized.includes('必需') || normalized.includes('required') || normalized.includes('must')
}

function isOptional(value: string) {
  const normalized = normalizeRequiredLevel(value)
  return normalized.includes('可选') || normalized.includes('optional')
}

function groupPrerequisites(items: TemplatePrerequisite[]) {
  const required = items.filter((item) => isRequired(item.requiredLevel))
  const optional = items.filter((item) => isOptional(item.requiredLevel))
  const others = items.filter((item) => !isRequired(item.requiredLevel) && !isOptional(item.requiredLevel))
  return { required, optional, others }
}

function SectionList({ items, emptyText }: { items: string[]; emptyText: string }) {
  if (items.length === 0) {
    return <p className="text-sm text-[#737373]">{emptyText}</p>
  }

  return (
    <ul className="space-y-2">
      {items.map((item) => (
        <li key={item} className="text-sm leading-relaxed text-[#404040]">• {item}</li>
      ))}
    </ul>
  )
}

function PrerequisiteList({ items, emptyText }: { items: TemplatePrerequisite[]; emptyText: string }) {
  if (items.length === 0) {
    return <p className="text-sm text-[#737373]">{emptyText}</p>
  }

  return (
    <div className="space-y-3">
      {items.map((item) => (
        <div key={`${item.systemName}-${item.permissionName}-${item.requiredLevel}`} className="rounded-xl border border-[#f3f4f6] bg-[#fafafa] p-3">
          <div className="text-sm font-semibold text-[#0a0a0a]">{item.systemName}</div>
          <div className="mt-1 text-xs text-[#737373]">{item.permissionName}</div>
          {item.purpose && <p className="mt-2 text-xs leading-relaxed text-[#404040]">{item.purpose}</p>}
        </div>
      ))}
    </div>
  )
}

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

  const scenarios = useMemo(() => {
    return template ? buildScenarioSections(template.responsibilityBoundary.inScope) : null
  }, [template])

  const prerequisites = useMemo(() => {
    return template ? groupPrerequisites(template.prerequisites) : null
  }, [template])

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载模板详情...
        </div>
      </div>
    )
  }

  if (error || !template || !scenarios || !prerequisites) {
    return (
      <div className="hb-page space-y-4">
        <button type="button" onClick={() => navigate('/template-pool')} className="hb-btn-ghost">
          <ArrowLeft size={14} />
          返回模板池
        </button>
        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
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
          <span className="hb-squircle h-16 w-16 bg-[#dde9ff] text-2xl text-[#3d5cff]">
            {template.name.slice(0, 1)}
          </span>
          <div className="min-w-0 flex-1">
            <h1 className="text-[30px] font-semibold leading-tight text-[#0a0a0a]">{template.name}</h1>
            <p className="mt-2 text-sm text-[#737373]">{template.tagline}</p>
            <p className="mt-3 text-sm leading-relaxed text-[#404040]">{template.description || '暂无模板说明'}</p>
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
          <h2 className="text-base font-semibold text-[#0a0a0a]">详细说明</h2>
          <div className="hb-template-doc mt-4">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>
              {template.detailDoc}
            </ReactMarkdown>
          </div>
        </div>
      ) : null}

      <div className="grid gap-5 xl:grid-cols-2">
        <div className="hb-card p-6">
          <h2 className="text-base font-semibold text-[#0a0a0a]">核心能力</h2>
          <div className="mt-3">
            <SectionList items={template.coreAbilities} emptyText="暂无能力说明" />
          </div>
        </div>

        <div className="hb-card p-6">
          <h2 className="text-base font-semibold text-[#0a0a0a]">能力边界</h2>
          <div className="mt-3">
            <SectionList items={template.responsibilityBoundary.outOfScope} emptyText="暂无边界说明" />
          </div>
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">适用场景</h2>
        <div className="mt-4 grid gap-4 md:grid-cols-3">
          <div>
            <h3 className="mb-2 text-xs font-semibold text-[#404040]">企业规模</h3>
            <SectionList items={scenarios.companyScale} emptyText="暂无说明" />
          </div>
          <div>
            <h3 className="mb-2 text-xs font-semibold text-[#404040]">使用场景</h3>
            <SectionList items={scenarios.useCases} emptyText="暂无说明" />
          </div>
          <div>
            <h3 className="mb-2 text-xs font-semibold text-[#404040]">团队配置</h3>
            <SectionList items={scenarios.teamSetup} emptyText="暂无说明" />
          </div>
        </div>
        {scenarios.others.length > 0 && (
          <div className="mt-4">
            <h3 className="mb-2 text-xs font-semibold text-[#404040]">其他</h3>
            <SectionList items={scenarios.others} emptyText="暂无说明" />
          </div>
        )}
      </div>

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">前置准备</h2>
        <div className="mt-4 grid gap-4 md:grid-cols-2">
          <div>
            <h3 className="mb-2 text-xs font-semibold text-[#404040]">必需</h3>
            <PrerequisiteList items={prerequisites.required} emptyText="暂无必需项" />
          </div>
          <div>
            <h3 className="mb-2 text-xs font-semibold text-[#404040]">可选</h3>
            <PrerequisiteList items={prerequisites.optional} emptyText="暂无可选项" />
          </div>
        </div>
        {prerequisites.others.length > 0 && (
          <div className="mt-4">
            <h3 className="mb-2 text-xs font-semibold text-[#404040]">其他</h3>
            <PrerequisiteList items={prerequisites.others} emptyText="暂无其他项" />
          </div>
        )}
      </div>
    </div>
  )
}
