import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Check, Clock3, CopyPlus, Loader2, ShieldCheck, X } from "lucide-react";
import { api, type EmployeeDetail, type EvaluationState } from "@/infra/api";
import CloneEmployeeModal from "./CloneEmployeeModal";
import {
  toEmployeeDetailSummary,
  withEmployeeView,
} from "../employeeView";

interface EmployeeDetailModalProps {
  open: boolean;
  employeeId: string;
  onClose: () => void;
  onCloneSuccess?: () => void;
}

export default function EmployeeDetailModal({
  open,
  employeeId,
  onClose,
  onCloneSuccess,
}: EmployeeDetailModalProps) {
  const [detail, setDetail] = useState<EmployeeDetail | null>(null);
  const [evaluation, setEvaluation] = useState<EvaluationState | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cloneModalOpen, setCloneModalOpen] = useState(false);
  const { t } = useTranslation();

  useEffect(() => {
    if (!open || !employeeId) return;

    setDetail(null);
    setEvaluation(null);
    setError(null);

    async function load() {
      setLoading(true);
      try {
        const [data, evaluationState] = await Promise.all([
          api.employeeRuntime.getEmployee(employeeId),
          api.employeeRuntime.getEvaluationState(employeeId).catch(() => null),
        ]);
        setDetail(data);
        setEvaluation(evaluationState);
      } catch (err) {
        setError(err instanceof Error ? err.message : t('hiring.employee.loadFailed'));
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, [open, employeeId]);

  if (!open) return null;

  const view = detail
    ? withEmployeeView(toEmployeeDetailSummary(detail))
    : null;
  const canClone =
    view?.ownership === "department" && view?.mappedStatus === "live";
  const readyCount = detail
    ? detail.capabilities.filter((c) => c.ready).length
    : 0;
  const latestReport = evaluation?.latestReport ?? detail?.latestReport ?? null;
  const evaluationScenarios = evaluation?.scenarios ?? [];
  const evaluationRecommendation = evaluation?.recommendation?.trim() ?? "";

  return (
    <>
      <div className="hb-modal-mask" onClick={onClose}>
        <div className="hb-modal" onClick={(e) => e.stopPropagation()}>
          <button
            type="button"
            className="hb-modal-close"
            onClick={onClose}
            aria-label={t('hiring.button.close')}
          >
            <X size={16} />
          </button>

          {loading ? (
            <div className="flex items-center justify-center gap-2 p-10 text-sm text-[var(--hb-soft)]">
              <Loader2 size={16} className="animate-spin" />
              {t('hiring.employee.loading')}
            </div>
          ) : error ? (
            <div className="p-8">
              <div className="hb-alert hb-alert-error">{error}</div>
            </div>
          ) : !detail || !view ? (
            <div className="p-8 text-sm text-[var(--hb-soft)]">
              {t('hiring.employee.notFound')}
            </div>
          ) : (
            <>
              <div className="hb-modal-head">
                <h3 className="hb-modal-title">{t('hiring.employee.modalTitle')}</h3>
              </div>

              <div className="hb-modal-body space-y-4">
                <div className="min-w-0 flex-1">
                  <h3 className="hb-detail-panel-title truncate">
                    {detail.nickname}
                  </h3>
                  <p className="mt-1 text-sm text-[var(--hb-soft)]">
                    {detail.roleName || detail.sourceTemplate}
                  </p>
                </div>

                {/* 元信息 */}
                <div className="rounded-lg border border-[var(--hb-border)] bg-[var(--hb-surface-soft)] p-3 text-xs">
                  <div className="flex flex-wrap gap-x-4 gap-y-1">
                    <span>
                      {t('hiring.employee.sourceTemplate', { template: detail.sourceTemplate || "—" })}
                    </span>
                    <span>{t('hiring.employee.department', { dept: detail.owningTeam || detail.departmentId })}</span>
                    <span>{t('hiring.employee.createdAt', { date: detail.createdAt })}</span>
                    {detail.graduatedAt && (
                      <span>{t('hiring.employee.graduatedAt', { date: detail.graduatedAt })}</span>
                    )}
                  </div>
                </div>

                {/* 能力列表 */}
                <div>
                  <h4 className="mb-2 text-xs font-medium text-[var(--hb-soft)]">
                    {t('hiring.employee.capabilities')}
                  </h4>
                  <div className="space-y-1">
                    {detail.capabilities.map((cap) => (
                      <div
                        key={cap.name}
                        className={`flex items-center gap-2 rounded px-2 py-1 text-sm ${cap.ready ? "" : "text-[var(--hb-soft)]"}`}
                      >
                        <span className="flex h-4 w-4 items-center justify-center rounded-full bg-[var(--hb-surface-soft)] text-[10px]">
                          {cap.ready ? (
                            <Check size={10} className="text-emerald-500" />
                          ) : (
                            "×"
                          )}
                        </span>
                        <span className="truncate">{cap.name}</span>
                      </div>
                    ))}
                  </div>
                </div>

                {/* 运行状态 */}
                {view.mappedStatus === "live" && (
                  <div className="flex gap-4 rounded-lg border border-[var(--hb-border)] p-3 text-xs">
                    <div className="flex items-center gap-1">
                      <Clock3 size={12} />
                      <strong>{detail.graduatedAt || "—"}</strong>
                      <span className="text-[var(--hb-soft)]">{t('hiring.employee.graduated')}</span>
                    </div>
                    <div className="flex items-center gap-1">
                      <ShieldCheck size={12} />
                      <strong>{readyCount}</strong>
                      <span className="text-[var(--hb-soft)]">{t('hiring.employee.capabilityCount')}</span>
                    </div>
                    <div className="flex items-center gap-1">
                      <span className="text-[var(--hb-soft)]">{t('hiring.employee.version')}</span>
                      <strong>
                        {detail.isConfigured ? "v1.0" : t('hiring.employee.notConfigured')}
                      </strong>
                    </div>
                  </div>
                )}

                {/* AI 评估结果 */}
                {latestReport && (
                  <div className="rounded-lg border border-indigo-200 bg-indigo-50 p-3 text-xs text-indigo-950">
                    <div className="flex items-start justify-between gap-3">
                      <div>
                        <div className="text-[11px] font-semibold uppercase tracking-[0.06em] text-indigo-700">
                          AI 评估结果
                        </div>
                        <div className="mt-1 text-sm font-semibold text-indigo-950">
                          {latestReport.passed ? "已通过 AI 评估" : "未通过 AI 评估"}
                        </div>
                        <div className="mt-1 text-[11px] leading-relaxed text-indigo-700">
                          第 {latestReport.iteration} 轮 · {new Date(latestReport.createdAtUtc).toLocaleString()}
                        </div>
                      </div>
                      <div className="rounded-2xl border border-indigo-200 bg-white px-4 py-2 text-center">
                        <div className="text-2xl font-bold tabular-nums text-indigo-950">
                          {latestReport.overallScore}
                        </div>
                        <div className="mt-0.5 text-[10px] text-indigo-700">综合评分</div>
                      </div>
                    </div>

                    {latestReport.dimensionScores && latestReport.dimensionScores.length > 0 && (
                      <div className="mt-3 grid grid-cols-2 gap-2">
                        {latestReport.dimensionScores.slice(0, 4).map((item) => (
                          <div
                            key={item.dimension}
                            className="rounded-lg border border-indigo-200 bg-white px-3 py-2"
                          >
                            <div className="text-[10px] uppercase tracking-[0.04em] text-indigo-700">
                              {item.dimension}
                            </div>
                            <div className="mt-1 text-sm font-semibold text-indigo-950">
                              {item.score}
                            </div>
                            {item.comment && (
                              <div className="mt-1 line-clamp-2 text-[10px] leading-relaxed text-indigo-700">
                                {item.comment}
                              </div>
                            )}
                          </div>
                        ))}
                      </div>
                    )}

                    {(latestReport.reportHtmlUrl || latestReport.reportJsonUrl) && (
                      <div className="mt-3 flex flex-wrap gap-2">
                        {latestReport.reportHtmlUrl && (
                          <a
                            href={latestReport.reportHtmlUrl}
                            target="_blank"
                            rel="noreferrer"
                            className="inline-flex items-center rounded-full border border-indigo-200 bg-white px-3 py-1 text-[11px] text-indigo-800 transition hover:bg-indigo-100"
                          >
                            查看 HTML 报告
                          </a>
                        )}
                        {latestReport.reportJsonUrl && (
                          <a
                            href={latestReport.reportJsonUrl}
                            target="_blank"
                            rel="noreferrer"
                            className="inline-flex items-center rounded-full border border-indigo-200 bg-white px-3 py-1 text-[11px] text-indigo-800 transition hover:bg-indigo-100"
                          >
                            查看 JSON 报告
                          </a>
                        )}
                      </div>
                    )}

                    {evaluationRecommendation && (
                      <div className="mt-3 rounded-lg border border-indigo-200 bg-white px-3 py-2 text-[11px] leading-relaxed text-indigo-900">
                        <div className="mb-1 font-semibold text-indigo-700">AI 建议</div>
                        <div>{evaluationRecommendation}</div>
                      </div>
                    )}

                    {evaluationScenarios.length > 0 && (
                      <div className="mt-3 space-y-2">
                        <div className="text-[11px] font-semibold text-indigo-700">场景结论</div>
                        {evaluationScenarios.slice(0, 6).map((scenario) => {
                          const verdictLabel =
                            scenario.verdict === "passed"
                              ? "通过"
                              : scenario.verdict === "failed"
                                ? "未通过"
                                : scenario.verdict === "blocked"
                                  ? "阻塞"
                                  : "待判定";
                          const verdictClass =
                            scenario.verdict === "passed"
                              ? "border-emerald-200 bg-emerald-50 text-emerald-700"
                              : scenario.verdict === "failed"
                                ? "border-rose-200 bg-rose-50 text-rose-700"
                                : scenario.verdict === "blocked"
                                  ? "border-amber-200 bg-amber-50 text-amber-700"
                                  : "border-slate-200 bg-slate-50 text-slate-700";

                          return (
                            <div
                              key={scenario.scenarioId}
                              className="rounded-lg border border-indigo-200 bg-white px-3 py-2"
                            >
                              <div className="flex items-center justify-between gap-2">
                                <div className="min-w-0 truncate text-[11px] font-medium text-indigo-950">
                                  {scenario.scenarioName}
                                </div>
                                <span className={`shrink-0 rounded-full border px-2 py-0.5 text-[10px] font-medium ${verdictClass}`}>
                                  {verdictLabel}
                                </span>
                              </div>
                              {scenario.verdictComment && (
                                <div className="mt-1 text-[10px] leading-relaxed text-indigo-700">
                                  {scenario.verdictComment}
                                </div>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    )}
                  </div>
                )}

                {/* 状态说明 */}
                <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-xs text-emerald-800">
                  {view.mappedStatus === "live"
                    ? view.ownership === "department"
                      ? t('hiring.employee.descDepartmentGraduated')
                      : t('hiring.employee.descPersonalGraduated')
                    : view.mappedStatus === "interning_ai"
                      ? t('hiring.employee.descInterningAi')
                      : view.mappedStatus === "interning_human"
                        ? t('hiring.employee.descInterningHuman')
                        : view.mappedStatus === "failed"
                          ? t('hiring.employee.descFailed')
                          : view.mappedStatus === "retired"
                            ? t('hiring.employee.descRetired')
                            : t('hiring.employee.descHired')}
                </div>
              </div>

              <div className="hb-modal-foot">
                {canClone && (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => setCloneModalOpen(true)}
                  >
                    <CopyPlus size={14} />
                    {t('hiring.employee.createClone')}
                  </button>
                )}
                <button
                  type="button"
                  className="hb-btn-ghost"
                  onClick={onClose}
                >
                  {t('hiring.button.close')}
                </button>
              </div>
            </>
          )}
        </div>
      </div>

      <CloneEmployeeModal
        open={cloneModalOpen}
        employeeId={employeeId}
        sourceNickname={detail?.nickname ?? ""}
        sourceRoleName={detail?.roleName ?? ""}
        onClose={() => setCloneModalOpen(false)}
        onSuccess={() => {
          setCloneModalOpen(false);
          onCloneSuccess?.();
        }}
      />
    </>
  );
}
