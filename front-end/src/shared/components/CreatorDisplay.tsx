import { useTranslation } from 'react-i18next'
import type { CreatorRef } from '@/infra/api'
import { formatCreatorAvatarFromRef, formatCreatorFromRef } from '@/shared/utils/creator-display'

interface CreatorAvatarProps {
  creator: CreatorRef | undefined | null
  size?: number
  className?: string
}

/**
 * 创建人头像组件
 * 显示创建人的首字母头像
 */
export function CreatorAvatar({ creator, size = 32, className = '' }: CreatorAvatarProps) {
  const { i18n } = useTranslation()
  
  const initial = formatCreatorAvatarFromRef(creator, i18n.language)
  const name = formatCreatorFromRef(creator, i18n.language)

  return (
    <div
      className={`creator-avatar ${className}`}
      style={{
        width: size,
        height: size,
        borderRadius: '50%',
        backgroundColor: 'var(--color-primary-light, #e3f2fd)',
        color: 'var(--color-primary, #1976d2)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        fontSize: size * 0.5,
        fontWeight: 600,
        flexShrink: 0,
      }}
      title={name}
    >
      {initial}
    </div>
  )
}

interface CreatorDisplayProps {
  creator: CreatorRef | undefined | null
  showAvatar?: boolean
  avatarSize?: number
  className?: string
}

/**
 * 创建人信息展示组件
 * 显示创建人的头像和姓名
 */
export function CreatorDisplay({ 
  creator, 
  showAvatar = true, 
  avatarSize = 24,
  className = '' 
}: CreatorDisplayProps) {
  const { i18n } = useTranslation()
  
  const name = formatCreatorFromRef(creator, i18n.language)

  return (
    <div 
      className={`creator-display ${className}`}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
      }}
    >
      {showAvatar && <CreatorAvatar creator={creator} size={avatarSize} />}
      <span 
        style={{ 
          fontSize: 13, 
          color: 'var(--color-text-secondary, #666)',
        }}
      >
        {name}
      </span>
    </div>
  )
}

interface CreatorBadgeProps {
  creator: CreatorRef | undefined | null
  label?: string
  className?: string
}

/**
 * 创建人徽章组件
 * 用于在列表或卡片中显示创建人标签
 */
export function CreatorBadge({ creator, label = '创建人', className = '' }: CreatorBadgeProps) {
  const { i18n } = useTranslation()
  
  const name = formatCreatorFromRef(creator, i18n.language)

  return (
    <div 
      className={`creator-badge ${className}`}
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '4px 10px',
        borderRadius: 6,
        backgroundColor: 'var(--color-bg-secondary, #f5f5f5)',
        fontSize: 12,
      }}
    >
      <span style={{ color: 'var(--color-text-tertiary, #999)' }}>{label}:</span>
      <span style={{ color: 'var(--color-text-primary, #333)', fontWeight: 500 }}>
        {name}
      </span>
    </div>
  )
}
