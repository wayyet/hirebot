/**
 * artifactConstants.ts - Artifact 业务常量
 * 
 * 包含 artifact 数据脱敏、过滤等业务常量配置
 */

/**
 * 隐藏的 artifact 数据键（不对用户展示的技术字段）
 */
export const HIDDEN_ARTIFACT_DATA_KEYS = new Set([
  'artifactroot',
  'debug',
  'generatedat',
  'generatedby',
  'metadata',
  'raw',
  'rootpath',
  'sourcepath',
  'storagepath',
  'technicalartifact',
  'templateslug',
  'trace',
  'workspacedir',
  'workspacepath',
  'workspaceroot',
])

/**
 * 敏感数据键名部分（包含这些关键词的字段会被隐藏）
 */
export const SENSITIVE_ARTIFACT_DATA_KEY_PARTS = [
  'apikey',
  'authorization',
  'bearer',
  'connectionstring',
  'credential',
  'env',
  'header',
  'metadata',
  'password',
  'privatekey',
  'secret',
  'token',
]

/**
 * 标准化 artifact 数据键名（小写+去除非字母数字字符）
 */
export function normalizeArtifactDataKey(key: string): string {
  return key.toLowerCase().replace(/[^a-z0-9]/g, '')
}

/**
 * 判断 artifact 数据键是否应该被隐藏
 */
export function shouldHideArtifactDataKey(key: string): boolean {
  const normalized = normalizeArtifactDataKey(key)
  return HIDDEN_ARTIFACT_DATA_KEYS.has(normalized) ||
    SENSITIVE_ARTIFACT_DATA_KEY_PARTS.some(part => normalized.includes(part))
}

/**
 * 清理 artifact 数据用于显示（递归移除隐藏/敏感字段）
 */
export function sanitizeArtifactDataForDisplay(value: unknown, seen = new WeakSet<object>()): unknown {
  if (Array.isArray(value)) {
    return value.map(item => sanitizeArtifactDataForDisplay(item, seen))
  }

  // 使用独立的 isRecord 判断避免循环依赖
  const isRecordLocal = (v: unknown): v is Record<string, unknown> => {
    return !!v && typeof v === 'object' && !Array.isArray(v)
  }

  const record = isRecordLocal(value) ? value : null
  if (!record) {
    return value
  }

  if (seen.has(record)) {
    return '[Circular]'
  }
  seen.add(record)

  const sanitized: Record<string, unknown> = {}
  for (const [key, child] of Object.entries(record)) {
    if (shouldHideArtifactDataKey(key)) {
      continue
    }

    sanitized[key] = sanitizeArtifactDataForDisplay(child, seen)
  }

  seen.delete(record)
  return sanitized
}
