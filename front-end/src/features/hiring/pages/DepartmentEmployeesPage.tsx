import { useEffect, useMemo, useState } from "react";
import {
  ArrowRight,
  BarChart2,
  Bot,
  CheckCircle2,
  CopyPlus,
  Loader2,
  Search,
  Sparkles,
  Trash2,
  UserCheck,
  Users,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useUserRole } from "@/app/context/UserRoleContext";
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
  const [page, setPage] = useState(1);
  const [cloneTarget, setCloneTarget] = useState<{
    employeeId: string;
    nickname: string;
    roleName: string;
  } | null>(null);
  const [detailEmployeeId, setDetailEmployeeId] = useState<string | null>(null);

  async function handleDelete(employeeId: string, nickname: string) {
    if (
      !window.confirm(
        `确认删除数字员工「${nickname}」？此操作不可撤销，将同时清理五件套文件。`,
      )
    ) {
      return;
    }

    setDeletingId(employeeId);
    try {
      await api.employeeRuntime.deleteEmployee(employeeId);
      setRefreshKey((k) => k + 1);
    } catch (deleteError: unknown) {
      const message =
        deleteError instanceof Error ? deleteError.message : "删除失败";
      window.alert(message);
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
              : "部门数字员工加载失败",
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
            {role === "manager" ? "团队资产总览" : "部门可复制员工"}
          </span>
          <h1 className="hb-page-title">
            部门数字员工 · <span className="accent">研发部</span>
          </h1>
          <p className="hb-page-copy">
            {role === "manager"
              ? "部门长视角下统一管理已雇佣、评估中、已上岗三个阶段。所有卡片先进入详情，再从详情页分发下一步动作。"
              : "普通成员只看到本部门已上岗结果集。进入详情后可以继续复制为自己的分身。"}
          </p>
        </div>
        {role === "manager" ? (
          <div className="hb-page-actions">
            <button
              type="button"
              className="hb-btn-primary hb-hub-btn-primary"
              onClick={() => setUploadModalOpen(true)}
            >
              上传模版
            </button>
            <button
              type="button"
              className="hb-btn-primary hb-hub-btn-primary"
              onClick={() => navigate("/template-pool")}
            >
              从模板池雇佣
            </button>
          </div>
        ) : null}
      </div>

      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Users size={14} /> 部门员工
          </div>
          <div className="hb-stat-value">{viewedEmployees.length}</div>
          <div className="hb-stat-note">可按状态筛选</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <CheckCircle2 size={14} /> 已上岗
          </div>
          <div className="hb-stat-value">{counts.live}</div>
          <div className="hb-stat-note">可直接复制</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Sparkles size={14} /> 评估中
          </div>
          <div className="hb-stat-value">{counts.intern}</div>
          <div className="hb-stat-note">AI / 人工评估</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <BarChart2 size={14} /> 雇佣中
          </div>
          <div className="hb-stat-value">{counts.hired}</div>
          <div className="hb-stat-note">等待进入评估</div>
        </div>
      </div>

      <div className="hb-search-shell mt-5">
        <Search size={16} />
        <input
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          className="hb-search-input"
          placeholder="搜索员工名称、能力标签、所属场景"
        />
        <div className="hb-search-controls">
          <button
            type="button"
            className="hb-btn-ghost hb-hub-btn-secondary"
            onClick={() => setQuery("")}
          >
            清空筛选
          </button>
        </div>
      </div>

      <div className="mt-5">
        <div className="hb-tab-row">
          {(role === "manager"
            ? [
                { id: "hired" as const, label: "已雇佣", count: counts.hired },
                {
                  id: "intern" as const,
                  label: "待实习",
                  count: counts.intern,
                },
                { id: "live" as const, label: "已上岗", count: counts.live },
              ]
            : [{ id: "live" as const, label: "已上岗", count: counts.live }]
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
            AI 评估
            <span>{counts.ai}</span>
          </button>
          <button
            type="button"
            className={`hb-chip ${internSubTab === "human" ? "is-active" : ""}`}
            onClick={() => setInternSubTab("human")}
          >
            人工评估
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
            正在加载部门数字员工...
          </div>
        ) : visibleEmployees.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">当前没有符合筛选条件的数字员工</div>
            <div className="hb-empty-copy">
              {role === "manager"
                ? "去模板池开始一轮新雇佣，或切换到其他状态查看不同阶段的员工。"
                : "等部门长完成上岗后，这里就会出现可以直接使用和复制的员工。"}
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
                  <button
                    type="button"
                    onClick={() => openCard(employee)}
                    className="block w-full flex-1 text-left"
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
                  <div>
                    <div className="hb-employee-card-footer">
                      <span>创建于 {employee.createdAt}</span>
                      {canClone ? (
                        <span className="text-emerald-600 dark:text-emerald-400">
                          可复制
                        </span>
                      ) : null}
                      {isInterningAi ? (
                        <span className="text-violet-600 dark:text-violet-400">
                          AI 评估中
                        </span>
                      ) : null}
                      {isInterningHuman ? (
                        <span className="text-indigo-600 dark:text-indigo-400">
                          人工评估中
                        </span>
                      ) : null}
                    </div>
                    <div className="hb-employee-card-actions">
                      {isInterningAi ? (
                        <>
                          <button
                            type="button"
                            className="hb-btn-primary hb-hub-btn-primary"
                            onClick={() =>
                              openAiEvaluation(employee.employeeId)
                            }
                          >
                            <Bot size={14} />
                            进入 AI 评估
                          </button>
                          <button
                            type="button"
                            className="hb-btn-ghost hb-hub-btn-secondary"
                            onClick={() => setDetailEmployeeId(employee.employeeId)}
                          >
                            查看详情
                            <ArrowRight size={14} />
                          </button>
                        </>
                      ) : isInterningHuman ? (
                        <>
                          <button
                            type="button"
                            className="hb-btn-primary hb-hub-btn-primary"
                            onClick={() =>
                              openHumanEvaluation(employee.employeeId)
                            }
                          >
                            <UserCheck size={14} />
                            进入人工评估
                          </button>
                          <button
                            type="button"
                            className="hb-btn-ghost hb-hub-btn-secondary"
                            onClick={() => setDetailEmployeeId(employee.employeeId)}
                          >
                            查看详情
                            <ArrowRight size={14} />
                          </button>
                        </>
                      ) : (
                        <>
                          {canClone ? (
                            <button
                              type="button"
                              className="hb-btn-primary hb-hub-btn-primary"
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
                              创建分身
                            </button>
                          ) : null}
                          <button
                            type="button"
                            className={
                              canClone
                                ? "hb-btn-ghost hb-hub-btn-secondary"
                                : "hb-btn-primary hb-hub-btn-primary"
                            }
                            onClick={() => setDetailEmployeeId(employee.employeeId)}
                          >
                            查看详情
                            <ArrowRight size={14} />
                          </button>
                        </>
                      )}
                      <button
                        type="button"
                        className="hb-btn-ghost hb-hub-btn-secondary ml-auto text-[var(--hb-soft)] hover:text-red-500"
                        onClick={(e) => {
                          e.stopPropagation();
                          handleDelete(employee.employeeId, employee.nickname);
                        }}
                        disabled={deletingId === employee.employeeId}
                        title="删除员工"
                      >
                        {deletingId === employee.employeeId ? (
                          <Loader2 size={14} className="animate-spin" />
                        ) : (
                          <Trash2 size={14} />
                        )}
                      </button>
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
