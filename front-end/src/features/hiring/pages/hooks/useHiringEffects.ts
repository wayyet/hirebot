import { useEffect, useRef, type RefObject } from 'react'
import { api, type EmployeeTemplateDetail } from '@/infra/api'
import type { PersistedChatFile, PersistedPackageStructure } from '@/infra/api'
import type { ChatFile, ChatMessage, DownstreamRunsSnapshot, ToolStep } from '../hiringPageTypes'
import type { HiringUiStage } from '../hiringWorkflowViewModel'
import type { GatewayWs } from '@/infra/sandbox/gateway-ws'

/**
 * 滚动到聊天底部
 */
export function useScrollToBottom(
  chatEndRef: RefObject<HTMLDivElement>,
  messages: ChatMessage[],
  visibleTyping: string | null,
  visibleStreamingContent: string | null,
  streamingToolSteps: ToolStep[],
) {
  useEffect(() => {
    const frame = window.requestAnimationFrame(() => {
      chatEndRef.current?.scrollIntoView({ behavior: visibleStreamingContent !== null ? 'auto' : 'smooth' })
    })
    return () => window.cancelAnimationFrame(frame)
  }, [chatEndRef, messages, visibleTyping, visibleStreamingContent, streamingToolSteps])
}

/**
 * 页面挂载时添加 body class，卸载时清理（含 WebSocket 断开）
 */
export function useBodyClassAndCleanup(
  wsRef: RefObject<GatewayWs | null>,
) {
  useEffect(() => {
    document.body.classList.add('hb-body-hiring-prototype')
    return () => {
      document.body.classList.remove('hb-body-hiring-prototype')
      // 离开页面时断开沙箱 WebSocket
      wsRef.current?.disconnect()
      wsRef.current = null
    }
  }, [wsRef])
}

/**
 * 加载模板详情
 */
export function useTemplateDetail(
  templateId: string | undefined,
  setTemplate: (template: EmployeeTemplateDetail | null) => void,
  setTemplateLoading: (loading: boolean) => void,
  setTemplateError: (error: string) => void,
  t: (key: string) => string,
  normalizeErrorMessage: (error: unknown) => string,
) {
  useEffect(() => {
    if (!templateId) {
      setTemplate(null)
      setTemplateLoading(false)
      setTemplateError(t('hiring.error.templateParamMissing'))
      return
    }

    let mounted = true
    setTemplateLoading(true)
    setTemplateError('')
    api.employeeTemplate.getDetail(templateId)
      .then((detail) => {
        if (mounted) {
          setTemplate(detail)
        }
      })
      .catch((error: unknown) => {
        if (mounted) {
          setTemplate(null)
          setTemplateError(normalizeErrorMessage(error))
        }
      })
      .finally(() => {
        if (mounted) {
          setTemplateLoading(false)
        }
      })

    return () => {
      mounted = false
    }
  }, [templateId, setTemplate, setTemplateLoading, setTemplateError, t, normalizeErrorMessage])
}

/**
 * 同步 messages 到 ref
 */
export function useSyncMessagesRef(
  messages: ChatMessage[],
  messagesRef: RefObject<ChatMessage[]>,
) {
  useEffect(() => {
    if (messagesRef.current) {
      messagesRef.current = messages
    }
  }, [messages, messagesRef])
}

/**
 * 保存运行时状态到后端（防抖 2 秒）
 * 同时持久化：stageOverrides、downstreamRuns、uploadedFiles、packageStructure
 */
export function useRuntimeStateSync(
  workflowHireId: string,
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
  downstreamRuns: DownstreamRunsSnapshot,
  allFiles: ChatFile[],
  artifactArchive: { fileName: string; blob: Blob } | null,
  artifactFileNames: string[],
  createdId: string,
) {
  useEffect(() => {
    if (!workflowHireId) return

    // 如果没有任何状态需要保存，跳过
    const hasStage = wsStageOverrides.size > 0
    const hasRuns = Object.keys(downstreamRuns).length > 0
    const hasFiles = allFiles.length > 0
    const hasPackage = artifactArchive !== null
    if (!hasStage && !hasRuns && !hasFiles && !hasPackage) return

    const timer = setTimeout(() => {
      // 将 ChatFile[] 转为 PersistedChatFile[]（剥离 rawFile / content）
      const uploadedFiles: PersistedChatFile[] | undefined = hasFiles
        ? allFiles.map(f => ({
            id: f.id,
            name: f.name,
            size: f.size,
            status: f.status,
            type: f.type,
            mimeType: f.mimeType,
            metadata: f.metadata,
          }))
        : undefined

      const packageStructure: PersistedPackageStructure | undefined = hasPackage
        ? { fileName: artifactArchive!.fileName, fileNames: artifactFileNames, employeeId: createdId || undefined }
        : undefined

      const state = {
        stageOverrides: hasStage ? Object.fromEntries(wsStageOverrides) : undefined,
        downstreamRuns: hasRuns ? downstreamRuns : undefined,
        uploadedFiles,
        packageStructure,
      }
      api.hiringWorkflow.saveRuntimeState(workflowHireId, state).catch(() => {})
    }, 2000)

    return () => clearTimeout(timer)
  }, [wsStageOverrides, downstreamRuns, allFiles, artifactArchive, artifactFileNames, createdId, workflowHireId])
}

/**
 * 自动设置焦点阶段
 */
export function useAutoFocusStage(
  journeyGuideVisible: boolean,
  focusedStage: HiringUiStage | null,
  workflowCurrentStage: HiringUiStage,
  setFocusedStage: (stage: HiringUiStage) => void,
) {
  useEffect(() => {
    if (journeyGuideVisible && !focusedStage) {
      setFocusedStage(workflowCurrentStage)
    }
  }, [focusedStage, journeyGuideVisible, workflowCurrentStage, setFocusedStage])
}
