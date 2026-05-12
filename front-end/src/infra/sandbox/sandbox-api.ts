/**
 * 直连沙箱 OpenSandbox Gateway 的 REST API 封装。
 * 无需经过 HireBot 后端代理，直接用当前 Keycloak token 鉴权。
 */
import { tokenService } from '@/infra/auth/token-service'

interface ChatTurn {
  role: string
  content: string
  timestamp?: string
  toolCalls?: { toolName: string; arguments?: string; result?: string }[]
}

interface SessionDetail {
  id: string
  history: ChatTurn[]
}

interface SessionDetailResponse {
  session: SessionDetail | null
  isActive: boolean
}

export interface SessionSummary {
  id: string
  channelId: string
  createdAt: string
  lastActiveAt: string
  state: string
  historyTurns: number
  isActive: boolean
}

interface PagedSessionList {
  items: SessionSummary[]
  hasMore: boolean
}

interface IntegrationSessionsResponse {
  active: SessionSummary[]
  persisted: PagedSessionList
}

export interface SandboxMessage {
  type: string
  content?: string
  text?: string
  _historical?: boolean
  [key: string]: unknown
}

// ── 内部工具函数 ───────────────────────────────────────────────────────────

function buildUrl(endpoint: string, path: string): string {
  const trimmed = endpoint.trim()
  let base: string
  if (/^https?:\/\//i.test(trimmed)) {
    base = trimmed.replace(/\/$/, '')
  } else {
    const protocol = location.protocol === 'https:' ? 'https' : 'http'
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

async function sandboxGet<T>(endpoint: string, path: string): Promise<T> {
  const res = await fetch(buildUrl(endpoint, path), { headers: await authHeaders() })
  if (!res.ok) throw new Error(`GET ${path}: ${res.status}`)
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
    const resp = await sandboxGet<IntegrationSessionsResponse>(
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
  try {
    const encoded = encodeURIComponent(sessionId)
    const resp = await sandboxGet<SessionDetailResponse>(
      endpoint,
      `/api/integration/sessions/${encoded}`,
    )
    if (!resp.session) return []

    const messages: SandboxMessage[] = []
    let assistantStarted = false
    for (const turn of resp.session.history) {
      if (turn.role === 'assistant') {
        assistantStarted = true
        // 工具调用条目跳过，只显示文字内容
        const rawContent = turn.content ?? ''
        if (rawContent && rawContent !== '[tool_use]') {
          messages.push({ type: 'assistant_message', content: rawContent, _historical: true })
        }
      } else if (turn.role === 'user') {
        // 第一个 assistant 回复之前的 user 消息是后端内部注入的冷启动 system prompt，
        // 不是用户真实输入，跳过不显示
        if (!assistantStarted) continue
        messages.push({ type: 'user_message', text: turn.content, _historical: true })
      }
    }
    return messages
  } catch {
    return []
  }
}
