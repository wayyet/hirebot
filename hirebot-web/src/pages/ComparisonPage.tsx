import { useState, useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { ArrowLeft, X, CheckCircle, XCircle, Star, Zap, AlertCircle, Plus } from 'lucide-react'
import { templates } from '../mock/data'
import Badge from '../components/Badge'

const levelColor = { '低': 'green', '中': 'yellow', '高': 'red' } as const

export default function ComparisonPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [compareIds, setCompareIds] = useState<string[]>([])
  const [showAddDialog, setShowAddDialog] = useState(false)

  // 从 URL 参数初始化对比列表
  useEffect(() => {
    const ids = searchParams.get('ids')?.split(',').filter(Boolean) || []
    setCompareIds(ids)
  }, [searchParams])

  const compareTemplates = compareIds.map(id => templates.find(t => t.id === id)).filter(Boolean)
  const availableTemplates = templates.filter(t => !compareIds.includes(t.id))

  // 检查某个字段在所有模板中是否有差异
  const hasDifference = (field: string) => {
    if (compareTemplates.length <= 1) return false
    const values = compareTemplates.map(t => {
      const value = (t as any)[field]
      return typeof value === 'object' ? JSON.stringify(value) : value
    })
    return new Set(values).size > 1
  }

  // 检查数组字段是否有差异
  const hasArrayDifference = (field: string) => {
    if (compareTemplates.length <= 1) return false
    const allValues = new Set<string>()
    compareTemplates.forEach(t => {
      const arr = (t as any)[field] as string[]
      arr.forEach(item => allValues.add(item))
    })
    // 如果任何一个模板缺少某个值，就认为有差异
    return compareTemplates.some(t => {
      const arr = (t as any)[field] as string[]
      return arr.length !== allValues.size
    })
  }

  const removeTemplate = (id: string) => {
    const newIds = compareIds.filter(i => i !== id)
    setCompareIds(newIds)
    navigate(`/comparison?ids=${newIds.join(',')}`, { replace: true })
  }

  const addTemplate = (id: string) => {
    if (compareIds.length >= 4) {
      alert('最多只能对比 4 个数字员工')
      return
    }
    const newIds = [...compareIds, id]
    setCompareIds(newIds)
    navigate(`/comparison?ids=${newIds.join(',')}`, { replace: true })
    setShowAddDialog(false)
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
          <div className="text-6xl mb-4">📊</div>
          <h2 className="text-xl font-bold text-slate-800 mb-2">还没有添加对比项</h2>
          <p className="text-slate-500 text-sm mb-6">在数字员工详情页点击"加入比较"开始对比</p>
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
          <p className="text-sm text-slate-500 mt-1">对比 {compareTemplates.length} 个数字员工模板</p>
        </div>
        <div className="flex items-center gap-4">
          <div className="flex items-center gap-2 text-xs text-slate-500">
            <div className="w-4 h-4 bg-pink-50 border border-pink-100 rounded"></div>
            <span>粉色标记表示该项存在差异</span>
          </div>
          {compareTemplates.length < 4 && (
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

      {/* Comparison Table */}
      <div className="bg-white rounded-xl border border-slate-100 overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b border-slate-100">
                <th className="text-left p-4 bg-slate-50 text-xs font-semibold text-slate-500 uppercase w-48 sticky left-0 z-10">
                  对比项
                </th>
                {compareTemplates.map(t => (
                  <th key={t!.id} className="p-4 bg-slate-50 min-w-[280px]">
                    <div className="flex items-start gap-3">
                      <div className="w-12 h-12 rounded-lg bg-indigo-50 flex items-center justify-center text-2xl shrink-0">
                        {t!.primaryFunction === '销售' ? '💼' : t!.primaryFunction === '客服' ? '🎧' : t!.primaryFunction === '法务' ? '📋' : t!.primaryFunction === '数据分析' ? '📊' : t!.primaryFunction === '人力资源' ? '👥' : '📣'}
                      </div>
                      <div className="flex-1 text-left">
                        <div className="font-semibold text-slate-800 text-sm mb-1">{t!.name}</div>
                        <div className="text-xs text-slate-500">{t!.oneLiner}</div>
                      </div>
                      <button
                        onClick={() => removeTemplate(t!.id)}
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
              {/* 基本信息 */}
              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  行业 / 职能
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasDifference('primaryIndustry') || hasDifference('primaryFunction') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="flex gap-2">
                      <Badge variant="gray">{t!.primaryIndustry}</Badge>
                      <Badge variant="blue">{t!.primaryFunction}</Badge>
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  转正率
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasDifference('graduationRate') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="flex items-center gap-2">
                      <Star size={14} className="text-amber-400 fill-amber-400" />
                      <span className="text-sm font-semibold text-slate-800">{t!.graduationRate}%</span>
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  出首价值时间
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasDifference('estimatedTimeToFirstValue') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="flex items-center gap-2">
                      <Zap size={14} className="text-indigo-400" />
                      <span className="text-sm text-slate-700">{t!.estimatedTimeToFirstValue}</span>
                    </div>
                  </td>
                ))}
              </tr>

              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  带教门槛
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasDifference('onboardingRequirementLevel') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="flex items-center gap-2">
                      <AlertCircle size={14} className={t!.onboardingRequirementLevel === '低' ? 'text-emerald-400' : t!.onboardingRequirementLevel === '中' ? 'text-amber-400' : 'text-red-400'} />
                      <Badge variant={levelColor[t!.onboardingRequirementLevel]}>{t!.onboardingRequirementLevel}</Badge>
                    </div>
                  </td>
                ))}
              </tr>

              {/* 核心能力 */}
              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  核心能力
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasArrayDifference('coreCapabilities') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="space-y-1.5">
                      {t!.coreCapabilities.map(cap => (
                        <div key={cap} className="flex items-center gap-2 text-xs text-slate-600">
                          <CheckCircle size={12} className="text-emerald-500 shrink-0" />
                          {cap}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              {/* 核心优势 */}
              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  核心优势
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasArrayDifference('topBenefits') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="space-y-1.5">
                      {t!.topBenefits.map(benefit => (
                        <div key={benefit} className="flex items-start gap-2 text-xs text-slate-600">
                          <span className="mt-0.5 w-1 h-1 rounded-full bg-indigo-400 shrink-0" />
                          {benefit}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              {/* 职责范围内 */}
              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  职责范围内
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasArrayDifference('inScopeItems') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="space-y-1.5">
                      {t!.inScopeItems.map(item => (
                        <div key={item} className="flex items-start gap-2 text-xs text-slate-600">
                          <CheckCircle size={12} className="text-emerald-500 shrink-0 mt-0.5" />
                          {item}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              {/* 不做什么 */}
              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  不做什么
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasArrayDifference('outOfScopeItems') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="space-y-1.5">
                      {t!.outOfScopeItems.map(item => (
                        <div key={item} className="flex items-start gap-2 text-xs text-slate-500">
                          <XCircle size={12} className="text-slate-400 shrink-0 mt-0.5" />
                          {item}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              {/* 需要准备 */}
              <tr className="border-b border-slate-50">
                <td className="p-4 bg-slate-50 text-sm font-medium text-slate-700 sticky left-0 z-10">
                  需要准备
                </td>
                {compareTemplates.map(t => (
                  <td 
                    key={t!.id} 
                    className={`p-4 ${hasArrayDifference('requiredSystems') ? 'bg-pink-50' : ''}`}
                  >
                    <div className="space-y-1.5">
                      {t!.requiredSystems.map(sys => (
                        <div key={sys} className="flex items-start gap-2 text-xs text-slate-600">
                          <AlertCircle size={12} className="text-amber-400 shrink-0 mt-0.5" />
                          {sys}
                        </div>
                      ))}
                    </div>
                  </td>
                ))}
              </tr>

              {/* 操作 */}
              <tr>
                <td className="p-4 bg-slate-50 sticky left-0 z-10"></td>
                {compareTemplates.map(t => (
                  <td key={t!.id} className="p-4">
                    <div className="flex flex-col gap-2">
                      <button
                        onClick={() => navigate(`/hiring/${t!.id}`)}
                        className="w-full px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors"
                      >
                        开始雇佣
                      </button>
                      <button
                        onClick={() => navigate(`/templates/${t!.id}`)}
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

      {/* Add Template Dialog */}
      {showAddDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-6" onClick={() => setShowAddDialog(false)}>
          <div className="bg-white rounded-2xl max-w-3xl w-full max-h-[80vh] overflow-hidden flex flex-col" onClick={(e) => e.stopPropagation()}>
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
                {availableTemplates.map(t => (
                  <div
                    key={t.id}
                    onClick={() => addTemplate(t.id)}
                    className="bg-white rounded-xl border border-slate-100 p-4 hover:shadow-md transition-all cursor-pointer group"
                  >
                    <div className="flex items-start gap-3 mb-3">
                      <div className="w-10 h-10 rounded-lg bg-indigo-50 flex items-center justify-center text-xl shrink-0">
                        {t.primaryFunction === '销售' ? '💼' : t.primaryFunction === '客服' ? '🎧' : t.primaryFunction === '法务' ? '📋' : t.primaryFunction === '数据分析' ? '📊' : t.primaryFunction === '人力资源' ? '👥' : '📣'}
                      </div>
                      <div className="flex-1">
                        <h3 className="font-semibold text-slate-800 mb-1 group-hover:text-indigo-600 transition-colors">{t.name}</h3>
                        <div className="flex gap-1">
                          <Badge variant="gray">{t.primaryFunction}</Badge>
                          <Badge variant="gray">{t.primaryIndustry}</Badge>
                        </div>
                      </div>
                    </div>
                    <p className="text-xs text-slate-500 line-clamp-2">{t.oneLiner}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
