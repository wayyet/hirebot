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
    <div className="flex items-start w-full">
      {steps.map((step, i) => {
        const completed = i < current
        const active = i === current
        const clickable = onStepClick && i < current

        return (
          <div key={i} className="flex items-start flex-1 min-w-0">
            {/* Step node */}
            <div
              className={`flex flex-col items-center ${clickable ? 'cursor-pointer' : ''}`}
              style={{ minWidth: 40 }}
              onClick={() => clickable && onStepClick(i)}
            >
              {/* Circle */}
              <div className={`
                w-8 h-8 rounded-full flex items-center justify-center text-sm font-semibold transition-all shrink-0
                ${completed ? 'bg-emerald-500 text-white' : ''}
                ${active ? 'bg-slate-900 text-white ring-4 ring-slate-100' : ''}
                ${!completed && !active ? 'bg-white border-2 border-slate-200 text-slate-400' : ''}
              `}>
                {completed ? <Check size={14} strokeWidth={2.5} /> : <span>{i + 1}</span>}
              </div>

              {/* Title */}
              <div className={`mt-2 text-xs font-medium text-center leading-tight whitespace-nowrap ${
                active ? 'text-slate-900' : completed ? 'text-slate-500' : 'text-slate-300'
              }`}>
                {step.title}
              </div>

              {/* Description */}
              {step.description && (
                <div className={`mt-0.5 text-[10px] text-center leading-tight max-w-[80px] ${
                  active ? 'text-slate-500' : 'text-slate-300'
                }`}>
                  {step.description}
                </div>
              )}
            </div>

            {/* Connector line */}
            {i < steps.length - 1 && (
              <div className="flex-1 flex items-start pt-4 mx-1">
                <div className={`h-px w-full transition-colors ${completed ? 'bg-emerald-300' : 'bg-slate-100'}`} />
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}
