import { useState, useRef, useCallback, useEffect } from 'react'
import { useNavigate, useParams, useLocation } from 'react-router-dom'
import {
  ArrowLeft, Paperclip, Send, FileText, X, CheckCircle,
  Circle, CheckCircle2, Sparkles, Bot, Lock,
} from 'lucide-react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { templates } from '../mock/data'
import { addUserEmployee, generateEmployeeId, updateUserEmployee } from '../utils/storage'
import type { DigitalEmployee } from '../mock/data'
import Stepper from '../components/Stepper'
import type { StepDef } from '../components/Stepper'

// ── 步骤定义 ──────────────────────────────────────────────────────────────────
const STEPS: StepDef[] = [
  { title: '雇佣目标', description: '命名与核心目标' },
  { title: '业务场景', description: '流程与痛点描述' },
  { title: '系统对接', description: '提炼系统，上传资料' },
  { title: '缺口确认', description: '补全关键配置项' },
  { title: '生成实例', description: '进入实习培训' },
]

// ── 类型 ──────────────────────────────────────────────────────────────────────
interface ChatFile {
  id: string; name: string; size: number; status: '解析中' | '已解析'
}

interface ChatMessage {
  id: string
  role: 'bot' | 'user'
  content: string
  files?: ChatFile[]
  showCreate?: boolean  // 显示"生成实例"按钮
}

// ── 追问缺口 ─────────────────────────────────────────────────────────────────
interface GapQ {
  id: string
  system: string
  itemName: string
  question: string
  critical: boolean   // true = 必填，锁定生成按钮
  answered: boolean
  answer?: string
}

// ── 系统识别结果 ──────────────────────────────────────────────────────────────
interface IdentifiedSystem {
  name: string
  reason: string        // 从场景中提炼的依据
  required: boolean
  fileHint: string      // 建议上传什么文件
  status: 'pending' | 'uploading' | 'ready'
}

// ── 配置 mock（按模板 ID）────────────────────────────────────────────────────
interface ConfigItem { id: string; name: string; detail: string; source: 'extracted' | 'manual'; from?: string; value?: string }
interface ConfigGroup { system: string; icon: string; items: ConfigItem[] }

const CONFIGS: Record<string, ConfigGroup[]> = {
  't001': [
    { system: 'CRM 系统', icon: '🗂️', items: [
      { id: 'c1', name: 'API 凭证', detail: 'Client ID + Secret', source: 'extracted', from: 'salesforce_config.json', value: 'sf_prod_●●●' },
      { id: 'c2', name: '商机阶段定义', detail: '识别到 6 个阶段', source: 'extracted', from: '销售流程手册.pdf' },
      { id: 'c3', name: '字段映射表', detail: 'CRM 字段与系统标准字段对应关系', source: 'manual' },
    ]},
    { system: '企业 IM', icon: '💬', items: [
      { id: 'i1', name: 'Bot Token', detail: '机器人身份凭证', source: 'extracted', from: 'feishu_app.txt', value: 'bt_●●●●' },
      { id: 'i2', name: '通知群 Webhook', detail: '推送提醒的目标群', source: 'extracted', from: 'feishu_robot.txt', value: 'https://open.feishu.cn/webhook/v2/●●●●' },
    ]},
  ],
  't002': [
    { system: '客服系统', icon: '🎫', items: [
      { id: 'c1', name: 'API Token', detail: 'Zendesk 访问凭证', source: 'extracted', from: 'zendesk_config.json', value: 'zdsk_●●●●' },
      { id: 'c2', name: '工单分类规则', detail: '识别到 12 个一级分类', source: 'extracted', from: 'FAQ知识库.xlsx' },
      { id: 'c3', name: '升级触发条件', detail: '哪些情况需转人工', source: 'manual' },
    ]},
    { system: '知识库', icon: '📚', items: [
      { id: 'k1', name: 'FAQ 文档', detail: '已解析 156 条 Q&A', source: 'extracted', from: 'FAQ知识库.xlsx' },
      { id: 'k2', name: '回复语气', detail: '回复风格配置', source: 'manual' },
    ]},
  ],
  't003': [
    { system: '文档系统', icon: '📁', items: [
      { id: 'c1', name: '云文档权限', detail: 'docs:read 已授权', source: 'extracted', from: 'feishu_app.txt', value: '已授权' },
      { id: 'c2', name: '标准合同模板', detail: '识别到 8 类模板', source: 'extracted', from: '标准合同模板库.zip' },
      { id: 'c3', name: '红线条款', detail: '23 条已识别，建议人工复核', source: 'extracted', from: '法务红线手册.pdf' },
    ]},
    { system: '审批通知', icon: '🔔', items: [
      { id: 'a1', name: '法务负责人 IM', detail: '高风险合同升级通知接收人', source: 'manual' },
      { id: 'a2', name: '审批回调地址', detail: '审核结果写回审批流', source: 'manual' },
    ]},
  ],
}

const DEFAULT_CONFIGS: ConfigGroup[] = [
  { system: '系统接入', icon: '🔌', items: [
    { id: 'd1', name: 'API 凭证', detail: '从配置文件提取', source: 'extracted', from: '配置文件.json', value: 'key_●●●●' },
    { id: 'd2', name: '环境配置', detail: '生产 / 测试环境地址', source: 'manual' },
  ]},
  { system: '企业 IM', icon: '💬', items: [
    { id: 'i1', name: 'Bot Token', detail: '机器人凭证', source: 'extracted', from: 'im_config.txt', value: 'bt_●●●●' },
    { id: 'i2', name: '通知群 Webhook', detail: '推送目标群', source: 'extracted', from: 'feishu_robot.txt', value: 'https://open.feishu.cn/webhook/v2/●●●●' },
  ]},
]

// 从场景描述识别出需要对接的系统
function inferSystems(scenario: string, t: ReturnType<typeof templates.find>): IdentifiedSystem[] {
  const base: IdentifiedSystem[] = t?.requiredSystems.map(s => ({
    name: s.split('（')[0].split('(')[0].trim(),
    reason: '模板必需系统',
    required: true,
    fileHint: `${s} 的 API 凭证、配置文件或接入说明`,
    status: 'pending' as const,
  })) ?? []

  const extra: IdentifiedSystem[] = []
  if (scenario.includes('审批') && !base.find(b => b.name.includes('审批')))
    extra.push({ name: '审批系统', reason: '场景提到审批流程', required: false, fileHint: '审批流 Webhook 或回调地址', status: 'pending' })
  if ((scenario.includes('邮件') || scenario.includes('email')) && !base.find(b => b.name.includes('邮件')))
    extra.push({ name: '邮件系统', reason: '场景提到邮件沟通', required: false, fileHint: 'SMTP 配置或邮件 API 凭证', status: 'pending' })

  // 企业 IM 是所有数字员工的必需通道
  if (!base.find(b => b.name.includes('IM') || b.name.includes('飞书') || b.name.includes('企微')))
    extra.push({ name: '企业 IM', reason: '数字员工通过企业 IM 接收任务与推送通知', required: true, fileHint: '飞书应用 App ID / Bot Token 或企微机器人配置', status: 'pending' })

  return [...base, ...extra]
}

// 追问列表
function buildGaps(configs: ConfigGroup[]): GapQ[] {
  const gapQuestions: Record<string, string> = {
    '字段映射表': '你们 CRM 的关键字段是哪些？请描述字段名与含义，或上传字段说明文档。',
    '通知群 Webhook': '请在飞书 / 企微群设置 → 机器人中获取 Webhook URL，并粘贴到这里。',
    '升级触发条件': '哪些情况必须转给人工处理？请列出主要场景（如退款、投诉、账号安全）。',
    '回复语气': '你希望 TA 的回复风格是正式、友好还是简洁？有没有需要避开的措辞？',
    '法务负责人 IM': '高风险合同需通知谁？请提供其飞书用户 ID 或手机号。',
    '审批回调地址': '审核结果需写回哪个系统？请提供回调 URL 或审批流 ID。',
    '环境配置': '你们使用生产环境还是测试环境？请提供对应的 API 地址。',
  }
  return configs.flatMap(g =>
    g.items.filter(i => i.source === 'manual').map(i => ({
      id: i.id,
      system: g.system,
      itemName: i.name,
      question: gapQuestions[i.name] ?? `${i.detail}（请补充具体配置信息）`,
      critical: g.items.indexOf(i) === g.items.filter(x => x.source === 'manual').indexOf(i)
        ? true : false, // 每组第一个 manual 项为关键
      answered: false,
    }))
  ).map((g, idx) => ({ ...g, critical: idx < 2 })) // 前两项为关键缺口
}

function mkId() { return `${Date.now()}_${Math.random().toString(36).slice(2)}` }
function fmtSize(b: number) { return b < 1048576 ? `${(b / 1024).toFixed(1)} KB` : `${(b / 1048576).toFixed(1)} MB` }

// ── 主组件 ────────────────────────────────────────────────────────────────────
export default function HiringPage() {
  const { templateId } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const customEmployee = location.state?.customEmployee
  const t = templates.find(t => t.id === templateId)
  const employeeName = customEmployee?.name ?? t?.name ?? ''
  const configs = CONFIGS[templateId ?? ''] ?? DEFAULT_CONFIGS

  // ── 对话状态 ────────────────────────────────────────────────────────────────
  const [stage, setStage] = useState(0)   // 0 雇佣目标 1 业务场景 2 系统对接 3 缺口确认 4 完成
  const [messages, setMessages] = useState<ChatMessage[]>([])
  const [typing, setTyping] = useState(false)
  const [input, setInput] = useState('')
  const [pendingFiles, setPendingFiles] = useState<ChatFile[]>([])
  const [allFiles, setAllFiles] = useState<ChatFile[]>([])

  // ── 右侧面板数据 ─────────────────────────────────────────────────────────────
  const [hiringGoal, setHiringGoal] = useState('')
  const [scenarioPoints, setScenarioPoints] = useState<{ team: string; trigger: string; pain: string; raw: string } | null>(null)
  const [identifiedSystems, setIdentifiedSystems] = useState<IdentifiedSystem[]>([])
  const [gaps, setGaps] = useState<GapQ[]>([])
  const [gapIndex, setGapIndex] = useState(0)

  // ── 实例创建 ────────────────────────────────────────────────────────────────
  const [instanceCreated, setInstanceCreated] = useState(false)
  const [createdId, setCreatedId] = useState('')
  const [generatedPackage, setGeneratedPackage] = useState<{ ontology: object; skills: object } | null>(null)

  const criticalGaps = gaps.filter(g => g.critical)
  const canCreate = criticalGaps.every(g => g.answered)

  const fileRef = useRef<HTMLInputElement>(null)
  const chatEndRef = useRef<HTMLDivElement>(null)
  const initialized = useRef(false)

  useEffect(() => {
    chatEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages, typing])

  useEffect(() => {
    if (initialized.current) return
    initialized.current = true
    botSay(`你好！我是 NCrew 上岗助理 👋\n\n我们一起完成「${employeeName}」的上岗配置。\n\n**第一步：雇佣目标**\n\n请告诉我：\n• 给 TA 起个名字（例如：小追、合同助手）\n• 你希望 TA 主要解决什么问题\n• 归属哪个团队`, 500)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // stage 2：选完文件后自动发送，无需手动点击发送按钮
  useEffect(() => {
    if (stage === 2 && pendingFiles.length > 0) {
      const timer = setTimeout(() => {
        const incoming = [...pendingFiles]
        setPendingFiles([])
        const userMsg: ChatMessage = { id: mkId(), role: 'user', content: '', files: incoming }
        setMessages(prev => [...prev, userMsg])
        setAllFiles(prev => [...prev, ...incoming])
        incoming.forEach((f, fi) => {
          setTimeout(() => {
            setAllFiles(prev => prev.map(p => p.id === f.id ? { ...p, status: '已解析' } : p))
            setIdentifiedSystems(prev => prev.map((s, i) => i <= fi ? { ...s, status: 'ready' } : s))
          }, 1500 + Math.random() * 600)
        })
        handleUploadedFiles('', incoming)
      }, 300)
      return () => clearTimeout(timer)
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stage, pendingFiles.length])

  // ── bot 工具 ────────────────────────────────────────────────────────────────
  function botSay(content: string, delay = 1200, extra?: Partial<ChatMessage>) {
    setTyping(true)
    setTimeout(() => {
      setTyping(false)
      setMessages(prev => [...prev, { id: mkId(), role: 'bot', content, ...extra }])
    }, delay)
  }

  // ── 发送消息 ────────────────────────────────────────────────────────────────
  function handleSend() {
    const text = input.trim()
    if (!text && pendingFiles.length === 0) return

    const userMsg: ChatMessage = {
      id: mkId(), role: 'user', content: text,
      files: pendingFiles.length ? [...pendingFiles] : undefined,
    }
    setMessages(prev => [...prev, userMsg])
    setInput('')

    if (pendingFiles.length) {
      const incoming = [...pendingFiles]
      setPendingFiles([])
      setAllFiles(prev => [...prev, ...incoming])
      incoming.forEach((f, fi) => {
        setTimeout(() => {
          setAllFiles(prev => prev.map(p => p.id === f.id ? { ...p, status: '已解析' } : p))
          setIdentifiedSystems(prev => prev.map((s, i) => i <= fi ? { ...s, status: 'ready' } : s))
        }, 1500 + Math.random() * 600)
      })
      handleUploadedFiles(text, incoming)
      return
    }

    if (stage === 0) handleGoalStage(text)
    else if (stage === 1) handleScenarioStage(text)
    else if (stage === 3) handleGapAnswer(text)
  }

  // ── Stage 0：雇佣目标 ────────────────────────────────────────────────────────
  function handleGoalStage(text: string) {
    setHiringGoal(text)
    setStage(1)
    botSay(`好的，已记录雇佣目标 ✓\n\n**第二步：业务场景**\n\n请详细描述 TA 要工作的业务背景：\n• 当前的业务流程是怎样的？\n• 什么情况下会触发 TA 工作？\n• 现在的痛点是什么？\n• 期望 TA 输出什么结果？`, 1200)
  }

  // ── Stage 1：业务场景 ────────────────────────────────────────────────────────
  function handleScenarioStage(text: string) {
    const team = text.match(/(\d+)\s*人/) ? `${text.match(/(\d+)\s*人/)![1]} 人团队` : '中型团队'
    const trigger = text.includes('每天') ? '每日触发' : text.includes('每月') ? '每月触发' : '事件触发'
    const pain = text.includes('手动') ? '手动操作耗时' : text.includes('漏') ? '信息遗漏风险' : text.includes('审核') ? '审核效率低' : '流程效率低'
    setScenarioPoints({ team, trigger, pain, raw: text })

    const systems = inferSystems(text, t)
    setIdentifiedSystems(systems)
    setStage(2)

    const systemList = systems.map(s => `• **${s.name}**${s.required ? '（必需）' : '（可选）'} — ${s.reason}`).join('\n')
    const uploadHints = systems.slice(0, 3).map(s => `• ${s.fileHint}`).join('\n')

    botSay(
      `场景已理解 ✓ 根据你描述的业务，我识别出需要对接以下系统：\n\n${systemList}\n\n**第三步：上传资料**\n\n请上传这些系统的配置文件，帮助我加强理解：\n${uploadHints}\n\n点击 📎 或直接拖入文件。`,
      1400,
    )
  }

  // ── Stage 2：文件上传后 ──────────────────────────────────────────────────────
  function handleUploadedFiles(_text: string, incoming: ChatFile[]) {
    botSay(`收到 ${incoming.length} 个文件，解析中...`, 500)

    const gapList = buildGaps(configs)
    setGaps(gapList)
    setGapIndex(0)
    setStage(3)

    const criticalCount = gapList.filter(g => g.critical).length
    const first = gapList[0]

    botSay(
      `文件解析完成 ✅ 自动提取了部分配置。\n\n**第四步：缺口确认**\n\n还有 **${gapList.length} 项**需要你来补充，其中 **${criticalCount} 项关键配置**必须填写才能生成实例。\n\n先来第一项 👇\n\n**【${first?.system}】${first?.itemName}** ${first?.critical ? '🔴 关键' : '🟡 可选'}\n${first?.question}`,
      2200,
    )
  }

  // ── Stage 3：缺口回答 ────────────────────────────────────────────────────────
  function handleGapAnswer(text: string) {
    const current = gaps[gapIndex]
    if (!current) return

    const isSkip = text.includes('跳过') || text.includes('skip')

    if (isSkip && current.critical) {
      botSay(`「${current.itemName}」是关键配置项，跳过后将无法生成实例。\n\n如果暂时不确定，可以先填写目前了解到的情况，后续再补全。请尽量提供信息。`, 800)
      return
    }

    const updated = gaps.map((g, i) => i === gapIndex ? { ...g, answered: !isSkip, answer: isSkip ? undefined : text } : g)
    setGaps(updated)

    const next = gapIndex + 1
    setGapIndex(next)

    if (next < updated.length) {
      const nextGap = updated[next]
      botSay(
        `${isSkip ? '已跳过' : '已记录 ✓'}\n\n下一项 👇\n\n**【${nextGap.system}】${nextGap.itemName}** ${nextGap.critical ? '🔴 关键' : '🟡 可选'}\n${nextGap.question}`,
        700,
      )
    } else {
      const allCriticalDone = updated.filter(g => g.critical).every(g => g.answered)
      if (allCriticalDone) {
        botSay(
          `所有关键缺口已补全 ✅\n\n现在可以生成实例了，点击下方按钮继续。`,
          700,
          { showCreate: true },
        )
      } else {
        const missing = updated.filter(g => g.critical && !g.answered).map(g => `• ${g.system}：${g.itemName}`)
        botSay(
          `追问完成，但以下关键配置尚未填写，无法生成实例：\n\n${missing.join('\n')}\n\n请补充上述信息后再继续。`,
          700,
        )
      }
    }
  }

  // ── 生成实例 ────────────────────────────────────────────────────────────────
  function triggerCreate() {
    if (!canCreate || instanceCreated) return
    botSay(`正在为你生成「${employeeName}」实例...`, 500)

    setTimeout(() => {
      if (t) {
        const emp: DigitalEmployee = {
          id: generateEmployeeId(), nickname: t.name, roleName: t.name,
          sourceTemplate: t.name, sourceTemplateId: t.id, lifecycleStatus: '待启动',
          stageSummary: '实例已生成，等待进入实习',
          primarySignal: '待操作：启动实习', signalLevel: 'ok', owningTeam: '未指定',
          createdAt: new Date().toISOString().split('T')[0], tasksDone: 0, tasksTotal: 0,
          pendingActions: [],
          capabilities: t.coreCapabilities.map(c => ({ name: c, ready: false })),
        }
        addUserEmployee(emp)
        setCreatedId(emp.id)
      }
      // ── 生成 ontology slice + business skills 包 ───────────────────────────
      const answeredGaps = gaps.filter(g => g.answered)
      const ontology = {
        meta: { name: employeeName, generatedAt: new Date().toISOString(), version: '1.0' },
        scenario: {
          summary: scenarioPoints?.raw ?? hiringGoal,
          team: scenarioPoints?.team,
          trigger: scenarioPoints?.trigger,
          painPoint: scenarioPoints?.pain,
        },
        entities: identifiedSystems.map(s => ({
          name: s.name,
          type: 'system',
          required: s.required,
          status: s.status,
        })),
        constraints: answeredGaps.map(g => ({
          system: g.system,
          field: g.itemName,
          value: g.answer,
          critical: g.critical,
        })),
        configSnapshot: configs.map(group => ({
          system: group.system,
          items: group.items.map(i => ({
            name: i.name,
            source: i.source,
            value: i.value ?? gaps.find(g => g.itemName === i.name)?.answer ?? null,
          })),
        })),
      }

      const capabilities = t?.coreCapabilities ?? []
      const skills = {
        meta: { name: employeeName, generatedAt: new Date().toISOString(), version: '1.0' },
        hiringGoal,
        capabilities: capabilities.map((cap, idx) => ({
          id: `skill_${idx + 1}`,
          name: cap,
          ready: identifiedSystems.filter(s => s.status === 'ready').length > idx,
          trigger: idx === 0 ? scenarioPoints?.trigger ?? '事件触发' : '依赖前置技能',
          systems: configs.map(g => g.system),
        })),
        connectors: configs.flatMap(group =>
          group.items.filter(i => i.source === 'extracted').map(i => ({
            system: group.system,
            field: i.name,
            value: i.value,
            from: i.from,
          }))
        ),
        gapResolutions: answeredGaps.map(g => ({
          system: g.system,
          field: g.itemName,
          answer: g.answer,
        })),
      }

      setGeneratedPackage({ ontology, skills })
      setInstanceCreated(true)
      setStage(4)
      botSay(`🎉 实例生成成功！\n\n已生成 **Ontology Slice** 和 **业务技能包**，可在右侧下载。完成配置待办后 TA 就可以进入实习了。`, 800)
    }, 1800)
  }

  function downloadJson(data: object, filename: string) {
    const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = filename
    a.click()
    URL.revokeObjectURL(url)
  }

  function handleFinish() {
    if (!createdId) { navigate('/team'); return }
    updateUserEmployee(createdId, { stageSummary: '配置已完成', primarySignal: '待操作：启动实习', signalLevel: 'ok', pendingActions: [] })
    navigate('/team')
  }

  const addPendingFiles = useCallback((fl: FileList | File[]) => {
    const newFiles = Array.from(fl).map(f => ({ id: mkId(), name: f.name, size: f.size, status: '解析中' as const }))
    setPendingFiles(prev => [...prev, ...newFiles])
  }, [])

  if (!t && !customEmployee) return <div className="flex items-center justify-center h-64 text-slate-400">模板不存在</div>

  const placeholders = ['描述雇佣目标、团队和命名…', '描述业务场景、流程和痛点…', '上传配置文件，或补充说明…', '回答缺口问题（输入"跳过"可略过可选项）…', '有任何问题，随时问我…']

  return (
    <div className="h-[calc(100vh-4rem)] flex flex-col overflow-hidden">

      {/* 顶栏 */}
      <div className="shrink-0 border-b border-slate-100 px-6 py-3 flex items-center gap-4 bg-white">
        <button onClick={() => customEmployee ? navigate('/market') : navigate(`/templates/${t?.id}`)} className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 transition-colors">
          <ArrowLeft size={14} /> {customEmployee ? '返回市场' : '返回模板详情'}
        </button>
        <div className="w-px h-4 bg-slate-200" />
        <span className="text-sm font-semibold text-slate-800">雇佣 {employeeName}</span>
      </div>

      {/* 步骤条 */}
      <div className="shrink-0 border-b border-slate-100 px-8 py-4 bg-white">
        <Stepper steps={STEPS} current={Math.min(stage, 4)} />
      </div>

      {/* 主体 */}
      <div className="flex-1 flex overflow-hidden">

        {/* ── 左：Chat ── */}
        <div className="w-1/2 flex flex-col border-r border-slate-100 bg-white">
          <div className="flex-1 overflow-y-auto px-5 py-5 space-y-4">
            {messages.map(msg => (
              <div key={msg.id} className={`flex gap-3 ${msg.role === 'user' ? 'flex-row-reverse' : ''}`}>
                {msg.role === 'bot' && (
                  <div className="w-7 h-7 rounded-full bg-slate-900 flex items-center justify-center shrink-0 mt-0.5">
                    <Bot size={13} className="text-white" />
                  </div>
                )}
                <div className={`flex flex-col gap-2 max-w-[82%] ${msg.role === 'user' ? 'items-end' : 'items-start'}`}>
                  {msg.content && (
                    <div className={`px-4 py-3 rounded-2xl text-sm leading-relaxed [&_p]:mb-1 [&_ul]:list-disc [&_ul]:pl-4 [&_ol]:list-decimal [&_ol]:pl-4 [&_h1]:text-base [&_h1]:font-bold [&_h2]:text-sm [&_h2]:font-bold [&_h3]:text-sm [&_h3]:font-semibold [&_code]:px-1 [&_code]:rounded [&_code]:text-xs [&_pre]:p-3 [&_pre]:rounded-lg [&_pre]:overflow-x-auto [&_pre]:my-2 [&_table]:border-collapse [&_th]:border [&_th]:border-slate-300 [&_th]:px-2 [&_th]:py-1 [&_td]:border [&_td]:border-slate-300 [&_td]:px-2 [&_td]:py-1 [&_blockquote]:border-l-2 [&_blockquote]:pl-3 ${
                      msg.role === 'bot'
                        ? 'bg-slate-50 text-slate-700 rounded-tl-sm [&_code]:bg-slate-100 [&_pre]:bg-slate-800 [&_pre]:text-white [&_th]:bg-slate-100 [&_a]:text-teal-600 [&_a]:underline [&_blockquote]:border-slate-300 [&_blockquote]:text-slate-500'
                        : 'bg-slate-900 text-white rounded-tr-sm [&_code]:bg-white/20 [&_pre]:bg-black/30 [&_pre]:text-white [&_th]:bg-white/10 [&_a]:text-teal-300 [&_a]:underline [&_blockquote]:border-white/30 [&_blockquote]:text-white/70'
                    }`}>
                      <ReactMarkdown
                        remarkPlugins={[remarkGfm]}
                      >
                        {msg.content}
                      </ReactMarkdown>
                    </div>
                  )}
                  {msg.files?.map(f => (
                    <div key={f.id} className="flex items-center gap-2 px-3 py-2 bg-slate-100 rounded-xl text-xs text-slate-600">
                      <FileText size={12} className="text-slate-400 shrink-0" />
                      <span className="truncate max-w-[160px]">{f.name}</span>
                      <span className="text-slate-400">{fmtSize(f.size)}</span>
                    </div>
                  ))}
                  {msg.showCreate && stage === 3 && !instanceCreated && (
                    <button
                      onClick={triggerCreate}
                      disabled={!canCreate}
                      className={`flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold transition-colors ${
                        canCreate
                          ? 'bg-slate-900 text-white hover:bg-slate-700'
                          : 'bg-slate-200 text-slate-400 cursor-not-allowed'
                      }`}
                    >
                      {canCreate ? <><Sparkles size={13} />生成实例</> : <><Lock size={13} />关键缺口未补全</>}
                    </button>
                  )}
                </div>
              </div>
            ))}
            {typing && (
              <div className="flex gap-3">
                <div className="w-7 h-7 rounded-full bg-slate-900 flex items-center justify-center shrink-0 mt-0.5">
                  <Bot size={13} className="text-white" />
                </div>
                <div className="px-4 py-3 bg-slate-50 rounded-2xl rounded-tl-sm flex items-center gap-1.5">
                  {[0,1,2].map(i => <div key={i} className="w-1.5 h-1.5 rounded-full bg-slate-400 animate-bounce" style={{ animationDelay: `${i * 0.15}s` }} />)}
                </div>
              </div>
            )}
            <div ref={chatEndRef} />
          </div>

          {pendingFiles.length > 0 && (
            <div className="px-4 pt-2 flex flex-wrap gap-2">
              {pendingFiles.map(f => (
                <div key={f.id} className="flex items-center gap-1.5 px-2.5 py-1.5 bg-slate-100 rounded-lg text-xs text-slate-600">
                  <FileText size={11} className="text-slate-400" />
                  <span className="max-w-[120px] truncate">{f.name}</span>
                  <button onClick={() => setPendingFiles(p => p.filter(x => x.id !== f.id))}><X size={11} className="text-slate-400 hover:text-slate-600" /></button>
                </div>
              ))}
            </div>
          )}

          <div className="shrink-0 border-t border-slate-100 px-4 py-3">
            <input ref={fileRef} type="file" multiple className="hidden" onChange={e => { if (e.target.files?.length) { addPendingFiles(e.target.files); e.target.value = '' } }} />
            <div className="flex items-end gap-2">
              <button onClick={() => fileRef.current?.click()} className="p-2 rounded-lg hover:bg-slate-100 text-slate-400 hover:text-slate-600 transition-colors shrink-0 mb-0.5" title="上传文件">
                <Paperclip size={17} />
              </button>
              <textarea
                value={input}
                onChange={e => setInput(e.target.value)}
                onKeyDown={e => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend() } }}
                rows={1}
                placeholder={placeholders[Math.min(stage, 4)]}
                className="flex-1 resize-none px-3 py-2 bg-slate-50 border border-slate-200 rounded-xl text-sm outline-none focus:border-slate-400 leading-relaxed max-h-32 overflow-y-auto"
              />
              <button onClick={handleSend} disabled={!input.trim() && pendingFiles.length === 0} className="p-2 rounded-xl bg-slate-900 text-white hover:bg-slate-700 disabled:opacity-30 transition-colors shrink-0 mb-0.5">
                <Send size={15} />
              </button>
            </div>
          </div>
        </div>

        {/* ── 右：Output Panel ── */}
        <div className="w-1/2 overflow-y-auto bg-slate-50 px-6 py-6 space-y-5">

          {stage === 0 && (
            <div className="flex flex-col items-center justify-center h-full text-center gap-3 opacity-40">
              <div className="w-12 h-12 rounded-full bg-slate-200 flex items-center justify-center"><Bot size={22} className="text-slate-400" /></div>
              <p className="text-sm text-slate-500">对话开始后，配置信息会在这里实时展示</p>
            </div>
          )}

          {/* 雇佣目标 */}
          {stage >= 1 && hiringGoal && (
            <OutputCard title="雇佣目标" icon="🎯" tag="已确认">
              <p className="text-sm text-slate-600 leading-relaxed">{hiringGoal}</p>
            </OutputCard>
          )}

          {/* 业务场景摘要 */}
          {stage >= 2 && scenarioPoints && (
            <OutputCard title="业务场景" icon="📋" tag="已提炼">
              <div className="grid grid-cols-3 gap-2 mb-3">
                {[{ label: '团队规模', value: scenarioPoints.team }, { label: '触发频率', value: scenarioPoints.trigger }, { label: '核心痛点', value: scenarioPoints.pain }].map(({ label, value }) => (
                  <div key={label} className="bg-white rounded-lg p-2.5">
                    <div className="text-[10px] text-slate-400 mb-0.5">{label}</div>
                    <div className="text-xs font-semibold text-slate-700">{value}</div>
                  </div>
                ))}
              </div>
              <p className="text-xs text-slate-500 leading-relaxed line-clamp-3">{scenarioPoints.raw}</p>
            </OutputCard>
          )}

          {/* 需对接的系统 + 配置状态（stage 2 系统级，stage 3+ 带配置项标签）*/}
          {stage >= 2 && (
            <OutputCard
              title="需对接的系统"
              icon="🔌"
              tag={stage >= 3
                ? `${gaps.filter(g => g.answered).length + configs.flatMap(g => g.items.filter(i => i.source === 'extracted')).length} / ${configs.flatMap(g => g.items).length} 项已就绪`
                : `${identifiedSystems.length} 个系统`}
            >
              {stage < 3 ? (
                /* Stage 2：系统级概览 */
                <div className="space-y-2">
                  {identifiedSystems.map((s, i) => (
                    <div key={i} className="flex items-start gap-2.5 px-3 py-2.5 bg-white rounded-lg">
                      <div className={`mt-0.5 w-4 h-4 rounded-full flex items-center justify-center shrink-0 ${s.status === 'ready' ? 'bg-emerald-100' : 'bg-slate-100'}`}>
                        {s.status === 'ready' ? <CheckCircle size={9} className="text-emerald-600" /> : <div className="w-2 h-2 rounded-full bg-slate-400" />}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-1.5">
                          <span className="text-xs font-medium text-slate-700">{s.name}</span>
                          <span className={`text-[9px] px-1 py-0.5 rounded font-medium ${s.required ? 'bg-red-50 text-red-500' : 'bg-slate-100 text-slate-400'}`}>
                            {s.required ? '必需' : '可选'}
                          </span>
                        </div>
                        <p className="text-[10px] text-slate-400 mt-0.5">{s.fileHint}</p>
                      </div>
                    </div>
                  ))}
                </div>
              ) : (
                /* Stage 3+：配置项级别，带缺口标签 */
                <div className="space-y-4">
                  {configs.map(group => (
                    <div key={group.system}>
                      <div className="flex items-center gap-1.5 mb-1.5">
                        <span className="text-sm leading-none">{group.icon}</span>
                        <span className="text-xs font-semibold text-slate-600">{group.system}</span>
                      </div>
                      <div className="space-y-1">
                        {group.items.map(item => {
                          const gap = gaps.find(g => g.system === group.system && g.itemName === item.name)
                          const gapIdx = gaps.findIndex(g => g.system === group.system && g.itemName === item.name)
                          const isCurrent = gapIdx === gapIndex && stage === 3 && gap && !gap.answered

                          if (item.source === 'extracted') {
                            return (
                              <div key={item.id} className="flex items-center justify-between px-2.5 py-1.5 bg-emerald-50 rounded-lg">
                                <div className="flex-1 min-w-0">
                                  <span className="text-xs text-slate-600">{item.name}</span>
                                  {item.value && <span className="ml-2 text-[10px] text-slate-400 font-mono">{item.value}</span>}
                                </div>
                                <span className="ml-2 shrink-0 text-[9px] px-1.5 py-0.5 bg-emerald-100 text-emerald-600 rounded font-medium">已提取</span>
                              </div>
                            )
                          }

                          if (!gap) return null
                          return (
                            <div key={item.id} className={`flex items-start justify-between px-2.5 py-1.5 rounded-lg transition-colors ${
                              gap.answered ? 'bg-emerald-50' : isCurrent ? 'bg-amber-50 ring-1 ring-amber-200' : 'bg-white border border-slate-100'
                            }`}>
                              <div className="flex-1 min-w-0">
                                <span className="text-xs text-slate-600">{item.name}</span>
                                {gap.answered && gap.answer && (
                                  <div className="text-[10px] text-emerald-600 mt-0.5 line-clamp-1">{gap.answer}</div>
                                )}
                              </div>
                              <span className={`ml-2 shrink-0 text-[9px] px-1.5 py-0.5 rounded font-medium ${
                                gap.answered ? 'bg-emerald-100 text-emerald-600'
                                : isCurrent ? 'bg-amber-100 text-amber-600'
                                : gap.critical ? 'bg-red-50 text-red-500'
                                : 'bg-slate-100 text-slate-400'
                              }`}>
                                {gap.answered ? '已补全' : isCurrent ? '追问中' : gap.critical ? '关键' : '可选'}
                              </span>
                            </div>
                          )
                        })}
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {/* 上传的文件 */}
              {allFiles.length > 0 && (
                <div className="mt-3 pt-3 border-t border-slate-50 space-y-1.5">
                  {allFiles.map(f => (
                    <div key={f.id} className="flex items-center gap-2 text-xs text-slate-500">
                      <FileText size={11} className="text-slate-400 shrink-0" />
                      <span className="truncate flex-1">{f.name}</span>
                      {f.status === '已解析' ? <CheckCircle size={11} className="text-emerald-500 shrink-0" /> : <div className="w-3 h-3 border-2 border-slate-300 border-t-transparent rounded-full animate-spin shrink-0" />}
                    </div>
                  ))}
                </div>
              )}

              {/* 关键缺口未完成提示 */}
              {!canCreate && stage === 3 && (
                <div className="mt-3 flex items-center gap-2 px-3 py-2 bg-red-50 rounded-lg">
                  <Lock size={11} className="text-red-400 shrink-0" />
                  <span className="text-[11px] text-red-600">关键缺口未补全，无法生成实例</span>
                </div>
              )}
            </OutputCard>
          )}

          {/* 实例生成完成 */}
          {stage === 4 && (
            <>
              {/* 实例概览 */}
              <OutputCard title="实习实例" icon="🧑‍💼" tag="可进入实习">
                <div className="space-y-2.5">
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-400">实例名称</span>
                    <span className="text-xs font-semibold text-slate-700">{employeeName}</span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-400">来源模板</span>
                    <span className="text-xs text-slate-600">{employeeName}</span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-400">预计实习开始</span>
                    <span className="text-xs text-slate-600">完成配置后即可启动</span>
                  </div>
                  <div className="flex items-center justify-between">
                    <span className="text-xs text-slate-400">实例状态</span>
                    <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full bg-emerald-50 text-emerald-600">
                      可进入实习
                    </span>
                  </div>
                </div>
              </OutputCard>

              {/* 能力就绪情况（仅已就绪） */}
              {t && (
                <OutputCard title="能力就绪情况" icon="⚡" tag={`${identifiedSystems.filter(s => s.status === 'ready').length}/${identifiedSystems.length} 系统已就绪`}>
                  <div className="space-y-1.5">
                    {t.coreCapabilities.slice(0, Math.max(1, identifiedSystems.filter(s => s.status === 'ready').length)).map(cap => (
                      <div key={cap} className="flex items-center gap-2 px-3 py-2 bg-emerald-50 rounded-lg">
                        <CheckCircle size={11} className="text-emerald-500 shrink-0" />
                        <span className="text-xs text-slate-700">{cap}</span>
                      </div>
                    ))}
                  </div>
                </OutputCard>
              )}

              {/* 配置包下载 */}
              {generatedPackage && (
                <OutputCard title="配置包" icon="📦" tag="可下载">
                  <div className="space-y-2">
                    <div className="flex items-center justify-between px-3 py-2.5 bg-slate-50 rounded-lg">
                      <div className="flex items-center gap-2.5">
                        <div className="w-8 h-8 rounded-lg bg-violet-100 flex items-center justify-center shrink-0">
                          <span className="text-sm">🧬</span>
                        </div>
                        <div>
                          <div className="text-xs font-semibold text-slate-700">Ontology Slice</div>
                          <div className="text-[10px] text-slate-400 mt-0.5">实体 · 约束 · 系统配置快照</div>
                        </div>
                      </div>
                      <div className="flex items-center gap-1.5 shrink-0">
                        <button
                          onClick={() => alert('已保存至知识库')}
                          className="px-2.5 py-1.5 rounded-lg border border-slate-200 text-slate-500 text-[10px] font-medium hover:bg-slate-50 transition-colors"
                        >
                          保存
                        </button>
                        <button
                          onClick={() => downloadJson(generatedPackage.ontology, `${employeeName}_ontology.json`)}
                          className="px-2.5 py-1.5 rounded-lg bg-slate-900 text-white text-[10px] font-medium hover:bg-slate-700 transition-colors"
                        >
                          下载
                        </button>
                      </div>
                    </div>
                    <div className="flex items-center justify-between px-3 py-2.5 bg-slate-50 rounded-lg">
                      <div className="flex items-center gap-2.5">
                        <div className="w-8 h-8 rounded-lg bg-blue-100 flex items-center justify-center shrink-0">
                          <span className="text-sm">⚡</span>
                        </div>
                        <div>
                          <div className="text-xs font-semibold text-slate-700">业务技能包</div>
                          <div className="text-[10px] text-slate-400 mt-0.5">能力 · 连接器 · 缺口解答</div>
                        </div>
                      </div>
                      <div className="flex items-center gap-1.5 shrink-0">
                        <button
                          onClick={() => alert('已保存至技能库')}
                          className="px-2.5 py-1.5 rounded-lg border border-slate-200 text-slate-500 text-[10px] font-medium hover:bg-slate-50 transition-colors"
                        >
                          保存
                        </button>
                        <button
                          onClick={() => downloadJson(generatedPackage.skills, `${employeeName}_skills.json`)}
                          className="px-2.5 py-1.5 rounded-lg bg-slate-900 text-white text-[10px] font-medium hover:bg-slate-700 transition-colors"
                        >
                          下载
                        </button>
                      </div>
                    </div>
                    <button
                      onClick={() => {
                        downloadJson(generatedPackage.ontology, `${employeeName}_ontology.json`)
                        setTimeout(() => downloadJson(generatedPackage.skills, `${employeeName}_skills.json`), 300)
                      }}
                      className="w-full py-2 rounded-lg border border-slate-200 text-xs text-slate-500 hover:bg-slate-50 hover:text-slate-700 transition-colors"
                    >
                      全部下载
                    </button>
                  </div>
                </OutputCard>
              )}

              {/* 进入培训流程 */}
              <button
                onClick={() => navigate(`/instances/${createdId}/training`)}
                className="w-full flex items-center justify-center gap-2 py-3 rounded-xl bg-violet-600 text-white text-sm font-semibold hover:bg-violet-700 transition-colors shadow-sm"
              >
                <Sparkles size={15} /> 进入培训 · 评估 · 实习 · 上岗流程
              </button>
            </>
          )}
        </div>
      </div>
    </div>
  )
}

function OutputCard({ title, icon, tag, children }: { title: string; icon: string; tag?: string; children: React.ReactNode }) {
  return (
    <div className="bg-white rounded-xl border border-slate-100 overflow-hidden">
      <div className="flex items-center gap-2 px-4 py-3 border-b border-slate-50">
        <span className="text-base">{icon}</span>
        <span className="text-sm font-semibold text-slate-700 flex-1">{title}</span>
        {tag && <span className="text-[10px] text-slate-400 bg-slate-50 px-2 py-0.5 rounded-full">{tag}</span>}
      </div>
      <div className="px-4 py-4">{children}</div>
    </div>
  )
}
