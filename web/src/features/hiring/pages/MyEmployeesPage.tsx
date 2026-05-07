import { useEffect, useMemo, useState } from 'react'
import { Bot, Loader2, ShieldCheck, Sparkles, Users } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { api, type EmployeeSummary } from '@/infra/api'
import {
  firstCharacter,
  isEvaluating,
  ownershipClass,
  ownershipLabel,
  withEmployeeView,
} from './employeeView'

type FilterTab = 'all' | 'live' | 'evaluating' | 'branch' | 'failed'

export default function MyEmployeesPage() {
  const navigate = useNavigate()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [employees, setEmployees] = useState<EmployeeSummary[]>([])
  const [filter, setFilter] = useState<FilterTab>('all')

  useEffect(() => {
    let cancelled = false

    async function loadEmployees() {
      setLoading(true)
      setError('')

      try {
        const items = await api.employeeRuntime.getEmployees()
        if (!cancelled) {
          setEmployees(items)
        }
      } catch (requestError: unknown) {
        if (!cancelled) {
          setEmployees([])
          setError(requestError instanceof Error ? requestError.message : '我的数字员工加载失败')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void loadEmployees()

    return () => {
      cancelled = true
    }
  }, [])

  const viewedEmployees = useMemo(() => employees.map(withEmployeeView), [employees])

  const myEmployees = useMemo(() => {
    return viewedEmployees.filter((item) => item.ownership === 'personal_clone' || item.ownership === 'private_branch')
  }, [viewedEmployees])

  const counts = useMemo(() => {
    return {
      all: myEmployees.length,
      live: myEmployees.filter((item) => item.mappedStatus === 'live').length,
      evaluating: myEmployees.filter((item) => isEvaluating(item.mappedStatus)).length,
      branch: myEmployees.filter((item) => item.ownership === 'private_branch').length,
      failed: myEmployees.filter((item) => item.mappedStatus === 'failed').length,
    }
  }, [myEmployees])

  const visibleEmployees = useMemo(() => {
    if (filter === 'all') return myEmployees
    if (filter === 'live') return myEmployees.filter((item) => item.mappedStatus === 'live')
    if (filter === 'evaluating') return myEmployees.filter((item) => isEvaluating(item.mappedStatus))
    if (filter === 'branch') return myEmployees.filter((item) => item.ownership === 'private_branch')
    return myEmployees.filter((item) => item.mappedStatus === 'failed')
  }, [filter, myEmployees])

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">个人资产面板</span>
          <h1 className="hb-page-title">我的数字员工</h1>
          <p className="hb-page-copy">
            这里仅展示你本人拥有的「我的分身」和「私有分支」。已上岗实例可继续进入详情、飞书上岗和私有化扩展。
          </p>
        </div>
        <div className="hb-page-actions">
          <button type="button" className="hb-btn-ghost" onClick={() => navigate('/department-employees')}>
            去部门数字员工复制一个 →
          </button>
        </div>
      </div>

      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label"><Users size={14} /> 实例总数</div>
          <div className="hb-stat-value">{counts.all}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label"><Bot size={14} /> 已上岗</div>
          <div className="hb-stat-value">{counts.live}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label"><Sparkles size={14} /> 评估中</div>
          <div className="hb-stat-value">{counts.evaluating}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label"><ShieldCheck size={14} /> 私有分支</div>
          <div className="hb-stat-value">{counts.branch}</div>
          <div className="hb-stat-note">失败实例 {counts.failed}</div>
        </div>
      </div>

      <div className="mt-5 hb-chip-row">
        {[
          { id: 'all' as const, label: '全部', count: counts.all },
          { id: 'live' as const, label: '已上岗', count: counts.live },
          { id: 'evaluating' as const, label: '评估中', count: counts.evaluating },
          { id: 'branch' as const, label: '私有分支', count: counts.branch },
          { id: 'failed' as const, label: '待回退', count: counts.failed },
        ].map((item) => (
          <button
            key={item.id}
            type="button"
            className={`hb-chip ${filter === item.id ? 'is-active' : ''}`}
            onClick={() => setFilter(item.id)}
          >
            {item.label}
            <span>{item.count}</span>
          </button>
        ))}
      </div>

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <span>{error}</span>
        </div>
      ) : null}

      <div className="mt-5">
        {loading ? (
          <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
            <Loader2 size={16} className="animate-spin" />
            正在加载我的数字员工...
          </div>
        ) : visibleEmployees.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">当前筛选下还没有你的个人资产</div>
            <div className="hb-empty-copy">
              先去「部门数字员工」复制一个已上岗员工给自己，再回来这里继续对话、评估或定制。
            </div>
          </div>
        ) : (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {visibleEmployees.map((employee) => (
              <button
                key={employee.employeeId}
                type="button"
                onClick={() => navigate(`/instances/${employee.employeeId}`)}
                className="hb-card cursor-pointer p-5 text-left transition-transform duration-150 hover:-translate-y-0.5"
              >
                <div className="mb-3 flex items-start gap-3">
                  <span className="hb-squircle h-11 w-11 bg-[#ece7fb] text-[#6a5acd]">
                    {firstCharacter(employee.nickname)}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-center justify-between gap-2">
                      <h3 className="truncate text-[15px] font-semibold text-[#0a0a0a]">{employee.nickname}</h3>
                      <span className={`hb-pill ${ownershipClass(employee.ownership)}`}>{ownershipLabel(employee.ownership)}</span>
                    </div>
                    <p className="mt-1 truncate text-xs text-[#737373]">{employee.roleName || employee.sourceTemplate}</p>
                  </div>
                </div>
                <p className="line-clamp-2 min-h-10 text-sm leading-relaxed text-[#404040]">
                  {employee.primarySignal || employee.stageSummary}
                </p>
                <div className="mt-4 flex items-center justify-between border-t border-[#f5f5f5] pt-3 text-xs text-[#737373]">
                  <span>最近更新 {employee.createdAt}</span>
                  <span className="text-[#4a6cf7]">查看详情 →</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
