/**
 * useHiringState - 雇佣页面状态管理 Hook
 *
 * 集中管理 HiringPage 的所有状态，包括：
 * - 模板信息
 * - 对话消息和文件
 * - UI 状态（模态框、焦点等）
 * - 工作流状态
 * - Artifact 和阶段相关状态
 * - 下游运行状态
 */
import { useState } from 'react'
import type { EmployeeTemplateDetail } from '@/infra/api'
import type {
  ChatFile,
  ChatMessage,
  DownstreamRunsSnapshot,
  MaterialRequestedCategory,
} from '../hiringPageTypes'
import type { HiringUiStage } from '../hiringWorkflowViewModel'
import type { PendingStageAdvanceConfirmation } from '../stageAdvanceConfirmation'

export interface HiringState {
  // ── 模板相关状态 ────────────────────────────────────────────────────────────
  template: EmployeeTemplateDetail | null
  templateLoading: boolean
  templateError: string

  // ── 对话相关状态 ────────────────────────────────────────────────────────────
  messages: ChatMessage[]
  typing: boolean
  input: string
  pendingFiles: ChatFile[]
  allFiles: ChatFile[]

  // ── UI 状态 ─────────────────────────────────────────────────────────────────
  showSkillUploadModal: boolean
  journeyGuideVisible: boolean
  focusedStage: HiringUiStage | null

  // ── 实例/工作流状态 ─────────────────────────────────────────────────────────
  instanceCreated: boolean
  createdId: string
  workflowHireId: string
  workflowBooting: boolean
  workflowError: string
  workflowNotice: string
  workflowInitAttempted: boolean

  // ── Artifact 相关状态 ───────────────────────────────────────────────────────
  artifactArchive: { fileName: string; blob: Blob } | null
  artifactFileNames: string[]
  /** 从后端恢复的产物包文件名（无 blob 时仅用于显示）*/
  restoredPackageFileName: string
  materialRequestedCategories: MaterialRequestedCategory[]
  pendingPackageArtifact: { fileUrl: string; fileName: string } | null
  pendingStageConfirmation: PendingStageAdvanceConfirmation | null
  requiresFreshPackaging: boolean

  // ── 技能相关状态 ────────────────────────────────────────────────────────────
  linkedStoreSkillIds: string[]

  // ── 提交/流式状态 ──────────────────────────────────────────────────────────
  submittingMessage: boolean
  streamingTurnInternal: boolean

  // ── 重置状态 ───────────────────────────────────────────────────────────────
  resetting: boolean

  // ── 阶段覆盖和下游运行 ─────────────────────────────────────────────────────
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>
  downstreamRuns: DownstreamRunsSnapshot
}

export interface HiringStateActions {
  // ── 模板相关操作 ────────────────────────────────────────────────────────────
  setTemplate: (template: EmployeeTemplateDetail | null) => void
  setTemplateLoading: (loading: boolean) => void
  setTemplateError: (error: string) => void

  // ── 对话相关操作 ────────────────────────────────────────────────────────────
  setMessages: (messages: ChatMessage[] | ((prev: ChatMessage[]) => ChatMessage[])) => void
  setTyping: (typing: boolean) => void
  setInput: (input: string) => void
  setPendingFiles: (files: ChatFile[] | ((prev: ChatFile[]) => ChatFile[])) => void
  setAllFiles: (files: ChatFile[] | ((prev: ChatFile[]) => ChatFile[])) => void

  // ── UI 状态操作 ─────────────────────────────────────────────────────────────
  setShowSkillUploadModal: (show: boolean) => void
  setJourneyGuideVisible: (visible: boolean) => void
  setFocusedStage: (stage: HiringUiStage | null) => void

  // ── 实例/工作流操作 ─────────────────────────────────────────────────────────
  setInstanceCreated: (created: boolean) => void
  setCreatedId: (id: string) => void
  setWorkflowHireId: (id: string) => void
  setWorkflowBooting: (booting: boolean) => void
  setWorkflowError: (error: string) => void
  setWorkflowNotice: (notice: string | ((prev: string) => string)) => void
  setWorkflowInitAttempted: (attempted: boolean) => void

  // ── Artifact 相关操作 ───────────────────────────────────────────────────────
  setArtifactArchive: (archive: { fileName: string; blob: Blob } | null) => void
  setArtifactFileNames: (names: string[]) => void
  /** 设置从后端恢复的产物包文件名 */
  setRestoredPackageFileName: (name: string) => void
  setMaterialRequestedCategories: (categories: MaterialRequestedCategory[]) => void
  setPendingPackageArtifact: (artifact: { fileUrl: string; fileName: string } | null) => void
  setPendingStageConfirmation: (
    confirmation: PendingStageAdvanceConfirmation | null | ((prev: PendingStageAdvanceConfirmation | null) => PendingStageAdvanceConfirmation | null)
  ) => void
  setRequiresFreshPackaging: (requires: boolean) => void

  // ── 技能相关操作 ────────────────────────────────────────────────────────────
  setLinkedStoreSkillIds: (ids: string[] | ((prev: string[]) => string[])) => void

  // ── 提交/流式操作 ──────────────────────────────────────────────────────────
  setSubmittingMessage: (submitting: boolean) => void
  setStreamingTurnInternal: (streaming: boolean) => void

  // ── 重置操作 ───────────────────────────────────────────────────────────────
  setResetting: (resetting: boolean) => void

  // ── 阶段覆盖和下游运行操作 ─────────────────────────────────────────────────
  setWsStageOverrides: (
    overrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'> | ((prev: Map<HiringUiStage, 'running' | 'completed' | 'failed'>) => Map<HiringUiStage, 'running' | 'completed' | 'failed'>)
  ) => void
  setDownstreamRuns: (
    runs: DownstreamRunsSnapshot | ((prev: DownstreamRunsSnapshot) => DownstreamRunsSnapshot)
  ) => void
}

/**
 * 雇佣页面状态管理 Hook
 * 
 * 返回状态对象和操作方法，所有状态集中管理
 */
export function useHiringState(): [HiringState, HiringStateActions] {
  // ── 模板相关状态 ────────────────────────────────────────────────────────────
  const [template, setTemplate] = useState<EmployeeTemplateDetail | null>(null)
  const [templateLoading, setTemplateLoading] = useState(true)
  const [templateError, setTemplateError] = useState('')

  // ── 对话相关状态 ────────────────────────────────────────────────────────────
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [typing, setTyping] = useState(false)
  const [input, setInput] = useState('')
  const [pendingFiles, setPendingFiles] = useState<ChatFile[]>([])
  const [allFiles, setAllFiles] = useState<ChatFile[]>([])

  // ── UI 状态 ─────────────────────────────────────────────────────────────────
  const [showSkillUploadModal, setShowSkillUploadModal] = useState(false)
  const [journeyGuideVisible, setJourneyGuideVisible] = useState(false)
  const [focusedStage, setFocusedStage] = useState<HiringUiStage | null>(null)

  // ── 实例/工作流状态 ─────────────────────────────────────────────────────────
  const [instanceCreated, setInstanceCreated] = useState(false)
  const [createdId, setCreatedId] = useState('')
  const [workflowHireId, setWorkflowHireId] = useState('')
  const [workflowBooting, setWorkflowBooting] = useState(false)
  const [workflowError, setWorkflowError] = useState('')
  const [workflowNotice, setWorkflowNotice] = useState('')
  const [workflowInitAttempted, setWorkflowInitAttempted] = useState(false)

  // ── Artifact 相关状态 ───────────────────────────────────────────────────────
  const [artifactArchive, setArtifactArchive] = useState<{ fileName: string; blob: Blob } | null>(null)
  const [artifactFileNames, setArtifactFileNames] = useState<string[]>([])
  const [restoredPackageFileName, setRestoredPackageFileName] = useState('')
  const [materialRequestedCategories, setMaterialRequestedCategories] = useState<MaterialRequestedCategory[]>([])
  const [pendingPackageArtifact, setPendingPackageArtifact] = useState<{ fileUrl: string; fileName: string } | null>(null)
  const [pendingStageConfirmation, setPendingStageConfirmation] = useState<PendingStageAdvanceConfirmation | null>(null)
  const [requiresFreshPackaging, setRequiresFreshPackaging] = useState(false)

  // ── 技能相关状态 ────────────────────────────────────────────────────────────
  const [linkedStoreSkillIds, setLinkedStoreSkillIds] = useState<string[]>([])

  // ── 提交/流式状态 ──────────────────────────────────────────────────────────
  const [submittingMessage, setSubmittingMessage] = useState(false)
  const [streamingTurnInternal, setStreamingTurnInternal] = useState(false)

  // ── 重置状态 ───────────────────────────────────────────────────────────────
  const [resetting, setResetting] = useState(false)

  // ── 阶段覆盖和下游运行 ─────────────────────────────────────────────────────
  const [wsStageOverrides, setWsStageOverrides] = useState<Map<HiringUiStage, 'running' | 'completed' | 'failed'>>(new Map())
  const [downstreamRuns, setDownstreamRuns] = useState<DownstreamRunsSnapshot>({})

  // 组装状态对象
  const state: HiringState = {
    template,
    templateLoading,
    templateError,
    messages,
    typing,
    input,
    pendingFiles,
    allFiles,
    showSkillUploadModal,
    journeyGuideVisible,
    focusedStage,
    instanceCreated,
    createdId,
    workflowHireId,
    workflowBooting,
    workflowError,
    workflowNotice,
    workflowInitAttempted,
    artifactArchive,
    artifactFileNames,
    restoredPackageFileName,
    materialRequestedCategories,
    pendingPackageArtifact,
    pendingStageConfirmation,
    requiresFreshPackaging,
    linkedStoreSkillIds,
    submittingMessage,
    streamingTurnInternal,
    resetting,
    wsStageOverrides,
    downstreamRuns,
  }

  // 组装操作方法
  const actions: HiringStateActions = {
    setTemplate,
    setTemplateLoading,
    setTemplateError,
    setMessages,
    setTyping,
    setInput,
    setPendingFiles,
    setAllFiles,
    setShowSkillUploadModal,
    setJourneyGuideVisible,
    setFocusedStage,
    setInstanceCreated,
    setCreatedId,
    setWorkflowHireId,
    setWorkflowBooting,
    setWorkflowError,
    setWorkflowNotice,
    setWorkflowInitAttempted,
    setArtifactArchive,
    setArtifactFileNames,
    setRestoredPackageFileName,
    setMaterialRequestedCategories,
    setPendingPackageArtifact,
    setPendingStageConfirmation,
    setRequiresFreshPackaging,
    setLinkedStoreSkillIds,
    setSubmittingMessage,
    setStreamingTurnInternal,
    setResetting,
    setWsStageOverrides,
    setDownstreamRuns,
  }

  return [state, actions]
}
