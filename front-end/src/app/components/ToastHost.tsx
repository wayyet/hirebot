export type OverlayToastKind = 'success' | 'info' | 'error'

export interface OverlayToast {
  id: string
  message: string
  kind: OverlayToastKind
}

export default function ToastHost({ toasts }: { toasts: OverlayToast[] }) {
  if (toasts.length === 0) {
    return null
  }

  return (
    <div className="hb-toast-wrap" aria-live="polite">
      {toasts.map((toast) => (
        <div key={toast.id} className={`hb-toast ${toast.kind}`}>
          {toast.message}
        </div>
      ))}
    </div>
  )
}
