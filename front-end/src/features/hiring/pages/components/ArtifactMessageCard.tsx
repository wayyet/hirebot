import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import type { ArtifactDisplayData } from '../hiringPageTypes'

interface Props {
  artifact: ArtifactDisplayData
  /** 带 token 的文件下载回调；未提供时退化为直接 <a href> */
  onFileDownload?: (url: string, fileName: string) => void
}

export function ArtifactMessageCard({ artifact, onFileDownload }: Props) {
  const { t } = useTranslation()
  const title = artifact.label ?? artifact.artifactType

  return (
    <div className="hb-artifact-card">
      <div className="hb-artifact-header">
        <ArtifactIcon artifact={artifact} />
        <div className="hb-artifact-title-group">
          <span className="hb-artifact-title">{title}</span>
          {(artifact.skillName || artifact.stage) && (
            <span className="hb-artifact-subtitle">
              {[artifact.skillName, artifact.stage, artifact.isTerminal ? t('hiring.artifact.terminal') : null]
                .filter(Boolean)
                .join(' · ')}
            </span>
          )}
        </div>
      </div>

      {artifact.kind === 'file' ? (
        onFileDownload && artifact.fileUrl ? (
          // gateway 文件需要附带 token，通过回调触发认证下载
          <button
            type="button"
            className="hb-artifact-file-link"
            onClick={() => onFileDownload(artifact.fileUrl!, artifact.fileName ?? title)}
          >
            <span className="hb-artifact-file-name">{artifact.fileName ?? title}</span>
            {artifact.sizeLabel && <span className="hb-artifact-file-size">{artifact.sizeLabel}</span>}
          </button>
        ) : (
          <a
            href={artifact.fileUrl ?? '#'}
            download={artifact.fileName ?? title}
            className="hb-artifact-file-link"
          >
            <span className="hb-artifact-file-name">{artifact.fileName ?? title}</span>
            {artifact.sizeLabel && <span className="hb-artifact-file-size">{artifact.sizeLabel}</span>}
          </a>
        )
      ) : (
        <ArtifactDataView artifact={artifact} />
      )}
    </div>
  )
}

function ArtifactDataView({ artifact }: { artifact: ArtifactDisplayData }) {
  // 类型优先：特定 artifactType 使用内置专用视图
  if (artifact.artifactType === 'material_handoff_summary') return <MaterialHandoffView data={artifact.data} />
  if (artifact.artifactType === 'ontology_extraction_done' || artifact.artifactType === 'ontology_extraction_progress') {
    return <OntologyExtractionView data={artifact.data} />
  }
  const hint = artifact.displayHint ?? 'text'
  if (hint === 'progress') return <ProgressView data={artifact.data} />
  if (hint === 'table') return <TableView data={artifact.data} />
  if (hint === 'badge') return <BadgeView data={artifact.data} />
  if (hint === 'code') return <CodeView data={artifact.data} />
  if (hint === 'tree') return <CodeView data={artifact.data} />
  return <TextView data={artifact.data} />
}

function ProgressView({ data }: { data: unknown }) {
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

function TableView({ data }: { data: unknown }) {
  const rows = Array.isArray(data) ? data.filter(isRecord) : []
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

function BadgeView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  const value = stringify(rec?.value ?? rec?.status ?? data)
  return (
    <span className="hb-artifact-badge">{value}</span>
  )
}

function CodeView({ data }: { data: unknown }) {
  return (
    <pre style={codeStyle}>
      <code>{typeof data === 'string' ? data : JSON.stringify(data, null, 2)}</code>
    </pre>
  )
}

function TextView({ data }: { data: unknown }) {
  return <div style={{ fontSize: 13, whiteSpace: 'pre-wrap' }}>{stringify(data)}</div>
}

/** ontology_extraction_done / _progress 专用结构化视图 */
function OntologyExtractionView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const totalSources = Number(rec.total_sources ?? 0)
  const completedSlices = Number(rec.completed_slices ?? 0)
  const slicePaths = Array.isArray(rec.slice_paths)
    ? rec.slice_paths.filter((p): p is string => typeof p === 'string')
    : []
  const validation = typeof rec.validation === 'string' ? rec.validation.toUpperCase() : ''
  const status = typeof rec.status === 'string' ? rec.status : ''
  const validationPass = validation === 'PASS'
  const isDone = status === 'done' || status === 'completed'

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {/* 统计行 */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        {totalSources > 0 && (
          <span style={statChipStyle}>
            {'📂 '}来源 <b style={{ marginLeft: 3 }}>{totalSources}</b>
          </span>
        )}
        {completedSlices > 0 && (
          <span style={statChipStyle}>
            {'✂️ '}切片 <b style={{ marginLeft: 3 }}>{completedSlices}</b>
          </span>
        )}
        {validation && (
          <span style={{
            fontSize: 11, padding: '2px 8px', borderRadius: 99,
            fontWeight: 700, letterSpacing: 0.3,
            background: validationPass ? 'rgba(16, 185, 129, 0.15)' : 'rgba(239, 68, 68, 0.15)',
            color: validationPass ? '#059669' : '#dc2626',
            border: `1px solid ${validationPass ? 'rgba(16, 185, 129, 0.30)' : 'rgba(239, 68, 68, 0.30)'}`,
          }}>
            {validationPass ? '✓ PASS' : '✗ ' + validation}
          </span>
        )}
        {!isDone && status && (
          <span style={{ fontSize: 11, color: 'var(--hb-text-muted, #6b7280)' }}>{status}</span>
        )}
      </div>

      {/* 输出切片路径列表 */}
      {slicePaths.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {slicePaths.map((p, i) => (
            <div key={i} style={{
              display: 'flex', alignItems: 'center', gap: 6,
              padding: '4px 8px', borderRadius: 6,
              border: '1px solid var(--hb-border, #e5e7eb)',
              background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 50%, transparent)',
              fontSize: 11, fontFamily: 'monospace',
              overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap',
            }}>
              <span style={{ opacity: 0.5, flexShrink: 0 }}>{'📄'}</span>
              <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{p}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

const statChipStyle: CSSProperties = {
  display: 'inline-flex', alignItems: 'center',
  fontSize: 11, padding: '2px 8px', borderRadius: 99,
  border: '1px solid var(--hb-border, #e5e7eb)',
  background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 60%, transparent)',
  color: 'var(--hb-text-muted, #6b7280)',
}

/** material_handoff_summary 专用结构化视图 */
function MaterialHandoffView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const summary = typeof rec.summary === 'string' ? rec.summary : ''
  const items = Array.isArray(rec.items) ? rec.items.filter(isRecord) : []

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {summary && (
        <div style={{ fontSize: 12, color: 'var(--hb-text-muted, #6b7280)', lineHeight: 1.6 }}>
          {summary}
        </div>
      )}
      {items.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
          {items.map((item, i) => {
            const status = String(item.status ?? '')
            const hasContent = item.source_path != null || status === 'collected'
            const title = String(item.title ?? '')
            const category = String(item.category ?? '')
            return (
              <div
                key={i}
                style={{
                  display: 'flex', alignItems: 'flex-start', gap: 7,
                  padding: '6px 8px', borderRadius: 7,
                  border: '1px solid var(--hb-border, #e5e7eb)',
                  background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 50%, transparent)',
                }}
              >
                <span style={{
                  marginTop: 3, width: 7, height: 7, borderRadius: '50%', flexShrink: 0,
                  background: hasContent ? '#10b981' : '#f59e0b',
                }} />
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div style={{ fontSize: 12, fontWeight: 600, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {title}
                  </div>
                  {category && (
                    <div style={{ fontSize: 11, color: 'var(--hb-text-muted, #6b7280)', marginTop: 1 }}>
                      {category}
                    </div>
                  )}
                </div>
                <span style={{
                  fontSize: 10, flexShrink: 0, marginTop: 2,
                  padding: '1px 6px', borderRadius: 99,
                  background: hasContent ? 'rgba(16, 185, 129, 0.15)' : 'rgba(245, 158, 11, 0.15)',
                  color: hasContent ? '#059669' : '#d97706',
                  border: `1px solid ${hasContent ? 'rgba(16, 185, 129, 0.30)' : 'rgba(245, 158, 11, 0.30)'}`,
                }}>
                  {hasContent ? '已收集' : '待补充'}
                </span>
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}

function ArtifactIcon({ artifact }: { artifact: ArtifactDisplayData }) {
  if (artifact.kind === 'file') return <span className="hb-artifact-icon">📄</span>
  const map: Record<string, string> = { table: '📊', code: '💻', tree: '🌿', badge: '✅', progress: '⏳' }
  return <span className="hb-artifact-icon">{map[artifact.displayHint ?? ''] ?? '📦'}</span>
}

function isRecord(v: unknown): v is Record<string, unknown> {
  return !!v && typeof v === 'object' && !Array.isArray(v)
}
function asRecord(v: unknown): Record<string, unknown> | null {
  return isRecord(v) ? v : null
}
function stringify(v: unknown): string {
  if (v == null) return ''
  if (typeof v === 'string') return v
  if (typeof v === 'number' || typeof v === 'boolean') return String(v)
  return JSON.stringify(v)
}
function clamp(v: number) {
  return Math.max(0, Math.min(100, Math.round(Number.isFinite(v) ? v : 0)))
}

const progressTrackStyle: CSSProperties = {
  height: 7, borderRadius: 99, background: 'var(--hb-surface-soft, #f3f4f6)', overflow: 'hidden',
  border: '1px solid var(--hb-border, #e5e7eb)',
}
const progressFillStyle: CSSProperties = {
  height: '100%', background: 'var(--hb-primary, #2563eb)', transition: 'width 0.3s ease',
}
function cellStyle(header: boolean): CSSProperties {
  return {
    padding: '7px 9px', borderBottom: '1px solid var(--hb-border, #e5e7eb)', textAlign: 'left',
    fontWeight: header ? 700 : 400,
    background: header ? 'var(--hb-surface-soft, #f9fafb)' : 'transparent',
    whiteSpace: 'nowrap', fontSize: 12,
  }
}
const codeStyle: CSSProperties = {
  margin: 0, padding: '9px 10px', borderRadius: 8,
  border: '1px solid var(--hb-border, #e5e7eb)',
  background: 'var(--hb-surface-soft, #f9fafb)',
  overflowX: 'auto', fontSize: 12, lineHeight: 1.55, maxHeight: 280,
}
