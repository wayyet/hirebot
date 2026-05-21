/**
 * 直连沙箱 OpenSandbox Gateway 的 WebSocket 封装。
 * 无需经过 HireBot 后端代理，直接用当前 Keycloak token 鉴权。
 */
import { inferGatewayProtocol } from '@/infra/sandbox/sandbox-utils'

export type GatewayMessage = Record<string, unknown> & { type: string }

function buildGatewayWsUrl(endpoint: string, token: string): string {
  const trimmed = endpoint.trim()
  let base = trimmed
  if (/^https?:\/\//i.test(base)) {
    base = base.replace(/^http/i, 'ws')
  } else if (!/^wss?:\/\//i.test(base)) {
    // 无 scheme：localhost 走 ws，其他地址始终走 wss
    const protocol = inferGatewayProtocol(base, 'wss', 'ws')
    base = `${protocol}://${base.replace(/^\/+/, '')}`
  }
  // 移除已有的 token 参数，避免重复
  base = base.replace(/([?&])token=[^&]*(&)?/i, '$1').replace(/[?&]$/, '')
  const hasWsPath = /\/ws(?:$|[?#])/i.test(base)
  const url = hasWsPath ? base : `${base.replace(/\/$/, '')}/ws`
  return `${url}${url.includes('?') ? '&' : '?'}token=${encodeURIComponent(token)}`
}

export class GatewayWs {
  private ws: WebSocket | null = null
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  private destroyed = false
  private isReconnect = false
  private readonly endpoint: string
  private readonly token: string

  onMessage: ((msg: GatewayMessage) => void) | null = null
  onStateChange: ((state: 'connecting' | 'open' | 'closed' | 'error') => void) | null = null
  /** 重连成功后触发，用于拉取断线期间的会话历史 */
  onReconnected: (() => void) | null = null

  constructor(endpoint: string, token: string) {
    this.endpoint = endpoint
    this.token = token
  }

  connect() {
    if (this.ws?.readyState === WebSocket.OPEN) return
    this.destroyed = false
    const url = buildGatewayWsUrl(this.endpoint, this.token)
    const wasReconnect = this.isReconnect
    this.ws = new WebSocket(url)
    this.onStateChange?.('connecting')
    this.ws.onopen = () => {
      this.onStateChange?.('open')
      if (wasReconnect) {
        this.isReconnect = false
        this.onReconnected?.()
      }
    }
    this.ws.onmessage = (ev) => {
      try {
        const msg = JSON.parse(ev.data as string) as GatewayMessage
        this.onMessage?.(msg)
      } catch { /* 忽略格式错误的帧 */ }
    }
    this.ws.onerror = () => this.onStateChange?.('error')
    this.ws.onclose = () => {
      this.onStateChange?.('closed')
      if (!this.destroyed) {
        this.isReconnect = true
        this.reconnectTimer = setTimeout(() => this.connect(), 3000)
      }
    }
  }

  isOpen() {
    return this.ws?.readyState === WebSocket.OPEN
  }

  send(msg: GatewayMessage) {
    if (this.ws?.readyState !== WebSocket.OPEN) {
      return false
    }
    this.ws.send(JSON.stringify(msg))
    return true
  }

  disconnect() {
    this.destroyed = true
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer)
    this.ws?.close()
    this.ws = null
  }
}
