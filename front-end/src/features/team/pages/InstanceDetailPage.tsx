import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  BarChart2,
  Bot,
  Check,
  ChevronDown,
  CopyPlus,
  ExternalLink,
  FileText,
  Loader2,
  MessageCircle,
  PlayCircle,
  RotateCcw,
  Settings,
  ShieldCheck,
  User,
  Zap,
} from "lucide-react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import CloneEmployeeModal from "@/features/hiring/pages/components/CloneEmployeeModal";
import {
  firstCharacter,
  ownershipClass,
  ownershipLabel,
  statusClass,
  statusLabel,
  toEmployeeDetailSummary,
  withEmployeeView,
} from "@/features/hiring/pages/employeeView";
import { api, type EmployeeDetail, type EvaluationState } from "@/infra/api";
import type { TraceJsonData } from "@/features/team/pages/evaluation/evaluationTypes";
import { Breadcrumb } from "@/shared/components/Breadcrumb";
import { instanceBasePath } from "@/shared/utils/instancePath";
import { CreatorDisplay } from "@/shared/components/CreatorDisplay";

type DetailEvalTab = "overview" | "testcase" | "trace" | "report";

function formatRelativeTime(dateStr: string, language: string): string {
  if (!dateStr) return "";
  const date = new Date(dateStr);
  if (Number.isNaN(date.getTime())) return dateStr;
  const formatter = new Intl.RelativeTimeFormat(
    language.startsWith("zh") ? "zh-CN" : "en",
    { numeric: "auto" },
  );
  const diffMs = date.getTime() - Date.now();
  const absMs = Math.abs(diffMs);
  const direction = diffMs < 0 ? -1 : 1;
  if (absMs < 60_000) return formatter.format(0, "second");
  const minutes = Math.floor(absMs / 60_000) * direction;
  if (absMs < 3_600_000) return formatter.format(minutes, "minute");
  const hours = Math.floor(absMs / 3_600_000) * direction;
  if (absMs < 86_400_000) return formatter.format(hours, "hour");
  const days = Math.floor(absMs / 86_400_000) * direction;
  if (absMs < 2_592_000_000) return formatter.format(days, "day");
  return dateStr;
}

function formatDateTime(value?: string | null): string {
  if (!value) return "--";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return date.toLocaleString("zh-CN", {
    hour12: false,
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function toAbsoluteApiUrl(value?: string | null): string | null {
  if (!value) return null;
  if (/^https?:\/\//i.test(value)) return value;
  return value.startsWith("/") ? value : `/${value}`;
}

function verdictLabel(verdict?: string | null): string {
  if (verdict === "passed") return "通过";
  if (verdict === "failed") return "未通过";
  if (verdict === "warning") return "待优化";
  return "待判定";
}

function verdictPillClass(verdict?: string | null): string {
  if (verdict === "passed") return "eval-tone-completed";
  if (verdict === "failed") return "eval-tone-failed";
  if (verdict === "warning") return "eval-tone-warning";
  return "eval-tone-pending";
}

export default function InstanceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { t, i18n } = useTranslation();
  const { showToast } = useUxOverlay();

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [evaluation, setEvaluation] = useState<EvaluationState | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cloneModalOpen, setCloneModalOpen] = useState(false);
  const [evalTab, setEvalTab] = useState<DetailEvalTab>("overview");
  const [expandedCardIds, setExpandedCardIds] = useState<string[]>([]);
  const [expandedTraceIds, setExpandedTraceIds] = useState<string[]>([]);
  const [traceDataCache, setTraceDataCache] = useState<Record<string, TraceJsonData | "loading" | "error">>({});
  const [templateDescription, setTemplateDescription] = useState("");
  const [coreAbilities, setCoreAbilities] = useState<string[]>([]);
  const [inScope, setInScope] = useState<string[]>([]);
  const [outOfScope, setOutOfScope] = useState<string[]>([]);

  const employeeView = useMemo(() => {
    if (!employee) return null;
    return withEmployeeView(toEmployeeDetailSummary(employee));
  }, [employee]);

  const backTarget =
    employeeView?.ownership === "department"
      ? "/department-employees"
      : "/my-employees";
  const isPersonalAsset =
    employeeView?.ownership === "personal_clone" ||
    employeeView?.ownership === "private_branch";
  const canCreatePersonalClone =
    employeeView?.ownership === "department" &&
    employeeView?.mappedStatus === "live";
  const reportSummary = evaluation?.latestReport ?? employee?.latestReport ?? null;
  const dimensionScores = reportSummary?.dimensionScores ?? [];
  const testcaseItems = useMemo(() => {
    const outlines = evaluation?.testcaseOutlines ?? [];
    const cards = evaluation?.questionCards ?? [];
    return outlines.length > 0
      ? outlines
      : cards.map((c) => ({ testcaseId: c.testcaseId, title: c.title, userRequest: "" }));
  }, [evaluation]);
  const questionCardMap = useMemo(
    () => new Map((evaluation?.questionCards ?? []).map((c) => [c.testcaseId, c])),
    [evaluation],
  );
  const traceAssets = useMemo(
    () => (evaluation?.assetRefs ?? []).filter((a) => a.assetType === "trace-json"),
    [evaluation],
  );
  const reportJsonUrl = toAbsoluteApiUrl(reportSummary?.reportJsonUrl ?? null);
  const reportHtmlUrl = toAbsoluteApiUrl(reportSummary?.reportHtmlUrl ?? null);
  const hasEvaluationPanel =
    reportSummary != null ||
    testcaseItems.length > 0 ||
    traceAssets.length > 0 ||
    Boolean(evaluation?.recommendation);
  const reportMetrics = useMemo(() => [
    { label: "题卡数量", value: `${testcaseItems.length}`, tone: "eval-text-teal" },
    { label: "轨迹数量", value: `${traceAssets.length}`, tone: "eval-text-amber" },
    { label: "场景通过", value: `${(evaluation?.scenarios ?? []).filter((s) => s.verdict === "passed").length}`, tone: "eval-text-green-bright" },
    { label: "场景失败", value: `${(evaluation?.scenarios ?? []).filter((s) => s.verdict === "failed").length}`, tone: "eval-text-red" },
  ], [testcaseItems.length, traceAssets.length, evaluation?.scenarios]);

  const loadEmployee = useCallback(async () => {
    if (!id) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const [data, evaluationData] = await Promise.all([
        api.employeeRuntime.getEmployee(id),
        api.employeeRuntime.getEvaluationState(id).catch(() => null),
      ]);
      setEmployee(data);
      setEvaluation(evaluationData);

      if (!data.sourceTemplateId) {
        setTemplateDescription("");
        setCoreAbilities([]);
        setInScope([]);
        setOutOfScope([]);
        return;
      }

      try {
        const template = await api.employeeTemplate.getDetail(data.sourceTemplateId);
        setTemplateDescription(template.description);
        setCoreAbilities(template.coreAbilities);
        setInScope(template.responsibilityBoundary.inScope);
        setOutOfScope(template.responsibilityBoundary.outOfScope);
      } catch {
        setTemplateDescription("");
        setCoreAbilities([]);
        setInScope([]);
        setOutOfScope([]);
      }
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : t("instanceDetail.loadFailed"),
      );
    } finally {
      setLoading(false);
    }
  }, [id, t]);

  useEffect(() => {
    void loadEmployee();
  }, [loadEmployee]);

  async function rehireEmployee() {
    if (
      !id ||
      !employeeView ||
      !isPersonalAsset ||
      employeeView.mappedStatus !== "retired"
    ) {
      return;
    }

    setSubmitting(true);
    setError("");

    try {
      const data = await api.employeeRuntime.rehire(id);
      setEmployee(data);
      showToast(t("instanceDetail.rehireSuccess"), "success");
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : t("instanceDetail.rehireFailed"),
      );
    } finally {
      setSubmitting(false);
    }
  }

  async function abandonBranch() {
    if (!id || !employee) return;
    if (!window.confirm(t("instanceDetail.confirmAbandon"))) return;

    setSubmitting(true);
    setError("");

    try {
      const data = await api.employeeRuntime.abandonPrivateBranch(id);
      setEmployee(data);
      showToast(t("instanceDetail.abandonSuccess"), "success");
      navigate(instanceBasePath(location.pathname, data.employeeId));
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : t("instanceDetail.abandonFailed"),
      );
    } finally {
      setSubmitting(false);
    }
  }

  function toggleCard(testcaseId: string) {
    setExpandedCardIds((prev) =>
      prev.includes(testcaseId) ? prev.filter((x) => x !== testcaseId) : [...prev, testcaseId],
    );
  }

  async function toggleTrace(sessionId: string) {
    const isExpanding = !expandedTraceIds.includes(sessionId);
    setExpandedTraceIds((prev) =>
      isExpanding ? [...prev, sessionId] : prev.filter((x) => x !== sessionId),
    );
    if (!isExpanding || !id) return;
    if (traceDataCache[sessionId] && traceDataCache[sessionId] !== "error") return;
    setTraceDataCache((prev) => ({ ...prev, [sessionId]: "loading" }));
    try {
      const resp = await api.employeeRuntime.getTraceContent(id, sessionId);
      const parsed = JSON.parse(resp.traceJsonContent) as TraceJsonData;
      setTraceDataCache((prev) => ({ ...prev, [sessionId]: parsed }));
    } catch {
      setTraceDataCache((prev) => ({ ...prev, [sessionId]: "error" }));
    }
  }

  return (
    <div className="hb-page hb-employee-page hb-employee-detail-page space-y-5">
      <Breadcrumb
        items={[
          {
            label:
              employeeView?.ownership === "department"
                ? t("instanceDetail.breadcrumbDept")
                : t("instanceDetail.breadcrumbMy"),
            to: backTarget,
          },
          { label: t("instanceDetail.breadcrumb") },
        ]}
      />

      {error ? (
        <div className="hb-alert hb-alert-error">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      ) : null}

      {loading ? (
        <div className="hb-card hb-detail-state">
          <Loader2 size={16} className="animate-spin" />
          {t("instanceDetail.loading")}
        </div>
      ) : !employee || !employeeView ? (
        <div className="hb-card hb-detail-state">{t("instanceDetail.notFound")}</div>
      ) : (
        <div className="space-y-5">
          <section className="hb-card hb-detail-hero">
            <div className="hb-detail-top">
              <div className="hb-detail-avatar hb-detail-avatar--accent">
                {firstCharacter(employee.nickname)}
              </div>
              <div className="hb-detail-main">
                <div className="hb-detail-title-row">
                  <div>
                    <h1>{employee.nickname}</h1>
                    {employee.roleName || employee.sourceTemplate ? (
                      <p className="hb-detail-subtitle">
                        {employee.roleName || employee.sourceTemplate}
                      </p>
                    ) : null}
                  </div>
                  <div className="hb-detail-badges">
                    <span
                      className={`hb-pill hb-pill--solid ${statusClass(employeeView.mappedStatus, employee.lifecycleStatus)}`}
                    >
                      <span className="hb-pill-dot" />
                      {statusLabel(
                        employeeView.mappedStatus,
                        employee.lifecycleStatus,
                      )}
                    </span>
                    <span
                      className={`hb-pill hb-pill--solid ${ownershipClass(employeeView.ownership)}`}
                    >
                      <span className="hb-pill-dot" />
                      {ownershipLabel(employeeView.ownership)}
                    </span>
                  </div>
                </div>
                <div className="hb-detail-meta flex items-center">
                  <span className="flex items-center gap-1">
                    <span>{t("instanceDetail.meta.owner")}</span>
                    {employee.createdBy ? (
                      <CreatorDisplay
                        creator={employee.createdBy}
                        avatarSize={16}
                        showAvatar={false}
                      />
                    ) : (
                      <span>{employee.ownerUserId || "-"}</span>
                    )}
                  </span>
                  <span className="hb-detail-meta-sep">|</span>
                  <span>
                    {t("instanceDetail.meta.createdAt")}{" "}
                    {formatRelativeTime(
                      employee.createdAt,
                      i18n.resolvedLanguage || i18n.language,
                    )}
                  </span>
                </div>
                <p className="hb-detail-desc">
                  {templateDescription || employee.primarySignal || employee.stageSummary}
                </p>
              </div>

              <div className="hb-detail-actions">
                {isPersonalAsset && employeeView.mappedStatus === "live" ? (
                  <button
                    type="button"
                    className="hb-btn-outline-brand"
                    onClick={() =>
                      navigate(`/my-employees/instances/${employee.employeeId}/im-config`)
                    }
                  >
                    <Settings size={12} />
                    {t("instanceDetail.actions.configureIm")}
                  </button>
                ) : null}

                {isPersonalAsset && employeeView.mappedStatus === "live" ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() =>
                      navigate(`/my-employees/instances/${employee.employeeId}/chat`)
                    }
                  >
                    <MessageCircle size={14} />
                    {t("instanceDetail.actions.startChat")}
                  </button>
                ) : null}

                {canCreatePersonalClone ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => setCloneModalOpen(true)}
                  >
                    <CopyPlus size={14} />
                    {t("instanceDetail.actions.createMyClone")}
                  </button>
                ) : null}

                {isPersonalAsset && employeeView.mappedStatus === "retired" ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => void rehireEmployee()}
                    disabled={submitting}
                  >
                    <RotateCcw size={14} />
                    {submitting
                      ? t("instanceDetail.actions.rehiring")
                      : t("instanceDetail.actions.rehire")}
                  </button>
                ) : null}

                {employeeView.ownership === "private_branch" &&
                employeeView.mappedStatus !== "retired" ? (
                  <button
                    type="button"
                    className="hb-detail-danger-btn"
                    disabled={submitting}
                    onClick={() => void abandonBranch()}
                  >
                    {t("instanceDetail.actions.abandonBranch")}
                  </button>
                ) : null}

                {employeeView.mappedStatus === "hiring" && employee?.basedOnTemplateId ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() =>
                      navigate(`/template-pool/hiring/${employee.basedOnTemplateId}`)
                    }
                  >
                    <PlayCircle size={14} />
                    {t("instanceDetail.actions.continueHiring")}
                  </button>
                ) : null}

                {employeeView.mappedStatus === "interning_ai" && id ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() =>
                      navigate(`${instanceBasePath(location.pathname, id)}/evaluation`)
                    }
                  >
                    <ShieldCheck size={14} />
                    {t("instanceDetail.actions.enterAiEvaluation")}
                  </button>
                ) : null}

                {employeeView.mappedStatus === "interning_human" && id ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() =>
                      navigate(
                        `${instanceBasePath(location.pathname, id)}/human-evaluation`,
                      )
                    }
                  >
                    <ShieldCheck size={14} />
                    {t("instanceDetail.actions.enterHumanEvaluation")}
                  </button>
                ) : null}
              </div>
            </div>
          </section>

          <section className="hb-card hb-detail-panel">
            <h2 className="hb-section-heading">{t("instanceDetail.capabilities")}</h2>
            {coreAbilities.length > 0 || inScope.length > 0 || outOfScope.length > 0 ? (
              <div className="hb-cap-list">
                {coreAbilities.length > 0 ? (
                  <div className="hb-cap-section-label">
                    {t("instanceDetail.intro.coreAbilities")}
                  </div>
                ) : null}
                {coreAbilities.map((item) => (
                  <div key={item} className="hb-cap">
                    <span className="hb-cap-check">
                      <Check size={12} />
                    </span>
                    <span className="min-w-0 flex-1">{item}</span>
                  </div>
                ))}

                {inScope.length > 0 ? (
                  <div className="hb-cap-section-label">
                    {t("instanceDetail.intro.inScope")}
                  </div>
                ) : null}
                {inScope.map((item) => (
                  <div key={item} className="hb-cap">
                    <span className="hb-cap-check">
                      <Check size={12} />
                    </span>
                    <span className="min-w-0 flex-1">{item}</span>
                  </div>
                ))}

                {outOfScope.length > 0 ? (
                  <div className="hb-cap-section-label">
                    {t("instanceDetail.intro.outOfScope")}
                  </div>
                ) : null}
                {outOfScope.map((item) => (
                  <div key={item} className="hb-cap is-muted">
                    <span className="hb-cap-check">×</span>
                    <span className="min-w-0 flex-1">{item}</span>
                  </div>
                ))}
              </div>
            ) : employee.cardIntro ? (
              <div className="hb-template-doc">
                <ReactMarkdown remarkPlugins={[remarkGfm]}>
                  {employee.cardIntro}
                </ReactMarkdown>
              </div>
            ) : templateDescription ? (
              <p className="hb-detail-copy-text">{templateDescription}</p>
            ) : (
              <p className="hb-detail-empty-text">{t("instanceDetail.intro.empty")}</p>
            )}
          </section>

          {hasEvaluationPanel ? (
            <section className="hb-card hb-detail-panel">
              {/* 标题 + 标签栏 */}
              <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
                <h2 className="hb-section-heading">AI 评估结果</h2>
                <div className="flex flex-wrap gap-2">
                  {(
                    [
                      { key: "overview", label: "概览报告", icon: BarChart2 },
                      { key: "testcase", label: "测试用例", icon: FileText },
                      { key: "trace",    label: "执行轨迹", icon: Zap },
                      { key: "report",   label: "评估报告", icon: BarChart2 },
                    ] as const
                  ).map((tab) => (
                    <button
                      key={tab.key}
                      type="button"
                      onClick={() => setEvalTab(tab.key)}
                      className={`inline-flex items-center gap-1 rounded-full border px-3 py-1.5 text-xs font-medium transition ${
                        evalTab === tab.key
                          ? "border-slate-900 bg-slate-900 text-white"
                          : "border-[var(--hb-border)] bg-white text-[var(--hb-soft)] hover:bg-[var(--hb-surface-soft)]"
                      }`}
                    >
                      <tab.icon size={12} />
                      {tab.label}
                    </button>
                  ))}
                </div>
              </div>

              {/* ── 概览报告 ── */}
              {evalTab === "overview" ? (
                <div className="space-y-3">
                  {/* 结论卡片 */}
                  <div className={`rounded-[22px] border p-4 ${reportSummary?.passed === false ? "eval-report-fail" : "eval-report-pass"}`}>
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary?.passed === false ? "eval-text-red-2" : "eval-text-green-mid"}`}>
                          {reportSummary == null ? "等待评估" : reportSummary.passed ? "✓ 评估通过" : "✗ 评估未通过"}
                        </div>
                        <div className="mt-1 text-base font-semibold eval-text-title">AI 评估结论</div>
                        <div className="mt-1 text-[11px] leading-relaxed eval-text-secondary">
                          {reportSummary
                            ? `第 ${reportSummary.iteration} 轮 · ${formatDateTime(reportSummary.createdAtUtc)}`
                            : "执行评估后，这里会展示本轮结论和关键指标。"}
                        </div>
                      </div>
                      <div className="rounded-2xl border eval-score-card px-4 py-3 text-center">
                        <div className="text-3xl font-bold tabular-nums eval-text-title">{reportSummary?.overallScore ?? "--"}</div>
                        <div className="mt-1 text-[11px] eval-text-secondary">综合评分</div>
                      </div>
                    </div>

                    {/* 统计指标 */}
                    <div className="mt-4 grid grid-cols-2 gap-2 xl:grid-cols-4">
                      {reportMetrics.map((metric) => (
                        <div key={metric.label} className="rounded-2xl border eval-score-card px-3 py-2.5">
                          <div className="text-[10px] uppercase tracking-[0.06em] eval-text-caption">{metric.label}</div>
                          <div className={`mt-1 text-sm font-semibold ${metric.tone}`}>{metric.value}</div>
                        </div>
                      ))}
                    </div>

                    {/* 建议 */}
                    <div className="mt-4 rounded-2xl border eval-recommendation px-3 py-3 text-[11px] leading-relaxed">
                      {evaluation?.recommendation || "暂无建议，等待评估结果生成。"}
                    </div>
                  </div>

                  {/* 维度明细 */}
                  {dimensionScores.length > 0 ? (
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
                            {item.comment ? (
                              <div className="mt-1.5 text-[10px] leading-relaxed eval-text-secondary">{item.comment}</div>
                            ) : null}
                          </div>
                        ))}
                      </div>
                    </details>
                  ) : null}

                  {/* 报告资源 */}
                  {(reportJsonUrl || reportHtmlUrl) ? (
                    <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel">
                      <summary className="eval-side-disclosure-summary">
                        <span className="inline-flex items-center gap-1.5">
                          <FileText size={12} />
                          报告资源
                        </span>
                      </summary>
                      <div className="eval-side-disclosure-body flex flex-wrap gap-2">
                        {reportJsonUrl ? (
                          <a href={reportJsonUrl} target="_blank" rel="noreferrer"
                            className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]">
                            <ExternalLink size={10} /> 查看报告 JSON
                          </a>
                        ) : null}
                        {reportHtmlUrl ? (
                          <a href={reportHtmlUrl} target="_blank" rel="noreferrer"
                            className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]">
                            <ExternalLink size={10} /> 下载报告 HTML
                          </a>
                        ) : null}
                      </div>
                    </details>
                  ) : null}

                  {/* 场景结论概览 */}
                  {(evaluation?.scenarios ?? []).length > 0 ? (
                    <details className="eval-side-disclosure rounded-[20px] border eval-overview-panel" open>
                      <summary className="eval-side-disclosure-summary">
                        <span className="inline-flex items-center gap-1.5">
                          <Check size={12} />
                          场景结论 · {evaluation!.scenarios.length} 个
                        </span>
                      </summary>
                      <div className="eval-side-disclosure-body space-y-2">
                        {evaluation!.scenarios.map((scenario) => (
                          <div key={scenario.scenarioId} className="rounded-xl border eval-side-case-card px-3 py-2.5">
                            <div className="flex items-center justify-between gap-2">
                              <div className="min-w-0 truncate text-[12px] font-medium eval-text-title">
                                {scenario.scenarioName}
                              </div>
                              <span className={`shrink-0 rounded-full border px-2 py-0.5 text-[10px] font-medium ${verdictPillClass(scenario.verdict)}`}>
                                {verdictLabel(scenario.verdict)}
                              </span>
                            </div>
                            {scenario.verdictComment ? (
                              <div className="mt-1.5 text-[11px] leading-relaxed eval-text-secondary">
                                {scenario.verdictComment}
                              </div>
                            ) : null}
                          </div>
                        ))}
                      </div>
                    </details>
                  ) : null}
                </div>
              ) : null}

              {/* ── 测试用例 ── */}
              {evalTab === "testcase" ? (
                <div className="space-y-3">
                  {testcaseItems.length === 0 ? (
                    <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                      暂无测试用例。
                    </div>
                  ) : (
                    <>
                      <div className="rounded-[18px] eval-side-status-banner px-4 py-3 text-[12px] font-medium">
                        <span className="inline-flex items-center gap-2">
                          <Check size={13} />
                          测试场景已就绪，共 {testcaseItems.length} 个
                        </span>
                      </div>
                      {testcaseItems.map((outline) => {
                        const card = questionCardMap.get(outline.testcaseId);
                        const expanded = expandedCardIds.includes(outline.testcaseId);
                        return (
                          <article key={outline.testcaseId} className="rounded-[20px] border eval-side-case-card px-4 py-4">
                            <div className="flex items-start justify-between gap-3">
                              <div className="flex min-w-0 items-start gap-3">
                                <span className="mt-1.5 h-2.5 w-2.5 shrink-0 rounded-full bg-[var(--hb-text-green)]" />
                                <div className="min-w-0">
                                  <div className="text-[14px] font-semibold leading-6 eval-text-title">{outline.title}</div>
                                  <div className="mt-2 border-l-2 border-[rgba(148,163,184,0.18)] pl-3 text-[12px] leading-6 eval-text-body-2">
                                    {outline.userRequest || "未提供用户请求。"}
                                  </div>
                                </div>
                              </div>
                              <span className="shrink-0 text-[11px] font-mono eval-text-caption">{outline.testcaseId}</span>
                            </div>

                            {card ? (
                              <div className="mt-4 flex flex-wrap items-center gap-4 text-[12px]">
                                <button type="button" className="eval-side-inline-action" onClick={() => toggleCard(outline.testcaseId)}>
                                  {expanded ? "收起题卡" : "展开题卡"}
                                </button>
                              </div>
                            ) : null}

                            {expanded && card ? (
                              <div className="mt-4 rounded-[18px] border eval-side-case-detail px-3 py-3">
                                {card.prompt ? (
                                  <div className="rounded-xl border eval-prompt-box px-3 py-2.5 text-[11px] leading-relaxed">{card.prompt}</div>
                                ) : null}
                                {card.steps.length > 0 ? (
                                  <div className="mt-3 space-y-2">
                                    <div className="text-[10px] font-semibold uppercase tracking-[0.06em] eval-text-caption">
                                      评估步骤（{card.steps.length}）
                                    </div>
                                    {card.steps.map((step, si) => (
                                      <div key={`${card.testcaseId}_step_${si}`} className="flex gap-2 rounded-xl border eval-step-row px-2.5 py-2">
                                        <span className="flex h-4 w-4 shrink-0 items-center justify-center rounded-full eval-seq-circle text-[9px] font-semibold">
                                          {si + 1}
                                        </span>
                                        <div className="text-[11px] leading-relaxed eval-text-body-2">{step}</div>
                                      </div>
                                    ))}
                                  </div>
                                ) : null}
                                {(card.scoringHint || card.sourceFile) ? (
                                  <div className="mt-3 space-y-2">
                                    {card.scoringHint ? (
                                      <div className="rounded-xl border border-dashed eval-scoring-hint px-3 py-2 text-[11px] leading-relaxed">
                                        <span className="font-semibold eval-text-body-2">评分提示：</span>{card.scoringHint}
                                      </div>
                                    ) : null}
                                    {card.sourceFile ? (
                                      <div className="text-[10px] eval-text-caption">来源文件：{card.sourceFile}</div>
                                    ) : null}
                                  </div>
                                ) : null}
                              </div>
                            ) : null}
                          </article>
                        );
                      })}
                    </>
                  )}
                </div>
              ) : null}

              {/* ── 执行轨迹 ── */}
              {evalTab === "trace" ? (
                <div className="space-y-3">
                  {traceAssets.length === 0 ? (
                    <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                      本次评估未生成执行轨迹。
                    </div>
                  ) : (
                    <>
                      <div className="rounded-[18px] border eval-overview-panel px-4 py-3">
                        <div className="text-[12px] font-medium eval-text-title">已生成 {traceAssets.length} 份执行轨迹</div>
                        <div className="mt-1 text-[11px] eval-text-secondary">
                          最新更新时间：{formatDateTime(traceAssets[0]?.createdAtUtc)}
                        </div>
                      </div>
                      {traceAssets.map((asset, index) => {
                        const sessionId = evaluation?.sessionId ?? null;
                        const isExpanded = sessionId != null && expandedTraceIds.includes(sessionId);
                        const traceData = sessionId != null ? traceDataCache[sessionId] : undefined;
                        const TURN_COLORS = ["l0", "l1", "l2", "l3", "l4"] as const;

                        return (
                          <div key={asset.relativePath} className="rounded-[18px] border eval-trace-card px-3 py-3">
                            <div className="flex items-center justify-between gap-2">
                              <div className="flex items-center gap-1.5">
                                <Zap size={11} className="eval-text-brand" />
                                <span className="text-[12px] font-semibold eval-text-title">轨迹 #{index + 1}</span>
                                <span className="text-[11px] eval-text-caption">{formatDateTime(asset.createdAtUtc)}</span>
                              </div>
                              <div className="flex items-center gap-2">
                                <a href={toAbsoluteApiUrl(asset.publicUrl) ?? asset.publicUrl} target="_blank" rel="noreferrer"
                                  className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-2.5 py-0.5 text-[11px]">
                                  <ExternalLink size={10} /> 原始文件
                                </a>
                                {sessionId ? (
                                  <button type="button"
                                    onClick={() => void toggleTrace(sessionId)}
                                    className="flex items-center gap-1 rounded-full border eval-pill-neutral px-2.5 py-0.5 text-[11px]">
                                    {traceData === "loading"
                                      ? <Loader2 size={10} className="animate-spin" />
                                      : <ChevronDown size={10} className={`transition-transform ${isExpanded ? "rotate-180" : ""}`} />}
                                    {isExpanded ? "收起" : "展开轨迹"}
                                  </button>
                                ) : null}
                              </div>
                            </div>

                            {isExpanded ? (
                              <div className="mt-3 space-y-2">
                                {traceData === "loading" ? (
                                  <div className="flex items-center gap-2 text-[11px] eval-text-secondary">
                                    <Loader2 size={11} className="animate-spin" />正在加载轨迹数据...
                                  </div>
                                ) : traceData === "error" ? (
                                  <div className="rounded-xl border eval-side-notice-warning px-3 py-2 text-[11px]">
                                    加载失败，请检查网络或重试。
                                  </div>
                                ) : traceData != null && typeof traceData === "object" ? (() => {
                                  const td = traceData as TraceJsonData;
                                  const usage = td.http_supplement?.dashboard?.providers?.usage?.[0];
                                  return (
                                    <>
                                      <div className="flex flex-wrap gap-1.5">
                                        {td.status ? (
                                          <span className={`rounded-full border px-2 py-0.5 text-[10px] font-semibold ${
                                            td.status === "completed" ? "eval-tone-completed"
                                            : td.status === "failed" ? "eval-tone-failed"
                                            : "eval-tone-running"
                                          }`}>{td.status}</span>
                                        ) : null}
                                        {td.meta?.total_turns != null ? (
                                          <span className="rounded-full border eval-stats-badge px-2 py-0.5 text-[10px]">
                                            {td.meta.total_turns} 轮
                                          </span>
                                        ) : null}
                                        {usage?.modelId ? (
                                          <span className="rounded-full border eval-trace-model-badge px-2 py-0.5 text-[10px] font-mono">
                                            {usage.modelId}
                                          </span>
                                        ) : null}
                                        {usage ? (
                                          <span className="rounded-full border eval-trace-token-badge px-2 py-0.5 text-[10px]">
                                            ↑{usage.inputTokens ?? 0} ↓{usage.outputTokens ?? 0}
                                          </span>
                                        ) : null}
                                      </div>
                                      {td.turns.map((turn) => {
                                        const et = turn.execution_trace;
                                        const colorIdx = turn.turn_index % TURN_COLORS.length;
                                        const toolLogs = et.logs.filter(
                                          (l) => l.type === "tool_use" || l.type === "tool_result",
                                        );
                                        return (
                                          <details key={turn.turn_index} open
                                            className={`eval-side-disclosure rounded-[16px] border eval-overview-panel eval-trace-turn-${TURN_COLORS[colorIdx]}`}>
                                            <summary className="eval-side-disclosure-summary">
                                              <span className="inline-flex items-center gap-2">
                                                <span className={`inline-flex h-[20px] w-[20px] items-center justify-center rounded-full text-[10px] font-bold eval-trace-seq-${colorIdx}`}>
                                                  {turn.turn_index + 1}
                                                </span>
                                                {turn.test_case_id ? (
                                                  <span className={`rounded-full px-1.5 py-0.5 text-[10px] font-mono font-semibold eval-trace-seq-${colorIdx}`}>
                                                    {turn.test_case_id}
                                                  </span>
                                                ) : null}
                                                {et.summary?.execution_time_seconds != null ? (
                                                  <span className="text-[10px] eval-text-caption">
                                                    {et.summary.execution_time_seconds.toFixed(1)}s
                                                  </span>
                                                ) : null}
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
                                                  : <div className="text-[11px] opacity-40 italic">（已通过评估脚本注入）</div>}
                                              </div>
                                              {et.assembled_assistant_text ? (
                                                <div className="rounded-xl border eval-trace-ai-block px-2.5 py-2">
                                                  <div className="mb-1 flex items-center gap-1 eval-text-indigo">
                                                    <Bot size={9} />
                                                    <span className="text-[10px] font-medium">AI 回复</span>
                                                  </div>
                                                  <div className="max-h-[100px] overflow-y-auto whitespace-pre-wrap break-words text-[11px] leading-relaxed eval-text-secondary">
                                                    {et.assembled_assistant_text}
                                                  </div>
                                                </div>
                                              ) : null}
                                              {et.summary ? (
                                                <div className="flex flex-wrap gap-1.5">
                                                  {et.summary.total_messages != null ? (
                                                    <span className="rounded-full border eval-stats-badge-ontology px-2 py-0.5 text-[10px]">
                                                      {et.summary.total_messages} msgs
                                                    </span>
                                                  ) : null}
                                                  {(et.summary.total_tool_calls ?? 0) > 0 ? (
                                                    <span className="rounded-full border eval-trace-badge-tool-use px-2 py-0.5 text-[10px]">
                                                      {et.summary.total_tool_calls} 工具
                                                    </span>
                                                  ) : null}
                                                  {et.summary.has_thought ? (
                                                    <span className="rounded-full border eval-trace-thought-badge px-2 py-0.5 text-[10px]">
                                                      思维链 ×{et.summary.think_count}
                                                    </span>
                                                  ) : null}
                                                </div>
                                              ) : null}
                                              {toolLogs.map((log, li) => (
                                                <div key={li} className="rounded-xl border eval-dim-item px-2.5 py-2">
                                                  <div className="flex items-center gap-1.5">
                                                    <span className={`rounded-md px-1.5 py-0.5 text-[10px] font-mono ${
                                                      log.type === "tool_use" ? "eval-trace-badge-tool-use" : "eval-trace-badge-tool-result"
                                                    }`}>
                                                      {log.type === "tool_use" ? "→ tool" : "← result"}
                                                    </span>
                                                    {log.name ? (
                                                      <span className="text-[11px] font-medium eval-text-title">{log.name}</span>
                                                    ) : null}
                                                    {(log.timestamp_start ?? log.timestamp) ? (
                                                      <span className="ml-auto text-[10px] eval-text-caption font-mono">
                                                        {(log.timestamp_start ?? log.timestamp ?? "").slice(11, 19)}
                                                      </span>
                                                    ) : null}
                                                  </div>
                                                  {log.type === "tool_use" && log.input != null ? (
                                                    <pre className="mt-1.5 max-h-[80px] overflow-y-auto break-all text-[10px] font-mono eval-text-secondary whitespace-pre-wrap">
                                                      {JSON.stringify(log.input, null, 2).slice(0, 400)}
                                                    </pre>
                                                  ) : null}
                                                  {log.type === "tool_result" && log.content != null ? (
                                                    <pre className="mt-1.5 max-h-[80px] overflow-y-auto break-all text-[10px] font-mono eval-text-secondary whitespace-pre-wrap">
                                                      {typeof log.content === "string"
                                                        ? log.content.slice(0, 400)
                                                        : JSON.stringify(log.content).slice(0, 400)}
                                                    </pre>
                                                  ) : null}
                                                </div>
                                              ))}
                                            </div>
                                          </details>
                                        );
                                      })}
                                    </>
                                  );
                                })() : null}
                              </div>
                            ) : null}
                          </div>
                        );
                      })}
                    </>
                  )}
                </div>
              ) : null}

              {/* ── 评估报告 ── */}
              {evalTab === "report" ? (
                <div className="space-y-3">
                  {!reportSummary ? (
                    <div className="rounded-[20px] border eval-empty-card px-4 py-3 text-[11px]">
                      暂无评估报告，请先执行评估。
                    </div>
                  ) : (
                    <>
                      <div className={`rounded-2xl border p-4 ${reportSummary.passed ? "eval-report-pass" : "eval-report-fail"}`}>
                        <div className="flex items-center justify-between gap-3">
                          <div>
                            <div className={`text-[11px] font-semibold uppercase tracking-[0.08em] ${reportSummary.passed ? "eval-text-green-mid" : "eval-text-red-2"}`}>
                              {reportSummary.passed ? "✓ 评估通过" : "✗ 评估未通过"}
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

                      {dimensionScores.length > 0 ? (
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
                                {item.comment ? (
                                  <div className="mt-1 text-[10px] leading-relaxed eval-text-secondary">{item.comment}</div>
                                ) : null}
                              </div>
                            ))}
                          </div>
                        </details>
                      ) : null}

                      {(reportJsonUrl || reportHtmlUrl) ? (
                        <div className="flex flex-wrap gap-2">
                          {reportJsonUrl ? (
                            <a href={reportJsonUrl} target="_blank" rel="noreferrer"
                              className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]">
                              <ExternalLink size={10} /> 查看报告 JSON
                            </a>
                          ) : null}
                          {reportHtmlUrl ? (
                            <a href={reportHtmlUrl} target="_blank" rel="noreferrer"
                              className="inline-flex items-center gap-1 rounded-full border eval-pill-neutral px-3 py-1 text-[11px]">
                              <ExternalLink size={10} /> 下载报告 HTML
                            </a>
                          ) : null}
                        </div>
                      ) : null}
                    </>
                  )}
                </div>
              ) : null}
            </section>
          ) : null}
        </div>
      )}

      <CloneEmployeeModal
        open={cloneModalOpen}
        employeeId={employee?.employeeId ?? ""}
        sourceNickname={employee?.nickname ?? ""}
        sourceRoleName={employee?.roleName ?? ""}
        onClose={() => setCloneModalOpen(false)}
        onSuccess={() => {
          setCloneModalOpen(false);
          void loadEmployee();
        }}
      />
    </div>
  );
}
