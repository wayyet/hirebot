import type { ReactNode } from 'react'

import clsx from 'clsx'

import {
  HiringCollectionPhase,
  HiringCollectionStage,
  HiringCredentialBindingStatus,
} from '@/infra/api'
import type {
  ConfigGovernanceState,
  CredentialSlot,
  HiringCollectionPhaseType,
} from '@/infra/api'

import type { CredentialDraft } from '../hiringPageTypes'
import type { HiringActionVm, HiringStageCardVm, HiringUiStage } from '../hiringWorkflowViewModel'

type SummaryItem = {
  label: string
  value: string
}

type HiringProgressLedgerProps = {
  stageCards: HiringStageCardVm[]
  overallProgress: number
  currentStage: HiringUiStage
  collectionPhase: HiringCollectionPhaseType
  actionState: HiringActionVm
  instanceCreated: boolean
  createdId: string
  summaryItems: SummaryItem[]
  artifactFileNames: string[]
  hasArtifactArchive: boolean
  credentialSlots: CredentialSlot[]
  credentialDrafts: Record<string, CredentialDraft>
  credentialSubmittingSlot: string | null
  configGovernance: ConfigGovernanceState | null
  configDrafts: Record<string, string>
  configSavingKey: string | null
  onContinue: () => void
  onFinalize: () => void
  onEnterTraining: (employeeId: string) => void
  onDownloadArtifact: (artifactName: string) => void
  onDownloadArchive: () => void
  onCredentialChange: (credentialSlot: string, field: keyof CredentialDraft, value: string) => void
  onCredentialSubmit: (slot: CredentialSlot) => void
  onConfigChange: (configKey: string, value: string) => void
  onConfigSave: (configKey: string) => void
}

export function HiringProgressLedger({
  stageCards,
  overallProgress,
  currentStage,
  collectionPhase,
  actionState,
  instanceCreated,
  createdId,
  summaryItems,
  artifactFileNames,
  hasArtifactArchive,
  credentialSlots,
  credentialDrafts,
  credentialSubmittingSlot,
  configGovernance,
  configDrafts,
  configSavingKey,
  onContinue,
  onFinalize,
  onEnterTraining,
  onDownloadArtifact,
  onDownloadArchive,
  onCredentialChange,
  onCredentialSubmit,
  onConfigChange,
  onConfigSave,
}: HiringProgressLedgerProps) {
  const showExternalControls = currentStage === HiringCollectionStage.External || credentialSlots.length > 0 || Boolean(configGovernance)
  const previewArtifactNames = artifactFileNames.slice(0, 6)

  return (
    <div className="hb-hiring-side">
      <div className="hb-hiring-panel-head is-ledger">
        <div>
          <p className="hb-hiring-eyebrow">PROGRESS LEDGER</p>
          <h3 className="hb-hiring-panel-title">待办事项</h3>
        </div>
        <div className="hb-hiring-score-card">
          <span>{overallProgress}%</span>
          <small>完成度</small>
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
            progress={item.progress}
            notes={item.notes}
          >
            {item.stage === HiringCollectionStage.ReadyForPackaging && !instanceCreated ? (
              <button
                type="button"
                className={clsx('hb-hiring-card-action', actionState.canFinalize ? 'primary' : 'ghost')}
                onClick={actionState.canFinalize ? onFinalize : onContinue}
              >
                {actionState.canFinalize ? actionState.finalizeLabel : '继续补齐'}
              </button>
            ) : null}
            {item.stage === HiringCollectionStage.ReadyForPackaging && instanceCreated && createdId ? (
              <button
                type="button"
                className="hb-hiring-card-action primary"
                onClick={() => onEnterTraining(createdId)}
              >
                进入培训流程
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

        {showExternalControls ? (
          <>
            <CredentialBindingSection
              credentialSlots={credentialSlots}
              credentialDrafts={credentialDrafts}
              credentialSubmittingSlot={credentialSubmittingSlot}
              onCredentialChange={onCredentialChange}
              onCredentialSubmit={onCredentialSubmit}
            />
            <ConfigGovernanceSection
              collectionPhase={collectionPhase}
              configGovernance={configGovernance}
              configDrafts={configDrafts}
              configSavingKey={configSavingKey}
              blockedReason={actionState.blockedReason}
              onConfigChange={onConfigChange}
              onConfigSave={onConfigSave}
            />
          </>
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
                  下载后端交付包
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
  progress: number
  notes: string[]
  children?: ReactNode
}

function TodoItem({
  title,
  description,
  subtask,
  detail,
  status,
  progress,
  notes,
  children,
}: TodoItemProps) {
  const statusLabel = status === 'complete' ? '已完成' : status === 'active' ? '进行中' : '待办'
  const statusIcon = status === 'complete' ? '●' : status === 'active' ? '•' : '○'

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
      <div className="hb-hiring-todo-progress">
        <span style={{ width: `${progress}%` }} />
      </div>
      <div className={`hb-hiring-subtask-chip is-${status}`}>
        <span>{subtask}</span>
        <strong>{status === 'complete' ? `● ${detail}` : `○ ${detail}`}</strong>
      </div>
      {notes.length > 0 ? (
        <div className="hb-hiring-todo-notes">
          {notes.map((note) => (
            <p key={note}>{note}</p>
          ))}
        </div>
      ) : null}
      {children ? <div className="hb-hiring-todo-footer">{children}</div> : null}
    </div>
  )
}

type CredentialBindingSectionProps = {
  credentialSlots: CredentialSlot[]
  credentialDrafts: Record<string, CredentialDraft>
  credentialSubmittingSlot: string | null
  onCredentialChange: (credentialSlot: string, field: keyof CredentialDraft, value: string) => void
  onCredentialSubmit: (slot: CredentialSlot) => void
}

function CredentialBindingSection({
  credentialSlots,
  credentialDrafts,
  credentialSubmittingSlot,
  onCredentialChange,
  onCredentialSubmit,
}: CredentialBindingSectionProps) {
  if (credentialSlots.length === 0) {
    return null
  }

  return (
    <div className="hb-hiring-summary-card">
      <div className="hb-hiring-section-head">
        <div>
          <h4>凭据绑定</h4>
          <p>敏感凭据请在这里填写，不要直接发进聊天框。</p>
        </div>
      </div>
      <div className="hb-hiring-section-stack">
        {credentialSlots.map((slot) => {
          const draft = credentialDrafts[slot.credentialSlot] ?? { secretValue: '', secretRef: '' }
          const isPending = slot.bindingStatus !== HiringCredentialBindingStatus.Bound &&
            slot.bindingStatus !== HiringCredentialBindingStatus.NotRequired
          return (
            <div key={slot.credentialSlot} className="hb-hiring-editor-card">
              <div className="hb-hiring-summary-row">
                <span>{slot.credentialSlot}</span>
                <strong>{renderCredentialStatus(slot.bindingStatus)}</strong>
              </div>
              <div className="hb-hiring-editor-meta">
                <span>{slot.targetSystem || '未指定系统'}</span>
                <span>{slot.authKind || '未指定认证方式'}</span>
              </div>
              <div className="hb-hiring-form-field">
                <label className="hb-hiring-form-label">Secret Value</label>
                <input
                  type="password"
                  className="hb-hiring-form-input"
                  value={draft.secretValue}
                  onChange={(event) => onCredentialChange(slot.credentialSlot, 'secretValue', event.target.value)}
                  placeholder={isPending ? '输入真实凭据' : '留空表示保持不变'}
                />
              </div>
              <div className="hb-hiring-form-field">
                <label className="hb-hiring-form-label">Secret Ref</label>
                <input
                  type="text"
                  className="hb-hiring-form-input"
                  value={draft.secretRef}
                  onChange={(event) => onCredentialChange(slot.credentialSlot, 'secretRef', event.target.value)}
                  placeholder="可选，例如 vault://..."
                />
              </div>
              <button
                type="button"
                className="hb-hiring-inline-btn is-dark"
                disabled={!draft.secretValue.trim() || credentialSubmittingSlot === slot.credentialSlot}
                onClick={() => onCredentialSubmit(slot)}
              >
                {credentialSubmittingSlot === slot.credentialSlot ? '保存中...' : '保存凭据'}
              </button>
            </div>
          )
        })}
      </div>
    </div>
  )
}

type ConfigGovernanceSectionProps = {
  collectionPhase: HiringCollectionPhaseType
  configGovernance: ConfigGovernanceState | null
  configDrafts: Record<string, string>
  configSavingKey: string | null
  blockedReason: string
  onConfigChange: (configKey: string, value: string) => void
  onConfigSave: (configKey: string) => void
}

function ConfigGovernanceSection({
  collectionPhase,
  configGovernance,
  configDrafts,
  configSavingKey,
  blockedReason,
  onConfigChange,
  onConfigSave,
}: ConfigGovernanceSectionProps) {
  const files = configGovernance?.files ?? []
  const pendingReviewCount = configGovernance?.pendingReviewTodoIds.length ?? 0
  if (files.length === 0 && pendingReviewCount === 0) {
    return null
  }

  return (
    <div className="hb-hiring-summary-card">
      <div className="hb-hiring-section-head">
        <div>
          <h4>配置治理</h4>
          <p>配置修改后会触发相关工单重新复核。</p>
        </div>
        {pendingReviewCount > 0 ? (
          <span className="hb-hiring-section-pill is-warning">{pendingReviewCount} 条待复核</span>
        ) : null}
      </div>
      {pendingReviewCount > 0 ? (
        <div className="hb-hiring-config-warning">
          <strong>当前无法生成实例</strong>
          <span>{blockedReason || '请先完成受影响工单的重新确认。'}</span>
        </div>
      ) : collectionPhase === HiringCollectionPhase.ReadyForFinalize ? (
        <div className="hb-hiring-config-warning is-success">
          <strong>配置已稳定</strong>
          <span>当前配置可继续进入交付阶段。</span>
        </div>
      ) : null}
      <div className="hb-hiring-section-stack">
        {files.map((file) => {
          const draftValue = configDrafts[file.configKey] ?? file.content
          const hasChanged = draftValue !== file.content
          return (
            <div key={file.configKey} className="hb-hiring-editor-card">
              <div className="hb-hiring-summary-row">
                <span>{file.displayName}</span>
                <strong>{file.relativePath}</strong>
              </div>
              <p className="hb-hiring-editor-copy">{file.summary}</p>
              <textarea
                rows={6}
                className="hb-hiring-form-textarea"
                value={draftValue}
                onChange={(event) => onConfigChange(file.configKey, event.target.value)}
              />
              <button
                type="button"
                className="hb-hiring-inline-btn is-dark"
                disabled={!hasChanged || configSavingKey === file.configKey}
                onClick={() => onConfigSave(file.configKey)}
              >
                {configSavingKey === file.configKey ? '保存中...' : '保存配置'}
              </button>
            </div>
          )
        })}
      </div>
    </div>
  )
}

function renderCredentialStatus(status: string) {
  if (status === HiringCredentialBindingStatus.Bound) {
    return '已绑定'
  }

  if (status === HiringCredentialBindingStatus.NotRequired) {
    return '不需要'
  }

  if (status === HiringCredentialBindingStatus.Failed) {
    return '绑定失败'
  }

  return '待绑定'
}
