import { Rocket } from 'lucide-react'
import { useTranslation } from 'react-i18next'

interface EvalAutoInitScreenProps {
  countdown: number
  employeeName: string
  roleName: string
  onNow: () => void
  onCancel: () => void
}

export function EvalAutoInitScreen({ countdown, employeeName, roleName, onNow, onCancel }: EvalAutoInitScreenProps) {
  const { t } = useTranslation()

  return (
    <div className="flex h-[calc(100vh-116px)] min-h-[680px] items-center justify-center">
      <div className="w-full max-w-[360px] rounded-3xl border eval-chat-wrapper p-8 text-center shadow-xl">
        <div className="mb-5 flex justify-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-[var(--hb-blue)]/10">
            <Rocket size={26} className="text-[var(--hb-blue)]" />
          </div>
        </div>

        <h2 className="mb-1 text-[17px] font-semibold eval-text-title">
          {t('evaluationPage.autoInit.title')}
        </h2>
        <p className="mb-5 text-[13px] leading-relaxed eval-text-secondary">
          {t('evaluationPage.autoInit.desc', { name: employeeName, role: roleName })}
        </p>

        {/* 倒计时环形进度 */}
        <div className="relative mx-auto mb-5 h-[88px] w-[88px]">
          <svg className="h-[88px] w-[88px] -rotate-90" viewBox="0 0 100 100">
            <circle cx="50" cy="50" r="40" fill="none" stroke="var(--hb-border)" strokeWidth="6" />
            <circle
              cx="50" cy="50" r="40"
              fill="none"
              stroke="var(--hb-blue)"
              strokeWidth="6"
              strokeLinecap="round"
              strokeDasharray={`${2 * Math.PI * 40}`}
              strokeDashoffset={`${2 * Math.PI * 40 * (1 - countdown / 3)}`}
              style={{ transition: 'stroke-dashoffset 0.9s linear' }}
            />
          </svg>
          <div className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="text-[32px] font-bold leading-none eval-text-title">{countdown}</span>
            <span className="mt-0.5 text-[11px] eval-text-caption">{t('evaluationPage.autoInit.seconds')}</span>
          </div>
        </div>

        <p className="mb-6 text-[12px] leading-relaxed eval-text-secondary">
          {t('evaluationPage.autoInit.hint')}
        </p>

        <div className="flex flex-col gap-2">
          <button type="button" className="hb-btn-primary w-full !py-2.5 gap-1.5" onClick={onNow}>
            <Rocket size={13} />
            {t('evaluationPage.autoInit.btnNow')}
          </button>
          <button type="button" className="hb-btn-ghost w-full !py-2 !text-[12px]" onClick={onCancel}>
            {t('evaluationPage.autoInit.btnCancel')}
          </button>
        </div>
      </div>
    </div>
  )
}
