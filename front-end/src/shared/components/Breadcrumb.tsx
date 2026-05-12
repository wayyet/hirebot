import { ArrowLeft, ChevronRight } from 'lucide-react'
import { Link } from 'react-router-dom'

export interface BreadcrumbItem {
  label: string
  to?: string
}

interface BreadcrumbProps {
  items: BreadcrumbItem[]
}

/**
 * 通用面包屑导航组件
 * 第一项带返回箭头和链接，最后一项为当前页高亮显示
 */
export function Breadcrumb({ items }: BreadcrumbProps) {
  return (
    <nav className="hb-breadcrumb" aria-label="breadcrumb">
      {items.map((item, index) => {
        const isFirst = index === 0
        const isLast = index === items.length - 1

        return (
          <span key={index} className="hb-breadcrumb-item" style={{ display: 'inline-flex', alignItems: 'center', gap: 6 }}>
            {index > 0 && <ChevronRight size={13} className="hb-breadcrumb-sep" />}
            {!isLast && item.to ? (
              <Link to={item.to}>
                {isFirst && <ArrowLeft size={13} />}
                {item.label}
              </Link>
            ) : (
              <span className={isLast ? 'hb-breadcrumb-current' : undefined}>
                {isFirst && <ArrowLeft size={13} style={{ marginRight: 4 }} />}
                {item.label}
              </span>
            )}
          </span>
        )
      })}
    </nav>
  )
}
