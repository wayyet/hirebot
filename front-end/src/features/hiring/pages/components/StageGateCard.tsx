import type { StageGateData } from '../hiringPageTypes'

interface Props {
  stageGate: StageGateData
}

export function StageGateCard({ stageGate }: Props) {
  const { skillName, completedStage, nextStage, canProceed, blockedReason } = stageGate
  const stateClass = canProceed ? 'is-pass' : 'is-blocked'

  return (
    <div className={`hb-stage-gate-card ${stateClass}`}>
      <div className="hb-stage-gate-header">
        <span className="hb-stage-gate-icon">🔀</span>
        <span className="hb-stage-gate-skill">{skillName || '技能阶段'}</span>
        <span className={`hb-stage-gate-badge ${stateClass}`}>
          {canProceed ? '✓ 通过' : '✗ 阻塞'}
        </span>
      </div>

      <div className="hb-stage-gate-flow">
        {completedStage && (
          <span className="hb-stage-gate-stage">{completedStage}</span>
        )}
        {completedStage && nextStage && (
          <span className="hb-stage-gate-arrow">→</span>
        )}
        {nextStage && (
          <span className={`hb-stage-gate-stage ${canProceed ? 'is-next' : 'is-blocked-stage'}`}>
            {nextStage}
          </span>
        )}
      </div>

      {!canProceed && blockedReason && (
        <div className="hb-stage-gate-reason">{blockedReason}</div>
      )}
    </div>
  )
}
