import { describe, expect, it } from 'vitest'

import { HiringCollectionStage } from '@/infra/api'
import type { SandboxMessage } from '@/infra/sandbox/sandbox-api'

import {
  buildCoachResumePrompt,
  buildHistoricalHiringConversationState,
  buildUiStageOverrides,
  shouldHoldExternalStageUntilSkillImplementation,
} from './hiringArtifactState'

describe('buildUiStageOverrides', () => {
  it('does not change the main skill stage when no skill-stage hold is requested', () => {
    const overrides = buildUiStageOverrides(
      new Map([[HiringCollectionStage.Skill, 'completed' as const]]),
      {
        key: 'skill-generation',
        status: 'waiting_confirm',
        artifactType: 'skill_generation_ready',
        updatedAt: '2026-05-20T10:00:00Z',
      },
      false,
    )

    expect(overrides.get(HiringCollectionStage.Skill)).toBe('completed')
  })

  it('holds the external stage when skill implementation is still pending', () => {
    const overrides = buildUiStageOverrides(
      new Map([
        [HiringCollectionStage.Skill, 'completed' as const],
        [HiringCollectionStage.External, 'running' as const],
      ]),
      {
        key: 'skill-generation',
        status: 'waiting_confirm',
        artifactType: 'skill_generation_ready',
        updatedAt: '2026-05-20T10:00:00Z',
      },
      true,
    )

    expect(overrides.get(HiringCollectionStage.External)).toBeUndefined()
  })

  it('keeps the main skill stage running when skill definition is done but implementation is still pending', () => {
    const overrides = buildUiStageOverrides(
      new Map([[HiringCollectionStage.Skill, 'completed' as const]]),
      {
        key: 'skill-generation',
        status: 'waiting_confirm',
        artifactType: 'skill_generation_ready',
        updatedAt: '2026-05-20T10:00:00Z',
      },
      true,
    )

    expect(overrides.get(HiringCollectionStage.Skill)).toBe('running')
  })
})

describe('shouldHoldExternalStageUntilSkillImplementation', () => {
  it('returns true when skill generation still waits for confirmation', () => {
    const hold = shouldHoldExternalStageUntilSkillImplementation(
      {
        skills: [
          { skill_name: 'A', generation_action: 'reuse_existing' },
          { skill_name: 'B', generation_action: 'reuse_existing' },
        ],
      },
      {
        key: 'skill-generation',
        status: 'waiting_confirm',
        artifactType: 'skill_generation_ready',
        updatedAt: '2026-05-20T10:00:00Z',
      },
    )

    expect(hold).toBe(true)
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

  it('marks the external stage completed when external_config_committed exists in history', () => {
    const sandboxMessages: SandboxMessage[] = [
      {
        type: 'assistant_message',
        content: '外部配置已经提交完成。',
        toolCalls: [
          {
            toolName: 'emit_artifact',
            arguments: JSON.stringify({
              kind: 'data',
              artifactType: 'external_config_committed',
              label: '外部配置已提交',
              skillName: 'external-config',
              stage: 'stage3_external',
              isTerminal: true,
              displayHint: 'tree',
              data: {
                submissionMode: 'configured',
                updatedAtUtc: '2026-05-28T10:00:00Z',
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

    expect(restored.messages.some(message => message.role === 'artifact' && message.artifact?.artifactType === 'external_config_committed')).toBe(true)
    expect(restored.wsStageOverrides.get(HiringCollectionStage.External)).toBe('completed')
  })
})

describe('buildCoachResumePrompt', () => {
  it('builds a resume prompt that routes back to the coach after ontology extraction', () => {
    const prompt = buildCoachResumePrompt('post-ontology-extraction', {
      materialSummary: { total_items: 1 },
      ontologyResult: { completed_slices: 1 },
    })

    expect(prompt).toContain('Switch back to skill `employment-coach-conversation` now.')
    expect(prompt).toContain('ask whether to enter skill definition now')
    expect(prompt).toContain('"completed_slices": 1')
  })
})
