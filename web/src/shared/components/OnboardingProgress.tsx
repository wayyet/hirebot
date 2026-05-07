import { CheckCircle, AlertCircle, ArrowRight, RefreshCw, FileUp, Bot, Play, ClipboardCheck, UserCheck, Trophy } from 'lucide-react'

export interface OnboardingStep {
  id: string
  title: string
  description: string
  status: 'completed' | 'current' | 'pending' | 'blocked'
  completedAt?: string
  blockedReason?: string
  iterationLabel?: string  // e.g. "第 2 轮 / 30"
}

interface OnboardingProgressProps {
  steps: OnboardingStep[]
  currentStepId: string
  onStepClick?: (stepId: string) => void
}

const STEP_ICONS: Record<string, React.ElementType> = {
  'submit-materials': FileUp,
  'generate-cases': Bot,
  'execute': Play,
  'evaluate': ClipboardCheck,
  'review': UserCheck,
  'onboard': Trophy,
}

function StepIcon({ id, status }: { id: string; status: OnboardingStep['status'] }) {
  if (status === 'completed') return <CheckCircle size={16} className="text-white" />
  if (status === 'blocked') return <AlertCircle size={16} className="text-white" />
  const Icon = STEP_ICONS[id] ?? Play
  // retraining indicator for generate-cases when current
  if (id === 'generate-cases' && status === 'current') {
    return <RefreshCw size={16} className="text-white" />
  }
  return <Icon size={16} className="text-white" />
}

export default function OnboardingProgress({ steps, onStepClick }: OnboardingProgressProps) {
  const completedCount = steps.filter(s => s.status === 'completed').length
  const progress = Math.round((completedCount / steps.length) * 100)

  const activeStep = steps.find(s => s.status === 'current' || s.status === 'blocked')

  return (
    <div className="bg-white rounded-xl border border-slate-100 p-5">
      {/* Header */}
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-sm font-semibold text-slate-700">上岗进度</h3>
        <div className="flex items-center gap-2">
          <span className="text-xs text-slate-400">{completedCount}/{steps.length}</span>
          <span className="text-sm font-bold text-indigo-600">{progress}%</span>
        </div>
      </div>

      {/* Steps row */}
      <div className="flex items-start gap-1 mb-3">
        {steps.map((step, index) => {
          const isCompleted = step.status === 'completed'
          const isCurrent = step.status === 'current'
          const isBlocked = step.status === 'blocked'

          const circleBg = isCompleted
            ? 'bg-emerald-500'
            : isCurrent
            ? 'bg-indigo-500 ring-4 ring-indigo-100 shadow-md'
            : isBlocked
            ? 'bg-red-400'
            : 'bg-slate-200'

          const labelColor = isCompleted
            ? 'text-emerald-700'
            : isCurrent
            ? 'text-indigo-700'
            : isBlocked
            ? 'text-red-600'
            : 'text-slate-400'

          const badgeBg = isCompleted
            ? 'bg-emerald-600 text-white'
            : isCurrent
            ? 'bg-indigo-600 text-white'
            : isBlocked
            ? 'bg-red-500 text-white'
            : 'bg-slate-300 text-slate-600'

          return (
            <div key={step.id} className="flex items-center flex-1 min-w-0">
              {/* Step node */}
              <div
                className={`flex flex-col items-center flex-1 min-w-0 ${onStepClick ? 'cursor-pointer' : ''}`}
                onClick={() => onStepClick?.(step.id)}
              >
                {/* Circle */}
                <div className={`relative w-9 h-9 rounded-full flex items-center justify-center mb-1.5 transition-all shrink-0 ${circleBg} ${onStepClick ? 'hover:scale-110' : ''}`}>
                  <StepIcon id={step.id} status={step.status} />
                  <div className={`absolute -top-1 -right-1 w-4 h-4 rounded-full flex items-center justify-center text-[9px] font-bold ${badgeBg}`}>
                    {index + 1}
                  </div>
                </div>

                {/* Title */}
                <div className={`text-[11px] font-medium text-center leading-tight px-0.5 truncate w-full ${labelColor}`}>
                  {step.title}
                </div>

                {/* Sub-label */}
                {isCompleted && step.completedAt && (
                  <div className="text-[9px] text-emerald-600 mt-0.5 truncate">{step.completedAt}</div>
                )}
                {isCurrent && !step.iterationLabel && (
                  <div className="text-[9px] text-indigo-600 bg-indigo-50 px-1.5 py-0.5 rounded mt-0.5 animate-pulse">
                    进行中
                  </div>
                )}
                {isCurrent && step.iterationLabel && (
                  <div className="text-[9px] text-indigo-600 bg-indigo-50 border border-indigo-200 px-1.5 py-0.5 rounded mt-0.5 font-medium">
                    {step.iterationLabel}
                  </div>
                )}
                {isBlocked && (
                  <div className="text-[9px] text-red-600 bg-red-50 px-1.5 py-0.5 rounded mt-0.5">
                    受阻
                  </div>
                )}
              </div>

              {/* Connector arrow */}
              {index < steps.length - 1 && (
                <div className="flex items-center justify-center shrink-0" style={{ width: 16, marginTop: -20 }}>
                  <ArrowRight
                    size={12}
                    className={isCompleted ? 'text-emerald-400' : 'text-slate-200'}
                  />
                </div>
              )}
            </div>
          )
        })}
      </div>

      {/* Progress bar */}
      <div className="mb-3">
        <div className="bg-slate-100 rounded-full h-1.5">
          <div
            className="bg-indigo-500 h-1.5 rounded-full transition-all duration-500"
            style={{ width: `${progress}%` }}
          />
        </div>
      </div>

      {/* Current step info banner */}
      {activeStep && (
        <div className={`p-3 rounded-lg text-xs ${
          activeStep.status === 'blocked'
            ? 'bg-red-50 border border-red-100'
            : 'bg-indigo-50 border border-indigo-100'
        }`}>
          {activeStep.status === 'blocked' ? (
            <>
              <div className="font-semibold text-red-700 mb-0.5 flex items-center gap-1.5">
                <AlertCircle size={12} />
                {activeStep.title}
              </div>
              <div className="text-red-600">{activeStep.blockedReason}</div>
            </>
          ) : (
            <>
              <div className="font-semibold text-indigo-700 mb-0.5 flex items-center gap-1.5">
                {activeStep.iterationLabel && (
                  <span className="text-[10px] font-bold bg-indigo-100 text-indigo-600 px-1.5 py-0.5 rounded">
                    {activeStep.iterationLabel}
                  </span>
                )}
                {activeStep.title}
              </div>
              <div className="text-indigo-600">{activeStep.description}</div>
            </>
          )}
        </div>
      )}

      {/* All done */}
      {completedCount === steps.length && (
        <div className="p-3 bg-emerald-50 border border-emerald-100 rounded-lg text-xs">
          <div className="flex items-center gap-2">
            <CheckCircle size={13} className="text-emerald-600 shrink-0" />
            <span className="font-semibold text-emerald-700">
              所有步骤已完成，数字员工已正式上岗
            </span>
          </div>
        </div>
      )}
    </div>
  )
}
