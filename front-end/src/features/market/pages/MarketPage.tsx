import { useEffect, useMemo, useState } from 'react'
import { Clock, Search, Sparkles } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useQuery, keepPreviousData } from '@tanstack/react-query'
import { api, type EmployeeTemplateCard, type EmployeeTemplateListData } from '@/infra/api'

const PAGE_SIZE = 9

const EMPTY_LIST: EmployeeTemplateListData = {
  page: 1,
  pageSize: PAGE_SIZE,
  total: 0,
  items: [],
}

function getVisiblePages(current: number, total: number): number[] {
  const start = Math.max(1, current - 2)
  const end = Math.min(total, start + 4)
  const fixedStart = Math.max(1, end - 4)

  const pages: number[] = []
  for (let index = fixedStart; index <= end; index += 1) {
    pages.push(index)
  }

  return pages
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
  const [searchInput, setSearchInput] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setPage(1)
      setQuery(searchInput.trim())
    }, 300)

    return () => window.clearTimeout(timer)
  }, [searchInput])

  const { data = EMPTY_LIST, isLoading, isPlaceholderData, error } = useQuery({
    queryKey: ['templates', query, page],
    queryFn: ({ signal }) =>
      api.employeeTemplate.getList({ q: query, page, pageSize: PAGE_SIZE }, signal),
    staleTime: 5 * 60 * 1000,
    gcTime: 30 * 60 * 1000,
    placeholderData: keepPreviousData,
  })

  const totalPages = Math.max(1, Math.ceil(data.total / PAGE_SIZE))
  const visiblePages = useMemo(() => getVisiblePages(page, totalPages), [page, totalPages])

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">{t('market.kicker')}</span>
          <h1 className="hb-page-title">{t('market.title')}</h1>
          <p className="hb-page-copy">
            {t('market.copy')}
          </p>
        </div>
      </div>

      <div className="hb-search-shell mx-auto">
        <Search size={16} />
        <input
          value={searchInput}
          onChange={(event) => setSearchInput(event.target.value)}
          placeholder={t('market.searchPlaceholder')}
          className="hb-search-input"
        />
        <div className="hb-search-controls">
          <button type="button" className="hb-btn-primary">
            <Sparkles size={14} />
            {t('market.exploreAll')}
          </button>
        </div>
      </div>

      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
        <div className="hb-chip-row">
          <span className="hb-pill blue">{t('common.allTypes')}</span>
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
                onClick={() => navigate(`/templates/${template.id}`)}
              />
            ))}
          </div>
        )}
      </div>

      {data.items.length > 0 ? (
        <div className="mt-4 flex flex-wrap items-center justify-center gap-2">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => setPage((current) => Math.max(1, current - 1))}
            className="hb-btn-ghost !px-4 !py-2 !text-sm"
          >
            {t('market.prevPage')}
          </button>

          {visiblePages.map((item) => (
            <button
              key={item}
              type="button"
              onClick={() => setPage(item)}
              className={`hb-chip ${item === page ? 'is-active' : ''}`}
            >
              {item}
            </button>
          ))}

          <button
            type="button"
            disabled={page >= totalPages}
            onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
            className="hb-btn-ghost !px-4 !py-2 !text-sm"
          >
            {t('market.nextPage')}
          </button>
        </div>
      ) : null}

      <p className="mt-8 text-center text-xs text-[var(--hb-caption)]">
        {t('market.footer')}
      </p>
    </div>
  )
}
