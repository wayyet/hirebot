/* eslint-disable react-refresh/only-export-components */
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react'
import LarkGuideModal, { type LarkGuideEmployee } from '@/app/components/LarkGuideModal'
import ToastHost, { type OverlayToast, type OverlayToastKind } from '@/app/components/ToastHost'

interface UxOverlayContextValue {
  showToast: (message: string, kind?: OverlayToastKind) => void
  openLarkGuide: (employee: LarkGuideEmployee) => void
  closeLarkGuide: () => void
}

const UxOverlayContext = createContext<UxOverlayContextValue | null>(null)

function makeId() {
  return `${Date.now()}_${Math.random().toString(36).slice(2)}`
}

export function UxOverlayProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<OverlayToast[]>([])
  const [larkEmployee, setLarkEmployee] = useState<LarkGuideEmployee | null>(null)
  const [larkOpen, setLarkOpen] = useState(false)

  const showToast = useCallback((message: string, kind: OverlayToastKind = 'success') => {
    const id = makeId()
    setToasts((previous) => [...previous, { id, message, kind }])
    window.setTimeout(() => {
      setToasts((previous) => previous.filter((item) => item.id !== id))
    }, 2200)
  }, [])

  const openLarkGuide = useCallback((employee: LarkGuideEmployee) => {
    setLarkEmployee(employee)
    setLarkOpen(true)
  }, [])

  const closeLarkGuide = useCallback(() => {
    setLarkOpen(false)
  }, [])

  const value = useMemo<UxOverlayContextValue>(() => {
    return {
      showToast,
      openLarkGuide,
      closeLarkGuide,
    }
  }, [closeLarkGuide, openLarkGuide, showToast])

  return (
    <UxOverlayContext.Provider value={value}>
      {children}
      <ToastHost toasts={toasts} />
      <LarkGuideModal
        open={larkOpen}
        employee={larkEmployee}
        onClose={closeLarkGuide}
        onConfirm={() => {
          const targetName = larkEmployee?.name || '数字员工'
          const clipboardTask = navigator.clipboard?.writeText(targetName)
          if (clipboardTask) {
            void clipboardTask.catch(() => undefined)
          }
          showToast(`已复制「${targetName}」，请在飞书搜索并开始一对一私聊`, 'success')
          closeLarkGuide()
        }}
      />
    </UxOverlayContext.Provider>
  )
}

export function useUxOverlay() {
  const context = useContext(UxOverlayContext)
  if (!context) {
    throw new Error('useUxOverlay must be used inside UxOverlayProvider')
  }
  return context
}
