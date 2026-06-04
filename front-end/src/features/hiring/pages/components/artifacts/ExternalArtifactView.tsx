/**
 * ExternalArtifactView.tsx - 外部系统相关 Artifact 视图
 * 
 * 用于展示外部系统工作单、外部配置提交等 artifact
 */

import { CodeView } from './BaseArtifactViews'
import { asRecord, firstString, getRecordArray, isRecord, stringify } from './utils/artifactHelpers'
import { sectionLabelStyle, statChipStyle } from './utils/artifactStyles'

interface ExternalWorkorderSummaryViewProps {
  data: unknown
}

interface ExternalConfigCommittedViewProps {
  data: unknown
}

/**
 * 外部系统工作单摘要视图 - external_workorder_summary
 */
export function ExternalWorkorderSummaryView({ data }: ExternalWorkorderSummaryViewProps) {
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

/**
 * 外部配置提交视图 - external_config_committed 系统提交成功视图
 */
export function ExternalConfigCommittedView({ data }: ExternalConfigCommittedViewProps) {
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
              <div style={sectionLabelStyle}>MCP 服务</div>
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
                    {/* 规范化传输类型显示：http/stdio 旧数据映射到新标签 */}
                    {(() => {
                      const t = stringify(mcpServer.transport)
                      if (t === 'sse') return 'SSE'
                      if (t === 'streamable-http' || t === 'http') return 'Streamable HTTP'
                      if (t === 'stdio') return 'STDIO'
                      return t || 'Streamable HTTP'
                    })()}
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
