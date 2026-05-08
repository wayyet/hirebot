export type PrototypeRole = 'manager' | 'member'

export type PrototypeStatus = 'hired' | 'interning_ai' | 'interning_human' | 'live' | 'failed' | 'retired'

export type PrototypeOwnership = 'department' | 'personal_clone' | 'private_branch'

export type PrototypeRoute =
  | 'templates'
  | 'dept'
  | 'my'
  | `template/${string}`
  | `employee/${string}`
  | `hire/${string}`
  | `eval-ai/${string}`
  | `eval-human/${string}`
  | `review/${string}`
  | `publish/${string}`
  | `im/${string}`
  | `chat/${string}`
  | `clone/${string}`
  | `quick-clone/${string}`
  | `branch/${string}`

export interface PrototypeTemplate {
  id: string
  name: string
  source: '全局通用' | '企业专属'
  summary: string
  sectors: string[]
  abilities: string[]
  boundary: string[]
  tags: string[]
  used: number
  cloned: number
  cta: string
}

export interface PrototypeEmployee {
  id: string
  ownership: PrototypeOwnership
  name: string
  templateId: string
  status: PrototypeStatus
  desc: string
  owner: string
  dept: string
  updated: string
  tags: string[]
  runs: number
  cloned: number
  stageSummary: string
  primarySignal: string
  signalLevel: 'ok' | 'warn' | 'error'
  tasksDone?: number
  tasksTotal?: number
  evalProgress?: number
  hireProgress?: number
}

export interface PrototypeBinding {
  platform: 'lark' | 'dingding' | 'wecom'
  platformName: string
  mode: 'websocket' | 'callback'
  connected: boolean
  statusText: string
  channelId: string
  callbackUrl: string
}

export const prototypeTemplates: PrototypeTemplate[] = [
  {
    id: 'tpl_hr_assist',
    name: 'HR 助手',
    source: '全局通用',
    summary: '覆盖招聘 JD 撰写、候选人筛选、面试纪要整理等高频 HR 场景，开箱即可基于本部门制度做微调。',
    sectors: ['HR', '招聘', '员工关系'],
    abilities: ['生成 JD 草稿', '解析简历排序候选人', '整理面试纪要', '回答员工福利与考勤问题'],
    boundary: ['不直接对外发送 offer 与合同', '不替代背调或法律意见'],
    tags: ['AI增强', '信息处理'],
    used: 128,
    cloned: 312,
    cta: '开始雇佣 HR 助手',
  },
  {
    id: 'tpl_contract',
    name: '合同审核员',
    source: '企业专属',
    summary: '面向法务和业务部门，按企业合同模板与红线规则审阅合同条款，标注风险点并给出修改建议。',
    sectors: ['法务', '采购', '销售'],
    abilities: ['检查合同红线', '标注风险条款', '比对版本差异', '导出审阅报告'],
    boundary: ['不替代律师签署', '不提供管辖法域之外的意见'],
    tags: ['信息处理', '工具'],
    used: 64,
    cloned: 89,
    cta: '开始雇佣合同审核员',
  },
  {
    id: 'tpl_qa',
    name: '客服质检员',
    source: '全局通用',
    summary: '对客服会话和录音做合规与服务质量质检，按企业话术体系打分并产出改进建议。',
    sectors: ['客服', '运营'],
    abilities: ['按话术规范打分', '识别敏感词与违规承诺', '汇总每日质检报告', '生成个人辅导建议'],
    boundary: ['不直接处罚员工', '不修改 CRM 工单'],
    tags: ['信息处理', '工具'],
    used: 91,
    cloned: 204,
    cta: '开始雇佣客服质检员',
  },
  {
    id: 'tpl_lead',
    name: '销售线索分析师',
    source: '全局通用',
    summary: '从 CRM、官网表单、活动签到中聚合线索，做意向分级、ICP 匹配与每日洞察推送。',
    sectors: ['销售', '市场'],
    abilities: ['汇总多渠道线索并去重', '按 ICP 给线索打分', '推送高意向客户名单', '生成跟进话术建议'],
    boundary: ['不直接联系客户', '不修改公海规则'],
    tags: ['AI增强', '工具'],
    used: 47,
    cloned: 138,
    cta: '开始雇佣线索分析师',
  },
  {
    id: 'tpl_research',
    name: '行业研究员',
    source: '企业专属',
    summary: '围绕指定行业做信息聚合、竞争格局拆解、关键事件追踪，并产出周报供管理层阅读。',
    sectors: ['战略', '市场'],
    abilities: ['采集公开行业信息', '拆解竞争对手动态', '撰写行业月报', '回答行业基础问题'],
    boundary: ['不发布对外文章', '不进行交易性建议'],
    tags: ['信息处理', 'AI增强'],
    used: 33,
    cloned: 70,
    cta: '开始雇佣行业研究员',
  },
  {
    id: 'tpl_finops',
    name: '费控审核员',
    source: '全局通用',
    summary: '审核员工报销与请款单据，按企业差旅、招待、采购规则提示异常并标注合规风险。',
    sectors: ['财务', '行政'],
    abilities: ['校验报销单据完整性', '对照差旅标准提示异常', '汇总预算执行情况', '回答报销问题'],
    boundary: ['不直接审批付款', '不替代税务申报'],
    tags: ['信息处理', '开发工具'],
    used: 22,
    cloned: 41,
    cta: '开始雇佣费控审核员',
  },
]

export const prototypeEmployees: PrototypeEmployee[] = [
  {
    id: 'de_hr_2024',
    ownership: 'department',
    name: '招聘小慧',
    templateId: 'tpl_hr_assist',
    status: 'live',
    desc: '面向研发与产品部门的招聘场景，熟悉本司 JD 模板与面试评估表。',
    owner: '李部门长',
    dept: '研发部',
    updated: '2 小时前',
    tags: ['AI增强', 'HR'],
    runs: 128,
    cloned: 12,
    stageSummary: '已上岗，正在支撑招聘与入职问答',
    primarySignal: '对候选人筛选和 JD 起草很稳定',
    signalLevel: 'ok',
    tasksDone: 12,
    tasksTotal: 12,
  },
  {
    id: 'de_contract_2024',
    ownership: 'department',
    name: '合同小审',
    templateId: 'tpl_contract',
    status: 'interning_human',
    desc: '面向研发部供应商与外采合同，已加载企业合同模板与红线清单。',
    owner: '李部门长',
    dept: '研发部',
    updated: '今天 11:24',
    tags: ['信息处理'],
    runs: 0,
    cloned: 0,
    stageSummary: '人工评估中，等待法务复核',
    primarySignal: '合同红线识别效果稳定',
    signalLevel: 'warn',
    evalProgress: 62,
    tasksDone: 5,
    tasksTotal: 8,
  },
  {
    id: 'de_qa_2024',
    ownership: 'department',
    name: '服务小检',
    templateId: 'tpl_qa',
    status: 'interning_ai',
    desc: '覆盖售前售后双场景质检，已接入会话样本与企业话术规范。',
    owner: '李部门长',
    dept: '研发部',
    updated: '今天 09:08',
    tags: ['信息处理'],
    runs: 0,
    cloned: 0,
    stageSummary: 'AI 评估中，正在核对质检规则',
    primarySignal: '能识别大部分敏感表达',
    signalLevel: 'warn',
    evalProgress: 38,
    tasksDone: 3,
    tasksTotal: 8,
  },
  {
    id: 'de_lead_2024',
    ownership: 'department',
    name: '线索小掘',
    templateId: 'tpl_lead',
    status: 'hired',
    desc: '对接企业 CRM 与官网表单，待完善 ICP 评分规则。',
    owner: '李部门长',
    dept: '研发部',
    updated: '昨天',
    tags: ['AI增强', '工具'],
    runs: 0,
    cloned: 0,
    stageSummary: '已雇佣，等待训练资料补齐',
    primarySignal: '需要补充评分规则与跟进模板',
    signalLevel: 'warn',
    hireProgress: 4,
    tasksDone: 1,
    tasksTotal: 6,
  },
  {
    id: 'de_research_2024',
    ownership: 'department',
    name: '行业小研',
    templateId: 'tpl_research',
    status: 'live',
    desc: '覆盖企业服务、AI 基础设施两条赛道。',
    owner: '李部门长',
    dept: '研发部',
    updated: '3 天前',
    tags: ['信息处理'],
    runs: 86,
    cloned: 5,
    stageSummary: '持续产出行业周报',
    primarySignal: '对外部动态聚合很稳定',
    signalLevel: 'ok',
    tasksDone: 10,
    tasksTotal: 10,
  },
  {
    id: 'de_finops_2024',
    ownership: 'department',
    name: '费控小核',
    templateId: 'tpl_finops',
    status: 'failed',
    desc: 'AI 评估发现差旅边界判定不一致，需要回退到差距挖掘工位。',
    owner: '李部门长',
    dept: '研发部',
    updated: '昨天 17:42',
    tags: ['信息处理'],
    runs: 0,
    cloned: 0,
    stageSummary: '评估失败，等待 Review 回退',
    primarySignal: '差旅边界判断偏差较大',
    signalLevel: 'error',
    tasksDone: 2,
    tasksTotal: 8,
  },
  {
    id: 'pc_hr_li',
    ownership: 'personal_clone',
    name: '李成员的 HR 分身',
    templateId: 'tpl_hr_assist',
    status: 'live',
    desc: '从部门 HR 助手复制出的个人分身，支持独立对话和私域 IM 使用。',
    owner: '王成员',
    dept: '研发部',
    updated: '1 小时前',
    tags: ['个人克隆', '飞书'],
    runs: 28,
    cloned: 0,
    stageSummary: '已在个人工作台使用',
    primarySignal: '可直接接入 IM',
    signalLevel: 'ok',
    tasksDone: 6,
    tasksTotal: 6,
  },
  {
    id: 'pb_hr_li_offer',
    ownership: 'private_branch',
    name: 'Offer 分支',
    templateId: 'tpl_hr_assist',
    status: 'live',
    desc: '李成员为 offer 发放和候选人跟进专门创建的私有分支。',
    owner: '王成员',
    dept: '研发部',
    updated: '30 分钟前',
    tags: ['分支', '人事'],
    runs: 8,
    cloned: 0,
    stageSummary: '正在独立执行 offer 流程',
    primarySignal: '只保留 offer 相关能力',
    signalLevel: 'ok',
    tasksDone: 4,
    tasksTotal: 4,
  },
  {
    id: 'pc_research_li',
    ownership: 'personal_clone',
    name: '李成员的研究分身',
    templateId: 'tpl_research',
    status: 'interning_ai',
    desc: '面向个人研究主题的分身，正在做 AI 评估。',
    owner: '王成员',
    dept: '研发部',
    updated: '今天',
    tags: ['个人克隆', '研究'],
    runs: 4,
    cloned: 0,
    stageSummary: '评估中，等待人工复核',
    primarySignal: '研究摘要生成效果良好',
    signalLevel: 'warn',
    tasksDone: 2,
    tasksTotal: 5,
  },
  {
    id: 'pc_qa_old',
    ownership: 'personal_clone',
    name: '老质检分身',
    templateId: 'tpl_qa',
    status: 'failed',
    desc: '曾经的客服质检分身，因规则漂移进入待回退状态。',
    owner: '王成员',
    dept: '研发部',
    updated: '昨天',
    tags: ['个人克隆', '待回退'],
    runs: 12,
    cloned: 0,
    stageSummary: '需要回退并重新训练',
    primarySignal: '质检规则过旧',
    signalLevel: 'error',
    tasksDone: 1,
    tasksTotal: 5,
  },
  {
    id: 'pc_hr_wang',
    ownership: 'personal_clone',
    name: '王成员的 HR 分身',
    templateId: 'tpl_hr_assist',
    status: 'live',
    desc: '王成员自己的常用分身，已与飞书 IM 绑定。',
    owner: '王成员',
    dept: '研发部',
    updated: '今天 08:20',
    tags: ['个人克隆', 'IM'],
    runs: 19,
    cloned: 0,
    stageSummary: '可以直接在 IM 中调用',
    primarySignal: '个人使用最频繁',
    signalLevel: 'ok',
    tasksDone: 5,
    tasksTotal: 5,
  },
  {
    id: 'pb_hr_wang_offer',
    ownership: 'private_branch',
    name: '王成员 Offer 分支',
    templateId: 'tpl_hr_assist',
    status: 'interning_human',
    desc: '王成员为候选人合同确认创建的分支，等待人工评估完成。',
    owner: '王成员',
    dept: '研发部',
    updated: '今天 10:05',
    tags: ['分支', 'offer'],
    runs: 3,
    cloned: 0,
    stageSummary: '等待人工评估通过后上岗',
    primarySignal: '需要补充合同签署细节',
    signalLevel: 'warn',
    tasksDone: 3,
    tasksTotal: 5,
  },
]

export const prototypeImBindings: PrototypeBinding[] = [
  {
    platform: 'lark',
    platformName: '飞书',
    mode: 'websocket',
    connected: true,
    statusText: '已连接到研发部群',
    channelId: 'lark-dev-group-01',
    callbackUrl: 'wss://demo.ai4c.cn/im/lark/dev',
  },
  {
    platform: 'dingding',
    platformName: '钉钉',
    mode: 'callback',
    connected: false,
    statusText: '待配置机器人 token',
    channelId: 'dingding-dev-group-02',
    callbackUrl: 'https://demo.ai4c.cn/im/dingding/callback',
  },
  {
    platform: 'wecom',
    platformName: '企业微信',
    mode: 'callback',
    connected: true,
    statusText: '已连接到招聘群',
    channelId: 'wecom-recruit-group-01',
    callbackUrl: 'https://demo.ai4c.cn/im/wecom/callback',
  },
]

export const viewerMeta = {
  manager: { name: '李部门长', short: '李', dept: '研发部' },
  member: { name: '王成员', short: '王', dept: '研发部' },
}

export const imMeta = {
  lark: { name: '飞书', short: '飞', accent: 'blue' },
  dingding: { name: '钉钉', short: '钉', accent: 'orange' },
  wecom: { name: '企微', short: '企', accent: 'green' },
} as const

export function findTemplate(id: string) {
  return prototypeTemplates.find((item) => item.id === id) ?? null
}

export function findEmployee(id: string) {
  return prototypeEmployees.find((item) => item.id === id) ?? null
}

export function employeesForRole(role: PrototypeRole) {
  return role === 'manager'
    ? prototypeEmployees.filter((item) => item.ownership === 'department')
    : prototypeEmployees.filter((item) => item.ownership === 'personal_clone' || item.ownership === 'private_branch')
}

export function cloneEmployee(templateId: string, owner: string, ownership: PrototypeOwnership, suffix: string) {
  const template = findTemplate(templateId)
  if (!template) return null

  return {
    id: `${ownership === 'private_branch' ? 'pb' : 'pc'}_${templateId}_${suffix}`,
    ownership,
    name: `${template.name}${ownership === 'private_branch' ? ' 私有分支' : ' 分身'}`,
    templateId,
    status: ownership === 'private_branch' ? 'interning_human' : 'live',
    desc: `基于「${template.name}」创建的${ownership === 'private_branch' ? '私有分支' : '个人克隆'}，可直接进入个人工作台。`,
    owner,
    dept: '研发部',
    updated: '刚刚',
    tags: ownership === 'private_branch' ? ['分支', '定制'] : ['个人克隆', 'IM'],
    runs: 0,
    cloned: 0,
    stageSummary: ownership === 'private_branch' ? '分支已生成，等待评估' : '克隆完成，可开始对话',
    primarySignal: ownership === 'private_branch' ? '适合单一场景收敛' : '适合个人工作流',
    signalLevel: ownership === 'private_branch' ? 'warn' : 'ok',
    tasksDone: 0,
    tasksTotal: 4,
  } satisfies PrototypeEmployee
}

export function makeNewDepartmentEmployee(templateId: string) {
  const template = findTemplate(templateId)
  if (!template) return null

  const shortId = Math.random().toString(36).slice(2, 6)
  return {
    id: `de_${templateId}_${shortId}`,
    ownership: 'department' as const,
    name: `${template.name} 1号`,
    templateId,
    status: 'interning_ai' as const,
    desc: `刚从「${template.name}」模板生成的部门数字员工，等待 AI / 人工双重评估。`,
    owner: '李部门长',
    dept: '研发部',
    updated: '刚刚',
    tags: template.tags.slice(0, 2),
    runs: 0,
    cloned: 0,
    stageSummary: '生成实例完成，进入 AI 评估',
    primarySignal: '可开始评估与训练',
    signalLevel: 'warn' as const,
    tasksDone: 0,
    tasksTotal: 6,
    evalProgress: 15,
  } satisfies PrototypeEmployee
}
