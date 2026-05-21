import { useEffect, useMemo, useRef, useState } from "react";
import {
  Bot,
  GitBranch,
  Loader2,
  MessageCircle,
  MoreHorizontal,
  ShieldCheck,
  Trash2,
  X,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useTranslation } from "react-i18next";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { api, type EmployeeSummary } from "@/infra/api";
import { withEmployeeView, extractCardIntroHeadline } from "./employeeView";
import { Pagination } from "@/shared/components/Pagination";

type FilterTab = "all" | "live" | "branch" | "retired";

type ConfirmAction =
  | { kind: "abandon"; employeeId: string }
  | { kind: "retire"; employeeId: string };

const PAGE_SIZE = 9;

export default function MyEmployeesPage() {
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { showToast } = useUxOverlay();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [employees, setEmployees] = useState<EmployeeSummary[]>([]);
  const [filter, setFilter] = useState<FilterTab>("all");
  const [page, setPage] = useState(1);
  const [abandoningId, setAbandoningId] = useState<string | null>(null);
  const [retiringId, setRetiringId] = useState<string | null>(null);
  const [confirmAction, setConfirmAction] = useState<ConfirmAction | null>(
    null,
  );
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [menuOpenId, setMenuOpenId] = useState<string | null>(null);
  const [lastInRowIds, setLastInRowIds] = useState<Set<string>>(new Set());
  const gridRef = useRef<HTMLDivElement>(null);
  const [deleteTarget, setDeleteTarget] = useState<{
    employeeId: string;
    nickname: string;
  } | null>(null);

  async function abandonBranch(branchId: string) {
    setAbandoningId(branchId);
    try {
      const restored = await api.employeeRuntime.abandonPrivateBranch(branchId);
      setEmployees((prev) =>
        prev.map((employee) =>
          employee.employeeId === branchId
            ? {
                ...employee,
                ...restored,
                instanceType: "personal_clone",
                status: "live",
              }
            : employee,
        ),
      );
      showToast(t("employees.myPage.abandonSuccess"), "success");
      setConfirmAction(null);
    } catch (requestError: unknown) {
      showToast(
        requestError instanceof Error
          ? requestError.message
          : t("employees.myPage.abandonFailed"),
        "error",
      );
    } finally {
      setAbandoningId(null);
    }
  }

  async function retireEmployee(employeeId: string) {
    setRetiringId(employeeId);
    try {
      await api.employeeRuntime.updateLifecycle(employeeId, {
        status: "retired",
        stageSummary: t("employees.myPage.retiredStageSummary"),
        primarySignal: t("employees.myPage.retiredPrimarySignal"),
        signalLevel: "warn",
      });
      setEmployees((prev) =>
        prev.map((e) =>
          e.employeeId === employeeId
            ? {
                ...e,
                status: "retired",
                lifecycleStatus: t("employees.status.retired"),
                stageSummary: t("employees.myPage.retiredStageSummary"),
                primarySignal: t("employees.myPage.retiredPrimarySignal"),
                signalLevel: "warn",
              }
            : e,
        ),
      );
      showToast(t("employees.myPage.retireSuccess"), "success");
      setConfirmAction(null);
    } catch (requestError: unknown) {
      showToast(
        requestError instanceof Error
          ? requestError.message
          : t("employees.myPage.retireFailed"),
        "error",
      );
    } finally {
      setRetiringId(null);
    }
  }

  async function handleDelete(employeeId: string) {
    setDeletingId(employeeId);
    try {
      await api.employeeRuntime.deleteEmployee(employeeId);
      setEmployees((prev) => prev.filter((e) => e.employeeId !== employeeId));
      showToast(t("employees.myPage.deleteSuccess"), "success");
      setDeleteTarget(null);
    } catch (deleteError: unknown) {
      const message =
        deleteError instanceof Error
          ? deleteError.message
          : t("employees.myPage.deleteFailed");
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
              : t("employees.myPage.loadFailed"),
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
  }, []);

  const viewedEmployees = useMemo(
    () => employees.map(withEmployeeView),
    [employees],
  );

  const myEmployees = useMemo(() => {
    return viewedEmployees
      .filter(
        (item) =>
          item.ownership === "personal_clone" ||
          item.ownership === "private_branch",
      )
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  }, [viewedEmployees]);

  const counts = useMemo(() => {
    return {
      all: myEmployees.length,
      live: myEmployees.filter((item) => item.mappedStatus === "live").length,
      branch: myEmployees.filter((item) => item.ownership === "private_branch")
        .length,
      retired: myEmployees.filter((item) => item.mappedStatus === "retired")
        .length,
    };
  }, [myEmployees]);

  const visibleEmployees = useMemo(() => {
    if (filter === "all") return myEmployees;
    if (filter === "live")
      return myEmployees.filter((item) => item.mappedStatus === "live");
    if (filter === "branch")
      return myEmployees.filter((item) => item.ownership === "private_branch");
    return myEmployees.filter((item) => item.mappedStatus === "retired");
  }, [filter, myEmployees]);

  const totalPages = Math.max(
    1,
    Math.ceil(visibleEmployees.length / PAGE_SIZE),
  );

  const pagedEmployees = useMemo(() => {
    const start = (page - 1) * PAGE_SIZE;
    return visibleEmployees.slice(start, start + PAGE_SIZE);
  }, [page, visibleEmployees]);

  useEffect(() => {
    setPage(1);
  }, [filter]);

  useEffect(() => {
    if (page > totalPages) {
      setPage(totalPages);
    }
  }, [page, totalPages]);

  useEffect(() => {
    if (!menuOpenId) return;
    function closeMenu() {
      setMenuOpenId(null);
    }
    window.addEventListener("click", closeMenu);
    return () => {
      window.removeEventListener("click", closeMenu);
    };
  }, [menuOpenId]);

  useEffect(() => {
    const grid = gridRef.current;
    if (!grid) return;

    const observer = new ResizeObserver(() => {
      const cards = grid.querySelectorAll<HTMLElement>(".hb-employee-card");
      const gridRect = grid.getBoundingClientRect();
      const lastIds = new Set<string>();

      cards.forEach((card) => {
        const rect = card.getBoundingClientRect();
        const id = card.dataset.employeeId;
        if (!id) return;
        if (gridRect.right - rect.right < 10) {
          lastIds.add(id);
        }
      });

      setLastInRowIds((prev) => {
        if (
          prev.size === lastIds.size &&
          [...prev].every((id) => lastIds.has(id))
        ) {
          return prev;
        }
        return lastIds;
      });
    });

    observer.observe(grid);
    return () => observer.disconnect();
  }, [pagedEmployees]);

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">{t("employees.myPage.kicker")}</span>
          <h1 className="hb-page-title">{t("employees.myPage.title")}</h1>
          <p className="hb-page-copy">{t("employees.myPage.copy")}</p>
        </div>
        <div className="hb-page-actions">
          <button
            type="button"
            className="hb-btn-primary hb-hub-btn-primary hb-page-head-cta"
            onClick={() => navigate("/department-employees")}
          >
            {t("employees.myPage.backToDepartment")}
          </button>
        </div>
      </div>
      {/* 
      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Users size={14} /> {t("employees.myPage.stats.totalLabel")}
          </div>
          <div className="hb-stat-value">{counts.all}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Bot size={14} /> {t("employees.myPage.stats.liveLabel")}
          </div>
          <div className="hb-stat-value">{counts.live}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <ShieldCheck size={14} /> {t("employees.myPage.stats.branchLabel")}
          </div>
          <div className="hb-stat-value">{counts.branch}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <GitBranch size={14} /> {t("employees.myPage.stats.retiredLabel")}
          </div>
          <div className="hb-stat-value">{counts.retired}</div>
        </div>
      </div> */}

      <div className="mt-5 hb-chip-row">
        {[
          {
            id: "all" as const,
            label: t("employees.myPage.filters.all"),
            count: counts.all,
          },
          {
            id: "live" as const,
            label: t("employees.myPage.filters.live"),
            count: counts.live,
          },
          {
            id: "branch" as const,
            label: t("employees.myPage.filters.branch"),
            count: counts.branch,
          },
          {
            id: "retired" as const,
            label: t("employees.myPage.filters.retired"),
            count: counts.retired,
          },
        ].map((item) => (
          <button
            key={item.id}
            type="button"
            className={`hb-chip ${filter === item.id ? "is-active" : ""}`}
            onClick={() => setFilter(item.id)}
          >
            {item.label}
            <span>{item.count}</span>
          </button>
        ))}
      </div>

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <span>{error}</span>
        </div>
      ) : null}

      <div className="mt-5">
        {loading ? (
          <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[var(--hb-soft)]">
            <Loader2 size={16} className="animate-spin" />
            {t("employees.myPage.loading")}
          </div>
        ) : visibleEmployees.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">
              {t("employees.myPage.emptyTitle")}
            </div>
            <div className="hb-empty-copy">
              {t("employees.myPage.emptyCopy")}
            </div>
          </div>
        ) : (
          <div className="hb-asset-grid" ref={gridRef}>
            {pagedEmployees.map((employee) => (
              <div
                key={employee.employeeId}
                role="button"
                tabIndex={0}
                data-employee-id={employee.employeeId}
                onClick={() =>
                  navigate(`/my-employees/instances/${employee.employeeId}`)
                }
                onKeyDown={(event) => {
                  if (event.key === "Enter" || event.key === " ") {
                    event.preventDefault();
                    navigate(`/my-employees/instances/${employee.employeeId}`);
                  }
                }}
                className="hb-card hb-employee-card cursor-pointer text-left"
                style={
                  menuOpenId === employee.employeeId
                    ? { zIndex: 10 }
                    : undefined
                }
              >
                <div className="hb-employee-card-menu-anchor" onClick={(event) => event.stopPropagation()}>
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
                  >
                    <MoreHorizontal size={16} />
                  </button>
                  {menuOpenId === employee.employeeId ? (
                    <div className={`hb-dropdown-menu hb-employee-card-menu${lastInRowIds.has(employee.employeeId) ? " hb-dropdown-menu--right" : ""}`}>
                      {employee.ownership === "personal_clone" &&
                      employee.mappedStatus === "live" ? (
                        <button
                          type="button"
                          className="hb-dropdown-item"
                          onClick={(event) => {
                            event.stopPropagation();
                            setMenuOpenId(null);
                            navigate(`/private-branch/${employee.employeeId}`);
                          }}
                        >
                          <GitBranch size={14} />
                          {t("employees.myPage.actions.createPrivateBranch")}
                        </button>
                      ) : null}
                      {employee.mappedStatus !== "retired" ? (
                        <button
                          type="button"
                          className="hb-dropdown-item hb-dropdown-item--danger"
                          disabled={retiringId === employee.employeeId}
                          onClick={(event) => {
                            event.stopPropagation();
                            setMenuOpenId(null);
                            setConfirmAction({
                              kind: "retire",
                              employeeId: employee.employeeId,
                            });
                          }}
                        >
                          <ShieldCheck size={14} />
                          {retiringId === employee.employeeId
                            ? t("employees.myPage.actions.retiring")
                            : t("employees.myPage.actions.retire")}
                        </button>
                      ) : null}
                      {employee.mappedStatus === "retired" ? (
                        <button
                          type="button"
                          className="hb-dropdown-item hb-dropdown-item--danger"
                          disabled={deletingId === employee.employeeId}
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
                          {deletingId === employee.employeeId
                            ? t("employees.myPage.actions.deleting")
                            : t("employees.myPage.actions.delete")}
                        </button>
                      ) : null}
                    </div>
                  ) : null}
                </div>
                <div className="hb-employee-card-head pr-12">
                  <div className="min-w-0 flex-1">
                    <h3 className="hb-employee-card-title">
                      {employee.nickname}
                    </h3>
                    <p className="hb-employee-card-subtitle mt-1">
                      {employee.roleName || employee.sourceTemplate}
                    </p>
                  </div>
                </div>
                <p className="hb-employee-card-desc">
                  {extractCardIntroHeadline(employee.cardIntro) || employee.primarySignal || employee.stageSummary}
                </p>
                <div className="hb-employee-card-divider" />
                <div className="hb-employee-card-footer">
                  <span>
                    {t("employees.myPage.updatedAt", {
                      date: employee.createdAt,
                    })}
                  </span>
                  <div className="hb-employee-card-footer-actions">
                    {employee.mappedStatus === "live" ? (
                      <>
                        <button
                          type="button"
                          className="hb-btn-outline-brand"
                          onClick={(e) => {
                            e.stopPropagation();
                            navigate(
                              `/my-employees/instances/${employee.employeeId}/im-config`,
                            );
                          }}
                        >
                          <Bot size={12} />
                          {t("employees.myPage.actions.configureIm")}
                        </button>
                        <button
                          type="button"
                          className="hb-btn-primary hb-hub-btn-primary text-xs"
                          onClick={(e) => {
                            e.stopPropagation();
                            navigate(
                              `/my-employees/instances/${employee.employeeId}/chat`,
                            );
                          }}
                        >
                          <MessageCircle size={12} />
                          {t("employees.myPage.actions.startChat")}
                        </button>
                      </>
                    ) : null}
                    {employee.ownership === "private_branch" &&
                    employee.mappedStatus !== "retired" ? (
                      <button
                        type="button"
                        className="hb-employee-card-inline-action hb-employee-card-inline-action--danger"
                        onClick={(e) => {
                          e.stopPropagation();
                          setConfirmAction({
                            kind: "abandon",
                            employeeId: employee.employeeId,
                          });
                        }}
                      >
                        {abandoningId === employee.employeeId
                          ? t("employees.myPage.actions.abandoning")
                          : t("employees.myPage.actions.abandon")}
                      </button>
                    ) : null}
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {visibleEmployees.length > 0 ? (
        <Pagination page={page} totalPages={totalPages} onChange={setPage} />
      ) : null}

      {confirmAction ? (
        <div
          className="hb-modal-mask"
          onClick={() =>
            abandoningId || retiringId ? undefined : setConfirmAction(null)
          }
        >
          <div
            className="hb-modal hb-delete-confirm-modal"
            onClick={(event) => event.stopPropagation()}
          >
            <button
              type="button"
              className="hb-modal-close"
              onClick={() => setConfirmAction(null)}
              disabled={Boolean(abandoningId || retiringId)}
            >
              <X size={16} />
            </button>
            <div className="hb-modal-head">
              <h3 className="hb-modal-title">
                {confirmAction.kind === "retire"
                  ? t("employees.myPage.retireDialogTitle")
                  : t("employees.myPage.abandonDialogTitle")}
              </h3>
              <p className="hb-modal-sub">
                {confirmAction.kind === "retire"
                  ? t("employees.myPage.confirmRetire")
                  : t("employees.myPage.confirmAbandon")}
              </p>
            </div>
            <div className="hb-modal-foot">
              <button
                type="button"
                className="hb-btn-ghost hb-hub-btn-secondary"
                onClick={() => setConfirmAction(null)}
                disabled={Boolean(abandoningId || retiringId)}
              >
                {t("common.cancel")}
              </button>
              <button
                type="button"
                className="hb-btn-primary hb-hub-btn-primary hb-btn-danger"
                disabled={Boolean(abandoningId || retiringId)}
                onClick={() => {
                  if (confirmAction.kind === "retire") {
                    void retireEmployee(confirmAction.employeeId);
                    return;
                  }
                  void abandonBranch(confirmAction.employeeId);
                }}
              >
                {(confirmAction.kind === "retire" &&
                  retiringId === confirmAction.employeeId) ||
                (confirmAction.kind === "abandon" &&
                  abandoningId === confirmAction.employeeId) ? (
                  <Loader2 size={14} className="animate-spin" />
                ) : null}
                {confirmAction.kind === "retire"
                  ? t("employees.myPage.actions.retire")
                  : t("employees.myPage.actions.abandon")}
              </button>
            </div>
          </div>
        </div>
      ) : null}

      {deleteTarget ? (
        <div
          className="hb-modal-mask"
          onClick={() => (deletingId ? undefined : setDeleteTarget(null))}
        >
          <div
            className="hb-modal hb-delete-confirm-modal"
            onClick={(event) => event.stopPropagation()}
          >
            <button
              type="button"
              className="hb-modal-close"
              onClick={() => setDeleteTarget(null)}
              disabled={Boolean(deletingId)}
            >
              <X size={16} />
            </button>
            <div className="hb-modal-head">
              <h3 className="hb-modal-title">
                {t("employees.myPage.deleteDialogTitle")}
              </h3>
              <p className="hb-modal-sub">
                {t("employees.myPage.confirmDelete", {
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
                onClick={() => handleDelete(deleteTarget.employeeId)}
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
    </div>
  );
}
