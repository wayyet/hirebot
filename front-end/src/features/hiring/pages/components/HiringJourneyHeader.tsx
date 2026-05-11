import { ArrowLeft } from 'lucide-react'

type HiringJourneyHeaderProps = {
  templateName: string
  onBack: () => void
  onReset: () => void
  onContinue: () => void
  resetting?: boolean
}

export function HiringJourneyHeader({
  templateName,
  onBack,
  onReset,
  onContinue,
  resetting = false,
}: HiringJourneyHeaderProps) {
  return (
    <div className="hb-hiring-header">
      <div className="hb-hiring-hero-copy">
        <div className="hb-hiring-header-top">
          <button type="button" onClick={onBack} className="hb-hiring-back-link">
            <ArrowLeft size={14} />
            返回
          </button>
          <h1 className="hb-hiring-hero-title">
            数字员工雇佣
            <span className="hb-hiring-hero-template-tag">「{templateName}」</span>
          </h1>
        </div>
        <p className="hb-hiring-journey-summary hb-hiring-hero-summary">
          资料、技能、外部系统与交付包在同一条工作流里闭环
        </p>
      </div>
      <div className="hb-hiring-hero-actions">
        <button type="button" className="hb-hiring-ghost-btn" onClick={onReset} disabled={resetting}>
          {resetting ? '重置中...' : '重置流程'}
        </button>
        <button type="button" className="hb-hiring-primary-btn" onClick={onContinue}>
          从当前阶段继续
        </button>
      </div>
    </div>
  )
}
