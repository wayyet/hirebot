import { useEffect, useState } from 'react'
import { AlertCircle, Loader2 } from 'lucide-react'
import { useParams } from 'react-router-dom'
import { api, type SkillDetail } from '@/infra/api'
import { Breadcrumb } from '@/shared/components/Breadcrumb'

function toneForStatus(status: string) {
  if (status.includes('上架')) return 'green'
  if (status.includes('审核')) return 'orange'
  return 'gray'
}

function toneForLevel(level: string) {
  if (level.toUpperCase().includes('L3')) return 'pink'
  if (level.toUpperCase().includes('L2')) return 'purple'
  return 'blue'
}

export default function SkillDetailPage() {
  const { id } = useParams<{ id: string }>()

  const [skill, setSkill] = useState<SkillDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!id) return
    let cancelled = false
    const skillId = id

    async function loadSkill() {
      setLoading(true)
      setError('')
      try {
        const data = await api.skillCatalog.getSkill(skillId)
        if (!cancelled) {
          setSkill(data)
        }
      } catch (requestError: unknown) {
        if (!cancelled) {
          setError(requestError instanceof Error ? requestError.message : '加载技能详情失败')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void loadSkill()

    return () => {
      cancelled = true
    }
  }, [id])

  return (
    <div className="hb-page">
      <Breadcrumb items={[{ label: 'Skill 列表', to: '/skill' }, { label: skill?.name ?? '技能详情' }]} />

      {error ? (
        <div className="hb-alert hb-alert-error mt-5">
          <AlertCircle size={14} />
          <span>{error}</span>
        </div>
      ) : null}

      <div className="mt-5">
        {loading ? (
          <div className="hb-card flex min-h-52 items-center justify-center gap-2 p-10 text-[#737373]">
            <Loader2 size={16} className="animate-spin" />
            加载技能详情中...
          </div>
        ) : !skill ? (
          <div className="hb-empty">
            <div className="hb-empty-title">技能不存在</div>
            <div className="hb-empty-copy">当前技能可能已下架或还未同步到前端目录。</div>
          </div>
        ) : (
          <>
            <section className="hb-section mt-5">
              <div className="flex flex-wrap items-start gap-4">
                <span className="hb-squircle h-16 w-16 bg-[#dde9ff] text-2xl text-[#3d5cff]">
                  {skill.name.slice(0, 1)}
                </span>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={`hb-pill ${toneForLevel(skill.level)}`}>{skill.level}</span>
                    <span className={`hb-pill ${toneForStatus(skill.status)}`}>{skill.status}</span>
                    <span className="hb-pill gray">{skill.version}</span>
                  </div>
                  <h1 className="hb-page-title mt-3 !text-[34px]">{skill.name}</h1>
                  <p className="hb-page-copy max-w-none">{skill.description}</p>
                </div>
              </div>

              <div className="hb-stat-grid mt-6">
                <div className="hb-stat-card">
                  <div className="hb-stat-label">更新时间</div>
                  <div className="hb-stat-value !text-[24px]">{skill.updatedAt}</div>
                </div>
                <div className="hb-stat-card">
                  <div className="hb-stat-label">关联模板</div>
                  <div className="hb-stat-value">{skill.boundTemplates.length}</div>
                </div>
                <div className="hb-stat-card">
                  <div className="hb-stat-label">文件数</div>
                  <div className="hb-stat-value">{skill.files.length}</div>
                </div>
              </div>
            </section>

            <section className="hb-section mt-5">
              <div className="hb-section-head">
                <div>
                  <h2 className="hb-section-title">输入 / 输出示例</h2>
                  <p className="hb-section-copy">用真实样例快速判断这个技能是否适合当前挂载场景。</p>
                </div>
              </div>
              <div className="grid gap-4 lg:grid-cols-2">
                <div>
                  <div className="mb-2 text-sm font-medium text-[#404040]">输入示例</div>
                  <pre className="hb-code-block whitespace-pre-wrap">{skill.inputExample || '暂无示例'}</pre>
                </div>
                <div>
                  <div className="mb-2 text-sm font-medium text-[#404040]">输出示例</div>
                  <pre className="hb-code-block whitespace-pre-wrap">{skill.outputExample || '暂无示例'}</pre>
                </div>
              </div>
            </section>

            <section className="hb-section mt-5">
              <div className="hb-section-head">
                <div>
                  <h2 className="hb-section-title">绑定关系</h2>
                  <p className="hb-section-copy">查看技能当前挂载范围，以及交付物落在哪些文件。</p>
                </div>
              </div>
              <div className="grid gap-5 lg:grid-cols-3">
                <div>
                  <div className="mb-2 text-sm font-medium text-[#404040]">关联模板</div>
                  <div className="flex flex-wrap gap-2">
                    {skill.boundTemplates.length > 0
                      ? skill.boundTemplates.map((item) => <span key={item} className="hb-pill blue">{item}</span>)
                      : <span className="text-sm text-[#737373]">无</span>}
                  </div>
                </div>
                <div>
                  <div className="mb-2 text-sm font-medium text-[#404040]">标签</div>
                  <div className="flex flex-wrap gap-2">
                    {skill.tags.length > 0
                      ? skill.tags.map((item, index) => <span key={item} className={`hb-pill ${index % 2 === 0 ? 'orange' : 'green'}`}>{item}</span>)
                      : <span className="text-sm text-[#737373]">无</span>}
                  </div>
                </div>
                <div>
                  <div className="mb-2 text-sm font-medium text-[#404040]">文件</div>
                  <div className="space-y-2">
                    {skill.files.length > 0
                      ? skill.files.map((item) => (
                        <div key={item} className="rounded-xl border border-[#ececec] bg-[#fafafa] px-3 py-2 text-xs text-[#404040]">
                          {item}
                        </div>
                      ))
                      : <span className="text-sm text-[#737373]">无</span>}
                  </div>
                </div>
              </div>
            </section>
          </>
        )}
      </div>
    </div>
  )
}
