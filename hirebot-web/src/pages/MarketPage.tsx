import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Search, Sparkles, TrendingUp, Users, Zap, Star, ChevronRight, BookOpen, FlaskConical, Trash2 } from 'lucide-react'
import { templates } from '../mock/data'
import { loadUserTemplates, saveUserTemplate } from '../utils/storage'
import type { UserTemplate } from '../utils/storage'

// ── Mock 数据：模拟一个由「智能合同审核助手」定制完成的企业模板 ──
const MOCK_CUSTOM_EMPLOYEE = {
  name: '智能合同审核助手',
  description: '法务团队在合同审核时需要逐条比对条款、识别风险点，耗时 2-3 小时/份，效率低且容易遗漏',
  isTemplate: true,
  discoveryData: {
    scenario: '法务合同审核与风险识别',
    beneficiary: '法务专员 / 业务合同负责人',
    realCase: {
      when: '收到乙方发来的采购合同后',
      who: '法务专员小王',
      steps: '打开合同 → 逐条阅读 → 标注风险条款 → 查内部规则库 → 撰写审核意见',
      pain: '每份合同 2-3 小时，语言表述千变万化，容易遗漏隐性条款',
      consequence: '一份漏审合同导致违约金纠纷，损失 80 万',
    },
    employee: {
      name: '智能合同审核助手',
      roleMetaphor: '法务老鸟',
      mission: '快速识别合同中的风险条款，输出结构化审核意见，将审核时间从 2-3 小时压缩到 15 分钟',
      keyJudgment: '判断条款是否偏离企业标准合同模板超出可接受边界',
      autonomy: '可自主完成初审，超出权限的争议条款自动升级给法务负责人',
      deliverable: '结构化审核报告（风险等级 + 具体条款 + 修改建议）',
      criticalFailure: '将高风险条款标记为低风险，或遗漏关键违约责任条款',
    },
    ontology: {
      entities: ['合同文本', '条款', '风险点', '标准模板', '审核意见'],
      actions: ['解析', '比对', '标注', '生成报告', '升级'],
      resources: ['企业标准合同库', '法律法规知识库', '历史审核案例'],
      constraints: ['不替代法务负责人做最终决策', '不处理诉讼类文件', '不直接对外发送审核意见'],
    },
    skills: [
      { name: '合同条款解析', purpose: '提取并结构化合同所有条款', trigger: '收到合同文件', input: 'PDF/Word 合同', output: '条款列表', autonomy: '全自动' },
      { name: '风险条款识别', purpose: '比对标准模板识别偏差条款', trigger: '条款解析完成', input: '条款列表', output: '风险标注列表', autonomy: '全自动' },
      { name: '审核报告生成', purpose: '输出结构化审核意见', trigger: '风险识别完成', input: '风险标注列表', output: 'Markdown 审核报告', autonomy: '全自动' },
      { name: '高风险升级通知', purpose: '超出权限时通知法务负责人', trigger: '存在高风险条款', input: '风险等级判断', output: 'IM 通知', autonomy: '规则触发' },
    ],
    clis: [
      { skill: '合同文件读取', system: '企业文件存储（飞书云文档）', action: '读取', interface: 'API', auth: 'OAuth' },
      { skill: '标准模板查询', system: '法务知识库', action: '查询', interface: 'RAG', auth: '内部 Token' },
    ],
  },
}

const MOCK_DIRECT_TEMPLATE: UserTemplate = {
  id: `tmpl_mock_${Date.now()}`,
  name: '智能合同审核助手',
  oneLiner: '快速识别合同风险条款，将审核时间从 2-3 小时压缩到 15 分钟',
  shortValueSummary: '输出结构化审核报告（风险等级 + 具体条款 + 修改建议）',
  primaryIndustry: '通用',
  primaryFunction: '法务',
  coreCapabilities: ['合同条款解析', '风险条款识别', '审核报告生成', '高风险升级通知'],
  readinessHint: '需接入企业文件存储和法务知识库',
  trustSignal: '企业自建',
  onboardingRequirementLevel: '中',
  estimatedTimeToFirstValue: '2 周',
  topBenefits: ['审核时间减少 80%', '风险遗漏率下降', '审核结论可追溯'],
  providerName: '法务团队',
  versionLabel: 'v1.0',
  hotScore: 0,
  graduationRate: 0,
  inScopeItems: ['合同条款解析', '风险条款识别', '审核报告生成', '高风险升级通知'],
  outOfScopeItems: ['不替代法务负责人做最终决策', '不处理诉讼类文件', '不直接对外发送审核意见'],
  requiredSystems: ['企业文件存储（飞书云文档）', '法务知识库'],
  successCases: ['3 个月内完成 400+ 份合同审核，平均耗时从 2.5h 降至 12 分钟，零遗漏高风险条款'],
  relatedTemplates: [],
  isUserCreated: true,
  publishedAt: new Date().toISOString(),
  creatorTeam: '法务团队',
}

const quickEntries = [
  { label: '销售提效', icon: TrendingUp },
  { label: '客服自动化', icon: Zap },
  { label: '数据报表', icon: BookOpen },
  { label: '招聘提速', icon: Users },
]

export default function MarketPage() {
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [tick, setTick] = useState(0)
  // tick 变化时强制重新从 localStorage 读取
  const userTemplates = tick >= 0 ? loadUserTemplates() : []
  const allTemplates = [...userTemplates, ...templates]

  const filtered = allTemplates.filter(t => {
    return !search || t.name.includes(search) || t.oneLiner.includes(search)
  })

  // ── 调试工具 ──
  function injectMockTemplate() {
    const tpl = { ...MOCK_DIRECT_TEMPLATE, id: `tmpl_mock_${Date.now()}` }
    saveUserTemplate(tpl)
    setTick(n => n + 1)
  }

  function clearUserTemplates() {
    localStorage.removeItem('ncrew_user_templates')
    setTick(n => n + 1)
  }

  return (
    <div className="min-h-screen bg-white">
      {/* Minimal Header */}
      <div className="border-b border-slate-100">
        <div className="max-w-7xl mx-auto px-8 py-6">
          <h1 className="text-2xl font-semibold text-slate-900 mb-2">数字员工市场</h1>
          <p className="text-sm text-slate-500">发现和雇佣适合你团队的数字员工 · 找到 {allTemplates.length} 个角色</p>
        </div>
      </div>

      <div className="max-w-7xl mx-auto px-8 py-8">
        {/* Search and Actions */}
        <div className="flex items-center gap-4 mb-8">
          <div className="flex-1 relative">
            <Search size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-slate-400" />
            <input
              className="w-full pl-12 pr-4 py-3 bg-slate-50 border border-slate-200 rounded-lg text-sm outline-none focus:border-slate-300 focus:bg-white transition-colors placeholder:text-slate-400"
              placeholder="搜索数字员工..."
              value={search}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          <button
            onClick={() => navigate('/custom-employee')}
            className="px-5 py-3 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors flex items-center gap-2 whitespace-nowrap"
          >
            <Sparkles size={16} />
            定制数字员工
          </button>
        </div>

        {/* Template Grid - Minimal Cards */}
        <div className="grid grid-cols-3 gap-6">
          {filtered.map(t => {
            const isUserCreated = (t as any).isUserCreated === true
            return (
              <div
                key={t.id}
                className="group bg-white border border-slate-100 rounded-lg p-6 hover:shadow-sm hover:border-slate-300 transition-all cursor-pointer relative"
                onClick={() => navigate(`/templates/${t.id}`)}
              >
                {/* 企业自建角标 */}
                {isUserCreated && (
                  <span className="absolute top-3 left-3 px-1.5 py-0.5 bg-violet-50 text-violet-600 border border-violet-100 rounded text-[10px] font-medium">
                    企业自建
                  </span>
                )}

                {/* Title */}
                <h3 className="font-semibold text-slate-900 mb-2 group-hover:text-slate-700 transition-colors">
                  {t.name}
                </h3>

                {/* Description */}
                <p className="text-sm text-slate-500 mb-4 line-clamp-2 leading-relaxed">
                  {t.oneLiner}
                </p>

                {/* Capabilities */}
                <div className="flex gap-1.5 flex-wrap mb-4">
                  {t.coreCapabilities.slice(0, 3).map(cap => (
                    <span key={cap} className="px-2 py-1 bg-slate-50 rounded text-xs text-slate-600">
                      {cap}
                    </span>
                  ))}
                </div>

                {/* Meta */}
                <div className="flex items-center justify-between pt-4 border-t border-slate-50">
                  <div className="flex items-center gap-3 text-xs text-slate-400">
                    <span className="flex items-center gap-1">
                      <Star size={12} className="text-amber-400 fill-amber-400" />
                      {t.graduationRate}%
                    </span>
                    <span>{t.trustSignal}</span>
                  </div>
                  <ChevronRight size={16} className="text-slate-300 group-hover:text-slate-400 group-hover:translate-x-0.5 transition-all" />
                </div>
              </div>
            )
          })}
        </div>

        {/* Pagination */}
        {filtered.length > 12 && (
          <div className="flex items-center justify-center gap-2 mt-12 pt-8 border-t border-slate-100">
            <button className="px-3 py-1.5 text-sm text-slate-400 hover:text-slate-600 transition-colors">
              上一页
            </button>
            <button className="px-3 py-1.5 text-sm bg-slate-900 text-white rounded">1</button>
            <button className="px-3 py-1.5 text-sm text-slate-600 hover:text-slate-900 transition-colors">2</button>
            <button className="px-3 py-1.5 text-sm text-slate-600 hover:text-slate-900 transition-colors">3</button>
            <button className="px-3 py-1.5 text-sm text-slate-400 hover:text-slate-600 transition-colors">
              下一页
            </button>
          </div>
        )}
      </div>

      {/* ── DEV 调试面板 ── */}
      <div className="fixed bottom-6 right-6 z-50 flex flex-col items-end gap-2">
        <div className="bg-white border border-slate-200 rounded-xl shadow-lg p-3 flex flex-col gap-2 text-xs w-52">
          <div className="flex items-center gap-1.5 text-slate-400 font-medium pb-1 border-b border-slate-100">
            <FlaskConical size={12} />
            DEV 测试工具
            <span className="ml-auto text-slate-300">企业自建模板 {userTemplates.length} 个</span>
          </div>
          <button
            onClick={() => navigate('/hiring', { state: { customEmployee: MOCK_CUSTOM_EMPLOYEE } })}
            className="w-full text-left px-2.5 py-2 rounded-lg bg-violet-50 text-violet-700 hover:bg-violet-100 transition-colors leading-snug"
          >
            <div className="font-medium">① 模拟完整定制流程</div>
            <div className="text-violet-400 mt-0.5">跳转 HiringPage，走完 5 步向导</div>
          </button>
          <button
            onClick={injectMockTemplate}
            className="w-full text-left px-2.5 py-2 rounded-lg bg-emerald-50 text-emerald-700 hover:bg-emerald-100 transition-colors leading-snug"
          >
            <div className="font-medium">② 直接注入测试模板</div>
            <div className="text-emerald-500 mt-0.5">跳过向导，立刻在市场看到结果</div>
          </button>
          {userTemplates.length > 0 && (
            <button
              onClick={clearUserTemplates}
              className="w-full flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-500 transition-colors"
            >
              <Trash2 size={11} />
              清除全部企业自建模板（{userTemplates.length}）
            </button>
          )}
        </div>
      </div>

    </div>
  )
}
