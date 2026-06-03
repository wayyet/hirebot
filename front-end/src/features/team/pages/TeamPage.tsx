import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  Plus,
  AlertCircle,
  Clock,
  CheckCircle2,
  Users,
  TrendingUp,
  ArrowUpRight,
  MessageSquare,
  Check,
  ChevronDown,
  ChevronUp,
  ClipboardCheck,
  GitBranch,
  UserCheck,
  Milestone,
  Loader2,
  Database,
} from 'lucide-react'
import { api, type EmployeeDetail, type EmployeeSummary, type TeamImItem } from '@/infra/api'

type LifecycleStatus = '待启动' | '待AI评估' | '待人工评估' | '实习中' | '已转正' | '离职中' | '已归档'
type SignalLevel = 'ok' | 'warn' | 'error'

interface TeamEmployee extends EmployeeDetail {
  id: string
}

const TIMELINE_ORDER: LifecycleStatus[] = ['待启动', '待AI评估', '待人工评估', '实习中', '已转正', '离职中']

const MEMBER_NODES = [
  { label: '模板', color: 'bg-blue-300', activeColor: 'bg-blue-400', ringColor: 'ring-blue-200' },
  { label: '被雇佣', color: 'bg-blue-400', activeColor: 'bg-blue-500', ringColor: 'ring-blue-200' },
  { label: '待AI评估', color: 'bg-blue-500', activeColor: 'bg-blue-600', ringColor: 'ring-blue-300' },
  { label: '待人工评估', color: 'bg-blue-500', activeColor: 'bg-blue-700', ringColor: 'ring-blue-300' },
  { label: '实习', color: 'bg-emerald-400', activeColor: 'bg-emerald-500', ringColor: 'ring-emerald-200' },
  { label: '转正', color: 'bg-emerald-500', activeColor: 'bg-emerald-600', ringColor: 'ring-emerald-200' },
  { label: '离职', color: 'bg-slate-400', activeColor: 'bg-slate-500', ringColor: 'ring-slate-200' },
] as const

const ONBOARD_STEP = 4

const MEMBER_GROUP_ORDER: { status: LifecycleStatus; label: string; dot: string; badge: string }[] = [
  { status: '已转正', label: '已转正', dot: 'bg-emerald-500', badge: 'bg-emerald-50 text-emerald-700' },
  { status: '实习中', label: '实习中', dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-600' },
  { status: '待人工评估', label: '待人工评估', dot: 'bg-blue-500', badge: 'bg-blue-50 text-blue-700' },
  { status: '待AI评估', label: '待AI评估', dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-600' },
  { status: '待启动', label: '模板', dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
  { status: '离职中', label: '离职中', dot: 'bg-slate-500', badge: 'bg-slate-100 text-slate-700' },
]

const STATUS_TO_STEP: Record<LifecycleStatus, number> = {
  待启动: 1,
  待AI评估: 2,
  待人工评估: 3,
  实习中: 4,
  已转正: 5,
  离职中: 6,
  已归档: 6,
}

const CATEGORY_COLORS: Record<string, string> = {
  客户意向: 'bg-emerald-50 text-emerald-700',
  合同收到: 'bg-blue-50 text-blue-700',
  数据异常: 'bg-red-50 text-red-600',
  跟进提醒: 'bg-amber-50 text-amber-700',
  候选人确认: 'bg-violet-50 text-violet-700',
  待办处理: 'bg-slate-100 text-slate-600',
}

function toSignalLevel(level: string | undefined): SignalLevel {
  if (level === 'warn') return 'warn'
  if (level === 'error') return 'error'
  return 'ok'
}

function signalIcon(level: string | undefined) {
  const normalized = toSignalLevel(level)
  if (normalized === 'ok') return <CheckCircle2 size={14} className="text-emerald-500" />
  if (normalized === 'warn') return <AlertCircle size={14} className="text-amber-500" />
  return <AlertCircle size={14} className="text-red-500" />
}

function toTeamEmployee(detail: EmployeeDetail): TeamEmployee {
  return {
    ...detail,
    id: detail.employeeId,
    pendingActions: detail.pendingActions ?? [],
    capabilities: detail.capabilities ?? [],
  }
}

function toDetailFallback(summary: EmployeeSummary): EmployeeDetail {
  return {
    ...summary,
    capabilities: [],
    internshipStartAt: null,
    graduatedAt: null,
    satisfactionScore: null,
    evalPhase: null,
    evalIteration: null,
    evalMaxIterations: null,
  }
}

function routeForEmployee(employee: TeamEmployee): string {
  if (employee.lifecycleStatus === '待人工评估') {
    return `/department-employees/instances/${employee.id}/human-evaluation`
  }

  if (employee.lifecycleStatus === '待AI评估') {
    return `/department-employees/instances/${employee.id}/evaluation`
  }

  return `/department-employees/instances/${employee.id}`
}

function buildRequestId(): string {
  if (globalThis.crypto?.randomUUID) {
    return globalThis.crypto.randomUUID()
  }

  return `im_${Date.now()}_${Math.random().toString(36).slice(2, 10)}`
}

export default function TeamPage() {
  const navigate = useNavigate()

  const [viewMode, setViewMode] = useState<'timeline' | 'member'>('timeline')
  const [imExpanded, setImExpanded] = useState(true)
  const [employees, setEmployees] = useState<TeamEmployee[]>([])
  const [imItems, setImItems] = useState<TeamImItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [pendingActionId, setPendingActionId] = useState<string | null>(null)
  const [pendingImId, setPendingImId] = useState<string | null>(null)
  const [fixtureImporting, setFixtureImporting] = useState(false)

  const digitalEmployees = useMemo(() => employees, [employees])

  const total = digitalEmployees.length
  const evalPendingCount = digitalEmployees.filter((employee) => employee.lifecycleStatus === '待AI评估').length
  const humanEvalPendingCount = digitalEmployees.filter((employee) => employee.lifecycleStatus === '待人工评估').length
  const internCount = digitalEmployees.filter((employee) => employee.lifecycleStatus === '实习中').length
  const activeCount = digitalEmployees.filter((employee) => employee.lifecycleStatus === '已转正').length
  const attentionCount = digitalEmployees.filter((employee) => employee.pendingActions.length > 0).length
  const escalationCount = digitalEmployees.filter((employee) =>
    employee.pendingActions.some((action) => action.includes('转人工')),
  ).length

  const pendingItems = imItems.filter((item) => item.status === 'pending')
  const confirmedItems = imItems.filter((item) => item.status === 'confirmed')

  useEffect(() => {
    void loadPageData()
  }, [])

  async function loadPageData() {
    setLoading(true)
    setError('')

    try {
      const summaries = await api.employeeRuntime.getEmployees()
      const details = await Promise.all(
        summaries.map(async (summary) => {
          try {
            return await api.employeeRuntime.getEmployee(summary.employeeId)
          } catch {
            return toDetailFallback(summary)
          }
        }),
      )

      const teamImItems = await api.teamIm.getItems({ status: 'all', page: 1, pageSize: 100 })
      setEmployees(details.map(toTeamEmployee))
      setImItems(teamImItems)
    } catch (err) {
      setError(err instanceof Error ? err.message : '团队数据加载失败')
      setEmployees([])
      setImItems([])
    } finally {
      setLoading(false)
    }
  }

  async function handleStartEvaluation(id: string) {
    setPendingActionId(id)
    setError('')

    try {
      await api.employeeRuntime.updateLifecycle(id, {
        lifecycleStatus: '待AI评估',
        stageSummary: '已发起评估，等待 AI 评估执行',
        primarySignal: '待执行 AI 评估',
        signalLevel: 'warn',
      })
      await loadPageData()
      navigate(`/department-employees/instances/${id}/evaluation`)
    } catch (err) {
      setError(err instanceof Error ? err.message : '发起评估失败')
    } finally {
      setPendingActionId(null)
    }
  }

  async function handlePromote(id: string) {
    setPendingActionId(id)
    setError('')

    try {
      await api.employeeRuntime.updateLifecycle(id, {
        lifecycleStatus: '已转正',
        stageSummary: '已转正，正式进入工作状态',
        primarySignal: '运行正常',
        signalLevel: 'ok',
        graduatedAt: new Date().toISOString().split('T')[0],
      })
      await loadPageData()
    } catch (err) {
      setError(err instanceof Error ? err.message : '转正失败')
    } finally {
      setPendingActionId(null)
    }
  }

  async function handleResign(id: string) {
    setPendingActionId(id)
    setError('')

    try {
      await api.employeeRuntime.updateLifecycle(id, {
        lifecycleStatus: '离职中',
        stageSummary: '正在办理离职手续',
        primarySignal: '离职流程处理中',
        signalLevel: 'warn',
      })
      await loadPageData()
    } catch (err) {
      setError(err instanceof Error ? err.message : '离职操作失败')
    } finally {
      setPendingActionId(null)
    }
  }

  async function confirmImItem(itemId: string) {
    const rollbackItems = imItems
    setPendingImId(itemId)
    setError('')

    setImItems((previous) =>
      previous.map((item) =>
        item.itemId === itemId
          ? {
              ...item,
              status: 'confirmed',
              confirmedAt: item.confirmedAt ?? new Date().toISOString(),
            }
          : item,
      ),
    )

    try {
      const updated = await api.teamIm.confirmItem(itemId, { requestId: buildRequestId() })
      setImItems((previous) => previous.map((item) => (item.itemId === itemId ? updated : item)))
    } catch (err) {
      setImItems(rollbackItems)
      setError(err instanceof Error ? err.message : '确认 IM 信息失败')
    } finally {
      setPendingImId(null)
    }
  }

  async function handleImportFixtures() {
    setFixtureImporting(true)
    setError('')
    setNotice('')

    try {
      const result = await api.employeeRuntime.importFixtureInstances()
      await loadPageData()
      setNotice(`已导入示例实例：${result.importedEmployees} 个员工，${result.importedImItems} 条 IM 信息`)
    } catch (err) {
      setError(err instanceof Error ? err.message : '导入示例实例失败')
    } finally {
      setFixtureImporting(false)
    }
  }

  return (
    <div className="min-h-screen bg-white">
      <div className="border-b border-slate-100">
        <div className="max-w-7xl mx-auto px-4 md:px-8 py-6">
          <div className="flex items-center justify-between gap-4">
            <div>
              <h1 className="text-2xl font-semibold text-slate-900 mb-2">团队</h1>
              <p className="text-sm text-slate-500">管理雇佣中的数字员工 · {total} 个成员</p>
            </div>
            <div className="flex items-center gap-3">
              <button
                onClick={() => void handleImportFixtures()}
                disabled={fixtureImporting}
                className="px-4 py-2.5 border border-slate-300 text-slate-700 rounded-lg text-sm font-medium hover:bg-slate-50 transition-colors flex items-center gap-2 disabled:opacity-50"
              >
                {fixtureImporting ? <Loader2 size={16} className="animate-spin" /> : <Database size={16} />}
                生成示例实例（测试）
              </button>
              <button
                onClick={() => navigate('/market')}
                className="px-4 py-2.5 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors flex items-center gap-2"
              >
                <Plus size={16} /> 雇佣新员工
              </button>
            </div>
          </div>
        </div>
      </div>

      <div className="max-w-7xl mx-auto px-4 md:px-8 py-8 space-y-8">
        {error && (
          <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 flex items-center gap-2">
            <AlertCircle size={14} />
            {error}
          </div>
        )}

        {notice && (
          <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-sm text-emerald-700 flex items-center gap-2">
            <CheckCircle2 size={14} />
            {notice}
          </div>
        )}

        {loading && digitalEmployees.length === 0 && (
          <div className="rounded-xl border border-slate-200 bg-white p-10 flex items-center justify-center text-slate-500 gap-2">
            <Loader2 size={16} className="animate-spin" />
            加载中...
          </div>
        )}

        {!loading && digitalEmployees.length === 0 && (
          <div className="rounded-xl border border-slate-200 bg-white p-8 text-center space-y-3">
            <p className="text-slate-600">当前暂无团队成员</p>
            <button
              onClick={() => navigate('/market')}
              className="px-4 py-2 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors"
            >
              去雇佣员工
            </button>
          </div>
        )}

        {!loading && digitalEmployees.length > 0 && (
          <>
            <div className="grid grid-cols-2 md:grid-cols-4 xl:grid-cols-7 gap-4">
              {[
                { label: '全部员工', value: total, icon: Users },
                { label: '待AI评估', value: evalPendingCount, icon: ClipboardCheck },
                { label: '待人工评估', value: humanEvalPendingCount, icon: UserCheck },
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

            <div className="border border-slate-200 rounded-xl overflow-hidden">
              <button
                className="w-full flex items-center justify-between px-5 py-4 bg-slate-50 hover:bg-slate-100 transition-colors"
                onClick={() => setImExpanded((value) => !value)}
              >
                <div className="flex items-center gap-2.5">
                  <MessageSquare size={16} className="text-slate-500" />
                  <span className="text-sm font-semibold text-slate-700">IM 待确认信息</span>
                  {pendingItems.length > 0 ? (
                    <span className="px-2 py-0.5 rounded-full bg-amber-100 text-amber-700 text-xs font-semibold">
                      {pendingItems.length} 条待确认
                    </span>
                  ) : (
                    <span className="px-2 py-0.5 rounded-full bg-emerald-100 text-emerald-700 text-xs font-semibold">
                      全部已确认
                    </span>
                  )}
                </div>
                <div className="flex items-center gap-2 text-xs text-slate-400">
                  <span>共 {imItems.length} 条 · {confirmedItems.length} 已确认</span>
                  {imExpanded ? <ChevronUp size={14} /> : <ChevronDown size={14} />}
                </div>
              </button>

              {imExpanded && (
                <div className="divide-y divide-slate-100">
                  {pendingItems.map((item) => (
                    <div key={item.itemId} className="flex items-start gap-4 px-5 py-4 hover:bg-slate-50 transition-colors">
                      <button
                        onClick={() => void confirmImItem(item.itemId)}
                        disabled={pendingImId === item.itemId}
                        className="mt-0.5 w-5 h-5 rounded border-2 border-slate-300 hover:border-slate-500 flex items-center justify-center shrink-0 transition-colors disabled:opacity-50"
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
                          onClick={() => void confirmImItem(item.itemId)}
                          disabled={pendingImId === item.itemId}
                          className="px-3 py-1.5 rounded-lg bg-slate-900 text-white text-xs font-medium hover:bg-slate-700 transition-colors whitespace-nowrap disabled:opacity-50"
                        >
                          确认
                        </button>
                      </div>
                    </div>
                  ))}
                  {confirmedItems.map((item) => (
                    <div key={item.itemId} className="flex items-start gap-4 px-5 py-4 opacity-40">
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

            <div className="flex items-center justify-between">
              <h2 className="text-base font-semibold text-slate-900">成员信息</h2>
              <div className="flex items-center bg-slate-100 rounded-lg p-1">
                <button
                  onClick={() => setViewMode('timeline')}
                  className={`px-3 py-1.5 rounded-md text-sm font-medium flex items-center gap-1.5 transition-colors ${
                    viewMode === 'timeline' ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'
                  }`}
                >
                  <GitBranch size={14} />
                  时间轴
                </button>
                <button
                  onClick={() => setViewMode('member')}
                  className={`px-3 py-1.5 rounded-md text-sm font-medium flex items-center gap-1.5 transition-colors ${
                    viewMode === 'member' ? 'bg-white text-slate-900 shadow-sm' : 'text-slate-500 hover:text-slate-700'
                  }`}
                >
                  <Milestone size={14} />
                  成员轴
                </button>
              </div>
            </div>

            {viewMode === 'timeline' && (
              <div className="pt-4 pb-8 overflow-x-auto">
                <div className="relative min-w-[820px]">
                  <div className="absolute top-16 left-0 right-0 h-0.5 bg-slate-200" />
                  <div className="flex justify-between gap-8">
                    {TIMELINE_ORDER.map((status) => {
                      const group = digitalEmployees.filter((employee) => employee.lifecycleStatus === status)
                      if (!group.length) return null

                      const statusColors: Record<LifecycleStatus, string> = {
                        待启动: 'border-slate-500 bg-slate-50',
                        待AI评估: 'border-amber-500 bg-amber-50',
                        待人工评估: 'border-indigo-500 bg-indigo-50',
                        实习中: 'border-blue-500 bg-blue-50',
                        已转正: 'border-emerald-500 bg-emerald-50',
                        离职中: 'border-slate-400 bg-slate-100',
                        已归档: 'border-slate-500 bg-slate-200',
                      }

                      return (
                        <div key={status} className="flex flex-col items-center relative">
                          <div className={`w-4 h-4 rounded-full border-2 ${statusColors[status]} z-10`} />
                          <div className="mt-3 mb-6 px-3 py-1.5 bg-slate-900 text-white text-xs font-medium rounded-full whitespace-nowrap">
                            {status} · {group.length}人
                          </div>

                          <div className="space-y-3 w-44">
                            {group.map((employee) => (
                              <div
                                key={employee.id}
                                onClick={() => navigate(routeForEmployee(employee))}
                                className="bg-white border border-slate-200 rounded-lg p-3 shadow-sm hover:shadow-md hover:border-slate-300 transition-all cursor-pointer"
                              >
                                <div className="flex items-center justify-between mb-1.5 gap-2">
                                  <span className="font-semibold text-sm text-slate-900 truncate">{employee.nickname}</span>
                                  <span className="text-[10px] text-slate-400 shrink-0">{employee.roleName}</span>
                                </div>
                                <div className="text-[10px] text-slate-500 truncate mb-2">{employee.primarySignal}</div>

                                <div className="flex items-center gap-1" onClick={(event) => event.stopPropagation()}>
                                  {employee.lifecycleStatus === '待启动' && (
                                    <button
                                      disabled={pendingActionId === employee.id}
                                      onClick={() => void handleStartEvaluation(employee.id)}
                                      className="flex-1 px-2 py-1 bg-slate-700 text-white rounded text-[10px] font-medium hover:bg-slate-800 transition-colors disabled:opacity-50"
                                    >
                                      发起评估
                                    </button>
                                  )}
                                  {employee.lifecycleStatus === '待AI评估' && (
                                    <button
                                      onClick={() => navigate(`/department-employees/instances/${employee.id}/evaluation`)}
                                      className="flex-1 px-2 py-1 bg-violet-600 text-white rounded text-[10px] font-medium hover:bg-violet-700 transition-colors"
                                    >
                                      进入AI评估
                                    </button>
                                  )}
                                  {employee.lifecycleStatus === '待人工评估' && (
                                    <button
                                      onClick={() => navigate(`/department-employees/instances/${employee.id}/human-evaluation`)}
                                      className="flex-1 px-2 py-1 bg-indigo-600 text-white rounded text-[10px] font-medium hover:bg-indigo-700 transition-colors"
                                    >
                                      执行人工评估
                                    </button>
                                  )}
                                  {employee.lifecycleStatus === '实习中' && (
                                    <button
                                      disabled={pendingActionId === employee.id}
                                      onClick={() => void handlePromote(employee.id)}
                                      className="flex-1 px-2 py-1 bg-emerald-600 text-white rounded text-[10px] font-medium hover:bg-emerald-700 transition-colors disabled:opacity-50"
                                    >
                                      转正
                                    </button>
                                  )}
                                  {employee.lifecycleStatus === '已转正' && (
                                    <button
                                      disabled={pendingActionId === employee.id}
                                      onClick={() => void handleResign(employee.id)}
                                      className="flex-1 px-2 py-1 bg-rose-600 text-white rounded text-[10px] font-medium hover:bg-rose-700 transition-colors disabled:opacity-50"
                                    >
                                      离职
                                    </button>
                                  )}
                                  {employee.lifecycleStatus === '离职中' && (
                                    <span className="flex-1 px-2 py-1 bg-slate-100 text-slate-500 rounded text-[10px] font-medium text-center">
                                      离职中
                                    </span>
                                  )}
                                  {employee.lifecycleStatus === '已归档' && (
                                    <span className="flex-1 px-2 py-1 bg-slate-200 text-slate-600 rounded text-[10px] font-medium text-center">
                                      已归档
                                    </span>
                                  )}
                                </div>
                              </div>
                            ))}
                          </div>
                        </div>
                      )
                    })}
                  </div>
                </div>
              </div>
            )}

            {viewMode === 'member' && (
              <div className="space-y-3">
                {MEMBER_GROUP_ORDER.map(({ status, label, dot, badge }) => {
                  const group = digitalEmployees.filter((employee) => employee.lifecycleStatus === status)
                  if (!group.length) return null

                  const totalNodes = MEMBER_NODES.length

                  return (
                    <div key={status} className="bg-white border border-slate-100 rounded-xl overflow-hidden">
                      <div className="flex items-center gap-2.5 px-5 py-3 border-b border-slate-100">
                        <div className={`w-2 h-2 rounded-full shrink-0 ${dot}`} />
                        <span className="text-sm font-semibold text-slate-800">{label}</span>
                        <span className={`text-[10px] px-2 py-0.5 rounded-full font-medium ${badge}`}>
                          {group.length} 人
                        </span>
                      </div>

                      <div className="overflow-x-auto">
                        <div className="min-w-[880px]">
                          <div className="flex items-center px-5 py-2 border-b border-slate-50 bg-slate-50/60">
                            <div className="w-44 shrink-0 text-[11px] font-medium text-slate-300">成员</div>
                            <div className="flex-1 flex">
                              {MEMBER_NODES.map((node, idx) => (
                                <div key={idx} className="flex-1 text-center text-[11px] font-medium text-slate-300">
                                  {node.label}
                                </div>
                              ))}
                            </div>
                          </div>

                          <div className="divide-y divide-slate-50">
                            {group.map((employee) => {
                              const lifecycleStatus =
                                employee.lifecycleStatus in STATUS_TO_STEP
                                  ? (employee.lifecycleStatus as LifecycleStatus)
                                  : '待启动'
                              const activeStep = STATUS_TO_STEP[lifecycleStatus] ?? 1
                              return (
                                <div
                                  key={employee.id}
                                  className="flex items-center px-5 py-3.5 hover:bg-slate-50 transition-colors cursor-pointer"
                                  onClick={() => navigate(routeForEmployee(employee))}
                                >
                                  <div className="w-44 shrink-0">
                                    <div className="flex items-center gap-1.5">
                                      <span className="text-sm font-semibold text-slate-900">{employee.nickname}</span>
                                      {signalIcon(employee.signalLevel)}
                                    </div>
                                    <div className="text-xs text-slate-400 mt-0.5 truncate">{employee.roleName}</div>
                                  </div>

                                  <div className="flex-1 relative flex items-center">
                                    <div className="absolute inset-x-0 top-1/2 -translate-y-1/2 h-0.5 bg-slate-200" />
                                    {activeStep > 0 && (
                                      <div
                                        className="absolute left-0 top-1/2 -translate-y-1/2 h-0.5 bg-blue-400 transition-all"
                                        style={{ width: `${(Math.min(activeStep, ONBOARD_STEP) / (totalNodes - 1)) * 100}%` }}
                                      />
                                    )}
                                    {activeStep > ONBOARD_STEP && (
                                      <div
                                        className="absolute top-1/2 -translate-y-1/2 h-0.5 bg-emerald-400 transition-all"
                                        style={{
                                          left: `${(ONBOARD_STEP / (totalNodes - 1)) * 100}%`,
                                          width: `${((Math.min(activeStep, totalNodes - 2) - ONBOARD_STEP) / (totalNodes - 1)) * 100}%`,
                                        }}
                                      />
                                    )}
                                    <div className="relative flex w-full">
                                      {MEMBER_NODES.map((node, idx) => {
                                        const isCompleted = idx < activeStep
                                        const isActive = idx === activeStep
                                        return (
                                          <div key={idx} className="flex-1 flex justify-center">
                                            <div
                                              className={`w-5 h-5 rounded-full z-10 flex items-center justify-center transition-all ${
                                                isCompleted
                                                  ? `${node.color} shadow-sm`
                                                  : isActive
                                                    ? `${node.activeColor} ring-4 ${node.ringColor} shadow-md`
                                                    : 'bg-white border-2 border-slate-200'
                                              }`}
                                            >
                                              {isCompleted && (
                                                <svg className="w-2.5 h-2.5 text-white" viewBox="0 0 10 10" fill="none">
                                                  <path d="M2 5l2.5 2.5L8 3" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                                                </svg>
                                              )}
                                              {isActive && <div className="w-2 h-2 rounded-full bg-white" />}
                                            </div>
                                          </div>
                                        )
                                      })}
                                    </div>
                                  </div>
                                </div>
                              )
                            })}
                          </div>
                        </div>
                      </div>
                    </div>
                  )
                })}
              </div>
            )}
          </>
        )}
      </div>
    </div>
  )
}
