import { ChevronLeft, ChevronRight } from 'lucide-react'

interface PaginationProps {
  page: number
  totalPages: number
  onChange: (page: number) => void
  showWhenSinglePage?: boolean
}

export function Pagination({
  page,
  totalPages,
  onChange,
  showWhenSinglePage = false,
}: PaginationProps) {
  const normalizedTotalPages = Math.max(1, totalPages)

  if (!showWhenSinglePage && normalizedTotalPages <= 1) {
    return null
  }

  const pages: Array<number | '...'> = []

  if (normalizedTotalPages <= 7) {
    for (let current = 1; current <= normalizedTotalPages; current += 1) {
      pages.push(current)
    }
  } else {
    pages.push(1)

    if (page > 3) {
      pages.push('...')
    }

    for (
      let current = Math.max(2, page - 1);
      current <= Math.min(normalizedTotalPages - 1, page + 1);
      current += 1
    ) {
      pages.push(current)
    }

    if (page < normalizedTotalPages - 2) {
      pages.push('...')
    }

    pages.push(normalizedTotalPages)
  }

  return (
    <div className="hb-pagination" aria-label="分页导航">
      <button
        type="button"
        className="hb-pagination-btn"
        onClick={() => onChange(page - 1)}
        disabled={page === 1}
        aria-label="上一页"
      >
        <ChevronLeft size={14} />
      </button>

      {pages.map((item, index) =>
        item === '...'
          ? (
              <span key={`ellipsis-${index}`} className="hb-pagination-ellipsis">
                ...
              </span>
            )
          : (
              <button
                key={item}
                type="button"
                className={`hb-pagination-btn ${item === page ? 'is-active' : ''}`}
                onClick={() => onChange(item)}
                aria-current={item === page ? 'page' : undefined}
              >
                {item}
              </button>
            ),
      )}

      <button
        type="button"
        className="hb-pagination-btn"
        onClick={() => onChange(page + 1)}
        disabled={page === normalizedTotalPages}
        aria-label="下一页"
      >
        <ChevronRight size={14} />
      </button>
    </div>
  )
}