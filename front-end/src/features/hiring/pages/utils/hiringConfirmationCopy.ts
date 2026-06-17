import type { DownstreamRunState } from '../hiringPageTypes'

export interface ConfirmationActionCopy {
  text: string
  button: string
  visibleMessage: string
}

function hasOpenQuestions(data: unknown): boolean {
  if (!data || typeof data !== 'object' || Array.isArray(data)) {
    return false
  }

  const openQuestions = (data as Record<string, unknown>).open_questions
  return Array.isArray(openQuestions) && openQuestions.length > 0
}

export function getConfirmationActionCopy(run: DownstreamRunState | null): ConfirmationActionCopy {
  if (run?.artifactType === 'material_handoff_ready') {
    return {
      text: '资料已经整理好。是否开始分析这批业务资料？',
      button: '开始分析资料',
      visibleMessage: '开始分析这批业务资料',
    }
  }

  if (run?.artifactType === 'skill_definition_entry_ready') {
    return {
      text: '业务资料分析完成。是否进入技能定义？',
      button: '进入技能定义',
      visibleMessage: '进入技能定义',
    }
  }

  if (run?.artifactType === 'skill_definition_ready') {
    return {
      text: '技能清单已整理好。确认后会固定本轮技能范围，并进入匹配资料前确认。',
      button: '确认技能清单',
      visibleMessage: '确认当前技能清单',
    }
  }

  if (run?.artifactType === 'ontology_projection_ready') {
    return {
      text: '技能范围已确认。确认后会把业务资料匹配到这些技能，作为生成实现的依据。',
      button: '匹配技能资料',
      visibleMessage: '开始匹配技能所需资料',
    }
  }

  if (run?.artifactType === 'skill_generation_ready') {
    const withQuestions = hasOpenQuestions(run.data)
    return {
      text: withQuestions
        ? '技能所需资料已匹配完成，但还有生成前业务口径需要确认。确认后会按这些口径生成技能实现。'
        : '技能所需资料已匹配完成。确认后开始生成技能实现。',
      button: withQuestions ? '确认口径并生成' : '生成技能实现',
      visibleMessage: withQuestions ? '确认生成前业务口径并生成技能实现' : '生成技能实现',
    }
  }

  if (run?.artifactType === 'external_system_entry_ready') {
    return {
      text: '技能实现已完成。确认后进入外部系统配置，检查是否需要读取数据、写入结果或发送通知。',
      button: '进入外部配置',
      visibleMessage: '进入外部系统配置',
    }
  }

  if (run?.artifactType === 'packaging_testcases_ready') {
    return {
      text: '外部配置已完成。是否生成评估测试用例？你也可以跳过，直接进入打包前审查。',
      button: '生成测试用例',
      visibleMessage: '生成评估测试用例',
    }
  }

  if (run?.artifactType === 'review_readiness') {
    return {
      text: '数字员工内容已准备好。确认后会先做完整性审查，再根据结果继续生成数字员工包。',
      button: '开始完整性审查',
      visibleMessage: '开始完整性审查',
    }
  }

  return {
    text: '当前步骤需要你确认后继续。',
    button: '确认继续',
    visibleMessage: '确认继续',
  }
}

export function getStageAdvanceConfirmationCopy(stage: 'material' | 'external') {
  if (stage === 'material') {
    return {
      title: '资料阶段待确认',
      prompt: '当前资料已整理好。是否开始分析这批业务资料？如果还想补充，可以先继续上传。',
      continueLabel: '继续补充资料',
      confirmLabel: '开始分析资料',
      continueNotice: '资料阶段保持打开，你可以继续补充资料后再推进。',
      visibleMessage: '开始分析这批业务资料',
    }
  }

  return {
    title: '外部配置待确认',
    prompt: '当前外部配置已保存。确认后会进入打包前准备；如果还需要调整，可以先继续修改。',
    continueLabel: '继续调整配置',
    confirmLabel: '确认外部配置',
    continueNotice: '外部配置阶段保持打开，你可以继续修改配置后再推进。',
    visibleMessage: '确认外部配置并继续',
  }
}
