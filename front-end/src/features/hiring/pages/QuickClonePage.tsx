import { useEffect, useState } from "react";
import { ArrowLeft, Loader2, Sparkles } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { api, type EmployeeDetail } from "@/infra/api";
import { useUserRole } from "@/app/context/UserRoleContext";
import { firstCharacter } from "./employeeView";

type Step = 0 | 1 | 2;

export default function QuickClonePage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const { role } = useUserRole();

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [step, setStep] = useState<Step>(0);
  const [displayName, setDisplayName] = useState("");
  const [displayDescription, setDisplayDescription] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [newInstanceId, setNewInstanceId] = useState<string | null>(null);
  const [showGuide, setShowGuide] = useState(false);

  useEffect(() => {
    if (!id) {
      setError("实例 ID 缺失");
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError("");

    api.employeeRuntime
      .getEmployee(id)
      .then((detail) => {
        if (!cancelled) {
          setEmployee(detail);
          setDisplayName(`${detail.nickname}（二号）`);
          setDisplayDescription(`基于 ${detail.nickname} 快捷复制创建的部门员工。`);
        }
      })
      .catch((requestError: unknown) => {
        if (!cancelled) {
          setEmployee(null);
          setError(
            requestError instanceof Error
              ? requestError.message
              : "加载员工信息失败",
          );
        }
      })
      .finally(() => {
        if (!cancelled) {
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [id]);

  if (role !== "manager") {
    return (
      <div className="hb-page">
        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          仅部门长可执行快捷复制操作。
        </div>
      </div>
    );
  }

  async function submitQuickClone() {
    if (!id || !employee || submitting) return;

    setSubmitting(true);
    setError("");

    try {
      const result = await api.employeeRuntime.quickClone(id, {
        displayName: displayName.trim(),
        userRole: role,
        displayDescription: displayDescription.trim(),
      });
      setNewInstanceId(result.newInstanceId);
      setStep(2);
      setShowGuide(true);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "快捷复制失败",
      );
    } finally {
      setSubmitting(false);
    }
  }

  if (loading) {
    return (
      <div className="hb-page">
        <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-8 text-sm text-[#737373]">
          <Loader2 size={16} className="animate-spin" />
          正在加载复制向导...
        </div>
      </div>
    );
  }

  if (!employee) {
    return (
      <div className="hb-page space-y-4">
        <button
          type="button"
          onClick={() => navigate("/department-employees")}
          className="hb-btn-ghost"
        >
          <ArrowLeft size={14} />
          返回部门数字员工
        </button>
        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          {error || "未找到员工数据"}
        </div>
      </div>
    );
  }

  const steps = ["确认复制", "配置信息", "完成上岗"];

  return (
    <div className="hb-page space-y-5">
      <button
        type="button"
        onClick={() => navigate("/department-employees")}
        className="hb-btn-ghost"
      >
        <ArrowLeft size={14} />
        返回部门数字员工
      </button>

      <div className="hb-hero">
        <div className="hb-hero-grid">
          <div className="hb-toolbar">
            <div>
              <div className="hb-hero-eyebrow">部门长快捷复制</div>
              <h1 className="hb-hero-title">
                创建部门员工 · {employee.nickname}
              </h1>
              <p className="hb-hero-copy">
                基于已上岗部门员工一键复制，继承评估结果，无需重新评估，直接上岗发布到部门员工列表。
              </p>
            </div>
          </div>
          <div className="hb-hero-metrics">
            <div className="hb-metric-card">
              <div className="hb-metric-label">源员工</div>
              <div className="hb-metric-value">{employee.nickname}</div>
            </div>
            <div className="hb-metric-card">
              <div className="hb-metric-label">来源模板</div>
              <div className="hb-metric-value">{employee.sourceTemplate}</div>
            </div>
            <div className="hb-metric-card">
              <div className="hb-metric-label">评估结果</div>
              <div className="hb-metric-value text-[#15803d]">继承源员工</div>
            </div>
          </div>
        </div>
      </div>

      <div className="hb-section-soft">
        <div className="flex flex-wrap items-center gap-2">
          {steps.map((label, index) => (
            <span
              key={label}
              className={`inline-flex items-center gap-2 rounded-full border px-3 py-1.5 text-sm ${
                index < step
                  ? "border-[#d1fae5] bg-[#ecfdf5] text-[#15803d]"
                  : index === step
                    ? "border-[#0a0a0a] bg-[#0a0a0a] text-white"
                    : "border-[#ececec] bg-white text-[#737373]"
              }`}
            >
              <span className="text-xs">{index + 1}</span>
              {label}
            </span>
          ))}
        </div>
      </div>

      <div className="hb-card p-6">
        {step === 0 && (
          <div className="space-y-4">
            <div className="flex items-start gap-3 rounded-2xl border border-[#f3f4f6] bg-[#fafafa] p-4">
              <span className="hb-squircle h-12 w-12 bg-[#dde9ff] text-[#3d5cff]">
                {firstCharacter(employee.nickname)}
              </span>
              <div className="min-w-0">
                <div className="text-sm font-semibold text-[#0a0a0a]">
                  {employee.nickname}
                </div>
                <div className="mt-1 text-xs text-[#737373]">
                  {employee.roleName || employee.sourceTemplate}
                </div>
                <div className="mt-2 text-sm text-[#404040]">
                  状态：已上岗 · 可快捷复制
                </div>
              </div>
            </div>
            <div className="rounded-xl border border-[#d1fae5] bg-[#ecfdf5] px-4 py-3 text-sm text-[#15803d]">
              快捷复制将基于「{employee.nickname}」创建新的部门员工。新员工继承源员工的评估结果，无需重新评估，直接上岗发布到部门员工列表，全部门成员可见。
            </div>
            <div className="flex justify-end gap-2">
              <button
                type="button"
                className="hb-btn-ghost"
                onClick={() => navigate("/department-employees")}
              >
                取消
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => setStep(1)}
              >
                确认复制 →
              </button>
            </div>
          </div>
        )}

        {step === 1 && (
          <div className="space-y-4">
            <label className="block">
              <div className="mb-1 text-sm font-medium text-[#404040]">
                display_name <span className="text-[#b3263c]">*</span>
              </div>
              <input
                value={displayName}
                onChange={(event) => setDisplayName(event.target.value)}
                className="w-full rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
              />
            </label>
            <label className="block">
              <div className="mb-1 text-sm font-medium text-[#404040]">
                display_description
              </div>
              <textarea
                rows={4}
                value={displayDescription}
                onChange={(event) => setDisplayDescription(event.target.value)}
                className="w-full resize-none rounded-lg border border-[#e5e5e5] bg-white px-3 py-2 text-sm outline-none focus:border-[#4a6cf7] focus:shadow-[0_0_0_3px_rgba(74,108,247,0.2)]"
              />
            </label>
            {error && (
              <div className="rounded-xl border border-[#ffd5da] bg-[#fff1f2] px-3 py-2 text-xs text-[#b3263c]">
                {error}
              </div>
            )}
            <div className="flex justify-end gap-2">
              <button
                type="button"
                className="hb-btn-ghost"
                onClick={() => setStep(0)}
              >
                上一步
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => void submitQuickClone()}
                disabled={!displayName.trim() || submitting}
              >
                {submitting ? (
                  <>
                    <Loader2 size={14} className="mr-2 animate-spin" />
                    复制中...
                  </>
                ) : (
                  "发布到部门员工列表 →"
                )}
              </button>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-4">
            <div className="rounded-2xl border border-[#d1fae5] bg-[#ecfdf5] px-4 py-3 text-sm text-[#15803d]">
              <p className="font-semibold">部门员工已发布</p>
              <p className="mt-1">
                「{displayName}」已作为部门员工上岗，站内对话立即可用，全部门成员可从该员工创建个人分身。
              </p>
            </div>

            {showGuide && (
              <div className="rounded-2xl border border-[#d9e1ff] bg-[#eef2ff] px-4 py-3">
                <div className="flex items-start gap-3">
                  <Sparkles size={18} className="mt-0.5 text-[#3d5cff]" />
                  <div>
                    <p className="text-sm font-semibold text-[#2e3da9]">
                      建议您先复制一个自己的分身体验
                    </p>
                    <p className="mt-1 text-sm text-[#4a5bc7]">
                      该员工已发布，您可以为自己创建一个个人分身来实际体验效果。
                    </p>
                    <div className="mt-3 flex gap-2">
                      <button
                        type="button"
                        className="hb-btn-primary text-xs"
                        onClick={() => {
                          if (newInstanceId) {
                            navigate(`/clone/${newInstanceId}`);
                          }
                        }}
                      >
                        立即创建我的分身
                      </button>
                      <button
                        type="button"
                        className="hb-btn-ghost text-xs"
                        onClick={() => setShowGuide(false)}
                      >
                        稍后再说
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            )}

            <div className="flex justify-end gap-2">
              {newInstanceId ? (
                <button
                  type="button"
                  className="hb-btn-primary"
                  onClick={() => navigate(`/instances/${newInstanceId}`)}
                >
                  查看新员工详情 →
                </button>
              ) : null}
              <button
                type="button"
                className="hb-btn-ghost"
                onClick={() => navigate("/department-employees")}
              >
                返回部门列表
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
