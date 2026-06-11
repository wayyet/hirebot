import {
  Activity,
  AlertCircle,
  BarChart2,
  Bot,
  Check,
  ChevronDown,
  ClipboardCheck,
  ExternalLink,
  FileText,
  Loader2,
  PanelRightClose,
  User,
  Zap,
} from 'lucide-react'
import type {
  EvaluationState,
  EvaluationWorkspaceStatus,
  EvaluationReportSummary,
  EvaluationQuestionCard,
  EvaluationTestcaseOutline,
  EvaluationAssetRef,
} from '@/infra/api'
import type { ArtifactTab, TraceJsonData } from './evaluationTypes'
import { formatDateTime } from './evaluationUtils'

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
  onReportAction: (reportId: string, fileType: 'json' | 'html', fileName: string, action: 'download' | 'open') => void
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
  onReportAction,
}: EvalArtifactsPanelProps) {
  const dimensionScores = reportSummary?.dimensionScores ?? []
  // 仅用于判断报告文件是否存在；实际下载通过后台鉴权接口完成，不需要绝对 URL
  const reportJsonUrl = reportSummary?.reportJsonUrl ?? null
  const reportHtmlUrl = reportSummary?.reportHtmlUrl ?? null

  const TABS: Array<{ key: ArtifactTab; label: string; icon: typeof BarChart2 }> = [
    { key: 'overview', label: '概览报告', icon: BarChart2 },
    { key: 'testcase', label: '测试用例', icon: ClipboardCheck },
    { key: 'trace', label: '执行轨迹', icon: Activity },
    { key: 'report', label: '评估报告', icon: FileText },
  ]

  const activeTabMeta = {
    overview: {
      title: '评估概览',
      subtitle: '会话、题卡、材料和评分结果',
      status: reportSummary == null ? '等待评估' : reportSummary.passed ? '评估通过' : '评估未通过',
      tone: reportSummary?.passed === false ? 'eval-artifact-chip-danger' : reportSummary ? 'eval-artifact-chip-success' : 'eval-artifact-chip-neutral',
    },
    testcase: {
      title: '测试用例',
      subtitle: '题卡、提示与单场景运行',
      status: testcaseItems.length > 0 ? `${testcaseItems.length} 个场景` : '待生成',
      tone: testcaseItems.length > 0 ? 'eval-artifact-chip-success' : 'eval-artifact-chip-neutral',
    },
    trace: {
      title: '执行轨迹',
      subtitle: '回合、工具调用和 token 证据',
      status: traceAssets.length > 0 ? `${traceAssets.length} 份轨迹` : '暂无轨迹',
      tone: traceAssets.length > 0 ? 'eval-artifact-chip-success' : 'eval-artifact-chip-neutral',
    },
    report: {
      title: '评估报告',
      subtitle: '最终评分、报告资源和人工复核入口',
      status: reportSummary ? '已生成' : '待生成',
      tone: reportSummary ? 'eval-artifact-chip-success' : 'eval-artifact-chip-neutral',
    },
  }[artifactTab]

  return (
    <div
      className={`${
        rightCollapsed ? 'w-10' : 'w-[420px] xl:w-[460px] 2xl:w-[500px]'
      } hb-card eval-artifacts-shell flex shrink-0 flex-col overflow-hidden transition-all duration-200`}
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
          <div className="eval-tab-bar flex items-center gap-1 border-b px-2 py-2">
            {TABS.map((tab) => (
              <button
                key={tab.key}
                type="button"
                onClick={() => onSetArtifactTab(tab.key)}
                className={`eval-side-tab-button flex flex-1 items-center justify-center gap-1.5 rounded-xl border px-2.5 py-2 text-[12px] font-semibold whitespace-nowrap ${
                  artifactTab === tab.key ? 'eval-tab-active' : 'eval-tab-inactive'
                }`}
              >
                <span className="eval-side-tab-icon">
                  <tab.icon size={13} />
                </span>
                <span>{tab.label}</span>
              </button>
            ))}
            <button
              type="button"
              onClick={() => onSetRightCollapsed(true)}
              className="eval-collapse-action ml-1 flex h-9 w-9 shrink-0 items-center justify-center rounded-xl text-[var(--hb-caption)] transition-colors hover:bg-[var(--hb-surface-soft)] hover:text-[var(--hb-soft)]"
            >
              <PanelRightClose size={15} />
            </button>
          </div>

          <div className="eval-artifact-pane-head">
            <div className="min-w-0">
              <div className="eval-artifact-kicker">Evaluation sandbox</div>
              <div className="mt-1 flex items-center gap-2">
                <h2 className="truncate text-[16px] font-semibold eval-text-title">{activeTabMeta.title}</h2>
                <span className={`eval-artifact-status-chip ${activeTabMeta.tone}`}>{activeTabMeta.status}</span>
              </div>
              <p className="mt-1 text-[12px] leading-5 eval-text-secondary">{activeTabMeta.subtitle}</p>
            </div>
          </div>

          <div className="eval-artifact-content flex-1 overflow-y-auto px-4 pb-4 pt-3 text-xs">
            {/* ── 概览报告 ── */}
            {artifactTab === 'overview' && (
              <div className="space-y-3">
                <div className={`eval-overview-hero rounded-[22px] border p-4 ${reportSummary?.passed === false ? 'eval-report-fail' : 'eval-report-pass'}`}>
                  <div className="flex items-start justify-between gap-3">
                    <div className="min-w-0">
                      <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary?.passed === false ? 'eval-text-red-2' : 'eval-text-green-mid'}`}>
                        {reportSummary == null ? '等待评估' : reportSummary.passed ? '评估通过' : '评估未通过'}
                      </div>
                      <div className="mt-1 text-base font-semibold eval-text-title">AI 评估结论</div>
                      <div className="mt-1 text-[11px] leading-relaxed eval-text-secondary">
                        {reportSummary
                          ? `第 ${reportSummary.iteration} 轮 · ${formatDateTime(reportSummary.createdAtUtc)}`
                          : '执行评估后，这里会展示本轮结论和关键指标。'}
                      </div>
                    </div>
                    <div className="eval-score-orb shrink-0 text-center">
                      <div className="text-3xl font-bold tabular-nums eval-text-title">{reportSummary?.overallScore ?? '--'}</div>
                      <div className="mt-1 text-[11px] eval-text-secondary">综合评分</div>
                    </div>
                  </div>
                  <div className="mt-4 grid grid-cols-2 gap-2">
                    {reportMetrics.slice(0, 4).map((metric) => (
                      <div key={metric.label} className="eval-metric-card rounded-2xl border px-3 py-2.5">
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
                            <button
                              type="button"
                              className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]"
                              onClick={() => reportSummary && onReportAction(reportSummary.reportId, 'json', `evaluation-report-${reportSummary.reportId}.json`, 'open')}
                            >
                              <ExternalLink size={10} />
                              查看报告 JSON
                            </button>
                          )}
                          {reportHtmlUrl && reportSummary && (
                            <button
                              type="button"
                              className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]"
                              onClick={() => onReportAction(reportSummary.reportId, 'html', `evaluation-report-${reportSummary.reportId}.html`, 'download')}
                            >
                              <ExternalLink size={10} />
                              下载报告 HTML
                            </button>
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
                    <div className="eval-panel-empty rounded-[20px] border eval-empty-card px-4 py-5">
                      <ClipboardCheck size={18} className="eval-text-caption" />
                      <div className="mt-3 text-[13px] font-semibold eval-text-title">沙箱初始化中</div>
                      <div className="mt-1 text-[12px] leading-5 eval-text-secondary">完成双沙箱准备后会显示测试场景。</div>
                    </div>
                  ) : !materialsReady ? (
                    <div className="eval-panel-empty rounded-[20px] border eval-side-notice-warning px-4 py-5">
                      <AlertCircle size={18} />
                      <div className="mt-3 text-[13px] font-semibold">素材待补齐</div>
                      <div className="mt-1 text-[12px] leading-5">加载评估素材后会自动激活更多场景。</div>
                    </div>
                  ) : (
                    <div className="eval-panel-empty rounded-[20px] border eval-empty-card px-4 py-5">
                      <ClipboardCheck size={18} className="eval-text-caption" />
                      <div className="mt-3 text-[13px] font-semibold eval-text-title">暂无测试用例</div>
                      <div className="mt-1 text-[12px] leading-5 eval-text-secondary">当前评估材料还没有生成可展示的题卡。</div>
                    </div>
                  )
                ) : (
                  <>
                    <div className="eval-side-status-banner eval-tab-summary-card rounded-[18px] px-4 py-3">
                      <div className="flex items-center justify-between gap-3">
                        <span className="inline-flex items-center gap-2 text-[12px] font-semibold">
                          <Check size={13} />
                          用例已就绪
                        </span>
                        <span className="rounded-full border eval-pill-neutral px-2.5 py-1 text-[11px] font-semibold">
                          {testcaseItems.length} 个场景
                        </span>
                      </div>
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
                                <span className="eval-case-marker mt-0.5 shrink-0">
                                  <Check size={12} />
                                </span>
                                <div className="min-w-0">
                                  <div className="text-[15px] font-semibold leading-6 eval-text-title">
                                    {outline.title}
                                  </div>
                                  <div className="mt-2 border-l-2 border-[rgba(148,163,184,0.18)] pl-3 text-[13px] leading-6 eval-text-body-2">
                                    {outline.userRequest || '未提供用户请求。'}
                                  </div>
                                </div>
                              </div>
                              <span className="eval-case-id-chip shrink-0 font-mono text-[11px]">{outline.testcaseId}</span>
                            </div>

                            <div className="mt-4 flex flex-wrap items-center gap-2 text-[12px]">
                              {card && (
                                <button
                                  type="button"
                                  className="eval-side-inline-action eval-side-action-button"
                                  onClick={() => onToggleQuestionCardDetails(outline.testcaseId)}
                                >
                                  {expanded ? '收起题卡' : '展开题卡'}
                                </button>
                              )}
                            </div>

                            {expanded && card && (
                              <div className="mt-4 rounded-[18px] border eval-side-case-detail px-3 py-3">
                                {card.prompt && (
                                  <div className="rounded-xl border eval-prompt-box px-3 py-2.5 text-[12px] leading-relaxed">
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
                  <div className="eval-panel-empty rounded-[20px] border eval-empty-card px-4 py-5">
                    <Activity size={18} className="eval-text-caption" />
                    <div className="mt-3 text-[13px] font-semibold eval-text-title">
                      {evaluation.overallStatus === 'not_started' ? '等待执行评估' : '暂无执行轨迹'}
                    </div>
                    <div className="mt-1 text-[12px] leading-5 eval-text-secondary">
                      {evaluation.overallStatus === 'not_started'
                        ? '开始评估后会同步展示回合、工具调用和轨迹证据。'
                        : '评估器本次未上报轨迹数据。'}
                    </div>
                  </div>
                ) : (
                  <>
                    <div className="eval-tab-summary-card rounded-[18px] border eval-overview-panel px-4 py-3">
                      <div className="flex items-center justify-between gap-3">
                        <div>
                          <div className="text-[12px] font-semibold eval-text-title">轨迹证据已同步</div>
                          <div className="mt-1 text-[11px] eval-text-secondary">最新更新时间：{formatDateTime(traceAssets[0]?.createdAtUtc)}</div>
                        </div>
                        <span className="rounded-full border eval-pill-neutral px-2.5 py-1 text-[11px] font-semibold">
                          {traceAssets.length} 份
                        </span>
                      </div>
                    </div>
                    {traceAssets.map((asset, index) => {
                      const traceSessionId = evaluation?.sessionId ?? null
                      const isExpanded = traceSessionId != null && expandedTraceUrls.includes(traceSessionId)
                      const traceData = traceSessionId != null ? traceDataCache[traceSessionId] : undefined
                      const TURN_COLORS = ['l0', 'l1', 'l2', 'l3', 'l4'] as const

                      return (
                        <div key={asset.relativePath} className="rounded-[18px] border eval-trace-card px-3 py-3">
                          <div className="flex items-start justify-between gap-3">
                            <div className="flex min-w-0 items-start gap-2">
                              <span className="eval-trace-icon-frame">
                                <Zap size={12} />
                              </span>
                              <div className="min-w-0">
                                <div className="text-[12px] font-semibold eval-text-title">轨迹 #{index + 1}</div>
                                <div className="mt-0.5 truncate text-[11px] eval-text-caption">{formatDateTime(asset.createdAtUtc)}</div>
                              </div>
                            </div>
                            {traceSessionId && (
                              <button
                                type="button"
                                onClick={() => onToggleTraceExpand(traceSessionId)}
                                className="flex shrink-0 items-center gap-1 rounded-full border eval-pill-neutral px-2.5 py-1 text-[11px] font-medium"
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
                  <div className="eval-panel-empty rounded-[20px] border eval-empty-card px-4 py-5">
                    <FileText size={18} className="eval-text-caption" />
                    <div className="mt-3 text-[13px] font-semibold eval-text-title">暂无评估报告</div>
                    <div className="mt-1 text-[12px] leading-5 eval-text-secondary">执行评估后会生成最终评分、维度明细和报告资源。</div>
                  </div>
                ) : (
                  <>
                    <div className={`eval-report-hero rounded-2xl border p-4 ${reportSummary.passed ? 'eval-report-pass' : 'eval-report-fail'}`}>
                      <div className="flex items-start justify-between gap-3">
                        <div className="min-w-0">
                          <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary.passed ? 'eval-text-green-mid' : 'eval-text-red-2'}`}>
                            {reportSummary.passed ? '评估通过' : '评估未通过'}
                          </div>
                          <div className="mt-1 text-base font-semibold eval-text-title">最终评估结果</div>
                          <div className="mt-1 text-[11px] leading-relaxed eval-text-secondary">
                            第 {reportSummary.iteration} 轮 · {formatDateTime(reportSummary.createdAtUtc)}
                          </div>
                        </div>
                        <div className="eval-score-orb shrink-0 text-center">
                          <div className="text-3xl font-bold tabular-nums eval-text-title">{reportSummary.overallScore}</div>
                          <div className="mt-1 text-[11px] eval-text-secondary">综合评分</div>
                        </div>
                      </div>
                      <div className="mt-4 rounded-2xl border eval-recommendation px-3 py-3 text-[11px] leading-relaxed">
                        {evaluation.recommendation || '暂无建议，等待评估结果生成。'}
                      </div>
                      {(reportJsonUrl || reportHtmlUrl) && (
                        <div className="mt-4 flex flex-wrap gap-2">
                          {reportJsonUrl && (
                            <button
                              type="button"
                              className="inline-flex items-center gap-1.5 rounded-full border eval-pill-neutral px-3 py-1.5 text-[11px] font-medium"
                              onClick={() => onReportAction(reportSummary.reportId, 'json', `evaluation-report-${reportSummary.reportId}.json`, 'open')}
                            >
                              <ExternalLink size={11} />
                              查看 JSON
                            </button>
                          )}
                          {reportHtmlUrl && (
                            <button
                              type="button"
                              className="inline-flex items-center gap-1.5 rounded-full border eval-pill-neutral px-3 py-1.5 text-[11px] font-medium"
                              onClick={() => reportSummary && onReportAction(reportSummary.reportId, 'html', `evaluation-report-${reportSummary.reportId}.html`, 'download')}
                            >
                              <ExternalLink size={11} />
                              下载 HTML
                            </button>
                          )}
                        </div>
                      )}
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
