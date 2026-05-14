import { useEffect, useMemo, useState } from "react";
import {
  BarChart2,
  Bot,
  CheckCircle2,
  CopyPlus,
  Loader2,
  MoreHorizontal,
  Search,
  Sparkles,
  Trash2,
  X,
  UserCheck,
  Users,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useUserRole } from "@/app/context/UserRoleContext";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { api, type EmployeeSummary } from "@/infra/api";
import TemplateUploadModal from "./components/TemplateUploadModal";
import CloneEmployeeModal from "./components/CloneEmployeeModal";
import EmployeeDetailModal from "./components/EmployeeDetailModal";
import {
  statusClass,
  statusLabel,
  withEmployeeView,
} from "./employeeView";
import { Pagination } from "@/shared/components/Pagination";

type StageTab = "hired" | "intern" | "live";
type InternSubTab = "ai" | "human";

const PAGE_SIZE = 9;

export default function DepartmentEmployeesPage() {
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { showToast } = useUxOverlay();
  const { role } = useUserRole();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [employees, setEmployees] = useState<EmployeeSummary[]>([]);
  const [tab, setTab] = useState<StageTab>("live");
  const [internSubTab, setInternSubTab] = useState<InternSubTab>("ai");
  const [query, setQuery] = useState("");
  const [uploadModalOpen, setUploadModalOpen] = useState(false);
  const [refreshKey, setRefreshKey] = useState(0);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [menuOpenId, setMenuOpenId] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<{
    employeeId: string;
    nickname: string;
  } | null>(null);
  const [page, setPage] = useState(1);
  const [cloneTarget, setCloneTarget] = useState<{
    employeeId: string;
    nickname: string;
    roleName: string;
  } | null>(null);
  const [detailEmployeeId, setDetailEmployeeId] = useState<string | null>(null);

  async function handleDelete(employeeId: string) {
    setDeletingId(employeeId);
    try {
      await api.employeeRuntime.deleteEmployee(employeeId);
      setRefreshKey((k) => k + 1);
      setDeleteTarget(null);
    } catch (deleteError: unknown) {
      const message =
        deleteError instanceof Error
          ? deleteError.message
          : t("employees.departmentPage.deleteFailed");
      showToast(message, "error");
    } finally {
      setDeletingId(null);
    }
  }

  useEffect(() => {
    let cancelled = false;

    async function loadEmployees() {
      setLoading(true);
      setError("");

      try {
        const items = await api.employeeRuntime.getEmployees();
        if (!cancelled) {
          setEmployees(items);
        }
      } catch (requestError: unknown) {
        if (!cancelled) {
          setEmployees([]);
          setError(
            requestError instanceof Error
              ? requestError.message
                : t("employees.departmentPage.loadFailed"),
          );
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void loadEmployees();

    return () => {
      cancelled = true;
    };
  }, [refreshKey]);

  const viewedEmployees = useMemo(() => {
    return employees
      .map(withEmployeeView)
      .filter((item) => item.ownership === "department");
  }, [employees]);

  const counts = useMemo(() => {
    return {
      hired: viewedEmployees.filter(
        (item) =>
          item.mappedStatus === "hired" || item.mappedStatus === "failed",
      ).length,
      ai: viewedEmployees.filter((item) => item.mappedStatus === "interning_ai")
        .length,
      human: viewedEmployees.filter(
        (item) => item.mappedStatus === "interning_human",
      ).length,
      intern: viewedEmployees.filter(
        (item) =>
          item.mappedStatus === "interning_ai" ||
          item.mappedStatus === "interning_human",
      ).length,
      live: viewedEmployees.filter((item) => item.mappedStatus === "live")
        .length,
    };
  }, [viewedEmployees]);

  const visibleEmployees = useMemo(() => {
    const activeTab = role === "member" ? "live" : tab;

    const baseList = (() => {
      if (role === "member") {
        return viewedEmployees.filter((item) => item.mappedStatus === "live");
      }

      if (activeTab === "live") {
        return viewedEmployees.filter((item) => item.mappedStatus === "live");
      }

      if (activeTab === "hired") {
        return viewedEmployees.filter(
          (item) =>
            item.mappedStatus === "hired" || item.mappedStatus === "failed",
        );
      }

      if (internSubTab === "ai") {
        return viewedEmployees.filter(
          (item) => item.mappedStatus === "interning_ai",
        );
      }

      return viewedEmployees.filter(
        (item) => item.mappedStatus === "interning_human",
      );
    })();

    const keyword = query.trim().toLowerCase();
    if (!keyword) return baseList;

    return baseList.filter((item) => {
      const haystack = [
        item.nickname,
        item.roleName,
        item.sourceTemplate,
        item.primarySignal,
        item.stageSummary,
      ]
        .join(" ")
        .toLowerCase();
      return haystack.includes(keyword);
    });
  }, [internSubTab, query, role, tab, viewedEmployees]);

  const totalPages = Math.max(1, Math.ceil(visibleEmployees.length / PAGE_SIZE));

  const pagedEmployees = useMemo(() => {
    const start = (page - 1) * PAGE_SIZE;
    return visibleEmployees.slice(start, start + PAGE_SIZE);
  }, [page, visibleEmployees]);

  useEffect(() => {
    setPage(1);
  }, [internSubTab, query, role, tab]);

  useEffect(() => {
    if (page > totalPages) {
      setPage(totalPages);
    }
  }, [page, totalPages]);

  useEffect(() => {
    if (!menuOpenId) {
      return;
    }

    function closeMenu() {
      setMenuOpenId(null);
    }

    window.addEventListener("click", closeMenu);
    return () => {
      window.removeEventListener("click", closeMenu);
    };
  }, [menuOpenId]);

  function openAiEvaluation(employeeId: string) {
    navigate(`/department-employees/instances/${employeeId}/evaluation`);
  }

  function openHumanEvaluation(employeeId: string) {
    navigate(`/department-employees/instances/${employeeId}/human-evaluation`);
  }

  function openCard(employee: { employeeId: string; mappedStatus: string }) {
    if (employee.mappedStatus === "interning_ai") {
      navigate(
        `/department-employees/instances/${employee.employeeId}/evaluation`,
      );
    } else if (employee.mappedStatus === "interning_human") {
      navigate(
        `/department-employees/instances/${employee.employeeId}/human-evaluation`,
      );
    } else {
      setDetailEmployeeId(employee.employeeId);
    }
  }

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">
            {role === "manager"
              ? t("employees.departmentPage.kickerManager")
              : t("employees.departmentPage.kickerMember")}
          </span>
          <h1 className="hb-page-title">{t("employees.departmentPage.title")}</h1>
          <p className="hb-page-copy">
            {role === "manager"
              ? t("employees.departmentPage.copyManager", {
                  department: t("employees.departmentPage.departmentName"),
                })
              : t("employees.departmentPage.copyMember", {
                  department: t("employees.departmentPage.departmentName"),
                })}
          </p>
        </div>
        {role === "manager" ? (
          <div className="hb-page-actions">
            <button
              type="button"
              className="hb-btn-primary hb-hub-btn-primary"
              onClick={() => setUploadModalOpen(true)}
            >
              {t("employees.departmentPage.actions.uploadTemplate")}
            </button>
            <button
              type="button"
              className="hb-btn-primary hb-hub-btn-primary"
              onClick={() => navigate("/template-pool")}
            >
              {t("employees.departmentPage.actions.hireFromPool")}
            </button>
          </div>
        ) : null}
      </div>

      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Users size={14} /> {t("employees.ownership.department")}
          </div>
          <div className="hb-stat-value">{viewedEmployees.length}</div>
          <div className="hb-stat-note">{t("employees.departmentPage.stats.totalNote")}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <CheckCircle2 size={14} /> {t("employees.departmentPage.stats.liveLabel")}
          </div>
          <div className="hb-stat-value">{counts.live}</div>
          <div className="hb-stat-note">{t("employees.departmentPage.stats.liveNote")}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Sparkles size={14} /> {t("employees.departmentPage.stats.evaluatingLabel")}
          </div>
          <div className="hb-stat-value">{counts.intern}</div>
          <div className="hb-stat-note">{t("employees.departmentPage.stats.evaluatingNote")}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <BarChart2 size={14} /> {t("employees.departmentPage.stats.hiredLabel")}
          </div>
          <div className="hb-stat-value">{counts.hired}</div>
          <div className="hb-stat-note">{t("employees.departmentPage.stats.hiredNote")}</div>
        </div>
      </div>

      <div className="hb-search-shell mt-5">
        <Search size={16} />
        <input
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          className="hb-search-input"
          placeholder={t("employees.departmentPage.searchPlaceholder")}
        />
        <div className="hb-search-controls">
          <button
            type="button"
            className="hb-btn-ghost hb-hub-btn-secondary"
            onClick={() => setQuery("")}
          >
            {t("employees.departmentPage.clearFilters")}
          </button>
        </div>
      </div>

      <div className="mt-5">
        <div className="hb-tab-row">
          {(role === "manager"
            ? [
                { id: "hired" as const, label: t("employees.departmentPage.tabs.hired"), count: counts.hired },
                {
                  id: "intern" as const,
                  label: t("employees.departmentPage.tabs.intern"),
                  count: counts.intern,
                },
                { id: "live" as const, label: t("employees.departmentPage.tabs.live"), count: counts.live },
              ]
            : [{ id: "live" as const, label: t("employees.departmentPage.tabs.live"), count: counts.live }]
          ).map((item) => (
            <button
              key={item.id}
              type="button"
              className={`hb-tab ${tab === item.id ? "is-active" : ""}`}
              onClick={() => setTab(item.id)}
            >
              {item.label}
              <span className="ml-2 text-xs text-[var(--hb-soft)]">
                {item.count}
              </span>
            </button>
          ))}
        </div>
      </div>

      {role === "manager" && tab === "intern" ? (
        <div className="mt-5 hb-chip-row">
          <button
            type="button"
            className={`hb-chip ${internSubTab === "ai" ? "is-active" : ""}`}
            onClick={() => setInternSubTab("ai")}
          >
            {t("employees.departmentPage.subtabs.ai")}
            <span>{counts.ai}</span>
          </button>
          <button
            type="button"
            className={`hb-chip ${internSubTab === "human" ? "is-active" : ""}`}
            onClick={() => setInternSubTab("human")}
          >
            {t("employees.departmentPage.subtabs.human")}
            <span>{counts.human}</span>
          </button>
        </div>
      ) : null}

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <span>{error}</span>
        </div>
      ) : null}

      <div className="mt-5">
        {loading ? (
          <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[var(--hb-soft)]">
            <Loader2 size={16} className="animate-spin" />
            {t("employees.departmentPage.loading")}
          </div>
        ) : visibleEmployees.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">{t("employees.departmentPage.emptyTitle")}</div>
            <div className="hb-empty-copy">
              {role === "manager"
                ? t("employees.departmentPage.emptyCopyManager")
                : t("employees.departmentPage.emptyCopyMember")}
            </div>
          </div>
        ) : (
          <div className="hb-asset-grid">
            {pagedEmployees.map((employee) => {
              const canClone =
                employee.ownership === "department" &&
                employee.mappedStatus === "live";
              const isInterningAi = employee.mappedStatus === "interning_ai";
              const isInterningHuman =
                employee.mappedStatus === "interning_human";
              return (
                <article
                  key={employee.employeeId}
                  className="hb-card hb-employee-card"
                >
                  <div
                    className="hb-employee-card-menu-anchor"
                    onClick={(event) => event.stopPropagation()}
                  >
                    <button
                      type="button"
                      className="hb-employee-card-menu-btn"
                      onClick={(event) => {
                        event.stopPropagation();
                        setMenuOpenId((current) =>
                          current === employee.employeeId
                            ? null
                            : employee.employeeId,
                        );
                      }}
                      title={t("employees.departmentPage.actions.more")}
                    >
                      <MoreHorizontal size={16} />
                    </button>
                    {menuOpenId === employee.employeeId ? (
                      <div className="hb-dropdown-menu hb-dropdown-menu--right hb-employee-card-menu">
                        <button
                          type="button"
                          className="hb-dropdown-item hb-dropdown-item--danger"
                          onClick={(event) => {
                            event.stopPropagation();
                            setMenuOpenId(null);
                            setDeleteTarget({
                              employeeId: employee.employeeId,
                              nickname: employee.nickname,
                            });
                          }}
                        >
                          <Trash2 size={14} />
                          {t("common.delete")}
                        </button>
                      </div>
                    ) : null}
                  </div>
                  <button
                    type="button"
                    onClick={() => openCard(employee)}
                    className="block w-full flex-1 pr-10 text-left"
                  >
                    <div className="hb-employee-card-head">
                      <div className="min-w-0 flex-1">
                        <h3 className="hb-employee-card-title">
                          {employee.nickname}
                        </h3>
                        <div className="hb-employee-card-badges mt-1">
                          <span
                            className={`hb-pill flex-shrink-0 ${statusClass(employee.mappedStatus, employee.lifecycleStatus)}`}
                          >
                            {statusLabel(
                              employee.mappedStatus,
                              employee.lifecycleStatus,
                            )}
                          </span>
                          <p className="hb-employee-card-subtitle">
                            {employee.roleName || employee.sourceTemplate}
                          </p>
                        </div>
                      </div>
                    </div>
                    <p className="hb-employee-card-desc">
                      {employee.primarySignal || employee.stageSummary}
                    </p>
                  </button>
                  <div className="hb-employee-card-divider" />
                  <div className="hb-employee-card-footer">
                    <span>{t("employees.departmentPage.createdAt", { date: employee.createdAt })}</span>
                    <div className="hb-employee-card-footer-actions">
                      {isInterningAi ? (
                        <button
                          type="button"
                          className="hb-btn-primary hb-hub-btn-primary text-xs"
                          onClick={() =>
                            openAiEvaluation(employee.employeeId)
                          }
                        >
                          <Bot size={14} />
                          {t("employees.departmentPage.actions.enterAiEvaluation")}
                        </button>
                      ) : isInterningHuman ? (
                        <button
                          type="button"
                          className="hb-btn-primary hb-hub-btn-primary text-xs"
                          onClick={() =>
                            openHumanEvaluation(employee.employeeId)
                          }
                        >
                          <UserCheck size={14} />
                          {t("employees.departmentPage.actions.enterHumanEvaluation")}
                        </button>
                      ) : (
                        <>
                          {canClone ? (
                            <button
                              type="button"
                              className="hb-btn-primary hb-hub-btn-primary text-xs"
                              onClick={() =>
                                setCloneTarget({
                                  employeeId: employee.employeeId,
                                  nickname: employee.nickname,
                                  roleName:
                                    employee.roleName ||
                                    employee.sourceTemplate,
                                })
                              }
                            >
                              <CopyPlus size={14} />
                              {t("employees.departmentPage.actions.createClone")}
                            </button>
                          ) : null}
                        </>
                      )}
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        )}
      </div>

      {visibleEmployees.length > 0 ? (
        <Pagination page={page} totalPages={totalPages} onChange={setPage} />
      ) : null}

      {deleteTarget ? (
        <div
          className="hb-modal-mask"
          onClick={() => (deletingId ? undefined : setDeleteTarget(null))}
        >
          <div className="hb-modal hb-delete-confirm-modal" onClick={(event) => event.stopPropagation()}>
            <button
              type="button"
              className="hb-modal-close"
              onClick={() => setDeleteTarget(null)}
              disabled={Boolean(deletingId)}
            >
              <X size={16} />
            </button>
            <div className="hb-modal-head">
              <h3 className="hb-modal-title">{t("employees.departmentPage.deleteDialogTitle")}</h3>
              <p className="hb-modal-sub">
                {t("employees.departmentPage.confirmDelete", {
                  nickname: deleteTarget.nickname,
                })}
              </p>
            </div>
            <div className="hb-modal-foot">
              <button
                type="button"
                className="hb-btn-ghost hb-hub-btn-secondary"
                onClick={() => setDeleteTarget(null)}
                disabled={Boolean(deletingId)}
              >
                {t("common.cancel")}
              </button>
              <button
                type="button"
                className="hb-btn-primary hb-hub-btn-primary hb-btn-danger"
                onClick={() =>
                  handleDelete(deleteTarget.employeeId)
                }
                disabled={Boolean(deletingId)}
              >
                {deletingId === deleteTarget.employeeId ? (
                  <Loader2 size={14} className="animate-spin" />
                ) : (
                  <Trash2 size={14} />
                )}
                {t("common.delete")}
              </button>
            </div>
          </div>
        </div>
      ) : null}

      <TemplateUploadModal
        open={uploadModalOpen}
        onClose={() => setUploadModalOpen(false)}
        onSuccess={() => {
          setUploadModalOpen(false);
          setRefreshKey((k) => k + 1);
        }}
      />

      <CloneEmployeeModal
        open={cloneTarget !== null}
        employeeId={cloneTarget?.employeeId ?? ""}
        sourceNickname={cloneTarget?.nickname ?? ""}
        sourceRoleName={cloneTarget?.roleName ?? ""}
        onClose={() => setCloneTarget(null)}
        onSuccess={() => {
          setCloneTarget(null);
          setRefreshKey((k) => k + 1);
        }}
      />

      <EmployeeDetailModal
        open={detailEmployeeId !== null}
        employeeId={detailEmployeeId ?? ""}
        onClose={() => setDetailEmployeeId(null)}
        onCloneSuccess={() => {
          setDetailEmployeeId(null);
          setRefreshKey((k) => k + 1);
        }}
      />
    </div>
  );
}
