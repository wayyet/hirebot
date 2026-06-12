import { describe, expect, it } from 'vitest'

import { resolveMaterialShellStatusLabel } from './materialCardStatus'

const messages: Record<string, string> = {
  'hiring.todo.status.completed': '已完成',
  'hiring.todo.status.failed': '失败',
  'hiring.todo.material.statusUploaded': '已上传 {{count}}',
  'hiring.todo.material.statusPendingCount': '待上传 {{count}}',
  'hiring.todo.material.statusPending': '待上传',
}

function t(key: string, options?: Record<string, unknown>) {
  const template = messages[key] ?? key
  return template.replace(/\{\{count\}\}/g, String(options?.count ?? ''))
}

describe('resolveMaterialShellStatusLabel', () => {
  it('资料阶段完成后优先显示完成状态，不再显示待上传数量', () => {
    const label = resolveMaterialShellStatusLabel({
      stageStatus: 'completed',
      materialCardCount: 3,
      completedCardCount: 1,
      totalUploadedCount: 1,
    }, t)

    expect(label).toBe('已完成')
  })

  it('资料阶段未完成时仍按分类上传进度显示待上传数量', () => {
    const label = resolveMaterialShellStatusLabel({
      stageStatus: 'running',
      materialCardCount: 3,
      completedCardCount: 1,
      totalUploadedCount: 1,
    }, t)

    expect(label).toBe('待上传 2')
  })
})
