import {
  HiringCollectionPhase,
  HiringCollectionStage,
  HiringCredentialBindingStatus,
  HiringStageReadinessStatus,
  HiringTodoStatus,
} from '@/infra/api'
import type {
  CredentialSlot,
  DiagnosticTodo,
  DispatchCallback,
  HandoffItem,
  HiringCollectionPhaseType,
  HiringCollectionStageType,
  HiringWorkflowState,
  StageReadiness,
  WorkflowRuntimeFacts,
  WorkflowTodo,
} from '@/infra/api'

export type HiringUiStage = HiringCollectionStageType
export type HiringStepStatus = 'complete' | 'active' | 'pending'

export interface HiringStageStepVm {
  stage: HiringUiStage
  index: number
  title: string
  description: string
  status: HiringStepStatus
  isClickable: boolean
  blockedReason: string
  /** 当前阶段最近一次 dispatch 的执行状态 */
  dispatchStatus: 'running' | 'completed' | 'failed' | null
  /** 最近 dispatch 的用户摘要（completed 时展示） */
  dispatchSummary: string | null
}

export interface HiringStageCardVm {
  stage: HiringUiStage
  title: string
  description: string
  subtask: string
  status: HiringStepStatus
  detail: string
  notes: string[]
  todoItems: HiringStageTodoVm[]
}

export interface HiringStageTodoVm {
  id: string
  title: string
  status: string
  summary: string
  detail: string
  source: string
  sourceLabel: string
  isFallback: boolean
}

export interface HiringGuideVm {
  stage: HiringUiStage
  title: string
  description: string
  bulletTitle: string
  bulletBody: string
  statusText: string
  hints: string[]
}

export interface HiringActionVm {
  canFinalize: boolean
  finalizeLabel: string
  blockedReason: string
}

export interface HiringWorkflowVm {
  uiCurrentStage: HiringUiStage
  stepPills: HiringStageStepVm[]
  stageCards: HiringStageCardVm[]
  guideCard: HiringGuideVm
  actionState: HiringActionVm
  blockedReason: string
  overallProgress: number
  promptPlaceholder: string
  currentStageReason: string
}

type StageConfig = {
  title: string
  description: string
  panelTitle: string
  panelDescription: string
  subtask: string
  pendingLabel: string
  completeLabel: string
  placeholder: string
}

const STAGE_ORDER: HiringUiStage[] = [
  HiringCollectionStage.Material,
  HiringCollectionStage.Skill,
  HiringCollectionStage.External,
  HiringCollectionStage.ReadyForPackaging,
]

const STAGE_CONFIG: Record<HiringUiStage, StageConfig> = {
  [HiringCollectionStage.Material]: {
    title: '业务资料',
    description: '上传、分类与抽取目标',
    panelTitle: '补齐业务资料',
    panelDescription: '至少上传 1 份资料，并为每份资料写清楚后续要抽取什么。',
    subtask: '资料分类',
    pendingLabel: '待上传',
    completeLabel: '已就绪',
    placeholder: '先发最能代表业务规则或真实案例的资料，我会继续追问分类和抽取目标。',
  },
  [HiringCollectionStage.Skill]: {
    title: '技能补齐',
    description: '默认基线与补充能力',
    panelTitle: '确认技能工单',
    panelDescription: '确认模板默认技能基线，为本轮需要新增或调整的能力项给出明确名称与描述。',
    subtask: '技能工单',
    pendingLabel: '待补齐',
    completeLabel: '已确认',
    placeholder: '继续描述还缺哪些技能；如果默认基线已经够用，也可以直接确认推进第三阶段。',
  },
  [HiringCollectionStage.External]: {
    title: '外部连接',
    description: '连接能力与凭据绑定',
    panelTitle: '配置外部连接',
    panelDescription: '按连接能力逐条说明系统、操作、目标与认证方式。',
    subtask: '连接配置',
    pendingLabel: '待配置',
    completeLabel: '已完成',
    placeholder: '补充外部系统、操作目标和认证方式；敏感凭据请走右侧绑定区。',
  },
  [HiringCollectionStage.ReadyForPackaging]: {
    title: '打包准备',
    description: '诊断与复核收口',
    panelTitle: '准备打包交付',
    panelDescription: '业务阶段完成后，这里只处理诊断或复核阻塞项。',
    subtask: '打包确认',
    pendingLabel: '待解锁',
    completeLabel: '可打包',
    placeholder: '如果还有诊断或复核阻塞项，会在这里继续提示你处理。',
  },
}

const EMPTY_RUNTIME_FACTS: WorkflowRuntimeFacts = {
  materialReady: false,
  materialClassifiedFiles: [],
  materialExtractionTargets: {},
  skillBaselineReviewed: false,
  skillBaselineConfirmed: false,
}

function getStageIndex(stage: string | null | undefined) {
  const foundIndex = STAGE_ORDER.findIndex(item => item === stage)
  return foundIndex >= 0 ? foundIndex : 0
}

function getStageReadiness(
  stageReadiness: StageReadiness[] | null | undefined,
  stage: HiringUiStage,
) {
  return stageReadiness?.find(item => item.stage === stage) ?? null
}

function getStageTodos(workflowTodos: WorkflowTodo[] | null | undefined, stage: HiringUiStage) {
  return workflowTodos?.filter(item => item.stage === stage) ?? []
}

function getDiagnosticTodos(
  workflowState: HiringWorkflowState | null,
  stage: HiringUiStage,
) {
  return workflowState?.latestDiagnosticReport?.diagnosticTodos.filter(item => item.stage === stage) ?? []
}

function isFallbackTodoSource(source: string | null | undefined) {
  return typeof source === 'string' && source.startsWith('system:fallback:')
}

/** 取当前阶段最近一次 dispatch（通过 todoIds 与 handoffItems / workflowTodos 关联） */
function getStageLatestDispatch(
  workflowState: HiringWorkflowState | null,
  stage: HiringUiStage,
): DispatchCallback | null {
  const dispatches = workflowState?.latestDispatches
  if (!dispatches?.length) return null

  const stageHandoffIds = new Set(
    (workflowState?.handoffItems ?? [])
      .filter(item => item.stage === stage)
      .map(item => item.handoff_id),
  )
  const stageTodoIds = new Set(
    (workflowState?.workflowTodos ?? [])
      .filter(todo => todo.stage === stage)
      .map(todo => todo.id),
  )

  const stageDispatches = dispatches.filter(dispatch =>
    dispatch.todoIds.some(id => stageHandoffIds.has(id) || stageTodoIds.has(id)),
  )
  return stageDispatches[stageDispatches.length - 1] ?? null
}

function resolveDispatchStatus(
  dispatch: DispatchCallback | null,
): 'running' | 'completed' | 'failed' | null {
  if (!dispatch) return null
  if (dispatch.status === 'completed') return 'completed'
  if (dispatch.status === 'failed' || (dispatch.errors?.length ?? 0) > 0) return 'failed'
  return 'running'
}

function mapHandoffToWorkflowTodo(handoff: HandoffItem): WorkflowTodo {
  const status = mapHandoffStatus(handoff.status)
  return {
    id: handoff.handoff_id,
    title: handoff.title,
    stage: handoff.stage,
    kind: handoff.kind,
    status,
    gapType: null,
    priority: null,
    currentState: handoff.intent ?? null,
    expectedState: null,
    acceptanceCriteria: handoff.acceptance ?? null,
    acceptanceEvidence: null,
    source: handoff.source ?? '',
    fingerprint: handoff.fingerprint ?? null,
    category: handoff.category ?? null,
    payload: handoff.payload ?? null,
    level: null,
    question: null,
    evidence: null,
    suggestedAction: null,
    relatedTodoIds: handoff.related_todos ?? [],
    relatedFiles: handoff.related_files ?? [],
    createdAtUtc: handoff.created_at,
    updatedAtUtc: handoff.updated_at,
  }
}

function mapHandoffStatus(handoffStatus: string): string {
  switch (handoffStatus) {
    case 'confirmed':
      return HiringTodoStatus.Done
    case 'dismissed':
      return HiringTodoStatus.Dismissed
    case 'needs_review':
      return HiringTodoStatus.NeedsReview
    case 'dispatched':
    case 'dirty':
      return HiringTodoStatus.InProgress
    case 'drafting':
    case 'ready_to_dispatch':
    default:
      return HiringTodoStatus.Open
  }
}

function getAllStageTodos(
  workflowState: HiringWorkflowState | null,
  stage: HiringUiStage,
): WorkflowTodo[] {
  const directTodos = getStageTodos(workflowState?.workflowTodos, stage)
  const mappedHandoffTodos = (workflowState?.handoffItems ?? [])
    .filter(item => item.stage === stage)
    .map(mapHandoffToWorkflowTodo)
  console.log(
    '[getAllStageTodos] stage=%s directTodos=%d handoffMapped=%d',
    stage,
    directTodos.length,
    mappedHandoffTodos.length,
  )
  return [...directTodos, ...mappedHandoffTodos]
}

function buildStageTodoItems(stageTodos: WorkflowTodo[]): HiringStageTodoVm[] {
  const result = stageTodos
    .filter(todo => todo.status !== HiringTodoStatus.Dismissed)
    .map((todo) => {
      const isFallback = isFallbackTodoSource(todo.source)

      return {
        id: todo.id,
        title: todo.title,
        status: todo.status,
        summary: todo.currentState ?? todo.question ?? todo.evidence ?? todo.acceptanceEvidence ?? '',
        detail: todo.expectedState ?? todo.acceptanceCriteria ?? todo.suggestedAction ?? '',
        source: todo.source,
        sourceLabel: isFallback ? '系统补位' : '结构化待办',
        isFallback,
      }
    })
  const dismissedCount = stageTodos.length - result.length
  if (dismissedCount > 0) {
    console.log(
      '[buildStageTodoItems] filtered out %d dismissed todos, remaining=%d',
      dismissedCount,
      result.length,
    )
  }
  return result
}

function getExternalPendingCredentialSlots(credentialSlots: CredentialSlot[] | null | undefined) {
  return credentialSlots?.filter(slot =>
    slot.bindingStatus !== HiringCredentialBindingStatus.Bound &&
    slot.bindingStatus !== HiringCredentialBindingStatus.NotRequired) ?? []
}

function getRuntimeFacts(workflowState: HiringWorkflowState | null): WorkflowRuntimeFacts {
  return workflowState?.runtimeFacts ?? EMPTY_RUNTIME_FACTS
}

function isTodoComplete(status: string) {
  return status === HiringTodoStatus.Done || status === HiringTodoStatus.Resolved
}

function summarizeTodoStatus(todos: WorkflowTodo[]) {
  return {
    completed: todos.filter(todo => isTodoComplete(todo.status)).length,
    pending: todos.filter(todo => !isTodoComplete(todo.status) && todo.status !== HiringTodoStatus.Dismissed).length,
    needsReview: todos.filter(todo => todo.status === HiringTodoStatus.NeedsReview).length,
  }
}

function summarizeStageNotes(
  workflowState: HiringWorkflowState | null,
  stage: HiringUiStage,
  stageTodos: WorkflowTodo[],
  stageDiagnostics: DiagnosticTodo[],
): string[] {
  const notes: string[] = []
  const summary = summarizeTodoStatus(stageTodos)
  const runtimeFacts = getRuntimeFacts(workflowState)

  if (stageDiagnostics.length > 0) {
    notes.push(stageDiagnostics[0].question)
  }

  if (summary.completed > 0) {
    notes.push(`已完成 ${summary.completed} 条工单`)
  }
  if (summary.pending > 0) {
    notes.push(`仍有 ${summary.pending} 条工单待处理`)
  }
  if (summary.needsReview > 0) {
    notes.push(`仍有 ${summary.needsReview} 条工单待复核`)
  }

  if (stage === HiringCollectionStage.Material) {
    if (runtimeFacts.materialClassifiedFiles.length > 0) {
      notes.push(`已分类 ${runtimeFacts.materialClassifiedFiles.length} 份资料`)
    }
    const extractionTargetCount = Object.keys(runtimeFacts.materialExtractionTargets ?? {}).length
    if (extractionTargetCount > 0) {
      notes.push(`已写明 ${extractionTargetCount} 个抽取目标`)
    }
  }

  if (stage === HiringCollectionStage.Skill) {
    if (runtimeFacts.skillBaselineReviewed) {
      notes.push(runtimeFacts.skillBaselineConfirmed ? '默认技能基线已确认' : '默认技能基线已盘点')
    }
  }

  if (stage === HiringCollectionStage.External) {
    const pendingSlots = getExternalPendingCredentialSlots(workflowState?.credentialSlots)
    if (pendingSlots.length > 0) {
      notes.push(`仍有 ${pendingSlots.length} 个凭据槽位待绑定`)
    }
    if (stageTodos.some(todo =>
      todo.gapType === 'external_skip_declaration' &&
      isTodoComplete(todo.status))) {
      notes.push('已明确跳过外部连接阶段')
    }
    const pendingReviewCount = workflowState?.configGovernance?.pendingReviewTodoIds.length ?? 0
    if (pendingReviewCount > 0) {
      notes.push(`配置治理影响 ${pendingReviewCount} 条工单待复核`)
    }
  }

  if (stage === HiringCollectionStage.ReadyForPackaging && workflowState?.latestDiagnosticReport?.readyForPackaging) {
    notes.push('诊断已通过，可以执行打包')
  }

  return notes.slice(0, 3)
}

function isFinalized(phase: HiringCollectionPhaseType) {
  return phase === HiringCollectionPhase.Finalized
}

function isStageComplete(
  stage: HiringUiStage,
  currentStage: HiringUiStage,
  collectionPhase: HiringCollectionPhaseType,
  readinessStatus: string | null | undefined,
) {
  if (isFinalized(collectionPhase)) {
    return true
  }

  if (readinessStatus === HiringStageReadinessStatus.Complete || readinessStatus === HiringStageReadinessStatus.Skipped) {
    return true
  }

  return getStageIndex(stage) < getStageIndex(currentStage)
}

function getStageStatus(
  stage: HiringUiStage,
  currentStage: HiringUiStage,
  collectionPhase: HiringCollectionPhaseType,
  readinessStatus: string | null | undefined,
): HiringStepStatus {
  if (isStageComplete(stage, currentStage, collectionPhase, readinessStatus)) {
    return 'complete'
  }

  if (!isFinalized(collectionPhase) && stage === currentStage) {
    return 'active'
  }

  return 'pending'
}

function getStageDetail(
  workflowState: HiringWorkflowState | null,
  stage: HiringUiStage,
  status: HiringStepStatus,
  readinessStatus: string | null | undefined,
  stageTodos: WorkflowTodo[],
) {
  const config = STAGE_CONFIG[stage]
  if (status === 'active') {
    if (stage === HiringCollectionStage.External) {
      const pendingSlots = getExternalPendingCredentialSlots(workflowState?.credentialSlots)
      if (pendingSlots.length > 0) {
        return `${pendingSlots.length} 个凭据待绑定`
      }
    }

    return '进行中'
  }

  if (status === 'complete') {
    if (readinessStatus === HiringStageReadinessStatus.Skipped) {
      return '已跳过'
    }

    if (stage === HiringCollectionStage.ReadyForPackaging) {
      return workflowState?.collectionPhase === HiringCollectionPhase.Finalized ? '已交付' : '可打包'
    }

    const completedCount = stageTodos.filter(todo => isTodoComplete(todo.status)).length
    if (completedCount > 0) {
      return `${completedCount} 条已完成`
    }

    return config.completeLabel
  }

  return config.pendingLabel
}

function buildGuideCard(
  workflowState: HiringWorkflowState | null,
  stage: HiringUiStage,
): HiringGuideVm {
  const stageTodos = getAllStageTodos(workflowState, stage)
  const diagnostics = getDiagnosticTodos(workflowState, stage)
  const readiness = getStageReadiness(workflowState?.stageReadiness, stage)
  const notes = summarizeStageNotes(workflowState, stage, stageTodos, diagnostics)
  const runtimeFacts = getRuntimeFacts(workflowState)
  const statusText = readiness?.status === HiringStageReadinessStatus.Complete || readiness?.status === HiringStageReadinessStatus.Skipped
    ? '已完成'
    : stage === workflowState?.currentStage
      ? '进行中'
      : '待推进'

  if (stage === HiringCollectionStage.Material) {
    return {
      stage,
      title: '先把资料阶段做实',
      description: '现在的重点不是补待办数量，而是把每份资料的分类和抽取目标讲清楚。',
      bulletTitle: '资料阶段',
      bulletBody: notes[0] ?? readiness?.reason ?? '至少上传 1 份资料，并为每份资料写明抽取目标。',
      statusText,
      hints: [
        '优先上传最能代表真实业务规则或真实案例的资料。',
        runtimeFacts.materialReady ? '资料阶段已经满足继续条件。' : '资料上传后，还需要继续补齐分类和抽取目标。',
      ],
    }
  }

  if (stage === HiringCollectionStage.Skill) {
    return {
      stage,
      title: '只补真正缺失的技能项',
      description: '默认技能基线不再生成右侧待办，只有待补充项才会形成 required 工单。',
      bulletTitle: '技能阶段',
      bulletBody: notes[0] ?? readiness?.reason ?? '先盘点默认技能基线，再决定是否还需要新增补充技能项。',
      statusText,
      hints: [
        runtimeFacts.skillBaselineReviewed ? '如果默认技能已经够用，直接确认进入第三阶段。' : '先把默认技能基线盘清楚，再决定要不要补充。',
        '每条补充技能都要说清楚触发条件、边界和预期输出。',
      ],
    }
  }

  if (stage === HiringCollectionStage.External) {
    return {
      stage,
      title: '把外部连接拆成能力单元',
      description: '每条外部能力都要明确系统、操作、目标、凭据槽位和关联技能。',
      bulletTitle: '外部阶段',
      bulletBody: notes[0] ?? readiness?.reason ?? '按 MCP / CLI / database 这类连接能力逐条补齐。',
      statusText,
      hints: [
        '敏感凭据不要发进聊天框，统一在右侧凭据绑定区处理。',
        '如果确实不需要外部系统，要显式声明跳过。',
      ],
    }
  }

  return {
    stage,
    title: '收口诊断与复核阻塞',
    description: '打包阶段不再新增业务缺口，只处理复核、配置治理和最终打包条件。',
    bulletTitle: '打包阶段',
    bulletBody: workflowState?.latestDiagnosticReport?.readyForPackaging
      ? '资料、技能与外部连接都已就绪，可以直接打包。'
      : notes[0] ?? workflowState?.latestDiagnosticReport?.userSummary ?? '仍有复核或诊断阻塞项待处理。',
    statusText,
    hints: [
      '只有进入可打包状态后，生成实例按钮才会解锁。',
      '如果看到待复核或凭据未绑定，就优先处理这些阻塞项。',
    ],
  }
}

export function getBlockedReasonForStage(
  workflowState: HiringWorkflowState | null,
  stage: HiringUiStage,
) {
  if (!workflowState) {
    return '工作流尚未初始化，请稍后再试。'
  }

  const targetIndex = getStageIndex(stage)
  const currentIndex = getStageIndex(workflowState.currentStage)
  if (targetIndex <= currentIndex) {
    return ''
  }

  for (const candidate of STAGE_ORDER.slice(0, targetIndex)) {
    const readiness = getStageReadiness(workflowState.stageReadiness, candidate)
    const isComplete = readiness?.status === HiringStageReadinessStatus.Complete || readiness?.status === HiringStageReadinessStatus.Skipped
    if (!isComplete) {
      return `请先完成「${STAGE_CONFIG[candidate].title}」：${readiness?.reason ?? '前序阶段尚未满足推进条件。'}`
    }
  }

  const readiness = getStageReadiness(workflowState.stageReadiness, stage)
  if (readiness?.reason) {
    return readiness.reason
  }

  const diagnostic = getDiagnosticTodos(workflowState, stage)[0]
  return diagnostic?.question ?? `「${STAGE_CONFIG[stage].title}」尚未解锁，请先完成前序阶段。`
}

export function buildHiringWorkflowViewModel(
  workflowState: HiringWorkflowState | null,
  focusedStage: HiringUiStage | null,
): HiringWorkflowVm {
  const uiCurrentStage = (workflowState?.currentStage as HiringUiStage) || HiringCollectionStage.Material
  const collectionPhase = (workflowState?.collectionPhase as HiringCollectionPhaseType) || HiringCollectionPhase.NotStarted
  const guideStage = focusedStage ?? uiCurrentStage
  const currentStageReason = getStageReadiness(workflowState?.stageReadiness, uiCurrentStage)?.reason
    ?? workflowState?.latestDiagnosticReport?.userSummary
    ?? '工作流已就绪。'

  const stageCards = STAGE_ORDER.map((stage) => {
    const readiness = getStageReadiness(workflowState?.stageReadiness, stage)
    const status = getStageStatus(stage, uiCurrentStage, collectionPhase, readiness?.status)
    const stageTodos = getAllStageTodos(workflowState, stage)
    const stageDiagnostics = getDiagnosticTodos(workflowState, stage)
    const notes = summarizeStageNotes(workflowState, stage, stageTodos, stageDiagnostics)

    return {
      stage,
      title: STAGE_CONFIG[stage].panelTitle,
      description: STAGE_CONFIG[stage].panelDescription,
      subtask: STAGE_CONFIG[stage].subtask,
      status,
      detail: getStageDetail(workflowState, stage, status, readiness?.status, stageTodos),
      notes,
      todoItems: buildStageTodoItems(stageTodos),
    } satisfies HiringStageCardVm
  })

  const canFinalize = collectionPhase === HiringCollectionPhase.ReadyForFinalize ||
    workflowState?.latestDiagnosticReport?.readyForPackaging === true
  const pendingReviewCount = workflowState?.configGovernance?.pendingReviewTodoIds.length ?? 0
  const pendingSlots = getExternalPendingCredentialSlots(workflowState?.credentialSlots)

  let blockedReason = ''
  if (!canFinalize) {
    blockedReason = workflowState?.latestDiagnosticReport?.userSummary ?? currentStageReason
  }
  if (pendingSlots.length > 0) {
    blockedReason = `仍有 ${pendingSlots.length} 个凭据槽位待绑定。`
  }
  if (pendingReviewCount > 0) {
    blockedReason = `仍有 ${pendingReviewCount} 条工单因配置治理待复核。`
  }

  const stepPills = STAGE_ORDER.map((stage, index) => {
    const readiness = getStageReadiness(workflowState?.stageReadiness, stage)
    const status = getStageStatus(stage, uiCurrentStage, collectionPhase, readiness?.status)
    const isClickable = status !== 'pending' || stage === uiCurrentStage
    const latestDispatch = getStageLatestDispatch(workflowState, stage)
    return {
      stage,
      index,
      title: STAGE_CONFIG[stage].title,
      description: STAGE_CONFIG[stage].description,
      status,
      isClickable,
      blockedReason: isClickable ? '' : getBlockedReasonForStage(workflowState, stage),
      dispatchStatus: resolveDispatchStatus(latestDispatch),
      dispatchSummary: latestDispatch?.userSummary ?? null,
    } satisfies HiringStageStepVm
  })

  const completedCount = stageCards.filter(item => item.status === 'complete').length

  return {
    uiCurrentStage,
    stepPills,
    stageCards,
    guideCard: buildGuideCard(workflowState, guideStage),
    actionState: {
      canFinalize,
      finalizeLabel: collectionPhase === HiringCollectionPhase.Finalized ? '已生成实例' : '发起打包',
      blockedReason,
    },
    blockedReason: completedCount === STAGE_ORDER.length ? '' : blockedReason,
    overallProgress: completedCount,
    promptPlaceholder: STAGE_CONFIG[uiCurrentStage].placeholder,
    currentStageReason,
  }
}

