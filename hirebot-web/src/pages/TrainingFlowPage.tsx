import { useState, useEffect, useRef, useCallback } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, Brain, CheckCircle, Upload, FileText, X,
  Play, BarChart3, RefreshCw, ArrowRight, CheckCircle2,
  AlertTriangle, Sparkles, Activity, Target, Cpu, Eye,
  ChevronRight, TrendingUp, Send, Bot,
} from 'lucide-react'
import { digitalEmployees as mockEmployees } from '../mock/data'
import { loadUserEmployees, updateAnyEmployee } from '../utils/storage'
import type { DigitalEmployee } from '../mock/data'
import Stepper from '../components/Stepper'
import type { StepDef } from '../components/Stepper'

// ── Top-level stepper steps ──────────────────────────────────────────────
const TOP_STEPS: StepDef[] = [
  { title: '培训', description: '上传材料 · 自主学习' },
  { title: '评估', description: '考试 · 报告审核' },
  { title: '实习', description: '真实场景验证' },
  { title: '上岗', description: '正式上线' },
]

// ── Types ──────────────────────────────────────────────────────────────
type Phase =
  | 'materials'
  | 'training'
  | 'exam'
  | 'report'
  | 'real-data'
  | 'decision'
  | 'evolution'
  | 'onboard'

type AgentStatus = 'waiting' | 'running' | 'done'

interface LogEntry {
  id: string
  agent: 'planner' | 'generator' | 'evaluator' | 'system'
  message: string
  time: string
}

interface ExamCase {
  id: string
  skill: string
  input: string
  output: string
  score: number
  passed: boolean
  status: 'pending' | 'running' | 'done'
}

interface FileItem {
  id: string
  name: string
  size: number
  ext: string
  status: '解析中' | '已解析'
}

interface ChatMessage {
  id: string
  role: 'bot' | 'user'
  content: string
}

// ── Helpers ────────────────────────────────────────────────────────────
function getExt(name: string) {
  const ext = name.split('.').pop()?.toLowerCase() ?? ''
  const m: Record<string, string> = {
    pdf: 'PDF', doc: 'DOC', docx: 'DOC',
    xls: 'XLS', xlsx: 'XLS', md: 'MD', txt: 'TXT', json: 'JSON', csv: 'CSV',
  }
  return m[ext] ?? ext.toUpperCase()
}

function nowTime() {
  const d = new Date()
  return [d.getHours(), d.getMinutes(), d.getSeconds()]
    .map(n => n.toString().padStart(2, '0')).join(':')
}

function mkId() { return `${Date.now()}_${Math.random().toString(36).slice(2)}` }

// Phase → top stepper index
function phaseToStep(phase: Phase): number {
  if (phase === 'materials' || phase === 'training') return 0
  if (phase === 'exam' || phase === 'report') return 1
  if (phase === 'real-data' || phase === 'decision' || phase === 'evolution') return 2
  return 3
}

// ── Agent scripts ─────────────────────────────────────────────────────
const PLANNER_SCRIPT = [
  '分析上传材料，构建知识本体图谱...',
  '识别核心技能模块，建立能力依赖关系',
  '解析本地切片，提取业务语义与边缘场景',
  '生成培训契约：5 模块 · 120 迭代轮次 · 4 评估维度',
  '产物移交：TrainingSpec → 生成器 ✓',
]
const GENERATOR_SCRIPT = [
  '读取 TrainingSpec，初始化场景生成引擎...',
  '模块 1/4：基础应答场景 (38 条) 生成完成',
  '模块 2/4：投诉处理场景 (29 条) 生成完成',
  '模块 3/4：边缘异常场景 (15 条) 生成完成',
  '模块 4/4：多轮对话场景 (22 条) 生成完成',
  '自评检查：样本多样性 ✓  语义覆盖 ✓  格式规范 ✓',
  '产物移交：TrainingSamples(104条) → 评估员 ✓',
]
const EVALUATOR_SCRIPT = [
  '读取 TrainingSamples，按评估契约启动质量审核...',
  '[维度 1] 技能覆盖率：94.2% ✓',
  '[维度 2] 回答准确性：89.7% ✓',
  '[维度 3] 语义一致性：91.3% ✓',
  '[维度 4] 边缘场景处理：78.5% — 检测到 3 处知识缺口',
  '⚡ 上下文重置 — 向规划器发起定向补充迭代请求',
  '[维度 4] 补充迭代完成，边缘场景处理：88.3% ✓',
  '综合质量评分：88/100 — 通过考试门槛 ✓',
]
const CONTEXT_RESET_LOG_INDEX = 5

// ── Mock exam cases ───────────────────────────────────────────────────
const EXAM_TEMPLATES: Omit<ExamCase, 'id' | 'skill' | 'status'>[] = [
  { input: '用户询问退款流程，称 3 天前购买商品不满意', output: '非常抱歉您对商品不满意。根据退款政策，购买后 7 天内可无理由退款，请提供订单号，我来为您处理退款申请。', score: 94, passed: true },
  { input: '客户投诉 bot 重复同一答案，情绪激动要求转人工', output: '非常抱歉造成不好的体验，我理解您的心情。我将立即为您转接人工客服，请稍候。', score: 91, passed: true },
  { input: '用户咨询促销活动是否可与优惠券叠加使用', output: '本次促销不支持与其他优惠叠加，每账户限参与一次。如需了解其他优惠请随时告知。', score: 88, passed: true },
  { input: '物流延迟超 3 天，用户要求赔偿运费', output: '查询到您的包裹因天气原因延误。延误超过 3 天符合运费补偿条件，我现在为您处理补偿申请。', score: 85, passed: true },
  { input: '用户反映账号异常登录，疑似被盗号', output: '紧急处理：已临时冻结异常登录权限。请立即修改密码并开启双重认证，我将全程协助您完成安全恢复。', score: 96, passed: true },
  { input: '用户使用不规范语言或方言咨询商品价格', output: '您好，我理解您在询问商品价格。请告诉我具体商品名称，我来为您查询。', score: 72, passed: true },
  { input: '用户咨询 FAQ 中未覆盖的特殊退货场景', output: '这个情况较为特殊，超出了标准处理范围。我已为您记录并升级到专业团队，将在 1 小时内回复。', score: 80, passed: true },
  { input: '用户重复提问同一问题，测试 bot 的适应能力', output: '[检测到重复提问] 您好，关于这个问题我之前已回答。[答案未做针对性调整，直接重复]', score: 42, passed: false },
]

function buildExamCases(caps: string[]): ExamCase[] {
  return EXAM_TEMPLATES.map((t, i) => ({
    ...t, id: `case_${i}`,
    skill: caps[i % Math.max(caps.length, 1)] ?? '通用能力',
    status: 'pending' as const,
  }))
}

// ── Chatbot scripts per phase ─────────────────────────────────────────
const PHASE_BOT_GREETINGS: Record<Phase, string> = {
  materials: '👋 欢迎来到培训流程！\n\n请先上传 **技能文档**（SOP、应答规范）和 **本地切片数据**（历史对话、FAQ），我会基于这些材料为 TA 定制培训方案。\n\n上传完成后点击「开始培训」，三个智能体将自主协作完成全部培训。',
  training: '🤖 培训正在进行中...\n\n三个智能体（规划器、生成器、评估员）正在按照既定契约协作。整个过程完全自主，**你只需观察**，无需干预。\n\n如发现异常我会第一时间提醒你。',
  exam: '📝 进入模拟考试阶段。\n\nBot 正在对 **{count} 个测试用例**逐一作答，评估其掌握程度。结果将自动汇总为考试报告，由你人工审核通过。',
  report: '📊 考试完成，报告已生成。\n\n请查阅右侧的详细评估报告，了解 Bot 在各技能维度的得分情况。确认后，请根据结果选择下一步：**进入真实场景验证** 或 **补充材料重新训练**。',
  'real-data': '✅ 模拟考试通过！\n\n现在需要用真实业务数据进一步验证 Bot 的实战能力。请上传真实对话记录或业务案例，我会协助你完成最终上岗审核。',
  decision: '🎯 真实场景验证完成。\n\n综合考试成绩与真实场景表现，请做出最终上岗决策。批准后 Bot 将正式进入实习阶段，开始在企业 IM 中承接真实任务。',
  evolution: '🔄 已进入自我进化模式。\n\n请上传针对薄弱技能的补充材料，三个智能体将重新协作，重点强化失分技能后再次参加考试。',
  onboard: '🎉 恭喜！Bot 已正式进入实习阶段。\n\n接下来可以在企业 IM 中为 TA 分配任务，同时在「我的团队」中实时监控实习表现。实习期满后根据评估结果决定是否转正。',
}

// ── Small UI pieces ───────────────────────────────────────────────────
function FileList({ files, onRemove }: { files: FileItem[]; onRemove: (id: string) => void }) {
  if (!files.length) return null
  return (
    <div className="mt-3 space-y-1.5">
      {files.map(f => (
        <div key={f.id} className="flex items-center gap-2.5 p-2.5 bg-slate-50 rounded-lg group">
          <FileText size={14} className="text-slate-400 shrink-0" />
          <div className="flex-1 min-w-0">
            <div className="text-xs text-slate-700 truncate">{f.name}</div>
            <div className="text-xs text-slate-400">{(f.size / 1024).toFixed(1)} KB · {f.status}</div>
          </div>
          {f.status === '已解析'
            ? <CheckCircle size={12} className="text-emerald-500 shrink-0" />
            : <div className="w-2.5 h-2.5 rounded-full border-2 border-slate-400 border-t-transparent animate-spin shrink-0" />}
          <button onClick={() => onRemove(f.id)} className="opacity-0 group-hover:opacity-100 p-0.5 rounded hover:bg-slate-200 transition-opacity">
            <X size={11} className="text-slate-400" />
          </button>
        </div>
      ))}
    </div>
  )
}

function UploadZone({ label, hint, inputRef, onFiles, children }: {
  label: string; hint: string
  inputRef: React.RefObject<HTMLInputElement | null>
  onFiles: (f: FileList) => void
  children?: React.ReactNode
}) {
  const [drag, setDrag] = useState(false)
  return (
    <div>
      <input ref={inputRef} type="file" multiple className="hidden"
        onChange={e => { if (e.target.files) { onFiles(e.target.files); e.target.value = '' } }}
      />
      <div
        className={`border-2 border-dashed rounded-xl p-4 text-center cursor-pointer transition-colors ${drag ? 'border-violet-400 bg-violet-50' : 'border-slate-200 hover:border-slate-300'}`}
        onClick={() => inputRef.current?.click()}
        onDragOver={e => { e.preventDefault(); setDrag(true) }}
        onDragLeave={() => setDrag(false)}
        onDrop={e => { e.preventDefault(); setDrag(false); if (e.dataTransfer.files) onFiles(e.dataTransfer.files) }}
      >
        <Upload size={18} className={`mx-auto mb-1.5 ${drag ? 'text-violet-400' : 'text-slate-300'}`} />
        <p className="text-xs font-medium text-slate-600">{label}</p>
        <p className="text-xs text-slate-400 mt-0.5">{hint}</p>
      </div>
      {children}
    </div>
  )
}

// ── Right panel content per phase ──────────────────────────────────────

function RightMaterials({ caps, skillFiles, sliceFiles, skillRef, sliceRef, onSkill, onSlice, rmSkill, rmSlice, onStart, canStart }: {
  caps: string[]; skillFiles: FileItem[]; sliceFiles: FileItem[]
  skillRef: React.RefObject<HTMLInputElement | null>; sliceRef: React.RefObject<HTMLInputElement | null>
  onSkill: (f: FileList) => void; onSlice: (f: FileList) => void
  rmSkill: (id: string) => void; rmSlice: (id: string) => void
  onStart: () => void; canStart: boolean
}) {
  return (
    <div className="space-y-5">
      <div>
        <h3 className="font-semibold text-slate-800 mb-0.5">上传培训材料</h3>
        <p className="text-xs text-slate-500">材料越丰富，Bot 培训效果越好</p>
      </div>
      <div className="space-y-4">
        <div>
          <div className="flex items-center gap-2 mb-2.5">
            <div className="w-6 h-6 bg-violet-100 rounded-lg flex items-center justify-center">
              <Brain size={12} className="text-violet-600" />
            </div>
            <div className="text-xs font-semibold text-slate-700">技能文档</div>
            <div className="text-xs text-slate-400">SOP、应答规范、技能定义</div>
          </div>
          {caps.length > 0 && (
            <div className="flex flex-wrap gap-1 mb-2">
              {caps.map(cap => (
                <span key={cap} className="px-2 py-0.5 bg-violet-50 border border-violet-100 text-violet-700 rounded text-[10px] font-medium">{cap}</span>
              ))}
            </div>
          )}
          <UploadZone label="上传技能文档" hint="PDF / Word / Markdown / TXT" inputRef={skillRef} onFiles={onSkill}>
            <FileList files={skillFiles} onRemove={rmSkill} />
          </UploadZone>
        </div>
        <div>
          <div className="flex items-center gap-2 mb-2.5">
            <div className="w-6 h-6 bg-blue-100 rounded-lg flex items-center justify-center">
              <Activity size={12} className="text-blue-600" />
            </div>
            <div className="text-xs font-semibold text-slate-700">本地切片</div>
            <div className="text-xs text-slate-400">历史对话、FAQ、业务案例</div>
          </div>
          <UploadZone label="上传本地切片数据" hint="CSV / Excel / JSON / TXT / PDF" inputRef={sliceRef} onFiles={onSlice}>
            <FileList files={sliceFiles} onRemove={rmSlice} />
          </UploadZone>
        </div>
      </div>
      <div className="pt-3 border-t border-slate-100 flex justify-end">
        <button onClick={onStart} disabled={!canStart}
          className={`flex items-center gap-2 px-5 py-2 rounded-lg text-sm font-medium transition-colors ${canStart ? 'bg-violet-600 text-white hover:bg-violet-700' : 'bg-slate-100 text-slate-400 cursor-not-allowed'}`}
        >
          <Play size={13} /> 开始培训
        </button>
      </div>
    </div>
  )
}

function RightTraining({ logs, logsRef, planner, generator, evaluator, pct, plannerDone, generatorDone, contextReset, evalDone }: {
  logs: LogEntry[]; logsRef: React.RefObject<HTMLDivElement | null>
  planner: AgentStatus; generator: AgentStatus; evaluator: AgentStatus; pct: number
  plannerDone: boolean; generatorDone: boolean; contextReset: boolean; evalDone: boolean
}) {
  const agentColor: Record<string, string> = { planner: 'text-violet-400', generator: 'text-blue-400', evaluator: 'text-emerald-400', system: 'text-slate-500' }
  const agentLabel: Record<string, string> = { planner: '规划器', generator: '生成器', evaluator: '评估员', system: '系统' }

  function AgentPill({ name, sub, status, color }: { name: string; sub: string; status: AgentStatus; color: string }) {
    const cols: Record<string, { bg: string; dot: string; text: string; border: string }> = {
      violet: { bg: 'bg-violet-100', dot: 'bg-violet-500', text: 'text-violet-600', border: 'border-violet-200' },
      blue: { bg: 'bg-blue-100', dot: 'bg-blue-500', text: 'text-blue-600', border: 'border-blue-200' },
      emerald: { bg: 'bg-emerald-100', dot: 'bg-emerald-500', text: 'text-emerald-600', border: 'border-emerald-200' },
    }
    const c = cols[color]
    return (
      <div className={`p-3 rounded-xl border transition-all ${status === 'running' ? `border-2 ${c.border} shadow-sm` : 'border-slate-100'} ${status === 'waiting' ? 'opacity-40' : ''}`}>
        <div className="flex items-center gap-2 mb-1.5">
          <div className={`w-6 h-6 ${c.bg} rounded-lg flex items-center justify-center shrink-0`}>
            {color === 'violet' && <Brain size={12} className="text-violet-600" />}
            {color === 'blue' && <Cpu size={12} className="text-blue-600" />}
            {color === 'emerald' && <Target size={12} className="text-emerald-600" />}
          </div>
          <div>
            <div className="text-xs font-semibold text-slate-800">{name}</div>
            <div className="text-[10px] text-slate-400">{sub}</div>
          </div>
        </div>
        <div className="flex items-center gap-1">
          {status === 'waiting' && <div className="w-1.5 h-1.5 rounded-full bg-slate-300" />}
          {status === 'running' && <div className={`w-1.5 h-1.5 rounded-full ${c.dot} animate-pulse`} />}
          {status === 'done' && <CheckCircle size={11} className="text-emerald-500" />}
          <span className={`text-[10px] font-medium ${status === 'running' ? c.text : status === 'done' ? 'text-emerald-600' : 'text-slate-400'}`}>
            {status === 'waiting' ? '等待中' : status === 'running' ? '运行中' : '已完成'}
          </span>
        </div>
      </div>
    )
  }

  const RUBRIC = [
    { name: '技能覆盖率', score: 94.2, threshold: 85 },
    { name: '回答准确性', score: 89.7, threshold: 85 },
    { name: '语义一致性', score: 91.3, threshold: 80 },
    { name: '边缘场景处理', score: 88.3, threshold: 75 },
  ]

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="font-semibold text-slate-800 mb-0.5">自主培训中</h3>
          <p className="text-xs text-slate-500">三个智能体按既定契约协作，实时展示内部运行状态</p>
        </div>
        <div className="flex items-center gap-1 text-[10px] text-slate-400 bg-slate-50 px-2.5 py-1.5 rounded-full shrink-0">
          <Eye size={11} /> 仅可观察
        </div>
      </div>

      <div className="grid grid-cols-3 gap-2">
        <AgentPill name="规划器" sub="TrainingSpec" status={planner} color="violet" />
        <AgentPill name="生成器" sub="TrainingSamples" status={generator} color="blue" />
        <AgentPill name="评估员" sub="EvalReport" status={evaluator} color="emerald" />
      </div>

      <div className="bg-slate-950 rounded-xl p-3 space-y-2.5">
        {plannerDone && (
          <div className="flex items-start gap-2 px-2.5 py-2 bg-slate-800 border border-slate-700 rounded-lg">
            <FileText size={11} className="text-slate-400 shrink-0 mt-0.5" />
            <div>
              <span className="text-[10px] text-slate-500">移交产物 </span>
              <span className="text-[10px] font-mono font-semibold text-slate-200">TrainingSpec</span>
              <div className="flex gap-3 mt-0.5">
                <span className="text-[10px] font-mono"><span className="text-slate-500">modules: </span><span className="text-emerald-400">5</span></span>
                <span className="text-[10px] font-mono"><span className="text-slate-500">iterations: </span><span className="text-emerald-400">120</span></span>
              </div>
            </div>
          </div>
        )}
        {generatorDone && (
          <div className="flex items-start gap-2 px-2.5 py-2 bg-slate-800 border border-slate-700 rounded-lg">
            <FileText size={11} className="text-slate-400 shrink-0 mt-0.5" />
            <div>
              <span className="text-[10px] text-slate-500">移交产物 </span>
              <span className="text-[10px] font-mono font-semibold text-slate-200">TrainingSamples</span>
              <div className="flex gap-3 mt-0.5">
                <span className="text-[10px] font-mono"><span className="text-slate-500">total: </span><span className="text-emerald-400">104</span></span>
                <span className="text-[10px] font-mono"><span className="text-slate-500">self_eval: </span><span className="text-emerald-400">passed ✓</span></span>
              </div>
            </div>
          </div>
        )}
        {contextReset && (
          <div className="flex items-start gap-2 px-2.5 py-2 bg-amber-950 border border-amber-800 rounded-lg">
            <RefreshCw size={11} className="text-amber-400 shrink-0 mt-0.5" />
            <div>
              <div className="text-[10px] font-semibold text-amber-300">⚡ 上下文重置触发</div>
              <div className="text-[10px] text-amber-600 leading-relaxed">检测到 3 处边缘场景缺口 — 规划器重新接管，发起定向补充训练</div>
            </div>
          </div>
        )}
        {evalDone && (
          <div className="px-2.5 py-2.5 bg-slate-800 border border-slate-700 rounded-lg">
            <div className="text-[10px] font-semibold text-slate-400 mb-2 flex items-center gap-1">
              <Target size={10} className="text-emerald-400" /> 评估契约得分
            </div>
            <div className="space-y-1.5">
              {RUBRIC.map(c => (
                <div key={c.name}>
                  <div className="flex justify-between text-[10px] mb-0.5">
                    <span className="text-slate-400">{c.name}</span>
                    <span className={`font-mono font-semibold ${c.score >= c.threshold ? 'text-emerald-400' : 'text-red-400'}`}>{c.score}%</span>
                  </div>
                  <div className="h-1 bg-slate-700 rounded-full overflow-hidden">
                    <div className={`h-full rounded-full ${c.score >= c.threshold ? 'bg-emerald-500' : 'bg-red-500'}`} style={{ width: `${c.score}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </div>
        )}
        <div className={`overflow-y-auto font-mono text-[10px] ${(plannerDone || generatorDone || contextReset || evalDone) ? 'h-28' : 'h-44'}`}>
          {logs.length === 0
            ? <span className="text-slate-600">初始化智能体...</span>
            : logs.map(l => (
              <div key={l.id} className="flex gap-1.5 mb-1">
                <span className="text-slate-600 shrink-0">{l.time}</span>
                <span className={`shrink-0 font-semibold ${agentColor[l.agent]}`}>[{agentLabel[l.agent]}]</span>
                <span className={l.message.startsWith('⚡') ? 'text-amber-400' : 'text-slate-300'}>{l.message}</span>
              </div>
            ))}
          <div ref={logsRef} />
        </div>
      </div>

      <div className="space-y-1">
        <div className="flex justify-between text-xs text-slate-500"><span>培训进度</span><span>{pct}%</span></div>
        <div className="h-1.5 bg-slate-100 rounded-full overflow-hidden">
          <div className="h-full bg-gradient-to-r from-violet-500 to-emerald-500 rounded-full transition-all duration-300" style={{ width: `${pct}%` }} />
        </div>
      </div>
    </div>
  )
}

function RightExam({ cases, pct, score, done }: { cases: ExamCase[]; pct: number; score: number; done: boolean }) {
  const passed = cases.filter(c => c.status === 'done' && c.passed).length
  const failed = cases.filter(c => c.status === 'done' && !c.passed).length
  return (
    <div className="space-y-4">
      <div>
        <h3 className="font-semibold text-slate-800 mb-0.5">模拟考试</h3>
        <p className="text-xs text-slate-500">Bot 自主完成测试用例，实时查看作答过程</p>
      </div>
      <div className="grid grid-cols-3 gap-2">
        <div className="bg-slate-50 rounded-xl p-3 text-center">
          <div className={`text-2xl font-bold ${score >= 75 ? 'text-slate-800' : score > 0 ? 'text-amber-600' : 'text-slate-300'}`}>{score || '—'}</div>
          <div className="text-[10px] text-slate-500 mt-0.5">当前平均分</div>
        </div>
        <div className="bg-emerald-50 rounded-xl p-3 text-center">
          <div className="text-2xl font-bold text-emerald-600">{passed}</div>
          <div className="text-[10px] text-slate-500 mt-0.5">已通过</div>
        </div>
        <div className="bg-red-50 rounded-xl p-3 text-center">
          <div className="text-2xl font-bold text-red-500">{failed}</div>
          <div className="text-[10px] text-slate-500 mt-0.5">未通过</div>
        </div>
      </div>
      <div className="space-y-1">
        <div className="flex justify-between text-xs text-slate-500">
          <span>考试进度</span>
          <span>{pct}%（{cases.filter(c => c.status === 'done').length}/{cases.length} 题）</span>
        </div>
        <div className="h-1.5 bg-slate-100 rounded-full overflow-hidden">
          <div className={`h-full rounded-full transition-all duration-500 ${done ? 'bg-emerald-500' : 'bg-blue-500'}`} style={{ width: `${pct}%` }} />
        </div>
      </div>
      <div className="space-y-1.5 max-h-64 overflow-y-auto">
        {cases.map((c, i) => (
          <div key={c.id} className={`p-2.5 rounded-xl border transition-all ${
            c.status === 'running' ? 'border-blue-200 bg-blue-50' :
            c.status === 'done' ? (c.passed ? 'border-emerald-100 bg-emerald-50' : 'border-red-100 bg-red-50') :
            'border-slate-100 bg-slate-50 opacity-30'
          }`}>
            <div className="flex items-start justify-between gap-2">
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-1.5 mb-0.5">
                  <span className="text-[10px] text-slate-400">#{i + 1}</span>
                  <span className="text-[10px] px-1.5 py-0.5 bg-white rounded border border-slate-200 text-slate-600">{c.skill}</span>
                  {c.status === 'running' && <span className="text-[10px] text-blue-600 flex items-center gap-1"><div className="w-1.5 h-1.5 rounded-full bg-blue-500 animate-pulse" />作答中...</span>}
                </div>
                <div className="text-[10px] text-slate-700 line-clamp-1">{c.input}</div>
                {c.status === 'done' && <div className="text-[10px] text-slate-400 line-clamp-1 mt-0.5">{c.output}</div>}
              </div>
              {c.status === 'done' && (
                <div className="shrink-0 text-right">
                  <div className={`text-sm font-bold ${c.passed ? 'text-emerald-600' : 'text-red-500'}`}>{c.score}</div>
                  {c.passed ? <CheckCircle size={11} className="text-emerald-500 ml-auto" /> : <AlertTriangle size={11} className="text-red-400 ml-auto" />}
                </div>
              )}
            </div>
          </div>
        ))}
      </div>
      {done && (
        <div className="flex items-center justify-center gap-2 text-xs text-emerald-700 bg-emerald-50 rounded-xl p-2.5">
          <Sparkles size={12} /> 考试完成，正在生成报告...
        </div>
      )}
    </div>
  )
}

function RightReport({ score, cases, aiPassed, onProceed }: { score: number; cases: ExamCase[]; aiPassed: boolean; onProceed: () => void }) {
  const passed = cases.filter(c => c.passed).length
  const failed = cases.filter(c => !c.passed).length
  const passRate = cases.length > 0 ? Math.round(passed / cases.length * 100) : 0
  return (
    <div className="space-y-4">
      <div>
        <h3 className="font-semibold text-slate-800 mb-0.5">考试报告 — 人工审核</h3>
        <p className="text-xs text-slate-500">智能体已完成自评，请审阅后决定下一步</p>
      </div>
      <div className={`rounded-xl p-4 border-2 ${aiPassed ? 'bg-emerald-50 border-emerald-200' : 'bg-red-50 border-red-200'}`}>
        <div className="flex items-center gap-2.5">
          <div className={`w-8 h-8 rounded-full flex items-center justify-center shrink-0 ${aiPassed ? 'bg-emerald-100' : 'bg-red-100'}`}>
            {aiPassed ? <CheckCircle2 size={16} className="text-emerald-600" /> : <AlertTriangle size={16} className="text-red-500" />}
          </div>
          <div>
            <div className={`text-xs font-bold ${aiPassed ? 'text-emerald-800' : 'text-red-700'}`}>
              评估员判定：{aiPassed ? '考试通过' : '考试未通过'}
            </div>
            <div className={`text-[10px] mt-0.5 ${aiPassed ? 'text-emerald-600' : 'text-red-500'}`}>
              {aiPassed ? '已达到上岗评估标准，建议进入真实场景验证' : '未达 75 分门槛，建议补充材料后进行自我进化'}
            </div>
          </div>
        </div>
      </div>
      <div className="grid grid-cols-3 gap-2">
        <div className="bg-slate-50 rounded-xl p-3 text-center">
          <div className={`text-2xl font-bold ${score >= 75 ? 'text-emerald-600' : 'text-red-500'}`}>{score}</div>
          <div className="text-[10px] text-slate-500 mt-0.5">综合评分</div>
          <div className="text-[10px] text-slate-400">门槛 75 分</div>
        </div>
        <div className="bg-slate-50 rounded-xl p-3 text-center">
          <div className="text-2xl font-bold text-slate-800">{passRate}%</div>
          <div className="text-[10px] text-slate-500 mt-0.5">通过率</div>
          <div className="text-[10px] text-slate-400">{passed}/{cases.length} 题</div>
        </div>
        <div className="bg-slate-50 rounded-xl p-3 text-center">
          <div className={`text-2xl font-bold ${failed > 0 ? 'text-red-500' : 'text-emerald-500'}`}>{failed}</div>
          <div className="text-[10px] text-slate-500 mt-0.5">未通过</div>
          <div className="text-[10px] text-slate-400">需关注</div>
        </div>
      </div>
      {failed > 0 && (
        <div>
          <div className="text-[10px] font-semibold text-slate-700 mb-1.5">未通过用例</div>
          <div className="space-y-1.5">
            {cases.filter(c => !c.passed).map(c => (
              <div key={c.id} className="p-2.5 bg-red-50 border border-red-100 rounded-xl">
                <div className="flex items-center gap-1.5 mb-0.5">
                  <AlertTriangle size={10} className="text-red-400 shrink-0" />
                  <span className="text-[10px] font-medium text-red-700">{c.skill}</span>
                  <span className="text-[10px] text-red-400 ml-auto">评分 {c.score}</span>
                </div>
                <div className="text-[10px] text-slate-600 line-clamp-1">{c.input}</div>
              </div>
            ))}
          </div>
        </div>
      )}
      <div className="pt-3 border-t border-slate-100 flex justify-end">
        <button onClick={onProceed}
          className={`flex items-center gap-2 px-5 py-2 rounded-lg text-xs font-medium transition-colors ${aiPassed ? 'bg-emerald-600 text-white hover:bg-emerald-700' : 'bg-amber-500 text-white hover:bg-amber-600'}`}
        >
          {aiPassed ? <><ArrowRight size={13} /> 进入真实场景验证</> : <><RefreshCw size={13} /> 进入自我进化</>}
        </button>
      </div>
    </div>
  )
}

function RightRealData({ files, inputRef, onFiles, rmFile, onSubmit, canSubmit }: {
  files: FileItem[]; inputRef: React.RefObject<HTMLInputElement | null>
  onFiles: (f: FileList) => void; rmFile: (id: string) => void
  onSubmit: () => void; canSubmit: boolean
}) {
  return (
    <div className="space-y-4">
      <div>
        <h3 className="font-semibold text-slate-800 mb-0.5">真实场景验证</h3>
        <p className="text-xs text-slate-500">上传真实业务数据，验证 Bot 的实战能力</p>
      </div>
      <div className="bg-blue-50 border border-blue-100 rounded-xl p-3">
        <div className="text-[10px] font-semibold text-blue-700 mb-1 flex items-center gap-1.5"><BarChart3 size={11} />为什么需要真实场景？</div>
        <div className="text-[10px] text-blue-700 leading-relaxed">模拟数据验证了基础能力，真实业务场景更复杂多变。此步骤用于确认 Bot 是否真正具备上岗资格，由人工最终把关。</div>
      </div>
      <UploadZone label="上传真实对话记录 / 业务案例" hint="CSV / Excel / JSON / TXT / PDF" inputRef={inputRef} onFiles={onFiles}>
        <FileList files={files} onRemove={rmFile} />
      </UploadZone>
      <div className="pt-3 border-t border-slate-100 flex justify-end">
        <button onClick={onSubmit} disabled={!canSubmit}
          className={`flex items-center gap-2 px-5 py-2 rounded-lg text-xs font-medium transition-colors ${canSubmit ? 'bg-slate-900 text-white hover:bg-slate-800' : 'bg-slate-100 text-slate-400 cursor-not-allowed'}`}
        >
          <ArrowRight size={13} /> 提交人工审核
        </button>
      </div>
    </div>
  )
}

function RightDecision({ employee, realFiles, examScore, onApprove, onReject }: {
  employee: DigitalEmployee; realFiles: FileItem[]; examScore: number
  onApprove: () => void; onReject: () => void
}) {
  return (
    <div className="space-y-4">
      <div>
        <h3 className="font-semibold text-slate-800 mb-0.5">人工上岗决策</h3>
        <p className="text-xs text-slate-500">综合考量考试表现与真实场景结果，做出最终决策</p>
      </div>
      <div className="bg-slate-50 rounded-xl p-3 space-y-2">
        <div className="text-[10px] font-semibold text-slate-700 mb-0.5">决策依据摘要</div>
        {[
          ['培训对象', `${employee.nickname} · ${employee.roleName}`],
          ['模拟考试', `${examScore} 分`],
          ['真实场景文件', `${realFiles.length} 个`],
          ['评估员判定', '通过'],
        ].map(([label, value]) => (
          <div key={label} className="flex items-center justify-between text-xs">
            <span className="text-slate-500">{label}</span>
            <span className="font-medium text-slate-800">{value}</span>
          </div>
        ))}
      </div>
      <div className="grid grid-cols-2 gap-3">
        <button onClick={onApprove} className="p-4 bg-emerald-50 border-2 border-emerald-200 rounded-xl text-left hover:bg-emerald-100 transition-colors">
          <div className="flex items-center gap-2 mb-1.5">
            <CheckCircle2 size={15} className="text-emerald-600" />
            <span className="font-semibold text-emerald-800 text-xs">批准进入实习</span>
          </div>
          <div className="text-[10px] text-emerald-600 leading-relaxed">Bot 已具备上岗资格，进入实习阶段</div>
        </button>
        <button onClick={onReject} className="p-4 bg-amber-50 border-2 border-amber-200 rounded-xl text-left hover:bg-amber-100 transition-colors">
          <div className="flex items-center gap-2 mb-1.5">
            <RefreshCw size={15} className="text-amber-600" />
            <span className="font-semibold text-amber-800 text-xs">补充材料 · 自我进化</span>
          </div>
          <div className="text-[10px] text-amber-600 leading-relaxed">补充辅助资料后触发自我进化，重新评估</div>
        </button>
      </div>
    </div>
  )
}

function RightEvolution({ files, inputRef, onFiles, rmFile, onStart, canStart, roundCount }: {
  files: FileItem[]; inputRef: React.RefObject<HTMLInputElement | null>
  onFiles: (f: FileList) => void; rmFile: (id: string) => void
  onStart: () => void; canStart: boolean; roundCount: number
}) {
  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2 mb-0.5">
        <Sparkles size={16} className="text-violet-500" />
        <h3 className="font-semibold text-slate-800">自我进化{roundCount > 1 ? ` · 第 ${roundCount} 轮` : ''}</h3>
      </div>
      <div className="bg-violet-50 border border-violet-100 rounded-xl p-3">
        <div className="text-[10px] font-semibold text-violet-700 mb-1.5">自我进化机制</div>
        <div className="space-y-1">
          {['三个智能体重新协作，重点针对失分技能进行强化训练', '评估员将对比前后两次结果，追踪技能进步幅度', '进化完成后重新进入考试，可多轮迭代直至达标'].map(t => (
            <div key={t} className="flex items-start gap-1.5 text-[10px] text-violet-600">
              <ChevronRight size={10} className="mt-0.5 shrink-0" /><span>{t}</span>
            </div>
          ))}
        </div>
      </div>
      <UploadZone label="上传补充辅助资料" hint="PDF / Word / CSV / Excel / Markdown" inputRef={inputRef} onFiles={onFiles}>
        <FileList files={files} onRemove={rmFile} />
      </UploadZone>
      <div className="pt-3 border-t border-slate-100 flex justify-end">
        <button onClick={onStart} disabled={!canStart}
          className={`flex items-center gap-2 px-5 py-2 rounded-lg text-xs font-medium transition-colors ${canStart ? 'bg-violet-600 text-white hover:bg-violet-700' : 'bg-slate-100 text-slate-400 cursor-not-allowed'}`}
        >
          <TrendingUp size={13} /> 开始自我进化
        </button>
      </div>
    </div>
  )
}

function RightOnboard({ employee, onGoTeam }: { employee: DigitalEmployee; onGoTeam: () => void }) {
  return (
    <div className="space-y-4">
      <div className="flex flex-col items-center py-6 text-center">
        <div className="w-16 h-16 bg-emerald-100 rounded-full flex items-center justify-center mb-4">
          <CheckCircle2 size={32} className="text-emerald-600" />
        </div>
        <h3 className="font-bold text-slate-800 text-lg mb-1">{employee.nickname} 已上岗！</h3>
        <p className="text-sm text-slate-500">Bot 已正式进入实习阶段，可在企业 IM 中分配任务</p>
      </div>
      <div className="bg-slate-50 rounded-xl p-4 space-y-2">
        {[
          ['上岗状态', '实习中'],
          ['评估结果', '考试通过 · 真实场景验证通过'],
          ['下一步', '企业 IM 分配任务，实习期满后评估转正'],
        ].map(([label, value]) => (
          <div key={label} className="flex items-start justify-between gap-4 text-xs">
            <span className="text-slate-500 shrink-0">{label}</span>
            <span className="font-medium text-slate-800 text-right">{value}</span>
          </div>
        ))}
      </div>
      <button onClick={onGoTeam}
        className="w-full flex items-center justify-center gap-2 py-2.5 rounded-xl bg-emerald-600 text-white text-sm font-semibold hover:bg-emerald-700 transition-colors"
      >
        <ArrowRight size={14} /> 前往我的团队
      </button>
    </div>
  )
}

// ── Main Component ──────────────────────────────────────────────────────
export default function TrainingFlowPage() {
  const { id } = useParams()
  const navigate = useNavigate()

  const userEmployees = loadUserEmployees()
  const allEmployees = [...mockEmployees, ...userEmployees]
  const employee = allEmployees.find(e => e.id === id)

  const [phase, setPhase] = useState<Phase>('materials')
  const [evolutionRound, setEvolutionRound] = useState(1)

  // Files
  const [skillFiles, setSkillFiles] = useState<FileItem[]>([])
  const [sliceFiles, setSliceFiles] = useState<FileItem[]>([])
  const [realFiles, setRealFiles] = useState<FileItem[]>([])
  const [evoFiles, setEvoFiles] = useState<FileItem[]>([])
  const skillRef = useRef<HTMLInputElement>(null)
  const sliceRef = useRef<HTMLInputElement>(null)
  const realRef = useRef<HTMLInputElement>(null)
  const evoRef = useRef<HTMLInputElement>(null)

  // Training agent state
  const [logs, setLogs] = useState<LogEntry[]>([])
  const [plannerStatus, setPlannerStatus] = useState<AgentStatus>('waiting')
  const [generatorStatus, setGeneratorStatus] = useState<AgentStatus>('waiting')
  const [evaluatorStatus, setEvaluatorStatus] = useState<AgentStatus>('waiting')
  const [trainingPct, setTrainingPct] = useState(0)
  const logsRef = useRef<HTMLDivElement>(null)
  const [plannerArtifact, setPlannerArtifact] = useState(false)
  const [generatorArtifact, setGeneratorArtifact] = useState(false)
  const [contextResetEvent, setContextResetEvent] = useState(false)
  const [evalRubric, setEvalRubric] = useState(false)

  // Exam state
  const [examCases, setExamCases] = useState<ExamCase[]>([])
  const [examPct, setExamPct] = useState(0)
  const [examScore, setExamScore] = useState(0)
  const [examDone, setExamDone] = useState(false)

  // Chatbot
  const [chatMessages, setChatMessages] = useState<ChatMessage[]>([])
  const [chatInput, setChatInput] = useState('')
  const chatEndRef = useRef<HTMLDivElement>(null)

  // Init chatbot greeting
  useEffect(() => {
    const greeting = PHASE_BOT_GREETINGS[phase]
      .replace('{count}', String(EXAM_TEMPLATES.length))
    setChatMessages([{ id: mkId(), role: 'bot', content: greeting }])
  }, [phase])

  // Auto-scroll chat
  useEffect(() => { chatEndRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [chatMessages])

  // Auto-scroll logs
  useEffect(() => { logsRef.current?.scrollIntoView({ behavior: 'smooth' }) }, [logs])

  // Training simulation
  useEffect(() => {
    if (phase !== 'training') return
    const timers: ReturnType<typeof setTimeout>[] = []
    const iv: ReturnType<typeof setInterval>[] = []

    const pushLog = (agent: LogEntry['agent'], msg: string, delay: number) => {
      timers.push(setTimeout(() => {
        setLogs(prev => [...prev, { id: mkId(), agent, message: msg, time: nowTime() }])
      }, delay))
    }

    let t = 400
    setPlannerStatus('running')
    PLANNER_SCRIPT.forEach((msg, i) => {
      pushLog('planner', msg, t)
      t += 900
      if (i === PLANNER_SCRIPT.length - 1) {
        timers.push(setTimeout(() => { setPlannerStatus('done'); setPlannerArtifact(true); setGeneratorStatus('running') }, t + 100))
      }
    })
    t += 400
    GENERATOR_SCRIPT.forEach((msg, i) => {
      pushLog('generator', msg, t)
      t += 1000
      if (i === GENERATOR_SCRIPT.length - 1) {
        timers.push(setTimeout(() => { setGeneratorStatus('done'); setGeneratorArtifact(true); setEvaluatorStatus('running') }, t + 100))
      }
    })
    t += 400
    EVALUATOR_SCRIPT.forEach((msg, i) => {
      pushLog('evaluator', msg, t)
      if (i === CONTEXT_RESET_LOG_INDEX) {
        timers.push(setTimeout(() => setContextResetEvent(true), t + 50))
      }
      t += 1100
    })
    t += 400
    pushLog('system', '培训流程已完成，即将进入模拟考试...', t)
    const totalMs = t + 200
    const tick = totalMs / 100
    const progressIv = setInterval(() => setTrainingPct(p => Math.min(p + 1, 99)), tick)
    iv.push(progressIv)
    timers.push(setTimeout(() => { clearInterval(progressIv); setTrainingPct(100); setEvaluatorStatus('done'); setEvalRubric(true) }, t))
    timers.push(setTimeout(() => setPhase('exam'), t + 1800))
    return () => { timers.forEach(clearTimeout); iv.forEach(clearInterval) }
  }, [phase])

  // Exam simulation
  useEffect(() => {
    if (phase !== 'exam' || !employee) return
    const cases = buildExamCases(employee.capabilities.map(c => c.name))
    setExamCases(cases)
    const timers: ReturnType<typeof setTimeout>[] = []
    let scoreSum = 0
    cases.forEach((c, i) => {
      timers.push(setTimeout(() => setExamCases(prev => prev.map((p, pi) => pi === i ? { ...p, status: 'running' } : p)), i * 1600 + 500))
      timers.push(setTimeout(() => {
        scoreSum += c.score
        setExamCases(prev => prev.map((p, pi) => pi === i ? { ...p, status: 'done' } : p))
        setExamPct(Math.round((i + 1) / cases.length * 100))
        setExamScore(Math.round(scoreSum / (i + 1)))
      }, i * 1600 + 1500))
    })
    const finishAt = cases.length * 1600 + 1800
    timers.push(setTimeout(() => { setExamDone(true); setTimeout(() => setPhase('report'), 1500) }, finishAt))
    return () => timers.forEach(clearTimeout)
  }, [phase])

  const addFiles = useCallback((setter: React.Dispatch<React.SetStateAction<FileItem[]>>, files: FileList | File[]) => {
    const items = Array.from(files).map(f => ({
      id: mkId(), name: f.name, size: f.size, ext: getExt(f.name), status: '解析中' as const,
    }))
    setter(prev => [...prev, ...items])
    items.forEach(f => setTimeout(() => {
      setter(prev => prev.map(p => p.id === f.id ? { ...p, status: '已解析' } : p))
    }, 1500))
  }, [])

  const resetTraining = useCallback(() => {
    setLogs([]); setPlannerStatus('waiting'); setGeneratorStatus('waiting'); setEvaluatorStatus('waiting')
    setTrainingPct(0); setExamCases([]); setExamPct(0); setExamScore(0); setExamDone(false)
    setPlannerArtifact(false); setGeneratorArtifact(false); setContextResetEvent(false); setEvalRubric(false)
  }, [])

  function sendChat() {
    const text = chatInput.trim()
    if (!text) return
    setChatMessages(prev => [...prev, { id: mkId(), role: 'user', content: text }])
    setChatInput('')
    setTimeout(() => {
      setChatMessages(prev => [...prev, {
        id: mkId(), role: 'bot',
        content: '收到你的问题，我正在处理当前培训阶段的任务。如有疑问可以随时问我。',
      }])
    }, 800)
  }

  if (!employee) {
    return <div className="flex items-center justify-center h-full text-slate-400">员工不存在</div>
  }

  const aiPassed = examScore >= 75
  const caps = employee.capabilities.map(c => c.name)
  const stepIdx = phaseToStep(phase)

  return (
    <div className="flex flex-col h-full">
      {/* Header */}
      <div className="px-6 pt-5 pb-4 border-b border-slate-100 bg-white shrink-0">
        <button onClick={() => navigate(`/instances/${id}`)}
          className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 mb-3 transition-colors"
        >
          <ArrowLeft size={14} /> 返回员工详情
        </button>
        <div className="flex items-center gap-3 mb-4">
          <div className="w-9 h-9 bg-violet-100 rounded-xl flex items-center justify-center">
            <Brain size={18} className="text-violet-600" />
          </div>
          <div>
            <h1 className="text-base font-bold text-slate-800">培训 · 评估 · 实习 · 上岗 — {employee.nickname}</h1>
            <p className="text-xs text-slate-500">{employee.roleName}</p>
          </div>
        </div>
        <Stepper steps={TOP_STEPS} current={Math.min(stepIdx, 3)} />
      </div>

      {/* Main split layout */}
      <div className="flex flex-1 overflow-hidden">
        {/* Left: Chatbot */}
        <div className="w-80 shrink-0 border-r border-slate-100 flex flex-col bg-white">
          <div className="px-4 py-3 border-b border-slate-100 shrink-0">
            <div className="flex items-center gap-2">
              <div className="w-7 h-7 bg-violet-600 rounded-lg flex items-center justify-center">
                <Bot size={14} className="text-white" />
              </div>
              <div>
                <div className="text-xs font-semibold text-slate-800">培训助手</div>
                <div className="text-[10px] text-slate-400">引导培训全流程</div>
              </div>
            </div>
          </div>

          {/* Messages */}
          <div className="flex-1 overflow-y-auto px-4 py-3 space-y-3">
            {chatMessages.map(msg => (
              <div key={msg.id} className={`flex gap-2 ${msg.role === 'user' ? 'flex-row-reverse' : ''}`}>
                {msg.role === 'bot' && (
                  <div className="w-6 h-6 bg-violet-100 rounded-full flex items-center justify-center shrink-0 mt-0.5">
                    <Bot size={12} className="text-violet-600" />
                  </div>
                )}
                <div className={`max-w-[85%] px-3 py-2 rounded-2xl text-xs leading-relaxed whitespace-pre-wrap ${
                  msg.role === 'bot'
                    ? 'bg-slate-100 text-slate-700 rounded-tl-sm'
                    : 'bg-violet-600 text-white rounded-tr-sm'
                }`}>
                  {msg.content}
                </div>
              </div>
            ))}
            <div ref={chatEndRef} />
          </div>

          {/* Input */}
          <div className="px-4 py-3 border-t border-slate-100 shrink-0">
            <div className="flex items-center gap-2 bg-slate-50 rounded-xl px-3 py-2">
              <input
                className="flex-1 bg-transparent text-xs text-slate-700 outline-none placeholder:text-slate-400"
                placeholder="有问题可以问我..."
                value={chatInput}
                onChange={e => setChatInput(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && !e.shiftKey && sendChat()}
              />
              <button onClick={sendChat} disabled={!chatInput.trim()}
                className={`p-1.5 rounded-lg transition-colors ${chatInput.trim() ? 'bg-violet-600 text-white hover:bg-violet-700' : 'bg-slate-200 text-slate-400'}`}
              >
                <Send size={11} />
              </button>
            </div>
          </div>
        </div>

        {/* Right: evaluation process & outputs */}
        <div className="flex-1 overflow-y-auto p-6 bg-slate-50">
          <div className="max-w-2xl mx-auto">
            <div className="bg-white rounded-2xl border border-slate-100 p-6">
              {phase === 'materials' && (
                <RightMaterials
                  caps={caps}
                  skillFiles={skillFiles} sliceFiles={sliceFiles}
                  skillRef={skillRef} sliceRef={sliceRef}
                  onSkill={f => addFiles(setSkillFiles, f)}
                  onSlice={f => addFiles(setSliceFiles, f)}
                  rmSkill={fid => setSkillFiles(p => p.filter(f => f.id !== fid))}
                  rmSlice={fid => setSliceFiles(p => p.filter(f => f.id !== fid))}
                  onStart={() => { resetTraining(); setPhase('training') }}
                  canStart={skillFiles.length > 0 || sliceFiles.length > 0}
                />
              )}
              {phase === 'training' && (
                <RightTraining
                  logs={logs} logsRef={logsRef}
                  planner={plannerStatus} generator={generatorStatus} evaluator={evaluatorStatus}
                  pct={trainingPct}
                  plannerDone={plannerArtifact} generatorDone={generatorArtifact}
                  contextReset={contextResetEvent} evalDone={evalRubric}
                />
              )}
              {phase === 'exam' && (
                <RightExam cases={examCases} pct={examPct} score={examScore} done={examDone} />
              )}
              {phase === 'report' && (
                <RightReport
                  score={examScore} cases={examCases} aiPassed={aiPassed}
                  onProceed={() => setPhase(aiPassed ? 'real-data' : 'evolution')}
                />
              )}
              {phase === 'real-data' && (
                <RightRealData
                  files={realFiles} inputRef={realRef}
                  onFiles={f => addFiles(setRealFiles, f)}
                  rmFile={fid => setRealFiles(p => p.filter(f => f.id !== fid))}
                  onSubmit={() => setPhase('decision')}
                  canSubmit={realFiles.length > 0}
                />
              )}
              {phase === 'decision' && (
                <RightDecision
                  employee={employee} realFiles={realFiles} examScore={examScore}
                  onApprove={() => {
                    updateAnyEmployee(employee.id, mockEmployees, {
                      lifecycleStatus: '实习中',
                      stageSummary: '已完成培训考核，进入实习阶段',
                      primarySignal: '实习中，积累评估数据',
                      signalLevel: 'ok',
                      internshipStartAt: new Date().toISOString().split('T')[0],
                    })
                    setPhase('onboard')
                  }}
                  onReject={() => { setEvolutionRound(r => r + 1); setPhase('evolution') }}
                />
              )}
              {phase === 'evolution' && (
                <RightEvolution
                  files={evoFiles} inputRef={evoRef}
                  onFiles={f => addFiles(setEvoFiles, f)}
                  rmFile={fid => setEvoFiles(p => p.filter(f => f.id !== fid))}
                  roundCount={evolutionRound}
                  onStart={() => { setEvoFiles([]); resetTraining(); setPhase('training') }}
                  canStart={evoFiles.length > 0}
                />
              )}
              {phase === 'onboard' && (
                <RightOnboard employee={employee} onGoTeam={() => navigate('/team')} />
              )}
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
