import { useEffect, useMemo, useState } from 'react'
import { AlertCircle, Loader2, Plus, Search, Sparkles } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { api, type SkillSummary } from '@/infra/api'

function levelTone(level: string) {
  if (level.toUpperCase().includes('L3')) return 'pink'
  if (level.toUpperCase().includes('L2')) return 'purple'
  return 'blue'
}

function statusTone(status: string) {
  if (status.includes('上架')) return 'green'
  if (status.includes('审核')) return 'orange'
  return 'gray'
}

export default function SkillListPage() {
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [level, setLevel] = useState('')
  const [status, setStatus] = useState('')
  const [skills, setSkills] = useState<SkillSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    let cancelled = false

    async function loadSkills() {
      setLoading(true)
      setError('')
      try {
        const data = await api.skillCatalog.getSkills({
          q: search.trim() || undefined,
          level: level || undefined,
          status: status || undefined,
        })
        if (!cancelled) {
          setSkills(data)
        }
      } catch (requestError: unknown) {
        if (!cancelled) {
          setError(requestError instanceof Error ? requestError.message : '加载技能列表失败')
        }
      } finally {
        if (!cancelled) {
          setLoading(false)
        }
      }
    }

    void loadSkills()

    return () => {
      cancelled = true
    }
  }, [search, level, status])

  const summary = useMemo(() => {
    return {
      total: skills.length,
      online: skills.filter((item) => item.status.includes('上架')).length,
      advanced: skills.filter((item) => item.level.toUpperCase().includes('L3')).length,
    }
  }, [skills])

  return (
    <div className="hb-page">
      <div className="hb-page-head">
        <div>
          <span className="hb-kicker">Skill Hub</span>
          <h1 className="hb-page-title">
            把可挂载能力收进同一套 <span className="accent">团队技能目录</span>
          </h1>
          <p className="hb-page-copy">
            这里展示后端真实技能状态，帮助你快速查看等级、版本、挂载范围和输入输出示例。
          </p>
        </div>
        <div className="hb-page-actions">
          <button
            type="button"
            onClick={() => navigate('/skill/register')}
            className="hb-btn-primary"
          >
            <Plus size={14} />
            注册 Skill
          </button>
        </div>
      </div>

      <div className="hb-stat-grid">
        <div className="hb-stat-card">
          <div className="hb-stat-label"><Sparkles size={14} /> 当前可见</div>
          <div className="hb-stat-value">{summary.total}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">已上架</div>
          <div className="hb-stat-value">{summary.online}</div>
        </div>
        <div className="hb-stat-card">
          <div className="hb-stat-label">高级技能</div>
          <div className="hb-stat-value">{summary.advanced}</div>
          <div className="hb-stat-note">L3 及以上</div>
        </div>
      </div>

      <div className="hb-section mt-5">
        <div className="hb-section-head">
          <div>
            <h2 className="hb-section-title">筛选与检索</h2>
            <p className="hb-section-copy">按名称、等级和状态快速定位要挂载的技能。</p>
          </div>
        </div>
        <div className="hb-search-shell max-w-none">
          <Search size={16} />
          <input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="搜索技能名称或描述"
            className="hb-search-input"
          />
          <div className="hb-search-controls">
            <select
              value={level}
              onChange={(event) => setLevel(event.target.value)}
              className="hb-select min-w-[120px]"
            >
              <option value="">全部等级</option>
              <option value="L1">L1</option>
              <option value="L2">L2</option>
              <option value="L3">L3</option>
            </select>
            <select
              value={status}
              onChange={(event) => setStatus(event.target.value)}
              className="hb-select min-w-[140px]"
            >
              <option value="">全部状态</option>
              <option value="上架中">上架中</option>
              <option value="下架中">下架中</option>
            </select>
          </div>
        </div>
      </div>

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
            加载技能列表中...
          </div>
        ) : skills.length === 0 ? (
          <div className="hb-empty">
            <div className="hb-empty-title">暂无匹配的技能</div>
            <div className="hb-empty-copy">可以更换筛选条件，或去注册页上传新的技能包。</div>
          </div>
        ) : (
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {skills.map((skill) => (
              <button
                key={skill.skillId}
                type="button"
                onClick={() => navigate(`/skill/${skill.skillId}`)}
                className="hb-card p-5 text-left transition-transform duration-150 hover:-translate-y-0.5"
              >
                <div className="mb-3 flex items-start gap-3">
                  <span className="hb-squircle h-11 w-11 bg-[#dde9ff] text-[#3d5cff]">
                    {skill.name.slice(0, 1)}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-start justify-between gap-2">
                      <h3 className="truncate text-[15px] font-semibold text-[#0a0a0a]">{skill.name}</h3>
                      <span className={`hb-pill ${statusTone(skill.status)}`}>{skill.status}</span>
                    </div>
                    <p className="mt-1 line-clamp-2 text-xs text-[#737373]">{skill.description}</p>
                  </div>
                </div>

                <div className="flex flex-wrap gap-2">
                  <span className={`hb-pill ${levelTone(skill.level)}`}>{skill.level}</span>
                  <span className="hb-pill gray">{skill.version}</span>
                </div>

                <div className="mt-4 flex items-center justify-between border-t border-[#f5f5f5] pt-3 text-xs text-[#737373]">
                  <span>更新于 {skill.updatedAt}</span>
                  <span className="text-[#4a6cf7]">查看详情 →</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
