/**
 * SkillCard — 技能阶段卡片组件
 *
 * 功能：
 * - 搜索并关联技能商店的技能
 * - 显示模板内置技能
 * - 显示已定义技能及其生成状态
 * - 支持防抖搜索和推荐技能
 */
import { useCallback, useEffect, useMemo, useState } from 'react'
import clsx from 'clsx'
import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'

import { api } from '@/infra/api'
import type {
  EmployeeTemplatePackageSkill,
  RecommendedStoreSkillItem,
  StoreSkillItem,
} from '@/infra/api'
import type {
  DefinedSkillItem,
  DownstreamRunState,
} from '../hiringPageTypes'

// ── 类型 ──────────────────────────────────────────────────────────────────────

export type StageStatus = 'running' | 'completed' | 'failed'

interface LinkedSkill {
  skillId: string
  name: string
  version: string
}

export interface SkillCardBodyProps {
  templateId?: string
  templatePackageSkills: EmployeeTemplatePackageSkill[]
  onAfterLink: (summary: string) => void
  onLinkedIdsChange?: (skillIds: string[]) => void
  definitionStageStatus: StageStatus | null
  skillGenerationState: DownstreamRunState | null
  definedSkills: DefinedSkillItem[]
}

// ── 工具函数 ─────────────────────────────────────────────────────────────────

function readArtifactNumber(data: unknown, key: string): number | null {
  if (!data || typeof data !== 'object') return null
  const val = (data as Record<string, unknown>)[key]
  return typeof val === 'number' ? val : null
}

function getSkillDefinitionStatusMeta(status: StageStatus | null): { label: string; tone: string } {
  if (!status) return { label: '待开始', tone: 'is-neutral' }
  if (status === 'running') return { label: '定义中…', tone: 'is-running' }
  if (status === 'completed') return { label: '已完成', tone: 'is-completed' }
  return { label: '异常', tone: 'is-error' }
}

function getSkillImplementationMeta(run: DownstreamRunState | null): {
  label: string
  tone: string
  description: string
} {
  if (!run || run.status === 'not_started') {
    return {
      label: '未启动',
      tone: 'is-neutral',
      description: '等待技能定义完成后自动开始实现。',
    }
  }

  if (run.status === 'waiting_confirm') {
    const total = readArtifactNumber(run.data, 'total_skills')
    const msg = total ? `将生成 ${total} 个技能，` : ''
    return {
      label: '等待确认',
      tone: 'is-waiting',
      description: `${msg}请确认后继续。`,
    }
  }

  if (run.status === 'running') {
    const total = readArtifactNumber(run.data, 'total_skills')
    const completed = readArtifactNumber(run.data, 'completed_skills')
    let msg = '正在生成技能定义…'
    if (total && completed != null) {
      msg = `正在生成 (${completed}/${total})…`
    } else if (total) {
      msg = `将生成 ${total} 个技能…`
    }
    return {
      label: '实现中',
      tone: 'is-running',
      description: msg,
    }
  }

  if (run.status === 'completed') {
    const total = readArtifactNumber(run.data, 'total_skills')
    const generated = readArtifactNumber(run.data, 'generated_count')
    let msg = '技能定义已生成。'
    if (total && generated != null) {
      msg = `成功生成 ${generated}/${total} 个技能定义。`
    } else if (total) {
      msg = `共 ${total} 个技能定义已生成。`
    }
    return {
      label: '已完成',
      tone: 'is-completed',
      description: msg,
    }
  }

  return {
    label: '失败',
    tone: 'is-error',
    description: run.error ?? '生成过程出错，请稍后重试或联系管理员。',
  }
}

function getDefinedSkillGenerationMeta(
  skill: DefinedSkillItem,
  skillGenerationState: DownstreamRunState | null,
): { label: string; tone: string } {
  // 如果全局状态为 not_started/running, 统一显示"待生成"
  if (!skillGenerationState || skillGenerationState.status === 'not_started' || skillGenerationState.status === 'running') {
    return { label: '待生成', tone: 'is-neutral' }
  }

  // 如果全局状态为 completed，根据单个技能状态判断
  if (skillGenerationState.status === 'completed') {
    if (skill.implementationStatus === 'completed') {
      return { label: '已生成', tone: 'is-completed' }
    }
    if (skill.implementationStatus === 'failed') {
      return { label: '生成失败', tone: 'is-error' }
    }
    return { label: '未生成', tone: 'is-neutral' }
  }

  return { label: '未知', tone: 'is-neutral' }
}

function isRecommendedSkill(skill: StoreSkillItem | RecommendedStoreSkillItem): skill is RecommendedStoreSkillItem {
  return 'matchedKeywords' in skill
}

// ── SkillCardBody 组件 ───────────────────────────────────────────────────────

export function SkillCardBody({
  templateId,
  templatePackageSkills,
  onAfterLink,
  onLinkedIdsChange,
  definitionStageStatus,
  skillGenerationState,
  definedSkills,
}: SkillCardBodyProps) {
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [searchResults, setSearchResults] = useState<StoreSkillItem[]>([])
  const [searchTotal, setSearchTotal] = useState(0)
  const [linked, setLinked] = useState<LinkedSkill[]>([])
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState('')
  const trimmedQuery = query.trim()

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

  // 每次 linked 变更都向父组件同步 store skill UUID 列表，供 import-package 请求使用
  useEffect(() => {
    onLinkedIdsChange?.(linked.map(l => l.skillId))
  }, [linked, onLinkedIdsChange])

  // 防抖搜索：对接模板池同源接口 /api/store/skills?page=1&pageSize=12&q=...
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
      } catch (e) {
        if ((e as { name?: string })?.name === 'AbortError') return
        setSearchError(e instanceof Error ? e.message : t('hiring.todo.skill.errorSearchFailed'))
      } finally {
        setSearching(false)
      }
    }, 300)
    return () => { window.clearTimeout(timer); controller.abort() }
  }, [trimmedQuery, t])

  const isLinked = useCallback((id: string) => linked.some(l => l.skillId === id), [linked])
  const currentResults = trimmedQuery ? searchResults : recommendedSkillData
  const currentTotal = trimmedQuery ? searchTotal : recommendedSkillData.length
  const currentSearching = trimmedQuery ? searching : isRecommendationLoading
  const currentError = trimmedQuery
    ? searchError
    : recommendationError
      ? t('hiring.todo.skill.recommendUnavailable')
      : ''

  const searchStatusLabel = currentSearching
    ? t('hiring.todo.skill.statusSearching')
    : linked.length > 0
      ? t('hiring.todo.skill.statusLinkedCount', { count: linked.length })
      : !trimmedQuery
        ? t('hiring.todo.skill.statusRecommendedCount', { count: currentResults.length })
        : currentResults.length > 0
          ? t('hiring.todo.skill.statusResultCount', { count: currentResults.length })
      : t('hiring.todo.skill.statusPending')

  const definitionMeta = useMemo(
    () => getSkillDefinitionStatusMeta(definitionStageStatus),
    [definitionStageStatus],
  )
  const implementationMeta = useMemo(
    () => getSkillImplementationMeta(skillGenerationState),
    [skillGenerationState],
  )

  function handleLink(s: StoreSkillItem) {
    if (isLinked(s.id)) return
    const next = [...linked, { skillId: s.id, name: s.displayName ?? s.name, version: s.currentVersion ?? '' }]
    setLinked(next)
    const names = next.map(l => l.name).join('、')
    onAfterLink(`已关联技能：${names}。请继续。`)
  }

  function handleUnlink(id: string) {
    setLinked(prev => prev.filter(l => l.skillId !== id))
  }

  return (
    <div className="hb-todo-skill">
      <section className="hb-todo-skill-section is-progress" aria-label="技能定义与实现状态">
        <div className="hb-todo-skill-section-head">
          {t('hiring.todo.skill.currentStatus')}
        </div>
        <div className="hb-todo-skill-linked-item">
          <div className="hb-todo-skill-main">
            <div className="hb-todo-skill-title-row">
              {t('hiring.todo.skill.definitionStatus')}
              <div className="hb-todo-skill-chips">
                <span className={clsx('hb-todo-skill-chip', definitionMeta.tone)}>{definitionMeta.label}</span>
              </div>
            </div>
          </div>
        </div>
        <div className="hb-todo-skill-linked-item">
          <div className="hb-todo-skill-main">
            <div className="hb-todo-skill-title-row">
              {t('hiring.todo.skill.implementationStatus')}
              <div className="hb-todo-skill-chips">
                <span className={clsx('hb-todo-skill-chip', implementationMeta.tone)}>{implementationMeta.label}</span>
              </div>
            </div>
            <p className="hb-todo-skill-desc">{implementationMeta.description}</p>
          </div>
        </div>
      </section>

      <div className="hb-todo-skill-toolbar">
        <label className="hb-todo-skill-search-field">
          <input
            type="text"
            className="hb-todo-input"
            placeholder={t('hiring.todo.skill.searchPlaceholder')}
            value={query}
            onChange={e => setQuery(e.target.value)}
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

      {definedSkills.length > 0 && (
        <section className="hb-todo-skill-section is-defined" aria-label="已定义技能">
          <div className="hb-todo-skill-section-head">
            {t('hiring.todo.skill.definedTitle')}
            <span className="hb-todo-skill-section-pill">{t('hiring.todo.skill.countLabel', { count: definedSkills.length })}</span>
          </div>
          <ul className="hb-todo-skill-list is-template">
            {definedSkills.map(skill => {
              const generationMeta = getDefinedSkillGenerationMeta(skill, skillGenerationState)

              return (
                <li key={skill.skillName} className="hb-todo-skill-item is-static">
                  <div className="hb-todo-skill-main">
                    <div className="hb-todo-skill-title-row">
                      <strong>{skill.skillName}</strong>
                      <div className="hb-todo-skill-chips">
                        <span className={clsx('hb-todo-skill-chip', generationMeta.tone)}>{generationMeta.label}</span>
                      </div>
                    </div>
                    {skill.description && <p className="hb-todo-skill-desc">{skill.description}</p>}
                    {skill.expectedOutput && (
                      <p className="hb-todo-skill-inline-meta">{t('hiring.todo.skill.expectedOutput', { value: skill.expectedOutput })}</p>
                    )}
                    {skill.triggers.length > 0 && (
                      <p className="hb-todo-skill-inline-meta">{t('hiring.todo.skill.triggers', { value: skill.triggers.join('、') })}</p>
                    )}
                  </div>
                </li>
              )
            })}
          </ul>
        </section>
      )}

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
              {currentResults.map(s => {
                const displayName = s.displayName ?? s.name
                const linkedNow = isLinked(s.id)
                const recommendation = isRecommendedSkill(s) ? s : null

                return (
                  <li key={s.id} className="hb-todo-skill-item">
                    <div className="hb-todo-skill-main">
                      <div className="hb-todo-skill-title-row">
                        <strong>{displayName}</strong>
                        <div className="hb-todo-skill-chips">
                          {s.currentVersion && <span className="hb-todo-skill-chip is-meta">{`v${s.currentVersion}`}</span>}
                          {s.level && <span className="hb-todo-skill-chip is-meta">{s.level}</span>}
                        </div>
                      </div>
                      {s.description && <p className="hb-todo-skill-desc">{s.description}</p>}
                      {recommendation?.matchedKeywords?.length && !trimmedQuery ? (
                        <ul className="hb-todo-tag-list">
                          {recommendation.matchedKeywords.slice(0, 5).map(keyword => (
                            <li key={keyword} className="hb-todo-tag is-mini is-reason">{keyword}</li>
                          ))}
                        </ul>
                      ) : null}
                      {s.tags && s.tags.length > 0 && (
                        <ul className="hb-todo-tag-list">
                          {s.tags.slice(0, 5).map(t => <li key={t} className="hb-todo-tag is-mini">{t}</li>)}
                        </ul>
                      )}
                    </div>
                    <div className="hb-todo-skill-actions">
                      <button
                        type="button"
                        className={clsx('hb-todo-row-btn', linkedNow ? 'is-ghost' : 'is-primary')}
                        disabled={linkedNow}
                        onClick={() => handleLink(s)}
                      >
                        {linkedNow ? t('hiring.todo.skill.linked') : t('hiring.todo.skill.link')}
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
            {linked.map(l => (
              <li key={l.skillId} className="hb-todo-skill-linked-item">
                <div className="hb-todo-skill-main">
                  <div className="hb-todo-skill-title-row">
                    <strong>{l.name}</strong>
                    <div className="hb-todo-skill-chips">
                      {l.version && <span className="hb-todo-skill-chip is-meta">{`v${l.version}`}</span>}
                    </div>
                  </div>
                </div>
                <div className="hb-todo-skill-actions">
                  <button
                    type="button"
                    className="hb-todo-row-btn is-ghost"
                    onClick={() => handleUnlink(l.skillId)}
                  >
                    {t('hiring.todo.skill.unlink')}
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
