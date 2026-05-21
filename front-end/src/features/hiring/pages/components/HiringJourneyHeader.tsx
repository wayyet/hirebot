import { useTranslation } from 'react-i18next'
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
}: HiringJourneyHeaderProps) {
  const { t } = useTranslation()

  return (
    <div className="hb-hiring-header">
      <div className="hb-hiring-hero-copy">
        <Breadcrumb
          items={[
            { label: t('hiring.header.breadcrumbTemplatePool'), to: '/template-pool' },
            { label: templateName, to: `/template-pool/templates/${templateId}` },
            { label: t('hiring.header.breadcrumbProcess') },
          ]}
        />
        <p className="hb-hiring-journey-summary hb-hiring-hero-summary">
          {t('hiring.header.summary')}
        </p>
      </div>
      {/* <div className="hb-hiring-hero-actions">
        <button type="button" className="hb-hiring-ghost-btn" onClick={onReset} disabled={resetting}>
          {resetting ? t('hiring.header.resetting') : t('hiring.header.resetProcess')}
        </button>
        <button type="button" className="hb-hiring-primary-btn" onClick={onContinue}>
          {t('hiring.header.continueCurrent')}
        </button>
      </div> */}
    </div>
  )
}
