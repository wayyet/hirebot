import type { CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import type { ArtifactDisplayData } from '../hiringPageTypes'

interface Props {
  artifact: ArtifactDisplayData
  /** 带 token 的文件下载回调；未提供时退化为直接 <a href> */
  onFileDownload?: (url: string, fileName: string, artifactType: string) => void
  /** 手动触发上传到系统（仅 template_package 展示） */
  onManualUpload?: (fileUrl: string, fileName: string) => void
  /** template_package：展示用 final 文件名（覆盖沙箱 artifact.fileName） */
  packageDownloadFileName?: string
  /** template_package：import 完成前禁用下载 */
  packageDownloadDisabled?: boolean
  /** 禁用时的 title / aria-label */
  packageDownloadDisabledTitle?: string
}

export function ArtifactMessageCard({
  artifact,
  onFileDownload,
  onManualUpload,
  packageDownloadFileName,
  packageDownloadDisabled = false,
  packageDownloadDisabledTitle,
}: Props) {
  const { t } = useTranslation()
  const title = artifact.label ?? artifact.artifactType
  const isPackage = artifact.artifactType === 'template_package'
  const displayFileName =
    isPackage && packageDownloadFileName
      ? packageDownloadFileName
      : (artifact.fileName ?? title)

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
        <div className="hb-artifact-file-row">
          {onFileDownload && artifact.fileUrl ? (
            // template_package：仅下载后端 final；import 前禁用，不走沙箱网关
            <button
              type="button"
              className="hb-artifact-file-link"
              disabled={isPackage && packageDownloadDisabled}
              title={isPackage && packageDownloadDisabled ? packageDownloadDisabledTitle : undefined}
              aria-label={
                isPackage && packageDownloadDisabled && packageDownloadDisabledTitle
                  ? packageDownloadDisabledTitle
                  : displayFileName
              }
              onClick={() => {
                if (isPackage && packageDownloadDisabled) return
                onFileDownload(artifact.fileUrl!, displayFileName, artifact.artifactType)
              }}
            >
              <span className="hb-artifact-file-name">{displayFileName}</span>
              {artifact.sizeLabel && <span className="hb-artifact-file-size">{artifact.sizeLabel}</span>}
            </button>
          ) : (
            <a
              href={artifact.fileUrl ?? '#'}
              download={displayFileName}
              className="hb-artifact-file-link"
            >
              <span className="hb-artifact-file-name">{displayFileName}</span>
              {artifact.sizeLabel && <span className="hb-artifact-file-size">{artifact.sizeLabel}</span>}
            </a>
          )}
          {isPackage && onManualUpload && artifact.fileUrl && (
            <button
              type="button"
              className="hb-artifact-action-btn"
              onClick={() => onManualUpload(artifact.fileUrl!, artifact.fileName ?? title)}
            >
              {t('hiring.artifact.manualImport')}
            </button>
          )}
        </div>
      ) : (
        <ArtifactDataView artifact={artifact} />
      )}
    </div>
  )
}

function ArtifactDataView({ artifact }: { artifact: ArtifactDisplayData }) {
  // 类型优先：特定 artifactType 使用内置专用视图
  if (artifact.artifactType === 'material_collection_progress' || artifact.artifactType === 'material_handoff_summary') {
    return <MaterialHandoffView data={artifact.data} />
  }
  if (artifact.artifactType === 'ontology_extraction_done' || artifact.artifactType === 'ontology_extraction_progress') {
    return <OntologyExtractionView data={artifact.data} />
  }
  if (artifact.artifactType === 'skill_workorder_progress' || artifact.artifactType === 'skill_workorder_summary') {
    return <SkillWorkorderSummaryView data={artifact.data} />
  }
  if (artifact.artifactType === 'external_workorder_progress' || artifact.artifactType === 'external_workorder_summary') {
    return <ExternalWorkorderSummaryView data={artifact.data} />
  }
  if (
    artifact.artifactType === 'skill_generation_ready' ||
    artifact.artifactType === 'skill_generation_progress' ||
    artifact.artifactType === 'skill_generation_done'
  ) {
    return <SkillGenerationStatusView artifactType={artifact.artifactType} data={artifact.data} />
  }
  if (
    artifact.artifactType === 'packaging_testcases_ready' ||
    artifact.artifactType === 'packaging_testcases_progress' ||
    artifact.artifactType === 'packaging_testcases_done'
  ) {
    return <PackagingTestCasesStatusView artifactType={artifact.artifactType} data={artifact.data} />
  }
  if (artifact.artifactType === 'external_config_committed') {
    return <ExternalConfigCommittedView data={artifact.data} />
  }
  if (artifact.artifactType === 'stage4_packaging') return <Stage4PackagingView data={artifact.data} />
  // 结构化兜底：未命中类型但数据具备对应特征时自动使用专用视图
  const _d = asRecord(artifact.data)
  if (_d && hasExternalWorkorderShape(_d)) {
    return <ExternalWorkorderSummaryView data={artifact.data} />
  }
  if (_d && hasSkillWorkorderShape(_d)) {
    return <SkillWorkorderSummaryView data={artifact.data} />
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

function BadgeView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  const value = stringify(rec?.value ?? rec?.status ?? sanitizeArtifactDataForDisplay(data))
  return (
    <span className="hb-artifact-badge">{value}</span>
  )
}

function CodeView({ data }: { data: unknown }) {
  const sanitized = sanitizeArtifactDataForDisplay(data)
  return (
    <pre style={codeStyle}>
      <code>{typeof sanitized === 'string' ? sanitized : JSON.stringify(sanitized, null, 2)}</code>
    </pre>
  )
}

function TextView({ data }: { data: unknown }) {
  return <div style={{ fontSize: 13, whiteSpace: 'pre-wrap' }}>{stringify(sanitizeArtifactDataForDisplay(data))}</div>
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

/** skill_generation_ready / _progress / _done 轻量状态视图 */
function SkillGenerationStatusView({
  artifactType, data,
}: { artifactType: string; data: unknown }) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const summary = typeof rec.summary === 'string' ? rec.summary : ''
  const totalSkills = typeof rec.total_skills === 'number' ? rec.total_skills : null

  const statusConfig: Record<string, { label: string; bg: string; color: string; border: string }> = {
    skill_generation_ready: {
      label: '☕ 等待确认',
      bg: 'rgba(245,158,11,0.10)', color: 'var(--hb-text-amber, #b45309)',
      border: 'rgba(245,158,11,0.30)',
    },
    skill_generation_progress: {
      label: '⏳ 生成中',
      bg: 'rgba(37,99,235,0.10)', color: 'var(--hb-text-blue, #1d4ed8)',
      border: 'rgba(37,99,235,0.25)',
    },
    skill_generation_done: {
      label: '✓ 已完成',
      bg: 'rgba(16,185,129,0.10)', color: 'var(--hb-text-green, #059669)',
      border: 'rgba(16,185,129,0.30)',
    },
  }
  const st = statusConfig[artifactType] ?? statusConfig.skill_generation_ready

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
        {totalSkills !== null && (
          <span style={statChipStyle}>
            {'🧩 技能数'} <b style={{ marginLeft: 3 }}>{totalSkills}</b>
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

/** packaging_testcases_ready / _progress / _done 轻量状态视图 */
function PackagingTestCasesStatusView({
  artifactType, data,
}: { artifactType: string; data: unknown }) {
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

/** skill_workorder_summary 专用结构化视图 */
function SkillWorkorderSummaryView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const summary = typeof rec.summary === 'string' ? rec.summary : ''
  const notes = typeof rec.notes === 'string' ? rec.notes : ''
  const skills = getRecordArray(rec, 'items', 'skills')

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {/* notes 优先展示（补充说明/变更说明） */}
      {notes && (
        <div style={{
          display: 'flex', gap: 6, fontSize: 12,
          padding: '6px 9px', borderRadius: 6, lineHeight: 1.55,
          border: '1px solid rgba(37,99,235,0.22)',
          background: 'rgba(37,99,235,0.06)', color: 'var(--hb-text-blue, #1d4ed8)',
        }}>
          <span style={{ flexShrink: 0 }}>📌</span>
          <span>{notes}</span>
        </div>
      )}
      {summary && (
        <div style={{ fontSize: 12, color: 'var(--hb-text-muted, #6b7280)', lineHeight: 1.6 }}>
          {summary}
        </div>
      )}
      {skills.map((skill, i) => (
        <SkillCard key={i} skill={skill} />
      ))}
    </div>
  )
}

function SkillCard({ skill }: { skill: Record<string, unknown> }) {
  const name = firstString(
    skill.display_name,
    skill.skill_name,
    skill.title,
    skill.name,
    skill.skill_slug,
    skill.skillSlug,
  )
  const description = firstString(skill.description, skill.summary)
  const generationAction = firstString(skill.generation_action, skill.generationAction, skill.action)
  const trigger = stringListText(skill.triggers ?? skill.trigger)
  const expectedOutput = stringListText(skill.expected_outputs ?? skill.expected_output ?? skill.expectedOutput)
  const boundaries = Array.isArray(skill.boundaries)
    ? skill.boundaries.filter((b): b is string => typeof b === 'string')
    : []
  const openQuestions = Array.isArray(skill.open_questions)
    ? skill.open_questions.filter((q): q is string => typeof q === 'string')
    : []
  const params = asRecord(skill.parameters)
  const deps = asRecord(skill.dependencies)
  const materials = Array.isArray(deps?.materials)
    ? deps!.materials.filter((m): m is string => typeof m === 'string').map(toPublicPathLabel)
    : []
  const ontologySlices = Array.isArray(deps?.ontology_slices)
    ? deps!.ontology_slices.filter((s): s is string => typeof s === 'string').map(toPublicPathLabel)
    : []
  const thresholds = Array.isArray(params?.default_thresholds)
    ? params!.default_thresholds.filter(isRecord)
    : []
  const paramChips = params
    ? Object.entries(params)
        .filter(([k, v]) => k !== 'default_thresholds' && (typeof v === 'string' || typeof v === 'number'))
        .slice(0, 4)
    : []
  const actionLabel: Record<string, string> = {
    generate_new: '新生成', update: '更新', extend: '扩展',
    generated: '已生成', tbd: '待确认', pending: '待决',
  }
  // 按 action 类型选择 badge 颜色
  const actionColor: Record<string, { bg: string; color: string; border: string }> = {
    generated:  { bg: 'rgba(16,185,129,0.10)',  color: 'var(--hb-text-green, #059669)',  border: 'rgba(16,185,129,0.28)' },
    tbd:        { bg: 'rgba(245,158,11,0.10)',  color: 'var(--hb-text-amber, #b45309)',  border: 'rgba(245,158,11,0.28)' },
    pending:    { bg: 'rgba(245,158,11,0.10)',  color: 'var(--hb-text-amber, #b45309)',  border: 'rgba(245,158,11,0.28)' },
    generate_new: { bg: 'rgba(37,99,235,0.10)', color: 'var(--hb-text-blue, #1d4ed8)',  border: 'rgba(37,99,235,0.25)' },
    update:     { bg: 'rgba(37,99,235,0.10)',  color: 'var(--hb-text-blue, #1d4ed8)',   border: 'rgba(37,99,235,0.25)' },
    extend:     { bg: 'rgba(37,99,235,0.10)',  color: 'var(--hb-text-blue, #1d4ed8)',   border: 'rgba(37,99,235,0.25)' },
  }
  const ac = actionColor[generationAction] ?? { bg: 'rgba(37,99,235,0.10)', color: 'var(--hb-text-blue, #1d4ed8)', border: 'rgba(37,99,235,0.25)' }

  return (
    <div style={{
      display: 'flex', flexDirection: 'column', gap: 8,
      padding: '10px 12px', borderRadius: 8,
      border: generationAction === 'tbd'
        ? '1px dashed rgba(245,158,11,0.45)'
        : '1px solid var(--hb-border, #e5e7eb)',
      background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 40%, transparent)',
      opacity: generationAction === 'tbd' ? 0.82 : 1,
    }}>
      {/* 技能名称 + action badge */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <span style={{ fontSize: 13, fontWeight: 700 }}>{name}</span>
        {generationAction && (
          <span style={{
            fontSize: 10, padding: '1px 7px', borderRadius: 99, fontWeight: 600,
            background: ac.bg, color: ac.color,
            border: `1px solid ${ac.border}`,
          }}>
            {actionLabel[generationAction] ?? generationAction}
          </span>
        )}
      </div>

      {/* 描述 */}
      {description && (
        <div style={{ fontSize: 12, color: 'var(--hb-text, #374151)', lineHeight: 1.7 }}>
          {description}
        </div>
      )}

      {/* 触发条件 / 预期输出 */}
      {(trigger || expectedOutput) && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 5 }}>
          {trigger && <SkillSectionRow label="触发条件" text={trigger} />}
          {expectedOutput && <SkillSectionRow label="预期输出" text={expectedOutput} />}
        </div>
      )}

      {/* 限制条件 */}
      {boundaries.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          <div style={sectionLabelStyle}>限制条件</div>
          {boundaries.map((b, i) => (
            <div key={i} style={{ display: 'flex', gap: 6, fontSize: 12, lineHeight: 1.5 }}>
              <span style={{ flexShrink: 0, color: 'var(--hb-text-amber, #f59e0b)' }}>•</span>
              <span style={{ color: 'var(--hb-text-muted, #6b7280)' }}>{b}</span>
            </div>
          ))}
        </div>
      )}

      {/* 参数配置 chips */}
      {paramChips.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <div style={sectionLabelStyle}>参数配置</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 5 }}>
            {paramChips.map(([k, v]) => (
              <span key={k} style={{
                fontSize: 11, padding: '2px 7px', borderRadius: 6,
                border: '1px solid var(--hb-border, #e5e7eb)',
                background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 60%, transparent)',
                color: 'var(--hb-text-muted, #6b7280)',
              }}>
                <span style={{ opacity: 0.7 }}>{k}: </span>
                <b>{String(v)}</b>
              </span>
            ))}
          </div>
        </div>
      )}

      {/* 阈值配置 mini-table */}
      {thresholds.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <div style={sectionLabelStyle}>默认阈值</div>
          <div style={{ overflowX: 'auto', borderRadius: 6, border: '1px solid var(--hb-border, #e5e7eb)' }}>
            <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 11 }}>
              <thead>
                <tr>
                  {['指标', '规则', '级别'].map(col => (
                    <th key={col} style={thresholdCellStyle(true)}>{col}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {thresholds.map((row, i) => (
                  <tr key={i}>
                    <td style={thresholdCellStyle(false)}>{stringify(row.metric)}</td>
                    <td style={thresholdCellStyle(false)}>{stringify(row.rule)}</td>
                    <td style={thresholdCellStyle(false)}>
                      <span style={{
                        padding: '1px 5px', borderRadius: 99, fontSize: 10, fontWeight: 600,
                        background: row.severity === 'warning' ? 'rgba(239,68,68,0.12)' : 'rgba(245,158,11,0.12)',
                        color: row.severity === 'warning' ? 'var(--hb-danger, #dc2626)' : 'var(--hb-text-amber, #d97706)',
                      }}>
                        {stringify(row.severity)}
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {/* 依赖材料 & 本体切片 */}
      {(materials.length > 0 || ontologySlices.length > 0) && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <div style={sectionLabelStyle}>依赖材料 &amp; 本体切片</div>
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
            {materials.map((m, i) => (
              <span key={i} style={{
                fontSize: 11, padding: '2px 7px', borderRadius: 6,
                border: '1px solid rgba(16,185,129,0.25)',
                background: 'rgba(16,185,129,0.08)', color: 'var(--hb-text-green, #059669)',
              }}>📁 {m}</span>
            ))}
            {ontologySlices.map((s, i) => (
              <span key={i} style={{
                fontSize: 11, padding: '2px 7px', borderRadius: 6, fontFamily: 'monospace',
                border: '1px solid rgba(124,58,237,0.25)',
                background: 'rgba(124,58,237,0.08)', color: 'var(--hb-text-purple, #7c3aed)',
                overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', maxWidth: '100%',
              }}>🌿 {s}</span>
            ))}
          </div>
        </div>
      )}

      {/* 待确认问题 */}
      {openQuestions.length > 0 && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
          <div style={sectionLabelStyle}>待确认问题</div>
          {openQuestions.map((q, i) => (
            <div key={i} style={{
              display: 'flex', gap: 6, fontSize: 11,
              padding: '5px 8px', borderRadius: 6, lineHeight: 1.5,
              border: '1px solid rgba(245,158,11,0.30)',
              background: 'rgba(245,158,11,0.08)', color: 'var(--hb-text-amber, #b45309)',
            }}>
              <span style={{ flexShrink: 0 }}>⚠</span>
              <span>{q}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

function SkillSectionRow({ label, text }: { label: string; text: string }) {
  return (
    <div style={{ display: 'flex', gap: 6, fontSize: 12 }}>
      <span style={{ flexShrink: 0, fontWeight: 600, color: 'var(--hb-text-muted, #6b7280)', minWidth: 52 }}>{label}</span>
      <span style={{ color: 'var(--hb-text, #374151)', lineHeight: 1.55 }}>{text}</span>
    </div>
  )
}

const sectionLabelStyle: CSSProperties = {
  fontSize: 11, fontWeight: 600, letterSpacing: 0.4,
  textTransform: 'uppercase', color: 'var(--hb-text-muted, #9ca3af)',
}

function thresholdCellStyle(header: boolean): CSSProperties {
  return {
    padding: '5px 8px', textAlign: 'left', fontSize: 11,
    borderBottom: '1px solid var(--hb-border, #e5e7eb)',
    fontWeight: header ? 700 : 400,
    background: header ? 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 80%, transparent)' : 'transparent',
    whiteSpace: 'nowrap',
  }
}

const statChipStyle: CSSProperties = {
  display: 'inline-flex', alignItems: 'center',
  fontSize: 11, padding: '2px 8px', borderRadius: 99,
  border: '1px solid var(--hb-border, #e5e7eb)',
  background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 60%, transparent)',
  color: 'var(--hb-text-muted, #6b7280)',
}

/** external_workorder_summary 外部能力需求摘要视图 */
function ExternalWorkorderSummaryView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const summary = typeof rec.summary === 'string' ? rec.summary : ''
  const capabilities = getRecordArray(rec, 'external_capabilities', 'items')
  const totalCapabilities = Number(rec.total_capabilities ?? rec.collected_count ?? capabilities.length)

  const realCaps = capabilities.filter(c => c.kind !== 'skip' && c.category !== 'skip')
  const allSkipped = capabilities.length > 0 && realCaps.length === 0

  const kindIcon: Record<string, string> = {
    skip: '⊘', read: '📖', write: '✏️', notify: '🔔',
    search: '🔍', transform: '⚙️', webhook: '🔗', api: '🌐',
  }
  const kindColor: Record<string, { bg: string; color: string; border: string }> = {
    skip:      { bg: 'rgba(107,114,128,0.07)', color: 'var(--hb-text-muted, #6b7280)',    border: 'rgba(107,114,128,0.20)' },
    read:      { bg: 'rgba(16,185,129,0.08)',  color: 'var(--hb-text-green, #059669)',    border: 'rgba(16,185,129,0.22)'  },
    write:     { bg: 'rgba(37,99,235,0.08)',   color: 'var(--hb-text-blue, #1d4ed8)',     border: 'rgba(37,99,235,0.22)'   },
    notify:    { bg: 'rgba(245,158,11,0.08)',  color: 'var(--hb-text-amber, #b45309)',    border: 'rgba(245,158,11,0.22)'  },
    search:    { bg: 'rgba(124,58,237,0.08)',  color: 'var(--hb-text-purple, #7c3aed)',   border: 'rgba(124,58,237,0.22)'  },
    transform: { bg: 'rgba(79,70,229,0.08)',   color: 'var(--hb-text-blue, #1d4ed8)',     border: 'rgba(79,70,229,0.22)'   },
  }

  // 全部跳过 — 空状态展示
  if (allSkipped) {
    const skipCap = capabilities[0]
    const linkedSkills = Array.isArray(skipCap?.linked_skills)
      ? (skipCap!.linked_skills as unknown[]).filter((s): s is string => typeof s === 'string')
      : []
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
        <div style={{
          display: 'flex', alignItems: 'flex-start', gap: 9,
          padding: '10px 12px', borderRadius: 8,
          border: '1px dashed var(--hb-border, #e5e7eb)',
          background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 50%, transparent)',
        }}>
          <span style={{ fontSize: 20, lineHeight: 1, marginTop: 1 }}>⊘</span>
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 12, fontWeight: 600, color: 'var(--hb-text, #374151)' }}>
              未配置外部系统
            </div>
            {skipCap?.objective != null && (
              <div style={{ fontSize: 11, color: 'var(--hb-text-muted, #6b7280)', marginTop: 2, lineHeight: 1.5 }}>
                {String(skipCap.objective)}
              </div>
            )}
          </div>
          <span style={{
            flexShrink: 0, fontSize: 10, padding: '2px 8px', borderRadius: 99, fontWeight: 600,
            background: 'rgba(107,114,128,0.08)', color: 'var(--hb-text-muted, #6b7280)',
            border: '1px solid rgba(107,114,128,0.20)',
          }}>已跳过</span>
        </div>
        {linkedSkills.length > 0 && (
          <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 4, paddingLeft: 2 }}>
            <span style={{ ...sectionLabelStyle, marginRight: 2 }}>关联技能</span>
            {linkedSkills.map((s, i) => (
              <span key={i} style={{
                fontSize: 11, padding: '1px 7px', borderRadius: 6,
                border: '1px solid rgba(37,99,235,0.20)',
                background: 'rgba(37,99,235,0.06)', color: 'var(--hb-text-blue, #1d4ed8)',
              }}>🧩 {s}</span>
            ))}
          </div>
        )}
        {summary && (
          <div style={{ fontSize: 11, color: 'var(--hb-text-muted, #6b7280)', lineHeight: 1.55, paddingLeft: 2 }}>
            {summary}
          </div>
        )}
      </div>
    )
  }

  // 有实际能力配置 — 逐条渲染
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        {totalCapabilities > 0 && (
          <span style={statChipStyle}>🔌 能力数 <b style={{ marginLeft: 3 }}>{totalCapabilities}</b></span>
        )}
        {summary && (
          <div style={{ fontSize: 12, color: 'var(--hb-text-muted, #6b7280)', lineHeight: 1.5, flex: 1 }}>
            {summary}
          </div>
        )}
      </div>
      {capabilities.map((cap, i) => {
        const kind = String(cap.kind ?? cap.category ?? 'api')
        const kc = kindColor[kind] ?? kindColor.read
        const objective = String(cap.objective ?? '')
        const targetSystem = firstString(cap.target_system, cap.display_name, cap.name)
        const methods = Array.isArray(cap.integration_methods)
          ? cap.integration_methods.filter((m): m is string => typeof m === 'string').filter(m => m !== 'none')
          : []
        const linkedSkills = Array.isArray(cap.linked_skills)
          ? cap.linked_skills.filter((s): s is string => typeof s === 'string')
          : []
        const authKind = cap.auth_kind != null ? String(cap.auth_kind) : ''
        const requiredFields = Array.isArray(cap.required_fields)
          ? cap.required_fields.filter((f): f is string => typeof f === 'string')
          : []
        return (
          <div key={i} style={{
            display: 'flex', flexDirection: 'column', gap: 6,
            padding: '9px 11px', borderRadius: 8,
            border: `1px solid ${kc.border}`,
            background: kc.bg,
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 7, flexWrap: 'wrap' }}>
              <span style={{ fontSize: 14 }}>{kindIcon[kind] ?? '🔌'}</span>
              <span style={{ fontSize: 12, fontWeight: 700, color: 'var(--hb-text, #374151)', flex: 1, minWidth: 0 }}>
                {targetSystem || objective || kind}
              </span>
              <span style={{
                fontSize: 10, padding: '1px 7px', borderRadius: 99, fontWeight: 600,
                background: kc.bg, color: kc.color, border: `1px solid ${kc.border}`,
              }}>{kind}</span>
              {authKind && authKind !== 'none' && (
                <span style={{
                  fontSize: 10, padding: '1px 6px', borderRadius: 99,
                  border: '1px solid var(--hb-border, #e5e7eb)',
                  color: 'var(--hb-text-muted, #6b7280)',
                }}>🔑 {authKind}</span>
              )}
            </div>
            {targetSystem && objective && (
              <div style={{ fontSize: 12, color: 'var(--hb-text, #374151)', lineHeight: 1.55 }}>
                {objective}
              </div>
            )}
            {methods.length > 0 && (
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                {methods.map((m, mi) => (
                  <span key={mi} style={{
                    fontSize: 10, padding: '1px 6px', borderRadius: 6,
                    border: '1px solid var(--hb-border, #e5e7eb)',
                    color: 'var(--hb-text-muted, #6b7280)',
                    background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 60%, transparent)',
                  }}>{m}</span>
                ))}
              </div>
            )}
            {linkedSkills.length > 0 && (
              <div style={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 4 }}>
                <span style={{ ...sectionLabelStyle, marginRight: 2 }}>技能</span>
                {linkedSkills.map((s, si) => (
                  <span key={si} style={{
                    fontSize: 11, padding: '1px 6px', borderRadius: 6,
                    border: '1px solid rgba(37,99,235,0.20)',
                    background: 'rgba(37,99,235,0.06)', color: 'var(--hb-text-blue, #1d4ed8)',
                  }}>🧩 {s}</span>
                ))}
              </div>
            )}
            {requiredFields.length > 0 && (
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: 3 }}>
                {requiredFields.map((f, fi) => (
                  <span key={fi} style={{
                    fontSize: 10, padding: '1px 5px', borderRadius: 4, fontFamily: 'monospace',
                    border: '1px solid var(--hb-border, #e5e7eb)',
                    color: 'var(--hb-text-muted, #6b7280)',
                  }}>{f}</span>
                ))}
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

/** external_config_committed 系统提交成功视图 */
function ExternalConfigCommittedView({ data }: { data: unknown }) {
  const rec = asRecord(data)
  if (!rec) return <CodeView data={data} />

  const submissionMode = typeof rec.submissionMode === 'string' ? rec.submissionMode : 'pending'
  const updatedAtUtc = typeof rec.updatedAtUtc === 'string' ? rec.updatedAtUtc : ''
  const cliTools = Array.isArray(rec.cliTools) ? rec.cliTools.filter(isRecord) : []
  const mcpServer = asRecord(rec.mcpServer)
  const isSkipped = submissionMode === 'skipped'
  const timeLabel = updatedAtUtc
    ? new Date(updatedAtUtc).toLocaleString('zh-CN', { hour12: false })
    : ''

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
        <span style={statChipStyle}>
          {'🔐 '}提交结果 <b style={{ marginLeft: 3 }}>{isSkipped ? '已跳过' : '已保存'}</b>
        </span>
        {timeLabel && (
          <span style={statChipStyle}>
            {'🕒 '}更新时间 <b style={{ marginLeft: 3 }}>{timeLabel}</b>
          </span>
        )}
      </div>

      {isSkipped ? (
        <div style={{
          display: 'flex', gap: 8,
          padding: '10px 12px', borderRadius: 8,
          border: '1px dashed var(--hb-border, #e5e7eb)',
          background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 50%, transparent)',
          fontSize: 12, color: 'var(--hb-text-muted, #6b7280)', lineHeight: 1.6,
        }}>
          <span style={{ flexShrink: 0 }}>⊘</span>
          <span>当前雇佣流程已明确无需对接外部系统，系统已提交跳过结果并可继续后续打包。</span>
        </div>
      ) : (
        <>
          {cliTools.length > 0 && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
              <div style={sectionLabelStyle}>CLI 工具</div>
              {cliTools.map((tool, index) => (
                <div key={index} style={{
                  display: 'flex', flexDirection: 'column', gap: 3,
                  padding: '8px 10px', borderRadius: 8,
                  border: '1px solid var(--hb-border, #e5e7eb)',
                  background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 50%, transparent)',
                }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                    <div className="hb-todo-truncate" title={stringify(tool.name)} style={{ fontWeight: 500, fontSize: 12 }}>
                      {stringify(tool.name)}
                    </div>
                    <div style={{
                      fontSize: 11, color: 'var(--hb-text-muted, #6b7280)',
                      background: 'var(--hb-surface-2, #f3f4f6)',
                      padding: '1px 6px', borderRadius: 4,
                    }}>
                      {stringify(tool.executionMode) || 'direct'}
                    </div>
                  </div>
                  {tool.command != null && String(tool.command).trim().length > 0 && (
                    <code style={{
                      fontSize: 11,
                      fontFamily: 'JetBrains Mono, Consolas, monospace',
                      color: 'var(--hb-text-muted, #6b7280)',
                    }}>
                      {String(tool.command)}
                    </code>
                  )}
                  {/* 可展开详情：参数 Schema 与描述 */}
                  <details style={{ marginTop: 4 }}>
                    <summary style={{
                      fontSize: 11,
                      color: 'var(--hb-text-muted, #6b7280)',
                      cursor: 'pointer',
                      userSelect: 'none',
                    }}>
                      查看详情
                    </summary>
                    <div style={{
                      marginTop: 6, padding: '8px 10px', borderRadius: 6,
                      background: 'var(--hb-surface-2, #f3f4f6)',
                      fontSize: 11, lineHeight: 1.6,
                    }}>
                      {tool.parameters != null && (
                        <div>
                          <div style={{ fontWeight: 500, marginBottom: 2 }}>参数 Schema:</div>
                          <pre style={{
                            margin: 0, whiteSpace: 'pre-wrap', wordBreak: 'break-all',
                            fontFamily: 'JetBrains Mono, Consolas, monospace', fontSize: 10,
                          }}>
                            {typeof tool.parameters === 'string'
                              ? tool.parameters
                              : JSON.stringify(tool.parameters, null, 2)}
                          </pre>
                        </div>
                      )}
                      {tool.description != null && String(tool.description).trim().length > 0 && (
                        <div style={{ marginTop: 4 }}>
                          <span style={{ fontWeight: 500 }}>描述: </span>
                          <span>{String(tool.description)}</span>
                        </div>
                      )}
                    </div>
                  </details>
                </div>
              ))}
            </div>
          )}

          {mcpServer && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
              <div style={sectionLabelStyle}>MCP</div>
              <div style={{
                display: 'flex', flexDirection: 'column', gap: 3,
                padding: '8px 10px', borderRadius: 8,
                border: '1px solid var(--hb-border, #e5e7eb)',
                background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 50%, transparent)',
                fontSize: 12,
              }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                  <div
                    className="hb-todo-truncate"
                    title={stringify(mcpServer.name) || 'MCP Server'}
                    style={{ fontWeight: 600 }}
                  >
                    {stringify(mcpServer.name) || 'MCP Server'}
                  </div>
                  <div style={{
                    fontSize: 11, color: 'var(--hb-text-muted, #6b7280)',
                    background: 'var(--hb-surface-2, #f3f4f6)',
                    padding: '1px 6px', borderRadius: 4,
                  }}>
                    {stringify(mcpServer.transport) || 'http'}
                  </div>
                </div>
                {mcpServer.command != null && String(mcpServer.command).trim().length > 0 && (
                  <code style={{
                    fontSize: 11,
                    fontFamily: 'JetBrains Mono, Consolas, monospace',
                    color: 'var(--hb-text-muted, #6b7280)',
                  }}>
                    {String(mcpServer.command)}
                  </code>
                )}
                {mcpServer.url != null && String(mcpServer.url).trim().length > 0 && (
                  <code style={{
                    fontSize: 11,
                    fontFamily: 'JetBrains Mono, Consolas, monospace',
                    color: 'var(--hb-text-muted, #6b7280)',
                  }}>
                    {String(mcpServer.url)}
                  </code>
                )}
                {/* 可展开详情：环境变量、请求头、启动参数等 */}
                <details style={{ marginTop: 4 }}>
                  <summary style={{
                    fontSize: 11,
                    color: 'var(--hb-text-muted, #6b7280)',
                    cursor: 'pointer',
                    userSelect: 'none',
                  }}>
                    查看详情
                  </summary>
                  <div style={{
                    marginTop: 6, padding: '8px 10px', borderRadius: 6,
                    background: 'var(--hb-surface-2, #f3f4f6)',
                    fontSize: 11, lineHeight: 1.6,
                  }}>
                    {mcpServer.env != null && typeof mcpServer.env === 'object' && (
                      <div>
                        <span style={{ fontWeight: 500 }}>环境变量: </span>
                        <span>{Object.keys(mcpServer.env as Record<string, unknown>).length} 项</span>
                      </div>
                    )}
                    {mcpServer.headers != null && typeof mcpServer.headers === 'object' && (
                      <div>
                        <span style={{ fontWeight: 500 }}>请求头: </span>
                        <span>{Object.keys(mcpServer.headers as Record<string, unknown>).length} 项</span>
                      </div>
                    )}
                    {mcpServer.headersFromEnv != null && typeof mcpServer.headersFromEnv === 'object' && (
                      <div>
                        <span style={{ fontWeight: 500 }}>环境变量映射请求头: </span>
                        <span>{Object.keys(mcpServer.headersFromEnv as Record<string, unknown>).length} 项</span>
                      </div>
                    )}
                    {mcpServer.bearerTokenEnv != null && String(mcpServer.bearerTokenEnv).trim().length > 0 && (
                      <div>
                        <span style={{ fontWeight: 500 }}>Bearer Token 环境变量: </span>
                        <code style={{ fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
                          {String(mcpServer.bearerTokenEnv)}
                        </code>
                      </div>
                    )}
                    {Array.isArray(mcpServer.args) && (mcpServer.args as unknown[]).length > 0 && (
                      <div>
                        <span style={{ fontWeight: 500 }}>启动参数: </span>
                        <code style={{ fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
                          {(mcpServer.args as unknown[]).map((a) => String(a)).join(' ')}
                        </code>
                      </div>
                    )}
                    {mcpServer.cwd != null && String(mcpServer.cwd).trim().length > 0 && (
                      <div>
                        <span style={{ fontWeight: 500 }}>工作目录: </span>
                        <code style={{ fontFamily: 'JetBrains Mono, Consolas, monospace' }}>
                          {String(mcpServer.cwd)}
                        </code>
                      </div>
                    )}
                  </div>
                </details>
              </div>
            </div>
          )}
        </>
      )}
    </div>
  )
}

/** stage4_packaging 专用状态视图 */
function Stage4PackagingView({ data }: { data: unknown }) {
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
    packaging: '打包中',
    done: '已完成',
    failed: '失败',
  }
  const statusColor: Record<string, string> = {
    waiting_downstream: 'var(--hb-text-amber, #b45309)',
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

/** material_handoff_summary 专用结构化视图 */
function MaterialHandoffView({ data }: { data: unknown }) {
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

function ArtifactIcon({ artifact }: { artifact: ArtifactDisplayData }) {
  if (artifact.kind === 'file') return <span className="hb-artifact-icon">📄</span>
  if (artifact.artifactType === 'skill_workorder_progress' || artifact.artifactType === 'skill_workorder_summary') return <span className="hb-artifact-icon">🧩</span>
  if (
    artifact.artifactType === 'skill_generation_ready' ||
    artifact.artifactType === 'skill_generation_progress' ||
    artifact.artifactType === 'skill_generation_done'
  ) return <span className="hb-artifact-icon">⚙️</span>
    if (
        artifact.artifactType === 'packaging_testcases_ready' ||
        artifact.artifactType === 'packaging_testcases_progress' ||
        artifact.artifactType === 'packaging_testcases_done'
    ) return <span className="hb-artifact-icon">🧪</span>
    if (artifact.artifactType === 'material_collection_progress' || artifact.artifactType === 'material_handoff_summary') return <span className="hb-artifact-icon">📋</span>
  if (artifact.artifactType === 'ontology_extraction_done' || artifact.artifactType === 'ontology_extraction_progress') return <span className="hb-artifact-icon">🌿</span>
  if (artifact.artifactType === 'external_workorder_progress' || artifact.artifactType === 'external_workorder_summary' || artifact.artifactType === 'external_config_committed') return <span className="hb-artifact-icon">🔌</span>
  if (artifact.artifactType === 'stage4_packaging') return <span className="hb-artifact-icon">📦</span>
  const map: Record<string, string> = { table: '📊', code: '💻', tree: '🌿', badge: '✅', progress: '⏳' }
  return <span className="hb-artifact-icon">{map[artifact.displayHint ?? ''] ?? '📦'}</span>
}

function isRecord(v: unknown): v is Record<string, unknown> {
  return !!v && typeof v === 'object' && !Array.isArray(v)
}
function asRecord(v: unknown): Record<string, unknown> | null {
  return isRecord(v) ? v : null
}
function getRecordArray(record: Record<string, unknown>, ...keys: string[]): Record<string, unknown>[] {
  for (const key of keys) {
    const value = record[key]
    if (Array.isArray(value)) {
      return value.filter(isRecord)
    }
  }

  return []
}
function firstString(...values: unknown[]): string {
  for (const value of values) {
    if (typeof value === 'string' && value.trim()) {
      return value.trim()
    }
  }

  return ''
}
function stringListText(value: unknown): string {
  if (Array.isArray(value)) {
    return value
      .filter((item): item is string => typeof item === 'string' && item.trim().length > 0)
      .join('；')
  }

  return firstString(value)
}
function hasSkillWorkorderShape(record: Record<string, unknown>): boolean {
  const items = getRecordArray(record, 'items', 'skills')
  return items.some(item =>
    item.generation_action != null ||
    item.generationAction != null ||
    item.expected_output != null ||
    item.expected_outputs != null ||
    item.trigger != null ||
    item.triggers != null ||
    item.skill_slug != null ||
    item.skill_name != null,
  )
}
function hasExternalWorkorderShape(record: Record<string, unknown>): boolean {
  const items = getRecordArray(record, 'external_capabilities', 'items')
  return items.some(item =>
    item.target_system != null ||
    item.auth_kind != null ||
    item.linked_skills != null ||
    item.required_fields != null ||
    item.integration_methods != null,
  )
}
function toPublicPathLabel(value: string): string {
  const trimmed = value.trim()
  if (!trimmed) return ''

  const markerMatch = /\[FILE_URL:([^\]]+)\]/.exec(trimmed)
  const pathLike = markerMatch?.[1]?.trim() || trimmed
  const parts = pathLike.split(/[\\/]/).filter(Boolean)
  return parts.at(-1) ?? trimmed
}
function stringify(v: unknown): string {
  if (v == null) return ''
  if (typeof v === 'string') return v
  if (typeof v === 'number' || typeof v === 'boolean') return String(v)
  return JSON.stringify(v)
}
const hiddenArtifactDataKeys = new Set([
  'artifactroot',
  'debug',
  'generatedat',
  'generatedby',
  'metadata',
  'raw',
  'rootpath',
  'sourcepath',
  'storagepath',
  'technicalartifact',
  'templateslug',
  'trace',
  'workspacedir',
  'workspacepath',
  'workspaceroot',
])
const sensitiveArtifactDataKeyParts = [
  'apikey',
  'authorization',
  'bearer',
  'connectionstring',
  'credential',
  'env',
  'header',
  'metadata',
  'password',
  'privatekey',
  'secret',
  'token',
]
function normalizeArtifactDataKey(key: string): string {
  return key.toLowerCase().replace(/[^a-z0-9]/g, '')
}
function shouldHideArtifactDataKey(key: string): boolean {
  const normalized = normalizeArtifactDataKey(key)
  return hiddenArtifactDataKeys.has(normalized) ||
    sensitiveArtifactDataKeyParts.some(part => normalized.includes(part))
}
function sanitizeArtifactDataForDisplay(value: unknown, seen = new WeakSet<object>()): unknown {
  if (Array.isArray(value)) {
    return value.map(item => sanitizeArtifactDataForDisplay(item, seen))
  }

  const record = asRecord(value)
  if (!record) {
    return value
  }

  if (seen.has(record)) {
    return '[Circular]'
  }
  seen.add(record)

  const sanitized: Record<string, unknown> = {}
  for (const [key, child] of Object.entries(record)) {
    if (shouldHideArtifactDataKey(key)) {
      continue
    }

    sanitized[key] = sanitizeArtifactDataForDisplay(child, seen)
  }

  seen.delete(record)
  return sanitized
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
