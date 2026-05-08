import { useEffect, useMemo, useState } from 'react'
import { ArrowRight, BarChart2, CheckCircle2, CopyPlus, Loader2, Search, Sparkles, Users } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { useUserRole } from '@/app/context/UserRoleContext'
import { api, type EmployeeSummary } from '@/infra/api'
import { firstCharacter, statusClass, statusLabel, withEmployeeView } from './employeeView'

type StageTab = 'hired' | 'intern' | 'live'
type InternSubTab = 'ai' | 'human'

export default function DepartmentEmployeesPage() {
  const navigate = useNavigate()
  const { role } = useUserRole()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [employees, setEmployees] = useState<EmployeeSummary[]>([])
  const [tab, setTab] = useState<StageTab>('live')
  const [internSubTab, setInternSubTab] = useState<InternSubTab>('ai')
  const [query, setQuery] = useState('')

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
          setError(requestError instanceof Error ? requestError.message : '部门数字员工加载失败')
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

  const viewedEmployees = useMemo(() => {
    return employees
      .map(withEmployeeView)
      .filter((item) => item.ownership === 'department')
  }, [employees])

  const counts = useMemo(() => {
    return {
      hired: viewedEmployees.filter((item) => item.mappedStatus === 'hired' || item.mappedStatus === 'failed').length,
      ai: viewedEmployees.filter((item) => item.mappedStatus === 'interning_ai').length,
      human: viewedEmployees.filter((item) => item.mappedStatus === 'interning_human').length,
      intern: viewedEmployees.filter((item) => item.mappedStatus === 'interning_ai' || item.mappedStatus === 'interning_human').length,
      live: viewedEmployees.filter((item) => item.mappedStatus === 'live').length,
    }
  }, [viewedEmployees])

  const visibleEmployees = useMemo(() => {
    const activeTab = role === 'member' ? 'live' : tab

    const baseList = (() => {
      if (role === 'member') {
        return viewedEmployees.filter((item) => item.mappedStatus === 'live')
      }

      if (activeTab === 'live') {
        return viewedEmployees.filter((item) => item.mappedStatus === 'live')
      }

      if (activeTab === 'hired') {
        return viewedEmployees.filter((item) => item.mappedStatus === 'hired' || item.mappedStatus === 'failed')
      }

      if (internSubTab === 'ai') {
        return viewedEmployees.filter((item) => item.mappedStatus === 'interning_ai')
      }

      return viewedEmployees.filter((item) => item.mappedStatus === 'interning_human')
    })()

    const keyword = query.trim().toLowerCase()
    if (!keyword) return baseList

    return baseList.filter((item) => {
      const haystack = [
        item.nickname,
        item.roleName,
        item.sourceTemplate,
        item.primarySignal,
        item.stageSummary,
      ]
        .join(' ')
        .toLowerCase()
      return haystack.includes(keyword)
    })
  }, [internSubTab, query, role, tab, viewedEmployees])

  function openClone(employeeId: string) {
    navigate(`/clone/${employeeId}`)
  }

  function openDetail(employeeId: string) {
    navigate(`/instances/${employeeId}`)
  }

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">{role === 'manager' ? '团队资产总览' : '部门可复制员工'}</span>
          <h1 className="hb-page-title">部门数字员工 · <span className="accent">研发部</span></h1>
          <p className="hb-page-copy">
            {role === 'manager'
              ? '部门长视角下统一管理已雇佣、评估中、已上岗三个阶段。所有卡片先进入详情，再从详情页分发下一步动作。'
              : '普通成员只看到本部门已上岗结果集。进入详情后可以继续复制为自己的分身。'}
          </p>
        </div>
        {role === 'manager' ? (
          <div className="hb-page-actions">
            <button type="button" className="hb-btn-primary" onClick={() => navigate('/template-pool')}>
              从模板池雇佣
            </button>
          </div>
        ) : null}
      </div>

      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label"><Users size={14} /> 部门员工</div>
          <div className="hb-stat-value">{viewedEmployees.length}</div>
          <div className="hb-stat-note">可按状态筛选</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label"><CheckCircle2 size={14} /> 已上岗</div>
          <div className="hb-stat-value">{counts.live}</div>
          <div className="hb-stat-note">可直接复制</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label"><Sparkles size={14} /> 评估中</div>
          <div className="hb-stat-value">{counts.intern}</div>
          <div className="hb-stat-note">AI / 人工评估</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label"><BarChart2 size={14} /> 雇佣中</div>
          <div className="hb-stat-value">{counts.hired}</div>
          <div className="hb-stat-note">等待进入评估</div>
        </div>
      </div>

      <div className="hb-search-shell mt-5">
        <Search size={16} />
        <input
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          className="hb-search-input"
          placeholder="搜索员工名称、能力标签、所属场景"
        />
        <div className="hb-search-controls">
          <button type="button" className="hb-btn-primary" onClick={() => setQuery('')}>
            清空筛选
          </button>
        </div>
      </div>

      <div className="mt-5">
        <div className="hb-tab-row">
          {(role === 'manager'
            ? [
                { id: 'hired' as const, label: '已雇佣', count: counts.hired },
                { id: 'intern' as const, label: '待实习', count: counts.intern },
                { id: 'live' as const, label: '已上岗', count: counts.live },
              ]
            : [{ id: 'live' as const, label: '已上岗', count: counts.live }]
          ).map((item) => (
            <button
              key={item.id}
              type="button"
              className={`hb-tab ${tab === item.id ? 'is-active' : ''}`}
              onClick={() => setTab(item.id)}
            >
              {item.label}
              <span className="ml-2 text-xs text-[#737373]">{item.count}</span>
            </button>
          ))}
        </div>
      </div>

      {role === 'manager' && tab === 'intern' ? (
        <div className="mt-5 hb-chip-row">
          <button
            type="button"
            className={`hb-chip ${internSubTab === 'ai' ? 'is-active' : ''}`}
            onClick={() => setInternSubTab('ai')}
          >
            AI 评估
            <span>{counts.ai}</span>
          </button>
          <button
            type="button"
            className={`hb-chip ${internSubTab === 'human' ? 'is-active' : ''}`}
            onClick={() => setInternSubTab('human')}
          >
            人工评估
            <span>{counts.human}</span>
          </button>
        </div>
      ) : null}

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <span>{error}</span>
        </div>
      ) : null}

      <div className="mt-5">
        {loading ? (
          <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
            <Loader2 size={16} className="animate-spin" />
            正在加载部门数字员工...
          </div>
        ) : visibleEmployees.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">当前没有符合筛选条件的数字员工</div>
            <div className="hb-empty-copy">
              {role === 'manager'
                ? '去模板池开始一轮新雇佣，或切换到其他状态查看不同阶段的员工。'
                : '等部门长完成上岗后，这里就会出现可以直接使用和复制的员工。'}
            </div>
          </div>
        ) : (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {visibleEmployees.map((employee) => {
              const canClone = employee.ownership === 'department' && employee.mappedStatus === 'live'
              return (
                <article
                  key={employee.employeeId}
                  className="hb-card p-5 transition-transform duration-150 hover:-translate-y-0.5"
                >
                  <button
                    type="button"
                    onClick={() => openDetail(employee.employeeId)}
                    className="block w-full text-left"
                  >
                    <div className="mb-3 flex items-start gap-3">
                      <span className="hb-squircle h-11 w-11 bg-[#dde9ff] text-[#3d5cff]">
                        {firstCharacter(employee.nickname)}
                      </span>
                      <div className="min-w-0 flex-1">
                        <div className="flex items-center justify-between gap-2">
                          <h3 className="truncate text-[15px] font-semibold text-[#0a0a0a]">{employee.nickname}</h3>
                          <span className={`hb-pill ${statusClass(employee.mappedStatus, employee.lifecycleStatus)}`}>
                            {statusLabel(employee.mappedStatus, employee.lifecycleStatus)}
                          </span>
                        </div>
                        <p className="mt-1 truncate text-xs text-[#737373]">{employee.roleName || employee.sourceTemplate}</p>
                      </div>
                    </div>
                    <p className="line-clamp-2 min-h-10 text-sm leading-relaxed text-[#404040]">
                      {employee.primarySignal || employee.stageSummary}
                    </p>
                  </button>
                  <div className="mt-4 border-t border-[#f5f5f5] pt-3">
                    <div className="flex items-center justify-between gap-2 text-xs text-[#737373]">
                      <span>创建于 {employee.createdAt}</span>
                      {canClone ? <span className="text-[#15803d]">可复制</span> : null}
                    </div>
                    <div className="mt-3 flex flex-wrap items-center gap-2">
                      {canClone ? (
                        <button
                          type="button"
                          className="hb-btn-primary"
                          onClick={() => openClone(employee.employeeId)}
                        >
                          <CopyPlus size={14} />
                          {role === 'member' ? '创建分身' : '复制分身'}
                        </button>
                      ) : null}
                      <button
                        type="button"
                        className={canClone ? 'hb-btn-ghost' : 'hb-btn-primary'}
                        onClick={() => openDetail(employee.employeeId)}
                      >
                        查看详情
                        <ArrowRight size={14} />
                      </button>
                    </div>
                  </div>
                </article>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
