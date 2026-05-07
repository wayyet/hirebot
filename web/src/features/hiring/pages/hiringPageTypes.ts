export interface ChatFile {
  id: string
  name: string
  size: number
  status: '解析中' | '已解析'
  type?: 'file' | 'skill'
  mimeType?: string
  content?: string
  metadata?: Record<string, string>
  rawFile?: File
}

export interface ChatMessage {
  id: string
  role: 'bot' | 'user'
  content: string
  files?: ChatFile[]
}

export interface SkillUploadPayload {
  file: File
  name: string
  releaseNote: string
  description: string
}

export interface CredentialDraft {
  secretValue: string
  secretRef: string
}
