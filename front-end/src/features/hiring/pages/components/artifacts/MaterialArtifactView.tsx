/**
 * MaterialArtifactView.tsx - 资料相关 Artifact 视图
 * 
 * 用于展示 material_collection_progress / material_handoff_summary 类型的 artifact
 */

import { CodeView } from './BaseArtifactViews'
import { asRecord, getRecordArray } from './utils/artifactHelpers'

interface MaterialArtifactViewProps {
  data: unknown
}

/**
 * 资料交接视图 - material_handoff_summary 专用视图
 */
export function MaterialHandoffView({ data }: MaterialArtifactViewProps) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const summary = typeof rec.summary === 'string' ? rec.summary : ''
  const items = getRecordArray(rec, 'items')

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
                  color: hasContent ? 'var(--hb-text-green, #059669)' : 'var(--hb-text-amber, #d97706)',
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
