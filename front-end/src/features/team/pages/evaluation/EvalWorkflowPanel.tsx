import { Check, CheckCircle2, Loader2, AlertCircle, Trash2 } from 'lucide-react'
import type { WorkflowStage } from './evaluationTypes'
import {
  workflowStageTone,
  workflowStageTextTone,
  workflowStageStatusLabel,
  renderWorkflowStageMarker,
} from './evaluationUtils'

interface EvalWorkflowPanelProps {
  stages: WorkflowStage[]
  currentStageIndex: number
  resetConfirm: boolean
  resetting: boolean
  submitting: boolean
  wsEvaluating: boolean
  aiRunning: boolean
  primaryActionLabel: string
  onSetResetConfirm: (value: boolean) => void
  onReset: () => void
  onSubmitRun: () => void
}

export function EvalWorkflowPanel({
  stages,
  currentStageIndex,
  resetConfirm,
  resetting,
  submitting,
  wsEvaluating,
  aiRunning,
  primaryActionLabel,
  onSetResetConfirm,
  onReset,
  onSubmitRun,
}: EvalWorkflowPanelProps) {
  return (
    <section className="hb-card eval-flow-panel px-3 pb-2 pt-2">
      <div className="flex flex-col gap-2">
        <div className="flex flex-col gap-2 xl:flex-row xl:items-start xl:justify-between">
          <div className="min-w-0 flex-1 overflow-x-auto pb-1 [scrollbar-width:none] [&::-webkit-scrollbar]:hidden">
            <div className="flex min-w-[900px] pl-[36px]">
              {stages.map((stage, index) => {
                const tone = workflowStageTone(stage.status)
                const textTone = workflowStageTextTone(stage.status)
                const isCurrentStage = index === currentStageIndex
                const connectorTone = stage.status === 'completed'
                  ? 'eval-flow-step-line-completed'
                  : stage.status === 'running'
                    ? 'eval-flow-step-line-running'
                    : stage.status === 'failed'
                      ? 'eval-flow-step-line-failed'
                      : 'eval-flow-step-line-pending'

                return (
                  <div key={stage.key} className="flex min-w-0 flex-1">
                    <div className="min-w-0 flex-1">
                      <div className="mt-[2px] flex items-center">
                        <div className={`eval-flow-step-node ${tone} ${isCurrentStage ? 'eval-flow-step-node-current' : ''}`}>
                          {renderWorkflowStageMarker(stage.status, index + 1)}
                        </div>
                        {index < stages.length - 1 && (
                          <div className={`eval-flow-step-line ${connectorTone}`} />
                        )}
                      </div>
                      <div className="eval-flow-stage-copy mt-[8px] pr-4">
                        <div className={`eval-flow-stage-title ${stage.status === 'pending' ? 'eval-flow-stage-title-muted' : ''} ${isCurrentStage ? 'eval-flow-stage-title-current' : ''}`}>
                          {stage.title}
                        </div>
                        <div className={`mt-1 inline-flex items-center gap-1.5 text-[12px] font-medium leading-4 ${textTone} ${isCurrentStage ? 'eval-flow-stage-status-current' : ''}`}>
                          {stage.status === 'completed' ? (
                            <Check size={12} />
                          ) : stage.status === 'running' ? (
                            <Loader2 size={12} className="animate-spin" />
                          ) : stage.status === 'failed' ? (
                            <AlertCircle size={12} />
                          ) : (
                            <span className="h-1.5 w-1.5 rounded-full bg-current opacity-70" />
                          )}
                          {workflowStageStatusLabel(stage.status, stage.pendingLabel)}
                        </div>
                      </div>
                    </div>
                  </div>
                )
              })}
            </div>
          </div>

          <div className="flex shrink-0 flex-wrap items-center gap-2 xl:ml-8 xl:self-start">
            {resetConfirm ? (
              <div className="flex items-center gap-1.5 rounded-lg border border-[var(--hb-danger)]/30 bg-[var(--hb-danger)]/5 px-2.5 py-2">
                <AlertCircle size={11} className="shrink-0 text-[var(--hb-danger)]" />
                <span className="whitespace-nowrap text-[11px] text-[var(--hb-danger)]">确认清理？</span>
                <button
                  type="button"
                  disabled={resetting || submitting}
                  className="text-[11px] font-semibold text-[var(--hb-danger)] underline-offset-2 hover:underline disabled:opacity-50"
                  onClick={onReset}
                >
                  {resetting ? (
                    <span className="flex items-center gap-1">
                      <Loader2 size={10} className="animate-spin" />清理中...
                    </span>
                  ) : '确认'}
                </button>
                <span className="text-[11px] text-[var(--hb-border)]">/</span>
                <button
                  type="button"
                  disabled={resetting}
                  className="text-[11px] text-[var(--hb-soft)] hover:text-[var(--hb-body)] disabled:opacity-50"
                  onClick={() => onSetResetConfirm(false)}
                >
                  取消
                </button>
              </div>
            ) : (
              <button
                type="button"
                disabled={resetting || submitting}
                className="eval-flow-ghost-btn"
                onClick={onReset}
                title="清理当前评估数据（工作区状态、会话记录、报告），便于重新走评估流程"
              >
                {resetting ? <Loader2 size={13} className="animate-spin" /> : <Trash2 size={13} />}
                {resetting ? '清理中...' : '清理'}
              </button>
            )}
            <button
              type="button"
              disabled={submitting || wsEvaluating || !aiRunning}
              className="eval-flow-primary-btn min-w-[176px] justify-center"
              onClick={onSubmitRun}
            >
              {wsEvaluating ? (
                <Loader2 size={13} className="animate-spin" />
              ) : (
                <CheckCircle2 size={13} />
              )}
              {primaryActionLabel}
            </button>
          </div>
        </div>


      </div>
    </section>
  )
}
