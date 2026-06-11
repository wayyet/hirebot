/**
 * PackagingArtifactView.tsx - 打包相关 Artifact 视图
 * 
 * 用于展示打包和测试用例相关的 artifact
 */

import { CodeView } from './BaseArtifactViews'
import { asRecord, stringify } from './utils/artifactHelpers'
import { statChipStyle } from './utils/artifactStyles'

interface PackagingArtifactViewProps {
  artifactType: string
  data: unknown
}

/**
 * 打包测试用例状态视图 - packaging_testcases_ready / _progress / _done
 */
export function PackagingTestCasesStatusView({
  artifactType, data,
}: PackagingArtifactViewProps) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const summary = stringify(rec.summary ?? rec.message ?? '')
  const generatedCount = typeof rec.generated_count === 'number'
    ? rec.generated_count
    : typeof rec.testcase_count === 'number'
      ? rec.testcase_count
      : null

  const statusConfig: Record<string, { label: string; bg: string; color: string; border: string }> = {
    packaging_testcases_ready: {
      label: '等待确认',
      bg: 'rgba(245,158,11,0.10)', color: 'var(--hb-text-amber, #b45309)',
      border: 'rgba(245,158,11,0.30)',
    },
    packaging_testcases_progress: {
      label: '生成中',
      bg: 'rgba(37,99,235,0.10)', color: 'var(--hb-text-blue, #1d4ed8)',
      border: 'rgba(37,99,235,0.25)',
    },
    packaging_testcases_done: {
      label: '已完成',
      bg: 'rgba(16,185,129,0.10)', color: 'var(--hb-text-green, #059669)',
      border: 'rgba(16,185,129,0.30)',
    },
  }
  const st = statusConfig[artifactType] ?? statusConfig.packaging_testcases_ready

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
        {generatedCount !== null && (
          <span style={statChipStyle}>
            {'用例数'} <b style={{ marginLeft: 3 }}>{generatedCount}</b>
          </span>
        )}
        <span style={{
          fontSize: 11, padding: '2px 8px', borderRadius: 99, fontWeight: 600,
          background: st.bg, color: st.color, border: `1px solid ${st.border}`,
        }}>
          {st.label}
        </span>
      </div>
      {summary && (
        <div style={{ fontSize: 12, color: 'var(--hb-text, #374151)', lineHeight: 1.65 }}>
          {summary}
        </div>
      )}
    </div>
  )
}

/**
 * 打包前完整性审查状态视图 - review_readiness / _progress / _report
 */
export function PackageReviewStatusView({
  artifactType, data,
}: PackagingArtifactViewProps) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const summary = stringify(rec.summary ?? rec.message ?? '')
  const status = stringify(rec.status ?? '')
  const score = typeof rec.score_average === 'number' ? rec.score_average : null
  const p0Count = Array.isArray(rec.p0_blockers) ? rec.p0_blockers.length : null
  const p1Count = Array.isArray(rec.p1_warnings) ? rec.p1_warnings.length : null

  const statusConfig: Record<string, { label: string; bg: string; color: string; border: string }> = {
    review_readiness: {
      label: '等待确认',
      bg: 'rgba(245,158,11,0.10)', color: 'var(--hb-text-amber, #b45309)',
      border: 'rgba(245,158,11,0.30)',
    },
    review_progress: {
      label: '审查中',
      bg: 'rgba(37,99,235,0.10)', color: 'var(--hb-text-blue, #1d4ed8)',
      border: 'rgba(37,99,235,0.25)',
    },
    review_report: {
      label: status || '审查完成',
      bg: status === 'FAIL' ? 'rgba(239,68,68,0.10)' : 'rgba(16,185,129,0.10)',
      color: status === 'FAIL' ? 'var(--hb-danger, #dc2626)' : 'var(--hb-text-green, #059669)',
      border: status === 'FAIL' ? 'rgba(239,68,68,0.25)' : 'rgba(16,185,129,0.30)',
    },
  }
  const st = statusConfig[artifactType] ?? statusConfig.review_readiness

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
        {score !== null && (
          <span style={statChipStyle}>
            {'评分'} <b style={{ marginLeft: 3 }}>{score}</b>
          </span>
        )}
        {p0Count !== null && (
          <span style={statChipStyle}>
            {'P0'} <b style={{ marginLeft: 3 }}>{p0Count}</b>
          </span>
        )}
        {p1Count !== null && (
          <span style={statChipStyle}>
            {'P1'} <b style={{ marginLeft: 3 }}>{p1Count}</b>
          </span>
        )}
        <span style={{
          fontSize: 11, padding: '2px 8px', borderRadius: 99, fontWeight: 600,
          background: st.bg, color: st.color, border: `1px solid ${st.border}`,
        }}>
          {st.label}
        </span>
      </div>
      {summary && (
        <div style={{ fontSize: 12, color: 'var(--hb-text, #374151)', lineHeight: 1.65 }}>
          {summary}
        </div>
      )}
    </div>
  )
}

/**
 * 阶段4打包视图 - stage4_packaging
 */
export function Stage4PackagingView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  const status = typeof rec?.status === 'string' ? rec.status : ''
  const pendingSkills = Array.isArray(rec?.pending_downstream_skills)
    ? (rec!.pending_downstream_skills as unknown[]).filter((s): s is string => typeof s === 'string')
    : []
  const included = Array.isArray(rec?.included)
    ? (rec!.included as unknown[]).filter((s): s is string => typeof s === 'string')
    : []

  const statusLabel: Record<string, string> = {
    waiting_downstream: '等待下游完成',
    packing: '打包中',
    packaging: '打包中',
    done: '已完成',
    failed: '失败',
  }
  const statusColor: Record<string, string> = {
    waiting_downstream: 'var(--hb-text-amber, #b45309)',
    packing: 'var(--hb-text-blue, #1d4ed8)',
    packaging: 'var(--hb-text-blue, #1d4ed8)',
    done: 'var(--hb-text-green, #059669)',
    failed: 'var(--hb-danger, #dc2626)',
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      {status && (
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span style={{
            fontSize: 11, padding: '2px 10px', borderRadius: 99, fontWeight: 600,
            background: 'color-mix(in srgb, currentColor 12%, transparent)',
            color: statusColor[status] ?? 'var(--hb-text-muted, #6b7280)',
            border: `1px solid color-mix(in srgb, currentColor 25%, transparent)`,
          }}>
            {statusLabel[status] ?? status}
          </span>
        </div>
      )}
      {pendingSkills.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <div style={{ fontSize: 11, color: 'var(--hb-text-muted, #6b7280)', fontWeight: 600, letterSpacing: 0.3 }}>等待下游</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
            {pendingSkills.map((s, i) => (
              <span key={i} style={{
                fontSize: 11, padding: '2px 8px', borderRadius: 6,
                background: 'color-mix(in srgb, var(--hb-text-amber, #b45309) 10%, transparent)',
                color: 'var(--hb-text-amber, #b45309)',
                border: '1px solid color-mix(in srgb, var(--hb-text-amber, #b45309) 25%, transparent)',
              }}>{s}</span>
            ))}
          </div>
        </div>
      )}
      {included.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          <div style={{ fontSize: 11, color: 'var(--hb-text-muted, #6b7280)', fontWeight: 600, letterSpacing: 0.3 }}>包含内容</div>
          {included.map((path, i) => (
            <div key={i} style={{
              fontSize: 11, fontFamily: 'monospace',
              padding: '3px 8px', borderRadius: 5,
              background: 'var(--hb-surface-soft, #f9fafb)',
              border: '1px solid var(--hb-border, #e5e7eb)',
              color: 'var(--hb-text, #374151)',
            }}>{path}</div>
          ))}
        </div>
      )}
    </div>
  )
}
