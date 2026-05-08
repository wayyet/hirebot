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

async function sandboxGet<T>(endpoint: string, path: string): Promise<T> {
  const res = await fetch(buildUrl(endpoint, path), { headers: await authHeaders() })
  if (!res.ok) throw new Error(`GET ${path}: ${res.status}`)
  return res.json() as Promise<T>
}

// ── 公开 API ─────────────────────────────────────────────────────────────

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
    for (const turn of resp.session.history) {
      if (turn.role === 'user') {
        messages.push({ type: 'user_message', text: turn.content, _historical: true })
      } else if (turn.role === 'assistant') {
        // 工具调用条目跳过，只显示文字内容
        const rawContent = turn.content ?? ''
        if (rawContent && rawContent !== '[tool_use]') {
          messages.push({ type: 'assistant_message', content: rawContent, _historical: true })
        }
      }
    }
    return messages
  } catch {
    return []
  }
}
