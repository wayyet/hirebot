import { describe, expect, it } from 'vitest'

import { buildSandboxSelectionViewState } from './settingsPageSelection'

describe('buildSandboxSelectionViewState', () => {
  it('shows all sandboxes as selected during delete-all confirmation', () => {
    const state = buildSandboxSelectionViewState(
      ['sandbox-1', 'sandbox-2', 'sandbox-3'],
      new Set(['sandbox-1']),
      'all',
    )

    expect(state.selectedCount).toBe(3)
    expect(state.allSelected).toBe(true)
    expect(state.indeterminate).toBe(false)
    expect(state.selectionDisabled).toBe(true)
    expect([...state.displaySelectedSandboxIds]).toEqual([
      'sandbox-1',
      'sandbox-2',
      'sandbox-3',
    ])
  })

  it('keeps the real selection during normal multi-select mode', () => {
    const state = buildSandboxSelectionViewState(
      ['sandbox-1', 'sandbox-2', 'sandbox-3'],
      new Set(['sandbox-1', 'sandbox-2']),
      'selected',
    )

    expect(state.selectedCount).toBe(2)
    expect(state.allSelected).toBe(false)
    expect(state.indeterminate).toBe(true)
    expect(state.selectionDisabled).toBe(false)
    expect([...state.displaySelectedSandboxIds]).toEqual([
      'sandbox-1',
      'sandbox-2',
    ])
  })
})
