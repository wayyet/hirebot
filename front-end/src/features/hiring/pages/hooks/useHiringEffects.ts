import { useEffect, useRef, type RefObject } from 'react'
import { api, type EmployeeTemplateDetail } from '@/infra/api'
import type { ChatMessage, DownstreamRunsSnapshot, ToolStep } from '../hiringPageTypes'
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
 */
export function useRuntimeStateSync(
  workflowHireId: string,
  wsStageOverrides: Map<HiringUiStage, 'running' | 'completed' | 'failed'>,
  downstreamRuns: DownstreamRunsSnapshot,
) {
  useEffect(() => {
    if (!workflowHireId) return
    
    // 如果没有任何状态需要保存，跳过
    if (wsStageOverrides.size === 0 && Object.keys(downstreamRuns).length === 0) {
      return
    }

    const timer = setTimeout(() => {
      const state = {
        stageOverrides: wsStageOverrides.size > 0 ? Object.fromEntries(wsStageOverrides) : undefined,
        downstreamRuns: Object.keys(downstreamRuns).length > 0 ? downstreamRuns : undefined,
      }
      api.hiringWorkflow.saveRuntimeState(workflowHireId, state).catch(() => {})
    }, 2000)
    
    return () => clearTimeout(timer)
  }, [wsStageOverrides, downstreamRuns, workflowHireId])
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
