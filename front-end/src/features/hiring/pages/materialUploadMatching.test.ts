import { describe, expect, it } from 'vitest'

import type { ChatFile, ChatMessage, MaterialRequestedCategory } from './hiringPageTypes'
import {
  buildUploadedCountByCategory,
  countDistinctMaterialUploads,
  extractConversationMaterialFiles,
  listUnmatchedMaterialUploads,
} from './materialUploadMatching'

describe('materialUploadMatching', () => {
  const requestedCategories: MaterialRequestedCategory[] = [
    { title: '工艺能力参数', examples: ['设备 Cpk', '公差边界'] },
    { title: '历史 DFM 案例', examples: ['缺陷案例'] },
  ]

  it('marks a category as uploaded when a conversation file name matches the card title', () => {
    const conversationFiles: ChatFile[] = [
      {
        id: 'file-1',
        name: '工艺能力参数.txt',
        size: 1200,
        status: '已解析',
        type: 'file',
      },
    ]

    const uploadedCountByCategory = buildUploadedCountByCategory(
      requestedCategories,
      [],
      conversationFiles,
    )

    expect(uploadedCountByCategory.get('工艺能力参数')).toBe(1)
    expect(uploadedCountByCategory.get('历史 DFM 案例') ?? 0).toBe(0)
  })

  it('keeps explicit requested_category_title bindings from persisted uploads', () => {
    const uploadedCountByCategory = buildUploadedCountByCategory(
      requestedCategories,
      [
        {
          key: 'saved-1',
          name: 'anything.md',
          sizeBytes: 88,
          requestedCategoryTitle: '历史 DFM 案例',
        },
      ],
      [],
    )

    expect(uploadedCountByCategory.get('历史 DFM 案例')).toBe(1)
  })

  it('deduplicates the same file across persisted uploads and conversation files', () => {
    const conversationFiles: ChatFile[] = [
      {
        id: 'file-1',
        name: '工艺能力参数.txt',
        size: 1200,
        status: '已解析',
        type: 'file',
      },
    ]

    const totalUploadedCount = countDistinctMaterialUploads(
      [
        {
          name: '工艺能力参数.txt',
          sizeBytes: 1200,
        },
      ],
      conversationFiles,
    )

    expect(totalUploadedCount).toBe(1)
  })

  it('extracts only non-skill conversation files from chat history', () => {
    const messages: ChatMessage[] = [
      {
        id: 'user-1',
        role: 'user',
        content: '上传资料',
        files: [
          {
            id: 'file-1',
            name: '工艺能力参数.txt',
            size: 1200,
            status: '已解析',
            type: 'file',
          },
          {
            id: 'skill-1',
            name: 'dfm-skill.zip',
            size: 3200,
            status: '已解析',
            type: 'skill',
          },
        ],
      },
    ]

    expect(extractConversationMaterialFiles(messages)).toEqual([
      {
        id: 'file-1',
        name: '工艺能力参数.txt',
        size: 1200,
        status: '已解析',
        type: 'file',
      },
    ])
  })

  it('collects uploaded files that do not match any requested category', () => {
    const unmatched = listUnmatchedMaterialUploads(
      requestedCategories,
      [],
      [
        {
          id: 'file-1',
          name: '客户补充说明.pdf',
          size: 2048,
          status: '已解析',
          type: 'file',
        },
        {
          id: 'file-2',
          name: '工艺能力参数.txt',
          size: 1200,
          status: '已解析',
          type: 'file',
        },
      ],
    )

    expect(unmatched).toEqual([
      {
        key: '客户补充说明::2048',
        name: '客户补充说明.pdf',
        sizeBytes: 2048,
      },
    ])
  })
})
