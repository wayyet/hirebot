import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Loader2, AlertCircle } from 'lucide-react'
import { api, type CollaborationGroupDetail } from '@/infra/api'

export default function CollaborationGroupDetailPage() {
  const { groupId } = useParams<{ groupId: string }>()
  const navigate = useNavigate()

  const [group, setGroup] = useState<CollaborationGroupDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  async function loadGroup() {
    if (!groupId) return
    setLoading(true)
    setError('')
    try {
      const data = await api.collaboration.getGroup(groupId)
      setGroup(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : '加载协作群详情失败')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    loadGroup()
  }, [groupId])

  return (
    <div className="max-w-4xl mx-auto px-6 py-6 space-y-5">
      <button
        onClick={() => navigate('/collaboration')}
        className="inline-flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700"
      >
        <ArrowLeft size={14} />
        返回协作群
      </button>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 p-3 text-sm text-red-700 flex items-center gap-2">
          <AlertCircle size={14} />
          {error}
        </div>
      )}

      {loading ? (
        <div className="rounded-xl border border-slate-200 bg-white p-10 flex items-center justify-center text-slate-500 gap-2">
          <Loader2 size={16} className="animate-spin" />
          加载中...
        </div>
      ) : !group ? (
        <div className="rounded-xl border border-slate-200 bg-white p-8 text-slate-500">协作群不存在</div>
      ) : (
        <>
          <section className="rounded-xl border border-slate-200 bg-white p-5">
            <h1 className="text-xl font-bold text-slate-900">{group.groupName}</h1>
            <p className="text-sm text-slate-500 mt-1">
              {group.businessPurpose} · {group.imPlatform} · {group.status}
            </p>
            <p className="text-sm text-slate-600 mt-2">{group.primarySignal}</p>
          </section>

          <section className="rounded-xl border border-slate-200 bg-white p-5">
            <h2 className="font-semibold text-slate-800 mb-3">成员列表</h2>
            <div className="space-y-2">
              {group.members.map((member, index) => (
                <div key={`${member.name}_${index}`} className="rounded-lg border border-slate-200 px-3 py-2">
                  <div className="text-sm font-medium text-slate-800">
                    {member.name}
                    {member.isDigital ? '（数字员工）' : ''}
                  </div>
                  <div className="text-xs text-slate-500 mt-1">
                    {member.role} · 最近活跃：{member.lastActive}
                  </div>
                </div>
              ))}
            </div>
          </section>
        </>
      )}
    </div>
  )
}
