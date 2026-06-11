/**
 * HiringTodoPanel — 雇佣 TODO 交互面板（artifact 驱动版）
 *
 * 设计要点：
 * - 不再依赖 HandoffItem 列表，3 个阶段卡片始终常驻渲染。
 * - 阶段亮灯/完成态完全由 `wsStageOverrides`（artifact / skill_stage_gate WS 事件聚合）控制。
 * - 资料卡：仅接受 .md / .json 的文件夹/文件上传，落盘到 wwwroot/resources/todo-files/{sessionId}/{folder?}/。
 * - 技能卡：调用内部 Skills Catalog 搜索并关联，外部系统配置作为可选项。
 * - 资料/外部阶段在用户完成一次上传或保存后，先进入待确认，再由用户明确选择继续补充或推进阶段。
 * - 含 200% 缩放（Surface Pro 8 类窄屏）适配。
 */
import { useCallback, useMemo, useState } from 'react'
import clsx from 'clsx'
import { ArrowRight } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import i18n from '@/i18n'

import { HiringCollectionStage } from '@/infra/api'
import type {
  EmployeeTemplatePackageSkill,
  HiringCollectionStageType,
  HiringExternalSystemConfig,
} from '@/infra/api'
import type {
  ChatFile,
  DefinedSkillItem,
  DownstreamRunState,
  MaterialRequestedCategory,
} from '../hiringPageTypes'
import type { ExternalConfigChangeSource } from '../externalPackagingState'
import type {
  PendingStageAdvanceConfirmation,
  StageAdvanceIntent,
} from '../stageAdvanceConfirmation'
import { ExternalCardBody } from './ExternalSystemConfig'
import { MaterialCardBody } from './MaterialCard'
import { SkillCardBody } from './SkillCard'

// ── 类型 ──────────────────────────────────────────────────────────────────────

export type StageStatus = 'running' | 'completed' | 'failed'
export type StageKey = HiringCollectionStageType

export interface HiringTodoPanelProps {
  hireId: string
  /** 当前雇佣会话 ID（用于上传/解析 todo 文件） */
  sessionId: string
  /** WS 阶段覆盖状态：由 HiringPage 聚合 artifact / skill_stage_gate 事件得到 */
  wsStageOverrides: Map<StageKey, StageStatus>
  /** 子卡片产出阶段摘要后回调；资料/外部阶段会先等待用户确认是否推进 */
  onAfterStageMessage?: (stage: StageKey, summary: string, intent?: StageAdvanceIntent) => void
  /** 触发生成数字员工 */
  onGenerate?: () => void
  generated?: boolean
  /** 生成完成后可下载最终数字员工包 */
  canDownloadFinalPackage?: boolean
  /** 下载最终数字员工包 */
  onDownloadFinalPackage?: () => void
  /** 生成完成后跳转 AI 评估页 */
  onEnterEvaluation?: () => void
  /** 已生成的数字员工包结构（刷新后从后端恢复，无 blob）*/
  packageStructure?: { fileName: string; fileNames: string[] } | null
  /** 用户关联的 store skill UUID 列表变化时回调；用于在导入数字员工包时一并提交给后端。 */
  onLinkedSkillIdsChange?: (skillIds: string[]) => void
  templateId?: string
  templatePackageSkills?: EmployeeTemplatePackageSkill[]
  requestedMaterialCategories?: MaterialRequestedCategory[]
  uploadedConversationFiles?: ChatFile[]
  skillDefinitionStageStatus?: StageStatus | null
  skillGenerationState?: DownstreamRunState | null
  definedSkills?: DefinedSkillItem[]
  onExternalConfigChange?: (config: HiringExternalSystemConfig | null, source?: ExternalConfigChangeSource) => void
  pendingStageConfirmation?: PendingStageAdvanceConfirmation | null
  onContinueStageCollection?: () => void
  onConfirmStageAdvance?: () => void
  stageConfirmationBusy?: boolean
  /** 技能阶段：确认生成技能 */
  onConfirmSkillGeneration?: () => void
  /** 技能阶段：推进到外部系统 */
  onConfirmSkillStageDone?: () => void
}

interface StageConfig {
  key: StageKey
  num: string
  title: string
  hint: string
}

const STAGES: StageConfig[] = [
  { key: HiringCollectionStage.Material, num: '1', title: i18n.t('hiring.todo.stage.material'), hint: i18n.t('hiring.todo.stage.materialHint') },
  { key: HiringCollectionStage.Skill,    num: '2', title: i18n.t('hiring.todo.stage.skill'), hint: '' },
  { key: HiringCollectionStage.External, num: '3', title: i18n.t('hiring.todo.stage.external'), hint: i18n.t('hiring.todo.stage.externalHint') },
]

// ── 主组件 ────────────────────────────────────────────────────────────────────

export function HiringTodoPanel({
  hireId,
  sessionId,
  wsStageOverrides,
  onAfterStageMessage,
  onGenerate,
  generated = false,
  canDownloadFinalPackage = false,
  onDownloadFinalPackage,
  onEnterEvaluation,
  onLinkedSkillIdsChange,
  packageStructure = null,
  templateId,
  templatePackageSkills = [],
  requestedMaterialCategories = [],
  uploadedConversationFiles = [],
  skillDefinitionStageStatus = null,
  skillGenerationState = null,
  definedSkills = [],
  onExternalConfigChange,
  pendingStageConfirmation = null,
  onContinueStageCollection,
  onConfirmStageAdvance,
  stageConfirmationBusy = false,
  onConfirmSkillGeneration,
  onConfirmSkillStageDone,
}: HiringTodoPanelProps) {
  const { t } = useTranslation()
  // 用户是否手动覆盖了某张卡片的展开状态；未手动覆盖的走"活跃阶段自动展开"逻辑
  const [userToggled, setUserToggled] = useState<Record<string, boolean>>({})
  const [manualExpanded, setManualExpanded] = useState<Record<string, boolean>>({})
  const [toggleScopeKey, setToggleScopeKey] = useState<StageKey | 'final' | null>(null)

  const allDone = useMemo(
    () => STAGES.every(s => wsStageOverrides.get(s.key) === 'completed'),
    [wsStageOverrides],
  )

  // 当前活跃阶段：优先取 running，没有 running 时取第一个未完成的阶段；全部完成则无活跃阶段
  const activeStageKey = useMemo<StageKey | 'final' | null>(() => {
    const running = STAGES.find(s => wsStageOverrides.get(s.key) === 'running')
    if (running) return running.key
    if (allDone) return 'final'
    const pending = STAGES.find(s => wsStageOverrides.get(s.key) !== 'completed')
    return pending?.key ?? null
  }, [wsStageOverrides, allDone])

  const isExternalStageUnlocked = useMemo(
    () => activeStageKey === HiringCollectionStage.External
      || activeStageKey === 'final'
      || wsStageOverrides.has(HiringCollectionStage.External),
    [activeStageKey, wsStageOverrides],
  )

  const scopedUserToggled = useMemo(
    () => (toggleScopeKey === activeStageKey ? userToggled : {}),
    [toggleScopeKey, activeStageKey, userToggled],
  )
  const scopedManualExpanded = useMemo(
    () => (toggleScopeKey === activeStageKey ? manualExpanded : {}),
    [toggleScopeKey, activeStageKey, manualExpanded],
  )

  // 计算每张卡片的展开态：用户手动覆盖优先，否则只展开活跃阶段
  const isExpanded = useCallback((key: string) => {
    if (scopedUserToggled[key]) return scopedManualExpanded[key]
    return key === activeStageKey
  }, [scopedUserToggled, scopedManualExpanded, activeStageKey])

  const toggle = (key: string) => {
    const shouldResetScope = toggleScopeKey !== activeStageKey
    const nextExpanded = !isExpanded(key)

    setToggleScopeKey(activeStageKey)
    setUserToggled(prev => shouldResetScope ? { [key]: true } : { ...prev, [key]: true })
    setManualExpanded(prev => shouldResetScope ? { [key]: nextExpanded } : { ...prev, [key]: nextExpanded })
  }

  return (
    <div className="hb-todo-panel">
      <div className="hb-todo-panel-head hb-todo-panel-head--compact">
        <h3 className="hb-todo-panel-title">{t('hiring.todo.panelTitle')}</h3>
      </div>

      <div className="hb-todo-panel-body">
        <div className="hb-todo-material-section">
          <MaterialCardBody
            hireId={hireId}
            sessionId={sessionId}
            requestedCategories={requestedMaterialCategories}
            uploadedConversationFiles={uploadedConversationFiles}
            pendingConfirmation={pendingStageConfirmation?.stage === HiringCollectionStage.Material ? pendingStageConfirmation : null}
            stageConfirmationBusy={stageConfirmationBusy}
            onContinueCollection={onContinueStageCollection}
            onConfirmAdvance={onConfirmStageAdvance}
            onAfterUpload={summary => onAfterStageMessage?.(HiringCollectionStage.Material, summary, 'ready_to_advance')}
          />
        </div>

        {STAGES.filter(stage => stage.key !== HiringCollectionStage.Material).map(stage => (
          <StageCard
            key={stage.key}
            stage={stage}
            status={wsStageOverrides.get(stage.key) ?? null}
            expanded={isExpanded(stage.key)}
            isFocus={activeStageKey === stage.key}
            onToggle={() => toggle(stage.key)}
          >
            {stage.key === HiringCollectionStage.Skill && (
              <SkillCardBody
                hireId={hireId}
                templateId={templateId}
                templatePackageSkills={templatePackageSkills}
                onAfterLink={summary => onAfterStageMessage?.(HiringCollectionStage.Skill, summary, 'collecting')}
                onLinkedIdsChange={onLinkedSkillIdsChange}
                definitionStageStatus={skillDefinitionStageStatus}
                skillGenerationState={skillGenerationState}
                definedSkills={definedSkills}
                onConfirmSkillGeneration={onConfirmSkillGeneration}
                onConfirmSkillStageDone={onConfirmSkillStageDone}
              />
            )}
            {stage.key === HiringCollectionStage.External && (
              <ExternalCardBody
                hireId={hireId}
                isUnlocked={isExternalStageUnlocked}
                pendingConfirmation={pendingStageConfirmation?.stage === HiringCollectionStage.External ? pendingStageConfirmation : null}
                stageConfirmationBusy={stageConfirmationBusy}
                onContinueCollection={onContinueStageCollection}
                onConfirmAdvance={onConfirmStageAdvance}
                onAfterSave={(summary, intent) => onAfterStageMessage?.(HiringCollectionStage.External, summary, intent)}
                onConfigChange={onExternalConfigChange}
              />
            )}
          </StageCard>
        ))}

        <FinalCard
          canGenerate={allDone}
          generated={generated}
          expanded={isExpanded('final')}
          isFocus={activeStageKey === 'final'}
          onToggle={() => toggle('final')}
          onGenerate={onGenerate}
          onEnterEvaluation={onEnterEvaluation}
          canDownload={canDownloadFinalPackage}
          onDownload={onDownloadFinalPackage}
          packageStructure={packageStructure}
        />
      </div>
    </div>
  )
}

// ── 阶段卡片外壳 ──────────────────────────────────────────────────────────────

function StageCard({
  stage, status, expanded, isFocus, onToggle, children,
}: {
  stage: StageConfig
  status: StageStatus | null
  expanded: boolean
  isFocus: boolean
  onToggle: () => void
  children: React.ReactNode
}) {
  const { t } = useTranslation()
  const isComplete = status === 'completed'
  const isActive = status === 'running'
  const isFailed = status === 'failed'
  return (
    <div className={clsx(
      'hb-todo-stage-card',
      (stage.key === HiringCollectionStage.Skill || stage.key === HiringCollectionStage.External) && 'is-accent-shell',
      isComplete && 'is-complete',
      isActive && 'is-active',
      isFailed && 'is-failed',
      // 当前关注的阶段卡片：占据右侧剩余高度，让里面的上传/搜索/表单区域尽量铺开
      isFocus && expanded && 'is-focus',
    )}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">{stage.num}</span>
        <span className="hb-todo-stage-title">{stage.title}</span>
        <span className={clsx('hb-todo-stage-badge', isComplete ? 'is-complete' : isActive ? 'is-active' : isFailed ? 'is-failed' : '')}>
          {isComplete ? t('hiring.todo.status.completed') : isActive ? t('hiring.todo.status.inProgress') : isFailed ? t('hiring.todo.status.failed') : t('hiring.todo.status.waiting')}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className="hb-todo-stage-body">
          {stage.hint && <p className="hb-todo-stage-hint">{stage.hint}</p>}
          {children}
        </div>
      )}
    </div>
  )
}


function FinalCard({
  canGenerate, generated, expanded, isFocus, onToggle, onGenerate, onEnterEvaluation, canDownload, onDownload, packageStructure,
}: {
  canGenerate: boolean
  generated: boolean
  expanded: boolean
  isFocus: boolean
  onToggle: () => void
  onGenerate?: () => void
  onEnterEvaluation?: () => void
  canDownload?: boolean
  onDownload?: () => void
  packageStructure?: { fileName: string; fileNames: string[] } | null
}) {
  const { t } = useTranslation()
  return (
    <div className={clsx(
      'hb-todo-stage-card',
      'is-accent-shell',
      generated && 'is-complete',
      !generated && canGenerate && 'is-active',
      isFocus && expanded && 'is-focus',
    )}>
      <button type="button" className="hb-todo-stage-head" onClick={onToggle} aria-expanded={expanded}>
        <span className="hb-todo-stage-num">4</span>
        <span className="hb-todo-stage-title">{t('hiring.todo.final.title')}</span>
        <span className={clsx('hb-todo-stage-badge', generated ? 'is-complete' : canGenerate ? 'is-active' : '')}>
          {generated ? t('hiring.todo.final.generatedBadge') : canGenerate ? t('hiring.todo.final.canGenerateBadge') : t('hiring.todo.status.waiting')}
        </span>
        <span className={clsx('hb-todo-stage-chevron', expanded && 'is-open')}>▾</span>
      </button>
      {expanded && (
        <div className="hb-todo-stage-body">
          <p className="hb-todo-stage-hint">
            {t('hiring.todo.final.hint')}
          </p>
          <div className="hb-todo-actions-row">
            <button type="button"
              className={clsx('hb-todo-row-btn', canGenerate && !generated ? 'is-primary' : 'is-ghost')}
              disabled={!canGenerate || generated}
              onClick={onGenerate}>
              {generated ? t('hiring.todo.final.generatedBtn') : t('hiring.todo.final.generateBtn')}
            </button>
            {generated && canDownload && onDownload && (
              <button
                type="button"
                className="hb-todo-row-btn is-primary"
                onClick={onDownload}
              >
                {t('hiring.todo.final.downloadPackageBtn')}
              </button>
            )}
          </div>
          {packageStructure?.fileNames.length ? (
            <div className="hb-todo-package-info">
              <ul className="hb-todo-package-files">
                {packageStructure.fileNames.slice(0, 8).map((name, i) => (
                  <li key={i} className="hb-todo-package-file">{name}</li>
                ))}
                {packageStructure.fileNames.length > 8 && (
                  <li className="hb-todo-package-file is-more">+{packageStructure.fileNames.length - 8} {t('hiring.todo.final.moreFiles')}</li>
                )}
              </ul>
            </div>
          ) : null}
          {(generated || packageStructure) && onEnterEvaluation && (
            <button
              type="button"
              className="hb-todo-row-btn is-primary"
              onClick={onEnterEvaluation}
            >
              <ArrowRight size={14} strokeWidth={2.2} />
              {t('hiring.todo.final.enterEvaluationBtn')}
            </button>
          )}
        </div>
      )}
    </div>
  )
}
