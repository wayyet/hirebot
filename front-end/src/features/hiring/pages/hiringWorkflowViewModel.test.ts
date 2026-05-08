import { describe, expect, it } from 'vitest'

import {
  HiringCollectionPhase,
  HiringCollectionStage,
  HiringCredentialBindingStatus,
  HiringStageReadinessStatus,
} from '@/infra/api'
import type { HiringWorkflowState } from '@/infra/api'

import { buildHiringWorkflowViewModel, getBlockedReasonForStage } from './hiringWorkflowViewModel'

function buildWorkflowState(overrides: Partial<HiringWorkflowState> = {}): HiringWorkflowState {
  return {
    hireId: 'hire-001',
    sessionId: 'session-001',
    currentStage: HiringCollectionStage.Material,
    requiresAudit: false,
    collectionPhase: HiringCollectionPhase.InProgress,
    stageSkills: [],
    auditLogs: [],
    stageCompletion: [],
    workflowTodos: [],
    latestDispatches: [],
    latestDiagnosticReport: {
      status: 'blocked',
      confidence: 'high',
      currentStage: HiringCollectionStage.Material,
      readyForPackaging: false,
      stageReadiness: [
        { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Partial, reason: '资料仍待分类', blockingTodoIds: [] },
        { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Missing, reason: '技能基线尚未盘点', blockingTodoIds: [] },
        { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Missing, reason: '外部阶段尚未开始', blockingTodoIds: [] },
      ],
      diagnosticTodos: [],
      todoCorrelation: [],
      openQuestions: [],
      userSummary: '仍需补齐资料阶段',
      generatedAtUtc: '2026-05-06T10:00:00Z',
    },
    credentialSlots: [],
    configGovernance: {
      files: [],
      pendingReviewTodoIds: [],
      updatedAtUtc: '2026-05-06T10:00:00Z',
    },
    stageReadiness: [
      { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Partial, reason: '资料仍待分类', blockingTodoIds: [] },
      { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Missing, reason: '技能基线尚未盘点', blockingTodoIds: [] },
      { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Missing, reason: '外部阶段尚未开始', blockingTodoIds: [] },
    ],
    runtimeFacts: {
      materialReady: false,
      materialClassifiedFiles: [],
      materialExtractionTargets: {},
      skillBaselineReviewed: false,
      skillBaselineConfirmed: false,
    },
    isConversationPaused: false,
    isConversationResponding: false,
    ...overrides,
  }
}

describe('buildHiringWorkflowViewModel', () => {
  it('maps workflow state to step pill statuses', () => {
    const vm = buildHiringWorkflowViewModel(buildWorkflowState(), null)

    expect(vm.uiCurrentStage).toBe(HiringCollectionStage.Material)
    expect(vm.stepPills.map(item => item.status)).toEqual(['active', 'pending', 'pending', 'pending'])
    expect(vm.promptPlaceholder).toContain('资料')
  })

  it('maps stage readiness to ledger card status', () => {
    const vm = buildHiringWorkflowViewModel(buildWorkflowState({
      currentStage: HiringCollectionStage.External,
      stageReadiness: [
        { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Complete, reason: '资料完成', blockingTodoIds: [] },
        { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Complete, reason: '技能完成', blockingTodoIds: [] },
        { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Partial, reason: '凭据待绑定', blockingTodoIds: ['todo-external'] },
      ],
      latestDiagnosticReport: {
        status: 'blocked',
        confidence: 'high',
        currentStage: HiringCollectionStage.External,
        readyForPackaging: false,
        stageReadiness: [
          { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Complete, reason: '资料完成', blockingTodoIds: [] },
          { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Complete, reason: '技能完成', blockingTodoIds: [] },
          { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Partial, reason: '凭据待绑定', blockingTodoIds: ['todo-external'] },
        ],
        diagnosticTodos: [],
        todoCorrelation: [],
        openQuestions: [],
        userSummary: '外部连接仍需补齐',
        generatedAtUtc: '2026-05-06T10:00:00Z',
      },
    }), null)

    expect(vm.stageCards[0].status).toBe('complete')
    expect(vm.stageCards[1].status).toBe('complete')
    expect(vm.stageCards[2].status).toBe('active')
    expect(vm.stageCards[3].status).toBe('pending')
  })

  it('builds detail copy from diagnostic todo and runtime facts', () => {
    const vm = buildHiringWorkflowViewModel(buildWorkflowState({
      workflowTodos: [
        {
          id: 'todo-material',
          title: '分类退货规则资料',
          stage: HiringCollectionStage.Material,
          kind: 'gap',
          status: 'done',
          gapType: 'material_classification',
          priority: 'required',
          currentState: '已上传资料',
          expectedState: '完成分类并写明抽取目标',
          acceptanceCriteria: '完成分类',
          acceptanceEvidence: '已分类',
          source: 'conversation',
          fingerprint: 'todo-material',
          category: 'material',
          payload: null,
          level: null,
          question: null,
          evidence: null,
          suggestedAction: null,
          relatedTodoIds: [],
          relatedFiles: ['refund-sop.pdf'],
          createdAtUtc: '2026-05-06T10:00:00Z',
          updatedAtUtc: '2026-05-06T10:00:00Z',
        },
      ],
      latestDiagnosticReport: {
        status: 'warning',
        confidence: 'high',
        currentStage: HiringCollectionStage.Material,
        readyForPackaging: false,
        stageReadiness: [
          { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Partial, reason: '仍有资料缺少抽取目标', blockingTodoIds: [] },
          { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Missing, reason: '技能基线尚未盘点', blockingTodoIds: [] },
          { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Missing, reason: '外部阶段尚未开始', blockingTodoIds: [] },
        ],
        diagnosticTodos: [
          {
            id: 'diag-material',
            stage: HiringCollectionStage.Material,
            level: 'required',
            category: 'stage_readiness',
            question: '是否已经上传并分类至少 1 份资料，并为每份资料写明抽取目标？',
            evidence: '抽取目标尚未补齐',
            suggestedAction: '继续补齐资料分类与抽取目标。',
            relatedTodoIds: ['todo-material'],
          },
        ],
        todoCorrelation: ['todo-material'],
        openQuestions: [],
        userSummary: '资料仍需补齐',
        generatedAtUtc: '2026-05-06T10:00:00Z',
      },
      runtimeFacts: {
        materialReady: false,
        materialClassifiedFiles: ['refund-sop.pdf'],
        materialExtractionTargets: {},
        skillBaselineReviewed: false,
        skillBaselineConfirmed: false,
      },
    }), HiringCollectionStage.Material)

    expect(vm.stageCards[0].notes.join(' ')).toContain('写明抽取目标')
    expect(vm.stageCards[0].notes.join(' ')).toContain('已分类 1 份资料')
    expect(vm.guideCard.bulletBody).toContain('写明抽取目标')
  })

  it('renders fallback workflow todo as a regular pending stage item', () => {
    const vm = buildHiringWorkflowViewModel(buildWorkflowState({
      currentStage: HiringCollectionStage.Skill,
      workflowTodos: [
        {
          id: 'fallback::hire-001::skill',
          title: '确认技能基线并补齐缺失能力',
          stage: HiringCollectionStage.Skill,
          kind: 'gap',
          status: 'in_progress',
          gapType: 'fallback_skill_readiness',
          priority: 'required',
          currentState: '技能阶段仍有待确认项',
          expectedState: '完成默认技能基线盘点，并确认是否还需要补充能力项。',
          acceptanceCriteria: '默认技能基线已确认，缺失能力已明确或已确认无需补充。',
          acceptanceEvidence: null,
          source: 'system:fallback:skill',
          fingerprint: 'fallback::skill',
          category: 'skill',
          payload: null,
          level: null,
          question: null,
          evidence: null,
          suggestedAction: null,
          relatedTodoIds: [],
          relatedFiles: [],
          createdAtUtc: '2026-05-06T10:00:00Z',
          updatedAtUtc: '2026-05-06T10:00:00Z',
        },
      ],
      latestDiagnosticReport: {
        status: 'blocked',
        confidence: 'high',
        currentStage: HiringCollectionStage.Skill,
        readyForPackaging: false,
        stageReadiness: [
          { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Complete, reason: '资料完成', blockingTodoIds: [] },
          { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Partial, reason: '技能阶段仍有待确认项', blockingTodoIds: [] },
          { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Missing, reason: '外部阶段尚未开始', blockingTodoIds: [] },
        ],
        diagnosticTodos: [],
        todoCorrelation: [],
        openQuestions: [],
        userSummary: '技能阶段仍有待确认项',
        generatedAtUtc: '2026-05-06T10:00:00Z',
      },
      stageReadiness: [
        { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Complete, reason: '资料完成', blockingTodoIds: [] },
        { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Partial, reason: '技能阶段仍有待确认项', blockingTodoIds: [] },
        { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Missing, reason: '外部阶段尚未开始', blockingTodoIds: [] },
      ],
      runtimeFacts: {
        materialReady: true,
        materialClassifiedFiles: ['refund-playbook.pdf'],
        materialExtractionTargets: { 'refund-playbook.pdf': '提取退款规则' },
        skillBaselineReviewed: true,
        skillBaselineConfirmed: false,
      },
    }), HiringCollectionStage.Skill)

    expect(vm.stageCards[1].notes).toContain('仍有 1 条工单待处理')
    expect(vm.guideCard.bulletBody).toContain('仍有 1 条工单待处理')
  })

  it('maps fallback workflow todo into an explicit todo item', () => {
    const vm = buildHiringWorkflowViewModel(buildWorkflowState({
      currentStage: HiringCollectionStage.Skill,
      workflowTodos: [
        {
          id: 'fallback::hire-001::skill',
          title: '确认技能基线并补齐缺失能力',
          stage: HiringCollectionStage.Skill,
          kind: 'gap',
          status: 'in_progress',
          gapType: 'fallback_skill_readiness',
          priority: 'required',
          currentState: '技能阶段仍有待确认项',
          expectedState: '完成默认技能基线盘点，并确认是否还需要补充能力项。',
          acceptanceCriteria: '默认技能基线已确认，缺失能力已明确或已确认无需补充。',
          acceptanceEvidence: null,
          source: 'system:fallback:skill',
          fingerprint: 'fallback::skill',
          category: 'skill',
          payload: null,
          level: null,
          question: null,
          evidence: null,
          suggestedAction: null,
          relatedTodoIds: [],
          relatedFiles: [],
          createdAtUtc: '2026-05-06T10:00:00Z',
          updatedAtUtc: '2026-05-06T10:00:00Z',
        },
      ],
      stageReadiness: [
        { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Complete, reason: '资料完成', blockingTodoIds: [] },
        { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Partial, reason: '技能阶段仍有待确认项', blockingTodoIds: [] },
        { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Missing, reason: '外部阶段尚未开始', blockingTodoIds: [] },
      ],
      runtimeFacts: {
        materialReady: true,
        materialClassifiedFiles: ['refund-playbook.pdf'],
        materialExtractionTargets: { 'refund-playbook.pdf': '提取退款规则' },
        skillBaselineReviewed: true,
        skillBaselineConfirmed: false,
      },
    }), HiringCollectionStage.Skill)

    expect(vm.stageCards[1].todoItems).toHaveLength(1)
    expect(vm.stageCards[1].todoItems[0]).toMatchObject({
      id: 'fallback::hire-001::skill',
      title: '确认技能基线并补齐缺失能力',
      status: 'in_progress',
      summary: '技能阶段仍有待确认项',
      detail: '完成默认技能基线盘点，并确认是否还需要补充能力项。',
      sourceLabel: '系统补位',
      isFallback: true,
    })
  })

  it('locks finalize CTA when credential slots or config review remain', () => {
    const workflow = buildWorkflowState({
      currentStage: HiringCollectionStage.External,
      stageReadiness: [
        { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Complete, reason: '资料完成', blockingTodoIds: [] },
        { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Complete, reason: '技能完成', blockingTodoIds: [] },
        { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Partial, reason: '凭据待绑定', blockingTodoIds: ['todo-external'] },
      ],
      latestDiagnosticReport: {
        status: 'blocked',
        confidence: 'high',
        currentStage: HiringCollectionStage.External,
        readyForPackaging: false,
        stageReadiness: [
          { stage: HiringCollectionStage.Material, status: HiringStageReadinessStatus.Complete, reason: '资料完成', blockingTodoIds: [] },
          { stage: HiringCollectionStage.Skill, status: HiringStageReadinessStatus.Complete, reason: '技能完成', blockingTodoIds: [] },
          { stage: HiringCollectionStage.External, status: HiringStageReadinessStatus.Partial, reason: '凭据待绑定', blockingTodoIds: ['todo-external'] },
        ],
        diagnosticTodos: [],
        todoCorrelation: [],
        openQuestions: [],
        userSummary: '仍有凭据待绑定',
        generatedAtUtc: '2026-05-06T10:00:00Z',
      },
      credentialSlots: [
        {
          credentialSlot: 'crm-api-token',
          secretRef: null,
          authKind: 'api_key',
          targetSystem: 'CRM',
          todoId: 'todo-external',
          bindingStatus: HiringCredentialBindingStatus.Pending,
          updatedAtUtc: '2026-05-06T10:00:00Z',
        },
      ],
      configGovernance: {
        files: [],
        pendingReviewTodoIds: ['todo-external'],
        updatedAtUtc: '2026-05-06T10:00:00Z',
      },
    })

    const vm = buildHiringWorkflowViewModel(workflow, HiringCollectionStage.External)
    expect(vm.actionState.canFinalize).toBe(false)
    expect(vm.actionState.blockedReason).toContain('待复核')
    expect(getBlockedReasonForStage(workflow, HiringCollectionStage.ReadyForPackaging)).toContain('请先完成')
  })
})
