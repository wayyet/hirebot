import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Shield, AlertCircle, MessageSquare, CheckCircle, ExternalLink, Edit3, Save, X } from 'lucide-react'
import { collaborationGroups } from '../mock/data'
import Badge from '../components/Badge'
import { openIMGroup, getIMPlatformIcon, type IMPlatform } from '../utils/imDeepLink'

const TABS = ['概览', '成员与角色', '协作活动', '权限与预设', '异常与升级']

// 权限预设选项
const PERMISSION_PRESETS = [
  {
    id: 'readonly',
    name: '只读顾问',
    description: '只能查看信息和提供建议，不能执行操作',
    level: 'L1',
    permissions: {
      '消息发送': true,
      '文件分享': false,
      '成员@提及': true,
      '调用外部系统': false,
      '创建子任务': false,
    }
  },
  {
    id: 'executor',
    name: '执行者',
    description: '可以执行明确规则内的操作，超出规则需请示',
    level: 'L2',
    permissions: {
      '消息发送': true,
      '文件分享': true,
      '成员@提及': true,
      '调用外部系统': true,
      '创建子任务': false,
    }
  },
  {
    id: 'full',
    name: '完整权限',
    description: '可以自主判断和执行大部分操作',
    level: 'L3',
    permissions: {
      '消息发送': true,
      '文件分享': true,
      '成员@提及': true,
      '调用外部系统': true,
      '创建子任务': true,
    }
  },
]

// 授权等级说明
const AUTH_LEVELS = [
  {
    level: 'L1',
    name: '建议模式',
    description: '只给建议，所有执行都需要人确认',
    suitable: '刚转正、高风险操作',
    color: 'slate',
  },
  {
    level: 'L2',
    name: '规则内自主',
    description: '在明确规则范围内自主执行，超出规则请示',
    suitable: '有清晰 SOP 的场景',
    color: 'blue',
  },
  {
    level: 'L3',
    name: '判断型自主',
    description: '可以做判断性决策，只在不确定时请示',
    suitable: '表现稳定、用户信任度高',
    color: 'indigo',
  },
  {
    level: 'L4',
    name: '完全自主',
    description: '几乎不需要人确认，只在极端异常时通知',
    suitable: '完全信任、低风险场景',
    color: 'purple',
  },
]

export default function CollaborationGroupDetailPage() {
  const { groupId } = useParams()
  const navigate = useNavigate()
  const [tab, setTab] = useState(0)
  const g = collaborationGroups.find(g => g.id === groupId)

  // 权限预设状态
  const [showPresetDialog, setShowPresetDialog] = useState(false)
  const [currentPreset, setCurrentPreset] = useState('executor')
  const [showPermissionEdit, setShowPermissionEdit] = useState(false)
  const [permissions, setPermissions] = useState({
    '消息发送': true,
    '文件分享': true,
    '成员@提及': true,
    '调用外部系统': true,
    '创建子任务': false,
  })

  // 异常处理状态
  const [showArchiveDialog, setShowArchiveDialog] = useState(false)
  const [isArchiving, setIsArchiving] = useState(false)

  if (!g) return <div className="flex items-center justify-center h-full text-slate-400">协作群不存在</div>

  const statusBadge = { '活跃': 'green', '低活跃': 'yellow', '异常': 'red', '已关闭': 'gray' } as const

  const handleChangePreset = (presetId: string) => {
    const preset = PERMISSION_PRESETS.find(p => p.id === presetId)
    if (preset) {
      setCurrentPreset(presetId)
      setPermissions(preset.permissions)
      setShowPresetDialog(false)
    }
  }

  const togglePermission = (key: string) => {
    setPermissions(prev => ({ ...prev, [key]: !(prev as any)[key] }))
  }

  // 处理权限配置（解决低活跃异常）
  const handleFixPermission = () => {
    // 自动切换到权限与预设 Tab
    setTab(3)
    // 滚动到顶部
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }

  // 归档群
  const handleArchiveGroup = () => {
    setIsArchiving(true)
    // 保存到 localStorage
    const stored = localStorage.getItem('ncrew_archived_groups')
    let archivedGroups: string[] = []
    if (stored) {
      try {
        archivedGroups = JSON.parse(stored)
      } catch (e) {
        console.error('Failed to parse archived groups:', e)
      }
    }
    if (!archivedGroups.includes(groupId!)) {
      archivedGroups.push(groupId!)
      localStorage.setItem('ncrew_archived_groups', JSON.stringify(archivedGroups))
    }
    
    // 模拟归档操作
    setTimeout(() => {
      setIsArchiving(false)
      setShowArchiveDialog(false)
      // 跳转回协作管理页，并显示成功提示
      navigate('/collaboration?archived=true')
    }, 1000)
  }

  const currentPresetInfo = PERMISSION_PRESETS.find(p => p.id === currentPreset)

  return (
    <div className="max-w-5xl mx-auto px-6 py-6">
      <button onClick={() => navigate('/collaboration')} className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 mb-5 transition-colors">
        <ArrowLeft size={14} />
        返回协作管理
      </button>

      {/* Header */}
      <div className="bg-white rounded-xl border border-slate-100 p-5 mb-5">
        <div className="flex items-start gap-4">
          <div className="w-12 h-12 rounded-xl bg-indigo-50 flex items-center justify-center shrink-0">
            <MessageSquare size={20} className="text-indigo-500" />
          </div>
          <div className="flex-1">
            <div className="flex items-center gap-2 mb-1">
              <h1 className="text-lg font-bold text-slate-800">{g.groupName}</h1>
              <Badge variant={statusBadge[g.status]}>{g.status}</Badge>
              <span className="text-xs text-slate-400 flex items-center gap-1">
                <span>{getIMPlatformIcon(g.imPlatform as IMPlatform)}</span>
                {g.imPlatform}
              </span>
            </div>
            <p className="text-sm text-slate-500 mb-2">{g.businessPurpose}</p>
            <div className="flex gap-4 text-xs text-slate-400">
              <span>{g.memberCount} 成员</span>
              <span>{g.digitalEmployeeCount} 数字员工</span>
              <span>7 日协作 {g.collaborationVolume7d} 次</span>
              <span>最近活跃 {g.recentActivityTime}</span>
            </div>
          </div>
          <button 
            onClick={() => openIMGroup({
              platform: g.imPlatform as IMPlatform,
              groupId: g.imGroupId,
              groupName: g.groupName
            })}
            className="px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors flex items-center gap-2 shadow-sm hover:shadow-md"
          >
            <ExternalLink size={14} />
            进入 IM 群
          </button>
        </div>
        
        {/* Deep Link 说明提示 */}
        <div className="mt-4 pt-4 border-t border-slate-50">
          <div className="flex items-start gap-2 text-xs text-slate-500">
            <div className="text-base">{getIMPlatformIcon(g.imPlatform as IMPlatform)}</div>
            <div>
              <div className="font-medium text-slate-600 mb-0.5">实时协作在 {g.imPlatform} 中进行</div>
              <div className="text-slate-400">
                点击"进入 IM 群"将直接跳转到 {g.imPlatform} 客户端的群聊界面
                {g.imPlatform === '飞书' && ' (需安装飞书客户端)'}
                {g.imPlatform === '钉钉' && ' (需安装钉钉客户端)'}
                {g.imPlatform === '企业微信' && ' (需安装企业微信客户端)'}
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 bg-white rounded-xl border border-slate-100 p-1 mb-5 w-fit">
        {TABS.map((t, i) => (
          <button
            key={t}
            onClick={() => setTab(i)}
            className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${tab === i ? 'bg-indigo-600 text-white' : 'text-slate-600 hover:bg-slate-50'}`}
          >
            {t}
          </button>
        ))}
      </div>

      {/* 概览 */}
      {tab === 0 && (
        <div className="grid grid-cols-2 gap-5">
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-4">群状态摘要</h3>
            <div className={`p-3 rounded-lg mb-3 ${g.status === '活跃' ? 'bg-emerald-50' : g.status === '低活跃' ? 'bg-amber-50' : 'bg-red-50'}`}>
              <div className={`flex items-center gap-2 text-sm font-medium ${g.status === '活跃' ? 'text-emerald-700' : g.status === '低活跃' ? 'text-amber-700' : 'text-red-700'}`}>
                {g.status === '活跃' ? <CheckCircle size={14} /> : <AlertCircle size={14} />}
                {g.primarySignal}
              </div>
            </div>
            <div className="grid grid-cols-2 gap-3 text-sm">
              <div className="bg-slate-50 rounded-lg p-3 text-center">
                <div className="text-2xl font-bold text-slate-800">{g.collaborationVolume7d}</div>
                <div className="text-xs text-slate-400">7 日协作次数</div>
              </div>
              <div className="bg-slate-50 rounded-lg p-3 text-center">
                <div className="text-2xl font-bold text-slate-800">{g.digitalEmployeeCount}</div>
                <div className="text-xs text-slate-400">数字员工</div>
              </div>
            </div>
          </div>
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-4">成员概览</h3>
            {g.members.slice(0, 4).map(m => (
              <div key={m.name} className="flex items-center gap-2 mb-2">
                <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-medium ${m.isDigital ? 'bg-indigo-100 text-indigo-600' : 'bg-slate-100 text-slate-600'}`}>
                  {m.isDigital ? '🤖' : m.name[0]}
                </div>
                <div className="flex-1">
                  <span className="text-sm text-slate-700">{m.name}</span>
                  {m.isDigital && <Badge variant="purple">数字员工</Badge>}
                </div>
                <span className="text-xs text-slate-400">{m.lastActive}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* 成员与角色 */}
      {tab === 1 && (
        <div className="bg-white rounded-xl border border-slate-100">
          <div className="px-5 py-4 border-b border-slate-50 text-sm font-medium text-slate-500 grid grid-cols-5 gap-4">
            <span className="col-span-2">成员</span>
            <span>角色</span>
            <span>加入时间</span>
            <span>最近活跃</span>
          </div>
          {g.members.map((m, i) => (
            <div key={i} className="px-5 py-3 grid grid-cols-5 gap-4 items-center hover:bg-slate-50 transition-colors">
              <div className="col-span-2 flex items-center gap-2">
                <div className={`w-8 h-8 rounded-full flex items-center justify-center text-sm font-medium ${m.isDigital ? 'bg-indigo-100 text-indigo-600' : 'bg-slate-100 text-slate-600'}`}>
                  {m.isDigital ? '🤖' : m.name[0]}
                </div>
                <div>
                  <div className="text-sm text-slate-700">{m.name}</div>
                  {m.isDigital && <div className="text-xs text-indigo-500">数字员工</div>}
                </div>
              </div>
              <span className="text-sm text-slate-600">{m.role}</span>
              <span className="text-xs text-slate-400">{m.joinedAt}</span>
              <span className="text-xs text-slate-400">{m.lastActive}</span>
            </div>
          ))}
        </div>
      )}

      {/* 协作活动 */}
      {tab === 2 && (
        <div className="space-y-4">
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-4">高频任务类型</h3>
            <div className="space-y-2">
              {[
                { type: '信息查询与摘要', count: 89, pct: 37 },
                { type: '任务提醒与跟进', count: 67, pct: 28 },
                { type: '数据更新同步', count: 48, pct: 20 },
                { type: '报告生成', count: 34, pct: 15 },
              ].map(t => (
                <div key={t.type}>
                  <div className="flex justify-between text-sm mb-1">
                    <span className="text-slate-600">{t.type}</span>
                    <span className="text-slate-400">{t.count} 次</span>
                  </div>
                  <div className="bg-slate-100 rounded-full h-1.5">
                    <div className="bg-indigo-400 h-1.5 rounded-full" style={{ width: `${t.pct}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </div>
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-4">近期升级记录</h3>
            <div className="space-y-2">
              {[
                { time: '2 天前', reason: '客户要求超出授权范围，已升级给销售主管', level: 'warn' },
                { time: '5 天前', reason: '合同金额超阈值，需人工审批', level: 'warn' },
              ].map((r, i) => (
                <div key={i} className="flex items-start gap-2 p-3 bg-amber-50 rounded-lg text-sm">
                  <AlertCircle size={13} className="text-amber-500 mt-0.5 shrink-0" />
                  <div>
                    <div className="text-amber-700">{r.reason}</div>
                    <div className="text-xs text-amber-500 mt-0.5">{r.time}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* 权限与预设 */}
      {tab === 3 && (
        <div className="space-y-5">
          {/* 当前预设信息 */}
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <div className="flex items-start justify-between mb-4">
              <div>
                <h3 className="font-semibold text-slate-800 mb-1 flex items-center gap-2">
                  <Shield size={15} className="text-indigo-500" />
                  当前权限预设
                </h3>
                <p className="text-xs text-slate-500">控制数字员工在此群的操作权限范围</p>
              </div>
              <button 
                onClick={() => setShowPresetDialog(true)}
                className="px-3 py-1.5 bg-indigo-50 text-indigo-600 rounded-lg text-xs font-medium hover:bg-indigo-100 transition-colors"
              >
                更换预设
              </button>
            </div>

            {currentPresetInfo && (
              <div className="bg-indigo-50 border border-indigo-100 rounded-lg p-4 mb-4">
                <div className="flex items-center gap-2 mb-2">
                  <Badge variant="blue">{currentPresetInfo.level}</Badge>
                  <span className="font-semibold text-indigo-800">{currentPresetInfo.name}</span>
                </div>
                <p className="text-sm text-indigo-600">{currentPresetInfo.description}</p>
              </div>
            )}

            {/* 权限详情 */}
            <div className="space-y-2">
              {Object.entries(permissions).map(([scope, enabled]) => (
                <div key={scope} className="flex items-center gap-3 p-3 rounded-lg bg-slate-50">
                  <div className={`w-2 h-2 rounded-full ${enabled ? 'bg-emerald-400' : 'bg-slate-300'}`} />
                  <div className="flex-1">
                    <div className="text-sm font-medium text-slate-700">{scope}</div>
                    <div className="text-xs text-slate-400">
                      {scope === '消息发送' && '可在群内发送通知与摘要'}
                      {scope === '文件分享' && '可分享文档和附件'}
                      {scope === '成员@提及' && '可主动提醒相关成员'}
                      {scope === '调用外部系统' && 'CRM、日历等已集成系统'}
                      {scope === '创建子任务' && '可创建待办和任务'}
                    </div>
                  </div>
                  <Badge variant={enabled ? 'green' : 'gray'}>
                    {enabled ? '允许' : '不允许'}
                  </Badge>
                  {showPermissionEdit && (
                    <button 
                      onClick={() => togglePermission(scope)}
                      className="text-xs text-indigo-500 hover:text-indigo-700"
                    >
                      切换
                    </button>
                  )}
                </div>
              ))}
            </div>

            <div className="mt-4 pt-4 border-t border-slate-50 flex gap-2">
              {!showPermissionEdit ? (
                <button 
                  onClick={() => setShowPermissionEdit(true)}
                  className="px-3 py-1.5 border border-slate-200 text-slate-600 rounded-lg text-xs hover:bg-slate-50 transition-colors flex items-center gap-1"
                >
                  <Edit3 size={12} />
                  微调权限
                </button>
              ) : (
                <>
                  <button 
                    onClick={() => setShowPermissionEdit(false)}
                    className="px-3 py-1.5 bg-indigo-600 text-white rounded-lg text-xs font-medium hover:bg-indigo-700 transition-colors flex items-center gap-1"
                  >
                    <Save size={12} />
                    保存
                  </button>
                  <button 
                    onClick={() => {
                      setShowPermissionEdit(false)
                      if (currentPresetInfo) {
                        setPermissions(currentPresetInfo.permissions)
                      }
                    }}
                    className="px-3 py-1.5 border border-slate-200 text-slate-600 rounded-lg text-xs hover:bg-slate-50 transition-colors flex items-center gap-1"
                  >
                    <X size={12} />
                    取消
                  </button>
                </>
              )}
            </div>
          </div>

          {/* 授权等级说明 */}
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-3">授权等级说明</h3>
            <div className="space-y-3">
              {AUTH_LEVELS.map(level => (
                <div 
                  key={level.level}
                  className={`p-3 rounded-lg border ${
                    currentPresetInfo?.level === level.level 
                      ? 'border-indigo-200 bg-indigo-50' 
                      : 'border-slate-100 bg-slate-50'
                  }`}
                >
                  <div className="flex items-center gap-2 mb-1">
                    <Badge variant={level.color as any}>{level.level}</Badge>
                    <span className="font-medium text-slate-700">{level.name}</span>
                    {currentPresetInfo?.level === level.level && (
                      <Badge variant="blue">当前</Badge>
                    )}
                  </div>
                  <p className="text-xs text-slate-600 mb-1">{level.description}</p>
                  <p className="text-xs text-slate-400">适用：{level.suitable}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* 更换预设对话框 */}
      {showPresetDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={() => setShowPresetDialog(false)}>
          <div className="bg-white rounded-xl p-6 max-w-2xl w-full mx-4 max-h-[80vh] overflow-y-auto" onClick={e => e.stopPropagation()}>
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-bold text-slate-800">选择权限预设</h3>
              <button 
                onClick={() => setShowPresetDialog(false)}
                className="text-slate-400 hover:text-slate-600"
              >
                <X size={20} />
              </button>
            </div>
            
            <p className="text-sm text-slate-500 mb-4">
              选择一个预设模板，快速配置数字员工在此群的权限范围
            </p>

            <div className="space-y-3">
              {PERMISSION_PRESETS.map(preset => (
                <button
                  key={preset.id}
                  onClick={() => handleChangePreset(preset.id)}
                  className={`w-full text-left p-4 rounded-lg border-2 transition-all ${
                    currentPreset === preset.id
                      ? 'border-indigo-500 bg-indigo-50'
                      : 'border-slate-200 hover:border-indigo-300 hover:bg-slate-50'
                  }`}
                >
                  <div className="flex items-center gap-2 mb-2">
                    <Badge variant={
                      preset.level === 'L1' ? 'gray' : 
                      preset.level === 'L2' ? 'blue' : 
                      'purple'
                    }>
                      {preset.level}
                    </Badge>
                    <span className="font-semibold text-slate-800">{preset.name}</span>
                    {currentPreset === preset.id && (
                      <CheckCircle size={16} className="text-indigo-600 ml-auto" />
                    )}
                  </div>
                  <p className="text-sm text-slate-600 mb-3">{preset.description}</p>
                  
                  <div className="grid grid-cols-2 gap-2">
                    {Object.entries(preset.permissions).map(([key, value]) => (
                      <div key={key} className="flex items-center gap-1.5 text-xs">
                        {value ? (
                          <CheckCircle size={12} className="text-emerald-500" />
                        ) : (
                          <X size={12} className="text-slate-300" />
                        )}
                        <span className={value ? 'text-slate-600' : 'text-slate-400'}>
                          {key}
                        </span>
                      </div>
                    ))}
                  </div>
                </button>
              ))}
            </div>

            <div className="mt-4 pt-4 border-t border-slate-100 text-xs text-slate-500">
              💡 提示：选择预设后，还可以在权限详情中进行微调
            </div>
          </div>
        </div>
      )}

      {/* 异常与升级 */}
      {tab === 4 && (
        <div className="space-y-4">
          {/* 异常分类说明 */}
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-3">异常分类说明</h3>
            <div className="space-y-2 text-sm">
              <div className="grid grid-cols-3 gap-3 text-xs font-medium text-slate-500 pb-2 border-b border-slate-100">
                <span>异常类型</span>
                <span>触发条件</span>
                <span>建议处理</span>
              </div>
              {[
                { type: '权限不足', trigger: '频繁请示人工，阻塞工作流', action: '扩大权限范围' },
                { type: '权限过大', trigger: '执行了不该执行的操作', action: '收紧权限或降级' },
                { type: '角色重叠', trigger: '多个数字员工职责冲突', action: '明确分工或移除冗余' },
                { type: '低活跃', trigger: '连续 3 天无协作活动', action: '检查配置或归档' },
                { type: '任务卡住', trigger: '同一任务多次失败未解决', action: '人工介入或重新配置' },
              ].map((item, i) => (
                <div key={i} className="grid grid-cols-3 gap-3 text-xs py-2 border-b border-slate-50 last:border-0">
                  <span className="text-slate-700 font-medium">{item.type}</span>
                  <span className="text-slate-500">{item.trigger}</span>
                  <span className="text-indigo-600">{item.action}</span>
                </div>
              ))}
            </div>
          </div>

          {/* 当前异常 - 根据不同异常类型展示 */}
          {g.status === '低活跃' ? (
            <div className="bg-amber-50 rounded-xl border border-amber-100 p-5">
              <div className="flex items-center gap-2 mb-3">
                <AlertCircle size={16} className="text-amber-600" />
                <h3 className="font-semibold text-amber-800">检测到低活跃异常</h3>
                <Badge variant="yellow">需要处理</Badge>
              </div>
              <p className="text-sm text-amber-700 mb-2">该群过去 3 天几乎无协作，数字员工权限未配置导致工作阻塞。</p>
              <div className="text-xs text-amber-600 mb-4 bg-amber-100 rounded-lg p-3">
                <div className="font-medium mb-1">诊断详情：</div>
                <ul className="space-y-0.5 ml-4 list-disc">
                  <li>数字员工"法务助手小法"权限预设为"只读顾问"，无法执行合同审核操作</li>
                  <li>过去 3 天有 12 次@提及未响应</li>
                  <li>群成员最近活跃时间：3 天前</li>
                </ul>
              </div>
              <div className="flex gap-2">
                <button 
                  onClick={handleFixPermission}
                  className="px-3 py-1.5 bg-amber-600 text-white rounded-lg text-xs font-medium hover:bg-amber-700 transition-colors"
                >
                  立即处理权限
                </button>
                <button 
                  onClick={() => setShowArchiveDialog(true)}
                  className="px-3 py-1.5 border border-amber-200 text-amber-700 rounded-lg text-xs hover:bg-amber-50 transition-colors"
                >
                  归档此群
                </button>
              </div>
            </div>
          ) : g.status === '异常' ? (
            <div className="space-y-3">
              {/* 演示：权限不足异常 */}
              <div className="bg-red-50 rounded-xl border border-red-100 p-5">
                <div className="flex items-center gap-2 mb-3">
                  <AlertCircle size={16} className="text-red-600" />
                  <h3 className="font-semibold text-red-800">检测到权限不足异常</h3>
                  <Badge variant="red">严重</Badge>
                </div>
                <p className="text-sm text-red-700 mb-2">数字员工频繁因权限不足请示人工，影响工作效率。</p>
                <div className="text-xs text-red-600 mb-4 bg-red-100 rounded-lg p-3">
                  <div className="font-medium mb-1">诊断详情：</div>
                  <ul className="space-y-0.5 ml-4 list-disc">
                    <li>过去 24 小时内有 23 次权限请示</li>
                    <li>主要卡在"调用外部系统"权限</li>
                    <li>平均响应延迟 2.3 小时</li>
                  </ul>
                </div>
                
                {/* 处理逻辑：扩大权限范围 */}
                <div className="bg-white rounded-lg p-3 mb-3 border border-red-200">
                  <div className="text-xs font-medium text-slate-700 mb-2">💡 建议处理方案：</div>
                  <div className="space-y-2 text-xs text-slate-600">
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-indigo-100 text-indigo-700 flex items-center justify-center shrink-0 font-bold">1</div>
                      <div>
                        <div className="font-medium text-slate-700">扩大权限范围</div>
                        <div className="text-slate-500">将当前预设从"只读顾问 L1"提升到"执行者 L2"，允许调用外部系统</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-indigo-100 text-indigo-700 flex items-center justify-center shrink-0 font-bold">2</div>
                      <div>
                        <div className="font-medium text-slate-700">设置自主规则</div>
                        <div className="text-slate-500">配置明确的自主执行规则，减少请示频率（如：金额 ≤5000 自动处理）</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-indigo-100 text-indigo-700 flex items-center justify-center shrink-0 font-bold">3</div>
                      <div>
                        <div className="font-medium text-slate-700">优化升级路径</div>
                        <div className="text-slate-500">缩短升级响应时间（2小时 → 30分钟），或增加备用处理人</div>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="flex gap-2">
                  <button 
                    onClick={handleFixPermission}
                    className="px-3 py-1.5 bg-red-600 text-white rounded-lg text-xs font-medium hover:bg-red-700 transition-colors"
                  >
                    扩大权限范围
                  </button>
                  <button className="px-3 py-1.5 border border-red-200 text-red-700 rounded-lg text-xs hover:bg-red-50 transition-colors">
                    查看请示日志
                  </button>
                </div>
              </div>

              {/* 演示：权限过大异常 */}
              <div className="bg-orange-50 rounded-xl border border-orange-100 p-5">
                <div className="flex items-center gap-2 mb-3">
                  <AlertCircle size={16} className="text-orange-600" />
                  <h3 className="font-semibold text-orange-800">检测到权限过大异常</h3>
                  <Badge variant="red">风险</Badge>
                </div>
                <p className="text-sm text-orange-700 mb-2">数字员工执行了不该执行的操作，存在安全风险。</p>
                <div className="text-xs text-orange-600 mb-4 bg-orange-100 rounded-lg p-3">
                  <div className="font-medium mb-1">诊断详情：</div>
                  <ul className="space-y-0.5 ml-4 list-disc">
                    <li>昨日自动审批了一笔 50000 元的异常报销（超出合理范围）</li>
                    <li>当前权限预设为"完整权限 L3"，无金额限制</li>
                    <li>触发了财务系统的风险预警</li>
                  </ul>
                </div>

                {/* 处理逻辑：收紧权限或降级 */}
                <div className="bg-white rounded-lg p-3 mb-3 border border-orange-200">
                  <div className="text-xs font-medium text-slate-700 mb-2">💡 建议处理方案：</div>
                  <div className="space-y-2 text-xs text-slate-600">
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-orange-100 text-orange-700 flex items-center justify-center shrink-0 font-bold">1</div>
                      <div>
                        <div className="font-medium text-slate-700">收紧权限范围</div>
                        <div className="text-slate-500">设置审批金额上限（如：≤10000 自主，&gt;10000 需人工确认）</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-orange-100 text-orange-700 flex items-center justify-center shrink-0 font-bold">2</div>
                      <div>
                        <div className="font-medium text-slate-700">降低授权等级</div>
                        <div className="text-slate-500">从 L3（判断型自主）降级到 L2（规则内自主），增加人工把关</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-orange-100 text-orange-700 flex items-center justify-center shrink-0 font-bold">3</div>
                      <div>
                        <div className="font-medium text-slate-700">增加审核规则</div>
                        <div className="text-slate-500">配置异常检测规则（如：单笔金额超过平均值 3 倍需人工审核）</div>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="flex gap-2">
                  <button 
                    onClick={handleFixPermission}
                    className="px-3 py-1.5 bg-orange-600 text-white rounded-lg text-xs font-medium hover:bg-orange-700 transition-colors"
                  >
                    收紧权限
                  </button>
                  <button className="px-3 py-1.5 border border-orange-200 text-orange-700 rounded-lg text-xs hover:bg-orange-50 transition-colors">
                    查看风险操作
                  </button>
                </div>
              </div>

              {/* 演示：角色重叠异常 */}
              <div className="bg-purple-50 rounded-xl border border-purple-100 p-5">
                <div className="flex items-center gap-2 mb-3">
                  <AlertCircle size={16} className="text-purple-600" />
                  <h3 className="font-semibold text-purple-800">检测到角色重叠异常</h3>
                  <Badge variant="purple">低效</Badge>
                </div>
                <p className="text-sm text-purple-700 mb-2">多个数字员工职责冲突，导致重复工作和协作混乱。</p>
                <div className="text-xs text-purple-600 mb-4 bg-purple-100 rounded-lg p-3">
                  <div className="font-medium mb-1">诊断详情：</div>
                  <ul className="space-y-0.5 ml-4 list-disc">
                    <li>"财务助手"和"报销专员"都在处理报销审批，出现重复审批</li>
                    <li>过去一周有 8 次同一任务被两个数字员工同时响应</li>
                    <li>用户反馈："不知道该 @谁，两个都会回复"</li>
                  </ul>
                </div>

                {/* 处理逻辑：明确分工或移除冗余 */}
                <div className="bg-white rounded-lg p-3 mb-3 border border-purple-200">
                  <div className="text-xs font-medium text-slate-700 mb-2">💡 建议处理方案：</div>
                  <div className="space-y-2 text-xs text-slate-600">
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-purple-100 text-purple-700 flex items-center justify-center shrink-0 font-bold">1</div>
                      <div>
                        <div className="font-medium text-slate-700">明确职责分工</div>
                        <div className="text-slate-500">财务助手：负责审批和政策咨询；报销专员：仅负责发票核验和流程跟进</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-purple-100 text-purple-700 flex items-center justify-center shrink-0 font-bold">2</div>
                      <div>
                        <div className="font-medium text-slate-700">调整权限范围</div>
                        <div className="text-slate-500">禁用报销专员的"审批建议"权限，避免越权响应</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-purple-100 text-purple-700 flex items-center justify-center shrink-0 font-bold">3</div>
                      <div>
                        <div className="font-medium text-slate-700">移除冗余角色</div>
                        <div className="text-slate-500">如果职责完全重叠，考虑只保留一个，将另一个移出此群</div>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="flex gap-2">
                  <button 
                    onClick={() => setTab(1)}
                    className="px-3 py-1.5 bg-purple-600 text-white rounded-lg text-xs font-medium hover:bg-purple-700 transition-colors"
                  >
                    调整成员角色
                  </button>
                  <button className="px-3 py-1.5 border border-purple-200 text-purple-700 rounded-lg text-xs hover:bg-purple-50 transition-colors">
                    查看冲突记录
                  </button>
                </div>
              </div>

              {/* 演示：任务卡住异常 */}
              <div className="bg-rose-50 rounded-xl border border-rose-100 p-5">
                <div className="flex items-center gap-2 mb-3">
                  <AlertCircle size={16} className="text-rose-600" />
                  <h3 className="font-semibold text-rose-800">检测到任务卡住异常</h3>
                  <Badge variant="red">阻塞</Badge>
                </div>
                <p className="text-sm text-rose-700 mb-2">同一任务多次失败未解决，工作流程被阻塞。</p>
                <div className="text-xs text-rose-600 mb-4 bg-rose-100 rounded-lg p-3">
                  <div className="font-medium mb-1">诊断详情：</div>
                  <ul className="space-y-0.5 ml-4 list-disc">
                    <li>合同审核任务"XX公司服务协议"已失败 5 次</li>
                    <li>失败原因：无法连接法务系统，缺少必要的合同模板数据</li>
                    <li>任务已卡住 2 天，影响业务进度</li>
                  </ul>
                </div>

                {/* 处理逻辑：人工介入或重新配置 */}
                <div className="bg-white rounded-lg p-3 mb-3 border border-rose-200">
                  <div className="text-xs font-medium text-slate-700 mb-2">💡 建议处理方案：</div>
                  <div className="space-y-2 text-xs text-slate-600">
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-rose-100 text-rose-700 flex items-center justify-center shrink-0 font-bold">1</div>
                      <div>
                        <div className="font-medium text-slate-700">人工接管任务</div>
                        <div className="text-slate-500">将此任务转交给人工处理，避免继续阻塞业务流程</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-rose-100 text-rose-700 flex items-center justify-center shrink-0 font-bold">2</div>
                      <div>
                        <div className="font-medium text-slate-700">修复连接器配置</div>
                        <div className="text-slate-500">检查法务系统连接器状态，重新配置认证信息或修复网络连接</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-rose-100 text-rose-700 flex items-center justify-center shrink-0 font-bold">3</div>
                      <div>
                        <div className="font-medium text-slate-700">补充业务资料</div>
                        <div className="text-slate-500">上传缺失的合同模板和审核规则，让数字员工重新学习</div>
                      </div>
                    </div>
                    <div className="flex items-start gap-2">
                      <div className="w-5 h-5 rounded bg-rose-100 text-rose-700 flex items-center justify-center shrink-0 font-bold">4</div>
                      <div>
                        <div className="font-medium text-slate-700">调整任务策略</div>
                        <div className="text-slate-500">设置失败重试上限和自动升级规则，避免长时间卡住</div>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="flex gap-2">
                  <button className="px-3 py-1.5 bg-rose-600 text-white rounded-lg text-xs font-medium hover:bg-rose-700 transition-colors">
                    人工接管
                  </button>
                  <button className="px-3 py-1.5 bg-indigo-600 text-white rounded-lg text-xs font-medium hover:bg-indigo-700 transition-colors">
                    修复配置
                  </button>
                  <button className="px-3 py-1.5 border border-rose-200 text-rose-700 rounded-lg text-xs hover:bg-rose-50 transition-colors">
                    查看失败日志
                  </button>
                </div>
              </div>
            </div>
          ) : (
            <div className="bg-emerald-50 rounded-xl border border-emerald-100 p-5 flex items-center gap-3">
              <CheckCircle size={18} className="text-emerald-500" />
              <div className="text-sm text-emerald-700">暂无协作异常，群运行健康</div>
            </div>
          )}

          {/* 升级路径配置 */}
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-3">升级路径配置</h3>
            
            <div className="text-sm text-slate-600 bg-slate-50 rounded-lg p-3 mb-3">
              <div className="font-medium text-slate-700 mb-2">当前升级路径：</div>
              <div className="flex items-center gap-2 text-xs flex-wrap">
                <span className="px-2 py-1 bg-indigo-100 text-indigo-700 rounded">数字员工遇到超权限请求</span>
                <span className="text-slate-400">→</span>
                <span className="px-2 py-1 bg-blue-100 text-blue-700 rounded">在 IM 群内 @群主</span>
                <span className="text-slate-400">→</span>
                <span className="px-2 py-1 bg-amber-100 text-amber-700 rounded">2 小时未处理</span>
                <span className="text-slate-400">→</span>
                <span className="px-2 py-1 bg-red-100 text-red-700 rounded">IM 私信 + 站内通知上级管理员</span>
              </div>
            </div>

            {/* 升级机制说明 */}
            <div className="bg-indigo-50 border border-indigo-100 rounded-lg p-4 mb-3">
              <div className="text-sm font-medium text-indigo-800 mb-2">升级机制说明</div>
              <div className="space-y-2 text-xs text-indigo-700">
                <div className="flex items-start gap-2">
                  <div className="w-5 h-5 rounded-full bg-indigo-200 flex items-center justify-center shrink-0 mt-0.5">
                    <span className="text-indigo-800 font-bold text-xs">1</span>
                  </div>
                  <div>
                    <div className="font-medium mb-0.5">第一级：群内 @提及</div>
                    <div className="text-indigo-600">数字员工在 {g.imPlatform} 群内直接 @群主，说明遇到的问题和需要的权限。群主可在 IM 内直接回复确认或拒绝。</div>
                  </div>
                </div>
                <div className="flex items-start gap-2">
                  <div className="w-5 h-5 rounded-full bg-amber-200 flex items-center justify-center shrink-0 mt-0.5">
                    <span className="text-amber-800 font-bold text-xs">2</span>
                  </div>
                  <div>
                    <div className="font-medium mb-0.5">第二级：多渠道通知</div>
                    <div className="text-indigo-600">若群主 2 小时未响应，系统通过 {g.imPlatform} 私信 + NCrew 站内通知同时触达上级管理员，确保不遗漏。</div>
                  </div>
                </div>
                <div className="flex items-start gap-2">
                  <div className="w-5 h-5 rounded-full bg-emerald-200 flex items-center justify-center shrink-0 mt-0.5">
                    <span className="text-emerald-800 font-bold text-xs">✓</span>
                  </div>
                  <div>
                    <div className="font-medium mb-0.5">处理方式</div>
                    <div className="text-indigo-600">管理员可在 IM 内直接回复确认，或点击通知跳转到 NCrew 进行权限调整。所有升级记录会自动归档。</div>
                  </div>
                </div>
              </div>
            </div>

            {/* 通知渠道说明 */}
            <div className="bg-slate-50 rounded-lg p-3 mb-3">
              <div className="text-xs font-medium text-slate-700 mb-2">通知渠道优先级</div>
              <div className="space-y-1.5 text-xs text-slate-600">
                <div className="flex items-center gap-2">
                  <div className="w-2 h-2 rounded-full bg-indigo-400"></div>
                  <span className="font-medium">群内 @提及：</span>
                  <span>最快速，上下文完整，适合日常协作</span>
                </div>
                <div className="flex items-center gap-2">
                  <div className="w-2 h-2 rounded-full bg-amber-400"></div>
                  <span className="font-medium">IM 私信：</span>
                  <span>确保触达，避免群消息淹没</span>
                </div>
                <div className="flex items-center gap-2">
                  <div className="w-2 h-2 rounded-full bg-red-400"></div>
                  <span className="font-medium">站内通知：</span>
                  <span>持久化记录，可追溯审计</span>
                </div>
              </div>
            </div>

            <button className="text-xs text-indigo-500 hover:text-indigo-700 font-medium">修改升级路径</button>
          </div>

          {/* 近期升级记录 */}
          <div className="bg-white rounded-xl border border-slate-100 p-5">
            <h3 className="font-semibold text-slate-800 mb-3">近期升级记录</h3>
            {g.status !== '活跃' ? (
              <div className="space-y-2">
                {[
                  { time: '2 天前', reason: '客户要求超出授权范围，已升级给销售主管', handler: '张经理', status: '已处理' },
                  { time: '5 天前', reason: '合同金额超阈值，需人工审批', handler: '李总监', status: '已处理' },
                  { time: '1 周前', reason: '权限不足无法访问 CRM 系统', handler: '系统管理员', status: '已处理' },
                ].map((r, i) => (
                  <div key={i} className="flex items-start gap-3 p-3 bg-slate-50 rounded-lg text-sm border border-slate-100">
                    <AlertCircle size={13} className="text-amber-500 mt-0.5 shrink-0" />
                    <div className="flex-1">
                      <div className="text-slate-700">{r.reason}</div>
                      <div className="text-xs text-slate-400 mt-1 flex items-center gap-3">
                        <span>{r.time}</span>
                        <span>处理人：{r.handler}</span>
                        <Badge variant="green">{r.status}</Badge>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="text-sm text-slate-400 text-center py-4">暂无升级记录</div>
            )}
          </div>
        </div>
      )}

      {/* 归档确认对话框 */}
      {showArchiveDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={() => setShowArchiveDialog(false)}>
          <div className="bg-white rounded-xl p-6 max-w-md w-full mx-4" onClick={e => e.stopPropagation()}>
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 rounded-full bg-amber-100 flex items-center justify-center">
                <AlertCircle size={20} className="text-amber-600" />
              </div>
              <h3 className="text-lg font-bold text-slate-800">确认归档协作群</h3>
            </div>
            
            <p className="text-sm text-slate-600 mb-4">
              归档后，该群将从活跃列表中移除，但历史数据会保留。你可以随时在"已归档"筛选中找回。
            </p>

            <div className="bg-amber-50 border border-amber-100 rounded-lg p-3 mb-4">
              <div className="text-xs text-amber-700">
                <div className="font-medium mb-1">归档影响：</div>
                <ul className="space-y-0.5 ml-4 list-disc">
                  <li>群内数字员工将停止响应</li>
                  <li>不再统计协作数据</li>
                  <li>可随时恢复激活</li>
                </ul>
              </div>
            </div>

            <div className="flex gap-2">
              <button 
                onClick={handleArchiveGroup}
                disabled={isArchiving}
                className="flex-1 px-4 py-2 bg-amber-600 text-white rounded-lg text-sm font-medium hover:bg-amber-700 transition-colors disabled:opacity-50"
              >
                {isArchiving ? '归档中...' : '确认归档'}
              </button>
              <button 
                onClick={() => setShowArchiveDialog(false)}
                disabled={isArchiving}
                className="flex-1 px-4 py-2 border border-slate-200 text-slate-600 rounded-lg text-sm hover:bg-slate-50 transition-colors disabled:opacity-50"
              >
                取消
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
