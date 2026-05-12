import clsx from 'clsx'

import type { HiringUiStage, HiringStageStepVm } from '../hiringWorkflowViewModel'

type HiringStagePillsProps = {
  journeySummary: string
  steps: HiringStageStepVm[]
  onSelectStage: (stage: HiringUiStage, blockedReason: string) => void
}

export function HiringStagePills({
  journeySummary,
  steps,
  onSelectStage,
}: HiringStagePillsProps) {
  return (
    <div className="hb-hiring-journey">
      <p className="hb-hiring-journey-summary">{journeySummary}</p>
      <div className="hb-hiring-step-pills">
        {steps.map((item) => (
          <button
            key={item.stage}
            type="button"
            className={clsx(
              'hb-hiring-step-pill',
              `is-${item.status}`,
              !item.isClickable && 'is-locked',
            )}
            onClick={() => onSelectStage(item.stage, item.blockedReason)}
          >
            <span className="hb-hiring-step-index">{item.index + 1}</span>
            <span className="hb-hiring-step-copy">
              <strong>{item.title}</strong>
              <span>{item.description}</span>
              {item.dispatchStatus === 'running' && (
                <span className="hb-hiring-step-dispatch is-running">派发中</span>
              )}
              {item.dispatchStatus === 'completed' && (
                <span className="hb-hiring-step-dispatch is-done">
                  {item.dispatchSummary ?? '已产出'}
                </span>
              )}
              {item.dispatchStatus === 'failed' && (
                <span className="hb-hiring-step-dispatch is-failed">派发失败</span>
              )}
            </span>
          </button>
        ))}
      </div>
    </div>
  )
}
