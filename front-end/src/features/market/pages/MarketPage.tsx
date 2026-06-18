import { useEffect, useMemo, useState } from 'react'
import { Clock, GitBranch, Search, UserRound } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { api, type EmployeeTemplateCard, type EmployeeTemplateListData } from '@/infra/api'
import { Pagination } from '@/shared/components/Pagination'

const PAGE_SIZE = 9

const EMPTY_LIST: EmployeeTemplateListData = {
  page: 1,
  pageSize: PAGE_SIZE,
  total: 0,
  items: [],
}

function formatVersionLabel(version?: string | null) {
  const clean = version?.trim()
  if (!clean) return '--'
  if (clean.startsWith('v')) return clean
  if (clean.startsWith('V')) return `v${clean.slice(1)}`
  return `v${clean}`
}

function formatCreatorName(
  template: EmployeeTemplateCard,
  locale: string,
) {
  const creator = template.createdBy
  const displayName = creator?.displayName?.trim()
  if (displayName) return displayName

  const familyName = creator?.familyName?.trim()
  const givenName = creator?.givenName?.trim()
  if (familyName && givenName) {
    return locale.toLowerCase().startsWith('zh')
      ? `${familyName}${givenName}`
      : `${givenName} ${familyName}`
  }
  if (familyName) return familyName
  if (givenName) return givenName

  const username = creator?.username?.trim()
  if (username) return username

  return template.createdByUserId?.trim() || '--'
}

function formatDate(value?: string | null) {
  if (!value) return '--'

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value

  return date.toLocaleDateString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  })
}

function TemplateCard({
  template,
  onClick,
}: {
  template: EmployeeTemplateCard
  onClick: () => void
}) {
  const { i18n } = useTranslation()
  const creatorName = formatCreatorName(template, i18n.language)

  return (
    <button type="button" onClick={onClick} className="hb-market-card">
      <div className="hb-market-card-heading">
        <h3 className="hb-market-card-title">{template.displayName || template.name}</h3>
        {template.currentVersion ? (
          <span className="hb-market-card-version">
            <GitBranch size={11} />
            {formatVersionLabel(template.currentVersion)}
          </span>
        ) : null}
      </div>
      <p className="hb-market-card-copy">{template.positioning}</p>

      <div className="hb-market-card-tags">
        {template.tags.slice(0, 4).map((tag) => (
          <span key={tag} className="hb-market-card-tag">
            {tag}
          </span>
        ))}
      </div>

      <div className="hb-market-card-meta">
        <span className="hb-market-card-meta-item" title={creatorName}>
          <UserRound size={12} />
          <span>{creatorName}</span>
        </span>
        <span className="hb-market-card-meta-item">
          <Clock size={12} />
          <span>{formatDate(template.updatedAt)}</span>
        </span>
      </div>
    </button>
  )
}

export default function MarketPage() {
  const navigate = useNavigate()
  const { t } = useTranslation()
  const [query, setQuery] = useState('')
  const [selectedTag, setSelectedTag] = useState('')
  const [page, setPage] = useState(1)

  const { data = EMPTY_LIST, isLoading, isPlaceholderData, error } = useQuery({
    queryKey: ['store-templates', query, selectedTag, page],
    queryFn: ({ signal }) =>
      api.employeeTemplate.getList({ q: query, tag: selectedTag || undefined, page, pageSize: PAGE_SIZE }, signal),
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
    placeholderData: keepPreviousData,
  })

  const { data: remoteTags = [] } = useQuery({
    queryKey: ['store-template-tags'],
    queryFn: async ({ signal }) => {
      try {
        return await api.employeeTemplate.getTags(signal)
      } catch {
        return []
      }
    },
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
  })

  const availableTags = useMemo(() => {
    if (remoteTags.length > 0) {
      return remoteTags
    }

    const tagSet = new Set<string>()
    for (const template of data.items) {
      for (const tag of template.tags ?? []) {
        if (tag) {
          tagSet.add(tag)
        }
      }
    }

    return Array.from(tagSet)
  }, [data.items, remoteTags])

  const totalPages = Math.max(1, Math.ceil(data.total / PAGE_SIZE))

  useEffect(() => {
    if (page <= totalPages) return

    queueMicrotask(() => {
      setPage(totalPages)
    })
  }, [page, totalPages])

  return (
    <div className="hb-page hb-market-page">
      <div className="hb-page-head">
        <div>
          <h1 className="hb-page-title">{t('market.title')}</h1>
          <p className="hb-page-copy hb-market-head-copy">
            {t('market.copy')}
            <span className="hb-page-copy-meta">
              · {t('market.templateCount', { count: data.total })}
            </span>
          </p>
        </div>
      </div>

      <div className="hb-search-shell hb-market-search-shell">
        <Search size={16} />
        <input
          value={query}
          onChange={(event) => {
            setQuery(event.target.value)
            setPage(1)
          }}
          placeholder={t('market.searchPlaceholder')}
          className="hb-search-input"
        />
        {query ? (
          <div className="hb-search-controls">
            <button
              type="button"
              className="hb-btn-ghost hb-hub-btn-secondary"
              onClick={() => {
                setQuery('')
                setPage(1)
              }}
            >
              {t('market.clearSearch')}
            </button>
          </div>
        ) : null}
      </div>

      <div className="hb-market-toolbar">
        <div className="hb-chip-row">
          <button
            type="button"
            className={`hb-market-tag-button ${selectedTag ? '' : 'is-active'}`}
            onClick={() => {
              setSelectedTag('')
              setPage(1)
            }}
            aria-pressed={!selectedTag}
          >
            {t('common.allTypes')}
          </button>
          {availableTags.map((tag) => (
            <button
              key={tag}
              type="button"
              className={`hb-market-tag-button ${selectedTag === tag ? 'is-active' : ''}`}
              onClick={() => {
                setSelectedTag((current) => (current === tag ? '' : tag))
                setPage(1)
              }}
              aria-pressed={selectedTag === tag}
            >
              {tag}
            </button>
          ))}
        </div>
        <span className="hb-market-count">{t('market.templateCount', { count: data.total })}</span>
      </div>

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <span>{error.message}</span>
        </div>
      ) : null}

      <div className="mt-5">
        {isLoading ? (
          <div className="hb-card py-20 text-center text-sm text-[var(--hb-soft)]">
            {t('market.loading')}
          </div>
        ) : data.items.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">{t('market.empty')}</div>
            <div className="hb-empty-copy">{t('market.emptyCopy')}</div>
          </div>
        ) : (
          <div className={`hb-market-grid${isPlaceholderData ? ' opacity-60 transition-opacity' : ''}`}>
            {data.items.map((template) => (
              <TemplateCard
                key={template.id}
                template={template}
                onClick={() => navigate(`/template-pool/templates/${template.id}`)}
              />
            ))}
          </div>
        )}
      </div>

      {data.items.length > 0 ? (
        <Pagination page={page} totalPages={totalPages} onChange={setPage} />
      ) : null}
    </div>
  )
}
