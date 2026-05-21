import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AlertTriangle, ArrowRight, CheckCircle2, CopyPlus, Loader2, X } from "lucide-react";
import { api } from "@/infra/api";
import { ApiClientError } from "@/infra/api/httpClient";

interface CloneEmployeeModalProps {
  open: boolean;
  employeeId: string;
  sourceNickname: string;
  sourceRoleName: string;
  onClose: () => void;
  onSuccess: () => void;
}

export default function CloneEmployeeModal({
  open,
  employeeId,
  sourceNickname,
  sourceRoleName,
  onClose,
  onSuccess,
}: CloneEmployeeModalProps) {
  const navigate = useNavigate();
  const { t } = useTranslation();
  const [displayName, setDisplayName] = useState("");
  const [cloneCount, setCloneCount] = useState(0);
  const [maxClones, setMaxClones] = useState(10);
  const [loadingCount, setLoadingCount] = useState(false);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cloneSuccess, setCloneSuccess] = useState(false);

  useEffect(() => {
    if (!open) return;

    setDisplayName(t('hiring.clone.defaultName', { sourceName: sourceNickname }));
    setError(null);
    setCloneSuccess(false);

    const max =
      (typeof window !== "undefined" &&
        window.__AUTH_CONFIG__?.MaxActivePersonalClonesPerOwner) ||
      10;
    setMaxClones(max);

    async function loadCount() {
      setLoadingCount(true);
      try {
        const employees = await api.employeeRuntime.getEmployees();
        const count = employees.filter(
          (e) =>
            e.instanceType === "personal_clone" && e.status !== "retired",
        ).length;
        setCloneCount(count);
      } catch {
        // 加载失败不阻塞
      } finally {
        setLoadingCount(false);
      }
    }

    void loadCount();
  }, [open, sourceNickname]);

  if (!open) return null;

  const limitReached = cloneCount >= maxClones;

  function handleClose() {
    if (creating) return;
    onClose();
  }

  async function handleCreate() {
    const name = displayName.trim();
    if (!name) {
      setError(t('hiring.clone.nameMissing'));
      return;
    }

    if (limitReached) return;

    setCreating(true);
    setError(null);

    try {
      await api.employeeRuntime.createPersonalClone(employeeId, {
        displayName: name,
      });
      setCloneSuccess(true);
    } catch (err) {
      const message =
        err instanceof ApiClientError ? err.message : t('hiring.clone.createFailed');
      setError(message);
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className="hb-modal-mask" onClick={handleClose}>
      <div className="hb-modal" onClick={(e) => e.stopPropagation()}>
        <button
          type="button"
          className="hb-modal-close"
          onClick={handleClose}
          aria-label={t('hiring.button.close')}
        >
          <X size={16} />
        </button>

        {cloneSuccess ? (
          <>
            <div className="hb-modal-body text-center py-8">
              <CheckCircle2
                size={40}
                className="mx-auto text-emerald-500"
              />
              <h3 className="mt-3 text-lg font-semibold text-[var(--hb-near-black)]">
                {t('hiring.clone.successTitle')}
              </h3>
              <p className="mt-1 text-sm text-[var(--hb-soft)]">
                {t('hiring.clone.successMessage', { name: displayName })}
              </p>
            </div>
            <div className="hb-modal-foot">
              <button
                type="button"
                className="hb-btn-ghost"
                onClick={() => {
                  onSuccess();
                  handleClose();
                }}
              >
                {t('hiring.button.close')}
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => {
                  onSuccess();
                  handleClose();
                  navigate("/my-employees");
                }}
              >
                {t('hiring.clone.goToEmployees')}
                <ArrowRight size={14} />
              </button>
            </div>
          </>
        ) : (
          <>
            <div className="hb-modal-head">
              <h3 className="hb-modal-title">{t('hiring.clone.title')}</h3>
              <p className="hb-modal-sub">
                {t('hiring.clone.subtitle')}
              </p>
            </div>

            <div className="hb-modal-body space-y-4">
              <div className="rounded-lg border border-[var(--hb-border)] bg-[var(--hb-surface-soft)] p-3">
                <p className="text-xs text-[var(--hb-soft)]">{t('hiring.clone.sourceEmployee')}</p>
                <p className="mt-0.5 truncate text-sm font-medium">
                  {sourceNickname}
                </p>
                <p className="text-xs text-[var(--hb-soft)]">{sourceRoleName}</p>
              </div>

              <div>
                <label className="mb-1 block text-xs font-medium text-[var(--hb-soft)]">
                  {t('hiring.clone.cloneName')}
                </label>
                <input
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  className="hb-input w-full"
                  placeholder={t('hiring.clone.cloneNamePlaceholder')}
                  disabled={creating}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") handleCreate();
                  }}
                />
              </div>

              <div className="flex items-center gap-2 text-xs text-[var(--hb-soft)]">
                {loadingCount ? (
                  <Loader2 size={12} className="animate-spin" />
                ) : null}
                <span>
                  {t('hiring.clone.cloneCountStatus', { count: cloneCount, max: maxClones })}
                </span>
              </div>

              {limitReached && (
                <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-700">
                  <AlertTriangle size={14} className="mt-0.5 flex-shrink-0" />
                  <span>{t('hiring.clone.limitReached', { max: maxClones })}</span>
                </div>
              )}

              {error && (
                <div className="hb-alert hb-alert-error">
                  <span>{error}</span>
                </div>
              )}
            </div>

            <div className="hb-modal-foot">
              <button
                type="button"
                className="hb-btn-ghost"
                onClick={handleClose}
                disabled={creating}
              >
                {t('hiring.button.cancel')}
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={handleCreate}
                disabled={limitReached || creating || !displayName.trim()}
              >
                {creating ? (
                  <>
                    <Loader2 size={14} className="animate-spin" />
                    {t('hiring.clone.creating')}
                  </>
                ) : (
                  <>
                    <CopyPlus size={14} />
                    {t('hiring.clone.confirmCreate')}
                  </>
                )}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}
