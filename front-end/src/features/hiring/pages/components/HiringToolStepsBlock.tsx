import { useState } from 'react'
import { Check, ChevronDown, ChevronRight, Loader2, X } from 'lucide-react'

import type { ToolStep } from '../hiringPageTypes'

/**
 * 单条工具调用行：标题（图标 + 工具名）+ 可展开的参数/返回详情
 * 参考自 kingcrab-console 的 ToolStepsBlock，样式贴合 HireBot 的 hb-* 体系
 */
function ToolStepRow({ step }: { step: ToolStep }) {
  const [detailOpen, setDetailOpen] = useState(false)
  const hasDetail = Boolean(step.args || step.result)

  return (
    <div className="hb-hiring-toolstep-row">
      <button
        type="button"
        disabled={!hasDetail}
        onClick={() => setDetailOpen((v) => !v)}
        className="hb-hiring-toolstep-row-head"
      >
        <span className="hb-hiring-toolstep-icon">
          {step.status === 'running' ? (
            <Loader2 size={14} className="hb-hiring-toolstep-spin" />
          ) : step.status === 'error' ? (
            <X size={14} color="#e11d48" />
          ) : (
            <Check size={14} color="#10b981" />
          )}
        </span>
        <code className="hb-hiring-toolstep-name">{step.name}</code>
        {hasDetail && (
          <span className="hb-hiring-toolstep-chevron">
            {detailOpen ? <ChevronDown size={12} /> : <ChevronRight size={12} />}
          </span>
        )}
      </button>
      {detailOpen && (
        <div className="hb-hiring-toolstep-detail">
          {step.args && (
            <div>
              <p className="hb-hiring-toolstep-label">参数</p>
              <pre className="hb-hiring-toolstep-code">
                {(() => {
                  try {
                    return JSON.stringify(JSON.parse(step.args), null, 2)
                  } catch {
                    return step.args
                  }
                })()}
              </pre>
            </div>
          )}
          {step.result && (
            <div>
              <p className="hb-hiring-toolstep-label">返回</p>
              <pre className="hb-hiring-toolstep-code">
                {step.result.length > 800 ? `${step.result.slice(0, 800)}…` : step.result}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

/**
 * 工具调用步骤折叠面板：渲染在 bot 消息气泡上方
 * - 标题栏概览：有 running 则显示当前工具+进度；否则显示已完成数量
 * - 展开后列出每条 ToolStepRow
 */
export function HiringToolStepsBlock({ steps }: { steps: ToolStep[] }) {
  const [open, setOpen] = useState(false)
  const runningStep = steps.find((s) => s.status === 'running')
  const doneCount = steps.filter((s) => s.status !== 'running').length

  return (
    <div className="hb-hiring-toolsteps">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="hb-hiring-toolsteps-head"
      >
        <span className="hb-hiring-toolsteps-chevron">
          {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        </span>
        {runningStep ? (
          <>
            <Loader2 size={12} className="hb-hiring-toolstep-spin" />
            <span className="hb-hiring-toolsteps-title is-running">
              正在调用 {runningStep.name}
            </span>
            {doneCount > 0 && (
              <span className="hb-hiring-toolsteps-count">
                {doneCount}/{steps.length}
              </span>
            )}
          </>
        ) : (
          <span className="hb-hiring-toolsteps-title">
            工具调用 ({steps.length})
          </span>
        )}
      </button>
      {open && (
        <div className="hb-hiring-toolsteps-body">
          {steps.map((step) => (
            <ToolStepRow key={step.id} step={step} />
          ))}
        </div>
      )}
    </div>
  )
}
