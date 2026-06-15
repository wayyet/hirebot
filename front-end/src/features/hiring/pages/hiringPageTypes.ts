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

export interface MaterialRequestedCategory {
  title: string
  description?: string
  examples?: string[]
}

/** 对应 WS type="skill_stage_gate" 消息 */
export interface StageGateData {
  skillName: string
  completedStage: string
  nextStage: string
  canProceed: boolean
  blockedReason?: string
}

export type DownstreamRunKey =
  | 'material-handoff'
  | 'ontology-slice-extraction'
  | 'skill-definition-entry'
  | 'ontology-projection'
  | 'skill-generation'
  | 'external-system-entry'
  | 'packaging-test-cases'

export type DownstreamRunStatus = 'idle' | 'waiting_confirm' | 'running' | 'completed' | 'failed'

export interface DefinedSkillItem {
  skillName: string
  generationAction?: string
  description?: string
  expectedOutput?: string
  triggers: string[]
  capabilities: string[]
}

export interface DownstreamRunState {
  key: DownstreamRunKey
  status: DownstreamRunStatus
  artifactType: string
  label?: string
  displayHint?: string
  updatedAt: string
  data?: unknown
}

export type DownstreamRunsSnapshot = Partial<Record<DownstreamRunKey, DownstreamRunState>>

/** MCP 工具单次调用状态 */
type ToolStepStatus = 'running' | 'done' | 'error'

/** 单次 MCP 工具调用的展示项，渲染于 bot 消息上方的折叠面板 */
export interface ToolStep {
  /** 本地生成的稳定 ID，用于 React key */
  id: string
  /** 工具名（已剥离 streaming. 前缀） */
  name: string
  /** 输入参数（JSON 字符串），可缺省 */
  args?: string
  /** 工具返回（截断/原文皆可），仅在完成时填充 */
  result?: string
  status: ToolStepStatus
}

export interface ChatMessage {
  id: string
  role: 'bot' | 'user' | 'artifact' | 'stage_gate'
  content: string
  files?: ChatFile[]
  artifact?: ArtifactDisplayData
  stageGate?: StageGateData
  /** 本轮 AI 回复期间产生的 MCP 工具调用步骤（仅 role=bot 携带） */
  toolSteps?: ToolStep[]
}

export interface SkillUploadPayload {
  file: File
  name: string
  releaseNote: string
  description: string
}
