import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft } from 'lucide-react'
import { templates } from '../mock/data'
import { loadUserTemplates } from '../utils/storage'

function emoji(fn: string) {
  return fn === '销售' ? '💼' : fn === '客服' ? '🎧' : fn === '法务' ? '📋' :
    fn === '数据分析' ? '📊' : fn === '人力资源' ? '👥' : '📣'
}

export default function TemplateDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const t = templates.find(t => t.id === id) ?? loadUserTemplates().find(t => t.id === id)

  if (!t) return (
    <div className="flex items-center justify-center h-full text-slate-400">模板不存在</div>
  )

  return (
    <div className="min-h-screen bg-white">
      {/* Header */}
      <div className="border-b border-slate-100">
        <div className="max-w-3xl mx-auto px-8 py-6">
          <button
            onClick={() => navigate(-1)}
            className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 mb-6 transition-colors"
          >
            <ArrowLeft size={14} />
            返回
          </button>

          <div className="flex items-start gap-5">
            <div className="text-5xl shrink-0">{emoji(t.primaryFunction)}</div>
            <div className="flex-1 min-w-0">
              <h1 className="text-2xl font-semibold text-slate-900 mb-1">{t.name}</h1>
              <p className="text-slate-500 text-sm">{t.oneLiner}</p>
            </div>
            <div className="flex items-center gap-2 shrink-0">
              <button
                onClick={() => navigate(`/hiring/${t.id}`)}
                className="px-6 py-2.5 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors whitespace-nowrap"
              >
                开始雇佣
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Content */}
      <div className="max-w-3xl mx-auto px-8 py-10 space-y-10">

        {/* 模板说明 */}
        <div>
          <h2 className="text-base font-semibold text-slate-900 mb-6">模板说明</h2>

          {/* 它能做什么 */}
          <section className="mb-8">
            <h3 className="text-sm font-semibold text-slate-700 mb-3">它能做什么</h3>
            <ul className="space-y-2">
              {t.inScopeItems.map(item => (
                <li key={item} className="flex items-start gap-2 text-sm text-slate-600">
                  <span className="mt-1.5 w-1.5 h-1.5 rounded-full bg-slate-400 shrink-0" />
                  {item}
                </li>
              ))}
            </ul>
          </section>

          {/* 能力边界 */}
          <section className="mb-8">
            <h3 className="text-sm font-semibold text-slate-700 mb-3">能力边界</h3>
            <ul className="space-y-2">
              {t.outOfScopeItems.map(item => (
                <li key={item} className="flex items-start gap-2 text-sm text-slate-600">
                  <span className="mt-1.5 w-1.5 h-1.5 rounded-full bg-slate-400 shrink-0" />
                  {item}
                </li>
              ))}
            </ul>
          </section>

          {/* 适用场景 */}
          {t.applicableScenes && (
            <section className="mb-8">
              <h3 className="text-sm font-semibold text-slate-700 mb-3">适用场景</h3>
              <div className="space-y-3">
                <div className="flex gap-3">
                  <span className="text-xs text-slate-400 w-14 shrink-0 pt-0.5">企业规模</span>
                  <span className="text-sm text-slate-700">{t.applicableScenes.enterpriseSize}</span>
                </div>
                <div className="flex gap-3">
                  <span className="text-xs text-slate-400 w-14 shrink-0 pt-0.5">使用场景</span>
                  <span className="text-sm text-slate-700">{t.applicableScenes.useScenes}</span>
                </div>
                <div className="flex gap-3">
                  <span className="text-xs text-slate-400 w-14 shrink-0 pt-0.5">团队配置</span>
                  <span className="text-sm text-slate-700">{t.applicableScenes.teamConfig}</span>
                </div>
              </div>
            </section>
          )}

          {/* 你需要准备什么 */}
          <section>
            <h3 className="text-sm font-semibold text-slate-700 mb-3">你需要准备什么</h3>
            <div className="space-y-2">
              {t.requiredSystems.map(item => (
                <div key={item} className="flex items-start gap-2 text-sm text-slate-700">
                  <span className="mt-0.5 px-1.5 py-0.5 rounded text-[10px] font-semibold bg-red-100 text-red-600 shrink-0 leading-4">必需</span>
                  {item}
                </div>
              ))}
              {t.optionalSystems?.map(item => (
                <div key={item} className="flex items-start gap-2 text-sm text-slate-500">
                  <span className="mt-0.5 px-1.5 py-0.5 rounded text-[10px] font-semibold bg-slate-100 text-slate-400 shrink-0 leading-4">可选</span>
                  {item}
                </div>
              ))}
            </div>
          </section>
        </div>

        {/* Bottom CTA */}
        <div className="pt-8 border-t border-slate-100">
          <div className="flex items-center justify-between">
            <div>
              <h3 className="font-semibold text-slate-900 mb-1">准备好雇佣 {t.name} 了吗？</h3>
              <p className="text-sm text-slate-500">{t.estimatedTimeToFirstValue}即可看到价值</p>
            </div>
            <button
              onClick={() => navigate(`/hiring/${t.id}`)}
              className="px-6 py-3 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors"
            >
              开始雇佣
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
