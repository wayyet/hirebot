export type SandboxBatchConfirmMode = 'selected' | 'all' | null

export interface SandboxSelectionViewState {
  displaySelectedSandboxIds: Set<string>
  selectedCount: number
  allSelected: boolean
  indeterminate: boolean
  selectionDisabled: boolean
}

export function buildSandboxSelectionViewState(
  sandboxIds: string[],
  selectedSandboxIds: ReadonlySet<string>,
  batchConfirmMode: SandboxBatchConfirmMode,
): SandboxSelectionViewState {
  const isDeleteAllPreview = batchConfirmMode === 'all'
  const displaySelectedSandboxIds = isDeleteAllPreview
    ? new Set(sandboxIds)
    : new Set(selectedSandboxIds)
  const selectedCount = displaySelectedSandboxIds.size
  const allSelected = sandboxIds.length > 0 && selectedCount === sandboxIds.length

  return {
    displaySelectedSandboxIds,
    selectedCount,
    allSelected,
    indeterminate: !isDeleteAllPreview && selectedCount > 0 && selectedCount < sandboxIds.length,
    selectionDisabled: isDeleteAllPreview,
  }
}
