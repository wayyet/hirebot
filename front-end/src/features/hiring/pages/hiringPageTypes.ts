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

/** 对应 ncrew ArtifactDisplayData，由 WS type="artifact" 消息携带 */
export interface ArtifactDisplayData {
  kind: 'file' | 'data'
  artifactType: string
  label?: string
  skillName?: string
  stage?: string
  isTerminal?: boolean
  // kind = 'file'
  fileUrl?: string
  fileName?: string
  mimeType?: string
  sizeLabel?: string
  // kind = 'data'
  data?: unknown
  /** 对应 contracts/artifacts.json 中声明的 display 字段 */
  displayHint?: 'progress' | 'tree' | 'table' | 'code' | 'badge' | string
}

/** 对应 WS type="skill_stage_gate" 消息 */
export interface StageGateData {
  skillName: string
  completedStage: string
  nextStage: string
  canProceed: boolean
  blockedReason?: string
}

export interface ChatMessage {
  id: string
  role: 'bot' | 'user' | 'artifact' | 'stage_gate'
  content: string
  files?: ChatFile[]
  artifact?: ArtifactDisplayData
  stageGate?: StageGateData
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
