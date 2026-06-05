/**
 * SkillArtifactView.tsx - 技能相关 Artifact 视图
 * 
 * 用于展示技能工作单、技能生成状态等 artifact
 */

import { CodeView } from './BaseArtifactViews'
import {
  asRecord,
  firstString,
  getRecordArray,
  isRecord,
  stringListText,
  stringify,
  toPublicPathLabel,
} from './utils/artifactHelpers'
import { sectionLabelStyle, statChipStyle, thresholdCellStyle } from './utils/artifactStyles'

interface SkillGenerationStatusViewProps {
  artifactType: string
  data: unknown
}

interface SkillWorkorderSummaryViewProps {
  data: unknown
}

/**
 * 技能生成状态视图 - skill_generation_ready / _progress / _done 轻量状态视图
 */
export function SkillGenerationStatusView({
  artifactType, data,
}: SkillGenerationStatusViewProps) {
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
    skill_projection_binding_ready: {
      label: '☕ 等待确认资料采用',
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

/**
 * 技能工作单摘要视图 - skill_workorder_summary 专用结构化视图
 */
export function SkillWorkorderSummaryView({ data }: SkillWorkorderSummaryViewProps) {
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

/**
 * 技能卡片 - 单个技能的详细展示
 */
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

      {/* 依赖材料 & 业务资料 */}
      {(materials.length > 0 || ontologySlices.length > 0) && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <div style={sectionLabelStyle}>依赖材料 &amp; 业务资料</div>
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

/**
 * 技能区段行 - 标签+文本的行展示（用于触发条件、预期输出等）
 */
function SkillSectionRow({ label, text }: { label: string; text: string }) {
  return (
    <div style={{ display: 'flex', gap: 6, fontSize: 12 }}>
      <span style={{ flexShrink: 0, fontWeight: 600, color: 'var(--hb-text-muted, #6b7280)', minWidth: 52 }}>{label}</span>
      <span style={{ color: 'var(--hb-text, #374151)', lineHeight: 1.55 }}>{text}</span>
    </div>
  )
}
