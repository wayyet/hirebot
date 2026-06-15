import { useCallback, useEffect, useRef, useState, type RefObject } from 'react'
import { api, HiringCollectionStage, type EmployeeTemplateDetail } from '@/infra/api'
import type { PersistedPackageStructure, RuntimeStateSaveRequest, RuntimeStateStage } from '@/infra/api'
import type { GatewayWs } from '@/infra/sandbox/gateway-ws'
import type { ChatFile, ChatMessage, DownstreamRunsSnapshot, ToolStep } from '../hiringPageTypes'
import type { HiringUiStage } from '../hiringWorkflowViewModel'
import { buildRuntimeStatePayloadByStage, hasRuntimeStatePayloadContent } from '../utils/hiringRuntimeState'

/**
 * 滚动到底部。
 */
export function useScrollToBottom(
  chatScrollRef: RefObject<HTMLDivElement | null>,
  messages: ChatMessage[],
  _visibleTyping: boolean,
  _visibleStreamingContent: string | null,
  _streamingToolSteps: ToolStep[],
) {
  const [showScrollToBottom, setShowScrollToBottom] = useState(false)
  const pinnedToBottomRef = useRef(true)
  const messageCountRef = useRef(messages.length)

  const updatePinnedState = useCallback(() => {
    const scroller = chatScrollRef.current
    if (!scroller) {
      pinnedToBottomRef.current = true
      setShowScrollToBottom(false)
      return true
    }

    const distanceFromBottom = scroller.scrollHeight - scroller.scrollTop - scroller.clientHeight
    const isPinned = distanceFromBottom <= 96
    pinnedToBottomRef.current = isPinned
    setShowScrollToBottom(!isPinned)
    return isPinned
  }, [chatScrollRef])

  const scrollToBottom = useCallback((behavior: ScrollBehavior = 'smooth') => {
    const scroller = chatScrollRef.current
    if (!scroller) return

    scroller.scrollTo({
      top: scroller.scrollHeight,
      behavior,
    })
    pinnedToBottomRef.current = true
    setShowScrollToBottom(false)
  }, [chatScrollRef])

  useEffect(() => {
    const scroller = chatScrollRef.current
    if (!scroller) return

    updatePinnedState()
    scroller.addEventListener('scroll', updatePinnedState, { passive: true })
    const resizeObserver = new ResizeObserver(() => {
      if (pinnedToBottomRef.current) {
        scrollToBottom('auto')
        return
      }

      updatePinnedState()
    })
    resizeObserver.observe(scroller)
    Array.from(scroller.children).forEach(child => resizeObserver.observe(child))

    return () => {
      scroller.removeEventListener('scroll', updatePinnedState)
      resizeObserver.disconnect()
    }
  }, [chatScrollRef, scrollToBottom, updatePinnedState])

  useEffect(() => {
    const previousMessageCount = messageCountRef.current
    messageCountRef.current = messages.length
    if (messages.length <= previousMessageCount) {
      updatePinnedState()
      return
    }

    const latestMessage = messages[messages.length - 1]
    if (latestMessage?.role !== 'user' && !pinnedToBottomRef.current) {
      updatePinnedState()
      return
    }

    const frame = window.requestAnimationFrame(() => scrollToBottom('smooth'))
    return () => window.cancelAnimationFrame(frame)
  }, [messages, scrollToBottom, updatePinnedState])

  return { showScrollToBottom, scrollToBottom }
}

/**
 * 页面挂载时增加 body class，卸载时清理。
 */
export function useBodyClassAndCleanup(
  wsRef: RefObject<GatewayWs | null>,
) {
  useEffect(() => {
    document.body.classList.add('hb-body-hiring-prototype')
    return () => {
      document.body.classList.remove('hb-body-hiring-prototype')
      wsRef.current?.disconnect()
      wsRef.current = null
    }
  }, [wsRef])
}

/**
 * 加载模板详情。
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
 * 同步 messages 到 ref。
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
 * 分阶段保存运行时状态，避免每次都走整包持久化。
 */
export function useRuntimeStateSync(
  workflowHireId: string,
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
  downstreamRuns: DownstreamRunsSnapshot,
  allFiles: ChatFile[],
  packageFileName: string,
  artifactFileNames: string[],
  createdId: string,
  instanceCreated: boolean,
) {
  const normalizedPackageFileName = packageFileName.trim()
  const packageStructure: PersistedPackageStructure | undefined = instanceCreated && normalizedPackageFileName
    ? { fileName: normalizedPackageFileName, fileNames: artifactFileNames, employeeId: createdId || undefined }
    : undefined

  useRuntimeStateStageSync(
    workflowHireId,
    HiringCollectionStage.Material,
    buildRuntimeStatePayloadByStage(HiringCollectionStage.Material, wsStageOverrides, downstreamRuns, allFiles, packageStructure),
  )
  useRuntimeStateStageSync(
    workflowHireId,
    HiringCollectionStage.Skill,
    buildRuntimeStatePayloadByStage(HiringCollectionStage.Skill, wsStageOverrides, downstreamRuns, allFiles, packageStructure),
  )
  useRuntimeStateStageSync(
    workflowHireId,
    HiringCollectionStage.External,
    buildRuntimeStatePayloadByStage(HiringCollectionStage.External, wsStageOverrides, downstreamRuns, allFiles, packageStructure),
  )
  useRuntimeStateStageSync(
    workflowHireId,
    HiringCollectionStage.ReadyForPackaging,
    buildRuntimeStatePayloadByStage(HiringCollectionStage.ReadyForPackaging, wsStageOverrides, downstreamRuns, allFiles, packageStructure),
  )
}

function useRuntimeStateStageSync(
  workflowHireId: string,
  stage: RuntimeStateStage,
  state: RuntimeStateSaveRequest,
) {
  const lastSnapshotRef = useRef<string | null>(null)
  const snapshot = JSON.stringify(state)
  const hasContent = hasRuntimeStatePayloadContent(state)

  useEffect(() => {
    if (!workflowHireId) return

    // 首次空状态不落库，避免在恢复前把服务端已有缓存清掉。
    if (!hasContent && lastSnapshotRef.current === null) return

    const timer = setTimeout(() => {
      api.hiringWorkflow.saveRuntimeStateByStage(workflowHireId, stage, state)
        .then(() => {
          lastSnapshotRef.current = hasContent ? snapshot : null
        })
        .catch(() => {})
    }, 2000)

    return () => clearTimeout(timer)
  }, [hasContent, snapshot, stage, state, workflowHireId])
}

/**
 * 自动设置焦点阶段。
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
