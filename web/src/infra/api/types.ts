export interface ApiResponseEnvelope<T> {
  code: number
  success: boolean
  message: string
  data: T
}

export type QueryValue = string | number | boolean | null | undefined
export type QueryParams = Record<string, QueryValue>

export interface RequestOptions<TBody = unknown> {
  path: string
  method?: 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'
  query?: QueryParams
  body?: TBody
  headers?: Record<string, string>
  signal?: AbortSignal
}
