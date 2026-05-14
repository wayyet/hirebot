import { Breadcrumb } from '@/shared/components/Breadcrumb'

type HiringJourneyHeaderProps = {
  templateName: string
  templateId: string
  onReset: () => void
  onContinue: () => void
  resetting?: boolean
}

export function HiringJourneyHeader({
  templateName,
  templateId,
  onReset,
  onContinue,
  resetting = false,
}: HiringJourneyHeaderProps) {
  return (
    <div className="hb-hiring-header">
      <div className="hb-hiring-hero-copy">
        <Breadcrumb
          items={[
            { label: '模板池', to: '/template-pool' },
            { label: templateName, to: `/template-pool/templates/${templateId}` },
            { label: '雇佣流程' },
          ]}
        />
        <p className="hb-hiring-journey-summary hb-hiring-hero-summary">
          资料、技能、外部系统与交付包在同一条工作流里闭环
        </p>
      </div>
      {/* <div className="hb-hiring-hero-actions">
        <button type="button" className="hb-hiring-ghost-btn" onClick={onReset} disabled={resetting}>
          {resetting ? '重置中...' : '重置流程'}
        </button>
        <button type="button" className="hb-hiring-primary-btn" onClick={onContinue}>
          从当前阶段继续
        </button>
      </div> */}
    </div>
  )
}
