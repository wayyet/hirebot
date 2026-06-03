import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  Check,
  CopyPlus,
  Loader2,
  MessageCircle,
  PlayCircle,
  RotateCcw,
  Settings,
  ShieldCheck,
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
import { api, type EmployeeDetail } from "@/infra/api";
import { Breadcrumb } from "@/shared/components/Breadcrumb";
import { instanceBasePath } from "@/shared/utils/instancePath";
import { CreatorDisplay } from "@/shared/components/CreatorDisplay";

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

export default function InstanceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const location = useLocation();
  const { t, i18n } = useTranslation();
  const { showToast } = useUxOverlay();

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cloneModalOpen, setCloneModalOpen] = useState(false);
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

  const loadEmployee = useCallback(async () => {
    if (!id) {
      setLoading(false);
      return;
    }

    setLoading(true);
    setError("");

    try {
      const data = await api.employeeRuntime.getEmployee(id);
      setEmployee(data);

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
