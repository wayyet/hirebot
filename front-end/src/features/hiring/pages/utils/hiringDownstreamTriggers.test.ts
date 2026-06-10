import { describe, expect, it } from 'vitest'

import { buildSkillGenerationPayload } from './hiringDownstreamTriggers'

describe('buildSkillGenerationPayload', () => {
  it('projection 目录 slug 与已确认技能 slug 不一致时不启动技能生成', () => {
    const payload = buildSkillGenerationPayload({
      workspace_root: '/workspace/template-1',
      items: [
        { name: 'order-insertion-feasibility', display_name: '插单可行性评估' },
      ],
    }, {
      projected_count: 1,
      projection_paths: [
        'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
      ],
    })

    expect(payload).toBeNull()
  })

  it('projection 目录 slug 属于已确认技能时附带稳定 slug 清单', () => {
    const payload = buildSkillGenerationPayload({
      workspace_root: '/workspace/template-1',
      items: [
        { name: 'insert-order-feasibility', display_name: '插单可行性评估' },
      ],
    }, {
      projected_count: 1,
      projection_paths: [
        'ontology/projections/insert-order-feasibility/cosmetics.workflow-contract.projection.json',
      ],
    })

    expect(payload).toMatchObject({
      confirmed_skill_slugs: ['insert-order-feasibility'],
      projection_skill_slugs: ['insert-order-feasibility'],
      projection_binding_confirmed: true,
      projection_contract_mode: 'required',
    })
  })
})
