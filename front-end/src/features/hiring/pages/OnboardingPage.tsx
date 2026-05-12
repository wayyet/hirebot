import { useEffect, useRef, useState } from 'react'
import { Loader2 } from 'lucide-react'
import { useNavigate, useParams } from 'react-router-dom'
import { useUxOverlay } from '@/app/context/UxOverlayContext'
import { api, type EmployeeDetail } from '@/infra/api'
import { firstCharacter } from './employeeView'
import { Breadcrumb } from '@/shared/components/Breadcrumb'

type OnboardPhase = 'form' | 'progress' | 'done'

type OnboardTask = {
  id: string
  label: string
  done: boolean
}

const DEFAULT_TASKS: OnboardTask[] = [
  { id: 'register_bot', label: 'bot 注册', done: false },
  { id: 'write_profile', label: '身份写入', done: false },
  { id: 'launch_runtime', label: '沙箱启动', done: false },
  { id: 'set_live', label: '状态切为 live', done: false },
]

export default function OnboardingPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { openLarkGuide, showToast } = useUxOverlay()

  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null)
  const [phase, setPhase] = useState<OnboardPhase>('form')
  const [tasks, setTasks] = useState<OnboardTask[]>(DEFAULT_TASKS)
  const [displayName, setDisplayName] = useState('')
  const [displayDescription, setDisplayDescription] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const intervalRef = useRef<number | null>(null)

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
          setDisplayName(detail.nickname)
          setDisplayDescription(detail.stageSummary || detail.primarySignal || '')
        }
      })
      .catch((requestError: unknown) => {
        if (!cancelled) {
          setEmployee(null)
          setError(requestError instanceof Error ? requestError.message : '加载上岗页面失败')
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

  useEffect(() => {
    return () => {
      if (intervalRef.current !== null) {
        window.clearInterval(intervalRef.current)
      }
    }
  }, [])

  async function finalizeOnboarding() {
    if (!id) return

    setSubmitting(true)
    setError('')

    try {
      await api.employeeRuntime.updateLifecycle(id, {
        status: 'live',
        stageSummary: `已完成飞书身份配置并上岗：${displayName}`,
        primarySignal: '运行稳定',
        signalLevel: 'ok',
      })
      setPhase('done')
      showToast(`「${displayName}」上岗成功`, 'success')
    } catch (requestError: unknown) {
      setError(requestError instanceof Error ? requestError.message : '上岗提交失败')
      setPhase('form')
      setTasks(DEFAULT_TASKS)
    } finally {
      setSubmitting(false)
    }
  }

  function startOnboarding() {
    if (!displayName.trim()) {
      setError('display_name 必填')
      return
    }

    if (submitting) {
      return
    }

    setError('')
    setPhase('progress')
    setTasks(DEFAULT_TASKS)

    let index = 0
    intervalRef.current = window.setInterval(() => {
      setTasks((previous) => previous.map((item, taskIndex) => (taskIndex === index ? { ...item, done: true } : item)))
      index += 1

      if (index >= DEFAULT_TASKS.length) {
        if (intervalRef.current !== null) {
          window.clearInterval(intervalRef.current)
          intervalRef.current = null
        }
        void finalizeOnboarding()
      }
    }, 650)
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载上岗配置...
        </div>
      </div>
    )
  }

  if (!employee) {
    return (
      <div className="hb-page space-y-4">
        <Breadcrumb items={[{ label: '部门数字员工', to: '/department-employees' }, { label: '上岗配置' }]} />
        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          {error || '未找到实例数据'}
        </div>
      </div>
    )
  }

  return (
    <div className="hb-page space-y-5">
<Breadcrumb items={[{ label: '实例详情', to: `/instances/${employee.employeeId}` }, { label: '上岗配置' }]} />

      <div className="hb-card p-6">
        <h1 className="text-[28px] font-semibold leading-tight text-[#0a0a0a]">飞书身份配置与上岗</h1>
        <p className="mt-2 text-sm text-[#737373]">
          修改对外身份不会触发重新评估。完成注册后，数字员工会进入「已上岗」状态。
        </p>
      </div>

      <div className="grid gap-5 lg:grid-cols-2">
        <div className="hb-card p-6">
          <h2 className="text-base font-semibold text-[#0a0a0a]">飞书预览</h2>
          <div className="mt-4 rounded-2xl border border-[#ececec] bg-[#fafafa] p-4">
            <div className="flex items-center gap-3 rounded-xl border border-[#f3f4f6] bg-white p-3">
              <span className="hb-squircle h-12 w-12 bg-[#dde9ff] text-[#3d5cff]">
                {firstCharacter(displayName)}
              </span>
              <div className="min-w-0">
                <div className="truncate text-sm font-semibold text-[#0a0a0a]">{displayName || '未填写名称'}</div>
                <div className="mt-1 line-clamp-2 text-xs text-[#737373]">
                  {displayDescription || '请填写对外介绍'}
                </div>
              </div>
            </div>
            <p className="mt-3 text-xs text-[#737373]">
              预览将在飞书机器人卡片中展示，建议使用业务可识别的名称和描述。
            </p>
          </div>
        </div>

        <div className="hb-card p-6">
          {phase === 'form' && (
            <>
              <h2 className="text-base font-semibold text-[#0a0a0a]">对外身份</h2>
              <div className="mt-4 space-y-4">
                <label className="block">
                  <div className="mb-1 text-sm font-medium text-[#404040]">display_name</div>
                  <input
                    value={displayName}
                    onChange={(event) => setDisplayName(event.target.value)}
                    className="w-full rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
                  />
                </label>

                <label className="block">
                  <div className="mb-1 text-sm font-medium text-[#404040]">display_description</div>
                  <textarea
                    rows={4}
                    value={displayDescription}
                    onChange={(event) => setDisplayDescription(event.target.value)}
                    className="w-full resize-none rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
                  />
                </label>

                {error && (
                  <div className="rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-3 py-2 text-xs text-[#b3263c]">
                    {error}
                  </div>
                )}

                <div className="flex justify-end gap-2">
                  <button type="button" className="hb-btn-ghost" onClick={() => navigate(`/instances/${employee.employeeId}`)}>
                    取消
                  </button>
                  <button type="button" className="hb-btn-primary" onClick={startOnboarding} disabled={submitting}>
                    注册并上岗 →
                  </button>
                </div>
              </div>
            </>
          )}

          {phase === 'progress' && (
            <>
              <h2 className="text-base font-semibold text-[#0a0a0a]">上岗中...</h2>
              <div className="mt-4 space-y-3">
                {tasks.map((task) => (
                  <div key={task.id} className="flex items-center justify-between rounded-xl border border-[#f3f4f6] px-3 py-2">
                    <span className="text-sm text-[#404040]">{task.label}</span>
                    <span className={`hb-pill ${task.done ? 'green' : 'gray'}`}>
                      {task.done ? '已完成' : '处理中'}
                    </span>
                  </div>
                ))}
              </div>
              {error && (
                <div className="mt-4 rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-3 py-2 text-xs text-[#b3263c]">
                  {error}
                </div>
              )}
            </>
          )}

          {phase === 'done' && (
            <>
              <h2 className="text-base font-semibold text-[#0a0a0a]">上岗成功</h2>
              <div className="mt-4 rounded-2xl border border-[#d1fae5] bg-[#ecfdf5] px-4 py-3 text-sm text-[#15803d]">
                {displayName} 已完成飞书注册，现在可以进入一对一会话使用。
              </div>
              <div className="mt-4 flex justify-end gap-2">
                <button type="button" className="hb-btn-ghost" onClick={() => navigate(`/instances/${employee.employeeId}`)}>
                  查看详情
                </button>
                <button
                  type="button"
                  className="hb-btn-primary"
                  onClick={() => openLarkGuide({
                    name: displayName,
                    description: displayDescription,
                    dept: employee.owningTeam,
                    initial: firstCharacter(displayName),
                  })}
                >
                  去飞书使用
                </button>
                <button type="button" className="hb-btn-ghost" onClick={() => navigate('/department-employees')}>
                  返回员工列表
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  )
}
