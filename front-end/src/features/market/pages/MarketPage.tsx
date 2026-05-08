import { useEffect, useMemo, useState } from 'react'
import { Search, Sparkles, Star, Users } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { api, type EmployeeTemplateCard, type EmployeeTemplateListData } from '@/infra/api'

const PAGE_SIZE = 9

const EMPTY_LIST: EmployeeTemplateListData = {
  page: 1,
  pageSize: PAGE_SIZE,
  totalCount: 0,
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

function tagColor(index: number) {
  const mods = ['blue', 'orange', 'green', 'gray', 'pink', 'purple'] as const
  return mods[index % mods.length]
}

function TemplateCard({ template, onClick }: { template: EmployeeTemplateCard; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className="hb-card p-5 text-left transition-all hover:-translate-y-0.5">
      <div className="mb-3 flex items-start gap-3">
        <span className="hb-squircle h-11 w-11 bg-[#dde9ff] text-[#3d5cff]">
          {template.name.slice(0, 1)}
        </span>

        <div className="min-w-0 flex-1">
          <div className="flex items-start justify-between gap-2">
            <h3 className="truncate text-[15px] font-semibold text-[#0a0a0a]">{template.name}</h3>
            <span className={`hb-pill ${template.isAvailable ? 'blue' : 'gray'}`}>
              {template.isAvailable ? '模板' : '暂不可用'}
            </span>
          </div>
          <p className="mt-1 line-clamp-2 text-xs text-[#737373]">{template.tagline}</p>
        </div>
      </div>

      <p className="line-clamp-2 min-h-10 text-sm leading-relaxed text-[#404040]">{template.tagline}</p>

      <div className="mt-3 flex flex-wrap gap-2">
        {template.coreAbilityTags.slice(0, 4).map((tag, index) => (
          <span key={tag} className={`hb-pill ${tagColor(index)}`}>
            {tag}
          </span>
        ))}
      </div>

      <div className="mt-4 flex items-center justify-between border-t border-[#f5f5f5] pt-3 text-xs text-[#737373]">
        <span className="inline-flex items-center gap-1.5">
          <Users size={12} />
          {template.trustProof.hiredCount} 部门已用
        </span>
        <span className="inline-flex items-center gap-1.5">
          <Star size={12} className="text-[#c47a26]" />
          {template.trustProof.avgRating.toFixed(1)} / {template.trustProof.successRate.toFixed(1)}%
        </span>
      </div>
    </button>
  )
}

export default function MarketPage() {
  const navigate = useNavigate()
  const [searchInput, setSearchInput] = useState('')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(1)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [listData, setListData] = useState<EmployeeTemplateListData>(EMPTY_LIST)

  useEffect(() => {
    const timer = window.setTimeout(() => {
      setPage(1)
      setQuery(searchInput.trim())
    }, 300)

    return () => window.clearTimeout(timer)
  }, [searchInput])

  useEffect(() => {
    let cancelled = false

    async function loadTemplates() {
      setLoading(true)
      setError('')

      try {
        const data = await api.employeeTemplate.getList({
          q: query,
          page,
          pageSize: PAGE_SIZE,
        })
        if (!cancelled) {
          setListData(data)
        }
      } catch (requestError: unknown) {
        if (!cancelled) {
          setListData(EMPTY_LIST)
          setError(requestError instanceof Error ? requestError.message : '加载模板池失败')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void loadTemplates()

    return () => {
      cancelled = true
    }
  }, [page, query])

  const totalPages = Math.max(1, Math.ceil(listData.totalCount / PAGE_SIZE))
  const visiblePages = useMemo(() => getVisiblePages(page, totalPages), [page, totalPages])

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">部门长入口</span>
          <h1 className="hb-page-title">
            从模板池出发，完成一条完整的 <span className="accent">部门版雇佣</span> 流程
          </h1>
          <p className="hb-page-copy">
            这里不生产模板，只负责选择已有模板并进入正式雇佣。页面动作收敛为搜索、查看详情、发起雇佣。
          </p>
        </div>
      </div>

      <div className="hb-search-shell">
        <Search size={16} />
        <input
          value={searchInput}
          onChange={(event) => setSearchInput(event.target.value)}
          placeholder="搜索模板名称、场景、能力关键词"
          className="hb-search-input"
        />
        <div className="hb-search-controls">
          <button type="button" className="hb-btn-primary">
            <Sparkles size={14} />
            探索全部模板
          </button>
        </div>
      </div>

      <div className="mt-6 flex flex-wrap items-center justify-between gap-3">
        <div className="hb-chip-row">
          <span className="hb-pill blue">全部</span>
          <span className="hb-pill gray">全局通用</span>
          <span className="hb-pill gray">企业专属</span>
        </div>
        <span className="text-xs text-[#737373]">共 {listData.totalCount} 个模板</span>
      </div>

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <span>{error}</span>
        </div>
      ) : null}

      <div className="mt-5">
        {loading ? (
          <div className="hb-card py-20 text-center text-sm text-[#737373]">
            模板加载中...
          </div>
        ) : listData.items.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">没有找到匹配模板</div>
            <div className="hb-empty-copy">尝试更换关键词，或等待更多模板被同步到模板池中。</div>
          </div>
        ) : (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
            {listData.items.map((template) => (
              <TemplateCard
                key={template.templateId}
                template={template}
                onClick={() => navigate(`/templates/${template.templateId}`)}
              />
            ))}
          </div>
        )}
      </div>

      {listData.items.length > 0 ? (
        <div className="mt-8 flex flex-wrap items-center justify-center gap-2">
          <button
            type="button"
            disabled={page <= 1}
            onClick={() => setPage((current) => Math.max(1, current - 1))}
            className="hb-btn-ghost !px-4 !py-2 !text-sm"
          >
            上一页
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
            下一页
          </button>
        </div>
      ) : null}

      <p className="mt-8 text-center text-xs text-[#737373]">
        📝 模板池只展示企业可雇佣的模板，新增模板统一由构建端生产。
      </p>
    </div>
  )
}
