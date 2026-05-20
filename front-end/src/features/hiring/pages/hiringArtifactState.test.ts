import { describe, expect, it } from 'vitest'

import { HiringCollectionStage } from '@/infra/api'
import type { SandboxMessage } from '@/infra/sandbox/sandbox-api'

import {
  buildHistoricalHiringConversationState,
  buildUiStageOverrides,
} from './hiringArtifactState'

describe('buildUiStageOverrides', () => {
  it('does not downgrade the main skill stage when skill generation is waiting for confirmation', () => {
    const overrides = buildUiStageOverrides(
      new Map([[HiringCollectionStage.Skill, 'completed' as const]]),
      {
        key: 'skill-generation',
        status: 'waiting_confirm',
        artifactType: 'skill_generation_ready',
        updatedAt: '2026-05-20T10:00:00Z',
      },
      null,
    )

    expect(overrides.get(HiringCollectionStage.Skill)).toBe('completed')
  })
})

describe('buildHistoricalHiringConversationState', () => {
  it('reconstructs emit_artifact history into artifact messages and downstream runs', () => {
    const sandboxMessages: SandboxMessage[] = [
      {
        type: 'assistant_message',
        content: '技能定义已经确认完成。',
        toolCalls: [
          {
            toolName: 'emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_workorder_summary',
              label: '技能清单已确认',
              skillName: 'employment-coach-conversation',
              stage: 'stage2_skill',
              isTerminal: true,
              displayHint: 'tree',
              data: {
                total_items: 1,
                items: [
                  {
                    skill_name: '应急触发判定与留痕协同',
                    generation_action: 'generate_new',
                  },
                ],
              },
            }),
          },
          {
            toolName: 'emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'skill_generation_done',
              label: '技能包已生成完毕',
              skillName: 'skill-generation',
              stage: 'skill-generation',
              isTerminal: true,
              displayHint: 'tree',
              data: {
                total_skills: 1,
                generated_count: 1,
                reused_count: 0,
                skill_slugs: ['emergency-trigger-and-audit'],
              },
            }),
          },
        ],
      },
    ]

    const restored = buildHistoricalHiringConversationState(
      sandboxMessages,
      content => content.trim(),
    )

    expect(restored.messages.some(message => message.role === 'artifact' && message.artifact?.artifactType === 'skill_workorder_summary')).toBe(true)
    expect(restored.messages.some(message => message.role === 'artifact' && message.artifact?.artifactType === 'skill_generation_done')).toBe(true)
    expect(restored.wsStageOverrides.get(HiringCollectionStage.Skill)).toBe('completed')
    expect(restored.downstreamRuns['skill-generation']?.status).toBe('completed')
    expect(restored.latestSkillSummary).toEqual({
      total_items: 1,
      items: [
        {
          skill_name: '应急触发判定与留痕协同',
          generation_action: 'generate_new',
        },
      ],
    })
  })
})
