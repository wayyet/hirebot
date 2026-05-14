import { useEffect, useState } from "react";
import { AlertTriangle, CopyPlus, Loader2, X } from "lucide-react";
import { api } from "@/infra/api";
import { ApiClientError } from "@/infra/api/httpClient";

interface CloneEmployeeModalProps {
  open: boolean;
  employeeId: string;
  sourceNickname: string;
  sourceRoleName: string;
  onClose: () => void;
  onSuccess: () => void;
}

export default function CloneEmployeeModal({
  open,
  employeeId,
  sourceNickname,
  sourceRoleName,
  onClose,
  onSuccess,
}: CloneEmployeeModalProps) {
  const [displayName, setDisplayName] = useState("");
  const [cloneCount, setCloneCount] = useState(0);
  const [maxClones, setMaxClones] = useState(10);
  const [loadingCount, setLoadingCount] = useState(false);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!open) return;

    setDisplayName(`${sourceNickname} · 我的分身`);
    setError(null);

    const max =
      (typeof window !== "undefined" &&
        window.__AUTH_CONFIG__?.MaxActivePersonalClonesPerOwner) ||
      10;
    setMaxClones(max);

    async function loadCount() {
      setLoadingCount(true);
      try {
        const employees = await api.employeeRuntime.getEmployees();
        const count = employees.filter(
          (e) =>
            e.instanceType === "personal_clone" && e.status !== "retired",
        ).length;
        setCloneCount(count);
      } catch {
        // 加载失败不阻塞
      } finally {
        setLoadingCount(false);
      }
    }

    void loadCount();
  }, [open, sourceNickname]);

  if (!open) return null;

  const limitReached = cloneCount >= maxClones;

  function handleClose() {
    if (creating) return;
    onClose();
  }

  async function handleCreate() {
    const name = displayName.trim();
    if (!name) {
      setError("请输入分身名称");
      return;
    }

    if (limitReached) return;

    setCreating(true);
    setError(null);

    try {
      await api.employeeRuntime.createPersonalClone(employeeId, {
        displayName: name,
      });
      onSuccess();
    } catch (err) {
      const message =
        err instanceof ApiClientError ? err.message : "创建分身失败，请重试";
      setError(message);
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className="hb-modal-mask" onClick={handleClose}>
      <div className="hb-modal" onClick={(e) => e.stopPropagation()}>
        <button
          type="button"
          className="hb-modal-close"
          onClick={handleClose}
          aria-label="关闭"
        >
          <X size={16} />
        </button>

        <div className="hb-modal-head">
          <h3 className="hb-modal-title">复制为我的分身</h3>
          <p className="hb-modal-sub">
            基于部门员工创建独立的个人副本，你的会话不会回流给部门版。
          </p>
        </div>

        <div className="hb-modal-body space-y-4">
          <div className="rounded-lg border border-[var(--hb-border)] bg-[var(--hb-surface-soft)] p-3">
            <p className="text-xs text-[var(--hb-soft)]">源员工</p>
            <p className="mt-0.5 truncate text-sm font-medium">
              {sourceNickname}
            </p>
            <p className="text-xs text-[var(--hb-soft)]">{sourceRoleName}</p>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-[var(--hb-soft)]">
              分身名称
            </label>
            <input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              className="hb-input w-full"
              placeholder="输入分身名称"
              disabled={creating}
              onKeyDown={(e) => {
                if (e.key === "Enter") handleCreate();
              }}
            />
          </div>

          <div className="flex items-center gap-2 text-xs text-[var(--hb-soft)]">
            {loadingCount ? (
              <Loader2 size={12} className="animate-spin" />
            ) : null}
            <span>
              当前已有 {cloneCount}/{maxClones} 个分身
            </span>
          </div>

          {limitReached && (
            <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-xs text-amber-700">
              <AlertTriangle size={14} className="mt-0.5 flex-shrink-0" />
              <span>分身上限已达（{maxClones}个），请先清理不再使用的分身。</span>
            </div>
          )}

          {error && (
            <div className="hb-alert hb-alert-error">
              <span>{error}</span>
            </div>
          )}
        </div>

        <div className="hb-modal-foot">
          <button
            type="button"
            className="hb-btn-ghost"
            onClick={handleClose}
            disabled={creating}
          >
            取消
          </button>
          <button
            type="button"
            className="hb-btn-primary"
            onClick={handleCreate}
            disabled={limitReached || creating || !displayName.trim()}
          >
            {creating ? (
              <>
                <Loader2 size={14} className="animate-spin" />
                创建中...
              </>
            ) : (
              <>
                <CopyPlus size={14} />
                确认创建
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
}
