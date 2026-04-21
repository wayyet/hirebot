import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import {
  ArrowLeft, Upload, FileText, CheckCircle, XCircle, AlertTriangle,
  Loader2, ChevronLeft, ChevronRight, Play, BarChart2, X,
} from 'lucide-react'
import { digitalEmployees as mockEmployees } from '../mock/data'
import { loadUserEmployees } from '../utils/storage'

// ─── Types ────────────────────────────────────────────────────────────────────

interface Config {
  mode: 'mock' | 'real'
  maxRounds: number
  passingScore: number
}

interface Dimension {
  name: string
  weight: number | 'dynamic'
  score: number
}

interface Issue {
  id: string
  title: string
  impact: string
  severity: '严重' | '高' | '中' | '低'
}

interface Suggestion {
  text: string
  expectedImprovement: string
}

interface Modification {
  id: string
  type: 'system_prompt' | 'few_shot' | 'tool_config' | 'rule'
  title: string
  content: string
  before?: string
  after?: string
  expectedGain: string
}

interface RoundResult {
  round: number
  score: number
  passed: boolean
  dimensions: Dimension[]
  issues: Issue[]
  suggestions: Suggestion[]
  modifications: Modification[]
}

// ─── Mock Data ────────────────────────────────────────────────────────────────

const MOCK_ANALYSIS = {
  title: '销售跟进助理',
  description: '负责商机跟进、CRM 同步和销售提醒，帮助销售团队不漏单',
  tags: ['商机追踪', '自动提醒', 'CRM 同步', '通话纪要'],
  dimensions: [
    { name: '功能完整性', weight: 0.25 as number | 'dynamic' },
    { name: '交互质量', weight: 0.30 as number | 'dynamic' },
    { name: '流程合规性', weight: 0.20 as number | 'dynamic' },
    { name: '问题解决', weight: 0.25 as number | 'dynamic' },
    { name: '工具调用正确性', weight: 'dynamic' as number | 'dynamic' },
  ],
  executionFlow: ['接收商机信号', '判断跟进优先级', '生成提醒并推送', '记录操作日志到 CRM', '闭环确认'],
  testCases: [
    { input: '商机"华东区-某科技公司"已 15 天未跟进', expected: '触发提醒 → 通知销售经理 → 更新 CRM 状态' },
    { input: '销售与客户通话 30 分钟，讨论产品方案', expected: '生成结构化纪要，推送给相关成员' },
  ],
}

const MOCK_ROUNDS: RoundResult[] = [
  {
    round: 1,
    score: 48,
    passed: false,
    dimensions: [
      { name: '功能完整性', weight: 0.25, score: 48 },
      { name: '交互质量', weight: 0.30, score: 47 },
      { name: '流程合规性', weight: 0.20, score: 48 },
      { name: '问题解决', weight: 0.25, score: 49 },
      { name: '工具调用正确性', weight: 0.05, score: 48 },
    ],
    issues: [
      { id: 'ISS-001', title: '未按照流程执行必要步骤', impact: '业务无法推进', severity: '严重' },
      { id: 'ISS-002', title: '工具调用错误/遗漏', impact: '无工单记录、无法追溯', severity: '高' },
    ],
    suggestions: [
      { text: '增加流程强制规则，确保所有步骤执行', expectedImprovement: '流程合规性提升至 60 分以上' },
      { text: '增加工具调用校验，未完成工具调用无法结束流程', expectedImprovement: '工具调用正确性提升至 80 分以上' },
    ],
    modifications: [
      {
        id: 'MOD-001',
        type: 'system_prompt',
        title: 'Prompt 优化',
        content: '增加流程强制规则，要求必须执行所有必要步骤',
        before: '你是销售跟进助理，负责处理商机信息。',
        after: '你是销售跟进助理。你必须按顺序执行：① 判断优先级 → ② 生成提醒 → ③ 推送负责人 → ④ 更新 CRM，每步不可跳过。',
        expectedGain: '流程合规性 +20，交互质量 +15',
      },
      {
        id: 'MOD-002',
        type: 'few_shot',
        title: '增加工具调用示例',
        content: '添加 3 组工具调用的 few-shot 示例，明确调用时机和参数格式',
        expectedGain: '工具调用正确性 +25',
      },
    ],
  },
  {
    round: 2,
    score: 66,
    passed: false,
    dimensions: [
      { name: '功能完整性', weight: 0.25, score: 63 },
      { name: '交互质量', weight: 0.30, score: 68 },
      { name: '流程合规性', weight: 0.20, score: 58 },
      { name: '问题解决', weight: 0.25, score: 70 },
      { name: '工具调用正确性', weight: 0.05, score: 73 },
    ],
    issues: [
      { id: 'ISS-003', title: '部分流程细节不符合规划', impact: '输出结果存在风险偏差', severity: '中' },
    ],
    suggestions: [
      { text: '完善流程细节校验规则，补充规范说明', expectedImprovement: '流程合规性提升至 80 分以上' },
    ],
    modifications: [
      {
        id: 'MOD-003',
        type: 'system_prompt',
        title: 'Prompt 优化',
        content: '完善流程细节要求，补充规范说明',
        before: '指当前规则中未完整覆盖所有节点说明，应当确保所有用户反馈得到妥善处置。',
        after: '你必须在每个流程节点完成后输出当前状态摘要，格式为：[步骤X完成] + 执行结果描述。遇到异常必须立即上报。',
        expectedGain: '流程合规性 +20，交互质量 +15',
      },
    ],
  },
  {
    round: 3,
    score: 79,
    passed: true,
    dimensions: [
      { name: '功能完整性', weight: 0.25, score: 80 },
      { name: '交互质量', weight: 0.30, score: 78 },
      { name: '流程合规性', weight: 0.20, score: 82 },
      { name: '问题解决', weight: 0.25, score: 76 },
      { name: '工具调用正确性', weight: 0.05, score: 88 },
    ],
    issues: [],
    suggestions: [],
    modifications: [],
  },
]

// ─── Score Trend Chart ────────────────────────────────────────────────────────

function ScoreTrendChart({ rounds, passingScore }: { rounds: RoundResult[]; passingScore: number }) {
  if (rounds.length === 0) return null
  const W = 560
  const H = 140
  const PAD = { top: 16, right: 24, bottom: 28, left: 36 }
  const innerW = W - PAD.left - PAD.right
  const innerH = H - PAD.top - PAD.bottom

  const scores = rounds.map(r => r.score)
  const maxX = Math.max(rounds.length - 1, 1)

  const px = (i: number) => PAD.left + (i / maxX) * innerW
  const py = (s: number) => PAD.top + innerH - (s / 100) * innerH

  const pathD = scores.map((s, i) => `${i === 0 ? 'M' : 'L'} ${px(i)} ${py(s)}`).join(' ')
  const areaD = `${pathD} L ${px(scores.length - 1)} ${PAD.top + innerH} L ${PAD.left} ${PAD.top + innerH} Z`

  const thresholdY = py(passingScore)
  const yLabels = [0, 25, 50, 75, 100]

  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="w-full" style={{ maxHeight: 140 }}>
      {/* Grid lines */}
      {yLabels.map(v => (
        <g key={v}>
          <line x1={PAD.left} y1={py(v)} x2={W - PAD.right} y2={py(v)} stroke="#f1f5f9" strokeWidth="1" />
          <text x={PAD.left - 6} y={py(v) + 4} textAnchor="end" fontSize="9" fill="#94a3b8">{v}</text>
        </g>
      ))}

      {/* Passing score line */}
      <line x1={PAD.left} y1={thresholdY} x2={W - PAD.right} y2={thresholdY} stroke="#22c55e" strokeWidth="1" strokeDasharray="4 3" />
      <text x={W - PAD.right + 4} y={thresholdY + 4} fontSize="9" fill="#22c55e">合格线</text>

      {/* Area fill */}
      <path d={areaD} fill="#6366f1" fillOpacity="0.08" />

      {/* Line */}
      <path d={pathD} fill="none" stroke="#6366f1" strokeWidth="2" strokeLinejoin="round" />

      {/* Dots + labels */}
      {scores.map((s, i) => (
        <g key={i}>
          <circle cx={px(i)} cy={py(s)} r="4" fill={s >= passingScore ? '#22c55e' : '#6366f1'} stroke="white" strokeWidth="1.5" />
          <text x={px(i)} y={py(s) - 8} textAnchor="middle" fontSize="10" fontWeight="600"
            fill={s >= passingScore ? '#16a34a' : '#6366f1'}>{s}</text>
          <text x={px(i)} y={PAD.top + innerH + 16} textAnchor="middle" fontSize="9" fill="#94a3b8">
            第{i + 1}轮
          </text>
        </g>
      ))}
    </svg>
  )
}

// ─── Step Progress Bar ────────────────────────────────────────────────────────

const STEPS = ['上传素材', '场景解析', '训练评估', '结果输出']

function StepBar({ current }: { current: number }) {
  return (
    <div className="flex items-center justify-center gap-0 mb-6 select-none">
      {STEPS.map((label, i) => {
        const idx = i + 1
        const done = idx < current
        const active = idx === current
        return (
          <div key={idx} className="flex items-center">
            <div className="flex flex-col items-center">
              <div className={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-bold transition-all ${
                done ? 'bg-emerald-500 text-white' :
                active ? 'bg-indigo-600 text-white ring-4 ring-indigo-100' :
                'bg-slate-200 text-slate-500'
              }`}>
                {done ? <CheckCircle size={16} /> : idx}
              </div>
              <span className={`text-xs mt-1 font-medium ${active ? 'text-indigo-600' : done ? 'text-emerald-600' : 'text-slate-400'}`}>
                {label}
              </span>
            </div>
            {i < STEPS.length - 1 && (
              <div className={`w-24 h-0.5 mb-4 mx-1 transition-all ${done ? 'bg-emerald-400' : 'bg-slate-200'}`} />
            )}
          </div>
        )
      })}
    </div>
  )
}

// ─── Step 1: Upload ───────────────────────────────────────────────────────────

function Step1Upload({
  config, setConfig, file, setFile, onNext,
}: {
  config: Config
  setConfig: (c: Config) => void
  file: File | null
  setFile: (f: File | null) => void
  onNext: () => void
}) {
  const [dragging, setDragging] = useState(false)

  const handleFile = (f: File) => setFile(f)

  return (
    <div className="space-y-5">
      {/* Config */}
      <div className="bg-slate-50 rounded-xl border border-slate-200 p-4">
        <div className="flex items-center gap-2 mb-3 text-sm font-semibold text-slate-700">
          ⚙️ 运行配置
        </div>
        <div className="grid grid-cols-3 gap-4">
          <div>
            <label className="text-xs text-slate-500 mb-1 block">运行模式</label>
            <select
              value={config.mode}
              onChange={e => setConfig({ ...config, mode: e.target.value as Config['mode'] })}
              className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm outline-none focus:border-indigo-400 bg-white"
            >
              <option value="mock">Mock 模式（无需 API Key，演示用）</option>
              <option value="real">真实模式（需要配置 API Key）</option>
            </select>
          </div>
          <div>
            <label className="text-xs text-slate-500 mb-1 block">最大训练轮次</label>
            <input
              type="number"
              value={config.maxRounds}
              onChange={e => setConfig({ ...config, maxRounds: Number(e.target.value) })}
              min={1} max={30}
              className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm outline-none focus:border-indigo-400"
            />
          </div>
          <div>
            <label className="text-xs text-slate-500 mb-1 block">合格分数线</label>
            <input
              type="number"
              value={config.passingScore}
              onChange={e => setConfig({ ...config, passingScore: Number(e.target.value) })}
              min={50} max={100}
              className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm outline-none focus:border-indigo-400"
            />
          </div>
        </div>
      </div>

      {/* Drop zone */}
      <div
        onDragOver={e => { e.preventDefault(); setDragging(true) }}
        onDragLeave={() => setDragging(false)}
        onDrop={e => { e.preventDefault(); setDragging(false); const f = e.dataTransfer.files[0]; if (f) handleFile(f) }}
        className={`border-2 border-dashed rounded-xl p-12 text-center transition-all ${
          dragging ? 'border-indigo-400 bg-indigo-50' : 'border-slate-300 hover:border-indigo-300 hover:bg-slate-50'
        }`}
      >
        <Upload size={40} className="text-slate-400 mx-auto mb-3" />
        <div className="text-base font-semibold text-slate-700 mb-1">上传岗位素材文件</div>
        <div className="text-sm text-slate-500 mb-4">
          支持 txt、md、docx 格式，系统将自动解析岗位信息、评估标准和测试用例<br />
          或将文件拖拽到此处上传
        </div>
        <label className="inline-flex items-center gap-2 px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors cursor-pointer">
          <FileText size={14} />
          选择文件
          <input type="file" accept=".txt,.md,.docx,.pdf" className="hidden"
            onChange={e => { const f = e.target.files?.[0]; if (f) handleFile(f) }} />
        </label>
      </div>

      {/* Uploaded file */}
      {file && (
        <div className="flex items-center justify-between px-4 py-3 bg-indigo-50 border border-indigo-200 rounded-xl">
          <div className="flex items-center gap-3">
            <div className="w-8 h-8 bg-indigo-100 rounded-lg flex items-center justify-center">
              <FileText size={16} className="text-indigo-600" />
            </div>
            <div>
              <div className="text-sm font-medium text-slate-700">{file.name}</div>
              <div className="text-xs text-slate-400">{(file.size / 1024).toFixed(1)} KB</div>
            </div>
          </div>
          <button onClick={() => setFile(null)} className="text-slate-400 hover:text-slate-600">
            <X size={16} />
          </button>
        </div>
      )}

      {/* Footer */}
      <div className="flex justify-end pt-2">
        <button
          onClick={onNext}
          disabled={!file}
          className={`flex items-center gap-2 px-5 py-2.5 rounded-xl text-sm font-medium transition-all ${
            file ? 'bg-indigo-600 text-white hover:bg-indigo-700' : 'bg-slate-100 text-slate-400 cursor-not-allowed'
          }`}
        >
          下一步：场景解析 →
        </button>
      </div>
    </div>
  )
}

// ─── Step 2: Analysis ─────────────────────────────────────────────────────────

function Step2Analysis({ onBack, onStart }: { onBack: () => void; onStart: () => void }) {
  const a = MOCK_ANALYSIS
  return (
    <div className="space-y-5">
      {/* Profile card */}
      <div className="bg-slate-50 rounded-xl border border-slate-200 p-5">
        <h2 className="text-base font-bold text-slate-800 mb-1">{a.title}</h2>
        <p className="text-sm text-slate-500 mb-3">{a.description}</p>
        <div className="flex flex-wrap gap-2">
          {a.tags.map(t => (
            <span key={t} className="px-2.5 py-1 bg-indigo-100 text-indigo-700 text-xs rounded-full font-medium">{t}</span>
          ))}
        </div>
      </div>

      {/* Dimension weights */}
      <div>
        <div className="flex items-center gap-2 text-sm font-semibold text-slate-700 mb-3">
          <BarChart2 size={15} />
          评估维度权重
        </div>
        <div className="grid grid-cols-5 gap-3">
          {a.dimensions.map(d => (
            <div key={d.name} className="bg-white border border-slate-200 rounded-xl p-3 text-center">
              <div className="text-xs text-slate-500 mb-1 leading-tight">{d.name}</div>
              <div className="text-lg font-bold text-indigo-600">
                {d.weight === 'dynamic' ? '动态' : `${Math.round((d.weight as number) * 100)}%`}
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Execution flow */}
      <div>
        <div className="text-sm font-semibold text-slate-700 mb-3">☰ 预期执行流程</div>
        <div className="bg-white border border-slate-200 rounded-xl divide-y divide-slate-100">
          {a.executionFlow.map((step, i) => (
            <div key={i} className="flex items-center gap-3 px-4 py-2.5">
              <div className="w-6 h-6 rounded-full bg-indigo-100 text-indigo-700 text-xs font-bold flex items-center justify-center shrink-0">
                {i + 1}
              </div>
              <span className="text-sm text-slate-700">{step}</span>
            </div>
          ))}
        </div>
      </div>

      {/* Test cases */}
      <div>
        <div className="text-sm font-semibold text-slate-700 mb-3">🧪 测试用例</div>
        {a.testCases.map((tc, i) => (
          <div key={i} className="border border-amber-200 bg-amber-50 rounded-xl p-4 mb-2">
            <div className="grid grid-cols-2 gap-4 text-xs">
              <div>
                <div className="font-semibold text-amber-700 mb-1">用户输入</div>
                <div className="text-slate-700">{tc.input}</div>
              </div>
              <div>
                <div className="font-semibold text-emerald-700 mb-1">预期输出</div>
                <div className="text-slate-700">{tc.expected}</div>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Footer */}
      <div className="flex items-center justify-between pt-2">
        <button onClick={onBack} className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700">
          <ChevronLeft size={16} />
          返回上传
        </button>
        <button
          onClick={onStart}
          className="flex items-center gap-2 px-5 py-2.5 bg-emerald-600 text-white rounded-xl text-sm font-medium hover:bg-emerald-700 transition-colors"
        >
          <Play size={14} />
          开始训练
        </button>
      </div>
    </div>
  )
}

// ─── Step 3: Training ─────────────────────────────────────────────────────────

const SEVERITY_STYLE: Record<string, string> = {
  '严重': 'bg-red-100 text-red-700 border border-red-200',
  '高': 'bg-orange-100 text-orange-700 border border-orange-200',
  '中': 'bg-yellow-100 text-yellow-700 border border-yellow-200',
  '低': 'bg-slate-100 text-slate-600 border border-slate-200',
}

type ReviewDecision = 'confirmed' | 'override_pass' | 'override_reject'

interface ReviewRecord {
  decision: ReviewDecision
  comment: string
}

// 人工审核面板：每轮结束后必须经过此节点才能进入下一轮或结束
function HumanReviewPanel({
  round,
  aiPassed,
  onConfirm,
}: {
  round: RoundResult
  aiPassed: boolean
  onConfirm: (decision: ReviewDecision, comment: string) => void
}) {
  const [comment, setComment] = useState('')
  const [overrideMode, setOverrideMode] = useState<'pass' | 'reject' | null>(null)

  const submit = (decision: ReviewDecision) => {
    if ((decision === 'override_pass' || decision === 'override_reject') && !comment.trim()) return
    onConfirm(decision, comment)
  }

  return (
    <div className="border-2 border-amber-300 bg-amber-50 rounded-2xl p-5 space-y-4">
      {/* Header */}
      <div className="flex items-center gap-2">
        <div className="w-6 h-6 rounded-full bg-amber-400 flex items-center justify-center shrink-0">
          <AlertTriangle size={13} className="text-white" />
        </div>
        <span className="text-sm font-bold text-amber-800">人工审核节点 · 第 {round.round} 轮</span>
        <span className="ml-auto text-xs text-amber-600 bg-amber-100 border border-amber-200 px-2 py-0.5 rounded-full font-medium">
          待审核
        </span>
      </div>

      {/* AI decision summary */}
      <div className="bg-white rounded-xl border border-amber-200 p-4">
        <div className="text-xs text-slate-500 mb-2 font-medium">AI 评估结论</div>
        <div className="flex items-center gap-3">
          <span className={`text-2xl font-black ${aiPassed ? 'text-emerald-500' : 'text-red-500'}`}>{round.score}</span>
          <div>
            {aiPassed
              ? <span className="text-xs font-semibold text-emerald-700 bg-emerald-50 border border-emerald-200 px-2.5 py-1 rounded-full">AI 判定：合格</span>
              : <span className="text-xs font-semibold text-red-600 bg-red-50 border border-red-200 px-2.5 py-1 rounded-full">AI 判定：不合格</span>
            }
            <div className="text-xs text-slate-400 mt-1">
              {aiPassed
                ? '已达到合格分数线，建议通过'
                : `综合得分 ${round.score} 分，未达合格线，建议继续训练`}
            </div>
          </div>
        </div>
      </div>

      {/* Comment */}
      <div>
        <label className="text-xs font-medium text-slate-600 mb-1.5 block">
          审核备注{overrideMode ? '（Override 必填）' : '（选填）'}
        </label>
        <textarea
          value={comment}
          onChange={e => setComment(e.target.value)}
          rows={2}
          placeholder={overrideMode ? '请说明 Override 原因…' : '填写审核意见（可选）…'}
          className="w-full px-3 py-2 border border-amber-200 rounded-lg text-sm outline-none focus:border-amber-400 resize-none bg-white"
        />
      </div>

      {/* Action buttons */}
      {overrideMode === null ? (
        <div className="space-y-2">
          {/* Confirm AI decision */}
          <button
            onClick={() => submit('confirmed')}
            className="w-full py-2.5 bg-indigo-600 text-white rounded-xl text-sm font-medium hover:bg-indigo-700 transition-colors flex items-center justify-center gap-2"
          >
            <CheckCircle size={14} />
            确认 AI 判定（{aiPassed ? '通过，进入结果页' : '不通过，进入下一轮训练'}）
          </button>

          {/* Override options */}
          <div className="flex gap-2">
            {!aiPassed && (
              <button
                onClick={() => setOverrideMode('pass')}
                className="flex-1 py-2 border border-emerald-400 text-emerald-700 rounded-xl text-xs font-medium hover:bg-emerald-50 transition-colors"
              >
                Override → 强制通过
              </button>
            )}
            {aiPassed && (
              <button
                onClick={() => setOverrideMode('reject')}
                className="flex-1 py-2 border border-red-300 text-red-600 rounded-xl text-xs font-medium hover:bg-red-50 transition-colors"
              >
                Override → 打回，继续训练
              </button>
            )}
          </div>
        </div>
      ) : (
        <div className="space-y-2">
          <div className={`text-xs font-semibold px-3 py-2 rounded-lg border ${
            overrideMode === 'pass'
              ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
              : 'bg-red-50 border-red-200 text-red-700'
          }`}>
            ⚡ Override：{overrideMode === 'pass' ? '强制通过（覆盖 AI 不合格判定）' : '打回重训（覆盖 AI 合格判定）'}
          </div>
          <div className="flex gap-2">
            <button
              onClick={() => { setOverrideMode(null); setComment('') }}
              className="flex-1 py-2 border border-slate-200 text-slate-600 rounded-xl text-sm hover:bg-slate-50 transition-colors"
            >
              取消
            </button>
            <button
              onClick={() => submit(overrideMode === 'pass' ? 'override_pass' : 'override_reject')}
              disabled={!comment.trim()}
              className={`flex-1 py-2 rounded-xl text-sm font-medium transition-colors disabled:opacity-40 ${
                overrideMode === 'pass'
                  ? 'bg-emerald-600 text-white hover:bg-emerald-700'
                  : 'bg-red-500 text-white hover:bg-red-600'
              }`}
            >
              确认 Override
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

function Step3Training({
  config,
  rounds,
  reviews,
  onAddRound,
  onReview,
  onFinish,
  onBack,
}: {
  config: Config
  rounds: RoundResult[]
  reviews: Record<number, ReviewRecord>  // keyed by round index (0-based)
  onAddRound: () => void
  onReview: (roundIndex: number, decision: ReviewDecision, comment: string) => void
  onFinish: () => void
  onBack: () => void
}) {
  const [viewRound, setViewRound] = useState(rounds.length - 1)
  const [loading, setLoading] = useState(false)
  const [expandedMods, setExpandedMods] = useState<Record<string, boolean>>({})

  const current = rounds[viewRound]
  const latestIndex = rounds.length - 1
  const isViewingLatest = viewRound === latestIndex
  const latestReview = reviews[latestIndex]
  const latestRound = rounds[latestIndex]

  // Determine overall flow state from the LATEST reviewed round
  const overallPassed = (): boolean => {
    for (let i = rounds.length - 1; i >= 0; i--) {
      const rev = reviews[i]
      if (!rev) continue
      if (rev.decision === 'confirmed' && rounds[i].passed) return true
      if (rev.decision === 'override_pass') return true
    }
    return false
  }

  const reachedMax = rounds.length >= config.maxRounds
  const flowDone = overallPassed() || (reachedMax && latestReview !== undefined)

  const handleNextRound = () => {
    setLoading(true)
    setTimeout(() => {
      onAddRound()
      setLoading(false)
      setViewRound(rounds.length) // becomes the new latest after add
    }, 1800)
  }

  const handleReview = (decision: ReviewDecision, comment: string) => {
    onReview(latestIndex, decision, comment)
    if (decision === 'confirmed' && latestRound.passed) return   // → finish handled externally
    if (decision === 'override_pass') return                     // → finish handled externally
    // confirmed fail or override_reject → stay, wait for next round action
  }

  const toggleMod = (id: string) => setExpandedMods(prev => ({ ...prev, [id]: !prev[id] }))

  return (
    <div className="space-y-5">
      {/* Round selector */}
      <div className="flex items-center gap-3">
        <div className="flex gap-2 flex-wrap">
          {rounds.map((r, i) => {
            const rev = reviews[i]
            const roundPassed = rev?.decision === 'override_pass' || (rev?.decision === 'confirmed' && r.passed)
            return (
              <button
                key={i}
                onClick={() => setViewRound(i)}
                className={`px-3 py-1.5 rounded-lg text-xs font-bold border transition-all ${
                  viewRound === i
                    ? roundPassed
                      ? 'bg-emerald-500 text-white border-emerald-500'
                      : 'bg-indigo-600 text-white border-indigo-600'
                    : 'bg-white text-slate-600 border-slate-200 hover:border-slate-300'
                }`}
              >
                第{r.round}轮
                {rev && (
                  <span className="ml-1">
                    {roundPassed ? '✓' : rev.decision === 'override_reject' ? '↩' : '×'}
                  </span>
                )}
                {!rev && i === latestIndex && (
                  <span className="ml-1 text-amber-400">●</span>
                )}
              </button>
            )
          })}
        </div>
        <span className="text-xs text-slate-400">{rounds.length}/{config.maxRounds} 轮</span>
      </div>

      {/* Historical review badge (when viewing non-latest round) */}
      {!isViewingLatest && reviews[viewRound] && (
        <div className={`flex items-center gap-2 px-4 py-2.5 rounded-xl text-xs border ${
          reviews[viewRound].decision === 'override_pass'
            ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
            : reviews[viewRound].decision === 'override_reject'
            ? 'bg-orange-50 border-orange-200 text-orange-700'
            : reviews[viewRound].decision === 'confirmed' && rounds[viewRound].passed
            ? 'bg-emerald-50 border-emerald-200 text-emerald-700'
            : 'bg-slate-50 border-slate-200 text-slate-600'
        }`}>
          <CheckCircle size={12} />
          <span className="font-medium">已审核：</span>
          {reviews[viewRound].decision === 'confirmed' && rounds[viewRound].passed && '确认通过'}
          {reviews[viewRound].decision === 'confirmed' && !rounds[viewRound].passed && '确认不通过，进入下一轮训练'}
          {reviews[viewRound].decision === 'override_pass' && 'Override → 强制通过'}
          {reviews[viewRound].decision === 'override_reject' && 'Override → 打回重训'}
          {reviews[viewRound].comment && <span className="text-slate-400 ml-1">· {reviews[viewRound].comment}</span>}
        </div>
      )}

      {/* Score + chart */}
      <div className={`rounded-xl border p-5 ${current.passed ? 'bg-emerald-50 border-emerald-200' : 'bg-slate-50 border-slate-200'}`}>
        <div className="text-center mb-4">
          <div className={`text-5xl font-black mb-1 ${current.passed ? 'text-emerald-500' : current.score >= 60 ? 'text-amber-500' : 'text-slate-600'}`}>
            {current.score}
          </div>
          <div className="text-sm text-slate-500 mb-2">综合得分</div>
          {current.passed
            ? <span className="text-xs font-semibold text-emerald-700 bg-emerald-100 border border-emerald-300 px-3 py-1 rounded-full">AI 判定：合格</span>
            : <span className="text-xs font-semibold text-red-600 bg-red-50 border border-red-200 px-3 py-1 rounded-full">AI 判定：不合格</span>
          }
        </div>
        <ScoreTrendChart rounds={rounds} passingScore={config.passingScore} />
      </div>

      {/* Dimension details */}
      <div>
        <div className="text-sm font-semibold text-slate-700 mb-3 flex items-center gap-2">
          <BarChart2 size={15} />
          维度得分详情
        </div>
        <div className="grid grid-cols-5 gap-3">
          {current.dimensions.map(d => (
            <div key={d.name} className="bg-white border border-slate-200 rounded-xl p-3">
              <div className="text-xs text-slate-500 mb-1.5 leading-tight">{d.name}</div>
              <div className={`text-xl font-black mb-1 ${d.score >= 75 ? 'text-emerald-600' : d.score >= 60 ? 'text-amber-500' : 'text-red-500'}`}>
                {d.score}
              </div>
              <div className="bg-slate-100 rounded-full h-1.5">
                <div
                  className={`h-1.5 rounded-full ${d.score >= 75 ? 'bg-emerald-500' : d.score >= 60 ? 'bg-amber-400' : 'bg-red-400'}`}
                  style={{ width: `${d.score}%` }}
                />
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Issues */}
      {current.issues.length > 0 && (
        <div className="bg-red-50 border border-red-100 rounded-xl p-4">
          <div className="flex items-center gap-2 text-sm font-semibold text-red-700 mb-3">
            <AlertTriangle size={14} />
            发现的严重问题
          </div>
          <div className="space-y-2">
            {current.issues.map(issue => (
              <div key={issue.id} className="flex items-start justify-between bg-white border border-red-100 rounded-lg px-4 py-3">
                <div>
                  <div className="text-xs text-slate-400 mb-0.5">{issue.id}</div>
                  <div className="text-sm font-medium text-slate-800">{issue.title}</div>
                  <div className="text-xs text-slate-500 mt-0.5 italic">影响：{issue.impact}</div>
                </div>
                <span className={`text-xs font-semibold px-2 py-0.5 rounded-full shrink-0 ml-3 ${SEVERITY_STYLE[issue.severity]}`}>
                  {issue.severity}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Suggestions */}
      {current.suggestions.length > 0 && (
        <div className="bg-blue-50 border border-blue-100 rounded-xl p-4">
          <div className="text-sm font-semibold text-blue-700 mb-3">✦ 改进建议</div>
          <div className="space-y-2">
            {current.suggestions.map((s, i) => (
              <div key={i} className="text-sm text-blue-800">
                · {s.text}
                <span className="text-xs text-blue-500 ml-2">预期提升：{s.expectedImprovement}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Modifications */}
      {current.modifications.length > 0 && (
        <div>
          <div className="text-sm font-semibold text-slate-700 mb-3">{'<>'} 修改方案详情</div>
          <div className="space-y-3">
            {current.modifications.map(mod => (
              <div key={mod.id} className="bg-white border border-slate-200 rounded-xl overflow-hidden">
                <button
                  onClick={() => toggleMod(mod.id)}
                  className="w-full flex items-center justify-between px-4 py-3 hover:bg-slate-50 transition-colors"
                >
                  <div className="flex items-center gap-3">
                    <span className="text-xs text-slate-400">{mod.id}</span>
                    <span className="text-sm font-medium text-slate-800">{mod.title}</span>
                    <span className="text-xs px-2 py-0.5 bg-indigo-100 text-indigo-700 rounded font-mono">{mod.type}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-xs text-slate-400">{mod.expectedGain}</span>
                    {expandedMods[mod.id] ? <ChevronLeft size={14} className="rotate-90 text-slate-400" /> : <ChevronRight size={14} className="text-slate-400" />}
                  </div>
                </button>
                {expandedMods[mod.id] && (
                  <div className="border-t border-slate-100 p-4 space-y-3">
                    <p className="text-sm text-slate-600">{mod.content}</p>
                    {mod.before && mod.after && (
                      <div className="grid grid-cols-2 gap-3">
                        <div>
                          <div className="text-xs text-red-600 font-medium mb-1">修改前</div>
                          <div className="text-xs bg-red-50 border border-red-100 rounded-lg p-3 text-red-800 leading-5">{mod.before}</div>
                        </div>
                        <div>
                          <div className="text-xs text-emerald-600 font-medium mb-1">修改后</div>
                          <div className="text-xs bg-emerald-50 border border-emerald-100 rounded-lg p-3 text-emerald-800 leading-5">{mod.after}</div>
                        </div>
                      </div>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Human review panel — only for latest round, only if not yet reviewed */}
      {isViewingLatest && !latestReview && (
        <HumanReviewPanel
          round={latestRound}
          aiPassed={latestRound.passed}
          onConfirm={handleReview}
        />
      )}

      {/* Footer */}
      <div className="flex items-center justify-between pt-2 border-t border-slate-100">
        <button onClick={onBack} className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700">
          <ChevronLeft size={16} />
          返回场景解析
        </button>
        <div className="flex items-center gap-3">
          {loading && (
            <div className="flex items-center gap-2 text-sm text-indigo-600">
              <Loader2 size={14} className="animate-spin" />
              训练中…
            </div>
          )}
          {/* Next round button: shown after review confirms "fail" or "override_reject" */}
          {isViewingLatest && latestReview && !flowDone && !loading && (
            <button
              onClick={handleNextRound}
              className="flex items-center gap-2 px-5 py-2.5 bg-indigo-600 text-white rounded-xl text-sm font-medium hover:bg-indigo-700 transition-colors"
            >
              <Play size={14} />
              开始第 {rounds.length + 1} 轮训练
            </button>
          )}
          {/* Finish button: shown when flow is done */}
          {flowDone && !loading && (
            <button
              onClick={onFinish}
              className="flex items-center gap-2 px-5 py-2.5 bg-emerald-600 text-white rounded-xl text-sm font-medium hover:bg-emerald-700 transition-colors"
            >
              查看最终结果 →
            </button>
          )}
        </div>
      </div>
    </div>
  )
}

// ─── Step 4: Result ───────────────────────────────────────────────────────────

function Step4Result({
  rounds,
  reviews,
  config,
  employeeId,
}: {
  rounds: RoundResult[]
  reviews: Record<number, ReviewRecord>
  config: Config
  employeeId: string
}) {
  const navigate = useNavigate()

  // Determine final outcome considering overrides
  let finalPassed = false
  let overrideNote: string | null = null
  let finalRound: RoundResult | undefined

  for (let i = rounds.length - 1; i >= 0; i--) {
    const rev = reviews[i]
    if (!rev) continue
    if (rev.decision === 'override_pass') {
      finalPassed = true
      overrideNote = `人工 Override 通过（第 ${rounds[i].round} 轮，AI 得分 ${rounds[i].score} 分）`
      finalRound = rounds[i]
      break
    }
    if (rev.decision === 'confirmed' && rounds[i].passed) {
      finalPassed = true
      finalRound = rounds[i]
      break
    }
  }

  const passed = finalPassed
  const best = rounds.reduce((a, b) => (a.score > b.score ? a : b))
  const passedRound = finalRound

  return (
    <div className="space-y-6">
      {/* Hero */}
      <div className={`rounded-2xl p-8 text-center ${passed ? 'bg-emerald-50 border border-emerald-200' : 'bg-red-50 border border-red-200'}`}>
        <div className={`w-16 h-16 rounded-full mx-auto flex items-center justify-center mb-4 ${passed ? 'bg-emerald-500' : 'bg-red-400'}`}>
          {passed ? <CheckCircle size={32} className="text-white" /> : <XCircle size={32} className="text-white" />}
        </div>
        <div className={`text-2xl font-black mb-1 ${passed ? 'text-emerald-600' : 'text-red-500'}`}>
          {passed ? '评估通过' : '训练失败'}
        </div>
        <p className="text-sm text-slate-500">
          {passed && !overrideNote
            ? `第 ${passedRound!.round} 轮达到合格线（${config.passingScore} 分），综合得分 ${passedRound!.score} 分`
            : passed && overrideNote
            ? overrideNote
            : `已完成全部 ${config.maxRounds} 轮训练，最高分 ${best.score} 分，未达到合格线 ${config.passingScore} 分`
          }
        </p>
        {overrideNote && (
          <div className="mt-2 inline-flex items-center gap-1.5 text-xs text-amber-700 bg-amber-50 border border-amber-200 px-3 py-1.5 rounded-full">
            <AlertTriangle size={11} />
            人工 Override 决策，已覆盖 AI 不合格判定
          </div>
        )}
      </div>

      {/* Summary table */}
      <div>
        <div className="text-sm font-semibold text-slate-700 mb-3">训练过程汇总</div>
        <div className="bg-white border border-slate-200 rounded-xl overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-slate-50 text-xs text-slate-500">
              <tr>
                <th className="text-left px-4 py-2.5 font-medium">轮次</th>
                <th className="text-center px-4 py-2.5 font-medium">综合得分</th>
                <th className="text-center px-4 py-2.5 font-medium">AI 判定</th>
                <th className="text-center px-4 py-2.5 font-medium">人工审核</th>
                <th className="text-center px-4 py-2.5 font-medium">问题数</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {rounds.map((r, i) => {
                const rev = reviews[i]
                return (
                  <tr key={r.round}>
                    <td className="px-4 py-3 text-slate-700">第 {r.round} 轮</td>
                    <td className={`px-4 py-3 text-center font-bold ${r.passed ? 'text-emerald-600' : 'text-slate-700'}`}>{r.score}</td>
                    <td className="px-4 py-3 text-center">
                      {r.passed
                        ? <span className="text-xs text-emerald-600 bg-emerald-50 border border-emerald-200 px-2 py-0.5 rounded-full font-medium">合格</span>
                        : <span className="text-xs text-red-500 bg-red-50 border border-red-100 px-2 py-0.5 rounded-full">不合格</span>
                      }
                    </td>
                    <td className="px-4 py-3 text-center">
                      {!rev ? <span className="text-xs text-slate-400">—</span> :
                        rev.decision === 'confirmed'
                          ? <span className="text-xs text-slate-600">确认</span>
                          : rev.decision === 'override_pass'
                          ? <span className="text-xs text-amber-700 bg-amber-50 border border-amber-200 px-2 py-0.5 rounded-full font-medium">Override 通过</span>
                          : <span className="text-xs text-orange-600 bg-orange-50 border border-orange-200 px-2 py-0.5 rounded-full">Override 打回</span>
                      }
                    </td>
                    <td className="px-4 py-3 text-center text-slate-500">{r.issues.length}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </div>

      {/* Final dimension scores */}
      {passedRound && (
        <div>
          <div className="text-sm font-semibold text-slate-700 mb-3">最终维度得分</div>
          <div className="grid grid-cols-5 gap-3">
            {passedRound.dimensions.map(d => (
              <div key={d.name} className="bg-white border border-emerald-200 rounded-xl p-3 text-center">
                <div className="text-xs text-slate-500 mb-1 leading-tight">{d.name}</div>
                <div className="text-2xl font-black text-emerald-600">{d.score}</div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Actions */}
      <div className="flex items-center justify-between pt-2">
        <div />
        {passed ? (
          <button
            onClick={() => navigate(`/instances/${employeeId}`)}
            className="px-6 py-2.5 bg-emerald-600 text-white rounded-xl text-sm font-medium hover:bg-emerald-700 transition-colors"
          >
            返回员工详情，办理转正
          </button>
        ) : (
          <div className="flex gap-3">
            <button
              onClick={() => navigate(`/instances/${employeeId}`)}
              className="px-4 py-2.5 border border-slate-200 text-slate-600 rounded-xl text-sm hover:bg-slate-50 transition-colors"
            >
              返回员工详情
            </button>
            <button
              onClick={() => window.location.reload()}
              className="px-4 py-2.5 bg-indigo-600 text-white rounded-xl text-sm font-medium hover:bg-indigo-700 transition-colors"
            >
              重置并重新训练
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

// ─── Main Page ────────────────────────────────────────────────────────────────

export default function EvaluationPage() {
  const { id } = useParams()
  const navigate = useNavigate()

  const userEmployees = loadUserEmployees()
  const allEmployees = [...mockEmployees, ...userEmployees]
  const e = allEmployees.find(emp => emp.id === id)

  const [step, setStep] = useState(1)
  const [config, setConfig] = useState<Config>({ mode: 'mock', maxRounds: 4, passingScore: 75 })
  const [file, setFile] = useState<File | null>(null)
  const [rounds, setRounds] = useState<RoundResult[]>([])
  const [reviews, setReviews] = useState<Record<number, ReviewRecord>>({})

  if (!e) return <div className="flex items-center justify-center h-full text-slate-400">员工不存在</div>

  const handleStartTraining = () => {
    setRounds([MOCK_ROUNDS[0]])
    setReviews({})
    setStep(3)
  }

  const handleAddRound = () => {
    const nextIndex = rounds.length
    if (nextIndex < MOCK_ROUNDS.length) {
      setRounds(prev => [...prev, MOCK_ROUNDS[nextIndex]])
    }
  }

  const handleReview = (roundIndex: number, decision: ReviewDecision, comment: string) => {
    setReviews(prev => ({ ...prev, [roundIndex]: { decision, comment } }))
    // Auto-advance to step 4 on pass decisions
    if (decision === 'override_pass') setStep(4)
    if (decision === 'confirmed' && rounds[roundIndex].passed) setStep(4)
    // If max rounds reached after this review, also advance
    const reachedMax = rounds.length >= config.maxRounds
    if (reachedMax && (decision === 'confirmed' || decision === 'override_reject')) setStep(4)
  }

  return (
    <div className="max-w-4xl mx-auto px-6 py-6">
      <button
        onClick={() => navigate(`/instances/${id}`)}
        className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 mb-5 transition-colors"
      >
        <ArrowLeft size={14} />
        返回实例详情
      </button>

      <div className="mb-6">
        <h1 className="text-xl font-bold text-slate-800">AI 评估训练</h1>
        <p className="text-sm text-slate-500 mt-0.5">{e.nickname} · {e.roleName}</p>
      </div>

      <div className="bg-white rounded-2xl border border-slate-100 p-6">
        <StepBar current={step} />

        {step === 1 && (
          <Step1Upload
            config={config}
            setConfig={setConfig}
            file={file}
            setFile={setFile}
            onNext={() => setStep(2)}
          />
        )}

        {step === 2 && (
          <Step2Analysis
            onBack={() => setStep(1)}
            onStart={handleStartTraining}
          />
        )}

        {step === 3 && rounds.length > 0 && (
          <Step3Training
            config={config}
            rounds={rounds}
            reviews={reviews}
            onAddRound={handleAddRound}
            onReview={handleReview}
            onFinish={() => setStep(4)}
            onBack={() => setStep(2)}
          />
        )}

        {step === 4 && (
          <Step4Result
            rounds={rounds}
            reviews={reviews}
            config={config}
            employeeId={id!}
          />
        )}
      </div>
    </div>
  )
}
