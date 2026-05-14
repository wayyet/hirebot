import { useEffect, useState } from "react";
import { Check, Clock3, CopyPlus, Loader2, ShieldCheck, X } from "lucide-react";
import { api, type EmployeeDetail } from "@/infra/api";
import CloneEmployeeModal from "./CloneEmployeeModal";
import {
  toEmployeeDetailSummary,
  withEmployeeView,
} from "../employeeView";

interface EmployeeDetailModalProps {
  open: boolean;
  employeeId: string;
  onClose: () => void;
  onCloneSuccess?: () => void;
}

export default function EmployeeDetailModal({
  open,
  employeeId,
  onClose,
  onCloneSuccess,
}: EmployeeDetailModalProps) {
  const [detail, setDetail] = useState<EmployeeDetail | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [cloneModalOpen, setCloneModalOpen] = useState(false);

  useEffect(() => {
    if (!open || !employeeId) return;

    setDetail(null);
    setError(null);

    async function load() {
      setLoading(true);
      try {
        const data = await api.employeeRuntime.getEmployee(employeeId);
        setDetail(data);
      } catch (err) {
        setError(err instanceof Error ? err.message : "加载详情失败");
      } finally {
        setLoading(false);
      }
    }

    void load();
  }, [open, employeeId]);

  if (!open) return null;

  const view = detail
    ? withEmployeeView(toEmployeeDetailSummary(detail))
    : null;
  const canClone =
    view?.ownership === "department" && view?.mappedStatus === "live";
  const readyCount = detail
    ? detail.capabilities.filter((c) => c.ready).length
    : 0;

  return (
    <>
      <div className="hb-modal-mask" onClick={onClose}>
        <div className="hb-modal" onClick={(e) => e.stopPropagation()}>
          <button
            type="button"
            className="hb-modal-close"
            onClick={onClose}
            aria-label="关闭"
          >
            <X size={16} />
          </button>

          {loading ? (
            <div className="flex items-center justify-center gap-2 p-10 text-sm text-[var(--hb-soft)]">
              <Loader2 size={16} className="animate-spin" />
              加载中...
            </div>
          ) : error ? (
            <div className="p-8">
              <div className="hb-alert hb-alert-error">{error}</div>
            </div>
          ) : !detail || !view ? (
            <div className="p-8 text-sm text-[var(--hb-soft)]">
              实例不存在
            </div>
          ) : (
            <>
              <div className="hb-modal-head">
                <h3 className="hb-modal-title">员工详情</h3>
              </div>

              <div className="hb-modal-body space-y-4">
                <div className="min-w-0 flex-1">
                  <h3 className="hb-detail-panel-title truncate">
                    {detail.nickname}
                  </h3>
                  <p className="mt-1 text-sm text-[var(--hb-soft)]">
                    {detail.roleName || detail.sourceTemplate}
                  </p>
                </div>

                {/* 元信息 */}
                <div className="rounded-lg border border-[var(--hb-border)] bg-[var(--hb-surface-soft)] p-3 text-xs">
                  <div className="flex flex-wrap gap-x-4 gap-y-1">
                    <span>
                      来源模板：{detail.sourceTemplate || "—"}
                    </span>
                    <span>部门：{detail.owningTeam || detail.departmentId}</span>
                    <span>创建于 {detail.createdAt}</span>
                    {detail.graduatedAt && (
                      <span>上岗时间：{detail.graduatedAt}</span>
                    )}
                  </div>
                </div>

                {/* 能力列表 */}
                <div>
                  <h4 className="mb-2 text-xs font-medium text-[var(--hb-soft)]">
                    能力简介
                  </h4>
                  <div className="space-y-1">
                    {detail.capabilities.map((cap) => (
                      <div
                        key={cap.name}
                        className={`flex items-center gap-2 rounded px-2 py-1 text-sm ${cap.ready ? "" : "text-[var(--hb-soft)]"}`}
                      >
                        <span className="flex h-4 w-4 items-center justify-center rounded-full bg-[var(--hb-surface-soft)] text-[10px]">
                          {cap.ready ? (
                            <Check size={10} className="text-emerald-500" />
                          ) : (
                            "×"
                          )}
                        </span>
                        <span className="truncate">{cap.name}</span>
                      </div>
                    ))}
                  </div>
                </div>

                {/* 运行状态 */}
                {view.mappedStatus === "live" && (
                  <div className="flex gap-4 rounded-lg border border-[var(--hb-border)] p-3 text-xs">
                    <div className="flex items-center gap-1">
                      <Clock3 size={12} />
                      <strong>{detail.graduatedAt || "—"}</strong>
                      <span className="text-[var(--hb-soft)]">上岗</span>
                    </div>
                    <div className="flex items-center gap-1">
                      <ShieldCheck size={12} />
                      <strong>{readyCount}</strong>
                      <span className="text-[var(--hb-soft)]">能力</span>
                    </div>
                    <div className="flex items-center gap-1">
                      <span className="text-[var(--hb-soft)]">版本</span>
                      <strong>
                        {detail.isConfigured ? "v1.0" : "待配置"}
                      </strong>
                    </div>
                  </div>
                )}

                {/* 状态说明 */}
                <div className="rounded-lg border border-emerald-200 bg-emerald-50 p-3 text-xs text-emerald-800">
                  {view.mappedStatus === "live"
                    ? view.ownership === "department"
                      ? "已上岗的部门员工可以作为复制源，创建分身后拥有独立会话。"
                      : "已上岗的个人资产可以站内对话，并按需配置飞书、钉钉或企微。"
                    : view.mappedStatus === "interning_ai"
                      ? "AI 评估中，通过后才允许进入人工评估。"
                      : view.mappedStatus === "interning_human"
                        ? "人工评估中，通过后才允许标记为已上岗。"
                        : view.mappedStatus === "failed"
                          ? "评估未通过，可重新雇佣或放弃。"
                          : view.mappedStatus === "retired"
                            ? "该实例已退役，当前仅保留历史信息。"
                            : "已雇佣，等待进入评估流程。"}
                </div>
              </div>

              <div className="hb-modal-foot">
                {canClone && (
                  <button
                    type="button"
                    className="hb-btn-primary"
                    onClick={() => setCloneModalOpen(true)}
                  >
                    <CopyPlus size={14} />
                    创建分身
                  </button>
                )}
                <button
                  type="button"
                  className="hb-btn-ghost"
                  onClick={onClose}
                >
                  关闭
                </button>
              </div>
            </>
          )}
        </div>
      </div>

      <CloneEmployeeModal
        open={cloneModalOpen}
        employeeId={employeeId}
        sourceNickname={detail?.nickname ?? ""}
        sourceRoleName={detail?.roleName ?? ""}
        onClose={() => setCloneModalOpen(false)}
        onSuccess={() => {
          setCloneModalOpen(false);
          onCloneSuccess?.();
        }}
      />
    </>
  );
}
