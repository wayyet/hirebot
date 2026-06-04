/**
 * BaseArtifactViews.tsx - 通用 Artifact 视图组件
 * 
 * 包含可复用的基础展示视图：进度条、表格、徽章、代码块、纯文本
 */

import { sanitizeArtifactDataForDisplay } from './utils/artifactConstants'
import { asRecord, clamp, isRecord, stringify } from './utils/artifactHelpers'
import { cellStyle, codeStyle, progressFillStyle, progressTrackStyle } from './utils/artifactStyles'

interface ViewProps {
  data: unknown
}

/**
 * 进度条视图 - 展示百分比进度和消息文本
 */
export function ProgressView({ data }: ViewProps) {
  const rec = asRecord(data)
  const percent = clamp(Number(rec?.percent ?? rec?.progress ?? 0))
  const message = stringify(rec?.message ?? rec?.label ?? '')
  
  // 无进度值且无说明文字时，不渲染空进度条
  if (percent === 0 && !message) return null
  
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      {percent > 0 && (
        <div style={progressTrackStyle}>
          <div style={{ ...progressFillStyle, width: `${percent}%` }} />
        </div>
      )}
      {message && (
        <div style={{ fontSize: 11, color: 'var(--hb-text-muted, #6b7280)' }}>
          <span>{message}</span>
        </div>
      )}
    </div>
  )
}

/**
 * 表格视图 - 将数组数据渲染为表格（最多12行、8列）
 */
export function TableView({ data }: ViewProps) {
  const sanitized = sanitizeArtifactDataForDisplay(data)
  const rows = Array.isArray(sanitized) ? sanitized.filter(isRecord) : []
  
  if (rows.length === 0) return <CodeView data={data} />
  
  const columns = Array.from(new Set(rows.flatMap(r => Object.keys(r)))).slice(0, 8)
  
  return (
    <div style={{ overflowX: 'auto', border: '1px solid var(--hb-border, #e5e7eb)', borderRadius: 8, background: 'var(--hb-surface-soft, #f9fafb)' }}>
      <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 12 }}>
        <thead>
          <tr>{columns.map(col => <th key={col} style={cellStyle(true)}>{col}</th>)}</tr>
        </thead>
        <tbody>
          {rows.slice(0, 12).map((row, i) => (
            <tr key={i}>{columns.map(col => <td key={col} style={cellStyle(false)}>{stringify(row[col])}</td>)}</tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

/**
 * 徽章视图 - 展示简短状态或值（适用于单一指示器）
 */
export function BadgeView({ data }: ViewProps) {
  const rec = asRecord(data)
  const value = stringify(rec?.value ?? rec?.status ?? sanitizeArtifactDataForDisplay(data))
  
  return (
    <span className="hb-artifact-badge">{value}</span>
  )
}

/**
 * 代码块视图 - JSON 或文本代码展示
 */
export function CodeView({ data }: ViewProps) {
  const sanitized = sanitizeArtifactDataForDisplay(data)
  
  return (
    <pre style={codeStyle}>
      <code>{typeof sanitized === 'string' ? sanitized : JSON.stringify(sanitized, null, 2)}</code>
    </pre>
  )
}

/**
 * 纯文本视图 - 最简单的文本展示（兜底）
 */
export function TextView({ data }: ViewProps) {
  return (
    <div style={{ fontSize: 13, whiteSpace: 'pre-wrap' }}>
      {stringify(sanitizeArtifactDataForDisplay(data))}
    </div>
  )
}
