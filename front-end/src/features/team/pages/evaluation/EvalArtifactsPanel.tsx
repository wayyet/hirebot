import { BarChart2, FileText, Zap, Check, ChevronDown, ExternalLink, Loader2, Bot, User, AlertCircle } from 'lucide-react'
import type {
  EvaluationState,
  EvaluationWorkspaceStatus,
  EvaluationReportSummary,
  EvaluationQuestionCard,
  EvaluationTestcaseOutline,
  EvaluationAssetRef,
} from '@/infra/api'
import type { ArtifactTab, TraceJsonData } from './evaluationTypes'
import { formatDateTime, toAbsoluteApiUrl } from './evaluationUtils'

interface ReportMetric {
  label: string
  value: string
  tone: string
}

interface EvalArtifactsPanelProps {
  artifactTab: ArtifactTab
  rightCollapsed: boolean
  reportSummary: EvaluationReportSummary | null
  reportMetrics: ReportMetric[]
  evaluation: EvaluationState
  workspaceStatus: EvaluationWorkspaceStatus | null
  testcaseItems: EvaluationTestcaseOutline[]
  questionCardMap: Map<string, EvaluationQuestionCard>
  traceAssets: EvaluationAssetRef[]
  traceDataCache: Record<string, TraceJsonData | 'loading' | 'error'>
  expandedTraceUrls: string[]
  expandedQuestionCardIds: string[]
  workspaceReady: boolean
  materialsReady: boolean
  aiRunning: boolean
  chatSending: boolean
  humanEvalPath: string | null
  onSetArtifactTab: (tab: ArtifactTab) => void
  onSetRightCollapsed: (value: boolean) => void
  onToggleTraceExpand: (sessionId: string) => void
  onToggleQuestionCardDetails: (testcaseId: string) => void
  onRunSingleScenario: (testcaseId: string, title: string) => void
  onEnterHumanEval: () => void
}

export function EvalArtifactsPanel({
  artifactTab,
  rightCollapsed,
  reportSummary,
  reportMetrics,
  evaluation,
  workspaceStatus,
  testcaseItems,
  questionCardMap,
  traceAssets,
  traceDataCache,
  expandedTraceUrls,
  expandedQuestionCardIds,
  workspaceReady,
  materialsReady,
  aiRunning,
  chatSending,
  humanEvalPath,
  onSetArtifactTab,
  onSetRightCollapsed,
  onToggleTraceExpand,
  onToggleQuestionCardDetails,
  onRunSingleScenario,
  onEnterHumanEval,
}: EvalArtifactsPanelProps) {
  const dimensionScores = reportSummary?.dimensionScores ?? []
  const reportJsonUrl = toAbsoluteApiUrl(reportSummary?.reportJsonUrl ?? null)
  const reportHtmlUrl = toAbsoluteApiUrl(reportSummary?.reportHtmlUrl ?? null)

  const TABS: Array<{ key: ArtifactTab; label: string; icon: typeof BarChart2 }> = [
    { key: 'overview', label: '概览报告', icon: BarChart2 },
    { key: 'testcase', label: '测试用例', icon: FileText },
    { key: 'trace', label: '执行轨迹', icon: Zap },
    { key: 'report', label: '评估报告', icon: BarChart2 },
  ]

  return (
    <div
      className={`${
        rightCollapsed ? 'w-10' : 'w-[320px] xl:w-[340px] 2xl:w-[360px]'
      } hb-card flex shrink-0 flex-col overflow-hidden transition-all duration-200`}
    >
      {rightCollapsed ? (
        <button
          type="button"
          onClick={() => onSetRightCollapsed(false)}
          className="eval-collapse-btn flex h-full w-full items-center justify-center transition-colors"
        >
          <ChevronDown size={16} className="-rotate-90 text-[var(--hb-caption)]" />
        </button>
      ) : (
        <>
          {/* 标签栏 */}
          <div className="flex items-center border-b eval-tab-bar px-2">
            {TABS.map((tab) => (
              <button
                key={tab.key}
                type="button"
                onClick={() => onSetArtifactTab(tab.key)}
                className={`eval-side-tab-button flex flex-1 items-center justify-center gap-1 border-b-2 px-2 py-3 text-[11px] font-medium whitespace-nowrap ${
                  artifactTab === tab.key ? 'eval-tab-active' : 'eval-tab-inactive'
                }`}
              >
                <tab.icon size={12} />
                {tab.label}
              </button>
            ))}
            <button
              type="button"
              onClick={() => onSetRightCollapsed(true)}
              className="ml-auto rounded-lg px-2 py-2 text-[var(--hb-caption)] transition-colors hover:bg-[var(--hb-surface-soft)] hover:text-[var(--hb-soft)]"
            >
              <ChevronDown size={14} className="rotate-90" />
            </button>
          </div>

          <div className="flex-1 overflow-y-auto p-4 pt-3 text-xs">
            {/* ── 概览报告 ── */}
            {artifactTab === 'overview' && (
              <div className="space-y-3">
                <div className={`rounded-[22px] border p-4 ${reportSummary?.passed === false ? 'eval-report-fail' : 'eval-report-pass'}`}>
                  <div className="flex items-start justify-between gap-3">
                    <div>
                      <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary?.passed === false ? 'eval-text-red-2' : 'eval-text-green-mid'}`}>
                        {reportSummary == null ? '等待评估' : reportSummary.passed ? '✓ 评估通过' : '✗ 评估未通过'}
                      </div>
                      <div className="mt-1 text-base font-semibold eval-text-title">AI 评估结论</div>
                      <div className="mt-1 text-[11px] leading-relaxed eval-text-secondary">
                        {reportSummary
                          ? `第 ${reportSummary.iteration} 轮 · ${formatDateTime(reportSummary.createdAtUtc)}`
                          : '执行评估后，这里会展示本轮结论和关键指标。'}
                      </div>
                    </div>
                    <div className="rounded-2xl border eval-score-card px-4 py-3 text-center">
                      <div className="text-3xl font-bold tabular-nums eval-text-title">{reportSummary?.overallScore ?? '--'}</div>
                      <div className="mt-1 text-[11px] eval-text-secondary">综合评分</div>
                    </div>
                  </div>
                  <div className="mt-4 grid grid-cols-2 gap-2">
                    {reportMetrics.slice(0, 4).map((metric) => (
                      <div key={metric.label} className="rounded-2xl border eval-score-card px-3 py-2.5">
                        <div className="text-[10px] uppercase tracking-[0.06em] eval-text-caption">{metric.label}</div>
                        <div className={`mt-1 text-sm font-semibold ${metric.tone}`}>{metric.value}</div>
                      </div>
                    ))}
                  </div>
                  <div className="mt-4 rounded-2xl border eval-recommendation px-3 py-3 text-[11px] leading-relaxed">
                    {evaluation.recommendation || '暂无建议，等待评估结果生成。'}
                  </div>
                </div>

                {dimensionScores.length > 0 && (
                  <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel">
                    <summary className="eval-side-disclosure-summary">
                      <span className="inline-flex items-center gap-1.5">
                        <BarChart2 size={12} />
                        维度评分明细
                      </span>
                    </summary>
                    <div className="eval-side-disclosure-body space-y-2">
                      {dimensionScores.map((item) => (
                        <div key={item.dimension} className="rounded-xl border eval-dim-item px-3 py-2.5">
                          <div className="flex items-center justify-between gap-2">
                            <span className="text-[11px] font-medium eval-text-title">{item.dimension}</span>
                            <span className="tabular-nums text-[11px] font-semibold eval-text-indigo">{item.score}</span>
                          </div>
                          {item.comment && (
                            <div className="mt-1.5 text-[10px] leading-relaxed eval-text-secondary">{item.comment}</div>
                          )}
                        </div>
                      ))}
                    </div>
                  </details>
                )}

                {(reportJsonUrl || reportHtmlUrl || workspaceStatus?.sessionId) && (
                  <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel">
                    <summary className="eval-side-disclosure-summary">
                      <span className="inline-flex items-center gap-1.5">
                        <FileText size={12} />
                        报告资源与调试信息
                      </span>
                    </summary>
                    <div className="eval-side-disclosure-body space-y-3">
                      {(reportJsonUrl || reportHtmlUrl) && (
                        <div className="flex flex-wrap gap-2">
                          {reportJsonUrl && (
                            <a
                              href={reportJsonUrl}
                              target="_blank"
                              rel="noreferrer"
                              className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]"
                            >
                              <ExternalLink size={10} />
                              查看报告 JSON
                            </a>
                          )}
                          {reportHtmlUrl && reportSummary && (
                            <a
                              href={reportHtmlUrl}
                              download={`evaluation-report-${reportSummary.reportId}.html`}
                              target="_blank"
                              rel="noreferrer"
                              className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]"
                            >
                              <ExternalLink size={10} />
                              下载报告 HTML
                            </a>
                          )}
                        </div>
                      )}
                      <div className="space-y-1 text-[10px] font-mono leading-relaxed eval-text-secondary">
                        <div>session: {workspaceStatus?.sessionId ?? '--'}</div>
                        <div>target: {workspaceStatus?.targetSandboxId ?? '--'}</div>
                        <div>evaluator: {workspaceStatus?.evaluatorSandboxId ?? '--'}</div>
                      </div>
                    </div>
                  </details>
                )}

                {testcaseItems.length > 0 && (
                  <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel" open>
                    <summary className="eval-side-disclosure-summary">
                      <span className="inline-flex items-center gap-1.5">
                        <Check size={12} />
                        测试用例 · {testcaseItems.length} 个
                      </span>
                    </summary>
                    <div className="eval-side-disclosure-body space-y-1.5">
                      {testcaseItems.slice(0, 3).map((outline) => (
                        <div key={outline.testcaseId} className="truncate rounded-xl border eval-pill-neutral px-2.5 py-1.5 text-[11px]">
                          {outline.title || outline.testcaseId}
                        </div>
                      ))}
                      {testcaseItems.length > 3 && (
                        <button
                          type="button"
                          onClick={() => onSetArtifactTab('testcase')}
                          className="w-full rounded-xl border eval-pill-neutral px-2.5 py-1.5 text-left text-[11px] eval-text-indigo"
                        >
                          +{testcaseItems.length - 3} 查看全部 →
                        </button>
                      )}
                    </div>
                  </details>
                )}
              </div>
            )}

            {/* ── 测试用例 ── */}
            {artifactTab === 'testcase' && (
              <div className="space-y-3">
                {testcaseItems.length === 0 ? (
                  !workspaceReady ? (
                    <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                      请先完成沙箱初始化流程，随后展示测试用例。
                    </div>
                  ) : !materialsReady ? (
                    <div className="rounded-[20px] border eval-side-notice-warning px-4 py-3 text-[11px] leading-relaxed">
                      素材未就绪，等待完成"加载评估素材"后将自动激活更多场景。
                    </div>
                  ) : (
                    <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                      暂无测试用例。
                    </div>
                  )
                ) : (
                  <>
                    <div className="rounded-[18px] eval-side-status-banner px-4 py-3 text-[12px] font-medium">
                      <span className="inline-flex items-center gap-2">
                        <Check size={13} />
                        用例已就绪，可开始评估
                      </span>
                    </div>

                    <div className="flex items-center gap-2 px-1">
                      <div className="text-[13px] font-semibold eval-text-title">测试场景</div>
                      <span className="text-[12px] font-medium eval-text-caption">{testcaseItems.length} 个</span>
                    </div>

                    <div className="space-y-3">
                      {testcaseItems.map((outline) => {
                        const card = questionCardMap.get(outline.testcaseId)
                        const expanded = expandedQuestionCardIds.includes(outline.testcaseId)

                        return (
                          <article key={outline.testcaseId} className="rounded-[20px] border eval-side-case-card px-4 py-4">
                            <div className="flex items-start justify-between gap-3">
                              <div className="flex min-w-0 items-start gap-3">
                                <span className="mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full bg-[var(--hb-text-green)]" />
                                <div className="min-w-0">
                                  <div className="text-[14px] font-semibold leading-6 eval-text-title">
                                    {outline.title}
                                  </div>
                                  <div className="mt-2 border-l-2 border-[rgba(148,163,184,0.18)] pl-3 text-[12px] leading-6 eval-text-body-2">
                                    {outline.userRequest || '未提供用户请求。'}
                                  </div>
                                </div>
                              </div>
                              <span className="shrink-0 text-[11px] font-mono eval-text-caption">{outline.testcaseId}</span>
                            </div>

                            <div className="mt-4 flex flex-wrap items-center gap-4 text-[12px]">
                              {card && (
                                <button
                                  type="button"
                                  className="eval-side-inline-action"
                                  onClick={() => onToggleQuestionCardDetails(outline.testcaseId)}
                                >
                                  {expanded ? '收起题卡' : '展开题卡'}
                                </button>
                              )}
                              <button
                                type="button"
                                disabled={!aiRunning || chatSending}
                                className="eval-side-inline-action disabled:opacity-50"
                                onClick={() => onRunSingleScenario(outline.testcaseId, outline.title)}
                              >
                                仅运行此场景
                              </button>
                            </div>

                            {expanded && card && (
                              <div className="mt-4 rounded-[18px] border eval-side-case-detail px-3 py-3">
                                {card.prompt && (
                                  <div className="rounded-xl border eval-prompt-box px-3 py-2.5 text-[11px] leading-relaxed">
                                    {card.prompt}
                                  </div>
                                )}

                                {card.steps.length > 0 && (
                                  <div className="mt-3 space-y-2">
                                    <div className="text-[10px] font-semibold uppercase tracking-[0.06em] eval-text-caption">
                                      评估步骤（{card.steps.length}）
                                    </div>
                                    {card.steps.map((step, stepIndex) => (
                                      <div
                                        key={`${card.testcaseId}_step_${stepIndex}`}
                                        className="flex gap-2 rounded-xl border eval-step-row px-2.5 py-2"
                                      >
                                        <span className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full eval-seq-circle text-[9px] font-semibold">
                                          {stepIndex + 1}
                                        </span>
                                        <div className="text-[11px] leading-relaxed eval-text-body-2">{step}</div>
                                      </div>
                                    ))}
                                  </div>
                                )}

                                {(card.scoringHint || card.sourceFile) && (
                                  <div className="mt-3 space-y-2">
                                    {card.scoringHint && (
                                      <div className="rounded-xl border border-dashed eval-scoring-hint px-3 py-2 text-[11px] leading-relaxed">
                                        <span className="font-semibold eval-text-body-2">评分提示：</span>
                                        {card.scoringHint}
                                      </div>
                                    )}
                                    {card.sourceFile && (
                                      <div className="text-[10px] eval-text-caption">来源文件：{card.sourceFile}</div>
                                    )}
                                  </div>
                                )}
                              </div>
                            )}
                          </article>
                        )
                      })}
                    </div>
                  </>
                )}
              </div>
            )}

            {/* ── 执行轨迹 ── */}
            {artifactTab === 'trace' && (
              <div className="space-y-3">
                {traceAssets.length === 0 ? (
                  <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                    暂无执行轨迹，请先执行评估。
                  </div>
                ) : (
                  <>
                    <div className="rounded-[18px] border eval-overview-panel px-4 py-3">
                      <div className="text-[12px] font-medium eval-text-title">已生成 {traceAssets.length} 份执行轨迹</div>
                      <div className="mt-1 text-[11px] eval-text-secondary">最新更新时间：{formatDateTime(traceAssets[0]?.createdAtUtc)}</div>
                    </div>
                    {traceAssets.map((asset, index) => {
                      const traceSessionId = evaluation?.sessionId ?? null
                      const isExpanded = traceSessionId != null && expandedTraceUrls.includes(traceSessionId)
                      const traceData = traceSessionId != null ? traceDataCache[traceSessionId] : undefined
                      const TURN_COLORS = ['l0', 'l1', 'l2', 'l3', 'l4'] as const

                      return (
                        <div key={asset.relativePath} className="rounded-[18px] border eval-trace-card px-3 py-3">
                          <div className="flex items-center justify-between gap-2">
                            <div className="flex items-center gap-1.5">
                              <Zap size={11} className="eval-text-brand" />
                              <span className="text-[12px] font-semibold eval-text-title">轨迹 #{index + 1}</span>
                              <span className="text-[11px] eval-text-caption">{formatDateTime(asset.createdAtUtc)}</span>
                            </div>
                            {traceSessionId && (
                              <button
                                type="button"
                                onClick={() => onToggleTraceExpand(traceSessionId)}
                                className="flex items-center gap-1 rounded-full border eval-pill-neutral px-2.5 py-0.5 text-[11px]"
                              >
                                {traceData === 'loading'
                                  ? <Loader2 size={10} className="animate-spin" />
                                  : <ChevronDown size={10} className={`transition-transform ${isExpanded ? 'rotate-180' : ''}`} />}
                                {isExpanded ? '收起' : '展开轨迹'}
                              </button>
                            )}
                          </div>

                          {isExpanded && (
                            <div className="mt-3 space-y-2">
                              {traceData === 'loading' && (
                                <div className="flex items-center gap-2 text-[11px] eval-text-secondary">
                                  <Loader2 size={11} className="animate-spin" />
                                  正在加载轨迹数据...
                                </div>
                              )}
                              {traceData === 'error' && (
                                <div className="rounded-xl border eval-side-notice-warning px-3 py-2 text-[11px]">
                                  加载失败，请检查网络或重试。
                                </div>
                              )}
                              {traceData != null && traceData !== 'loading' && traceData !== 'error' && (() => {
                                const td = traceData as TraceJsonData
                                const usage = td.http_supplement?.dashboard?.providers?.usage?.[0]
                                return (
                                  <>
                                    <div className="flex flex-wrap gap-1.5">
                                      {td.status && (
                                        <span className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${
                                          td.status === 'completed'
                                            ? 'eval-tone-completed'
                                            : td.status === 'failed'
                                            ? 'eval-tone-failed'
                                            : 'eval-tone-running'
                                        }`}>{td.status}</span>
                                      )}
                                      {td.meta?.total_turns != null && (
                                        <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[10px]">
                                          {td.meta.total_turns} 轮
                                        </span>
                                      )}
                                      {td.meta?.iteration != null && (
                                        <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[10px]">
                                          iter-{td.meta.iteration}
                                        </span>
                                      )}
                                      {usage?.modelId && (
                                        <span className="rounded-full border eval-trace-model-badge px-2 py-0.5 text-[10px] font-mono">
                                          {usage.modelId}
                                        </span>
                                      )}
                                      {usage && (
                                        <span className="rounded-full border eval-trace-token-badge px-2 py-0.5 text-[10px]">
                                          ↑{usage.inputTokens ?? 0} ↓{usage.outputTokens ?? 0}
                                          {(usage.cacheReadTokens ?? 0) > 0 && (
                                            <span className="opacity-65"> cache {usage.cacheReadTokens}</span>
                                          )}
                                        </span>
                                      )}
                                    </div>

                                    {td.turns.map((turn) => {
                                      const et = turn.execution_trace
                                      const colorIdx = turn.turn_index % TURN_COLORS.length
                                      const toolLogs = et.logs.filter(l =>
                                        l.type === 'tool_use' || l.type === 'tool_result'
                                      )
                                      return (
                                        <details
                                          key={turn.turn_index}
                                          open
                                          className={`eval-side-disclosure rounded-[16px] border eval-overview-panel eval-trace-turn-${TURN_COLORS[colorIdx]}`}
                                        >
                                          <summary className="eval-side-disclosure-summary">
                                            <span className="inline-flex items-center gap-2">
                                              <span className={`inline-flex h-[20px] w-[20px] items-center justify-center rounded-full text-[10px] font-bold eval-trace-seq-${colorIdx}`}>
                                                {turn.turn_index + 1}
                                              </span>
                                              {turn.test_case_id && (
                                                <span className={`rounded-full px-1.5 py-0.5 text-[10px] font-mono font-semibold eval-trace-seq-${colorIdx}`}>
                                                  {turn.test_case_id}
                                                </span>
                                              )}
                                              {et.summary?.execution_time_seconds != null && (
                                                <span className="text-[10px] eval-text-caption">
                                                  {et.summary.execution_time_seconds.toFixed(1)}s
                                                </span>
                                              )}
                                            </span>
                                          </summary>
                                          <div className="eval-side-disclosure-body space-y-2">
                                            <div className="rounded-xl border eval-trace-user-block px-2.5 py-2">
                                              <div className="mb-1 flex items-center gap-1">
                                                <User size={9} />
                                                <span className="text-[10px] font-medium opacity-70">用户</span>
                                              </div>
                                              {turn.user_input
                                                ? <div className="text-[11px] font-medium">{turn.user_input}</div>
                                                : <div className="text-[11px] opacity-40 italic">（已通过评估脚本注入，内容不在轨迹中记录）</div>
                                              }
                                            </div>
                                            {et.assembled_assistant_text && (
                                              <div className="rounded-xl border eval-trace-ai-block px-2.5 py-2">
                                                <div className="mb-1 flex items-center gap-1 eval-text-indigo">
                                                  <Bot size={9} />
                                                  <span className="text-[10px] font-medium">AI 回复</span>
                                                </div>
                                                <div className="max-h-[100px] overflow-y-auto whitespace-pre-wrap break-words text-[11px] leading-relaxed eval-text-secondary">
                                                  {et.assembled_assistant_text}
                                                </div>
                                              </div>
                                            )}
                                            {et.summary && (
                                              <div className="flex flex-wrap gap-1.5">
                                                {et.summary.total_messages != null && (
                                                  <span className="rounded-full border eval-stats-badge-ontology px-2 py-0.5 text-[10px]">
                                                    {et.summary.total_messages} msgs
                                                  </span>
                                                )}
                                                {(et.summary.total_tool_calls ?? 0) > 0 && (
                                                  <span className="rounded-full border eval-trace-badge-tool-use px-2 py-0.5 text-[10px]">
                                                    {et.summary.total_tool_calls} 工具
                                                  </span>
                                                )}
                                                {et.summary.has_thought && (
                                                  <span className="rounded-full border eval-trace-thought-badge px-2 py-0.5 text-[10px]">
                                                    思维链 ×{et.summary.think_count}
                                                  </span>
                                                )}
                                              </div>
                                            )}
                                            {toolLogs.map((log, li) => (
                                              <div key={li} className="rounded-xl border eval-dim-item px-2.5 py-2">
                                                <div className="flex items-center gap-1.5">
                                                  <span className={`rounded-md px-1.5 py-0.5 text-[10px] font-mono ${
                                                    log.type === 'tool_use'
                                                      ? 'eval-trace-badge-tool-use'
                                                      : 'eval-trace-badge-tool-result'
                                                  }`}>
                                                    {log.type === 'tool_use' ? '→ tool' : '← result'}
                                                  </span>
                                                  {log.name && (
                                                    <span className="text-[11px] font-medium eval-text-title">{log.name}</span>
                                                  )}
                                                  {(log.timestamp_start ?? log.timestamp) && (
                                                    <span className="ml-auto text-[10px] eval-text-caption font-mono">
                                                      {(log.timestamp_start ?? log.timestamp ?? '').slice(11, 19)}
                                                    </span>
                                                  )}
                                                </div>
                                                {log.type === 'tool_use' && log.input != null && (
                                                  <pre className="mt-1.5 max-h-[80px] overflow-y-auto break-all text-[10px] font-mono eval-text-secondary whitespace-pre-wrap">
                                                    {JSON.stringify(log.input, null, 2).slice(0, 400)}
                                                  </pre>
                                                )}
                                                {log.type === 'tool_result' && log.content != null && (
                                                  <pre className="mt-1.5 max-h-[80px] overflow-y-auto break-all text-[10px] font-mono eval-text-secondary whitespace-pre-wrap">
                                                    {typeof log.content === 'string'
                                                      ? log.content.slice(0, 400)
                                                      : JSON.stringify(log.content).slice(0, 400)}
                                                  </pre>
                                                )}
                                              </div>
                                            ))}
                                          </div>
                                        </details>
                                      )
                                    })}
                                  </>
                                )
                              })()}
                            </div>
                          )}
                        </div>
                      )
                    })}
                  </>
                )}
              </div>
            )}

            {/* ── 评估报告 ── */}
            {artifactTab === 'report' && (
              <div className="space-y-3">
                {!reportSummary ? (
                  <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                    暂无评估报告，请先执行评估。
                  </div>
                ) : (
                  <>
                    <div className={`rounded-2xl border p-4 ${reportSummary.passed ? 'eval-report-pass' : 'eval-report-fail'}`}>
                      <div className="flex items-center justify-between gap-3">
                        <div>
                          <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary.passed ? 'eval-text-green-mid' : 'eval-text-red-2'}`}>
                            {reportSummary.passed ? '✓ 评估通过' : '✗ 评估未通过'}
                          </div>
                          <div className="mt-1 text-[11px] eval-text-secondary">
                            第 {reportSummary.iteration} 轮 · {formatDateTime(reportSummary.createdAtUtc)}
                          </div>
                        </div>
                        <div className="rounded-xl border eval-score-card px-3 py-2 text-center shadow-sm">
                          <div className="text-2xl font-bold tabular-nums eval-text-title">{reportSummary.overallScore}</div>
                          <div className="text-[10px] eval-text-secondary">综合评分</div>
                        </div>
                      </div>
                    </div>

                    {dimensionScores.length > 0 && (
                      <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel">
                        <summary className="eval-side-disclosure-summary">
                          <span>维度评分明细</span>
                        </summary>
                        <div className="eval-side-disclosure-body space-y-2">
                          {dimensionScores.map((item) => (
                            <div key={item.dimension} className="rounded-xl border eval-dim-item px-3 py-2">
                              <div className="flex items-center justify-between gap-2">
                                <span className="text-[11px] font-medium eval-text-title">{item.dimension}</span>
                                <span className="tabular-nums text-[11px] font-semibold eval-text-indigo">{item.score}</span>
                              </div>
                              {item.comment && (
                                <div className="mt-1 text-[10px] leading-relaxed eval-text-secondary">{item.comment}</div>
                              )}
                            </div>
                          ))}
                        </div>
                      </details>
                    )}

                    {humanEvalPath && (
                      <button
                        type="button"
                        className="hb-btn-primary w-full !py-2 !text-[12px]"
                        onClick={onEnterHumanEval}
                      >
                        <AlertCircle size={12} />
                        进入人工评估环节 →
                      </button>
                    )}
                  </>
                )}
              </div>
            )}
          </div>
        </>
      )}
    </div>
  )
}
