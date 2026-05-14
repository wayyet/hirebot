import { useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  Bot,
  Check,
  Clock3,
  CopyPlus,
  Loader2,
  RotateCcw,
  ShieldCheck,
} from "lucide-react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import { useUserRole } from "@/app/context/UserRoleContext";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { api, type EmployeeDetail } from "@/infra/api";
import { instanceBasePath } from "@/shared/utils/instancePath";
import { Breadcrumb } from "@/shared/components/Breadcrumb";
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

export default function InstanceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { role } = useUserRole();
  const { showToast } = useUxOverlay();

  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [cloneModalOpen, setCloneModalOpen] = useState(false);

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
      showToast("重新雇佣已完成，沙箱已重新启动", "success");
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error ? requestError.message : "重新雇佣失败",
      );
    } finally {
      setSubmitting(false);
    }
  }

  async function abandonBranch() {
    if (!id || !employee) return;
    if (
      !window.confirm(
        "废弃后会回滚五件套并恢复为个人分身，沙箱、对话和 IM 配置都会继续沿用。此操作不可撤销，确定继续？",
      )
    )
      return;
    setSubmitting(true);
    setError("");
    try {
      const data = await api.employeeRuntime.abandonPrivateBranch(id);
      showToast("私有分支已废弃，已恢复为个人分身", "success");
      // The private branch is restored in place, so the instance id is unchanged.
      navigate(`${instanceBasePath(location.pathname, data.employeeId)}`);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "废弃私有分支失败",
      );
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

  const location = useLocation();

  const isPersonalAsset =
    employeeView?.ownership === "personal_clone" ||
    employeeView?.ownership === "private_branch";
  const canCreatePersonalClone =
    employeeView?.ownership === "department" &&
    employeeView.mappedStatus === "live";
  return (
    <div className="hb-page space-y-5">
      <Breadcrumb
        items={[
          {
            label:
              employeeView?.ownership === "department"
                ? "部门数字员工"
                : "我的数字员工",
            to: backTarget,
          },
          { label: "员工详情" },
        ]}
      />

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
                    onClick={() => setCloneModalOpen(true)}
                  >
                    <CopyPlus size={14} />
                    {role === "member" ? "创建分身" : "复制为我的分身"}
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

                {employeeView?.ownership === "private_branch" &&
                employeeView.mappedStatus !== "retired" ? (
                  <button
                    type="button"
                    className="rounded-full border border-[#fde2e2] bg-white px-4 py-2 text-sm font-medium text-[#be3a4a] hover:bg-[#fff5f5]"
                    disabled={submitting}
                    onClick={() => void abandonBranch()}
                  >
                    废弃私有分支
                  </button>
                ) : null}
              </div>
            </div>
          </section>

          <section className="hb-detail-split">
            <div className="hb-card hb-detail-panel">
              <h2 className="hb-section-heading">能力简介</h2>
              <div className="hb-cap-list">
                {employee.capabilities.map((capability) => (
                  <div
                    key={capability.name}
                    className={`hb-cap ${capability.ready ? "" : "is-muted"}`}
                  >
                    <span className="hb-cap-check">
                      {capability.ready ? <Check size={12} /> : "×"}
                    </span>
                    <span className="min-w-0 flex-1">{capability.name}</span>
                  </div>
                ))}
              </div>

              <div className="hb-divider" />
              <div className="hb-callout info">
                详情页展示当前实例已具备的业务能力。
              </div>
            </div>

            <div className="hb-card hb-detail-panel">
              <h2 className="hb-section-heading">运行状态</h2>
              {employeeView.mappedStatus === "live" ? (
                <div className="hb-stat-strip">
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
            </div>
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
