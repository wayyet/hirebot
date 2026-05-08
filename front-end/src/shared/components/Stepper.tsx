import { Check } from 'lucide-react'

export interface StepDef {
  title: string
  description?: string
}

interface StepperProps {
  steps: StepDef[]
  current: number
  onStepClick?: (index: number) => void
}

export default function Stepper({ steps, current, onStepClick }: StepperProps) {
  return (
    <div className="hb-stepper">
      {steps.map((step, i) => {
        const completed = i < current
        const active = i === current
        const clickable = onStepClick && i < current

        return (
          <button
            key={i}
            type="button"
            disabled={!clickable}
            aria-current={active ? 'step' : undefined}
            onClick={() => clickable && onStepClick(i)}
            className={`hb-stepper-item ${completed ? 'is-done' : ''} ${active ? 'is-active' : ''} ${clickable ? 'is-clickable' : ''}`}
          >
            <span className="hb-stepper-index">
              {completed ? <Check size={14} strokeWidth={2.5} /> : <span>{i + 1}</span>}
            </span>
            <span className="hb-stepper-copy">
              <strong>{step.title}</strong>
              {step.description && <small>{step.description}</small>}
            </span>
          </button>
        )
      })}
    </div>
  )
}
