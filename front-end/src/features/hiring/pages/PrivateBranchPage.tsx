import { useEffect, useMemo, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, type EmployeeDetail } from '@/infra/api'
import { firstCharacter } from './employeeView'
import { Breadcrumb } from '@/shared/components/Breadcrumb'

type StationKey = 'persona' | 'knowledge' | 'ability' | 'external'

const STATIONS: Array<{ key: StationKey; title: string; description: string }> = [
  { key: 'persona', title: '人设', description: '调整角色定位、语气与不可越界事项' },
  { key: 'knowledge', title: '知识', description: '追加或替换私有资料' },
  { key: 'ability', title: '能力', description: '新增能力或收紧边界' },
  { key: 'external', title: '外部对接', description: '替换外部系统连接信息' },
]

export default function PrivateBranchPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [goal, setGoal] = useState('我希望它在 offer 谈判时自动套用 P7 薪资 band，并避免主动提到福利细节。')
  const [stations, setStations] = useState<Record<StationKey, boolean>>({
    persona: false,
    knowledge: true,
    ability: true,
    external: false,
  })
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    if (!id) {
      setError('实例 ID 缺失')
      setLoading(false)
      return
    }

    let cancelled = false
    setLoading(true)
    setError('')

    api.employeeRuntime.getEmployee(id)
      .then((detail) => {
        if (!cancelled) {
          setEmployee(detail)
        }
      })
      .catch((requestError: unknown) => {
        if (!cancelled) {
          setEmployee(null)
          setError(requestError instanceof Error ? requestError.message : '加载私人定制页面失败')
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

  const pickedCount = useMemo(() => {
    return Object.values(stations).filter(Boolean).length
  }, [stations])

  function toggleStation(key: StationKey) {
    setStations((previous) => ({ ...previous, [key]: !previous[key] }))
  }

  async function createBranch() {
    if (!employee || submitting) return

    setSubmitting(true)
    setError('')
    try {
      const selected = (Object.entries(stations) as Array<[StationKey, boolean]>)
        .filter(([, v]) => v)
        .map(([k]) => k)
      const branch = await api.employeeRuntime.createPrivateBranch(employee.employeeId, {
        displayName: `${employee.nickname} · 私有分支`,
        displayDescription: goal.trim() || '基于当前分身创建的私有分支',
        selectedStations: selected,
      })
      // After creation, go to evaluation page (status is "hired")
      navigate(`/instances/${branch.branchId}/evaluation`)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '创建私有分支失败')
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载私人定制...
        </div>
      </div>
    )
  }

  if (!employee) {
    return (
      <div className="hb-page space-y-4">
        <Breadcrumb items={[{ label: '我的数字员工', to: '/my-employees' }, { label: '私有分支' }]} />
        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          {error || '未找到实例数据'}
        </div>
      </div>
    )
  }

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '我的数字员工', to: '/my-employees' }, { label: `私人定制 · ${employee.nickname}` }]} />

      <div className="hb-hero">
        <div className="hb-hero-grid">
          <div className="hb-toolbar">
            <div>
              <div className="hb-hero-eyebrow">私有分支向导</div>
              <h1 className="hb-hero-title">私人定制 · 基于 {employee.nickname}</h1>
              <p className="hb-hero-copy">
                在不破坏原分身的前提下完成个性化定制。失败可放弃，不影响原分身。
              </p>
            </div>
          </div>
          <div className="hb-hero-metrics">
            <div className="hb-metric-card">
              <div className="hb-metric-label">原分身</div>
              <div className="hb-metric-value">{employee.nickname}</div>
            </div>
            <div className="hb-metric-card">
              <div className="hb-metric-label">已选工位</div>
              <div className="hb-metric-value">{pickedCount}</div>
            </div>
            <div className="hb-metric-card">
              <div className="hb-metric-label">状态</div>
              <div className="hb-metric-value">{employee.lifecycleStatus}</div>
            </div>
          </div>
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">原分身信息</h2>
        <div className="mt-4 flex items-center gap-3 rounded-2xl border border-[#f3f4f6] bg-[#fafafa] p-4">
          <span className="hb-squircle h-12 w-12 bg-[#ece7fb] text-[#6a5acd]">
            {firstCharacter(employee.nickname)}
          </span>
          <div className="min-w-0 flex-1">
            <div className="truncate text-sm font-semibold text-[#0a0a0a]">{employee.nickname}</div>
            <div className="mt-1 text-xs text-[#737373]">
              最近更新 {employee.createdAt} · 任务完成 {employee.tasksDone}/{employee.tasksTotal}
            </div>
          </div>
          <span className="hb-pill blue">{employee.lifecycleStatus}</span>
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">差异目标</h2>
        <p className="mt-1 text-sm text-[#737373]">先说清楚你想改变什么，再选择要调整的工位。</p>
        <textarea
          rows={4}
          value={goal}
          onChange={(event) => setGoal(event.target.value)}
          className="mt-4 w-full resize-none rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
        />
      </div>

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">选择要调整的工位</h2>
        <p className="mt-1 text-sm text-[#737373]">未选工位继续沿用原分身，减少不必要的改动成本。</p>
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          {STATIONS.map((station) => (
            <button
              key={station.key}
              type="button"
              onClick={() => toggleStation(station.key)}
              className={`rounded-2xl border p-4 text-left transition-colors ${
                stations[station.key]
                  ? 'border-[#0a0a0a] bg-white'
                  : 'border-[#ececec] bg-[#fafafa] hover:border-[#d4d4d8]'
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="text-sm font-semibold text-[#0a0a0a]">{station.title}</span>
                {stations[station.key] && <span className="hb-pill green">已选</span>}
              </div>
              <p className="mt-2 text-xs text-[#737373]">{station.description}</p>
            </button>
          ))}
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="text-base font-semibold text-[#0a0a0a]">继承说明</h2>
        <div className="mt-3 rounded-xl border border-[#d9e1ff] bg-[#eef2ff] px-4 py-3 text-sm text-[#2e3da9]">
          只对你勾选的 {pickedCount} 个工位做精简追问，其余继续沿用原分身。私有分支不能再创建二级分支。
          创建后需通过 AI 评估 + 用户自评，上岗后将替换原分身的 IM 路由，不新增飞书联系人。
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <button
          type="button"
          className="rounded-full border border-[#fde2e2] bg-white px-4 py-2 text-sm font-medium text-[#be3a4a] hover:bg-[#fff5f5]"
          onClick={() => navigate('/my-employees')}
        >
          放弃定制
        </button>
        <button
          type="button"
          className="hb-btn-primary"
          disabled={pickedCount === 0 || !goal.trim() || submitting}
          onClick={() => void createBranch()}
        >
          生成私有分支 →
        </button>
      </div>
    </div>
  )
}
