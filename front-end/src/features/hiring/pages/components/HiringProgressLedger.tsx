import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import clsx from 'clsx'

import {
  HiringCollectionStage,
} from '@/infra/api'

import type {
  HiringActionVm,
  HiringStageCardVm,
  HiringStageTodoVm,
} from '../hiringWorkflowViewModel'

type SummaryItem = {
  label: string
  value: string
}

type HiringProgressLedgerProps = {
  stageCards: HiringStageCardVm[]
  overallProgress: number
  actionState: HiringActionVm
  instanceCreated: boolean
  createdId: string
  summaryItems: SummaryItem[]
  artifactFileNames: string[]
  hasArtifactArchive: boolean
  onContinue: () => void
  onFinalize: () => void
  onEnterTraining: (employeeId: string) => void
  onDownloadArtifact: (artifactName: string) => void
  onDownloadArchive: () => void
}

export function HiringProgressLedger({
  stageCards,
  overallProgress,
  actionState,
  instanceCreated,
  createdId,
  summaryItems,
  artifactFileNames,
  hasArtifactArchive,
  onContinue,
  onFinalize,
  onEnterTraining,
  onDownloadArtifact,
  onDownloadArchive,
}: HiringProgressLedgerProps) {
  const { t } = useTranslation()
  const previewArtifactNames = artifactFileNames.slice(0, 6)

  return (
    <div className="hb-hiring-side">
      <div className="hb-hiring-panel-head is-ledger">
        <div>
          <p className="hb-hiring-eyebrow">{t('hiring.ledger.eyebrow')}</p>
          <h3 className="hb-hiring-panel-title">{t('hiring.ledger.title')}</h3>
        </div>
        <div className="hb-hiring-score-card">
          <span>{overallProgress}/{stageCards.length}</span>
          <small>{t('hiring.ledger.stageComplete')}</small>
        </div>
      </div>

      <div className="hb-hiring-side-body">
        {stageCards.map((item) => (
          <TodoItem
            key={item.stage}
            title={item.title}
            description={item.description}
            subtask={item.subtask}
            detail={item.detail}
            status={item.status}
            notes={item.notes}
            todoItems={item.todoItems}
          >
            {item.stage === HiringCollectionStage.ReadyForPackaging && !instanceCreated ? (
              <button
                type="button"
                className={clsx('hb-hiring-card-action', actionState.canFinalize ? 'primary' : 'ghost')}
                onClick={actionState.canFinalize ? onFinalize : onContinue}
              >
                {actionState.canFinalize ? actionState.finalizeLabel : t('hiring.ledger.continueFill')}
              </button>
            ) : null}
            {item.stage === HiringCollectionStage.ReadyForPackaging && instanceCreated && createdId ? (
              <button
                type="button"
                className="hb-hiring-card-action primary"
                onClick={() => onEnterTraining(createdId)}
              >
                {t('hiring.ledger.enterTraining')}
              </button>
            ) : null}
          </TodoItem>
        ))}

        {summaryItems.length > 0 ? (
          <div className="hb-hiring-summary-card">
            {summaryItems.map((item) => (
              <div key={item.label} className="hb-hiring-summary-row">
                <span>{item.label}</span>
                <strong>{item.value}</strong>
              </div>
            ))}
          </div>
        ) : null}

        {previewArtifactNames.length > 0 ? (
          <div className="hb-hiring-summary-card">
            <div className="hb-hiring-artifact-list">
              {previewArtifactNames.map((fileName) => (
                <button
                  key={fileName}
                  type="button"
                  className="hb-hiring-artifact-btn"
                  onClick={() => onDownloadArtifact(fileName)}
                >
                  {fileName.split('/').pop() || fileName}
                </button>
              ))}
              {hasArtifactArchive ? (
                <button
                  type="button"
                  className="hb-hiring-artifact-btn is-primary"
                  onClick={onDownloadArchive}
                >
                  {t('hiring.ledger.downloadArchive')}
                </button>
              ) : null}
            </div>
          </div>
        ) : null}
      </div>
    </div>
  )
}

type TodoItemProps = {
  title: string
  description: string
  subtask: string
  detail: string
  status: 'complete' | 'active' | 'pending'
  notes: string[]
  todoItems: HiringStageTodoVm[]
  children?: ReactNode
}

function TodoItem({
  title,
  description,
  subtask,
  detail,
  status,
  notes,
  todoItems,
  children,
}: TodoItemProps) {
  const { t } = useTranslation()
  const statusLabel = status === 'complete' ? t('hiring.ledger.todoStatus.completed') : status === 'active' ? t('hiring.ledger.todoStatus.active') : t('hiring.ledger.todoStatus.pending')
  const statusIcon = status === 'complete' ? '✓' : status === 'active' ? '●' : '○'

  return (
    <div className="hb-hiring-todo-item">
      <div className="hb-hiring-todo-head">
        <div>
          <h3>{title}</h3>
          <p>{description}</p>
        </div>
        <div className={`hb-hiring-todo-status is-${status}`}>
          <span className="hb-hiring-todo-status-icon">{statusIcon}</span>
          <span>{statusLabel}</span>
        </div>
      </div>
      <div className={`hb-hiring-subtask-chip is-${status}`}>
        <span>{subtask}</span>
        <strong>{status === 'complete' ? t('hiring.ledger.subtaskComplete', { detail }) : t('hiring.ledger.subtaskPending', { detail })}</strong>
      </div>
      {notes.length > 0 ? (
        <div className="hb-hiring-todo-notes">
          {notes.map((note) => (
            <p key={note}>{note}</p>
          ))}
        </div>
      ) : null}
      {todoItems.length > 0 ? (
        <div className="hb-hiring-stage-todo-list">
          {todoItems.map((todo) => (
            <WorkflowTodoRow key={todo.id} todo={todo} />
          ))}
        </div>
      ) : null}
      {children ? <div className="hb-hiring-todo-footer">{children}</div> : null}
    </div>
  )
}

function WorkflowTodoRow({ todo }: { todo: HiringStageTodoVm }) {
  const { t } = useTranslation()

  return (
    <div className={clsx('hb-hiring-stage-todo-row', todo.isFallback && 'is-fallback')}>
      <div className="hb-hiring-stage-todo-row-head">
        <strong>{todo.title}</strong>
        <div className="hb-hiring-stage-todo-row-tags">
          <span className={clsx('hb-hiring-stage-todo-pill', `is-${statusTone(todo.status)}`)}>
            {getWorkflowTodoStatusLabel(todo.status, t)}
          </span>
          <span className={clsx('hb-hiring-stage-todo-pill', todo.isFallback ? 'is-fallback' : 'is-structured')}>
            {todo.sourceLabel}
          </span>
        </div>
      </div>
      {todo.summary ? <p>{todo.summary}</p> : null}
      {todo.detail ? <small>{todo.detail}</small> : null}
    </div>
  )
}

function getWorkflowTodoStatusLabel(status: string, t: (key: string) => string) {
  if (status === 'done' || status === 'resolved') {
    return t('hiring.ledger.todoStatus.workflowDone')
  }

  if (status === 'in_progress') {
    return t('hiring.ledger.todoStatus.workflowProgress')
  }

  if (status === 'needs_review') {
    return t('hiring.ledger.todoStatus.workflowNeedsReview')
  }

  return t('hiring.ledger.todoStatus.workflowPending')
}

function statusTone(status: string) {
  if (status === 'done' || status === 'resolved') {
    return 'complete'
  }

  if (status === 'in_progress') {
    return 'active'
  }

  return 'pending'
}
