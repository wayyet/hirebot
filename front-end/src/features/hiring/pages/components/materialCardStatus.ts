export type MaterialStageStatus = 'running' | 'completed' | 'failed' | null

export interface MaterialShellStatusInput {
  stageStatus: MaterialStageStatus
  materialCardCount: number
  completedCardCount: number
  totalUploadedCount: number
}

export function resolveMaterialShellStatusLabel(
  input: MaterialShellStatusInput,
  t: (key: string, options?: Record<string, unknown>) => string,
): string {
  if (input.stageStatus === 'completed') {
    return t('hiring.todo.status.completed')
  }

  if (input.stageStatus === 'failed') {
    return t('hiring.todo.status.failed')
  }

  if (input.materialCardCount > 0) {
    return input.completedCardCount >= input.materialCardCount
      ? t('hiring.todo.material.statusUploaded', { count: input.completedCardCount })
      : t('hiring.todo.material.statusPendingCount', { count: input.materialCardCount - input.completedCardCount })
  }

  if (input.totalUploadedCount > 0) {
    return t('hiring.todo.material.statusUploaded', { count: input.totalUploadedCount })
  }

  return t('hiring.todo.material.statusPending')
}
