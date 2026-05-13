import { useEffect, useRef, useState } from "react";
import { Loader2 } from "lucide-react";
import { useNavigate, useParams } from "react-router-dom";
import { api, type EmployeeDetail } from "@/infra/api";
import { firstCharacter } from "./employeeView";
import { Breadcrumb } from "@/shared/components/Breadcrumb";

type Step = 0 | 1 | 2;

const DEFAULT_MAX_PERSONAL_CLONES = 10;

function resolveMaxPersonalClones() {
  const configured =
    typeof window !== "undefined"
      ? window.__AUTH_CONFIG__?.MaxActivePersonalClonesPerOwner
      : undefined;
  return typeof configured === "number" && Number.isFinite(configured) && configured > 0
    ? configured
    : DEFAULT_MAX_PERSONAL_CLONES;
}

export default function CloneEmployeePage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [employee, setEmployee] = useState<EmployeeDetail | null>(null);
  const [personalCloneCount, setPersonalCloneCount] = useState(0);
  const [step, setStep] = useState<Step>(0);
  const [displayName, setDisplayName] = useState("");
  const [displayDescription, setDisplayDescription] = useState("");
  const [bindProgress, setBindProgress] = useState(0);
  const [creatingClone, setCreatingClone] = useState(false);
  const progressRef = useRef<number | null>(null);
  const maxPersonalClones = resolveMaxPersonalClones();

  useEffect(() => {
    if (!id) {
      setError("实例 ID 缺失");
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError("");

    Promise.all([
      api.employeeRuntime.getEmployee(id),
      api.employeeRuntime.getEmployees(),
    ])
      .then(([detail, employees]) => {
        if (!cancelled) {
          setEmployee(detail);
          setPersonalCloneCount(
            employees.filter(
              (item) =>
                item.instanceType === "personal_clone" &&
                item.status !== "retired",
            ).length,
          );
          setDisplayName(`${detail.nickname} · 我的分身`);
          setDisplayDescription(
            `基于 ${detail.nickname} 创建的个人版本，记住我的工作偏好。`,
          );
        }
      })
      .catch((requestError: unknown) => {
        if (!cancelled) {
          setEmployee(null);
          setError(
            requestError instanceof Error
              ? requestError.message
              : "加载复制页面失败",
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

  useEffect(() => {
    return () => {
      if (progressRef.current !== null) {
        window.clearInterval(progressRef.current);
      }
    };
  }, []);

  function beginBinding() {
    setStep(2);
    setBindProgress(0);

    if (progressRef.current !== null) {
      window.clearInterval(progressRef.current);
      progressRef.current = null;
    }

    progressRef.current = window.setInterval(() => {
      setBindProgress((prev) => {
        const next = prev + 1;
        if (next >= 3 && progressRef.current !== null) {
          window.clearInterval(progressRef.current);
          progressRef.current = null;
          void createClone();
        }
        return Math.min(next, 3);
      });
    }, 700);
  }

  async function createClone() {
    if (!id || !employee || creatingClone) return;
    if (personalCloneCount >= maxPersonalClones) {
      setError(
        `个人分身数量已达上限（最多 ${maxPersonalClones} 个），请先归档不再使用的分身。`,
      );
      setStep(0);
      return;
    }

    setCreatingClone(true);
    setError("");

    try {
      const cloned = await api.employeeRuntime.createPersonalClone(id, {
        displayName: displayName.trim(),
        displayDescription: displayDescription.trim(),
      });
      navigate(`/my-employees/instances/${cloned.employeeId}`);
    } catch (requestError: unknown) {
      setError(
        requestError instanceof Error
          ? requestError.message
          : "创建个人分身失败",
      );
      setStep(1);
    } finally {
      setCreatingClone(false);
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
        <Breadcrumb items={[{ label: '我的数字员工', to: '/my-employees' }, { label: '复制员工' }]} />

        <div className="rounded-2xl border border-[#ffd5da] bg-[#fff1f2] px-4 py-3 text-sm text-[#b3263c]">
          {error || "未找到实例数据"}
        </div>
      </div>
    );
  }

  const steps = ["确认复制", "配置个人身份", "绑定并上岗"];

  const cloneLimitReached = personalCloneCount >= maxPersonalClones;

  return (
    <div className="hb-page space-y-5">
      <Breadcrumb items={[{ label: '我的数字员工', to: '/my-employees' }, { label: `复制分身 · ${employee.nickname}` }]} />

      <div className="hb-hero">
        <div className="hb-hero-grid">
          <div className="hb-toolbar">
            <div>
              <div className="hb-hero-eyebrow">分身复制向导</div>
              <h1 className="hb-hero-title">
                复制为我的分身 · {employee.nickname}
              </h1>
              <p className="hb-hero-copy">
                复制流程不会触发新的雇佣对话，完成后会进入「我的数字员工」。你只需要确认名称、描述，再完成绑定即可。
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
              <div className="hb-metric-label">原状态</div>
              <div className="hb-metric-value">{employee.lifecycleStatus}</div>
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
                  {employee.stageSummary || employee.primarySignal}
                </div>
              </div>
            </div>
            <div className="rounded-xl border border-[#d9e1ff] bg-[#eef2ff] px-4 py-3 text-sm text-[#2e3da9]">
              复制后是独立实例，你的会话不会回流给部门版，他人也看不到你的会话明细。
            </div>
            <div
              className={`rounded-xl border px-4 py-3 text-sm ${
                cloneLimitReached
                  ? "border-[#ffd5da] bg-[#fff1f2] text-[#b3263c]"
                  : "border-[#e5e7eb] bg-white text-[#525252]"
              }`}
            >
              你当前已有 {personalCloneCount}/{maxPersonalClones} 个个人分身。最多只能创建{" "}
              {maxPersonalClones} 个个人分身。
              {cloneLimitReached ? " 请先归档不再使用的分身后再创建。" : ""}
            </div>
            <div className="flex justify-end gap-2">
              <button
                type="button"
                className="hb-btn-ghost"
                onClick={() => navigate("/my-employees")}
              >
                取消
              </button>
              <button
                type="button"
                className="hb-btn-primary"
                onClick={() => setStep(1)}
                disabled={cloneLimitReached}
              >
                开始复制 →
              </button>
            </div>
          </div>
        )}

        {step === 1 && (
          <div className="space-y-4">
            <label className="block">
              <div className="mb-1 text-sm font-medium text-[#404040]">
                display_name
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
                onClick={beginBinding}
                disabled={!displayName.trim() || cloneLimitReached}
              >
                绑定并上岗 →
              </button>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3">
            {["注册 bot", "启动运行时", "状态切为 live"].map((task, index) => (
              <div
                key={task}
                className="flex items-center justify-between rounded-xl border border-[#f3f4f6] px-3 py-2"
              >
                <span className="text-sm text-[#404040]">{task}</span>
                <span
                  className={`hb-pill ${bindProgress > index ? "green" : bindProgress === index ? "blue" : "gray"}`}
                >
                  {bindProgress > index
                    ? "已完成"
                    : bindProgress === index
                      ? "进行中"
                      : "等待"}
                </span>
              </div>
            ))}

            {bindProgress >= 3 && (
              <>
                <div className="rounded-2xl border border-[#d1fae5] bg-[#ecfdf5] px-4 py-3 text-sm text-[#15803d]">
                  {displayName}{" "}
                  正在调用真实个人分身接口，完成后会直接进入实例详情。
                </div>
                <div className="flex justify-end">
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => void createClone()}
                    disabled={creatingClone}
                  >
                    {creatingClone ? (
                      <>
                        <Loader2 size={14} className="animate-spin mr-2" />
                        生成分身中...
                      </>
                    ) : (
                      "生成分身 →"
                    )}
                  </button>
                </div>
              </>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
