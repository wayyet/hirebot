import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { AlertCircle, ArrowLeft, CheckCircle, Plus, X, XCircle } from 'lucide-react'
import { api, type EmployeeTemplateCard, type EmployeeTemplateDetail } from '@/infra/api'
import Badge from '@/shared/components/Badge'

const MAX_COMPARE_COUNT = 4

function hasTextArrayDifference(details: EmployeeTemplateDetail[], selector: (detail: EmployeeTemplateDetail) => string[]) {
  if (details.length <= 1) {
    return false
  }

  const normalized = details.map((detail) => selector(detail).join('|'))
  return new Set(normalized).size > 1
}

function hasTextDifference(details: EmployeeTemplateDetail[], selector: (detail: EmployeeTemplateDetail) => string) {
  if (details.length <= 1) {
    return false
  }

  const normalized = details.map((detail) => selector(detail).trim())
  return new Set(normalized).size > 1
}

export default function ComparisonPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [compareIds, setCompareIds] = useState<string[]>([])
  const [showAddDialog, setShowAddDialog] = useState(false)
  const [templateCards, setTemplateCards] = useState<EmployeeTemplateCard[]>([])
  const [templateDetails, setTemplateDetails] = useState<Record<string, EmployeeTemplateDetail>>({})
  const [loading, setLoading] = useState(true)
  const [detailLoading, setDetailLoading] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    const ids = searchParams.get('ids')?.split(',').map((item) => item.trim()).filter(Boolean) ?? []
    setCompareIds(ids)
  }, [searchParams])

  useEffect(() => {
    let mounted = true

    async function loadCards() {
      setLoading(true)
      setError('')

      try {
        const response = await api.employeeTemplate.getList({ page: 1, pageSize: 100 })
        if (mounted) {
          setTemplateCards(response.items)
        }
      } catch (err) {
        if (mounted) {
          setError(err instanceof Error ? err.message : '加载模板列表失败')
        }
      } finally {
        if (mounted) {
          setLoading(false)
        }
      }
    }

    loadCards()
    return () => {
      mounted = false
    }
  }, [])

  useEffect(() => {
    const missingTemplateIds = compareIds.filter((id) => !templateDetails[id])
    if (missingTemplateIds.length === 0) {
      return
    }

    let mounted = true
    setDetailLoading(true)

    Promise.all(
      missingTemplateIds.map(async (id) => {
        const detail = await api.employeeTemplate.getDetail(id)
        return [id, detail] as const
      }),
    )
      .then((pairs) => {
        if (!mounted) {
          return
        }

        setTemplateDetails((previous) => {
          const next = { ...previous }
          for (const [id, detail] of pairs) {
            next[id] = detail
          }
          return next
        })
      })
      .catch((err) => {
        if (mounted) {
          setError(err instanceof Error ? err.message : '加载模板详情失败')
        }
      })
      .finally(() => {
        if (mounted) {
          setDetailLoading(false)
        }
      })

    return () => {
      mounted = false
    }
  }, [compareIds, templateDetails])

  const compareTemplates = useMemo(
    () => compareIds.map((id) => templateDetails[id]).filter((item): item is EmployeeTemplateDetail => Boolean(item)),
    [compareIds, templateDetails],
  )

  const availableTemplates = useMemo(
    () => templateCards.filter((item) => !compareIds.includes(item.templateId)),
    [compareIds, templateCards],
  )

  const removeTemplate = (id: string) => {
    const nextIds = compareIds.filter((item) => item !== id)
    setCompareIds(nextIds)
    navigate(nextIds.length > 0 ? `/comparison?ids=${nextIds.join(',')}` : '/comparison', { replace: true })
  }

  const addTemplate = (id: string) => {
    if (compareIds.length >= MAX_COMPARE_COUNT) {
      return
    }

    const nextIds = [...compareIds, id]
    setCompareIds(nextIds)
    navigate(`/comparison?ids=${nextIds.join(',')}`, { replace: true })
    setShowAddDialog(false)
  }

  if (loading) {
    return <div className="max-w-6xl mx-auto px-6 py-6 text-sm text-slate-500">加载模板中...</div>
  }

  if (compareTemplates.length === 0) {
    return (
      <div className="max-w-6xl mx-auto px-6 py-6">
        <button
          onClick={() => navigate('/market')}
          className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 mb-5 transition-colors"
        >
          <ArrowLeft size={14} />
          返回市场
        </button>
        <div className="flex flex-col items-center justify-center py-20">
          <h2 className="text-xl font-bold text-slate-800 mb-2">还没有添加对比项</h2>
          <p className="text-slate-500 text-sm mb-6">请先从模板列表中选择待对比员工。</p>
          <button
            onClick={() => navigate('/market')}
            className="px-5 py-2.5 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors"
          >
            去市场浏览
          </button>
        </div>
      </div>
    )
  }

  const descHasDifference = hasTextDifference(compareTemplates, (item) => item.description)
  const coreAbilityHasDifference = hasTextArrayDifference(compareTemplates, (item) => item.coreAbilities)
  const inScopeHasDifference = hasTextArrayDifference(compareTemplates, (item) => item.responsibilityBoundary.inScope)
  const outOfScopeHasDifference = hasTextArrayDifference(compareTemplates, (item) => item.responsibilityBoundary.outOfScope)
  const prereqHasDifference = hasTextArrayDifference(compareTemplates, (item) =>
    item.prerequisites.map((entry) => `${entry.systemName}|${entry.permissionName}|${entry.requiredLevel}|${entry.purpose}`),
  )
  const successHasDifference = hasTextArrayDifference(compareTemplates, (item) => item.successCases)

  return (
    <div className="max-w-7xl mx-auto px-6 py-6">
      <button
        onClick={() => navigate('/market')}
        className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 mb-5 transition-colors"
      >
        <ArrowLeft size={14} />
        返回市场
      </button>

      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-xl font-bold text-slate-800">数字员工对比</h1>
          <p className="text-sm text-slate-500 mt-1">对比 {compareTemplates.length} 个模板</p>
        </div>
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2 text-xs text-slate-500">
            <div className="w-4 h-4 bg-pink-50 border border-pink-100 rounded" />
            <span>粉色标记表示该项存在差异</span>
          </div>
          {compareTemplates.length < MAX_COMPARE_COUNT && (
            <button
              onClick={() => setShowAddDialog(true)}
              className="px-4 py-2 border border-indigo-200 text-indigo-600 rounded-lg text-sm font-medium hover:bg-indigo-50 transition-colors flex items-center gap-2"
            >
              <Plus size={14} />
              添加对比项
            </button>
          )}
        </div>
      </div>

      {error && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 flex items-center gap-2">
          <AlertCircle size={14} />
          {error}
        </div>
      )}

      <div className="bg-white rounded-xl border border-slate-100 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-100">
                <th className="text-left p-4 bg-slate-50 text-xs font-semibold text-slate-500 uppercase w-44 sticky left-0 z-10">
                  对比项
                </th>
                {compareTemplates.map((template) => (
                  <th key={template.templateId} className="p-4 bg-slate-50 min-w-[280px]">
                    <div className="flex items-start gap-3">
                      <img src={template.iconUrl} alt={template.name} className="w-10 h-10 rounded-lg object-cover bg-slate-100 shrink-0" />
                      <div className="flex-1 text-left">
                        <div className="font-semibold text-slate-800 text-sm mb-1">{template.name}</div>
                        <div className="text-xs text-slate-500">{template.tagline}</div>
                      </div>
                      <button
                        onClick={() => removeTemplate(template.templateId)}
                        className="w-6 h-6 rounded-full hover:bg-slate-200 flex items-center justify-center text-slate-400 hover:text-slate-600 transition-colors"
                      >
                        <X size={14} />
                      </button>
                    </div>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">能力标签</td>
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className="p-4">
                    <div className="flex flex-wrap gap-1.5">
                      {template.coreAbilities.map((ability) => (
                        <Badge key={ability} variant="gray">{ability}</Badge>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">模板说明</td>
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className={`p-4 text-sm text-slate-600 ${descHasDifference ? 'bg-pink-50' : ''}`}>
                    {template.description}
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">核心能力</td>
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className={`p-4 ${coreAbilityHasDifference ? 'bg-pink-50' : ''}`}>
                    <div className="space-y-1.5">
                      {template.coreAbilities.map((item) => (
                        <div key={item} className="flex items-center gap-2 text-xs text-slate-600">
                          <CheckCircle size={12} className="text-emerald-500 shrink-0" />
                          {item}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">职责范围内</td>
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className={`p-4 ${inScopeHasDifference ? 'bg-pink-50' : ''}`}>
                    <div className="space-y-1.5">
                      {template.responsibilityBoundary.inScope.map((item) => (
                        <div key={item} className="flex items-start gap-2 text-xs text-slate-600">
                          <CheckCircle size={12} className="text-emerald-500 shrink-0 mt-0.5" />
                          {item}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">职责范围外</td>
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className={`p-4 ${outOfScopeHasDifference ? 'bg-pink-50' : ''}`}>
                    <div className="space-y-1.5">
                      {template.responsibilityBoundary.outOfScope.map((item) => (
                        <div key={item} className="flex items-start gap-2 text-xs text-slate-500">
                          <XCircle size={12} className="text-slate-400 shrink-0 mt-0.5" />
                          {item}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">接入前提</td>
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className={`p-4 ${prereqHasDifference ? 'bg-pink-50' : ''}`}>
                    <div className="space-y-2">
                      {template.prerequisites.map((item) => (
                        <div key={`${item.systemName}-${item.permissionName}-${item.requiredLevel}`} className="rounded border border-slate-200 p-2 text-xs text-slate-600">
                          <div className="font-medium text-slate-700">{item.systemName}</div>
                          <div className="mt-1">{item.permissionName} ({item.requiredLevel})</div>
                          <div className="mt-1 text-slate-500">{item.purpose}</div>
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">成功案例</td>
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className={`p-4 ${successHasDifference ? 'bg-pink-50' : ''}`}>
                    <div className="space-y-1.5">
                      {template.successCases.map((item) => (
                        <div key={item} className="text-xs text-slate-600">{item}</div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              <tr>
                <td className="p-4 bg-slate-50 sticky left-0 z-10" />
                {compareTemplates.map((template) => (
                  <td key={template.templateId} className="p-4">
                    <div className="flex flex-col gap-2">
                      <button
                        onClick={() => navigate(`/hiring/${template.templateId}`)}
                        className="w-full px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors"
                      >
                        开始雇佣
                      </button>
                      <button
                        onClick={() => navigate(`/templates/${template.templateId}`)}
                        className="w-full px-4 py-2 border border-slate-200 text-slate-600 rounded-lg text-sm hover:bg-slate-50 transition-colors"
                      >
                        查看详情
                      </button>
                    </div>
                  </td>
                ))}
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      {showAddDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-6" onClick={() => setShowAddDialog(false)}>
          <div className="bg-white rounded-2xl max-w-3xl w-full max-h-[80vh] overflow-hidden flex flex-col" onClick={(event) => event.stopPropagation()}>
            <div className="px-6 py-4 border-b border-slate-200 flex items-center justify-between">
              <h2 className="text-lg font-bold text-slate-800">添加对比项</h2>
              <button
                onClick={() => setShowAddDialog(false)}
                className="w-8 h-8 rounded-lg hover:bg-slate-100 flex items-center justify-center text-slate-400 hover:text-slate-600 transition-colors"
              >
                <X size={16} />
              </button>
            </div>
            <div className="flex-1 overflow-y-auto p-6">
              <div className="grid grid-cols-2 gap-4">
                {availableTemplates.map((template) => (
                  <button
                    key={template.templateId}
                    onClick={() => addTemplate(template.templateId)}
                    className="text-left bg-white rounded-xl border border-slate-100 p-4 hover:shadow-md transition-all group"
                  >
                    <div className="flex items-start gap-3 mb-2">
                      <img src={template.iconUrl} alt={template.name} className="w-10 h-10 rounded-lg object-cover bg-slate-100 shrink-0" />
                      <div className="flex-1">
                        <h3 className="font-semibold text-slate-800 mb-1 group-hover:text-indigo-600 transition-colors">{template.name}</h3>
                        <div className="flex gap-1 flex-wrap">
                          {template.coreAbilityTags.map((tag) => (
                            <Badge key={tag} variant="gray">{tag}</Badge>
                          ))}
                        </div>
                      </div>
                    </div>
                    <p className="text-xs text-slate-500 line-clamp-2">{template.tagline}</p>
                  </button>
                ))}
              </div>
              {availableTemplates.length === 0 && (
                <div className="text-sm text-slate-500 py-10 text-center">没有可添加的模板。</div>
              )}
            </div>
          </div>
        </div>
      )}

      {detailLoading && <div className="mt-4 text-xs text-slate-500">正在加载对比详情...</div>}
    </div>
  )
}
