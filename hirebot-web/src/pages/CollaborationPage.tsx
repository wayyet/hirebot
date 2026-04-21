import { useState, useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Plus, AlertCircle, Users, MessageSquare, Activity, ChevronRight, ExternalLink, Archive, X } from 'lucide-react'
import { collaborationGroups } from '../mock/data'
import Badge from '../components/Badge'
import { openIMGroup, getIMPlatformIcon, type IMPlatform } from '../utils/imDeepLink'
import { digitalEmployees as mockEmployees } from '../mock/data'
import { loadUserEmployees } from '../utils/storage'

const statusBadge = { '活跃': 'green', '低活跃': 'yellow', '异常': 'red', '已关闭': 'gray', '已归档': 'gray' } as const

export default function CollaborationPage() {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [filter, setFilter] = useState<'all' | 'archived'>('all')
  const [archivedGroups, setArchivedGroups] = useState<string[]>([])
  const [showArchivedSuccess, setShowArchivedSuccess] = useState(false)
  const [showCreateDialog, setShowCreateDialog] = useState(false)

  // 从 localStorage 加载已归档的群
  useEffect(() => {
    const stored = localStorage.getItem('ncrew_archived_groups')
    if (stored) {
      try {
        setArchivedGroups(JSON.parse(stored))
      } catch (e) {
        console.error('Failed to parse archived groups:', e)
      }
    }
  }, [])

  // 检查是否刚完成归档操作
  useEffect(() => {
    if (searchParams.get('archived') === 'true') {
      setShowArchivedSuccess(true)
      setTimeout(() => setShowArchivedSuccess(false), 3000)
    }
  }, [searchParams])

  // 根据筛选条件过滤群
  const displayGroups = filter === 'archived' 
    ? collaborationGroups.filter(g => archivedGroups.includes(g.id)).map(g => ({ ...g, status: '已归档' as const }))
    : collaborationGroups.filter(g => !archivedGroups.includes(g.id))

  const total = collaborationGroups.length
  const active = collaborationGroups.filter(g => g.status === '活跃' && !archivedGroups.includes(g.id)).length
  const withDE = collaborationGroups.filter(g => g.digitalEmployeeCount > 0 && !archivedGroups.includes(g.id)).length
  const issues = collaborationGroups.filter(g => g.status !== '活跃' && !archivedGroups.includes(g.id)).length
  const archived = archivedGroups.length

  return (
    <div className="max-w-5xl mx-auto px-6 py-6 space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-xl font-bold text-slate-800">协作管理</h1>
          <p className="text-sm text-slate-500 mt-0.5">查看协作群健康度，管理数字员工在组织中的协同关系</p>
        </div>
        <button 
          onClick={() => setShowCreateDialog(true)}
          className="flex items-center gap-1.5 px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors"
        >
          <Plus size={14} />
          创建协作群
        </button>
      </div>

      {/* 归档成功提示 */}
      {showArchivedSuccess && (
        <div className="bg-emerald-50 border border-emerald-200 rounded-xl p-4 flex items-center gap-3">
          <div className="w-8 h-8 rounded-full bg-emerald-100 flex items-center justify-center">
            <Archive size={16} className="text-emerald-600" />
          </div>
          <div className="flex-1">
            <div className="text-sm font-medium text-emerald-800">协作群已归档</div>
            <div className="text-xs text-emerald-600">可在"已归档"筛选中查看和恢复</div>
          </div>
        </div>
      )}

      {/* Summary */}
      <div className="grid grid-cols-5 gap-4">
        {[
          { label: '协作群总数', value: total, icon: MessageSquare, color: 'text-slate-600', bg: 'bg-slate-100' },
          { label: '活跃群', value: active, icon: Activity, color: 'text-emerald-600', bg: 'bg-emerald-50' },
          { label: '含数字员工', value: withDE, icon: Users, color: 'text-indigo-600', bg: 'bg-indigo-50' },
          { label: '有异常', value: issues, icon: AlertCircle, color: 'text-amber-600', bg: 'bg-amber-50' },
          { label: '已归档', value: archived, icon: Archive, color: 'text-slate-500', bg: 'bg-slate-50' },
        ].map(({ label, value, icon: Icon, color, bg }) => (
          <div key={label} className="bg-white rounded-xl border border-slate-100 p-4 flex items-center gap-3">
            <div className={`w-10 h-10 rounded-xl ${bg} flex items-center justify-center`}>
              <Icon size={18} className={color} />
            </div>
            <div>
              <div className="text-2xl font-bold text-slate-800">{value}</div>
              <div className="text-xs text-slate-500">{label}</div>
            </div>
          </div>
        ))}
      </div>

      {/* 筛选器 */}
      <div className="flex gap-2">
        <button
          onClick={() => setFilter('all')}
          className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${
            filter === 'all'
              ? 'bg-indigo-600 text-white'
              : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-50'
          }`}
        >
          活跃群组
        </button>
        <button
          onClick={() => setFilter('archived')}
          className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors flex items-center gap-1.5 ${
            filter === 'archived'
              ? 'bg-indigo-600 text-white'
              : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-50'
          }`}
        >
          <Archive size={14} />
          已归档 {archived > 0 && `(${archived})`}
        </button>
      </div>

      {/* Groups list */}
      <div className="space-y-3">
        {displayGroups.length === 0 ? (
          <div className="bg-white rounded-xl border border-slate-100 p-12 text-center">
            <Archive size={32} className="text-slate-300 mx-auto mb-3" />
            <div className="text-sm text-slate-500">
              {filter === 'archived' ? '暂无已归档的协作群' : '暂无活跃的协作群'}
            </div>
          </div>
        ) : (
          displayGroups.map(g => (
          <div
            key={g.id}
            className="bg-white rounded-xl border border-slate-100 p-5 hover:shadow-md transition-all group"
          >
            <div className="flex items-start gap-4">
              <div className="w-10 h-10 rounded-xl bg-indigo-50 flex items-center justify-center shrink-0">
                <MessageSquare size={18} className="text-indigo-500" />
              </div>
              <div 
                className="flex-1 min-w-0 cursor-pointer"
                onClick={() => navigate(`/collaboration/${g.id}`)}
              >
                <div className="flex items-center gap-2 mb-1">
                  <h3 className="font-semibold text-slate-800 group-hover:text-indigo-600 transition-colors">{g.groupName}</h3>
                  <Badge variant={statusBadge[g.status]}>{g.status}</Badge>
                  <span className="text-xs text-slate-400 ml-1 flex items-center gap-1">
                    <span>{getIMPlatformIcon(g.imPlatform as IMPlatform)}</span>
                    {g.imPlatform}
                  </span>
                </div>
                <p className="text-xs text-slate-500 mb-2 line-clamp-1">{g.businessPurpose}</p>
                <div className="flex items-center gap-4 text-xs text-slate-400">
                  <span className="flex items-center gap-1">
                    <Users size={11} />
                    {g.memberCount} 成员（含 {g.digitalEmployeeCount} 数字员工）
                  </span>
                  <span>7 日协作 {g.collaborationVolume7d} 次</span>
                  <span>最近活跃：{g.recentActivityTime}</span>
                </div>
              </div>
              <div className="shrink-0 flex items-center gap-3">
                <button
                  onClick={(e) => {
                    e.stopPropagation()
                    openIMGroup({
                      platform: g.imPlatform as IMPlatform,
                      groupId: g.imGroupId,
                      groupName: g.groupName
                    })
                  }}
                  className="px-3 py-1.5 bg-indigo-600 text-white rounded-lg text-xs font-medium hover:bg-indigo-700 transition-colors flex items-center gap-1.5 opacity-0 group-hover:opacity-100"
                  title="在 IM 中打开"
                >
                  <ExternalLink size={12} />
                  进入群聊
                </button>
                <div 
                  className={`text-xs ${g.status !== '活跃' ? 'text-amber-600' : 'text-emerald-600'}`}
                  onClick={() => navigate(`/collaboration/${g.id}`)}
                >
                  {g.primarySignal.length > 20 ? g.primarySignal.slice(0, 20) + '...' : g.primarySignal}
                </div>
                <ChevronRight size={14} className="text-slate-300" />
              </div>
            </div>

            {/* Signal bar */}
            {g.status !== '活跃' && g.status !== '已归档' && (
              <div className={`mt-3 pt-3 border-t border-slate-50 flex items-center gap-2 text-xs ${g.status === '低活跃' ? 'text-amber-600' : 'text-red-600'}`}>
                <AlertCircle size={12} />
                {g.primarySignal}
              </div>
            )}
          </div>
          ))
        )}
      </div>

      {/* Relationship hint */}
      <div className="bg-indigo-50 rounded-xl p-4 flex items-center gap-3">
        <div className="text-2xl">🔗</div>
        <div>
          <div className="text-sm font-medium text-indigo-800">关系视图（即将上线）</div>
          <div className="text-xs text-indigo-600">可视化展示群 — 人 — 数字员工关系，识别过载、协作枢纽与孤岛</div>
        </div>
      </div>

      {/* 创建协作群对话框 */}
      {showCreateDialog && <CreateCollaborationGroupDialog onClose={() => setShowCreateDialog(false)} />}
    </div>
  )
}

// 创建协作群对话框组件
function CreateCollaborationGroupDialog({ onClose }: { onClose: () => void }) {
  const [step, setStep] = useState<'method' | 'ncrew' | 'im'>('method')
  const [formData, setFormData] = useState({
    groupName: '',
    businessGoal: '',
    imPlatform: 'feishu' as IMPlatform,
    selectedMembers: [] as string[],
    selectedEmployees: [] as string[],
    permissionPreset: 'executor',
  })

  // 获取所有可用的数字员工 - 合并 mock 数据和用户创建的数据
  const userEmployees = loadUserEmployees()
  const allEmployees = [...mockEmployees, ...userEmployees]
  const activeEmployees = allEmployees.filter(e => 
    e.lifecycleStatus === '已转正' || e.lifecycleStatus === '实习中'
  )

  console.log('All employees:', allEmployees.length) // 调试日志
  console.log('Active employees:', activeEmployees.length) // 调试日志

  const handleCreate = () => {
    // 这里实现创建逻辑
    console.log('Creating collaboration group:', formData)
    // 实际应该调用 API 或更新 localStorage
    onClose()
  }

  return (
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4" onClick={onClose}>
      <div 
        className="bg-white rounded-xl max-w-3xl w-full max-h-[90vh] overflow-y-auto"
        onClick={e => e.stopPropagation()}
      >
        {/* 选择创建方式 */}
        {step === 'method' && (
          <div className="p-6">
            <div className="flex items-center justify-between mb-6">
              <h2 className="text-xl font-bold text-slate-800">创建协作群</h2>
              <button onClick={onClose} className="text-slate-400 hover:text-slate-600">
                <X size={20} />
              </button>
            </div>

            <p className="text-sm text-slate-600 mb-6">
              选择创建方式：在 NCrew 中配置后同步到企业 IM，或直接关联已有的 IM 群
            </p>

            <div className="space-y-3">
              {/* 方式 1：NCrew 创建 */}
              <button
                onClick={() => setStep('ncrew')}
                className="w-full text-left p-5 border-2 border-slate-200 rounded-xl hover:border-indigo-500 hover:bg-indigo-50 transition-all group"
              >
                <div className="flex items-start gap-4">
                  <div className="w-12 h-12 rounded-xl bg-indigo-100 flex items-center justify-center shrink-0 group-hover:bg-indigo-200">
                    <MessageSquare size={24} className="text-indigo-600" />
                  </div>
                  <div className="flex-1">
                    <div className="font-semibold text-slate-800 mb-1">在 NCrew 中创建（推荐）</div>
                    <div className="text-sm text-slate-600 mb-2">
                      在 NCrew 中配置群信息、成员和权限，然后自动在企业 IM 中创建群并邀请成员
                    </div>
                    <div className="flex items-center gap-2 text-xs text-slate-500">
                      <span className="px-2 py-0.5 bg-emerald-50 text-emerald-600 rounded">✓ 配置完整</span>
                      <span className="px-2 py-0.5 bg-emerald-50 text-emerald-600 rounded">✓ 自动同步</span>
                      <span className="px-2 py-0.5 bg-emerald-50 text-emerald-600 rounded">✓ 权限管理</span>
                    </div>
                  </div>
                  <ChevronRight size={20} className="text-slate-300 group-hover:text-indigo-500" />
                </div>
              </button>

              {/* 方式 2：关联已有 IM 群 */}
              <button
                onClick={() => setStep('im')}
                className="w-full text-left p-5 border-2 border-slate-200 rounded-xl hover:border-indigo-500 hover:bg-indigo-50 transition-all group"
              >
                <div className="flex items-start gap-4">
                  <div className="w-12 h-12 rounded-xl bg-blue-100 flex items-center justify-center shrink-0 group-hover:bg-blue-200">
                    <ExternalLink size={24} className="text-blue-600" />
                  </div>
                  <div className="flex-1">
                    <div className="font-semibold text-slate-800 mb-1">关联已有 IM 群</div>
                    <div className="text-sm text-slate-600 mb-2">
                      如果你已经在飞书/钉钉/企微中创建了群，可以直接关联到 NCrew 进行管理
                    </div>
                    <div className="flex items-center gap-2 text-xs text-slate-500">
                      <span className="px-2 py-0.5 bg-blue-50 text-blue-600 rounded">快速接入</span>
                      <span className="px-2 py-0.5 bg-blue-50 text-blue-600 rounded">保留现有群</span>
                    </div>
                  </div>
                  <ChevronRight size={20} className="text-slate-300 group-hover:text-indigo-500" />
                </div>
              </button>
            </div>

            <div className="mt-6 p-4 bg-slate-50 rounded-lg">
              <div className="text-xs text-slate-600">
                <div className="font-medium mb-1">💡 建议</div>
                <div>首次使用建议选择"在 NCrew 中创建"，可以更好地配置权限和管理数字员工。</div>
              </div>
            </div>
          </div>
        )}

        {/* NCrew 创建流程 */}
        {step === 'ncrew' && (
          <div className="p-6">
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-3">
                <button 
                  onClick={() => setStep('method')}
                  className="text-slate-400 hover:text-slate-600"
                >
                  <ChevronRight size={20} className="rotate-180" />
                </button>
                <h2 className="text-xl font-bold text-slate-800">创建协作群</h2>
              </div>
              <button onClick={onClose} className="text-slate-400 hover:text-slate-600">
                <X size={20} />
              </button>
            </div>

            <div className="space-y-5">
              {/* 基本信息 */}
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-2">
                  群名称 <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  value={formData.groupName}
                  onChange={e => setFormData({ ...formData, groupName: e.target.value })}
                  placeholder="例如：财务报销审批群"
                  className="w-full px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              <div>
                <label className="block text-sm font-medium text-slate-700 mb-2">
                  业务目标
                </label>
                <textarea
                  value={formData.businessGoal}
                  onChange={e => setFormData({ ...formData, businessGoal: e.target.value })}
                  placeholder="描述这个群的业务目标，例如：处理公司日常报销审批，提高审批效率"
                  rows={3}
                  className="w-full px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-indigo-500"
                />
              </div>

              {/* 选择 IM 平台 */}
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-2">
                  企业 IM 平台 <span className="text-red-500">*</span>
                </label>
                <div className="grid grid-cols-3 gap-3">
                  {(['feishu', 'dingtalk', 'wecom'] as IMPlatform[]).map(platform => (
                    <button
                      key={platform}
                      onClick={() => setFormData({ ...formData, imPlatform: platform })}
                      className={`p-3 border-2 rounded-lg text-sm font-medium transition-all ${
                        formData.imPlatform === platform
                          ? 'border-indigo-500 bg-indigo-50 text-indigo-700'
                          : 'border-slate-200 text-slate-600 hover:border-slate-300'
                      }`}
                    >
                      <div className="text-2xl mb-1">{getIMPlatformIcon(platform)}</div>
                      {platform === 'feishu' && '飞书'}
                      {platform === 'dingtalk' && '钉钉'}
                      {platform === 'wecom' && '企业微信'}
                    </button>
                  ))}
                </div>
              </div>

              {/* 选择数字员工 */}
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-2">
                  添加数字员工 <span className="text-red-500">*</span>
                </label>
                <div className="border border-slate-200 rounded-lg p-3 max-h-48 overflow-y-auto">
                  {activeEmployees.length === 0 ? (
                    <div className="text-sm text-slate-400 text-center py-4">
                      暂无可用的数字员工，请先雇佣数字员工
                    </div>
                  ) : (
                    <div className="space-y-2">
                      {activeEmployees.map(emp => (
                        <label
                          key={emp.id}
                          className="flex items-center gap-3 p-2 hover:bg-slate-50 rounded-lg cursor-pointer"
                        >
                          <input
                            type="checkbox"
                            checked={formData.selectedEmployees.includes(emp.id)}
                            onChange={e => {
                              if (e.target.checked) {
                                setFormData({
                                  ...formData,
                                  selectedEmployees: [...formData.selectedEmployees, emp.id]
                                })
                              } else {
                                setFormData({
                                  ...formData,
                                  selectedEmployees: formData.selectedEmployees.filter(id => id !== emp.id)
                                })
                              }
                            }}
                            className="w-4 h-4 text-indigo-600 rounded"
                          />
                          <div className="flex-1">
                            <div className="text-sm font-medium text-slate-700">{emp.nickname}</div>
                            <div className="text-xs text-slate-500">{emp.roleName} · {emp.lifecycleStatus}</div>
                          </div>
                          <Badge variant={emp.lifecycleStatus === '已转正' ? 'green' : 'yellow'}>
                            {emp.lifecycleStatus}
                          </Badge>
                        </label>
                      ))}
                    </div>
                  )}
                </div>
                <div className="text-xs text-slate-500 mt-1">
                  已选择 {formData.selectedEmployees.length} 个数字员工
                </div>
              </div>

              {/* 权限预设 */}
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-2">
                  默认权限预设
                </label>
                <div className="space-y-2">
                  {[
                    { id: 'readonly', name: '只读顾问 L1', desc: '只能查看信息和提供建议' },
                    { id: 'executor', name: '执行者 L2', desc: '可执行明确规则内的操作' },
                    { id: 'full', name: '完整权限 L3', desc: '可自主判断和执行大部分操作' },
                  ].map(preset => (
                    <label
                      key={preset.id}
                      className={`flex items-start gap-3 p-3 border-2 rounded-lg cursor-pointer transition-all ${
                        formData.permissionPreset === preset.id
                          ? 'border-indigo-500 bg-indigo-50'
                          : 'border-slate-200 hover:border-slate-300'
                      }`}
                    >
                      <input
                        type="radio"
                        name="preset"
                        checked={formData.permissionPreset === preset.id}
                        onChange={() => setFormData({ ...formData, permissionPreset: preset.id })}
                        className="mt-1"
                      />
                      <div className="flex-1">
                        <div className="text-sm font-medium text-slate-700">{preset.name}</div>
                        <div className="text-xs text-slate-500">{preset.desc}</div>
                      </div>
                    </label>
                  ))}
                </div>
              </div>

              {/* 提示信息 */}
              <div className="bg-indigo-50 border border-indigo-100 rounded-lg p-4">
                <div className="text-sm text-indigo-800">
                  <div className="font-medium mb-2">📋 创建后的操作</div>
                  <ol className="space-y-1 text-xs text-indigo-700 ml-4 list-decimal">
                    <li>系统将在 {formData.imPlatform === 'feishu' ? '飞书' : formData.imPlatform === 'dingtalk' ? '钉钉' : '企业微信'} 中自动创建群</li>
                    <li>邀请选中的数字员工（作为 Bot）加入群</li>
                    <li>你需要手动邀请真实成员加入群</li>
                    <li>数字员工将按照设定的权限预设开始工作</li>
                  </ol>
                </div>
              </div>
            </div>

            {/* 底部按钮 */}
            <div className="flex gap-3 mt-6 pt-6 border-t border-slate-100">
              <button
                onClick={() => setStep('method')}
                className="flex-1 px-4 py-2 border border-slate-200 text-slate-600 rounded-lg hover:bg-slate-50 transition-colors"
              >
                返回
              </button>
              <button
                onClick={handleCreate}
                disabled={!formData.groupName || formData.selectedEmployees.length === 0}
                className="flex-1 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              >
                创建协作群
              </button>
            </div>
          </div>
        )}

        {/* 关联已有 IM 群流程 */}
        {step === 'im' && (
          <div className="p-6">
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-3">
                <button 
                  onClick={() => setStep('method')}
                  className="text-slate-400 hover:text-slate-600"
                >
                  <ChevronRight size={20} className="rotate-180" />
                </button>
                <h2 className="text-xl font-bold text-slate-800">关联已有 IM 群</h2>
              </div>
              <button onClick={onClose} className="text-slate-400 hover:text-slate-600">
                <X size={20} />
              </button>
            </div>

            <div className="space-y-5">
              <div className="bg-blue-50 border border-blue-100 rounded-lg p-4">
                <div className="text-sm text-blue-800">
                  <div className="font-medium mb-2">📱 如何关联已有群</div>
                  <ol className="space-y-2 text-xs text-blue-700 ml-4 list-decimal">
                    <li>在企业 IM 中找到你要关联的群</li>
                    <li>复制群的 ID 或群链接（通常在群设置中）</li>
                    <li>在下方输入框中粘贴群 ID</li>
                    <li>系统将自动识别群信息并同步到 NCrew</li>
                  </ol>
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-slate-700 mb-2">
                  选择 IM 平台 <span className="text-red-500">*</span>
                </label>
                <div className="grid grid-cols-3 gap-3">
                  {(['feishu', 'dingtalk', 'wecom'] as IMPlatform[]).map(platform => (
                    <button
                      key={platform}
                      onClick={() => setFormData({ ...formData, imPlatform: platform })}
                      className={`p-3 border-2 rounded-lg text-sm font-medium transition-all ${
                        formData.imPlatform === platform
                          ? 'border-indigo-500 bg-indigo-50 text-indigo-700'
                          : 'border-slate-200 text-slate-600 hover:border-slate-300'
                      }`}
                    >
                      <div className="text-2xl mb-1">{getIMPlatformIcon(platform)}</div>
                      {platform === 'feishu' && '飞书'}
                      {platform === 'dingtalk' && '钉钉'}
                      {platform === 'wecom' && '企业微信'}
                    </button>
                  ))}
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-slate-700 mb-2">
                  群 ID 或群链接 <span className="text-red-500">*</span>
                </label>
                <input
                  type="text"
                  placeholder={
                    formData.imPlatform === 'feishu' 
                      ? '例如：oc_xxxxxxxxxxxxx' 
                      : formData.imPlatform === 'dingtalk'
                      ? '例如：chatxxxxxxxxxxxxxxx'
                      : '例如：wrxxxxxxxxxxxxxxx'
                  }
                  className="w-full px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-indigo-500"
                />
                <div className="text-xs text-slate-500 mt-1">
                  {formData.imPlatform === 'feishu' && '在飞书群设置 → 群管理 → 群 ID 中查看'}
                  {formData.imPlatform === 'dingtalk' && '在钉钉群设置 → 群管理 → 群号 中查看'}
                  {formData.imPlatform === 'wecom' && '在企业微信群设置 → 群信息 → 群 ID 中查看'}
                </div>
              </div>

              <div className="bg-amber-50 border border-amber-100 rounded-lg p-4">
                <div className="text-sm text-amber-800">
                  <div className="font-medium mb-1">⚠️ 注意事项</div>
                  <ul className="space-y-1 text-xs text-amber-700 ml-4 list-disc">
                    <li>关联后，NCrew 将同步群成员信息</li>
                    <li>需要确保 NCrew Bot 已加入该群</li>
                    <li>如果群中已有数字员工，将自动识别并关联</li>
                  </ul>
                </div>
              </div>
            </div>

            <div className="flex gap-3 mt-6 pt-6 border-t border-slate-100">
              <button
                onClick={() => setStep('method')}
                className="flex-1 px-4 py-2 border border-slate-200 text-slate-600 rounded-lg hover:bg-slate-50 transition-colors"
              >
                返回
              </button>
              <button
                onClick={handleCreate}
                className="flex-1 px-4 py-2 bg-indigo-600 text-white rounded-lg hover:bg-indigo-700 transition-colors"
              >
                关联群组
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
