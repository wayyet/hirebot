/**
 * OntologyArtifactView.tsx - 本体抽取 / 投影 Artifact 视图
 *
 * 用于展示以下 artifact 类型：
 * - ontology_slice_extraction_done / ontology_slice_extraction_progress（R1 本体切片抽取）
 * - ontology_projection_done / ontology_projection_progress（R2 投影匹配）
 */

import { CodeView } from './BaseArtifactViews'
import { asRecord, toPublicPathLabel } from './utils/artifactHelpers'
import { statChipStyle } from './utils/artifactStyles'

interface OntologyArtifactViewProps {
  data: unknown
  artifactType?: string
}

function isProjectionType(artifactType: string | undefined): boolean {
  return artifactType === 'ontology_projection_done' || artifactType === 'ontology_projection_progress'
}

/**
 * 本体抽取 / 投影视图
 *
 * R1 抽取: { total_sources, completed_slices, slice_paths, validation, status }
 * R2 投影: { total_skills/pending_skill_count, completed_projections/projected_count, projection_paths, skipped_count, summary }
 */
export function OntologyExtractionView({ data, artifactType }: OntologyArtifactViewProps) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const isProjection = isProjectionType(artifactType)

  if (isProjection) {
    return <ProjectionView rec={rec} />
  }
  return <ExtractionView rec={rec} />
}

function ExtractionView({ rec }: { rec: Record<string, unknown> }) {
  const totalSources = Number(rec.total_sources ?? 0)
  const completedSlices = Number(rec.completed_slices ?? 0)
  const slicePaths = Array.isArray(rec.slice_paths)
    ? rec.slice_paths.filter((p): p is string => typeof p === 'string').map(toPublicPathLabel)
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
            {'✂️ '}结果 <b style={{ marginLeft: 3 }}>{completedSlices}</b>
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

      {/* 输出结果列表 */}
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

function ProjectionView({ rec }: { rec: Record<string, unknown> }) {
  const totalSkills = Number(rec.total_skills ?? rec.pending_skill_count ?? 0)
  const projectedCount = Number(rec.projected_count ?? rec.completed_projections ?? 0)
  const skippedCount = Number(rec.skipped_count ?? 0)
  const projectionPaths = Array.isArray(rec.projection_paths)
    ? rec.projection_paths.filter((p): p is string => typeof p === 'string').map(toPublicPathLabel)
    : []
  const summary = typeof rec.summary === 'string' ? rec.summary : ''
  const skippedSkills = Array.isArray(rec.skipped_skills)
    ? rec.skipped_skills.filter((s): s is string => typeof s === 'string')
    : []

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {/* 统计行 */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, flexWrap: 'wrap' }}>
        {totalSkills > 0 && (
          <span style={statChipStyle}>
            {'🧩 '}技能总数 <b style={{ marginLeft: 3 }}>{totalSkills}</b>
          </span>
        )}
        {projectedCount > 0 && (
          <span style={statChipStyle}>
            {'✓ '}已匹配 <b style={{ marginLeft: 3 }}>{projectedCount}</b>
          </span>
        )}
        {skippedCount > 0 && (
          <span style={{
            ...statChipStyle,
            background: 'rgba(245, 158, 11, 0.12)',
            border: '1px solid rgba(245, 158, 11, 0.25)',
          }}>
            {'⊘ '}跳过 <b style={{ marginLeft: 3 }}>{skippedCount}</b>
          </span>
        )}
      </div>

      {/* 跳过技能列表 */}
      {skippedSkills.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {skippedSkills.map((s, i) => (
            <div key={i} style={{
              display: 'flex', alignItems: 'center', gap: 6,
              padding: '3px 8px', borderRadius: 6,
              border: '1px solid var(--hb-border, #e5e7eb)',
              fontSize: 11, fontFamily: 'monospace',
              color: 'var(--hb-text-muted, #6b7280)',
            }}>
              <span style={{ opacity: 0.5, flexShrink: 0 }}>{'⊘'}</span>
              <span>{s}</span>
            </div>
          ))}
        </div>
      )}

      {/* 投影路径列表 */}
      {projectionPaths.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          {projectionPaths.map((p, i) => (
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

      {/* 摘要文本 */}
      {summary && (
        <div style={{ fontSize: 12, color: 'var(--hb-text-muted, #6b7280)', lineHeight: 1.5 }}>
          {summary}
        </div>
      )}
    </div>
  )
}
