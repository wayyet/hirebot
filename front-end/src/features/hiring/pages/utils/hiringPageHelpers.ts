import i18n from '@/i18n'

const HIDDEN_ASSISTANT_TAG_PATTERN = 'think|dispatch|dispatch_callback|diagnostic_report|config_governance_patch'
const HIDDEN_ASSISTANT_TAG_REGEX = new RegExp(
  `<(${HIDDEN_ASSISTANT_TAG_PATTERN})\\b[^>]*>[\\s\\S]*?<\\/\\1>`,
  'gi',
)
const HIDDEN_ASSISTANT_OPEN_TAG_REGEX = new RegExp(
  `<(${HIDDEN_ASSISTANT_TAG_PATTERN})\\b[^>]*>`,
  'gi',
)
const INTERNAL_INSTRUCTION_LINE_REGEX =
  /^.*(?:\[Internal (?:stage resume|downstream trigger|packaging trigger|skill definition confirmation|external config commit)[^\]]*\]|Internal stage resume|Internal downstream trigger|Internal packaging trigger|Internal skill definition confirmation).*$(?:\r?\n)?/gim
const INTERNAL_PROTOCOL_MESSAGE_START_REGEX =
  /^\s*(?:\[Internal (?:stage resume|downstream trigger|packaging trigger|skill definition confirmation|external config commit)\b|Internal stage resume|Internal downstream trigger|Internal packaging trigger|Internal skill definition confirmation|Switch to skill `?[\w-]+`? now\.)/i
const LEAKED_DOWNSTREAM_PROTOCOL_TAIL_REGEX =
  /(?:^|\r?\n)\s*(?:Switch to skill `?[\w-]+`? now\.|required_artifacts:|artifact_payload:|skill_generation_progress\b|skill_generation_done\s+return_to:\s*employment-coach-conversation\b|return_to:\s*employment-coach-conversation\b)[\s\S]*$/i
const TOOL_BAN_DIAGNOSTIC_LINE_REGEX =
  /^\s*\[TOOL BAN\]\s*Refused to call\b.*$(?:\r?\n)?/gim
const ARTIFACT_PROTOCOL_DIAGNOSTIC_LINE_REGEX =
  /^\s*.*(?:不是这个阶段该发的 artifact|skill_generation_trigger 不在允许清单|skill_generation_trigger is not allowed).*$(?:\r?\n)?/gim
const FENCED_JSON_BLOCK_REGEX = /```(?:json)?\s*([\s\S]*?)```/gi

function asPlainObject(value: unknown): Record<string, unknown> | null {
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function hasOwn(record: Record<string, unknown>, key: string): boolean {
  return Object.prototype.hasOwnProperty.call(record, key)
}

function hasAnyKey(record: Record<string, unknown>, keys: string[]): boolean {
  return keys.some(key => hasOwn(record, key))
}

function tryParseJsonRecord(content: string): Record<string, unknown> | null {
  try {
    return asPlainObject(JSON.parse(content))
  } catch {
    return null
  }
}

function isLeakedArtifactProtocolRecord(record: Record<string, unknown>): boolean {
  const parameters = asPlainObject(record.parameters)
  if (parameters && isLeakedArtifactProtocolRecord(parameters)) {
    return true
  }

  if (hasAnyKey(record, ['artifactType', 'artifact_type', 'isTerminal', 'is_terminal', 'displayHint', 'display_hint'])) {
    return true
  }

  const hasMaterialReadyStatus = hasAnyKey(record, ['summary', 'message'])
    && hasAnyKey(record, ['next_step', 'nextStep', 'status'])
    && hasAnyKey(record, ['total_items', 'totalItems', 'category', 'objective', 'next_artifact', 'nextArtifact'])
  if (hasMaterialReadyStatus) {
    return true
  }

  return hasAnyKey(record, ['context_signature', 'contextSignature'])
    && hasAnyKey(record, ['status'])
    && hasAnyKey(record, ['next_artifact', 'nextArtifact', 'trigger_after', 'triggerAfter', 'options', 'summary', 'message'])
}

function readJsonRecordBlock(lines: string[], startIndex: number): { endIndex: number } | null {
  let block = ''
  const maxEndIndex = Math.min(lines.length, startIndex + 80)

  for (let index = startIndex; index < maxEndIndex; index += 1) {
    block += `${index === startIndex ? '' : '\n'}${lines[index]}`
    const record = tryParseJsonRecord(block.trim())
    if (!record) {
      continue
    }

    return isLeakedArtifactProtocolRecord(record)
      ? { endIndex: index }
      : null
  }

  return null
}

function removeLeakedStructuredJsonBlocks(content: string): string {
  const withoutFencedJson = content.replace(FENCED_JSON_BLOCK_REGEX, (match, inner: string) => {
    const record = tryParseJsonRecord(inner.trim())
    return record && isLeakedArtifactProtocolRecord(record) ? '' : match
  })
  const lines = withoutFencedJson.split(/\r?\n/)
  const visibleLines: string[] = []

  for (let index = 0; index < lines.length; index += 1) {
    if (lines[index].trim().startsWith('{')) {
      const block = readJsonRecordBlock(lines, index)
      if (block) {
        index = block.endIndex
        continue
      }
    }

    visibleLines.push(lines[index])
  }

  return visibleLines.join('\n').replace(/\n{3,}/g, '\n\n').trim()
}

function isInternalProtocolMessage(content: string): boolean {
  return INTERNAL_PROTOCOL_MESSAGE_START_REGEX.test(content)
}

function removeLeakedDownstreamProtocolTail(content: string): string {
  const match = LEAKED_DOWNSTREAM_PROTOCOL_TAIL_REGEX.exec(content)
  if (!match) {
    return content
  }

  return content.slice(0, match.index).trim()
}

function removeHiddenAssistantProtocol(content: string): string {
  const withoutClosedTags = content
    .replace(HIDDEN_ASSISTANT_TAG_REGEX, '')
    .replace(TOOL_BAN_DIAGNOSTIC_LINE_REGEX, '')
    .replace(ARTIFACT_PROTOCOL_DIAGNOSTIC_LINE_REGEX, '')
  if (isInternalProtocolMessage(withoutClosedTags)) {
    return ''
  }

  return removeLeakedStructuredJsonBlocks(removeLeakedDownstreamProtocolTail(withoutClosedTags)
    .replace(INTERNAL_INSTRUCTION_LINE_REGEX, '')
    .trim())
}

/**
 * 生成唯一 ID（时间戳 + 随机字符串）
 */
export function mkId(): string {
  return `${Date.now()}_${Math.random().toString(36).slice(2)}`
}

/**
 * 延迟指定毫秒
 */
export function sleep(ms: number): Promise<void> {
  return new Promise<void>((resolve) => {
    window.setTimeout(resolve, ms)
  })
}

/**
 * 规范化错误消息
 */
export function normalizeErrorMessage(error: unknown): string {
  if (error instanceof Error && error.message.trim().length > 0) {
    return error.message
  }

  return i18n.t('hiring.error.networkFailure')
}

/**
 * 规范化 AI 回复内容，移除仅供协议同步使用的内部标签，避免技术 JSON 直接展示给用户
 */
export function normalizeAssistantReply(content: string): string {
  return removeHiddenAssistantProtocol(content)
}

/**
 * 规范化流式预览内容。
 * 流式阶段可能只收到内部标签的开头，此时需要从标签起始处立刻截断，
 * 避免 technical artifact 的 JSON 在打字机过程中闪到界面上。
 */
export function normalizeAssistantStreamingPreview(content: string): string {
  const withoutClosedTags = content
    .replace(HIDDEN_ASSISTANT_TAG_REGEX, '')
    .replace(TOOL_BAN_DIAGNOSTIC_LINE_REGEX, '')
    .replace(ARTIFACT_PROTOCOL_DIAGNOSTIC_LINE_REGEX, '')
  if (isInternalProtocolMessage(withoutClosedTags)) {
    return ''
  }

  const withoutLeakedDownstreamSwitch = removeLeakedDownstreamProtocolTail(withoutClosedTags)
  const lowerContent = withoutLeakedDownstreamSwitch.toLowerCase()
  HIDDEN_ASSISTANT_OPEN_TAG_REGEX.lastIndex = 0

  let match: RegExpExecArray | null = null
  while ((match = HIDDEN_ASSISTANT_OPEN_TAG_REGEX.exec(withoutLeakedDownstreamSwitch)) !== null) {
    const tagName = match[1].toLowerCase()
    const closingTag = `</${tagName}>`
    const closingIndex = lowerContent.indexOf(closingTag, match.index + match[0].length)
    if (closingIndex >= 0) {
      continue
    }

    return withoutLeakedDownstreamSwitch.slice(0, match.index).trim()
  }

  return removeLeakedStructuredJsonBlocks(
    withoutLeakedDownstreamSwitch.replace(INTERNAL_INSTRUCTION_LINE_REGEX, '').trim(),
  )
}
