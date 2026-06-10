import { describe, expect, it } from 'vitest'
import { normalizeMaterialRequestedCategories } from './materialRequestedCategories'

describe('normalizeMaterialRequestedCategories', () => {
  it('兼容字符串数组格式的资料分类', () => {
    const categories = normalizeMaterialRequestedCategories({
      requested_categories: [
        '访客预约流程与审批规则',
        '楼宇区域划分与门禁点位图',
        '实名认证对接接口文档',
      ],
    })

    expect(categories).toEqual([
      { title: '访客预约流程与审批规则' },
      { title: '楼宇区域划分与门禁点位图' },
      { title: '实名认证对接接口文档' },
    ])
  })

  it('继续支持对象数组格式的资料分类', () => {
    const categories = normalizeMaterialRequestedCategories({
      requested_categories: [
        {
          title: '历史工单',
          description: '优先上传最近处理不顺的真实案例',
          examples: ['投诉工单', '售后记录', '多余示例'],
        },
      ],
    })

    expect(categories).toEqual([
      {
        title: '历史工单',
        description: '优先上传最近处理不顺的真实案例',
        examples: ['投诉工单', '售后记录'],
      },
    ])
  })

})
