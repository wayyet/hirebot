import type { ReactNode } from 'react'

interface ConfirmationActionPanelProps {
  ariaLabel: string
  message: ReactNode
  primaryLabel: string
  onPrimary?: () => void
  secondaryLabel?: string
  onSecondary?: () => void
  busy?: boolean
}

export function ConfirmationActionPanel({
  ariaLabel,
  message,
  primaryLabel,
  onPrimary,
  secondaryLabel,
  onSecondary,
  busy = false,
}: ConfirmationActionPanelProps) {
  return (
    <section className="hb-todo-confirmation-panel" aria-label={ariaLabel}>
      <p className="hb-todo-confirmation-text">{message}</p>
      <div className="hb-todo-confirmation-actions">
        {secondaryLabel && onSecondary ? (
          <button
            type="button"
            className="hb-todo-row-btn is-ghost"
            disabled={busy}
            onClick={onSecondary}
          >
            {secondaryLabel}
          </button>
        ) : null}
        <button
          type="button"
          className="hb-todo-row-btn is-primary"
          disabled={busy || !onPrimary}
          onClick={onPrimary}
        >
          {primaryLabel}
        </button>
      </div>
    </section>
  )
}
