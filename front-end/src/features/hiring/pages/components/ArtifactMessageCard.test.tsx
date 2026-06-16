import { renderToStaticMarkup } from 'react-dom/server'
import { describe, expect, it } from 'vitest'

import { ArtifactMessageCard } from './ArtifactMessageCard'

describe('ArtifactMessageCard', () => {
  it('资料收口确认门显示业务摘要而不是原始 JSON', () => {
    const html = renderToStaticMarkup(
      <ArtifactMessageCard
        artifact={{
          kind: 'data',
          artifactType: 'material_handoff_ready',
          label: '等待确认是否开始分析业务资料',
          skillName: 'employment-coach-conversation',
          stage: 'stage1_material',
          isTerminal: false,
          data: {
            context_signature: 'material-1',
            status: 'waiting_confirm',
            summary: '已整理 1 份资料，建议开始分析业务资料并进入技能定义阶段。',
            next_step: '客户信息收集规则与字段口径',
            total_items: 1,
          },
        }}
      />,
    )

    expect(html).toContain('已整理 1 份资料')
    expect(html).toContain('资料 1 项')
    expect(html).not.toContain('context_signature')
    expect(html).not.toContain('{&quot;')
  })

  it('资料收口确认门缺少 data 时不显示空对象', () => {
    const html = renderToStaticMarkup(
      <ArtifactMessageCard
        artifact={{
          kind: 'data',
          artifactType: 'material_handoff_ready',
          label: '等待确认是否开始分析业务资料',
          skillName: 'employment-coach-conversation',
          stage: 'stage1_material',
          isTerminal: false,
          data: {},
        }}
      />,
    )

    expect(html).toContain('等待确认')
    expect(html).toContain('资料已整理完成')
    expect(html).not.toContain('{}')
  })
})
