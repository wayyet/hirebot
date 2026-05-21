import { Check } from 'lucide-react'
import clsx from 'clsx'
import { useTranslation } from 'react-i18next'

import type { HiringUiStage, HiringStageStepVm } from '../hiringWorkflowViewModel'

type HiringStagePillsProps = {
  steps: HiringStageStepVm[]
  onSelectStage: (stage: HiringUiStage, blockedReason: string) => void
}

export function HiringStagePills({
  steps,
  onSelectStage,
}: HiringStagePillsProps) {
  const { t } = useTranslation()

  return (
    <div className="hb-hiring-journey">
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
            <span className="hb-hiring-step-index">
              {item.status === 'complete' ? <Check size={12} strokeWidth={2.5} /> : item.index + 1}
            </span>
            <span className="hb-hiring-step-copy">
              <strong>{item.title}</strong>
              <span>{item.description}</span>
              {item.dispatchStatus === 'running' && (
                <span className="hb-hiring-step-dispatch is-running">{t('hiring.dispatch.running')}</span>
              )}
              {item.dispatchStatus === 'completed' && (
                <span className="hb-hiring-step-dispatch is-done">
                  {item.dispatchSummary ?? t('hiring.dispatch.completed')}
                </span>
              )}
              {item.dispatchStatus === 'failed' && (
                <span className="hb-hiring-step-dispatch is-failed">{t('hiring.dispatch.failed')}</span>
              )}
            </span>
          </button>
        ))}
      </div>
    </div>
  )
}
