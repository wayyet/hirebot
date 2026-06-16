import { HiringCollectionStage, type HiringCollectionStageType } from '@/infra/api'
import { getStageAdvanceConfirmationCopy } from './utils/hiringConfirmationCopy'

export type StageAdvanceIntent = 'collecting' | 'ready_to_advance' | 'skip'

export interface PendingStageAdvanceConfirmation {
  stage: typeof HiringCollectionStage.Material | typeof HiringCollectionStage.External
  summary: string
  title: string
  prompt: string
  continueLabel: string
  confirmLabel: string
  continueNotice: string
  visibleMessage: string
}

export function shouldRequireStageAdvanceConfirmation(
  stage: HiringCollectionStageType,
  intent: StageAdvanceIntent,
): boolean {
  if (intent !== 'ready_to_advance') {
    return false
  }

  return stage === HiringCollectionStage.Material || stage === HiringCollectionStage.External
}

export function buildPendingStageAdvanceConfirmation(
  stage: HiringCollectionStageType,
  summary: string,
): PendingStageAdvanceConfirmation | null {
  if (stage === HiringCollectionStage.Material) {
    const copy = getStageAdvanceConfirmationCopy('material')
    return { stage, summary, ...copy }
  }

  if (stage === HiringCollectionStage.External) {
    const copy = getStageAdvanceConfirmationCopy('external')
    return { stage, summary, ...copy }
  }

  return null
}
