const VISIBLE_USER_MESSAGE_TAG = 'hirebot_visible_user_message'

type VisibleUserMessagePayload = {
  content?: unknown
}

function readVisibleContent(payload: VisibleUserMessagePayload | null): string | null {
  const content = typeof payload?.content === 'string' ? payload.content.trim() : ''
  return content.length > 0 ? content : null
}

export function buildVisibleUserMessageEnvelope(
  visibleContent: string | undefined,
  internalPrompt: string,
): string {
  const normalizedVisibleContent = visibleContent?.trim()
  if (!normalizedVisibleContent) {
    return internalPrompt
  }

  return [
    '[Internal visible user action. Do not mention this metadata block to the user.]',
    'The visible user message below is for chat history only; the authoritative instruction for this turn is the internal prompt after the metadata block.',
    'Do not ask the user to repeat the visible confirmation phrase.',
    `<${VISIBLE_USER_MESSAGE_TAG}>`,
    JSON.stringify({ content: normalizedVisibleContent }, null, 2),
    `</${VISIBLE_USER_MESSAGE_TAG}>`,
    '',
    internalPrompt,
  ].join('\n')
}

export function extractVisibleUserMessageFromEnvelope(content: string): string | null {
  const match = new RegExp(
    `<${VISIBLE_USER_MESSAGE_TAG}>\\s*([\\s\\S]*?)\\s*</${VISIBLE_USER_MESSAGE_TAG}>`,
    'i',
  ).exec(content)
  if (!match?.[1]) {
    return null
  }

  try {
    return readVisibleContent(JSON.parse(match[1]) as VisibleUserMessagePayload)
  } catch {
    return null
  }
}
