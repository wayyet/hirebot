import { Loader2 } from 'lucide-react'

interface WorkspaceProgressSummary {
  percent: number
  label: string
  completed: number
  total: number
}

interface EvalSandboxInitOverlayProps {
  resetting: boolean
  employeeName: string
  progressSummary: WorkspaceProgressSummary | null
}

export function EvalSandboxInitOverlay({ resetting, employeeName, progressSummary }: EvalSandboxInitOverlayProps) {
  return (
    <div className="flex h-[calc(100vh-116px)] min-h-[680px] items-center justify-center">
      <div className="w-full max-w-[400px] rounded-3xl border eval-chat-wrapper p-8 text-center shadow-xl">
        <div className="mb-5 flex justify-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--hb-blue)]/10">
            <Loader2 size={26} className="animate-spin text-[var(--hb-blue)]" />
          </div>
        </div>
        <h2 className="mb-1 text-[17px] font-semibold eval-text-title">
          {resetting ? '正在清理评估数据' : '正在初始化评估环境'}
        </h2>
        <p className="mb-6 text-[13px] leading-relaxed eval-text-secondary">
          正在为 <strong>{employeeName}</strong>
          {resetting ? ' 清理旧的评估数据，请稍候...' : ' 准备双沙箱环境，请稍候...'}
        </p>
        {!resetting && progressSummary && (
          <div className="mb-4">
            <div className="mb-2 flex items-center justify-between text-[12px]">
              <span className="truncate eval-text-secondary">{progressSummary.label}</span>
              <span className="ml-2 shrink-0 font-medium eval-text-title">
                {progressSummary.completed}/{progressSummary.total}
              </span>
            </div>
            <div className="h-1.5 w-full rounded-full eval-progress-track">
              <div
                className="h-1.5 rounded-full transition-all duration-500 eval-progress-bar-ok"
                style={{ width: `${progressSummary.percent}%` }}
              />
            </div>
          </div>
        )}
        <p className="text-[12px] eval-text-caption">初始化完成后页面将自动就绪</p>
      </div>
    </div>
  )
}
