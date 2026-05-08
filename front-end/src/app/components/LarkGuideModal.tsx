import { X } from 'lucide-react'

export interface LarkGuideEmployee {
  name: string
  description?: string
  dept?: string
  initial?: string
}

export default function LarkGuideModal({
  open,
  employee,
  onClose,
  onConfirm,
}: {
  open: boolean
  employee: LarkGuideEmployee | null
  onClose: () => void
  onConfirm: () => void
}) {
  if (!open || !employee) {
    return null
  }

  return (
    <div className="hb-modal-mask" onClick={onClose}>
      <div className="hb-modal" onClick={(event) => event.stopPropagation()}>
        <button type="button" className="hb-modal-close" onClick={onClose} aria-label="关闭">
          <X size={16} />
        </button>

        <div className="hb-modal-head">
          <h3 className="hb-modal-title">去飞书使用</h3>
          <p className="hb-modal-sub">数字员工只在飞书一对一私聊中正式使用，本页不做主生产对话。</p>
        </div>

        <div className="hb-modal-body">
          <div className="hb-lark-preview">
            <div className="hb-lark-bar">
              <span className="hb-squircle hb-lark-avatar">{employee.initial || employee.name.slice(0, 1)}</span>
              <div className="min-w-0">
                <div className="truncate text-sm font-semibold text-[#0a0a0a]">{employee.name}</div>
                <div className="mt-1 truncate text-xs text-[#737373]">@ {employee.dept || '研发部'} · 数字员工</div>
              </div>
            </div>
            {employee.description && (
              <p className="mt-3 text-xs leading-relaxed text-[#404040]">{employee.description}</p>
            )}
          </div>

          <ol className="mt-4 list-decimal space-y-2 pl-5 text-sm leading-relaxed text-[#404040]">
            <li>打开飞书，搜索该数字员工名称。</li>
            <li>进入一对一私聊窗口，直接用自然语言下达任务。</li>
            <li>群聊默认不响应，任何对外发送都将以你本人身份登记。</li>
          </ol>
        </div>

        <div className="hb-modal-foot">
          <button type="button" className="hb-btn-ghost" onClick={onClose}>稍后</button>
          <button type="button" className="hb-btn-primary" onClick={onConfirm}>已复制 · 打开飞书</button>
        </div>
      </div>
    </div>
  )
}
