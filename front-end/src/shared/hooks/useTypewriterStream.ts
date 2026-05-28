import { useCallback, useEffect, useRef, useState } from 'react'

type TypewriterDoneCallback = (rawText: string) => void

type TypewriterStreamOptions = {
  tickMs?: number
  reducedMotionQuery?: string
}

type TypewriterFinishOptions = {
  /**
   * 软结束缓冲时间：用于 typing_stop 这类可能早于 assistant_done 的事件。
   */
  deferMs?: number
}

const DEFAULT_TICK_MS = 18
export const TYPEWRITER_SOFT_FINISH_DEFER_MS = 600

function getChunkSize(remaining: number): number {
  if (remaining > 1200) return 48
  if (remaining > 700) return 32
  if (remaining > 360) return 18
  if (remaining > 160) return 9
  if (remaining > 48) return 4
  if (remaining > 16) return 2
  return 1
}

function resolveFinalRaw(currentRaw: string, finalRaw?: string): string {
  if (!finalRaw) return currentRaw
  if (!currentRaw) return finalRaw
  if (finalRaw === currentRaw) return currentRaw
  if (finalRaw.startsWith(currentRaw)) return finalRaw
  if (currentRaw.startsWith(finalRaw)) return currentRaw

  // 后端最终事件可能携带规范化后的完整文本，不一定与前序 delta 严格 prefix 对齐。
  // 这种情况下优先保留更完整的一侧，避免把半截流式缓存固化成正式消息。
  return finalRaw.length >= currentRaw.length ? finalRaw : currentRaw
}

export function useTypewriterStream(options: TypewriterStreamOptions = {}) {
  const tickMs = options.tickMs ?? DEFAULT_TICK_MS
  const reducedMotionQuery = options.reducedMotionQuery ?? '(prefers-reduced-motion: reduce)'

  const rawTextRef = useRef('')
  const displayedLengthRef = useRef(0)
  const displayTextRef = useRef<string | null>(null)
  const timerRef = useRef<ReturnType<typeof window.setTimeout> | null>(null)
  const deferredFinishTimerRef = useRef<ReturnType<typeof window.setTimeout> | null>(null)
  const pumpRef = useRef<() => void>(() => {})
  const activeRef = useRef(false)
  const finishPendingRef = useRef(false)
  const finishCallbackRef = useRef<TypewriterDoneCallback | null>(null)
  const turnIdRef = useRef(0)
  const finishedTurnIdRef = useRef<number | null>(null)
  const reducedMotionRef = useRef(false)

  const [displayText, setDisplayText] = useState<string | null>(null)
  const [isActive, setIsActive] = useState(false)

  const clearTimer = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }, [])

  const clearDeferredFinishTimer = useCallback(() => {
    if (deferredFinishTimerRef.current !== null) {
      window.clearTimeout(deferredFinishTimerRef.current)
      deferredFinishTimerRef.current = null
    }
  }, [])

  const complete = useCallback(() => {
    clearTimer()
    clearDeferredFinishTimer()
    const callback = finishCallbackRef.current
    const rawText = rawTextRef.current

    finishPendingRef.current = false
    finishCallbackRef.current = null
    activeRef.current = false
    finishedTurnIdRef.current = turnIdRef.current
    rawTextRef.current = ''
    displayedLengthRef.current = 0
    displayTextRef.current = null
    setDisplayText(null)
    setIsActive(false)
    callback?.(rawText)
  }, [clearDeferredFinishTimer, clearTimer])

  const pump = useCallback(() => {
    timerRef.current = null

    const rawText = rawTextRef.current
    const remaining = rawText.length - displayedLengthRef.current

    if (remaining > 0) {
      displayedLengthRef.current = Math.min(
        rawText.length,
        displayedLengthRef.current + getChunkSize(remaining),
      )
      const nextDisplayText = rawText.slice(0, displayedLengthRef.current)
      displayTextRef.current = nextDisplayText
      setDisplayText(nextDisplayText)
    }

    if (finishPendingRef.current && displayedLengthRef.current >= rawTextRef.current.length) {
      complete()
      return
    }

    if (activeRef.current && displayedLengthRef.current < rawTextRef.current.length) {
      timerRef.current = window.setTimeout(() => pumpRef.current(), tickMs)
    }
  }, [complete, tickMs])

  useEffect(() => {
    pumpRef.current = pump
  }, [pump])

  const schedulePump = useCallback(() => {
    if (reducedMotionRef.current) {
      displayedLengthRef.current = rawTextRef.current.length
      displayTextRef.current = rawTextRef.current
      setDisplayText(rawTextRef.current)
      if (finishPendingRef.current) {
        complete()
      }
      return
    }

    if (timerRef.current === null) {
      timerRef.current = window.setTimeout(() => pumpRef.current(), tickMs)
    }
  }, [complete, tickMs])

  const start = useCallback(() => {
    clearTimer()
    clearDeferredFinishTimer()
    turnIdRef.current += 1
    finishedTurnIdRef.current = null
    rawTextRef.current = ''
    displayedLengthRef.current = 0
    displayTextRef.current = ''
    activeRef.current = true
    finishPendingRef.current = false
    finishCallbackRef.current = null
    setDisplayText('')
    setIsActive(true)
  }, [clearDeferredFinishTimer, clearTimer])

  const append = useCallback((chunk: string) => {
    if (!chunk) return

    if (!activeRef.current && rawTextRef.current.length === 0) {
      finishedTurnIdRef.current = null
      activeRef.current = true
      displayTextRef.current = ''
      setDisplayText('')
      setIsActive(true)
    }

    rawTextRef.current += chunk
    schedulePump()
  }, [schedulePump])

  const finish = useCallback((
    finalRaw?: string,
    onDone?: TypewriterDoneCallback,
    finishOptions: TypewriterFinishOptions = {},
  ) => {
    if (!activeRef.current && finishedTurnIdRef.current === turnIdRef.current) {
      return
    }

    const resolvedRaw = resolveFinalRaw(rawTextRef.current, finalRaw)
    rawTextRef.current = resolvedRaw
    finishCallbackRef.current = onDone ?? finishCallbackRef.current

    if (!activeRef.current) {
      activeRef.current = true
      setIsActive(true)
      if (displayTextRef.current === null) {
        displayTextRef.current = ''
        setDisplayText('')
      }
    }

    clearDeferredFinishTimer()
    const deferMs = Math.max(0, finishOptions.deferMs ?? 0)
    if (deferMs > 0) {
      finishPendingRef.current = false
      deferredFinishTimerRef.current = window.setTimeout(() => {
        deferredFinishTimerRef.current = null
        finishPendingRef.current = true
        if (displayedLengthRef.current >= rawTextRef.current.length) {
          complete()
          return
        }
        schedulePump()
      }, deferMs)
    } else {
      finishPendingRef.current = true
    }

    if (displayedLengthRef.current >= rawTextRef.current.length) {
      if (finishPendingRef.current) {
        complete()
      }
      return
    }

    schedulePump()
  }, [clearDeferredFinishTimer, complete, schedulePump])

  const reset = useCallback(() => {
    clearTimer()
    clearDeferredFinishTimer()
    rawTextRef.current = ''
    displayedLengthRef.current = 0
    displayTextRef.current = null
    activeRef.current = false
    finishPendingRef.current = false
    finishCallbackRef.current = null
    finishedTurnIdRef.current = turnIdRef.current
    setDisplayText(null)
    setIsActive(false)
  }, [clearDeferredFinishTimer, clearTimer])

  useEffect(() => {
    const mediaQuery = window.matchMedia(reducedMotionQuery)
    const syncReducedMotion = () => {
      reducedMotionRef.current = mediaQuery.matches
      if (mediaQuery.matches && activeRef.current) {
        displayedLengthRef.current = rawTextRef.current.length
        displayTextRef.current = rawTextRef.current
        setDisplayText(rawTextRef.current)
        if (finishPendingRef.current) {
          complete()
        }
      }
    }

    syncReducedMotion()
    mediaQuery.addEventListener('change', syncReducedMotion)
    return () => {
      mediaQuery.removeEventListener('change', syncReducedMotion)
      clearTimer()
      clearDeferredFinishTimer()
    }
  }, [clearDeferredFinishTimer, clearTimer, complete, reducedMotionQuery])

  return {
    displayText,
    isActive,
    rawTextRef,
    start,
    append,
    finish,
    reset,
  }
}
