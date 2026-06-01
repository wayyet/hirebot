import { HiringCollectionStage, type HiringCollectionStageType } from '@/infra/api'

export type StageAdvanceIntent = 'collecting' | 'ready_to_advance' | 'skip'

export interface PendingStageAdvanceConfirmation {
  stage: typeof HiringCollectionStage.Material | typeof HiringCollectionStage.External
  summary: string
  title: string
  prompt: string
  continueLabel: string
  confirmLabel: string
  continueNotice: string
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
    return {
      stage,
      summary,
      title: '资料阶段待确认',
      prompt: '当前资料已收集到工作区。还需要继续上传资料，还是按当前资料推进到下一阶段？',
      continueLabel: '继续上传资料',
      confirmLabel: '确认推进资料阶段',
      continueNotice: '资料阶段保持开启，你可以继续补充资料后再推进。',
    }
  }

  if (stage === HiringCollectionStage.External) {
    return {
      stage,
      summary,
      title: '外部阶段待确认',
      prompt: '当前外部系统配置已保存。还需要继续调整配置，还是按当前配置推进到下一阶段？',
      continueLabel: '继续调整配置',
      confirmLabel: '确认推进外部阶段',
      continueNotice: '外部系统阶段保持开启，你可以继续修改配置后再推进。',
    }
  }

  return null
}
