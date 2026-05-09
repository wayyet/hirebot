import { useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  ArrowLeft,
  Bot,
  Check,
  Clock3,
  CopyPlus,
  GitBranch,
  Loader2,
  MessageCircle,
  RotateCcw,
  ShieldCheck,
  Users,
} from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { useUserRole } from "@/app/context/UserRoleContext";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { api, type EmployeeDetail } from "@/infra/api";
import {
  firstCharacter,
  ownershipClass,
  ownershipLabel,
  statusClass,
  statusLabel,
  toEmployeeDetailSummary,
  withEmployeeView,
} from "@/features/hiring/pages/employeeView";

type StatusAction = {
  label: string;
  status:
    | "hired"
    | "interning_ai"
    | "interning_human"
    | "live"
    | "failed"
    | "retired";
  stageSummary: string;
  primarySignal: string;
  signalLevel: "ok" | "warn" | "error";
};

const STATUS_ACTIONS: StatusAction[] = [
  {
    label: "重置为已雇佣",
    status: "hired",
    stageSummary: "实例已雇佣，等待发起评估",
    primarySignal: "待操作：进入 AI 评估",
    signalLevel: "warn",
  },
  {
    label: "进入 AI 评估",
    status: "interning_ai",
    stageSummary: "已进入 AI 评估阶段",
    primarySignal: "等待 AI 评估执行",
    signalLevel: "warn",
  },
  {
    label: "进入人工评估",
    status: "interning_human",
    stageSummary: "AI 评估通过，等待人工评估",
    primarySignal: "待人工审核",
    signalLevel: "warn",
  },
  {
    label: "标记为已上岗",
    status: "live",
    stageSummary: "已上岗，运行中",
    primarySignal: "运行稳定",
    signalLevel: "ok",
  },
  {
    label: "标记为失败",
    status: "failed",
    stageSummary: "评估未通过，等待 Review 回退",
    primarySignal: "待回退处理",
    signalLevel: "error",
  },
  {
    label: "标记为已退役",
    status: "retired",
    stageSummary: "实例已退役",
    primarySignal: "仅保留历史信息",
    signalLevel: "warn",
  },
];

const IM_CHANNELS = [
  { id: "feishu", label: "飞书", status: "可配置" },
  { id: "dingtalk", label: "钉钉", status: "待接入" },
  { id: "wecom", label: "企微", status: "待接入" },
];

export default function InstanceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { role } = useUserRole();
  const { showToast } = useUxOverlay();

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  async function loadEmployee() {
    if (!id) return;
    setLoading(true);
    setError("");
    try {
      const data = await api.employeeRuntime.getEmployee(id);
      setEmployee(data);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "加载实例失败",
      );
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    void loadEmployee();
  }, [id]);

  async function toggleCapability(name: string, ready: boolean) {
    if (!employee || !id) return;
    setSubmitting(true);
    setError("");
    try {
      const data = await api.employeeRuntime.updateCapabilities(id, {
        capabilities: employee.capabilities.map((cap) =>
          cap.name === name ? { ...cap, ready } : cap,
        ),
      });
      setEmployee(data);
      showToast(`能力「${name}」已${ready ? "启用" : "停用"}`, "success");
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "更新能力失败",
      );
    } finally {
      setSubmitting(false);
    }
  }

  async function setLifecycle(action: StatusAction) {
    if (!id) return;
    setSubmitting(true);
    setError("");
    try {
      const data = await api.employeeRuntime.updateLifecycle(id, {
        status: action.status,
        stageSummary: action.stageSummary,
        primarySignal: action.primarySignal,
        signalLevel: action.signalLevel,
      });
      setEmployee(data);
      showToast(`状态已更新为 ${statusLabel(action.status)}`, "success");
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "状态更新失败",
      );
    } finally {
      setSubmitting(false);
    }
  }

  async function rehireEmployee() {
    if (!id || !employeeView || !isPersonalAsset || employeeView.mappedStatus !== "retired") {
      return;
    }

    setSubmitting(true);
    setError("");
    try {
      const data = await api.employeeRuntime.rehire(id);
      setEmployee(data);
      showToast("重新雇佣已完成，沙箱已重新启动", "success");
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "重新雇佣失败",
      );
    } finally {
      setSubmitting(false);
    }
  }

  const readyCount = useMemo(() => {
    if (!employee) return 0;
    return employee.capabilities.filter((cap) => cap.ready).length;
  }, [employee]);

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
    employeeView.mappedStatus === "live";
  const canCreatePrivateBranch =
    employeeView?.ownership === "personal_clone" &&
    employeeView.mappedStatus === "live";

  function openHiringConversation() {
    if (!id) {
      return;
    }

    navigate(`/instances/${id}/chat`);
  }

  return (
    <div className="hb-page space-y-5">
      <button
        type="button"
        onClick={() => navigate(backTarget)}
        className="hb-detail-crumb"
      >
        <ArrowLeft size={14} />
        返回
        {employeeView?.ownership === "department"
          ? "部门数字员工"
          : "我的数字员工"}
      </button>

      {error && (
        <div className="hb-alert hb-alert-error">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      )}

      {loading ? (
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载实例详情...
        </div>
      ) : !employee || !employeeView ? (
        <div className="hb-card p-8 text-sm text-[#737373]">实例不存在</div>
      ) : (
        <div className="space-y-5">
          <section className="hb-card hb-detail-hero">
            <div className="hb-detail-top">
              <span className="hb-detail-avatar">
                {firstCharacter(employee.nickname)}
              </span>

              <div className="hb-detail-main">
                <div className="hb-detail-title-row">
                  <h1>{employee.nickname}</h1>
                  <span
                    className={`hb-pill ${statusClass(employeeView.mappedStatus, employee.lifecycleStatus)}`}
                  >
                    {statusLabel(
                      employeeView.mappedStatus,
                      employee.lifecycleStatus,
                    )}
                  </span>
                  <span
                    className={`hb-pill ${ownershipClass(employeeView.ownership)}`}
                  >
                    {ownershipLabel(employeeView.ownership)}
                  </span>
                </div>
                <div className="hb-detail-meta">
                  所属部门 {employee.departmentId || employee.owningTeam} ·
                  Owner {employee.ownerUserId} · 创建于 {employee.createdAt}
                </div>
                <p className="hb-detail-desc">
                  {employee.primarySignal || employee.stageSummary}
                </p>

                <div className="hb-divider" />
                <h3 className="hb-section-heading muted-heading">来源关系</h3>
                <div className="hb-lineage">
                  <span>{employee.sourceTemplate || "模板"}</span>
                  <span>→</span>
                  <span>
                    {employee.fromInstanceId
                      ? `源实例 ${employee.fromInstanceId}`
                      : "部门员工"}
                  </span>
                  <span>→</span>
                  <span>{employee.employeeId}</span>
                </div>
              </div>

              <div className="hb-detail-actions">
                {canCreatePersonalClone ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => navigate(`/clone/${employee.employeeId}`)}
                  >
                    <CopyPlus size={14} />
                    {role === "member" ? "创建分身" : "复制为我的分身"}
                  </button>
                ) : null}

                {isPersonalAsset && employeeView.mappedStatus === "live" ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={openHiringConversation}
                  >
                    <MessageCircle size={14} />
                    开始对话
                  </button>
                ) : null}

                {isPersonalAsset ? (
                  <button
                    type="button"
                    className="hb-btn-ghost"
                    onClick={() =>
                      navigate(
                        employeeView.mappedStatus === "retired"
                          ? `/instances/${employee.employeeId}/evaluation`
                          : `/instances/${employee.employeeId}/im-config`,
                      )
                    }
                  >
                    <Bot size={14} />
                    {employeeView.mappedStatus === "retired"
                      ? "查看评估报告"
                      : "配置 IM"}
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
                    {submitting ? "重新雇佣中" : "重新雇佣"}
                  </button>
                ) : null}

                {canCreatePrivateBranch ? (
                  <button
                    type="button"
                    className="hb-btn-ghost"
                    onClick={() =>
                      navigate(`/private-branch/${employee.employeeId}`)
                    }
                  >
                    <GitBranch size={14} />
                    创建私有分支
                  </button>
                ) : null}

                <button
                  type="button"
                  className="hb-btn-ghost"
                  onClick={() => {
                    const retireAction = STATUS_ACTIONS.find(
                      (a) => a.status === "retired",
                    );
                    if (retireAction) {
                      void setLifecycle(retireAction);
                    }
                  }}
                  disabled={submitting || employeeView.mappedStatus === "retired"}
                >
                  <ShieldCheck size={14} />
                  退役
                </button>
              </div>
            </div>
          </section>

          <section className="hb-detail-split">
            <div className="hb-card hb-detail-panel">
              <h2 className="hb-section-heading">能力简介</h2>
              <div className="hb-cap-list">
                {employee.capabilities.map((capability) => (
                  <label
                    key={capability.name}
                    className={`hb-cap ${capability.ready ? "" : "is-muted"}`}
                  >
                    <span className="hb-cap-check">
                      {capability.ready ? <Check size={12} /> : "×"}
                    </span>
                    <span className="min-w-0 flex-1">{capability.name}</span>
                    <input
                      type="checkbox"
                      checked={capability.ready}
                      disabled={submitting}
                      onChange={(event) =>
                        void toggleCapability(
                          capability.name,
                          event.target.checked,
                        )
                      }
                    />
                  </label>
                ))}
              </div>

              <div className="hb-divider" />
              <div className="hb-callout info">
                详情页只展示业务可读的能力和边界，能力开关会直接写回当前实例配置。
              </div>
            </div>

            <div className="hb-card hb-detail-panel">
              <h2 className="hb-section-heading">运行状态</h2>
              {employeeView.mappedStatus === "live" ? (
                <div className="hb-stat-strip">
                  <div className="hb-stat-item">
                    <MessageCircle size={16} />
                    <strong>{employee.tasksDone || 0}</strong>
                    <span>累计完成</span>
                  </div>
                  <div className="hb-stat-item">
                    <Users size={16} />
                    <strong>{employee.tasksTotal || 0}</strong>
                    <span>任务总数</span>
                  </div>
                  <div className="hb-stat-item">
                    <Clock3 size={16} />
                    <strong>{employee.graduatedAt || "—"}</strong>
                    <span>上岗时间</span>
                  </div>
                  <div className="hb-stat-item">
                    <ShieldCheck size={16} />
                    <strong>{readyCount}</strong>
                    <span>可用能力</span>
                  </div>
                  <div className="hb-stat-item">
                    <Bot size={16} />
                    <strong>{employee.isConfigured ? "v1.0" : "待配置"}</strong>
                    <span>实例版本</span>
                  </div>
                </div>
              ) : (
                <div className="hb-callout info">
                  该实例尚未上岗，当前以元数据、流程状态和回退入口为主。
                </div>
              )}

              <div className="mt-4 hb-callout success">
                <ShieldCheck size={18} />
                <div>
                  <div className="font-semibold text-[#0a0a0a]">
                    状态承接说明
                  </div>
                  <div className="mt-1">
                    {employeeView.mappedStatus === "live"
                      ? isPersonalAsset
                        ? "已上岗的个人资产可以站内对话，并按需配置飞书、钉钉或企微。"
                        : "已上岗的部门员工可以作为复制源，成员复制后拥有独立会话。"
                      : employeeView.mappedStatus === "retired"
                        ? isPersonalAsset
                          ? "已退役的个人资产可以重新雇佣，系统会重新启动沙箱并恢复站内对话。"
                          : "该实例已退役，当前仅保留历史信息。"
                        : employeeView.mappedStatus === "interning_ai"
                          ? "AI 评估通过后才允许进入人工评估。"
                          : employeeView.mappedStatus === "interning_human"
                            ? "人工评估通过后才允许标记为已上岗。"
                            : "你可以通过评估、回退和上岗配置逐步调整该实例。"}
                  </div>
                </div>
              </div>

              <div className="mt-4">
                <h3 className="mb-2 text-sm font-semibold text-[#0a0a0a]">
                  IM 接入状态
                </h3>
                <div className="grid gap-2 md:grid-cols-3">
                  {IM_CHANNELS.map((channel) => (
                    <button
                      key={channel.id}
                      type="button"
                      className="rounded-xl border border-[#ececec] bg-[#fafafa] px-3 py-2 text-left text-sm hover:bg-white"
                      onClick={() =>
                        navigate(`/instances/${employee.employeeId}/im-config`)
                      }
                    >
                      <div className="font-medium text-[#0a0a0a]">
                        {channel.label}
                      </div>
                      <div className="mt-0.5 text-xs text-[#737373]">
                        {channel.status}
                      </div>
                    </button>
                  ))}
                </div>
              </div>
            </div>
          </section>
        </div>
      )}
    </div>
  );
}
