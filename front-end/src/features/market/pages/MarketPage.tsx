import { useEffect, useMemo, useState } from 'react'
import { Clock, Search } from 'lucide-react'
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

function formatDate(value?: string | null) {
  if (!value) return '--'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleDateString('zh-CN', { year: 'numeric', month: '2-digit', day: '2-digit' })
}

function tagColor(index: number) {
  const mods = ['blue', 'orange', 'green', 'gray', 'pink', 'purple'] as const
  return mods[index % mods.length]
}

function TemplateCard({ template, onClick }: { template: EmployeeTemplateCard; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className="group relative flex h-full flex-col overflow-hidden rounded-2xl border border-[var(--hb-border)] bg-[var(--hb-surface-card)] p-6 text-left shadow-sm backdrop-blur-md transition-all hover:-translate-y-0.5 hover:shadow-lg hover:opacity-90">
      <h3 className="text-base font-semibold text-[var(--hb-near-black)]">{template.name}</h3>

      <p className="mt-1.5 line-clamp-2 text-sm leading-relaxed text-[var(--hb-body)]">{template.positioning}</p>

      <div className="mt-4 flex flex-wrap gap-2">
        {template.tags.slice(0, 4).map((tag, index) => (
          <span key={tag} className={`hb-pill ${tagColor(index)}`}>
            {tag}
          </span>
        ))}
      </div>

      <div className="mt-auto pt-4">
        <div className="flex items-center gap-1.5 border-t border-[var(--hb-border)] pt-3 text-xs text-[var(--hb-soft)]">
          <Clock size={12} />
          <span>更新于 {formatDate(template.updatedAt)}</span>
        </div>
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
    if (page > totalPages) {
      setPage(totalPages)
    }
  }, [page, totalPages])

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <h1 className="hb-page-title">{t('market.title')}</h1>
          <p className="hb-page-copy">
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

      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
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
          {availableTags.map((tag, index) => (
            <button
              key={tag}
              type="button"
              className={`hb-market-tag-button ${selectedTag === tag ? 'is-active' : ''} is-tone-${tagColor(index)}`}
              onClick={() => {
                setSelectedTag((current) => current === tag ? '' : tag)
                setPage(1)
              }}
              aria-pressed={selectedTag === tag}
            >
              {tag}
            </button>
          ))}
        </div>
        <span className="text-xs text-[var(--hb-soft)]">{t('market.templateCount', { count: data.total })}</span>
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
          <div className={`grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3${isPlaceholderData ? ' opacity-60 transition-opacity' : ''}`}>
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
