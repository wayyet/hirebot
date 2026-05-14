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
      // 私有分支是原地定制：后端返回的 branchId 仍是当前分身 id，创建后进入私有分支专用评估页。
      navigate(`/my-employees/instances/${branch.branchId}/evaluation`)
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '创建私有分支失败')
    } finally {
      setSubmitting(false)
    }
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card hb-private-branch-loading">
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
        <div className="hb-private-branch-error-card">
          {error || '未找到实例数据'}
        </div>
      </div>
    )
  }

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '我的数字员工', to: '/my-employees' }, { label: `私人定制 · ${employee.nickname}` }]} />

      <div className="hb-hero hb-private-branch-hero">
        <div className="hb-private-branch-hero-grid">
          <div className="hb-private-branch-hero-main">
            <div className="hb-hero-eyebrow">私有分支向导</div>
            <h1 className="hb-private-branch-hero-title">创建私有分支</h1>
            <p className="hb-private-branch-hero-copy">
              基于现有个人实例做定向定制，保留当前对话与运行上下文，只对你选中的工位继续调整。
            </p>
            <div className="hb-private-branch-source-card">
              <div className="hb-private-branch-source-label">来源实例</div>
              <div className="hb-private-branch-source-name" title={employee.nickname}>{employee.nickname}</div>
              <div className="hb-private-branch-source-meta">
                {employee.roleName || employee.sourceTemplate || '未命名角色'}
              </div>
            </div>
          </div>
          <div className="hb-private-branch-summary-card">
            <div className="hb-private-branch-summary-item">
              <span className="hb-private-branch-summary-label">已选工位</span>
              <strong className="hb-private-branch-summary-value">{pickedCount}</strong>
            </div>
            <div className="hb-private-branch-summary-item">
              <span className="hb-private-branch-summary-label">当前状态</span>
              <strong className="hb-private-branch-summary-value is-text" title={employee.lifecycleStatus}>{employee.lifecycleStatus}</strong>
            </div>
            <div className="hb-private-branch-summary-item">
              <span className="hb-private-branch-summary-label">最近更新</span>
              <strong className="hb-private-branch-summary-value is-text" title={employee.createdAt}>{employee.createdAt}</strong>
            </div>
          </div>
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="hb-private-branch-section-title">原分身信息</h2>
        <div className="hb-private-branch-origin-card mt-4">
          <div className="hb-private-branch-origin-head">
            <span className="hb-squircle hb-private-branch-origin-avatar hb-private-branch-origin-avatar-accent">
              {firstCharacter(employee.nickname)}
            </span>
            <div className="min-w-0 flex-1">
              <div className="hb-private-branch-card-title" title={employee.nickname}>{employee.nickname}</div>
              <div className="hb-private-branch-meta mt-1">
                {employee.roleName || employee.sourceTemplate || '未命名角色'}
              </div>
            </div>
            <span className="hb-pill blue">{employee.lifecycleStatus}</span>
          </div>
          <div className="hb-detail-meta-grid mt-4">
            <div className="hb-detail-meta-item">
              <span className="hb-detail-meta-label">最近更新</span>
              <strong title={employee.createdAt}>{employee.createdAt}</strong>
            </div>
            <div className="hb-detail-meta-item">
              <span className="hb-detail-meta-label">任务进度</span>
              <strong>{employee.tasksDone}/{employee.tasksTotal}</strong>
            </div>
            <div className="hb-detail-meta-item">
              <span className="hb-detail-meta-label">实例 ID</span>
              <strong title={employee.employeeId}>{employee.employeeId}</strong>
            </div>
            <div className="hb-detail-meta-item">
              <span className="hb-detail-meta-label">来源模板</span>
              <strong title={employee.sourceTemplate}>{employee.sourceTemplate || '未关联模板'}</strong>
            </div>
          </div>
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="hb-private-branch-section-title">差异目标</h2>
        <p className="hb-private-branch-copy mt-1">先说清楚你想改变什么，再选择要调整的工位。</p>
        <textarea
          rows={4}
          value={goal}
          onChange={(event) => setGoal(event.target.value)}
          className="hb-input hb-private-branch-textarea mt-4"
        />
      </div>

      <div className="hb-card p-6">
        <h2 className="hb-private-branch-section-title">选择要调整的工位</h2>
        <p className="hb-private-branch-copy mt-1">未选工位继续沿用原分身，减少不必要的改动成本。</p>
        <div className="mt-4 grid gap-3 md:grid-cols-2">
          {STATIONS.map((station) => (
            <button
              key={station.key}
              type="button"
              onClick={() => toggleStation(station.key)}
              className={`hb-private-branch-station-card rounded-2xl border p-4 text-left transition-colors ${
                stations[station.key]
                  ? 'is-selected'
                  : ''
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="hb-private-branch-card-title">{station.title}</span>
                {stations[station.key] && <span className="hb-pill green">已选</span>}
              </div>
              <p className="hb-private-branch-copy mt-2">{station.description}</p>
            </button>
          ))}
        </div>
      </div>

      <div className="hb-card p-6">
        <h2 className="hb-private-branch-section-title">继承说明</h2>
        <div className="hb-private-branch-note mt-3 rounded-xl px-4 py-3">
          只对你勾选的 {pickedCount} 个工位做精简追问，其余继续沿用原分身。私有分支不能再创建二级分支。
          创建后会原地升级当前分身，不新建实例、不新建沙箱、不改变 IM 路由；对话和 IM 配置都会继续沿用。
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-3">
        <button
          type="button"
          className="hb-private-branch-cancel-btn"
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
