/**
 * 沙箱 Gateway 地址工具函数。
 * 判断规则与 kingcrab-console 保持一致：
 *   - localhost / 127.0.0.1 / [::1] / *.localhost → 使用不安全协议（http / ws）
 *   - 其他所有地址 → 始终使用安全协议（https / wss），不依赖当前页面协议
 */

function isLocalGatewayHost(hostname: string): boolean {
  return (
    hostname === 'localhost' ||
    hostname === '127.0.0.1' ||
    hostname === '[::1]' ||
    hostname.endsWith('.localhost')
  )
}

function extractGatewayHostname(endpoint: string): string {
  // 去掉 scheme（如果有），再取 host 部分
  const withoutScheme = endpoint.trim().replace(/^[a-z]+:\/\//i, '')
  const hostPart = withoutScheme.split('/')[0] ?? ''

  // IPv6 格式：[::1]:port
  if (hostPart.startsWith('[')) {
    const closing = hostPart.indexOf(']')
    return closing >= 0
      ? hostPart.slice(0, closing + 1).toLowerCase()
      : hostPart.toLowerCase()
  }

  return (hostPart.split(':')[0] ?? '').toLowerCase()
}

/**
 * 根据 endpoint 推断应使用的协议。
 * @param endpoint   可以是带或不带 scheme 的地址
 * @param secure     非本地地址使用的安全协议（'https' | 'wss'）
 * @param insecure   本地地址使用的非安全协议（'http' | 'ws'）
 */
export function inferGatewayProtocol(
  endpoint: string,
  secure: 'https' | 'wss',
  insecure: 'http' | 'ws',
): 'https' | 'wss' | 'http' | 'ws' {
  const hostname = extractGatewayHostname(endpoint)
  return isLocalGatewayHost(hostname) ? insecure : secure
}
