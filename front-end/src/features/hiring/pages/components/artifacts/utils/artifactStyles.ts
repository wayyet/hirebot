/**
 * artifactStyles.ts - Artifact 样式常量
 * 
 * 包含 artifact 渲染所需的 CSS 样式对象和样式工厂函数
 */

import type { CSSProperties } from 'react'

/**
 * 进度条轨道样式
 */
export const progressTrackStyle: CSSProperties = {
  height: 7,
  borderRadius: 99,
  background: 'var(--hb-surface-soft, #f3f4f6)',
  overflow: 'hidden',
  border: '1px solid var(--hb-border, #e5e7eb)',
}

/**
 * 进度条填充样式
 */
export const progressFillStyle: CSSProperties = {
  height: '100%',
  background: 'var(--hb-primary, #2563eb)',
  transition: 'width 0.3s ease',
}

/**
 * 表格单元格样式工厂函数
 * @param header - 是否为表头单元格
 */
export function cellStyle(header: boolean): CSSProperties {
  return {
    padding: '7px 9px',
    borderBottom: '1px solid var(--hb-border, #e5e7eb)',
    textAlign: 'left',
    fontWeight: header ? 700 : 400,
    background: header ? 'var(--hb-surface-soft, #f9fafb)' : 'transparent',
    whiteSpace: 'nowrap',
    fontSize: 12,
  }
}

/**
 * 代码块样式
 */
export const codeStyle: CSSProperties = {
  margin: 0,
  padding: '9px 10px',
  borderRadius: 8,
  border: '1px solid var(--hb-border, #e5e7eb)',
  background: 'var(--hb-surface-soft, #f9fafb)',
  overflowX: 'auto',
  fontSize: 12,
  lineHeight: 1.55,
  maxHeight: 280,
}

/**
 * 区段标签样式（用于技能卡片等区段标题）
 */
export const sectionLabelStyle: CSSProperties = {
  fontSize: 11,
  fontWeight: 600,
  letterSpacing: 0.4,
  textTransform: 'uppercase',
  color: 'var(--hb-text-muted, #9ca3af)',
}

/**
 * 阈值表格单元格样式工厂函数
 * @param header - 是否为表头单元格
 */
export function thresholdCellStyle(header: boolean): CSSProperties {
  return {
    padding: '5px 8px',
    textAlign: 'left',
    fontSize: 11,
    borderBottom: '1px solid var(--hb-border, #e5e7eb)',
    fontWeight: header ? 700 : 400,
    background: header ? 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 80%, transparent)' : 'transparent',
    whiteSpace: 'nowrap',
  }
}

/**
 * 统计芯片样式（用于外部系统能力等统计标签）
 */
export const statChipStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  fontSize: 11,
  padding: '2px 8px',
  borderRadius: 99,
  border: '1px solid var(--hb-border, #e5e7eb)',
  background: 'color-mix(in srgb, var(--hb-surface-soft, #f9fafb) 60%, transparent)',
  color: 'var(--hb-text-muted, #6b7280)',
}
