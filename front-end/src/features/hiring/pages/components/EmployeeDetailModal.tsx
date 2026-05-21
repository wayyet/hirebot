import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Check, Clock3, CopyPlus, Loader2, ShieldCheck, X } from "lucide-react";
import { api, type EmployeeDetail } from "@/infra/api";
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
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cloneModalOpen, setCloneModalOpen] = useState(false);
  const { t } = useTranslation();

  useEffect(() => {
    if (!open || !employeeId) return;

    setDetail(null);
    setError(null);

    async function load() {
      setLoading(true);
      try {
        const data = await api.employeeRuntime.getEmployee(employeeId);
        setDetail(data);
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
