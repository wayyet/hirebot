import type { ApiResponseEnvelope, QueryParams, RequestOptions } from './types'
import { tokenService } from '@/infra/auth/token-service'

// 优先取后端注入的 ApiBase（镜像内部署时为 ""，即相对路径），
// 其次 VITE 环境变量，最后回退到本地开发默认地址
const API_BASE_URL = (() => {
  if (typeof window !== 'undefined' && window.__AUTH_CONFIG__?.ApiBase !== undefined) {
    return window.__AUTH_CONFIG__.ApiBase
  }
  return (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:5280'
})()

export class ApiClientError extends Error {
  public readonly status: number
  public readonly code?: number
  public readonly detail?: unknown

  constructor(message: string, status: number, code?: number, detail?: unknown) {
    super(message)
    this.name = 'ApiClientError'
    this.status = status
    this.code = code
    this.detail = detail
  }
}

function buildUrl(path: string, query?: QueryParams): string {
  const normalizedPath = path.startsWith('/') ? path : `/${path}`
  // 支持空 base（相对路径）：new URL('/api/...', origin) 可正确解析相对 URL
  const locationBase = typeof window !== 'undefined' ? window.location.origin : 'http://localhost'
  const url = new URL(`${API_BASE_URL}${normalizedPath}`, locationBase)

  if (!query) {
    return url.toString()
  }

  Object.entries(query).forEach(([key, value]) => {
    if (value === undefined || value === null || value === '') {
      return
    }

    url.searchParams.set(key, String(value))
  })

  return url.toString()
}

function normalizeMessage(status: number, payload?: Partial<ApiResponseEnvelope<unknown>>): string {
  if (payload?.message && payload.message.trim().length > 0) {
    return payload.message
  }

  return `请求失败（HTTP ${status}）`
}

function hasHeader(headers: Record<string, string>, headerName: string): boolean {
  const target = headerName.toLowerCase()
  return Object.keys(headers).some((key) => key.toLowerCase() === target)
}

export async function request<TResponse, TBody = unknown>(options: RequestOptions<TBody>): Promise<TResponse> {
  const {
    path,
    method = 'GET',
    query,
    body,
    headers,
    signal,
  } = options

  const url = buildUrl(path, query)
  const requestHeaders: Record<string, string> = {
    ...(headers ?? {}),
  }

  if (body !== undefined && !hasHeader(requestHeaders, 'Content-Type')) {
    requestHeaders['Content-Type'] = 'application/json'
  }

  const accessToken = await tokenService.ensureFresh()
  if (accessToken && !hasHeader(requestHeaders, 'Authorization')) {
    requestHeaders.Authorization = `Bearer ${accessToken}`
  }

  const response = await fetch(url, {
    method,
    signal,
    headers: requestHeaders,
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  const rawText = await response.text()
  const hasBody = rawText.trim().length > 0

  let payload: ApiResponseEnvelope<TResponse> | undefined
  if (hasBody) {
    try {
      payload = JSON.parse(rawText) as ApiResponseEnvelope<TResponse>
    } catch {
      throw new ApiClientError(`响应解析失败（HTTP ${response.status}）`, response.status, undefined, rawText)
    }
  }

  // 304 Not Modified: return null — caller should treat as "no change"
  if (response.status === 304) {
    return null as unknown as TResponse
  }

  if (!response.ok || (payload && !payload.success)) {
    throw new ApiClientError(
      normalizeMessage(response.status, payload),
      response.status,
      payload?.code,
      payload,
    )
  }

  if (!payload) {
    throw new ApiClientError('响应为空', response.status)
  }

  return payload.data
}

export const httpClient = {
  get<TResponse>(path: string, query?: QueryParams, signal?: AbortSignal) {
    return request<TResponse>({ path, method: 'GET', query, signal })
  },
  post<TResponse, TBody = unknown>(path: string, body?: TBody, signal?: AbortSignal) {
    return request<TResponse, TBody>({ path, method: 'POST', body, signal })
  },
  put<TResponse, TBody = unknown>(path: string, body?: TBody, signal?: AbortSignal) {
    return request<TResponse, TBody>({ path, method: 'PUT', body, signal })
  },
  patch<TResponse, TBody = unknown>(path: string, body?: TBody, signal?: AbortSignal) {
    return request<TResponse, TBody>({ path, method: 'PATCH', body, signal })
  },
  delete<TResponse>(path: string, query?: QueryParams, signal?: AbortSignal) {
    return request<TResponse>({ path, method: 'DELETE', query, signal })
  },
}


