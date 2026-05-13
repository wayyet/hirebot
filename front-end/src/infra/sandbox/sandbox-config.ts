/**
 * 沙箱网关端点解析。
 *
 * 开发调试时可在 .env.local 中配置 VITE_SANDBOX_URL=http://localhost:18789，
 * 此时所有沙箱连接都会忽略接口返回的地址，直接使用该固定端点。
 * 不配置则沿用接口返回的动态地址，生产环境行为不变。
 */
export function resolveGatewayEndpoint(apiEndpoint: string | null | undefined): string | null {
  const fixed = (import.meta.env.VITE_SANDBOX_URL as string | undefined)?.trim()
  if (fixed) return fixed
  return apiEndpoint ?? null
}
