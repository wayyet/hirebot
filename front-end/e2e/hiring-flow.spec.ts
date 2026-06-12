import { expect, test, type Page } from '@playwright/test'

const TEMPLATE_ID = '019ddd2a-4955-7acb-9930-67f88bf8ae8c'
const HIRE_ID = 'hire-001'

function envelope<T>(data: T) {
  return {
    code: 200,
    success: true,
    message: 'ok',
    data,
  }
}

function buildTemplate() {
  return {
    templateId: TEMPLATE_ID,
    iconUrl: '',
    name: '销售跟进助手',
    tagline: '销售团队数字员工',
    description: '帮助团队梳理资料、技能与系统配置。',
    coreAbilities: ['商机整理', '规则解释', '对外系统对接'],
    responsibilityBoundary: {
      inScope: ['跟进业务资料'],
      outOfScope: ['直接修改凭据'],
    },
    prerequisites: [
      {
        systemName: 'CRM',
        permissionName: 'api:read',
        requiredLevel: 'read',
        purpose: '读取商机数据',
      },
    ],
    successCases: ['销售跟进'],
    cta: {
      label: '开始雇佣',
      action: 'hire',
    },
  }
}

function buildStageSkills() {
  return [
    { stage: 'material', skillName: 'employment-coach-conversation.v2', requiredFields: ['material'], description: '资料整理' },
    { stage: 'skill', skillName: 'skill_generation', requiredFields: ['skill'], description: '技能生成' },
    { stage: 'external', skillName: 'external_config', requiredFields: ['external'], description: '外部系统配置' },
    { stage: 'ready_for_packaging', skillName: 'diagnosis', requiredFields: ['packaging'], description: '交付准备' },
  ]
}

function buildWorkflowState(overrides: Record<string, unknown> = {}) {
  return {
    hireId: HIRE_ID,
    sessionId: 'session-001',
    currentStage: 'material',
    requiresAudit: false,
    collectionPhase: 'IN_PROGRESS',
    stageSkills: buildStageSkills(),
    auditLogs: [],
    handoffTodos: [],
    latestDispatches: [],
    latestDiagnosticReport: {
      status: 'blocked',
      confidence: 'high',
      currentStage: 'material',
      readyForPackaging: false,
      stageReadiness: [
        { stage: 'material', status: 'partial', reason: '资料阶段仍需确认首条 handoff todo。', blockingTodoIds: ['todo-material'] },
        { stage: 'skill', status: 'missing', reason: '技能阶段尚未开始。', blockingTodoIds: [] },
        { stage: 'external', status: 'missing', reason: '外部阶段尚未开始。', blockingTodoIds: [] },
      ],
      diagnosticTodos: [],
      handoffCorrelation: [],
      openQuestions: [],
      userSummary: '仍需补齐资料阶段',
      generatedAtUtc: '2026-05-06T10:00:00Z',
    },
    configGovernance: {
      files: [],
      pendingReviewTodoIds: [],
      updatedAtUtc: '2026-05-06T10:00:00Z',
    },
    stageReadiness: [
      { stage: 'material', status: 'partial', reason: '资料阶段仍需确认首条 handoff todo。', blockingTodoIds: ['todo-material'] },
      { stage: 'skill', status: 'missing', reason: '技能阶段尚未开始。', blockingTodoIds: [] },
      { stage: 'external', status: 'missing', reason: '外部阶段尚未开始。', blockingTodoIds: [] },
    ],
    isConversationPaused: false,
    isConversationResponding: false,
    ...overrides,
  }
}

async function setupHiringMocks(
  page: Page,
  options?: {
    initialWorkflowState?: Record<string, unknown>
  },
) {
  let workflowState = buildWorkflowState(options?.initialWorkflowState)
  let timelineMessages = [
    {
      messageId: 'assistant-init',
      role: 'assistant',
      content: '欢迎开始数字员工雇佣流程，我们先从业务资料阶段开始。',
      createdAt: '2026-05-06T10:00:00Z',
    },
  ]

  await page.route('**/api/v1/**', async (route) => {
    const url = new URL(route.request().url())
    const path = url.pathname
    const method = route.request().method()

    if (method === 'GET' && path === `/api/v1/employee-templates/${TEMPLATE_ID}`) {
      await route.fulfill({ json: envelope(buildTemplate()) })
      return
    }

    if (method === 'POST' && path === `/api/v1/employee-templates/${TEMPLATE_ID}/hire`) {
      await route.fulfill({
        json: envelope({
          hireId: HIRE_ID,
          sandboxId: 'sandbox-001',
          status: 'READY',
          nextAction: 'start_conversation',
        }),
      })
      return
    }

    if (method === 'GET' && path === `/api/v1/hirings/${HIRE_ID}`) {
      await route.fulfill({
        json: envelope({
          hireId: HIRE_ID,
          sandboxId: 'sandbox-001',
          status: 'READY',
        }),
      })
      return
    }

    if (method === 'POST' && path === `/api/v1/hirings/${HIRE_ID}/conversation/start`) {
      await route.fulfill({
        json: envelope({
          hireId: HIRE_ID,
          sessionId: 'session-001',
          currentStage: workflowState.currentStage,
          requiresAudit: false,
          stageSkills: buildStageSkills(),
          isConversationPaused: false,
          isConversationResponding: false,
        }),
      })
      return
    }

    if (method === 'GET' && path === `/api/v1/hirings/${HIRE_ID}/conversation/messages`) {
      await route.fulfill({
        json: envelope({
          hireId: HIRE_ID,
          sessionId: 'session-001',
          currentStage: workflowState.currentStage,
          requiresAudit: false,
          collectionPhase: workflowState.collectionPhase,
          messages: timelineMessages,
          stageSkills: buildStageSkills(),
        }),
      })
      return
    }

    if (method === 'GET' && path === `/api/v1/hirings/${HIRE_ID}/workflow`) {
      await route.fulfill({ json: envelope(workflowState) })
      return
    }

    if (method === 'POST' && path === `/api/v1/hirings/${HIRE_ID}/conversation/messages`) {
      const body = JSON.parse(route.request().postData() ?? '{}') as { content?: string }
      const userContent = body.content?.trim() || '补充信息'
      timelineMessages = [
        ...timelineMessages,
        {
          messageId: `user-${timelineMessages.length}`,
          role: 'user',
          content: userContent,
          createdAt: '2026-05-06T10:01:00Z',
        },
      ]

      let assistantContent = '已记录你的补充信息。'
      if (workflowState.currentStage === 'material') {
        workflowState = buildWorkflowState({
          currentStage: 'skill',
          stageReadiness: [
            { stage: 'material', status: 'complete', reason: '资料已确认。', blockingTodoIds: [] },
            { stage: 'skill', status: 'partial', reason: '技能名称和触发条件待补齐。', blockingTodoIds: ['todo-skill'] },
            { stage: 'external', status: 'missing', reason: '外部阶段尚未开始。', blockingTodoIds: [] },
          ],
          latestDiagnosticReport: {
            status: 'warning',
            confidence: 'high',
            currentStage: 'skill',
            readyForPackaging: false,
            stageReadiness: [
              { stage: 'material', status: 'complete', reason: '资料已确认。', blockingTodoIds: [] },
              { stage: 'skill', status: 'partial', reason: '技能名称和触发条件待补齐。', blockingTodoIds: ['todo-skill'] },
              { stage: 'external', status: 'missing', reason: '外部阶段尚未开始。', blockingTodoIds: [] },
            ],
            diagnosticTodos: [
              {
                id: 'diag-skill',
                stage: 'skill',
                level: '必需',
                category: '阶段完备性',
                question: '请补齐 skill 名称、trigger 和 expected output。',
                evidence: '缺少技能定义',
                suggestedAction: '补齐技能定义',
                relatedHandoffTodos: ['todo-skill'],
              },
            ],
            handoffCorrelation: ['todo-skill'],
            openQuestions: [],
            userSummary: '资料已确认，进入技能阶段。',
            generatedAtUtc: '2026-05-06T10:01:00Z',
          },
          handoffTodos: [
            {
              id: 'todo-material',
              stage: 'material',
              targetSkill: 'ontology_extraction',
              intent: '整理资料',
              category: '资料',
              status: 'confirmed',
              source: 'conversation',
              acceptance: '已确认',
              payloadJson: null,
              createdAtUtc: '2026-05-06T10:00:00Z',
              updatedAtUtc: '2026-05-06T10:01:00Z',
            },
          ],
        })
        assistantContent = '资料阶段已确认，接下来请补齐技能名称、触发条件和预期输出。'
      }

      timelineMessages = [
        ...timelineMessages,
        {
          messageId: `assistant-${timelineMessages.length}`,
          role: 'assistant',
          content: assistantContent,
          createdAt: '2026-05-06T10:01:02Z',
        },
      ]

      await route.fulfill({
        json: envelope({
          hireId: HIRE_ID,
          sessionId: 'session-001',
          currentStage: workflowState.currentStage,
          requiresAudit: false,
          assistantMessage: timelineMessages[timelineMessages.length - 1],
          latestPreview: {
            hireId: HIRE_ID,
            stage: workflowState.currentStage,
            skillName: 'employment-coach-conversation.v2',
            summary: assistantContent,
            structuredData: {},
            missingFields: [],
            riskNotes: [],
            readyForAudit: false,
            generatedAt: '2026-05-06T10:01:02Z',
          },
          isConversationPaused: false,
          isConversationResponding: false,
        }),
      })
      return
    }

    if (method === 'PUT' && path.startsWith(`/api/v1/hirings/${HIRE_ID}/config-files/`)) {
      workflowState = {
        ...workflowState,
        configGovernance: {
          ...(workflowState.configGovernance as object),
          files: workflowState.configGovernance.files,
          pendingReviewTodoIds: ['todo-external'],
          updatedAtUtc: '2026-05-06T10:02:30Z',
        },
      }
      await route.fulfill({ json: envelope(workflowState) })
      return
    }

    if (method === 'POST' && path === `/api/v1/hirings/${HIRE_ID}/finalize`) {
      workflowState = {
        ...workflowState,
        collectionPhase: 'FINALIZED',
      }
      await route.fulfill({
        json: envelope({
          hireId: HIRE_ID,
          currentStage: 'ready_for_packaging',
          collectionPhase: 'FINALIZED',
          generatedFiles: ['artifacts/employee-package.zip', 'skills/sales-follow-up/SKILL.md'],
          downloadUrl: `http://localhost:5280/api/v1/hirings/${HIRE_ID}/artifacts/download`,
          employeeId: 'employee-001',
          packageFileName: '销售团队数字员工.zip',
        }),
      })
      return
    }

    if (method === 'GET' && path === `/api/v1/hirings/${HIRE_ID}/artifacts/download`) {
      await route.fulfill({
        status: 200,
        headers: {
          'content-type': 'application/zip',
          'content-disposition': 'attachment; filename="销售团队数字员工.zip"',
        },
        body: 'fake-binary-artifact',
      })
      return
    }

    if (method === 'GET' && path.startsWith(`/api/v1/hirings/${HIRE_ID}/artifacts/`)) {
      await route.fulfill({
        status: 200,
        headers: {
          'content-type': 'text/plain; charset=utf-8',
          'content-disposition': 'attachment; filename="artifact.txt"',
        },
        body: 'artifact',
      })
      return
    }

    await route.fallback()
  })
}

test.describe('数字员工雇佣流程', () => {
  test.beforeEach(async ({ page }) => {
    await setupHiringMocks(page)
  })

  test('首屏展示 4 步原型工作台，当前高亮与 workflow currentStage 一致', async ({ page }) => {
    await page.goto(`/hiring/${TEMPLATE_ID}`)

    await expect(page.getByRole('heading', { name: '数字员工雇佣流程' })).toBeVisible()
    await expect(page.locator('.hb-hiring-step-pill')).toHaveCount(4)
    await expect(page.locator('.hb-hiring-step-pill.is-active')).toContainText('业务资料')
    await expect(page.getByText('PROGRESS LEDGER')).toBeVisible()
  })

  test('资料阶段发消息后，只在 workflow 刷新确认后进入技能阶段', async ({ page }) => {
    await page.goto(`/hiring/${TEMPLATE_ID}`)

    await page.getByPlaceholder(/业务背景、资料目标/).fill('请先整理客服 SOP 和 CRM 字段说明。')
    await page.getByRole('button', { name: '发送' }).click()

    await expect(page.locator('.hb-hiring-step-pill.is-active')).toContainText('技能模块')
    await expect(page.getByText('请补齐 skill 名称、trigger 和 expected output。')).toBeVisible()
  })

  test('点击未来步骤不会越级跳转，而是显示阻塞原因', async ({ page }) => {
    await page.goto(`/hiring/${TEMPLATE_ID}`)

    await page.locator('.hb-hiring-step-pill').filter({ hasText: '外部系统' }).click()
    await expect(page.getByText(/请先完成「业务资料」/)).toBeVisible()
    await expect(page.locator('.hb-hiring-step-pill.is-active')).toContainText('业务资料')
  })

  test('外部阶段存在 skip 时显示已跳过并允许继续', async ({ page }) => {
    await setupHiringMocks(page, {
      initialWorkflowState: {
        currentStage: 'ready_for_packaging',
        collectionPhase: 'READY_FOR_FINALIZE',
        stageReadiness: [
          { stage: 'material', status: 'complete', reason: '资料完成', blockingTodoIds: [] },
          { stage: 'skill', status: 'complete', reason: '技能完成', blockingTodoIds: [] },
          { stage: 'external', status: 'skipped', reason: '用户已明确跳过外部系统。', blockingTodoIds: ['todo-skip'] },
        ],
        latestDiagnosticReport: {
          status: 'pass',
          confidence: 'high',
          currentStage: 'ready_for_packaging',
          readyForPackaging: true,
          stageReadiness: [
            { stage: 'material', status: 'complete', reason: '资料完成', blockingTodoIds: [] },
            { stage: 'skill', status: 'complete', reason: '技能完成', blockingTodoIds: [] },
            { stage: 'external', status: 'skipped', reason: '用户已明确跳过外部系统。', blockingTodoIds: ['todo-skip'] },
          ],
          diagnosticTodos: [],
          handoffCorrelation: [],
          openQuestions: [],
          userSummary: '可以继续打包。',
          generatedAtUtc: '2026-05-06T10:03:00Z',
        },
        handoffTodos: [
          {
            id: 'todo-skip',
            stage: 'external',
            targetSkill: 'external_config',
            intent: '跳过外部系统',
            category: 'skip',
            status: 'confirmed',
            source: 'conversation',
            acceptance: '已确认',
            payloadJson: '{"kind":"skip"}',
            createdAtUtc: '2026-05-06T10:02:00Z',
            updatedAtUtc: '2026-05-06T10:02:00Z',
          },
        ],
      },
    })
    await page.goto(`/hiring/${TEMPLATE_ID}`)

    await expect(page.getByText('已跳过')).toBeVisible()
    await expect(page.getByRole('button', { name: '生成实例', exact: true })).toBeVisible()
  })

  test('外部阶段存在配置治理待复核时不允许 finalize', async ({ page }) => {
    await setupHiringMocks(page, {
      initialWorkflowState: {
        currentStage: 'external',
        stageReadiness: [
          { stage: 'material', status: 'complete', reason: '资料完成', blockingTodoIds: [] },
          { stage: 'skill', status: 'complete', reason: '技能完成', blockingTodoIds: [] },
          { stage: 'external', status: 'partial', reason: '外部系统仍有配置治理待复核。', blockingTodoIds: ['todo-external'] },
        ],
        latestDiagnosticReport: {
          status: 'blocked',
          confidence: 'high',
          currentStage: 'external',
          readyForPackaging: false,
          stageReadiness: [
            { stage: 'material', status: 'complete', reason: '资料完成', blockingTodoIds: [] },
            { stage: 'skill', status: 'complete', reason: '技能完成', blockingTodoIds: [] },
            { stage: 'external', status: 'partial', reason: '外部系统仍有配置治理待复核。', blockingTodoIds: ['todo-external'] },
          ],
          diagnosticTodos: [],
          handoffCorrelation: [],
          openQuestions: [],
          userSummary: '仍有配置治理待复核。',
          generatedAtUtc: '2026-05-06T10:02:00Z',
        },
      },
    })
    await page.goto(`/hiring/${TEMPLATE_ID}`)

    await expect(page.getByText('待复核')).toBeVisible()
    await expect(page.getByRole('button', { name: '继续补齐', exact: true })).toBeVisible()
    await expect(page.locator('.hb-hiring-subtask-chip').getByText(/待复核/)).toBeVisible()
  })

  test('finalize 只在 READY_FOR_FINALIZE 时可点，成功后显示产物与实例入口', async ({ page }) => {
    await setupHiringMocks(page, {
      initialWorkflowState: {
        currentStage: 'ready_for_packaging',
        collectionPhase: 'READY_FOR_FINALIZE',
        stageReadiness: [
          { stage: 'material', status: 'complete', reason: '资料完成', blockingTodoIds: [] },
          { stage: 'skill', status: 'complete', reason: '技能完成', blockingTodoIds: [] },
          { stage: 'external', status: 'complete', reason: '外部配置完成', blockingTodoIds: [] },
        ],
        latestDiagnosticReport: {
          status: 'pass',
          confidence: 'high',
          currentStage: 'ready_for_packaging',
          readyForPackaging: true,
          stageReadiness: [
            { stage: 'material', status: 'complete', reason: '资料完成', blockingTodoIds: [] },
            { stage: 'skill', status: 'complete', reason: '技能完成', blockingTodoIds: [] },
            { stage: 'external', status: 'complete', reason: '外部配置完成', blockingTodoIds: [] },
          ],
          diagnosticTodos: [],
          handoffCorrelation: [],
          openQuestions: [],
          userSummary: '已满足打包条件。',
          generatedAtUtc: '2026-05-06T10:05:00Z',
        },
      },
    })
    await page.goto(`/hiring/${TEMPLATE_ID}`)

    await page.getByRole('button', { name: '生成实例', exact: true }).click()

    await expect(page.getByRole('button', { name: '进入培训流程' })).toBeVisible()
    await expect(page.getByRole('button', { name: 'employee-package.zip' })).toBeVisible()
    await expect(page.getByRole('button', { name: '下载后端交付包' })).toBeVisible()
  })
})
