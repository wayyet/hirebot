import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import clsx from 'clsx'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'

import { api } from '@/infra/api'
import type {
  EmployeeTemplatePackageSkill,
  RecommendedStoreSkillItem,
  StoreSkillItem,
} from '@/infra/api'
import type { HiringLinkedSkillItem } from '@/infra/api/modules/hiringWorkflowApi'
import type {
  DefinedSkillItem,
  DownstreamRunState,
} from '../hiringPageTypes'
import { ConfirmationActionPanel } from './ConfirmationActionPanel'
import { getConfirmationActionCopy } from '../utils/hiringConfirmationCopy'

export type StageStatus = 'running' | 'completed' | 'failed'

interface LinkedSkill {
  skillId: string
  name: string
  version: string
}

export interface SkillCardBodyProps {
  hireId: string
  templateId?: string
  templatePackageSkills: EmployeeTemplatePackageSkill[]
  onLinkedIdsChange?: (skillIds: string[]) => void
  definitionStageStatus: StageStatus | null
  skillGenerationState: DownstreamRunState | null
  externalSystemEntryState?: DownstreamRunState | null
  definedSkills: DefinedSkillItem[]
  confirmationBusy?: boolean
  /** skill-generation 等待确认时，用户点击确认生成 */
  onConfirmSkillGeneration?: () => void
  /** skill-generation 完成后，用户点击推进到外部系统 */
  onConfirmSkillStageDone?: () => void
  /** 外部系统入口确认时，用户点击跳过外部配置 */
  onSkipExternalSystem?: () => void
}

function readArtifactNumber(data: unknown, key: string): number | null {
  if (!data || typeof data !== 'object') return null
  const val = (data as Record<string, unknown>)[key]
  return typeof val === 'number' ? val : null
}

function getSkillDefinitionStatusMeta(status: StageStatus | null): { label: string; tone: string } {
  if (!status) return { label: '未开始', tone: 'is-neutral' }
  if (status === 'running') return { label: '定义中', tone: 'is-running' }
  if (status === 'completed') return { label: '已完成', tone: 'is-completed' }
  return { label: '异常', tone: 'is-error' }
}

function getSkillImplementationMeta(
  run: DownstreamRunState | null,
  definitionStageStatus: StageStatus | null,
  hasConfirmedDefinedSkills: boolean,
): {
  label: string
  tone: string
  description: string
} {
  if (!run || run.status === 'idle') {
    if (hasConfirmedDefinedSkills) {
      return {
        label: '准备实现',
        tone: 'is-waiting',
        description: '技能定义已确认，等待进入匹配技能数据。',
      }
    }

    if (definitionStageStatus === 'running') {
      return {
        label: '等待定义完成',
        tone: 'is-neutral',
        description: '等待技能清单确认后进入匹配技能数据。',
      }
    }

    return {
      label: '未启动',
      tone: 'is-neutral',
      description: '等待技能定义确认后进入匹配技能数据。',
    }
  }

  if (run.status === 'waiting_confirm') {
    if (run.artifactType === 'skill_definition_entry_ready') {
      return {
        label: '等待进入技能定义',
        tone: 'is-waiting',
        description: '业务资料分析已完成，请确认后进入技能定义。',
      }
    }

    if (run.artifactType === 'skill_definition_ready') {
      return {
        label: '等待确认技能清单',
        tone: 'is-waiting',
        description: '技能清单草案已整理，请确认后收口技能定义。',
      }
    }

    if (run.artifactType === 'ontology_projection_ready') {
      return {
        label: '准备实现',
        tone: 'is-waiting',
        description: '技能定义已确认，请确认是否开始匹配技能数据。',
      }
    }

    if (run.artifactType === 'skill_generation_ready') {
      return {
        label: '待生成实现',
        tone: 'is-waiting',
        description: '技能数据已匹配，请确认是否生成技能实现。',
      }
    }

    const total = readArtifactNumber(run.data, 'total_skills')
    const msg = total ? `将生成 ${total} 个技能，` : ''
    return {
      label: '等待确认',
      tone: 'is-waiting',
      description: `${msg}请确认后继续。`,
    }
  }

  if (run.status === 'running') {
    if (run.artifactType === 'skill_workorder_progress') {
      return {
        label: '定义收口中',
        tone: 'is-running',
        description: '正在固定确认后的技能范围。',
      }
    }

    if (run.artifactType === 'ontology_projection_progress') {
      return {
        label: '匹配资料中',
        tone: 'is-running',
        description: '正在为已确认技能匹配业务资料。',
      }
    }

    const total = readArtifactNumber(run.data, 'total_skills')
    const completed = readArtifactNumber(run.data, 'completed_skills')
    let msg = '正在生成技能实现。'
    if (total && completed != null) {
      msg = `正在生成技能实现 (${completed}/${total})`
    } else if (total) {
      msg = `将生成 ${total} 个技能实现。`
    }

    return {
      label: '正在实现',
      tone: 'is-running',
      description: msg,
    }
  }

  if (run.status === 'completed') {
    const total = readArtifactNumber(run.data, 'total_skills')
    const generated = readArtifactNumber(run.data, 'generated_count')
    let msg = '技能实现已生成。'
    if (total && generated != null) {
      msg = `成功生成 ${generated}/${total} 个技能实现。`
    } else if (total) {
      msg = `共 ${total} 个技能实现已生成。`
    }

    return {
      label: '已实现',
      tone: 'is-completed',
      description: msg,
    }
  }

  return {
    label: '失败',
    tone: 'is-error',
    description: '生成过程出错，请稍后重试或联系管理员。',
  }
}

function getSkillConfirmationAction(run: DownstreamRunState | null): { text: string; button: string } {
  const copy = getConfirmationActionCopy(run)
  return { text: copy.text, button: copy.button }
}

function isRecommendedSkill(skill: StoreSkillItem | RecommendedStoreSkillItem): skill is RecommendedStoreSkillItem {
  return 'matchedKeywords' in skill
}

function mapConfigSkillToLinkedSkill(skill: HiringLinkedSkillItem): LinkedSkill {
  return {
    skillId: skill.skillId,
    name: skill.displayName || skill.name || skill.skillId,
    version: skill.currentVersion || '',
  }
}

function mapLinkedSkillToConfigSkill(skill: LinkedSkill): HiringLinkedSkillItem {
  return {
    skillId: skill.skillId,
    name: skill.name,
    displayName: skill.name,
    versionId: '',
    currentVersion: skill.version,
    bindingMode: 'manual',
  }
}

export function SkillCardBody({
  hireId,
  templateId,
  templatePackageSkills,
  onLinkedIdsChange,
  definitionStageStatus,
  skillGenerationState,
  externalSystemEntryState = null,
  definedSkills,
  confirmationBusy = false,
  onConfirmSkillGeneration,
  onConfirmSkillStageDone,
  onSkipExternalSystem,
}: SkillCardBodyProps) {
  const { t } = useTranslation()
  const hydratedHireIdRef = useRef<string | null>(null)
  const [query, setQuery] = useState('')
  const [searchResults, setSearchResults] = useState<StoreSkillItem[]>([])
  const [searchTotal, setSearchTotal] = useState(0)
  const [linked, setLinked] = useState<LinkedSkill[]>([])
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState('')
  const [persisting, setPersisting] = useState(false)
  const [persistError, setPersistError] = useState('')
  const trimmedQuery = query.trim()

  const { data: skillLinkConfig } = useQuery({
    queryKey: ['hiring-skill-link-config', hireId],
    queryFn: () => api.hiringWorkflow.getSkillLinkConfig(hireId),
    enabled: Boolean(hireId),
    staleTime: 30 * 1000,
  })

  const {
    data: recommendedSkillData = [] as RecommendedStoreSkillItem[],
    isLoading: isRecommendationLoading,
    error: recommendationError,
  } = useQuery({
    queryKey: ['hiring-recommended-store-skills', templateId],
    queryFn: ({ signal }) => api.skillCatalog.getRecommendedStoreSkills(templateId ?? '', { limit: 5 }, signal),
    enabled: trimmedQuery.length === 0 && Boolean(templateId),
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  })

  useEffect(() => {
    hydratedHireIdRef.current = null
    setLinked([])
    setPersistError('')
  }, [hireId])

  useEffect(() => {
    if (!hireId || !skillLinkConfig) {
      return
    }

    if (hydratedHireIdRef.current === hireId) {
      return
    }

    hydratedHireIdRef.current = hireId
    setLinked((skillLinkConfig.linkedSkills ?? []).map(mapConfigSkillToLinkedSkill))
  }, [hireId, skillLinkConfig])

  useEffect(() => {
    onLinkedIdsChange?.(linked.map(item => item.skillId))
  }, [linked, onLinkedIdsChange])

  useEffect(() => {
    if (!trimmedQuery) {
      setSearchResults([])
      setSearchTotal(0)
      setSearchError('')
      setSearching(false)
      return
    }

    const controller = new AbortController()
    const timer = window.setTimeout(async () => {
      setSearching(true)
      try {
        const data = await api.skillCatalog.searchStoreSkills(
          { q: trimmedQuery, page: 1, pageSize: 12 },
          controller.signal,
        )
        setSearchResults(data?.items ?? [])
        setSearchTotal(data?.total ?? data?.items?.length ?? 0)
        setSearchError('')
      } catch (error) {
        if ((error as { name?: string })?.name === 'AbortError') return
        setSearchError(error instanceof Error ? error.message : t('hiring.todo.skill.errorSearchFailed'))
      } finally {
        setSearching(false)
      }
    }, 300)

    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [trimmedQuery, t])

  const isLinked = useCallback((id: string) => linked.some(item => item.skillId === id), [linked])
  const currentResults = trimmedQuery ? searchResults : recommendedSkillData
  const currentTotal = trimmedQuery ? searchTotal : recommendedSkillData.length
  const currentSearching = trimmedQuery ? searching : isRecommendationLoading
  const currentError = trimmedQuery
    ? searchError
    : recommendationError
      ? t('hiring.todo.skill.recommendUnavailable')
      : ''

  const saveLinkedSkills = useCallback(async (nextLinked: LinkedSkill[]) => {
    if (!hireId) {
      return null
    }

    setPersisting(true)
    setPersistError('')

    try {
      const saved = await api.hiringWorkflow.saveSkillLinkConfig(hireId, {
        submissionMode: nextLinked.length > 0 ? 'configured' : 'pending',
        linkedSkills: nextLinked.map(mapLinkedSkillToConfigSkill),
      })

      hydratedHireIdRef.current = hireId
      setLinked((saved.linkedSkills ?? []).map(mapConfigSkillToLinkedSkill))
      return saved
    } catch (error) {
      setPersistError(error instanceof Error ? error.message : t('common.requestFailed'))
      throw error
    } finally {
      setPersisting(false)
    }
  }, [hireId, t])

  const searchStatusLabel = currentSearching
    ? t('hiring.todo.skill.statusSearching')
    : linked.length > 0
      ? t('hiring.todo.skill.statusLinkedCount', { count: linked.length })
      : !trimmedQuery
        ? t('hiring.todo.skill.statusRecommendedCount', { count: currentResults.length })
        : currentResults.length > 0
          ? t('hiring.todo.skill.statusResultCount', { count: currentResults.length })
          : t('hiring.todo.skill.statusPending')
  const hasConfirmedDefinedSkills = definedSkills.length > 0

  const definitionMeta = useMemo(
    () => hasConfirmedDefinedSkills
      ? getSkillDefinitionStatusMeta('completed')
      : getSkillDefinitionStatusMeta(definitionStageStatus),
    [definitionStageStatus, hasConfirmedDefinedSkills],
  )
  const implementationMeta = useMemo(
    () => getSkillImplementationMeta(skillGenerationState, definitionStageStatus, hasConfirmedDefinedSkills),
    [definitionStageStatus, hasConfirmedDefinedSkills, skillGenerationState],
  )
  const skillConfirmationAction = getSkillConfirmationAction(skillGenerationState)
  const showSkillConfirmation =
    skillGenerationState?.status === 'waiting_confirm' &&
    skillGenerationState.artifactType !== 'skill_projection_binding_ready' &&
    Boolean(onConfirmSkillGeneration)
  const externalEntryConfirmationAction = getConfirmationActionCopy(externalSystemEntryState)
  const showExternalEntryConfirmation =
    externalSystemEntryState?.status === 'waiting_confirm' &&
    externalSystemEntryState.artifactType === 'external_system_entry_ready' &&
    (Boolean(onConfirmSkillStageDone) || Boolean(onSkipExternalSystem))

  async function handleLink(skill: StoreSkillItem) {
    if (persisting || isLinked(skill.id)) return

    const next = [
      ...linked,
      {
        skillId: skill.id,
        name: skill.displayName ?? skill.name,
        version: skill.currentVersion ?? '',
      },
    ]

    try {
      await saveLinkedSkills(next)
    } catch {
      // 错误已在界面展示
    }
  }

  async function handleUnlink(skillId: string) {
    if (persisting) return

    const next = linked.filter(item => item.skillId !== skillId)

    try {
      await saveLinkedSkills(next)
    } catch {
      // 错误已在界面展示
    }
  }

  return (
    <div className="hb-todo-skill">
      <section className="hb-todo-skill-section is-progress" aria-label="技能定义与实现状态">
        <div className="hb-todo-skill-section-head">
          {t('hiring.todo.skill.currentStatus')}
        </div>
        <div className="hb-todo-skill-status-stack">
          <div className="hb-todo-skill-status-grid">
            <div className="hb-todo-skill-status-card">
              <span className="hb-todo-skill-status-label">{t('hiring.todo.skill.definitionStatus')}</span>
              <span className={clsx('hb-todo-skill-chip', definitionMeta.tone)}>{definitionMeta.label}</span>
            </div>
            <div className="hb-todo-skill-status-card">
              <span className="hb-todo-skill-status-label">{t('hiring.todo.skill.implementationStatus')}</span>
              <span className={clsx('hb-todo-skill-chip', implementationMeta.tone)}>{implementationMeta.label}</span>
            </div>
          </div>

          <div className="hb-todo-skill-confirmed-block">
            <div className="hb-todo-skill-confirmed-head">
              <span>{hasConfirmedDefinedSkills ? '已确认技能' : '技能定义'}</span>
              {hasConfirmedDefinedSkills ? <span>共 {definedSkills.length} 项</span> : null}
            </div>
            {hasConfirmedDefinedSkills ? (
              <ul className="hb-todo-skill-confirmed-list" aria-label="已确认技能定义">
                {definedSkills.map((skill, index) => (
                  <li key={`${skill.skillName}-${index}`} className="hb-todo-skill-confirmed-item">
                    <strong title={skill.skillName}>{skill.skillName}</strong>
                    {skill.description ? <p title={skill.description}>{skill.description}</p> : null}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="hb-todo-skill-status-desc">等待用户确认技能清单后展示已定义技能。</p>
            )}
          </div>

          <div className="hb-todo-skill-implementation-note">
            <span className={clsx('hb-todo-skill-status-dot', implementationMeta.tone)} />
            <span>{implementationMeta.description}</span>
          </div>
        </div>
      </section>

      {/* 技能阶段快捷推进按钮 */}
      {showSkillConfirmation ? (
        <ConfirmationActionPanel
          ariaLabel="技能阶段确认"
          message={skillConfirmationAction.text}
          primaryLabel={skillConfirmationAction.button}
          busy={confirmationBusy}
          onPrimary={onConfirmSkillGeneration}
        />
      ) : null}
      {showExternalEntryConfirmation ? (
        <ConfirmationActionPanel
          ariaLabel="外部配置入口确认"
          message={externalEntryConfirmationAction.text}
          primaryLabel={externalEntryConfirmationAction.button}
          busy={confirmationBusy}
          onPrimary={onConfirmSkillStageDone}
          secondaryLabel="无需外部系统，跳过"
          onSecondary={onSkipExternalSystem}
        />
      ) : null}

      <div className="hb-todo-skill-toolbar">
        <label className="hb-todo-skill-search-field">
          <input
            type="text"
            className="hb-todo-input"
            placeholder={t('hiring.todo.skill.searchPlaceholder')}
            value={query}
            onChange={event => setQuery(event.target.value)}
          />
        </label>
        <span
          className={clsx(
            'hb-todo-skill-status-pill',
            currentSearching && 'is-searching',
            !currentSearching && linked.length > 0 && 'is-linked',
          )}
        >
          {searchStatusLabel}
        </span>
      </div>

      {persistError && <p className="hb-todo-error">{persistError}</p>}

      <section className="hb-todo-skill-section is-search" aria-label="搜索与推荐技能">
        {currentSearching && <p className="hb-todo-hint-muted">{t('hiring.todo.skill.searchingHint')}</p>}
        {currentError && <p className="hb-todo-error">{currentError}</p>}
        {!currentSearching && !currentError && currentResults.length === 0 && (
          <p className="hb-todo-skill-empty">{trimmedQuery ? t('hiring.todo.skill.noResults') : t('hiring.todo.skill.noRecommended')}</p>
        )}

        {currentResults.length > 0 && (
          <>
            {trimmedQuery && currentTotal > currentResults.length && (
              <p className="hb-todo-hint-muted">{t('hiring.todo.skill.resultsSummary', { total: currentTotal, count: currentResults.length })}</p>
            )}
            <ul className="hb-todo-skill-list">
              {currentResults.map(skill => {
                const displayName = skill.displayName ?? skill.name
                const linkedNow = isLinked(skill.id)
                const recommendation = isRecommendedSkill(skill) ? skill : null

                return (
                  <li key={skill.id} className="hb-todo-skill-item">
                    <div className="hb-todo-skill-main">
                      <div className="hb-todo-skill-title-row">
                        <strong>{displayName}</strong>
                        <div className="hb-todo-skill-chips">
                          {skill.currentVersion && <span className="hb-todo-skill-chip is-meta">{`v${skill.currentVersion}`}</span>}
                          {skill.level && <span className="hb-todo-skill-chip is-meta">{skill.level}</span>}
                        </div>
                      </div>
                      {skill.description && <p className="hb-todo-skill-desc">{skill.description}</p>}
                      {recommendation?.matchedKeywords?.length && !trimmedQuery ? (
                        <ul className="hb-todo-tag-list">
                          {recommendation.matchedKeywords.slice(0, 5).map(keyword => (
                            <li key={keyword} className="hb-todo-tag is-mini is-reason">{keyword}</li>
                          ))}
                        </ul>
                      ) : null}
                      {skill.tags && skill.tags.length > 0 && (
                        <ul className="hb-todo-tag-list">
                          {skill.tags.slice(0, 5).map(tag => <li key={tag} className="hb-todo-tag is-mini">{tag}</li>)}
                        </ul>
                      )}
                    </div>
                    <div className="hb-todo-skill-actions">
                      <button
                        type="button"
                        className={clsx('hb-todo-row-btn', linkedNow ? 'is-ghost' : 'is-primary')}
                        disabled={linkedNow || persisting}
                        onClick={() => handleLink(skill)}
                      >
                        {linkedNow ? t('hiring.todo.skill.linked') : persisting ? t('common.saving') : t('hiring.todo.skill.link')}
                      </button>
                    </div>
                  </li>
                )
              })}
            </ul>
          </>
        )}
      </section>

      {linked.length > 0 && (
        <section className="hb-todo-skill-section is-linked-summary" aria-label="已关联技能">
          <div className="hb-todo-skill-section-head">
            {t('hiring.todo.skill.linkedTitle')}
            <span className="hb-todo-skill-section-pill is-linked">{t('hiring.todo.skill.countLabel', { count: linked.length })}</span>
          </div>
          <p className="hb-todo-hint-muted">{t('hiring.todo.skill.linkedDesc')}</p>
          <ul className="hb-todo-skill-linked-list">
            {linked.map(skill => (
              <li key={skill.skillId} className="hb-todo-skill-linked-item">
                <div className="hb-todo-skill-main">
                  <div className="hb-todo-skill-title-row">
                    <strong>{skill.name}</strong>
                    <div className="hb-todo-skill-chips">
                      {skill.version && <span className="hb-todo-skill-chip is-meta">{`v${skill.version}`}</span>}
                    </div>
                  </div>
                </div>
                <div className="hb-todo-skill-actions">
                  <button
                    type="button"
                    className="hb-todo-row-btn is-ghost"
                    disabled={persisting}
                    onClick={() => handleUnlink(skill.skillId)}
                  >
                    {persisting ? t('common.saving') : t('hiring.todo.skill.unlink')}
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}

      {templatePackageSkills.length > 0 && (
        <section className="hb-todo-skill-section is-template" aria-label="模板内置技能">
          <div className="hb-todo-skill-section-head">
            {t('hiring.todo.skill.templateTitle')}
            <span className="hb-todo-skill-section-pill">{t('hiring.todo.skill.countLabel', { count: templatePackageSkills.length })}</span>
          </div>
          <p className="hb-todo-hint-muted">
            {t('hiring.todo.skill.templateDesc', { count: templatePackageSkills.length })}
          </p>
          <ul className="hb-todo-skill-list is-template">
            {templatePackageSkills.map(skill => (
              <li key={skill.relativePath} className="hb-todo-skill-item is-static">
                <div className="hb-todo-skill-main">
                  <div className="hb-todo-skill-title-row">
                    <strong>{skill.name}</strong>
                    <div className="hb-todo-skill-chips">
                      <span className={clsx('hb-todo-skill-chip', skill.required ? 'is-required' : 'is-optional')}>
                        {skill.required ? t('hiring.todo.skill.templateRequired') : t('hiring.todo.skill.templateOptional')}
                      </span>
                    </div>
                  </div>
                  <p className="hb-todo-skill-desc">{skill.relativePath}</p>
                </div>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}
