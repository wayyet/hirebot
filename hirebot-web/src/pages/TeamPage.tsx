import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Plus, AlertCircle, Clock, CheckCircle2, Users, TrendingUp, ArrowUpRight, MessageSquare, Check, ChevronDown, ChevronUp } from 'lucide-react'
import {
  digitalEmployees as mockEmployees,
  type LifecycleStatus,
} from '../mock/data'
import { loadUserEmployees } from '../utils/storage'

const statusConfig: Record<LifecycleStatus, { badge: 'green' | 'yellow' | 'red' | 'blue' | 'gray' }> = {
  '待启动': { badge: 'yellow' },
  '实习中': { badge: 'blue' },
  '已转正': { badge: 'green' },
  '离职中': { badge: 'red' },
  '已归档': { badge: 'gray' },
}

const statusGroups: LifecycleStatus[] = ['待启动', '实习中', '已转正', '离职中', '已归档']

const signalIcon = (level: 'ok' | 'warn' | 'error') => {
  if (level === 'ok') return <CheckCircle2 size={14} className="text-emerald-500" />
  if (level === 'warn') return <AlertCircle size={14} className="text-amber-500" />
  return <AlertCircle size={14} className="text-red-500" />
}

interface IMInfoItem {
  id: string
  employeeId: string
  employeeName: string
  category: string
  content: string
  source: string
  receivedAt: string
}

const MOCK_IM_ITEMS: IMInfoItem[] = [
  {
    id: 'im001',
    employeeId: 'e001',
    employeeName: '小追',
    category: '客户意向',
    content: '客户张总（北京科技）在群里表示对 Q3 方案有强烈意向，希望本周内安排演示',
    source: '销售群 · 李明',
    receivedAt: '10分钟前',
  },
  {
    id: 'im002',
    employeeId: 'e002',
    employeeName: '小审',
    category: '合同收到',
    content: '供应商发来采购合同（v3 版），已上传至飞书云文档，等待法务初审',
    source: '法务群 · 王芳',
    receivedAt: '32分钟前',
  },
  {
    id: 'im003',
    employeeId: 'e003',
    employeeName: '小数',
    category: '数据异常',
    content: '昨日日报中 GMV 数据与 BI 系统存在 12% 偏差，需人工核对口径后重新生成',
    source: '运营群 · 陈晓',
    receivedAt: '1小时前',
  },
  {
    id: 'im004',
    employeeId: 'e001',
    employeeName: '小追',
    category: '跟进提醒',
    content: '商机「华东零售-扩编项目」已超过 7 天未更新，销售赵磊确认仍在推进中',
    source: '销售群 · 赵磊',
    receivedAt: '2小时前',
  },
  {
    id: 'im005',
    employeeId: 'e004',
    employeeName: '小招',
    category: '候选人确认',
    content: '候选人刘雨（前端 P6）已接受初试邀约，面试时间待 HR 确认后回复',
    source: '招聘群 · 人事部',
    receivedAt: '3小时前',
  },
]

const CATEGORY_COLORS: Record<string, string> = {
  '客户意向': 'bg-emerald-50 text-emerald-700',
  '合同收到': 'bg-blue-50 text-blue-700',
  '数据异常': 'bg-red-50 text-red-600',
  '跟进提醒': 'bg-amber-50 text-amber-700',
  '候选人确认': 'bg-violet-50 text-violet-700',
}

export default function TeamPage() {
  const navigate = useNavigate()
  const [confirmedIds, setConfirmedIds] = useState<Set<string>>(new Set())
  const [checklistExpanded, setChecklistExpanded] = useState(true)

  const userEmployees = loadUserEmployees()
  const digitalEmployees = [...mockEmployees, ...userEmployees]

  const total = digitalEmployees.length
  const internCount = digitalEmployees.filter(e => e.lifecycleStatus === '实习中').length
  const activeCount = digitalEmployees.filter(e => e.lifecycleStatus === '已转正').length
  const attentionCount = digitalEmployees.filter(e => e.pendingActions.length > 0).length
  const escalationCount = digitalEmployees.filter(e => e.pendingActions.some(a => a.includes('转人工'))).length

  const pendingItems = MOCK_IM_ITEMS.filter(item => !confirmedIds.has(item.id))
  const confirmedItems = MOCK_IM_ITEMS.filter(item => confirmedIds.has(item.id))

  function confirm(id: string) {
    setConfirmedIds(prev => new Set([...prev, id]))
  }

  return (
    <div className="min-h-screen bg-white">
      {/* Header */}
      <div className="border-b border-slate-100">
        <div className="max-w-7xl mx-auto px-8 py-6">
          <div className="flex items-center justify-between">
            <div>
              <h1 className="text-2xl font-semibold text-slate-900 mb-2">我的团队</h1>
              <p className="text-sm text-slate-500">管理已雇佣的数字员工 · {total} 个成员</p>
            </div>
            <button
              onClick={() => navigate('/market')}
              className="px-5 py-3 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors flex items-center gap-2"
            >
              <Plus size={16} />
              雇佣新员工
            </button>
          </div>
        </div>
      </div>

      <div className="max-w-7xl mx-auto px-8 py-8">
        {/* Summary cards */}
        <div className="grid grid-cols-5 gap-4 mb-8">
          {[
            { label: '全部员工', value: total, icon: Users },
            { label: '实习中', value: internCount, icon: Clock },
            { label: '已转正', value: activeCount, icon: TrendingUp },
            { label: '需关注', value: attentionCount, icon: AlertCircle },
            { label: '转人工', value: escalationCount, icon: ArrowUpRight },
          ].map(({ label, value, icon: Icon }) => (
            <div key={label} className="bg-slate-50 rounded-lg p-4">
              <div className="flex items-center gap-2 mb-2">
                <Icon size={16} className="text-slate-400" />
                <span className="text-xs text-slate-500">{label}</span>
              </div>
              <div className="text-2xl font-semibold text-slate-900">{value}</div>
            </div>
          ))}
        </div>

        {/* IM 待确认信息清单 */}
        <div className="mb-8 border border-slate-200 rounded-xl overflow-hidden">
          {/* 清单头部 */}
          <button
            className="w-full flex items-center justify-between px-5 py-4 bg-slate-50 hover:bg-slate-100 transition-colors"
            onClick={() => setChecklistExpanded(v => !v)}
          >
            <div className="flex items-center gap-2.5">
              <MessageSquare size={16} className="text-slate-500" />
              <span className="text-sm font-semibold text-slate-700">IM 待确认信息</span>
              {pendingItems.length > 0 && (
                <span className="px-2 py-0.5 rounded-full bg-amber-100 text-amber-700 text-xs font-semibold">
                  {pendingItems.length} 条待确认
                </span>
              )}
              {pendingItems.length === 0 && (
                <span className="px-2 py-0.5 rounded-full bg-emerald-100 text-emerald-700 text-xs font-semibold">
                  全部已确认
                </span>
              )}
            </div>
            <div className="flex items-center gap-2 text-xs text-slate-400">
              <span>共 {MOCK_IM_ITEMS.length} 条 · {confirmedItems.length} 已确认</span>
              {checklistExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
            </div>
          </button>

          {checklistExpanded && (
            <div className="divide-y divide-slate-100">
              {/* 待确认 */}
              {pendingItems.map(item => (
                <div key={item.id} className="flex items-start gap-4 px-5 py-4 hover:bg-slate-50 transition-colors">
                  {/* 未确认 checkbox */}
                  <button
                    onClick={() => confirm(item.id)}
                    className="mt-0.5 w-5 h-5 rounded border-2 border-slate-300 hover:border-slate-500 flex items-center justify-center shrink-0 transition-colors"
                  />

                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <span className={`px-1.5 py-0.5 rounded text-[10px] font-semibold ${CATEGORY_COLORS[item.category] ?? 'bg-slate-100 text-slate-600'}`}>
                        {item.category}
                      </span>
                      <span className="text-xs text-slate-400">{item.employeeName}</span>
                      <span className="text-xs text-slate-300">·</span>
                      <span className="text-xs text-slate-400">{item.source}</span>
                    </div>
                    <p className="text-sm text-slate-700 leading-relaxed">{item.content}</p>
                  </div>

                  <div className="flex items-center gap-3 shrink-0">
                    <span className="text-xs text-slate-400 whitespace-nowrap">{item.receivedAt}</span>
                    <button
                      onClick={() => confirm(item.id)}
                      className="px-3 py-1.5 rounded-lg bg-slate-900 text-white text-xs font-medium hover:bg-slate-700 transition-colors whitespace-nowrap"
                    >
                      确认
                    </button>
                  </div>
                </div>
              ))}

              {/* 已确认 */}
              {confirmedItems.map(item => (
                <div key={item.id} className="flex items-start gap-4 px-5 py-4 opacity-40">
                  <div className="mt-0.5 w-5 h-5 rounded bg-emerald-500 flex items-center justify-center shrink-0">
                    <Check size={11} className="text-white" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <span className="text-xs text-slate-400">{item.category}</span>
                      <span className="text-xs text-slate-300">·</span>
                      <span className="text-xs text-slate-400">{item.employeeName}</span>
                    </div>
                    <p className="text-sm text-slate-500 line-through leading-relaxed">{item.content}</p>
                  </div>
                  <span className="text-xs text-emerald-600 shrink-0 mt-0.5">已确认</span>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* 员工列表 */}
        <div className="space-y-8">
          {statusGroups.map(status => {
            const group = digitalEmployees.filter(e => e.lifecycleStatus === status)
            if (!group.length) return null
            return (
              <section key={status}>
                <div className="flex items-center gap-3 mb-4">
                  <h2 className="text-lg font-semibold text-slate-900">{status}</h2>
                  <span className="text-sm text-slate-400">{group.length} 人</span>
                </div>
                <div className="grid grid-cols-3 gap-4">
                  {group.map(e => (
                    <div
                      key={e.id}
                      className="bg-white border border-slate-100 rounded-lg p-5 hover:border-slate-300 hover:shadow-sm transition-all cursor-pointer"
                      onClick={() => navigate(`/instances/${e.id}`)}
                    >
                      <h3 className="font-semibold text-slate-900 mb-1">{e.nickname}</h3>
                      <p className="text-sm text-slate-500 mb-3">{e.roleName}</p>
                      <div className="flex items-center gap-2 mb-3">
                        {signalIcon(e.signalLevel)}
                        <span className="text-xs text-slate-600">{e.primarySignal}</span>
                      </div>
                      <div className="pt-3 border-t border-slate-50 flex items-center justify-between text-xs text-slate-500">
                        <span>任务 {e.tasksDone}/{e.tasksTotal}</span>
                        {e.satisfactionScore && (
                          <span className="flex items-center gap-1">
                            ⭐ {e.satisfactionScore}
                          </span>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              </section>
            )
          })}
        </div>
      </div>
    </div>
  )
}
