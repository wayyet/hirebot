import { useTranslation } from 'react-i18next'
import type { ArtifactDisplayData } from '../hiringPageTypes'
import { BadgeView, CodeView, ProgressView, TableView, TextView } from './artifacts/BaseArtifactViews'
import {
  ExternalConfigCommittedView,
  ExternalWorkorderSummaryView,
} from './artifacts/ExternalArtifactView'
import { MaterialHandoffView } from './artifacts/MaterialArtifactView'
import { OntologyExtractionView } from './artifacts/OntologyArtifactView'
import {
  PackagingTestCasesStatusView,
  Stage4PackagingView,
} from './artifacts/PackagingArtifactView'
import {
  SkillGenerationStatusView,
  SkillWorkorderSummaryView,
} from './artifacts/SkillArtifactView'
import {
  asRecord,
  hasExternalWorkorderShape,
  hasSkillWorkorderShape,
} from './artifacts/utils/artifactHelpers'

interface Props {
  artifact: ArtifactDisplayData
  /** 带 token 的文件下载回调；未提供时退化为直接 <a href> */
  onFileDownload?: (url: string, fileName: string, artifactType: string) => void
  /** 手动触发上传到系统（仅 template_package 展示） */
  onManualUpload?: (fileUrl: string, fileName: string) => void
  /** template_package：展示用 final 文件名（覆盖沙箱 artifact.fileName） */
  packageDownloadFileName?: string
  /** template_package：import 完成前禁用下载 */
  packageDownloadDisabled?: boolean
  /** 禁用时的 title / aria-label */
  packageDownloadDisabledTitle?: string
}

export function ArtifactMessageCard({
  artifact,
  onFileDownload,
  onManualUpload,
  packageDownloadFileName,
  packageDownloadDisabled = false,
  packageDownloadDisabledTitle,
}: Props) {
  const { t } = useTranslation()
  const title = artifact.label ?? artifact.artifactType
  const isPackage = artifact.artifactType === 'template_package'
  const displayFileName =
    isPackage && packageDownloadFileName
      ? packageDownloadFileName
      : (artifact.fileName ?? title)

  return (
    <div className="hb-artifact-card">
      <div className="hb-artifact-header">
        <ArtifactIcon artifact={artifact} />
        <div className="hb-artifact-title-group">
          <span className="hb-artifact-title">{title}</span>
          {(artifact.skillName || artifact.stage) && (
            <span className="hb-artifact-subtitle">
              {[toArtifactSubtitleLabel(artifact.skillName), toArtifactSubtitleLabel(artifact.stage), artifact.isTerminal ? t('hiring.artifact.terminal') : null]
                .filter(Boolean)
                .join(' · ')}
            </span>
          )}
        </div>
      </div>

      {artifact.kind === 'file' ? (
        <div className="hb-artifact-file-row">
          {onFileDownload && artifact.fileUrl ? (
            // template_package：仅下载后端 final；import 前禁用，不走沙箱网关
            <button
              type="button"
              className="hb-artifact-file-link"
              disabled={isPackage && packageDownloadDisabled}
              title={isPackage && packageDownloadDisabled ? packageDownloadDisabledTitle : undefined}
              aria-label={
                isPackage && packageDownloadDisabled && packageDownloadDisabledTitle
                  ? packageDownloadDisabledTitle
                  : displayFileName
              }
              onClick={() => {
                if (isPackage && packageDownloadDisabled) return
                onFileDownload(artifact.fileUrl!, displayFileName, artifact.artifactType)
              }}
            >
              <span className="hb-artifact-file-name">{displayFileName}</span>
              {artifact.sizeLabel && <span className="hb-artifact-file-size">{artifact.sizeLabel}</span>}
            </button>
          ) : (
            <a
              href={artifact.fileUrl ?? '#'}
              download={displayFileName}
              className="hb-artifact-file-link"
            >
              <span className="hb-artifact-file-name">{displayFileName}</span>
              {artifact.sizeLabel && <span className="hb-artifact-file-size">{artifact.sizeLabel}</span>}
            </a>
          )}
          {isPackage && onManualUpload && artifact.fileUrl && (
            <button
              type="button"
              className="hb-artifact-action-btn"
              onClick={() => onManualUpload(artifact.fileUrl!, artifact.fileName ?? title)}
            >
              {t('hiring.artifact.manualImport')}
            </button>
          )}
        </div>
      ) : (
        <ArtifactDataView artifact={artifact} />
      )}
    </div>
  )
}

function toArtifactSubtitleLabel(value: string | undefined): string | undefined {
  if (!value) {
    return value
  }

  const labelMap: Record<string, string> = {
    'employment-coach-conversation': '雇佣教练',
    stage1_material: '资料阶段',
    stage2_skill: '技能阶段',
    stage3_external: '外部阶段',
    stage4_packaging: '打包阶段',
    'ontology-extraction': '业务整理',
    'ontology-projection': '技能准备',
    'skill-generation': '技能生成',
    'external-config': '外部配置',
    'packaging-test-cases': '评估用例准备',
  }

  return labelMap[value] ?? value
}

function ArtifactDataView({ artifact }: { artifact: ArtifactDisplayData }) {
  // 类型优先：特定 artifactType 使用内置专用视图
  if (artifact.artifactType === 'material_collection_progress' || artifact.artifactType === 'material_handoff_summary') {
    return <MaterialHandoffView data={artifact.data} />
  }
  if (artifact.artifactType === 'ontology_extraction_done' || artifact.artifactType === 'ontology_extraction_progress') {
    return <OntologyExtractionView data={artifact.data} />
  }
  if (artifact.artifactType === 'skill_workorder_progress' || artifact.artifactType === 'skill_workorder_summary') {
    return <SkillWorkorderSummaryView data={artifact.data} />
  }
  if (artifact.artifactType === 'external_workorder_progress' || artifact.artifactType === 'external_workorder_summary') {
    return <ExternalWorkorderSummaryView data={artifact.data} />
  }
  if (
    artifact.artifactType === 'skill_generation_ready' ||
    artifact.artifactType === 'skill_projection_binding_ready' ||
    artifact.artifactType === 'skill_generation_progress' ||
    artifact.artifactType === 'skill_generation_done'
  ) {
    return <SkillGenerationStatusView artifactType={artifact.artifactType} data={artifact.data} />
  }
  if (
    artifact.artifactType === 'packaging_testcases_ready' ||
    artifact.artifactType === 'packaging_testcases_progress' ||
    artifact.artifactType === 'packaging_testcases_done'
  ) {
    return <PackagingTestCasesStatusView artifactType={artifact.artifactType} data={artifact.data} />
  }
  if (artifact.artifactType === 'external_config_committed') {
    return <ExternalConfigCommittedView data={artifact.data} />
  }
  if (artifact.artifactType === 'stage4_packaging') return <Stage4PackagingView data={artifact.data} />
  // 结构化兜底：未命中类型但数据具备对应特征时自动使用专用视图
  const _d = asRecord(artifact.data)
  if (_d && hasExternalWorkorderShape(_d)) {
    return <ExternalWorkorderSummaryView data={artifact.data} />
  }
  if (_d && hasSkillWorkorderShape(_d)) {
    return <SkillWorkorderSummaryView data={artifact.data} />
  }
  const hint = artifact.displayHint ?? 'text'
  if (hint === 'progress') return <ProgressView data={artifact.data} />
  if (hint === 'table') return <TableView data={artifact.data} />
  if (hint === 'badge') return <BadgeView data={artifact.data} />
  if (hint === 'code') return <CodeView data={artifact.data} />
  if (hint === 'tree') return <CodeView data={artifact.data} />
  return <TextView data={artifact.data} />
}


function ArtifactIcon({ artifact }: { artifact: ArtifactDisplayData }) {
  if (artifact.kind === 'file') return <span className="hb-artifact-icon">📄</span>
  if (artifact.artifactType === 'skill_workorder_progress' || artifact.artifactType === 'skill_workorder_summary') return <span className="hb-artifact-icon">🧩</span>
  if (
    artifact.artifactType === 'skill_generation_ready' ||
    artifact.artifactType === 'skill_projection_binding_ready' ||
    artifact.artifactType === 'skill_generation_progress' ||
    artifact.artifactType === 'skill_generation_done'
  ) return <span className="hb-artifact-icon">⚙️</span>
    if (
        artifact.artifactType === 'packaging_testcases_ready' ||
        artifact.artifactType === 'packaging_testcases_progress' ||
        artifact.artifactType === 'packaging_testcases_done'
    ) return <span className="hb-artifact-icon">🧪</span>
    if (artifact.artifactType === 'material_collection_progress' || artifact.artifactType === 'material_handoff_summary') return <span className="hb-artifact-icon">📋</span>
  if (artifact.artifactType === 'ontology_extraction_done' || artifact.artifactType === 'ontology_extraction_progress') return <span className="hb-artifact-icon">🌿</span>
  if (artifact.artifactType === 'external_workorder_progress' || artifact.artifactType === 'external_workorder_summary' || artifact.artifactType === 'external_config_committed') return <span className="hb-artifact-icon">🔌</span>
  if (artifact.artifactType === 'stage4_packaging') return <span className="hb-artifact-icon">📦</span>
  const map: Record<string, string> = { table: '📊', code: '💻', tree: '🌿', badge: '✅', progress: '⏳' }
  return <span className="hb-artifact-icon">{map[artifact.displayHint ?? ''] ?? '📦'}</span>
}

