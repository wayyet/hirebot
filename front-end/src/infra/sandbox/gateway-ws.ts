/**
 * 直连沙箱 OpenSandbox Gateway 的 WebSocket 封装。
 * 无需经过 HireBot 后端代理，直接用当前 Keycloak token 鉴权。
 */
export type GatewayMessage = Record<string, unknown> & { type: string }

function buildGatewayWsUrl(endpoint: string, token: string): string {
  const trimmed = endpoint.trim()
  let base = trimmed
  if (/^https?:\/\//i.test(base)) {
    base = base.replace(/^http/i, 'ws')
  } else if (!/^wss?:\/\//i.test(base)) {
    // 无 scheme 时跟随页面协议：http:// → ws://，https:// → wss://
    const protocol = location.protocol === 'https:' ? 'wss' : 'ws'
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
  private readonly endpoint: string
  private readonly token: string

  onMessage: ((msg: GatewayMessage) => void) | null = null
  onStateChange: ((state: 'connecting' | 'open' | 'closed' | 'error') => void) | null = null

  constructor(endpoint: string, token: string) {
    this.endpoint = endpoint
    this.token = token
  }

  connect() {
    if (this.ws?.readyState === WebSocket.OPEN) return
    this.destroyed = false
    const url = buildGatewayWsUrl(this.endpoint, this.token)
    this.ws = new WebSocket(url)
    this.onStateChange?.('connecting')
    this.ws.onopen = () => this.onStateChange?.('open')
    this.ws.onmessage = (ev) => {
      try {
        const msg = JSON.parse(ev.data as string) as GatewayMessage
        this.onMessage?.(msg)
      } catch { /* 忽略格式错误的帧 */ }
    }
    this.ws.onerror = () => this.onStateChange?.('error')
    this.ws.onclose = () => {
      this.onStateChange?.('closed')
      // 非主动断开时自动重连
      if (!this.destroyed) {
        this.reconnectTimer = setTimeout(() => this.connect(), 3000)
      }
    }
  }

  send(msg: GatewayMessage) {
    if (this.ws?.readyState === WebSocket.OPEN) {
      this.ws.send(JSON.stringify(msg))
    }
  }

  disconnect() {
    this.destroyed = true
    if (this.reconnectTimer) clearTimeout(this.reconnectTimer)
    this.ws?.close()
    this.ws = null
  }
}
