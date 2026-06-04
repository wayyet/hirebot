/**
 * OntologyArtifactView.tsx - 本体抽取 Artifact 视图
 * 
 * 用于展示 ontology_extraction_done / ontology_extraction_progress 类型的 artifact
 */

import { CodeView } from './BaseArtifactViews'
import { asRecord } from './utils/artifactHelpers'
import { statChipStyle } from './utils/artifactStyles'

interface OntologyArtifactViewProps {
  data: unknown
}

/**
 * 本体抽取视图 - ontology_extraction_done / _progress 专用结构化视图
 */
export function OntologyExtractionView({ data }: OntologyArtifactViewProps) {
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
            color: validationPass ? 'var(--hb-text-green, #059669)' : 'var(--hb-danger, #dc2626)',
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
