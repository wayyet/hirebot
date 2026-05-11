import type { CSSProperties } from 'react'
import type { ArtifactDisplayData } from '../hiringPageTypes'

interface Props {
  artifact: ArtifactDisplayData
  formatFileSize: (bytes: number) => string
}

export function ArtifactMessageCard({ artifact, formatFileSize }: Props) {
  const title = artifact.label ?? artifact.artifactType

  return (
    <div className="hb-artifact-card">
      <div className="hb-artifact-header">
        <ArtifactIcon artifact={artifact} />
        <div className="hb-artifact-title-group">
          <span className="hb-artifact-title">{title}</span>
          {(artifact.skillName || artifact.stage) && (
            <span className="hb-artifact-subtitle">
              {[artifact.skillName, artifact.stage, artifact.isTerminal ? '终态' : null]
                .filter(Boolean)
                .join(' · ')}
            </span>
          )}
        </div>
      </div>

      {artifact.kind === 'file' ? (
        <a
          href={artifact.fileUrl ?? '#'}
          download={artifact.fileName ?? title}
          className="hb-artifact-file-link"
        >
          <span className="hb-artifact-file-name">{artifact.fileName ?? title}</span>
          {artifact.sizeLabel && <span className="hb-artifact-file-size">{artifact.sizeLabel}</span>}
        </a>
      ) : (
        <ArtifactDataView artifact={artifact} formatFileSize={formatFileSize} />
      )}
    </div>
  )
}

function ArtifactDataView({ artifact }: { artifact: ArtifactDisplayData; formatFileSize: (bytes: number) => string }) {
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
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <div style={progressTrackStyle}>
        <div style={{ ...progressFillStyle, width: `${percent}%` }} />
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 11, color: 'var(--hb-text-muted, #6b7280)' }}>
        <span>{message}</span>
        <span>{percent}%</span>
      </div>
    </div>
  )
}

function TableView({ data }: { data: unknown }) {
  const rows = Array.isArray(data) ? data.filter(isRecord) : []
  if (rows.length === 0) return <CodeView data={data} />
  const columns = Array.from(new Set(rows.flatMap(r => Object.keys(r)))).slice(0, 8)
  return (
    <div style={{ overflowX: 'auto', border: '1px solid var(--hb-border, #e5e7eb)', borderRadius: 8 }}>
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
  height: 7, borderRadius: 99, background: '#f3f4f6', overflow: 'hidden', border: '1px solid #e5e7eb',
}
const progressFillStyle: CSSProperties = {
  height: '100%', background: 'var(--hb-primary, #2563eb)', transition: 'width 0.3s ease',
}
function cellStyle(header: boolean): CSSProperties {
  return {
    padding: '7px 9px', borderBottom: '1px solid #e5e7eb', textAlign: 'left',
    fontWeight: header ? 700 : 400, background: header ? '#f9fafb' : 'transparent',
    whiteSpace: 'nowrap', fontSize: 12,
  }
}
const codeStyle: CSSProperties = {
  margin: 0, padding: '9px 10px', borderRadius: 8, border: '1px solid #e5e7eb',
  background: '#f9fafb', overflowX: 'auto', fontSize: 12, lineHeight: 1.55, maxHeight: 280,
}
