/**
 * 直连沙箱 OpenSandbox Gateway 的 REST API 封装。
 * 无需经过 HireBot 后端代理，直接用当前 Keycloak token 鉴权。
 */
import { tokenService } from '@/infra/auth/token-service'
import { inferGatewayProtocol } from '@/infra/sandbox/sandbox-utils'

interface ChatTurn {
  role: string
  content: string
  timestamp?: string
  toolCalls?: { toolName: string; arguments?: string; result?: string }[]
}

export interface SandboxToolCall {
  toolName: string
  arguments?: string
  result?: string
}

interface SessionDetail {
  id: string
  history: ChatTurn[]
}

interface SessionDetailResponse {
  success?: boolean
  error?: string
  message?: string
  session: SessionDetail | null
  isActive: boolean
}

export interface SandboxMessage {
  type: string
  content?: string
  text?: string
  createdAt?: string
  toolCalls?: SandboxToolCall[]
  _historical?: boolean
  [key: string]: unknown
}

// ── Admin Sessions ────────────────────────────────────────────────────────

export interface SessionSummary {
  id: string
  channelId: string
  senderId: string
  createdAt: string
  lastActiveAt: string
  state: 'Active' | 'Paused' | 'Expired'
  historyTurns: number
  totalInputTokens: number
  totalOutputTokens: number
  isActive: boolean
}

export interface PagedSessionList {
  page: number
  pageSize: number
  hasMore: boolean
  items: SessionSummary[]
}

export interface AdminSessionsResponse {
  filters: Record<string, unknown>
  active: SessionSummary[]
  persisted: PagedSessionList
}

export interface AdminSessionsParams {
  page?: number
  pageSize?: number
  search?: string
  channelId?: string
}

export async function fetchAdminSessions(
  endpoint: string,
  params: AdminSessionsParams = {},
): Promise<AdminSessionsResponse> {
  const searchParams = new URLSearchParams()
  // 与 kingcrab-console fetchAllSessions 保持一致：始终过滤 websocket 频道
  searchParams.set('channelId', 'websocket')
  if (params.page !== undefined) searchParams.set('page', String(params.page))
  if (params.pageSize !== undefined) searchParams.set('pageSize', String(params.pageSize))
  if (params.search) searchParams.set('search', params.search)

  const qs = searchParams.toString()
  // 使用 /api/integration/sessions（非 /admin/sessions），与 kingcrab-console 及同文件 fetchLatestGatewaySession 一致
  const path = `/api/integration/sessions?${qs}`
  return sandboxGet<AdminSessionsResponse>(endpoint, path)
}

// ── 内部工具函数 ───────────────────────────────────────────────────────────

function buildUrl(endpoint: string, path: string): string {
  const trimmed = endpoint.trim()
  let base: string
  if (/^https?:\/\//i.test(trimmed)) {
    base = trimmed.replace(/\/$/, '')
  } else {
    // 无 scheme：localhost 走 http，其他地址始终走 https
    const protocol = inferGatewayProtocol(trimmed, 'https', 'http')
    base = `${protocol}://${trimmed.replace(/^\/+/, '')}`
  }
  return `${base}/${path.replace(/^\/+/, '')}`
}

async function authHeaders(): Promise<HeadersInit> {
  const token = await tokenService.ensureFresh()
  return { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' }
}

// ── Media upload ─────────────────────────────────────────────────────────

interface GatewayMediaUploadResponse {
  id: string
  url: string
  fileName: string
  mimeType: string
  sizeBytes: number
}

export interface GatewayMediaUploadResult {
  mediaId: string
  url: string
  fileName: string
  mimeType: string
  sizeBytes: number
  marker: string
}

/**
 * 将文件直接上传到沙箱 Gateway 的 /media/upload 端点。
 * 返回 mediaId、url 和可用于 WebSocket 消息的 [FILE_URL:...] 标记。
 */
export async function uploadMediaToGateway(
  endpoint: string,
  token: string,
  file: File,
): Promise<GatewayMediaUploadResult> {
  const url = buildUrl(endpoint, '/media/upload')
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(url, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
    body: form,
  })
  if (!res.ok) {
    let errorMsg = `POST /media/upload: ${res.status}`
    try {
      const body = await res.json() as { error?: string; message?: string }
      if (body.error) errorMsg = body.error
      else if (body.message) errorMsg = body.message
    } catch { /* ignore */ }
    throw new Error(errorMsg)
  }
  const data = await res.json() as GatewayMediaUploadResponse
  return {
    mediaId: data.id,
    url: data.url,
    fileName: data.fileName,
    mimeType: data.mimeType,
    sizeBytes: data.sizeBytes,
    marker: `[FILE_URL:/app/memory/media-cache/${data.id}]`,
  }
}

export interface GatewayWorkspaceUploadResult {
  files: string[]
  fileCount: number
  workspaceDir: string
  workspacePath: string
  fileMarker: string
}

/**
 * 将文件直接上传到沙箱 Gateway 的 /admin/workspace/upload 端点，
 * 文件落盘到沙箱工作区（默认 /workspace/{dir}/{fileName}），
 * 返回可嵌入 WS 消息的 [FILE_URL:...] 标记。
 */
export async function uploadWorkspaceFileToGateway(
  endpoint: string,
  token: string,
  file: File,
  dir: string,
): Promise<GatewayWorkspaceUploadResult> {
  // 对路径各段分别编码，但保留 / 作为目录分隔符（避免 uploads%2Ftemplate-packages 被服务端当平坦目录名）
  const encodedDir = dir.split('/').map(s => encodeURIComponent(s)).join('/')
  const url = buildUrl(endpoint, `/admin/workspace/upload?dir=${encodedDir}`)
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(url, {
    method: 'POST',
    headers: { Authorization: `Bearer ${token}` },
    body: form,
  })
  if (!res.ok) {
    let errorMsg = `POST /admin/workspace/upload: ${res.status}`
    try {
      const body = await res.json() as { error?: string; message?: string }
      if (body.error) errorMsg = body.error
      else if (body.message) errorMsg = body.message
    } catch { /* ignore */ }
    throw new Error(errorMsg)
  }
  const data = await res.json() as { files: string[]; fileCount: number }
  const workspaceDir = `/workspace/${dir.replace(/^\/+/, '')}`
  const workspacePath = `${workspaceDir}/${file.name}`
  return {
    files: data.files ?? [],
    fileCount: data.fileCount ?? 0,
    workspaceDir,
    workspacePath,
    fileMarker: `[FILE_URL:${workspaceDir}]`,  // ZIP 已由 gateway 解压，标记指向解压后的目录根
  }
}

async function sandboxGet<T>(endpoint: string, path: string): Promise<T> {
  const res = await fetch(buildUrl(endpoint, path), { headers: await authHeaders() })
  if (!res.ok) {
    let errorMsg = `GET ${path}: ${res.status}`
    try {
      const body = await res.json() as { error?: string; message?: string }
      if (body.error) errorMsg = body.error
      else if (body.message) errorMsg = body.message
    } catch { /* ignore */ }
    throw new Error(errorMsg)
  }
  return res.json() as Promise<T>
}

async function sandboxPost<T>(endpoint: string, path: string, body: unknown): Promise<T> {
  const res = await fetch(buildUrl(endpoint, path), {
    method: 'POST',
    headers: await authHeaders(),
    body: JSON.stringify(body),
  })
  if (!res.ok) {
    let errorMsg = `POST ${path}: ${res.status}`
    try {
      const err = await res.json() as { error?: string; message?: string }
      if (err.error) errorMsg = err.error
      else if (err.message) errorMsg = err.message
    } catch { /* ignore */ }
    throw new Error(errorMsg)
  }
  return res.json() as Promise<T>
}

async function sandboxDelete<T>(endpoint: string, path: string): Promise<T> {
  const res = await fetch(buildUrl(endpoint, path), {
    method: 'DELETE',
    headers: await authHeaders(),
  })
  if (!res.ok) {
    let errorMsg = `DELETE ${path}: ${res.status}`
    try {
      const err = await res.json() as { error?: string; message?: string }
      if (err.error) errorMsg = err.error
      else if (err.message) errorMsg = err.message
    } catch { /* ignore */ }
    throw new Error(errorMsg)
  }
  return res.json() as Promise<T>
}

// ── 公开 API ─────────────────────────────────────────────────────────────

/**
 * 查询沙箱网关最新的 WebSocket 会话 ID。
 * 优先返回活跃会话，其次按最近活跃时间排序返回持久化会话中的最新一条。
 * 用于进入页面时判断是否有已有会话可恢复，而非每次都重新引导。
 */
export async function fetchLatestGatewaySession(endpoint: string): Promise<string | null> {
  try {
    const resp = await sandboxGet<AdminSessionsResponse>(
      endpoint,
      '/api/integration/sessions?channelId=websocket&pageSize=5',
    )
    if (resp.active.length > 0) return resp.active[0].id
    const items = resp.persisted?.items ?? []
    if (items.length > 0) {
      const sorted = [...items].sort(
        (a, b) => new Date(b.lastActiveAt).getTime() - new Date(a.lastActiveAt).getTime(),
      )
      return sorted[0].id
    }
    return null
  } catch {
    return null
  }
}

/**
 * 从沙箱直接获取指定会话的历史消息，映射为对话气泡格式。
 * 替代后端代理的 getConversationTimeline，减少多余的一跳。
 */
export async function fetchSandboxSessionMessages(
  endpoint: string,
  sessionId: string,
): Promise<SandboxMessage[]> {
  const encoded = encodeURIComponent(sessionId)
  let resp: SessionDetailResponse
  try {
    resp = await sandboxGet<SessionDetailResponse>(
      endpoint,
      `/api/integration/sessions/${encoded}`,
    )
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    if (/session not found/i.test(message) || /(^|\D)404(\D|$)/i.test(message)) {
      return []
    }
    throw error
  }

  if (
    resp.success === false &&
    /session not found/i.test(resp.error ?? resp.message ?? '')
  ) {
    return []
  }

  if (!resp.session) return []

  const messages: SandboxMessage[] = []
  for (const turn of resp.session.history) {
    if (turn.role === 'assistant') {
      const rawContent = turn.content ?? ''
      const toolCalls = Array.isArray(turn.toolCalls)
        ? turn.toolCalls
            .filter((toolCall) => Boolean(toolCall.toolName))
            .map<SandboxToolCall>((toolCall) => ({
              toolName: toolCall.toolName,
              arguments: toolCall.arguments,
              result: toolCall.result,
            }))
        : undefined

      if ((rawContent && rawContent !== '[tool_use]') || (toolCalls?.length ?? 0) > 0) {
        messages.push({
          type: 'assistant_message',
          content: rawContent !== '[tool_use]' ? rawContent : '',
          createdAt: turn.timestamp,
          toolCalls,
          _historical: true,
        })
      }
    } else if (turn.role === 'user') {
      messages.push({
        type: 'user_message',
        text: turn.content,
        createdAt: turn.timestamp,
        _historical: true,
      })
    }
  }
  return messages
}

// ── IM 频道配置 ────────────────────────────────────────────────────────────

export interface GatewayFeishuChannelConfig {
  status?: string | null
  connectionMode?: string | null
  webhookPath?: string | null
  configuredAt?: string | null
  lastError?: string | null
  appId?: string | null
  appSecret?: string | null
  appIdRef?: string | null
  appSecretRef?: string | null
}

export interface GatewayDingTalkChannelConfig {
  enabled?: boolean
  appId?: string | null
  appIdRef?: string | null
  appKey?: string | null
  appKeyRef?: string | null
  appSecret?: string | null
  appSecretRef?: string | null
  robotCode?: string | null
  robotCodeRef?: string | null
  groupPolicy?: string | null
  allowedFromUserIds?: string[] | null
  allowedGroupIds?: string[] | null
  maxInboundChars?: number | null
  requireMentionInGroup?: boolean | null
  exposeInboundMediaUrls?: boolean | null
  streamPollIntervalMs?: number | null
}

export interface GatewayWeComChannelConfig {
  enabled?: boolean
  botId?: string | null
  botIdRef?: string | null
  botSecret?: string | null
  botSecretRef?: string | null
}

export interface GatewayOperationResult {
  success: boolean
  message?: string | null
  error?: string | null
  mode?: string | null
}

export interface FeishuChannelConfigPayload {
  enabled: boolean
  appId?: string | null
  appIdRef?: string | null
  appSecret?: string | null
  appSecretRef?: string | null
  groupPolicy?: string | null
  allowedFromUserIds?: string[] | null
}

export interface DingTalkChannelConfigPayload {
  enabled: boolean
  appId?: string | null
  appIdRef?: string | null
  appKey?: string | null
  appKeyRef?: string | null
  appSecret?: string | null
  appSecretRef?: string | null
  robotCode?: string | null
  robotCodeRef?: string | null
  groupPolicy?: string | null
  allowedFromUserIds?: string[] | null
  allowedGroupIds?: string[] | null
  maxInboundChars?: number | null
  requireMentionInGroup?: boolean | null
  exposeInboundMediaUrls?: boolean | null
  streamPollIntervalMs?: number | null
}

export interface WeComChannelConfigPayload {
  enabled: boolean
  botId?: string | null
  botIdRef?: string | null
  botSecret?: string | null
  botSecretRef?: string | null
}

export async function fetchFeishuChannelConfig(
  endpoint: string,
): Promise<GatewayFeishuChannelConfig> {
  return sandboxGet<GatewayFeishuChannelConfig>(
    endpoint,
    '/admin/channels/feishu',
  )
}

export async function fetchDingTalkChannelConfig(
  endpoint: string,
): Promise<GatewayDingTalkChannelConfig> {
  return sandboxGet<GatewayDingTalkChannelConfig>(
    endpoint,
    '/admin/channels/dingtalk',
  )
}

export async function fetchWeComChannelConfig(
  endpoint: string,
): Promise<GatewayWeComChannelConfig> {
  return sandboxGet<GatewayWeComChannelConfig>(
    endpoint,
    '/admin/channels/wecom',
  )
}

export async function updateFeishuChannelConfig(
  endpoint: string,
  payload: FeishuChannelConfigPayload,
): Promise<GatewayOperationResult> {
  return sandboxPost<GatewayOperationResult>(
    endpoint,
    '/admin/channels/feishu/update',
    payload,
  )
}

export async function updateDingTalkChannelConfig(
  endpoint: string,
  payload: DingTalkChannelConfigPayload,
): Promise<GatewayOperationResult> {
  return sandboxPost<GatewayOperationResult>(
    endpoint,
    '/admin/channels/dingtalk/update',
    payload,
  )
}

export async function updateWeComChannelConfig(
  endpoint: string,
  payload: WeComChannelConfigPayload,
): Promise<GatewayOperationResult> {
  return sandboxPost<GatewayOperationResult>(
    endpoint,
    '/admin/channels/wecom/update',
    payload,
  )
}

export async function deleteFeishuChannelOverride(
  endpoint: string,
): Promise<GatewayOperationResult> {
  return sandboxDelete<GatewayOperationResult>(
    endpoint,
    '/admin/channels/feishu/override',
  )
}

export async function deleteDingTalkChannelOverride(
  endpoint: string,
): Promise<GatewayOperationResult> {
  return sandboxDelete<GatewayOperationResult>(
    endpoint,
    '/admin/channels/dingtalk/override',
  )
}

export async function deleteWeComChannelOverride(
  endpoint: string,
): Promise<GatewayOperationResult> {
  return sandboxDelete<GatewayOperationResult>(
    endpoint,
    '/admin/channels/wecom/override',
  )
}
