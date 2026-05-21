import { useEffect, useMemo, useState } from "react";
import {
  AlertCircle,
  Check,
  CopyPlus,
  Loader2,
  MessageCircle,
  RotateCcw,
  Settings,
  ShieldCheck,
} from "lucide-react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { useTranslation } from "react-i18next";
import { useUserRole } from "@/app/context/UserRoleContext";
import { useUxOverlay } from "@/app/context/UxOverlayContext";
import { api, type EmployeeDetail } from "@/infra/api";
import { instanceBasePath } from "@/shared/utils/instancePath";
import { Breadcrumb } from "@/shared/components/Breadcrumb";
import CloneEmployeeModal from "@/features/hiring/pages/components/CloneEmployeeModal";
import {
  ownershipClass,
  ownershipLabel,
  statusClass,
  statusLabel,
  toEmployeeDetailSummary,
  withEmployeeView,
} from "@/features/hiring/pages/employeeView";

function relativeTime(dateStr: string): string {
  if (!dateStr) return "";
  const date = new Date(dateStr);
  if (isNaN(date.getTime())) return dateStr;
  const now = Date.now();
  const diff = now - date.getTime();
  const minutes = Math.floor(diff / 60000);
  if (minutes < 1) return "刚刚";
  if (minutes < 60) return `${minutes} 分钟前`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} 小时前`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days} 天前`;
  return dateStr;
}

export default function InstanceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { t } = useTranslation();
  const { role } = useUserRole();
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

  async function loadEmployee() {
    if (!id) return;
    setLoading(true);
    setError("");
    try {
      const data = await api.employeeRuntime.getEmployee(id);
      setEmployee(data);
      if (data.sourceTemplateId) {
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
      }
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
              <div
                className="hb-detail-avatar"
                style={{
                  background: "linear-gradient(135deg, #667eea 0%, #764ba2 100%)",
                }}
              >
                {employee.nickname.slice(0, 2)}
              </div>
              <div className="hb-detail-main">
                <div className="hb-detail-title-row">
                  <div>
                    <h1>{employee.nickname}</h1>
                    {(employee.roleName || employee.sourceTemplate) ? (
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
                <div className="hb-detail-meta">
                  <span>{employee.owningTeam || employee.departmentId || "-"}</span>
                  <span className="hb-detail-meta-sep">·</span>
                  <span>Owner {employee.ownerUserId || "-"}</span>
                  <span className="hb-detail-meta-sep">·</span>
                  <span>最近更新 {relativeTime(employee.createdAt)}</span>
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
                    onClick={() => navigate(`/my-employees/instances/${employee.employeeId}/im-config`)}
                  >
                    <Settings size={12} />
                    {t("instanceDetail.actions.configureIm")}
                  </button>
                ) : null}

                {isPersonalAsset && employeeView.mappedStatus === "live" ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => navigate(`/my-employees/instances/${employee.employeeId}/chat`)}
                  >
                    <MessageCircle size={14} />
                    开始对话
                  </button>
                ) : null}

                {canCreatePersonalClone ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => setCloneModalOpen(true)}
                  >
                    <CopyPlus size={14} />
                    创建我的分身
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

                {employeeView.mappedStatus === "interning_ai" && id ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => navigate(`${instanceBasePath(location.pathname, id)}/evaluation`)}
                  >
                    <ShieldCheck size={14} />
                    进入 AI 评估
                  </button>
                ) : null}

                {employeeView.mappedStatus === "interning_human" && id ? (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => navigate(`${instanceBasePath(location.pathname, id)}/human-evaluation`)}
                  >
                    <ShieldCheck size={14} />
                    进入人工评估
                  </button>
                ) : null}
              </div>
            </div>
          </section>

          <section className="hb-card" style={{ padding: 24 }}>
            <h2 className="hb-section-heading">员工介绍</h2>
            {coreAbilities.length > 0 || inScope.length > 0 || outOfScope.length > 0 ? (
              <div className="hb-cap-list">
                {coreAbilities.length > 0 ? (
                  <div className="hb-cap-section-label">核心能力</div>
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
                  <div className="hb-cap-section-label">职责范围</div>
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
                  <div className="hb-cap-section-label">明确排除</div>
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
              <p style={{ color: "var(--hb-body)", fontSize: 14, lineHeight: 1.65 }}>
                {templateDescription}
              </p>
            ) : (
              <p style={{ color: "var(--hb-soft)", fontSize: 14 }}>
                暂无员工介绍
              </p>
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
