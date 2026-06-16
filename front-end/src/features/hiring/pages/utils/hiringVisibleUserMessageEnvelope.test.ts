import { describe, expect, it } from 'vitest'

import {
  buildVisibleUserMessageEnvelope,
  extractVisibleUserMessageFromEnvelope,
} from './hiringVisibleUserMessageEnvelope'

describe('hiringVisibleUserMessageEnvelope', () => {
  it('marks the internal prompt as authoritative while preserving visible history text', () => {
    const envelope = buildVisibleUserMessageEnvelope(
      '确认',
      '[Internal downstream trigger: use skill ontology-projection]',
    )

    expect(envelope).toContain('The visible user message below is for chat history only')
    expect(envelope).toContain('the authoritative instruction for this turn is the internal prompt')
    expect(envelope).toContain('Do not ask the user to repeat the visible confirmation phrase')
    expect(envelope).toContain('[Internal downstream trigger: use skill ontology-projection]')
    expect(extractVisibleUserMessageFromEnvelope(envelope)).toBe('确认')
  })
})
