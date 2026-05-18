import { describe, expect, it } from 'vitest'

import type { ChatMessage } from './hiringPageTypes'
import { extractLatestMaterialRequestedCategories, normalizeMaterialRequestedCategories } from './materialRequestedCategories'

describe('materialRequestedCategories', () => {
  it('normalizes at most three unique requested categories', () => {
    const categories = normalizeMaterialRequestedCategories({
      requested_categories: [
        { title: '  身份与权限资料  ', description: '用于确认账号边界', examples: ['身份证明', '授权说明', '忽略第三个示例'] },
        { title: '业务流程资料' },
        { name: '系统接入资料' },
        { title: '身份与权限资料' },
        { title: '额外资料' },
      ],
    })

    expect(categories).toEqual([
      { title: '身份与权限资料', description: '用于确认账号边界', examples: ['身份证明', '授权说明'] },
      { title: '业务流程资料', description: undefined, examples: undefined },
      { title: '系统接入资料', description: undefined, examples: undefined },
    ])
  })

  it('extracts requested categories from the latest material artifact', () => {
    const messages: ChatMessage[] = [
      {
        id: 'old',
        role: 'artifact',
        content: '',
        artifact: {
          kind: 'data',
          artifactType: 'material_collection_progress',
          data: { requested_categories: [{ title: '旧分类' }] },
        },
      },
      {
        id: 'new',
        role: 'artifact',
        content: '',
        artifact: {
          kind: 'data',
          artifactType: 'material_collection_progress',
          data: { requestedCategories: [{ title: '最新分类' }] },
        },
      },
    ]

    expect(extractLatestMaterialRequestedCategories(messages)).toEqual([
      { title: '最新分类', description: undefined, examples: undefined },
    ])
  })
})
