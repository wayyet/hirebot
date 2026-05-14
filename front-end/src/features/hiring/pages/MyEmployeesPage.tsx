import { useEffect, useMemo, useState } from "react";
import {
  Bot,
  GitBranch,
  Loader2,
  MessageCircle,
  ShieldCheck,
  Users,
} from "lucide-react";
import { useNavigate } from "react-router-dom";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { api, type EmployeeSummary } from "@/infra/api";
import {
  withEmployeeView,
} from "./employeeView";
import { Pagination } from "@/shared/components/Pagination";

type FilterTab = "all" | "live" | "branch" | "retired";

const PAGE_SIZE = 9;

export default function MyEmployeesPage() {
  const navigate = useNavigate();
  const { showToast } = useUxOverlay();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [employees, setEmployees] = useState<EmployeeSummary[]>([]);
  const [filter, setFilter] = useState<FilterTab>("all");
  const [page, setPage] = useState(1);
  const [abandoningId, setAbandoningId] = useState<string | null>(null);
  const [retiringId, setRetiringId] = useState<string | null>(null);

  async function abandonBranch(branchId: string, event: React.MouseEvent) {
    event.stopPropagation();
    if (
      !window.confirm(
        "废弃后会回滚五件套并恢复为个人分身，沙箱、对话和 IM 配置都会继续沿用。此操作不可撤销，确定继续？",
      )
    )
      return;
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
      showToast("私有分身已废弃，已恢复为个人分身", "success");
    } catch {
      // Silently handle — user can retry from detail page
    } finally {
      setAbandoningId(null);
    }
  }

  async function retireEmployee(employeeId: string, event: React.MouseEvent) {
    event.stopPropagation();
    if (!window.confirm("确定要将此实例退役吗？退役后仅保留历史信息。")) return;
    setRetiringId(employeeId);
    try {
      await api.employeeRuntime.updateLifecycle(employeeId, {
        status: "retired",
        stageSummary: "实例已退役",
        primarySignal: "仅保留历史信息",
        signalLevel: "warn",
      });
      setEmployees((prev) =>
        prev.map((e) =>
          e.employeeId === employeeId
            ? {
                ...e,
                status: "retired",
                lifecycleStatus: "已退役",
                stageSummary: "实例已退役",
                primarySignal: "仅保留历史信息",
                signalLevel: "warn",
              }
            : e,
        ),
      );
      showToast("实例已退役", "success");
    } catch {
      // Silently handle — user can retry
    } finally {
      setRetiringId(null);
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
              : "我的数字员工加载失败",
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

  const totalPages = Math.max(1, Math.ceil(visibleEmployees.length / PAGE_SIZE));

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

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">个人资产面板</span>
          <h1 className="hb-page-title">我的数字员工</h1>
          <p className="hb-page-copy">
            这里仅展示你本人拥有的个人资产实例。已上岗后可继续进入详情、站内对话、IM 配置和私有化扩展。
          </p>
        </div>
        <div className="hb-page-actions">
          <button
            type="button"
            className="hb-btn-ghost hb-hub-btn-secondary"
            onClick={() => navigate("/department-employees")}
          >
            去部门数字员工复制一个 →
          </button>
        </div>
      </div>

      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Users size={14} /> 实例总数
          </div>
          <div className="hb-stat-value">{counts.all}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <Bot size={14} /> 已上岗
          </div>
          <div className="hb-stat-value">{counts.live}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <ShieldCheck size={14} /> 私有分身
          </div>
          <div className="hb-stat-value">{counts.branch}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">
            <GitBranch size={14} /> 已退役
          </div>
          <div className="hb-stat-value">{counts.retired}</div>
        </div>
      </div>

      <div className="mt-5 hb-chip-row">
        {[
          { id: "all" as const, label: "全部", count: counts.all },
          { id: "live" as const, label: "已上岗", count: counts.live },
          { id: "branch" as const, label: "私有分身", count: counts.branch },
          { id: "retired" as const, label: "已退役", count: counts.retired },
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
            正在加载我的数字员工...
          </div>
        ) : visibleEmployees.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">当前筛选下还没有你的个人资产</div>
            <div className="hb-empty-copy">
              先去「部门数字员工」复制一个已上岗员工给自己，再回来这里继续对话、评估或定制。
            </div>
          </div>
        ) : (
          <div className="hb-asset-grid">
            {pagedEmployees.map((employee) => (
              <div
                key={employee.employeeId}
                role="button"
                tabIndex={0}
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
              >
                <div className="hb-employee-card-head">
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
                  {employee.primarySignal || employee.stageSummary}
                </p>
                <div
                  className="hb-employee-card-actions"
                  onClick={(e) => e.stopPropagation()}
                >
                  {employee.mappedStatus === "live" ? (
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
                      开始对话
                    </button>
                  ) : null}
                  <button
                    type="button"
                    className="hb-btn-ghost hb-hub-btn-secondary text-xs"
                    onClick={(e) => {
                      e.stopPropagation();
                      navigate(
                        employee.mappedStatus === "retired"
                          ? `/my-employees/instances/${employee.employeeId}/evaluation`
                          : `/my-employees/instances/${employee.employeeId}/im-config`,
                      );
                    }}
                  >
                    <Bot size={12} />
                    {employee.mappedStatus === "retired"
                      ? "查看评估报告"
                      : "配置 IM"}
                  </button>
                  {employee.ownership === "personal_clone" &&
                  employee.mappedStatus === "live" ? (
                    <button
                      type="button"
                      className="hb-btn-ghost hb-hub-btn-secondary text-xs"
                      onClick={(e) => {
                        e.stopPropagation();
                        navigate(`/private-branch/${employee.employeeId}`);
                      }}
                    >
                      <GitBranch size={12} />
                      创建私有分身
                    </button>
                  ) : null}
                  <button
                    type="button"
                    className="hb-btn-ghost hb-hub-btn-secondary text-xs"
                    disabled={
                      employee.mappedStatus === "retired" ||
                      retiringId === employee.employeeId
                    }
                    onClick={(e) => {
                      void retireEmployee(employee.employeeId, e);
                    }}
                  >
                    <ShieldCheck size={12} />
                    {retiringId === employee.employeeId ? "退役中..." : "退役"}
                  </button>
                </div>
                <div className="hb-employee-card-divider" />
                <div className="hb-employee-card-footer">
                  <span>最近更新 {employee.createdAt}</span>
                  <div className="flex items-center gap-2">
                    {employee.ownership === "private_branch" &&
                    employee.mappedStatus !== "retired" ? (
                      <span
                        className="cursor-pointer text-red-600 hover:underline dark:text-red-400"
                        onClick={(e) => {
                          void abandonBranch(employee.employeeId, e);
                        }}
                      >
                        {abandoningId === employee.employeeId
                          ? "废弃中..."
                          : "废弃"}
                      </span>
                    ) : null}
                    <span className="hb-employee-card-link">查看详情 →</span>
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
    </div>
  );
}
