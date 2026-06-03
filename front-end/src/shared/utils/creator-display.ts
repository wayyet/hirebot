/**
 * 创建人信息引用类型
 */
export interface CreatorRef {
  username?: string
  displayName?: string
  familyName?: string
  givenName?: string
}

/**
 * 标准化语言标识
 */
function normalizeLocale(locale: string): string {
  return locale.toLowerCase()
}

/**
 * 首字母大写
 */
function capitalize(value?: string | null): string {
  const trimmed = value?.trim()
  if (!trimmed) return ''
  return trimmed.charAt(0).toUpperCase() + trimmed.slice(1)
}

/**
 * 根据语言环境格式化创建人姓名
 * 
 * @param familyName 姓氏
 * @param givenName 名字
 * @param locale 语言标识 (如 'zh-CN', 'en-US')
 * @returns 格式化后的姓名
 * 
 * @example
 * // 中文环境
 * formatCreatorName('张', '三', 'zh-CN') // '张三'
 * 
 * // 英文环境
 * formatCreatorName('Zhang', 'San', 'en-US') // 'San Zhang'
 */
export function formatCreatorName(
  familyName: string | undefined | null,
  givenName: string | undefined | null,
  locale: string,
): string {
  const family = capitalize(familyName)
  const given = capitalize(givenName)

  if (family && given) {
    // 中文：姓+名，英文：名+姓
    return normalizeLocale(locale).startsWith('zh')
      ? `${family}${given}`
      : `${given} ${family}`
  }
  if (family) return family
  if (given) return given
  return '—'
}

/**
 * 根据语言环境生成创建人头像首字母
 * 
 * @param familyName 姓氏
 * @param givenName 名字
 * @param locale 语言标识
 * @returns 头像显示的首字母
 * 
 * @example
 * // 中文环境（优先取姓氏）
 * formatCreatorAvatar('张', '三', 'zh-CN') // '张'
 * 
 * // 英文环境（优先取名字）
 * formatCreatorAvatar('Zhang', 'San', 'en-US') // 'S'
 */
export function formatCreatorAvatar(
  familyName: string | undefined | null,
  givenName: string | undefined | null,
  locale: string,
): string {
  const family = capitalize(familyName)
  const given = capitalize(givenName)
  const isZh = normalizeLocale(locale).startsWith('zh')
  
  // 中文取姓氏首字母，英文取名字首字母
  const primary = isZh ? family : given
  const fallback = isZh ? given : family

  return primary.charAt(0) || fallback.charAt(0) || '—'
}

/**
 * 从 CreatorRef 对象格式化创建人姓名
 */
export function formatCreatorFromRef(creator: CreatorRef | undefined | null, locale: string): string {
  if (!creator) return '—'
  return formatCreatorName(creator.familyName, creator.givenName, locale)
}

/**
 * 从 CreatorRef 对象生成创建人头像首字母
 */
export function formatCreatorAvatarFromRef(creator: CreatorRef | undefined | null, locale: string): string {
  if (!creator) return '—'
  return formatCreatorAvatar(creator.familyName, creator.givenName, locale)
}
