import { test, expect, type Page } from '@playwright/test'

// ─── 辅助：等待步骤标题出现 ───────────────────────────────────────────────────
async function expectStep(page: Page, stepLabel: string) {
  await expect(page.getByRole('heading', { name: stepLabel })).toBeVisible()
}

// ─── 辅助：点击「下一步」并等待页面稳定 ─────────────────────────────────────
async function clickNext(page: Page) {
  const btn = page.getByRole('button', { name: /下一步|生成实例/ })
  await expect(btn).toBeVisible()
  await btn.click()
  await page.waitForTimeout(300)
}

// ─── 辅助：当前激活步骤 badge ───────────────────────────────────────────────
async function expectActiveStep(page: Page, label: string) {
  const activeChip = page.locator(
    `div.bg-indigo-600.text-white:has-text("${label}")`
  )
  await expect(activeChip).toBeVisible()
}

// ═══════════════════════════════════════════════════════════════════════════════
// 测试套件：构建数字员工 — 完整雇佣流程
// ═══════════════════════════════════════════════════════════════════════════════

test.describe('构建数字员工 — 雇佣流程 E2E', () => {

  test.beforeEach(async ({ page }) => {
    // 直接进入销售跟进助理的雇佣页
    await page.goto('/hiring/t001')
    // 等待主内容区渲染
    await expect(page.getByText('雇佣 · 销售跟进助理')).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-01  页面基础结构
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-01 雇佣流程页基础结构正确渲染', async ({ page }) => {
    // 标题
    await expect(page.getByText('雇佣 · 销售跟进助理')).toBeVisible()
    await expect(page.getByText('带 TA 了解你的业务，开始实习')).toBeVisible()

    // 步骤条 — 5 个步骤全部出现（用 first() 避免 strict mode 多节点报错）
    for (const label of ['确认雇佣目标', '描述业务场景', '上传资料', '系统追问', '生成实例']) {
      await expect(page.getByText(label).first()).toBeVisible()
    }

    // 右侧模板信息卡
    await expect(page.getByText('模板信息')).toBeVisible()
    await expect(page.getByText('销售跟进助理').first()).toBeVisible()
    await expect(page.getByText('销售 · 通用')).toBeVisible()

    // 进度面板
    await expect(page.getByText('进度')).toBeVisible()

    // 返回按钮
    await expect(page.getByText('返回模板详情')).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-02  Step 1 — 确认雇佣目标
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-02 Step 1 确认雇佣目标 — 表单渲染与填写', async ({ page }) => {
    await expectStep(page, '确认雇佣目标')
    await expectActiveStep(page, '确认雇佣目标')

    // 表单字段都存在
    await expect(page.getByPlaceholder(/小追、合同助手/)).toBeVisible()
    await expect(page.getByPlaceholder(/帮助销售团队/)).toBeVisible()
    await expect(page.getByPlaceholder(/华东销售团队/)).toBeVisible()
    await expect(page.getByPlaceholder(/销售经理/)).toBeVisible()

    // 紧迫程度下拉
    await expect(page.getByRole('combobox')).toBeVisible()

    // 上一步按钮在 Step 1 应为禁用
    const prevBtn = page.getByRole('button', { name: '上一步' })
    await expect(prevBtn).toBeDisabled()

    // 填写表单
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('帮助华东销售减少手工跟进，让商机不漏跟')
    await page.getByPlaceholder(/华东销售团队/).fill('华东销售')
    await page.getByPlaceholder(/销售经理/).fill('销售经理')
    await page.getByRole('combobox').selectOption('较紧急')

    // 验证已填值
    await expect(page.getByPlaceholder(/小追、合同助手/)).toHaveValue('小追')
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-03  Step 1 → Step 2 跳转
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-03 Step 1 填写后点击下一步跳转到 Step 2', async ({ page }) => {
    // 先填写必填项
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('减少商机漏跟')

    await clickNext(page)

    // 应进入 Step 2
    await expectStep(page, '描述你的业务场景')
    await expectActiveStep(page, '描述业务场景')

    // 右侧「已完成」摘要出现
    await expect(page.getByText('已完成')).toBeVisible()
    await expect(page.getByText('小追')).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-04  Step 2 — 描述业务场景
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-04 Step 2 描述业务场景 — 表单渲染与填写', async ({ page }) => {
    // 先跳到 Step 2
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('减少商机漏跟')
    await clickNext(page)

    await expectStep(page, '描述你的业务场景')

    // 字段检查
    await expect(page.getByPlaceholder(/销售跟进日常流程/)).toBeVisible()
    await expect(page.getByPlaceholder(/描述这个员工主要工作/)).toBeVisible()
    await expect(page.getByPlaceholder(/现在的流程有哪些问题/)).toBeVisible()
    await expect(page.getByPlaceholder(/Salesforce/)).toBeVisible()
    await expect(page.getByPlaceholder(/有哪些必须遵守的规则/)).toBeVisible()

    // 填写
    await page.getByPlaceholder(/销售跟进日常流程/).fill('华东区商机跟进流程')
    await page.getByPlaceholder(/描述这个员工主要工作/).fill('销售每天需要跟进 CRM 中的商机，手动记录通话内容，现在经常漏跟')
    await page.getByPlaceholder(/Salesforce/).fill('纷享销客、飞书')

    // 上一步可点击
    const prevBtn = page.getByRole('button', { name: '上一步' })
    await expect(prevBtn).toBeEnabled()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-05  Step 2 → Step 3 跳转
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-05 Step 2 填写后跳转到 Step 3 上传资料', async ({ page }) => {
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('减少漏跟')
    await clickNext(page)

    await page.getByPlaceholder(/销售跟进日常流程/).fill('华东商机跟进')
    await page.getByPlaceholder(/描述这个员工主要工作/).fill('跟进 CRM 商机')
    await clickNext(page)

    // 应进入 Step 3
    await expectStep(page, '上传资料与系统理解')
    await expectActiveStep(page, '上传资料')

    // 上传区
    await expect(page.getByText('拖入文件或点击上传')).toBeVisible()
    await expect(page.getByText('选择文件')).toBeVisible()

    // Mock 文件列表
    await expect(page.getByText('销售流程手册 v3.pdf')).toBeVisible()
    await expect(page.getByText('CRM 字段说明.xlsx')).toBeVisible()

    // 三态理解区
    await expect(page.getByText('系统已理解')).toBeVisible()
    await expect(page.getByText('还不确定')).toBeVisible()
    await expect(page.getByText('尚缺失')).toBeVisible()

    // 理解条目
    await expect(page.getByText('销售漏斗阶段定义')).toBeVisible()
    await expect(page.getByText('审批升级链路')).toBeVisible()
    await expect(page.getByText('价格权限规则文档')).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-06  Step 3 → Step 4 跳转
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-06 Step 3 跳转到 Step 4 系统追问', async ({ page }) => {
    // 快速跳到 Step 3
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('减少漏跟')
    await clickNext(page)
    await page.getByPlaceholder(/销售跟进日常流程/).fill('华东商机跟进')
    await page.getByPlaceholder(/描述这个员工主要工作/).fill('跟进 CRM 商机')
    await clickNext(page)
    await clickNext(page)

    // 应进入 Step 4
    await expectStep(page, '系统追问与缺口确认')
    await expectActiveStep(page, '系统追问')

    // 3 个追问题目
    await expect(page.getByText('规则缺口')).toBeVisible()
    await expect(page.getByText('术语 / 概念缺口')).toBeVisible()
    await expect(page.getByText('流程缺口')).toBeVisible()

    // 追问内容
    await expect(page.getByText(/商机超过 14 天未跟进/)).toBeVisible()
    await expect(page.getByText(/意向客户/)).toBeVisible()
    await expect(page.getByText(/会议纪要/)).toBeVisible()

    // 回答框均可输入
    const textareas = page.getByPlaceholder('请回答（可留空）…')
    await expect(textareas).toHaveCount(3)

    // 填写第一个回答
    await textareas.nth(0).fill('超过 14 天未跟进自动升级给直属主管')
    await expect(textareas.nth(0)).toHaveValue('超过 14 天未跟进自动升级给直属主管')
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-07  Step 4 → Step 5 生成实例
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-07 Step 4 点击「生成实例」跳转到 Step 5', async ({ page }) => {
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('减少漏跟')
    await clickNext(page)
    await page.getByPlaceholder(/销售跟进日常流程/).fill('华东商机跟进')
    await page.getByPlaceholder(/描述这个员工主要工作/).fill('跟进 CRM 商机')
    await clickNext(page)
    await clickNext(page)

    // Step 4：回答追问
    const textareas = page.getByPlaceholder('请回答（可留空）…')
    await textareas.nth(0).fill('超 14 天升级给主管')
    await textareas.nth(1).fill('意向=有过一次沟通，高意向=明确表达采购意愿')

    // 点击「生成实例」
    const genBtn = page.getByRole('button', { name: '生成实例' })
    await expect(genBtn).toBeVisible()
    await genBtn.click()
    await page.waitForTimeout(300)

    // Step 5 — 实例生成成功页
    await expect(page.getByText('实例已生成！')).toBeVisible()
    await expect(page.getByText('小追 实例已生成！')).toBeVisible()

    // 实例摘要
    await expect(page.getByText('实例名称')).toBeVisible()
    await expect(page.getByText('基于模板')).toBeVisible()
    await expect(page.getByText('当前状态')).toBeVisible()
    await expect(page.getByText('实习中')).toBeVisible()

    // 初始能力显示
    await expect(page.getByText('商机追踪')).toBeVisible()
    await expect(page.getByText('自动提醒')).toBeVisible()

    // 待配置项
    await expect(page.getByText('待你完成的配置')).toBeVisible()
    await expect(page.getByText(/企业 IM 邀请 TA/)).toBeVisible()

    // 两个跳转按钮
    await expect(page.getByRole('button', { name: '去我的团队' })).toBeVisible()
    await expect(page.getByRole('button', { name: '继续雇佣' })).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-08  完整主链路 — 全 5 步一气呵成
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-08 完整主链路：5 步全部通过并生成数字员工', async ({ page }) => {
    // ── Step 1
    await expectActiveStep(page, '确认雇佣目标')
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('帮助华东销售团队减少商机漏跟，让销售专注成交')
    await page.getByPlaceholder(/华东销售团队/).fill('华东销售中心')
    await page.getByPlaceholder(/销售经理/).fill('销售总监')
    await page.getByRole('combobox').selectOption('较紧急')
    await clickNext(page)

    // ── Step 2
    await expectActiveStep(page, '描述业务场景')
    await page.getByPlaceholder(/销售跟进日常流程/).fill('华东区 Q2 商机跟进 SOP')
    await page.getByPlaceholder(/描述这个员工主要工作/).fill(
      '销售每天需在 CRM 中标记商机阶段，记录通话要点，当前大量时间花在手工录入上，导致漏跟率达 18%'
    )
    await page.getByPlaceholder(/现在的流程有哪些问题/).fill('手工录入耗时多，漏跟无预警，周报需手动整理')
    await page.getByPlaceholder(/Salesforce/).fill('纷享销客、飞书、企业微信')
    await page.getByPlaceholder(/有哪些必须遵守的规则/).fill('成交额 > 50 万需总监审批；客户资料不得外传')
    await clickNext(page)

    // ── Step 3
    await expectActiveStep(page, '上传资料')
    await expect(page.getByText('销售流程手册 v3.pdf')).toBeVisible()
    await expect(page.getByText('系统已理解')).toBeVisible()
    await clickNext(page)

    // ── Step 4
    await expectActiveStep(page, '系统追问')
    const answers = page.getByPlaceholder('请回答（可留空）…')
    await answers.nth(0).fill('超 14 天自动推送飞书消息给主管，抄送 BU 负责人')
    await answers.nth(1).fill('意向客户：有过一次沟通；高意向客户：明确表达采购意向或询价')
    await answers.nth(2).fill('纪要仅内部留存，不推送客户')

    // ── Step 5 生成
    await page.getByRole('button', { name: '生成实例' }).click()
    await page.waitForTimeout(400)

    // 验证结果
    await expect(page.getByText('小追 实例已生成！')).toBeVisible()
    await expect(page.getByText('实习中')).toBeVisible()
    await expect(page.getByText('华东销售中心').first()).toBeVisible()

    // 导航到我的团队
    await page.getByRole('button', { name: '去我的团队' }).click()
    await expect(page).toHaveURL('/team')
    await expect(page.getByRole('heading', { name: '我的团队' })).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-09  步骤回退
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-09 步骤可以正常回退', async ({ page }) => {
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('减少漏跟')
    await clickNext(page)

    await expectActiveStep(page, '描述业务场景')

    // 点上一步回退
    await page.getByRole('button', { name: '上一步' }).click()
    await page.waitForTimeout(200)

    await expectActiveStep(page, '确认雇佣目标')
    // 已填写的值应保留
    await expect(page.getByPlaceholder(/小追、合同助手/)).toHaveValue('小追')
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-10  Step 4 允许空答案直接生成
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-10 Step 4 追问可不填直接生成实例', async ({ page }) => {
    await page.getByPlaceholder(/小追、合同助手/).fill('测试员工')
    await page.getByPlaceholder(/帮助销售团队/).fill('测试目标')
    await clickNext(page)
    await page.getByPlaceholder(/销售跟进日常流程/).fill('测试场景')
    await page.getByPlaceholder(/描述这个员工主要工作/).fill('测试描述')
    await clickNext(page)
    await clickNext(page)

    // 不填任何追问，直接生成
    await page.getByRole('button', { name: '生成实例' }).click()
    await page.waitForTimeout(300)

    await expect(page.getByText('测试员工 实例已生成！')).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-11  返回按钮导航
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-11 返回模板详情按钮跳转正确', async ({ page }) => {
    await page.getByText('返回模板详情').click()
    await expect(page).toHaveURL('/templates/t001')
    await expect(page.getByText('销售跟进助理').first()).toBeVisible()
  })

  // ──────────────────────────────────────────────────────────────────────────
  // TC-12  Step 5 — 继续雇佣按钮跳转市场
  // ──────────────────────────────────────────────────────────────────────────
  test('TC-12 生成实例后「继续雇佣」跳转市场首页', async ({ page }) => {
    await page.getByPlaceholder(/小追、合同助手/).fill('小追')
    await page.getByPlaceholder(/帮助销售团队/).fill('减少漏跟')
    await clickNext(page)
    await page.getByPlaceholder(/销售跟进日常流程/).fill('跟进流程')
    await page.getByPlaceholder(/描述这个员工主要工作/).fill('跟进')
    await clickNext(page)
    await clickNext(page)
    await page.getByRole('button', { name: '生成实例' }).click()
    await page.waitForTimeout(300)

    await page.getByRole('button', { name: '继续雇佣' }).click()
    await expect(page).toHaveURL('/market')
    await expect(page.getByText('数字员工市场')).toBeVisible()
  })
})
