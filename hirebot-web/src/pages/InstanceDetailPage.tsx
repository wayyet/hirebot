import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, MessageCircle, Star, CheckCircle, AlertCircle, TrendingUp, Shield, BarChart2, UserCheck, UserX, ExternalLink, Upload, X, Brain, ChevronRight } from 'lucide-react'
import { digitalEmployees as mockEmployees } from '../mock/data'
import { loadUserEmployees, updateAnyEmployee } from '../utils/storage'
import { openIMGroup, getIMPlatformIcon, type IMPlatform } from '../utils/imDeepLink'
import Badge from '../components/Badge'
import OnboardingProgress from '../components/OnboardingProgress'
import { generateOnboardingSteps, getCurrentStepId } from '../utils/onboardingSteps'

const INTERN_TABS = ['概览', '评估', '协作记录', '配置进度']
const ACTIVE_TABS = ['概览', '工作绩效', '成长轨迹', '授权管理', '协作记录']

export default function InstanceDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  
  // 合并 mock 数据和用户数据
  const userEmployees = loadUserEmployees()
  const allEmployees = [...mockEmployees, ...userEmployees]
  const e = allEmployees.find(e => e.id === id)
  
  const [tab, setTab] = useState(0)
  const [showPromotionDialog, setShowPromotionDialog] = useState(false)
  const [showResignDialog, setShowResignDialog] = useState(false)
  const [showActionDialog, setShowActionDialog] = useState(false)
  const [currentAction, setCurrentAction] = useState<string>('')
  const [showConfigDialog, setShowConfigDialog] = useState(false)
  const [currentCapability, setCurrentCapability] = useState<{ name: string; ready: boolean } | null>(null)
  const [uploadedFiles, setUploadedFiles] = useState<Array<{ name: string; size: number; type: string }>>([])
  const [configForm, setConfigForm] = useState({
    apiKey: '',
    apiEndpoint: '',
    permissions: [] as string[],
  })

  if (!e) return <div className="flex items-center justify-center h-full text-slate-400">员工不存在</div>

  const isIntern = e.lifecycleStatus === '实习中' || e.lifecycleStatus === '待启动'
  const tabs = isIntern ? INTERN_TABS : ACTIVE_TABS

  const signalBg = e.signalLevel === 'ok' ? 'bg-emerald-50 border-emerald-100' : e.signalLevel === 'warn' ? 'bg-amber-50 border-amber-100' : 'bg-red-50 border-red-100'
  const signalText = e.signalLevel === 'ok' ? 'text-emerald-700' : e.signalLevel === 'warn' ? 'text-amber-700' : 'text-red-700'

  // 检查是否所有能力都已配置
  const allCapabilitiesReady = e.capabilities.every(c => c.ready)
  const canStartInternship = e.lifecycleStatus === '待启动' && allCapabilitiesReady

  // 启动实习
  const handleStartInternship = () => {
    console.log('=== 启动实习 ===')
    console.log('员工 ID:', id)
    if (!id) {
      console.error('员工 ID 不存在')
      return
    }
    
    console.log('更新员工状态...')
    updateAnyEmployee(id, mockEmployees, {
      lifecycleStatus: '实习中',
      stageSummary: '已启动实习，开始积累评估数据',
      primarySignal: '实习中，等待积累评估数据',
      signalLevel: 'ok',
      internshipStartAt: new Date().toISOString().split('T')[0],
      pendingActions: [],
    })
    console.log('状态更新完成，准备刷新页面')
    window.location.reload()
  }

  // 确认转正
  const handlePromote = () => {
    if (!id) return
    updateAnyEmployee(id, mockEmployees, {
      lifecycleStatus: '已转正',
      stageSummary: '已转正，正式进入工作状态',
      primarySignal: '运行正常',
      signalLevel: 'ok',
      graduatedAt: new Date().toISOString().split('T')[0],
      pendingActions: [],
    })
    setShowPromotionDialog(false)
    window.location.reload()
  }

  // 开始离职
  const handleResign = () => {
    if (!id) return
    updateAnyEmployee(id, mockEmployees, {
      lifecycleStatus: '离职中',
      stageSummary: '正在进行交接，准备归档',
      primarySignal: '离职中',
      signalLevel: 'warn',
      pendingActions: ['完成工作交接', '知识转移', '权限回收'],
    })
    setShowResignDialog(false)
    window.location.reload()
  }

  // 处理待办事项
  const handlePendingAction = (action: string) => {
    setCurrentAction(action)
    setShowActionDialog(true)
  }

  // 完成待办事项
  const completeAction = () => {
    if (!id || !e) return
    const updatedActions = e.pendingActions.filter(a => a !== currentAction)
    updateAnyEmployee(id, mockEmployees, {
      pendingActions: updatedActions,
      signalLevel: updatedActions.length > 0 ? 'warn' : 'ok',
      primarySignal: updatedActions.length > 0 ? `还有 ${updatedActions.length} 项待处理` : '运行正常',
    })
    setShowActionDialog(false)
    setCurrentAction('')
    window.location.reload()
  }

  // 获取待办事项的处理建议
  const getActionSuggestion = (action: string) => {
    // 判断是否需要在 IM 中处理
    const needsIMHandling = action.includes('转人工') || 
                           action.includes('确认') || 
                           action.includes('审批') || 
                           action.includes('沟通') ||
                           action.includes('协调')

    if (action.includes('FAQ') || action.includes('知识库')) {
      return {
        title: '补充知识库',
        description: '添加缺失的常见问题和答案，提高自动回复的覆盖率。',
        needsIM: false,
        steps: [
          '整理用户高频咨询的问题',
          '编写标准答案和话术',
          '在知识库中添加新的 FAQ 条目',
          '测试数字员工的回答准确性'
        ]
      }
    } else if (action.includes('权限') || action.includes('配置')) {
      return {
        title: '配置系统权限',
        description: '为数字员工配置必要的系统访问权限，确保能够正常执行任务。',
        needsIM: false,
        steps: [
          '确认需要访问的系统和数据范围',
          '在系统管理后台创建服务账号',
          '配置只读或读写权限',
          '测试权限是否生效'
        ]
      }
    } else if (needsIMHandling) {
      return {
        title: '在 IM 中处理',
        description: '此事项需要在 IM 协作群中查看上下文并进行实际处理。处理完成后，状态会自动同步回 NCrew。',
        needsIM: true,
        imGroupName: getIMGroupForEmployee(e?.id),
        steps: [
          '点击下方按钮跳转到 IM 协作群',
          '在群中查看完整的对话上下文',
          '进行实际的业务处理（审批、确认等）',
          '处理完成后，Bot 会自动同步状态到 NCrew'
        ]
      }
    } else if (action.includes('交接') || action.includes('转移')) {
      return {
        title: '完成工作交接',
        description: '将数字员工负责的工作交接给其他成员或系统。',
        needsIM: false,
        steps: [
          '整理数字员工处理的任务清单',
          '导出相关数据和文档',
          '分配给其他团队成员',
          '确认交接完成'
        ]
      }
    } else {
      return {
        title: '处理待办事项',
        description: '根据具体情况处理该待办事项。',
        needsIM: false,
        steps: [
          '了解事项的具体要求',
          '执行相应的操作',
          '验证处理结果',
          '标记为已完成'
        ]
      }
    }
  }

  // 处理能力配置
  const handleCapabilityConfig = (capability: { name: string; ready: boolean }) => {
    setCurrentCapability(capability)
    setUploadedFiles([])
    setConfigForm({ apiKey: '', apiEndpoint: '', permissions: [] })
    setShowConfigDialog(true)
  }

  // 处理文件上传
  const handleFileUpload = (event: React.ChangeEvent<HTMLInputElement>) => {
    const files = event.target.files
    if (files) {
      const newFiles = Array.from(files).map(file => ({
        name: file.name,
        size: file.size,
        type: file.type,
      }))
      setUploadedFiles(prev => [...prev, ...newFiles])
    }
  }

  // 删除已上传的文件
  const removeFile = (index: number) => {
    setUploadedFiles(prev => prev.filter((_, i) => i !== index))
  }

  // 完成能力配置
  const completeCapabilityConfig = () => {
    if (!id || !currentCapability) return
    
    console.log('配置完成:', {
      capability: currentCapability.name,
      files: uploadedFiles,
      config: configForm,
    })
    
    // 更新能力状态
    const updatedCapabilities = e?.capabilities.map(cap => 
      cap.name === currentCapability.name ? { ...cap, ready: true } : cap
    ) || []
    
    const allReady = updatedCapabilities.every(cap => cap.ready)
    
    updateAnyEmployee(id, mockEmployees, {
      capabilities: updatedCapabilities,
      signalLevel: allReady ? 'ok' : 'warn',
      primarySignal: allReady ? '配置已完成，等待启动实习' : `还有 ${updatedCapabilities.filter(c => !c.ready).length} 项能力待配置`,
    })
    
    setShowConfigDialog(false)
    setCurrentCapability(null)
    setUploadedFiles([])
    window.location.reload()
  }

  // 获取数字员工关联的 IM 群组信息
  const getIMGroupForEmployee = (employeeId?: string) => {
    // 根据员工 ID 返回对应的 IM 群组信息
    const imGroupMap: Record<string, { platform: '飞书' | '钉钉' | '企业微信', groupId: string, groupName: string }> = {
      'e001': { platform: '飞书', groupId: 'oc_a5b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6', groupName: '华东销售作战群' },
      'e002': { platform: '飞书', groupId: 'oc_b6c7d8e9f0g1h2i3j4k5l6m7n8o9p0q1', groupName: '客服运营协同群' },
      'e003': { platform: '企业微信', groupId: 'wrOgQhDgAAUwcEaEAAAA', groupName: '法务合同评审群' },
      'e004': { platform: '飞书', groupId: 'oc_c7d8e9f0g1h2i3j4k5l6m7n8o9p0q1r2', groupName: '运营数据播报群' },
    }
    return imGroupMap[employeeId || ''] || { platform: '飞书', groupId: '', groupName: '协作群' }
  }

  return (
    <div className="min-h-screen bg-slate-50">
      <div className="max-w-5xl mx-auto px-8 py-8">
      <button onClick={() => navigate('/team')} className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 mb-5 transition-colors">
        <ArrowLeft size={14} />
        返回团队
      </button>

      {/* Fixed header */}
      <div className="bg-white rounded-xl border border-slate-100 p-5 mb-5">
        <div className="flex items-start gap-4">
          <div className="w-14 h-14 rounded-xl bg-indigo-50 flex items-center justify-center text-3xl shrink-0">
            {e.roleName.includes('销售') ? '💼' : e.roleName.includes('客服') ? '🎧' : e.roleName.includes('合同') ? '📋' : e.roleName.includes('数据') ? '📊' : '👥'}
          </div>
          <div className="flex-1">
            <div className="flex items-center gap-2 mb-1">
              <h1 className="text-lg font-bold text-slate-800">{e.nickname}</h1>
              <span className="text-slate-400 text-sm">{e.roleName}</span>
              <Badge variant={
                e.lifecycleStatus === '已转正' ? 'green' :
                e.lifecycleStatus === '实习中' ? 'blue' :
                e.lifecycleStatus === '待启动' ? 'yellow' : 'gray'
              }>{e.lifecycleStatus}</Badge>
            </div>
            <p className="text-xs text-slate-500 mb-2">基于模板 {e.sourceTemplate} · {e.owningTeam}</p>
            <div className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-lg border text-xs ${signalBg} ${signalText}`}>
              {e.signalLevel === 'ok' ? <CheckCircle size={12} /> : <AlertCircle size={12} />}
              {e.primarySignal}
            </div>
          </div>
          <div className="flex gap-2 shrink-0">
            <button className="px-3 py-1.5 bg-indigo-600 text-white rounded-lg text-xs font-medium hover:bg-indigo-700 transition-colors flex items-center gap-1">
              <MessageCircle size={12} />
              去企业 IM
            </button>
            
            {/* 待启动状态：进入培训流程 */}
            {e.lifecycleStatus === '待启动' && (
              <button
                onClick={() => navigate(`/instances/${id}/training`)}
                className="px-3 py-1.5 bg-violet-600 text-white rounded-lg text-xs font-medium hover:bg-violet-700 transition-colors flex items-center gap-1"
              >
                <Brain size={12} />
                开始培训
              </button>
            )}
            
            {/* 实习中状态：显示转正按钮 */}
            {e.lifecycleStatus === '实习中' && (
              <button 
                onClick={() => setShowPromotionDialog(true)}
                className="px-3 py-1.5 bg-emerald-600 text-white rounded-lg text-xs font-medium hover:bg-emerald-700 transition-colors flex items-center gap-1"
              >
                <UserCheck size={12} />
                确认转正
              </button>
            )}
            
            {/* 已转正状态：显示离职按钮 */}
            {e.lifecycleStatus === '已转正' && (
              <button 
                onClick={() => setShowResignDialog(true)}
                className="px-3 py-1.5 border border-red-200 text-red-600 rounded-lg text-xs hover:bg-red-50 transition-colors flex items-center gap-1"
              >
                <UserX size={12} />
                开始离职
              </button>
            )}
            
            <button
              onClick={() => navigate('/eval')}
              className="px-3 py-1.5 border border-slate-200 text-slate-600 rounded-lg text-xs hover:bg-slate-50 transition-colors flex items-center gap-1"
            >
              <Star size={12} />
              去评估
            </button>
            <button className="px-3 py-1.5 border border-slate-200 text-slate-600 rounded-lg text-xs hover:bg-slate-50 transition-colors flex items-center gap-1">
              <Shield size={12} />
              去授权
            </button>
          </div>
        </div>
      </div>

      {/* Tabs */}
      <div className="flex gap-1 bg-white rounded-xl border border-slate-100 p-1 mb-5 w-fit">
        {tabs.map((t, i) => (
          <button
            key={t}
            onClick={() => setTab(i)}
            className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${tab === i ? 'bg-indigo-600 text-white' : 'text-slate-600 hover:bg-slate-50'}`}
          >
            {t}
          </button>
        ))}
      </div>

      {/* Waterfall layout - single column */}
      <div className="space-y-5">
        {/* 上岗进度 - 移到顶部 */}
        <OnboardingProgress 
          steps={generateOnboardingSteps(e)}
          currentStepId={getCurrentStepId(e)}
        />

        {/* 概览 */}
        {tab === 0 && (
          <>
            {/* 待启动状态：培训上岗流程引导 */}
            {e.lifecycleStatus === '待启动' && (
              <>
                {/* 主引导卡 */}
                <div className="bg-violet-50 border border-violet-200 rounded-xl p-5">
                  <div className="flex items-start gap-4">
                    <div className="w-12 h-12 bg-violet-100 rounded-xl flex items-center justify-center shrink-0">
                      <Brain size={22} className="text-violet-600" />
                    </div>
                    <div className="flex-1">
                      <h3 className="font-semibold text-violet-900 mb-1">开始培训上岗流程</h3>
                      <p className="text-sm text-violet-700 mb-4 leading-relaxed">
                        上传技能文档和业务切片，bot 将由三个智能体（规划器、生成器、评估员）自主完成培训与考试，通过后由你决策是否进入实习。
                      </p>
                      {/* Flow preview */}
                      <div className="flex items-center gap-1.5 flex-wrap mb-4">
                        {['上传材料', '自主培训', '模拟考试', '人工审核', '进入实习'].map((s, i, arr) => (
                          <span key={s} className="flex items-center gap-1.5">
                            <span className="px-2.5 py-1 bg-white border border-violet-200 text-violet-700 rounded-lg text-xs font-medium">
                              {s}
                            </span>
                            {i < arr.length - 1 && <ChevronRight size={12} className="text-violet-400" />}
                          </span>
                        ))}
                      </div>
                      <button
                        onClick={() => navigate(`/instances/${id}/training`)}
                        className="px-5 py-2 bg-violet-600 text-white rounded-lg text-sm font-medium hover:bg-violet-700 transition-colors flex items-center gap-1.5"
                      >
                        <Brain size={14} />
                        开始培训上岗
                      </button>
                    </div>
                  </div>
                </div>
                {/* 技能列表预览 */}
                {e.capabilities.length > 0 && (
                  <div className="bg-slate-50 border border-slate-100 rounded-xl p-4">
                    <div className="text-xs font-semibold text-slate-600 mb-2">需要培训的技能（{e.capabilities.length} 项）</div>
                    <div className="flex flex-wrap gap-1.5">
                      {e.capabilities.map(c => (
                        <span key={c.name} className="px-2 py-1 bg-white border border-slate-200 text-slate-600 rounded-lg text-xs">
                          {c.name}
                        </span>
                      ))}
                    </div>
                  </div>
                )}
                {/* placeholder for old content */}
                <div className="hidden">
                  <div className="bg-amber-50 border border-amber-200 rounded-xl p-5">
                    <div className="flex items-start gap-3">
                      <div className="w-10 h-10 bg-amber-100 rounded-full flex items-center justify-center shrink-0">
                        <AlertCircle size={20} className="text-amber-600" />
                      </div>
                      <div className="flex-1">
                        <h3 className="font-semibold text-amber-800 mb-1">需要完成能力配置</h3>
                        <p className="text-sm text-amber-600 mb-3">
                          还有 {e.capabilities.filter(c => !c.ready).length} 项能力待配置。完成配置后才能启动实习。
                        </p>
                        <button
                          onClick={() => setTab(3)}
                          className="px-4 py-2 bg-amber-600 text-white rounded-lg text-sm font-medium hover:bg-amber-700 transition-colors flex items-center gap-1.5"
                        >
                          <AlertCircle size={14} />
                          去配置能力
                        </button>
                      </div>
                    </div>
                  </div>
                </div>
              </>
            )}
              
              <div className="bg-white rounded-xl border border-slate-100 p-5">
                <h3 className="font-semibold text-slate-800 mb-4">
                  {isIntern ? '需不需要介入？' : '现在总体运行得好吗？'}
                </h3>
                {e.pendingActions.length > 0 ? (
                  <div className="space-y-2">
                    <p className="text-sm text-amber-700 font-medium">⚠ 以下事项需要你处理：</p>
                    {e.pendingActions.map((a, i) => {
                      const needsIM = a.includes('转人工') || a.includes('确认') || a.includes('审批')
                      return (
                        <div key={i} className="flex items-center gap-2 p-3 bg-amber-50 rounded-lg">
                          <AlertCircle size={14} className="text-amber-500 shrink-0" />
                          <span className="text-sm text-amber-700 flex-1">{a}</span>
                          <div className="flex gap-2">
                            {needsIM && (
                              <button 
                                onClick={() => {
                                  const imInfo = getIMGroupForEmployee(e.id)
                                  openIMGroup({
                                    platform: imInfo.platform,
                                    groupId: imInfo.groupId,
                                    groupName: imInfo.groupName
                                  })
                                }}
                                className="px-3 py-1 bg-indigo-600 text-white rounded text-xs font-medium hover:bg-indigo-700 transition-colors flex items-center gap-1"
                                title="在 IM 中处理"
                              >
                                <MessageCircle size={12} />
                                去 IM
                              </button>
                            )}
                            <button 
                              onClick={() => handlePendingAction(a)}
                              className="px-3 py-1 bg-amber-600 text-white rounded text-xs font-medium hover:bg-amber-700 transition-colors"
                            >
                              {needsIM ? '详情' : '处理'}
                            </button>
                          </div>
                        </div>
                      )
                    })}
                  </div>
                ) : (
                  <div className="flex items-center gap-3 p-3 bg-emerald-50 rounded-lg">
                    <CheckCircle size={18} className="text-emerald-500" />
                    <span className="text-sm text-emerald-700">
                      {e.lifecycleStatus === '待启动' ? '配置已完成，等待启动实习' : '暂无需要介入的事项，TA 运行良好'}
                    </span>
                  </div>
                )}
              </div>

              <div className="bg-white rounded-xl border border-slate-100 p-5">
                <h3 className="font-semibold text-slate-800 mb-3">任务完成情况</h3>
                <div className="flex items-center gap-4 mb-3">
                  <div className="text-3xl font-bold text-slate-800">{e.tasksDone}</div>
                  <div className="flex-1">
                    <div className="text-xs text-slate-500 mb-1">已完成 / 总计 {e.tasksTotal}</div>
                    <div className="bg-slate-100 rounded-full h-2">
                      <div className="bg-indigo-500 h-2 rounded-full" style={{ width: `${e.tasksTotal ? Math.min(100, (e.tasksDone / e.tasksTotal) * 100) : 0}%` }} />
                    </div>
                  </div>
                  {e.satisfactionScore && (
                    <div className="text-right">
                      <div className="text-2xl font-bold text-amber-500">{e.satisfactionScore}</div>
                      <div className="text-xs text-slate-400">满意度</div>
                    </div>
                  )}
                </div>
              </div>

              <div className="bg-white rounded-xl border border-slate-100 p-5">
                <h3 className="font-semibold text-slate-800 mb-3">当前状态摘要</h3>
                <p className="text-sm text-slate-600">{e.stageSummary}</p>
                <div className="mt-3 pt-3 border-t border-slate-50 text-xs text-slate-400 flex gap-4">
                  <span>加入：{e.createdAt}</span>
                  {e.internshipStartAt && <span>实习开始：{e.internshipStartAt}</span>}
                  {e.graduatedAt && <span>转正：{e.graduatedAt}</span>}
                </div>
              </div>

              {/* 能力状态和快速数据 - 合并显示 */}
              <div className="grid grid-cols-2 gap-5">
                <div className="bg-white rounded-xl border border-slate-100 p-5">
                  <h3 className="text-sm font-semibold text-slate-700 mb-3">能力状态</h3>
                  <div className="space-y-2">
                    {e.capabilities.map(cap => (
                      <div key={cap.name} className="flex items-center gap-2">
                        {cap.ready ? <CheckCircle size={12} className="text-emerald-500" /> : <AlertCircle size={12} className="text-amber-400" />}
                        <span className="text-xs text-slate-600">{cap.name}</span>
                      </div>
                    ))}
                  </div>
                </div>

                <div className="bg-white rounded-xl border border-slate-100 p-5">
                  <h3 className="text-sm font-semibold text-slate-700 mb-3">快速数据</h3>
                  <div className="space-y-2">
                    <div className="flex justify-between text-xs">
                      <span className="text-slate-500">任务完成</span>
                      <span className="font-medium text-slate-700">{e.tasksDone}</span>
                    </div>
                    {e.satisfactionScore && (
                      <div className="flex justify-between text-xs">
                        <span className="text-slate-500">满意度</span>
                        <span className="font-medium text-amber-600 flex items-center gap-0.5">
                          <Star size={10} className="fill-amber-400 text-amber-400" />
                          {e.satisfactionScore}
                        </span>
                      </div>
                    )}
                    <div className="flex justify-between text-xs">
                      <span className="text-slate-500">所属团队</span>
                      <span className="font-medium text-slate-700">{e.owningTeam}</span>
                    </div>
                  </div>
                </div>
              </div>

              {/* 查看协作情况按钮 */}
              <button
                onClick={() => navigate('/collaboration')}
                className="w-full px-4 py-3 bg-indigo-50 text-indigo-600 rounded-xl text-sm font-medium hover:bg-indigo-100 transition-colors flex items-center justify-center gap-2"
              >
                <BarChart2 size={14} />
                查看协作情况
              </button>
            </>
          )}

          {/* 评估 / 工作绩效 */}
          {tab === 1 && (
            <div className="bg-white rounded-xl border border-slate-100 p-5">
              <h3 className="font-semibold text-slate-800 mb-4">
                {isIntern ? '评估维度' : '工作绩效'}
              </h3>
              <div className="space-y-3">
                {(isIntern ? [
                  { label: '业务理解准确率', value: 78, target: 90 },
                  { label: '任务执行完成率', value: 74, target: 85 },
                  { label: '转人工率（越低越好）', value: 22, target: 15, invert: true },
                  { label: '响应时效达标率', value: 91, target: 90 },
                ] : [
                  { label: '任务完成率', value: 97, target: 95 },
                  { label: '用户满意度', value: 94, target: 90 },
                  { label: '异常率（越低越好）', value: 3, target: 5, invert: true },
                  { label: '响应时效达标率', value: 99, target: 95 },
                ]).map(({ label, value, target, invert }) => {
                  const good = invert ? value <= target : value >= target
                  return (
                    <div key={label}>
                      <div className="flex justify-between text-sm mb-1">
                        <span className="text-slate-600">{label}</span>
                        <span className={`font-medium ${good ? 'text-emerald-600' : 'text-amber-600'}`}>{value}%</span>
                      </div>
                      <div className="bg-slate-100 rounded-full h-1.5">
                        <div className={`h-1.5 rounded-full ${good ? 'bg-emerald-500' : 'bg-amber-400'}`} style={{ width: `${Math.min(100, value)}%` }} />
                      </div>
                      <div className="text-xs text-slate-400 mt-0.5">目标：{target}%</div>
                    </div>
                  )
                })}
              </div>
            </div>
          )}

          {/* 协作记录 */}
          {(tab === (isIntern ? 2 : 4)) && (
            <div className="bg-white rounded-xl border border-slate-100 p-5">
              <h3 className="font-semibold text-slate-800 mb-4">近期协作记录</h3>
              <div className="space-y-3">
                {[
                  { time: '今天 14:32', action: '完成商机跟进提醒，通知 3 位销售及时跟进', type: 'ok' },
                  { time: '今天 11:15', action: '自动更新 CRM 中 5 条商机阶段', type: 'ok' },
                  { time: '昨天 16:40', action: '生成会议纪要并同步至项目文档', type: 'ok' },
                  { time: '昨天 10:20', action: '检测到商机超期未跟进，已升级提醒给销售主管', type: 'warn' },
                ].map((r, i) => (
                  <div key={i} className="flex items-start gap-3 text-sm">
                    <div className={`mt-0.5 w-2 h-2 rounded-full shrink-0 ${r.type === 'ok' ? 'bg-emerald-400' : 'bg-amber-400'}`} />
                    <div className="flex-1">
                      <div className="text-slate-700">{r.action}</div>
                      <div className="text-xs text-slate-400 mt-0.5">{r.time}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* 配置进度 */}
          {(isIntern && tab === 3) && (
            <div className="bg-white rounded-xl border border-slate-100 p-5">
              <div className="flex items-center justify-between mb-4">
                <h3 className="font-semibold text-slate-800">能力配置进度</h3>
                <div className="text-xs text-slate-500">
                  {e.capabilities.filter(c => c.ready).length} / {e.capabilities.length} 已就绪
                </div>
              </div>
              
              <div className="space-y-3">
                {e.capabilities.map(cap => {
                  return (
                    <div key={cap.name} className={`p-3 rounded-lg border transition-all ${
                      cap.ready 
                        ? 'border-emerald-100 bg-emerald-50/30' 
                        : 'border-amber-100 bg-amber-50/30 hover:shadow-sm'
                    }`}>
                      <div className="flex items-center gap-3">
                        {cap.ready ? (
                          <CheckCircle size={16} className="text-emerald-500 shrink-0" />
                        ) : (
                          <AlertCircle size={16} className="text-amber-500 shrink-0" />
                        )}
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2 mb-1">
                            <span className="text-sm font-medium text-slate-700">{cap.name}</span>
                            <Badge variant={cap.ready ? 'green' : 'yellow'}>
                              {cap.ready ? '已就绪' : '待配置'}
                            </Badge>
                          </div>
                          {!cap.ready && (
                            <div className="text-xs text-slate-500">需要完成相关配置</div>
                          )}
                        </div>
                        {!cap.ready && (
                          <button
                            onClick={() => handleCapabilityConfig(cap)}
                            className="px-3 py-1.5 bg-indigo-600 text-white rounded-lg text-xs font-medium hover:bg-indigo-700 transition-colors"
                          >
                            去配置
                          </button>
                        )}
                      </div>
                    </div>
                  )
                })}
              </div>

              {/* 配置完成度提示 */}
              {e.capabilities.some(c => !c.ready) && (
                <div className="mt-4 p-3 bg-indigo-50 border border-indigo-100 rounded-lg">
                  <div className="flex items-start gap-2">
                    <AlertCircle size={14} className="text-indigo-600 mt-0.5 shrink-0" />
                    <div className="text-xs text-indigo-700">
                      <strong>提示：</strong>完成所有能力配置后，数字员工才能发挥完整能力。建议优先配置核心能力，再启动实习。
                    </div>
                  </div>
                </div>
              )}
            </div>
          )}

          {/* 成长轨迹 */}
          {(!isIntern && tab === 2) && (
            <div className="bg-white rounded-xl border border-slate-100 p-5">
              <h3 className="font-semibold text-slate-800 mb-4 flex items-center gap-2">
                <TrendingUp size={15} className="text-indigo-500" />
                成长轨迹
              </h3>
              <div className="space-y-3">
                {[
                  { date: '2026-03-05', event: '转正，进入正式工作状态', type: 'milestone' },
                  { date: '2026-02-28', event: '完成首次全量 CRM 数据同步，零错误', type: 'ok' },
                  { date: '2026-02-20', event: '通话纪要能力解锁，准确率 94%', type: 'ok' },
                  { date: '2026-02-11', event: '开始实习，初步理解销售流程', type: 'milestone' },
                ].map((ev, i) => (
                  <div key={i} className="flex items-start gap-3">
                    <div className={`mt-0.5 w-2 h-2 rounded-full shrink-0 ${ev.type === 'milestone' ? 'bg-indigo-500' : 'bg-emerald-400'}`} />
                    <div>
                      <div className="text-sm text-slate-700">{ev.event}</div>
                      <div className="text-xs text-slate-400">{ev.date}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* 授权管理 */}
          {(!isIntern && tab === 3) && (
            <div className="bg-white rounded-xl border border-slate-100 p-5">
              <h3 className="font-semibold text-slate-800 mb-4 flex items-center gap-2">
                <Shield size={15} className="text-indigo-500" />
                授权管理
              </h3>
              <div className="space-y-3">
                {[
                  { scope: 'CRM 数据读取', level: '完全授权', granted: true },
                  { scope: 'CRM 商机更新', level: '部分授权（阶段字段）', granted: true },
                  { scope: 'IM 消息推送', level: '完全授权', granted: true },
                  { scope: '合同附件访问', level: '未授权', granted: false },
                  { scope: '客户档案删除', level: '未授权', granted: false },
                ].map((auth, i) => (
                  <div key={i} className="flex items-center gap-3 p-3 rounded-lg bg-slate-50">
                    <div className={`w-2 h-2 rounded-full shrink-0 ${auth.granted ? 'bg-emerald-400' : 'bg-slate-300'}`} />
                    <span className="text-sm text-slate-700 flex-1">{auth.scope}</span>
                    <span className={`text-xs ${auth.granted ? 'text-emerald-600' : 'text-slate-400'}`}>{auth.level}</span>
                    <button className="text-xs text-indigo-500 hover:text-indigo-700">调整</button>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

      {/* 转正确认对话框 */}
      {showPromotionDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={() => setShowPromotionDialog(false)}>
          <div className="bg-white rounded-xl p-6 max-w-md w-full mx-4" onClick={e => e.stopPropagation()}>
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 bg-emerald-100 rounded-full flex items-center justify-center">
                <UserCheck size={20} className="text-emerald-600" />
              </div>
              <h3 className="text-lg font-bold text-slate-800">确认转正</h3>
            </div>
            <p className="text-sm text-slate-600 mb-4">
              确认将 <strong>{e.nickname}</strong> 转为正式员工？转正后将进入正式工作状态，可以逐步扩展授权范围。
            </p>
            <div className="bg-emerald-50 border border-emerald-100 rounded-lg p-3 mb-4">
              <div className="text-xs font-semibold text-emerald-700 mb-2">转正条件检查</div>
              <div className="space-y-1">
                <div className="flex items-center gap-2 text-xs text-emerald-600">
                  <CheckCircle size={12} />
                  <span>已完成 {e.tasksDone} 个任务</span>
                </div>
                {e.satisfactionScore && (
                  <div className="flex items-center gap-2 text-xs text-emerald-600">
                    <CheckCircle size={12} />
                    <span>满意度 {e.satisfactionScore}/5.0</span>
                  </div>
                )}
                <div className="flex items-center gap-2 text-xs text-emerald-600">
                  <CheckCircle size={12} />
                  <span>核心能力已就绪</span>
                </div>
              </div>
            </div>
            <div className="flex gap-3">
              <button
                onClick={() => setShowPromotionDialog(false)}
                className="flex-1 px-4 py-2 border border-slate-200 text-slate-600 rounded-lg text-sm hover:bg-slate-50 transition-colors"
              >
                取消
              </button>
              <button
                onClick={handlePromote}
                className="flex-1 px-4 py-2 bg-emerald-600 text-white rounded-lg text-sm font-medium hover:bg-emerald-700 transition-colors"
              >
                确认转正
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 离职确认对话框 */}
      {showResignDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50" onClick={() => setShowResignDialog(false)}>
          <div className="bg-white rounded-xl p-6 max-w-md w-full mx-4" onClick={e => e.stopPropagation()}>
            <div className="flex items-center gap-3 mb-4">
              <div className="w-10 h-10 bg-red-100 rounded-full flex items-center justify-center">
                <UserX size={20} className="text-red-600" />
              </div>
              <h3 className="text-lg font-bold text-slate-800">开始离职流程</h3>
            </div>
            <p className="text-sm text-slate-600 mb-4">
              确认让 <strong>{e.nickname}</strong> 开始离职流程？需要完成工作交接、知识转移和权限回收后才能归档。
            </p>
            <div className="bg-amber-50 border border-amber-100 rounded-lg p-3 mb-4">
              <div className="text-xs font-semibold text-amber-700 mb-2">离职后需要处理</div>
              <div className="space-y-1">
                <div className="flex items-center gap-2 text-xs text-amber-600">
                  <AlertCircle size={12} />
                  <span>完成工作交接</span>
                </div>
                <div className="flex items-center gap-2 text-xs text-amber-600">
                  <AlertCircle size={12} />
                  <span>知识转移给其他成员</span>
                </div>
                <div className="flex items-center gap-2 text-xs text-amber-600">
                  <AlertCircle size={12} />
                  <span>回收系统权限</span>
                </div>
              </div>
            </div>
            <div className="flex gap-3">
              <button
                onClick={() => setShowResignDialog(false)}
                className="flex-1 px-4 py-2 border border-slate-200 text-slate-600 rounded-lg text-sm hover:bg-slate-50 transition-colors"
              >
                取消
              </button>
              <button
                onClick={handleResign}
                className="flex-1 px-4 py-2 bg-red-600 text-white rounded-lg text-sm font-medium hover:bg-red-700 transition-colors"
              >
                确认离职
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 处理待办事项对话框 */}
      {showActionDialog && currentAction && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-6" onClick={() => setShowActionDialog(false)}>
          <div className="bg-white rounded-xl p-6 max-w-lg w-full" onClick={e => e.stopPropagation()}>
            <div className="flex items-center gap-3 mb-4">
              <div className={`w-10 h-10 rounded-full flex items-center justify-center ${
                getActionSuggestion(currentAction).needsIM ? 'bg-indigo-100' : 'bg-amber-100'
              }`}>
                {getActionSuggestion(currentAction).needsIM ? (
                  <MessageCircle size={20} className="text-indigo-600" />
                ) : (
                  <AlertCircle size={20} className="text-amber-600" />
                )}
              </div>
              <h3 className="text-lg font-bold text-slate-800">{getActionSuggestion(currentAction).title}</h3>
            </div>
            
            <div className="mb-4">
              <div className="text-sm text-slate-600 mb-3">
                <strong>待处理事项：</strong>{currentAction}
              </div>
              <p className="text-sm text-slate-600 mb-3">
                {getActionSuggestion(currentAction).description}
              </p>
            </div>

            {/* 如果需要在 IM 中处理，显示 IM 群组信息 */}
            {getActionSuggestion(currentAction).needsIM && getActionSuggestion(currentAction).imGroupName && (
              <div className="bg-indigo-50 border border-indigo-200 rounded-lg p-4 mb-4">
                <div className="flex items-center gap-2 mb-2">
                  <span className="text-2xl">{getIMPlatformIcon(getActionSuggestion(currentAction).imGroupName!.platform as IMPlatform)}</span>
                  <div className="flex-1">
                    <div className="text-sm font-semibold text-indigo-800">
                      {getActionSuggestion(currentAction).imGroupName!.groupName}
                    </div>
                    <div className="text-xs text-indigo-600">
                      {getActionSuggestion(currentAction).imGroupName!.platform}
                    </div>
                  </div>
                </div>
                <button
                  onClick={() => {
                    const imInfo = getActionSuggestion(currentAction).imGroupName!
                    openIMGroup({
                      platform: imInfo.platform as IMPlatform,
                      groupId: imInfo.groupId,
                      groupName: imInfo.groupName
                    })
                  }}
                  className="w-full mt-2 px-4 py-2 bg-indigo-600 text-white rounded-lg text-sm font-medium hover:bg-indigo-700 transition-colors flex items-center justify-center gap-2"
                >
                  <ExternalLink size={14} />
                  打开 {getActionSuggestion(currentAction).imGroupName!.platform} 协作群
                </button>
              </div>
            )}

            <div className={`${getActionSuggestion(currentAction).needsIM ? 'bg-slate-50' : 'bg-indigo-50'} border ${
              getActionSuggestion(currentAction).needsIM ? 'border-slate-200' : 'border-indigo-100'
            } rounded-lg p-4 mb-4`}>
              <div className={`text-xs font-semibold mb-2 ${
                getActionSuggestion(currentAction).needsIM ? 'text-slate-700' : 'text-indigo-700'
              }`}>
                {getActionSuggestion(currentAction).needsIM ? '处理流程' : '处理步骤建议'}
              </div>
              <ol className="space-y-1.5">
                {getActionSuggestion(currentAction).steps.map((step, i) => (
                  <li key={i} className={`flex items-start gap-2 text-xs ${
                    getActionSuggestion(currentAction).needsIM ? 'text-slate-600' : 'text-indigo-600'
                  }`}>
                    <span className="font-medium shrink-0">{i + 1}.</span>
                    <span>{step}</span>
                  </li>
                ))}
              </ol>
            </div>

            {getActionSuggestion(currentAction).needsIM && (
              <div className="bg-amber-50 border border-amber-200 rounded-lg p-3 mb-4 text-xs text-amber-700">
                <strong>💡 提示：</strong>在 IM 中处理完成后，数字员工 Bot 会自动将状态同步回 NCrew，无需手动标记。
              </div>
            )}

            <div className="flex gap-3">
              <button
                onClick={() => setShowActionDialog(false)}
                className="flex-1 px-4 py-2 border border-slate-200 text-slate-600 rounded-lg text-sm hover:bg-slate-50 transition-colors"
              >
                {getActionSuggestion(currentAction).needsIM ? '关闭' : '稍后处理'}
              </button>
              {!getActionSuggestion(currentAction).needsIM && (
                <button
                  onClick={completeAction}
                  className="flex-1 px-4 py-2 bg-emerald-600 text-white rounded-lg text-sm font-medium hover:bg-emerald-700 transition-colors flex items-center justify-center gap-1.5"
                >
                  <CheckCircle size={14} />
                  标记为已完成
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* 能力配置对话框 */}
      {showConfigDialog && currentCapability && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-6" onClick={() => setShowConfigDialog(false)}>
          <div className="bg-white rounded-xl p-6 max-w-2xl w-full max-h-[90vh] overflow-y-auto" onClick={e => e.stopPropagation()}>
            <div className="flex items-center justify-between mb-6">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 bg-slate-100 rounded-lg flex items-center justify-center">
                  <Shield size={20} className="text-slate-600" />
                </div>
                <div>
                  <h3 className="text-lg font-semibold text-slate-900">配置能力</h3>
                  <p className="text-sm text-slate-500">{currentCapability.name}</p>
                </div>
              </div>
              <button
                onClick={() => setShowConfigDialog(false)}
                className="p-2 hover:bg-slate-100 rounded-lg transition-colors"
              >
                <X size={20} className="text-slate-400" />
              </button>
            </div>

            {/* 文件上传区域 */}
            <div className="mb-6">
              <label className="block text-sm font-medium text-slate-700 mb-3">
                上传配置文件
                <span className="text-slate-400 font-normal ml-2">（知识库、模板、规则文档等）</span>
              </label>
              
              {/* 上传按钮 */}
              <label className="block">
                <input
                  type="file"
                  multiple
                  onChange={handleFileUpload}
                  className="hidden"
                  accept=".pdf,.doc,.docx,.xls,.xlsx,.txt,.md,.json"
                />
                <div className="border-2 border-dashed border-slate-200 rounded-lg p-6 hover:border-slate-300 hover:bg-slate-50 transition-all cursor-pointer">
                  <div className="flex flex-col items-center gap-2">
                    <div className="w-12 h-12 bg-slate-100 rounded-full flex items-center justify-center">
                      <Upload size={20} className="text-slate-400" />
                    </div>
                    <div className="text-center">
                      <p className="text-sm font-medium text-slate-700">点击上传文件</p>
                      <p className="text-xs text-slate-400 mt-1">支持 PDF, Word, Excel, TXT, Markdown 等格式</p>
                    </div>
                  </div>
                </div>
              </label>

              {/* 已上传文件列表 */}
              {uploadedFiles.length > 0 && (
                <div className="mt-4 space-y-2">
                  {uploadedFiles.map((file, index) => (
                    <div key={index} className="flex items-center gap-3 p-3 bg-slate-50 rounded-lg">
                      <div className="w-8 h-8 bg-white rounded flex items-center justify-center shrink-0">
                        <span className="text-xs font-medium text-slate-600">
                          {file.name.split('.').pop()?.toUpperCase()}
                        </span>
                      </div>
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium text-slate-700 truncate">{file.name}</p>
                        <p className="text-xs text-slate-400">
                          {(file.size / 1024).toFixed(1)} KB
                        </p>
                      </div>
                      <button
                        onClick={() => removeFile(index)}
                        className="p-1 hover:bg-slate-200 rounded transition-colors"
                      >
                        <X size={16} className="text-slate-400" />
                      </button>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* API 配置 */}
            <div className="mb-6">
              <label className="block text-sm font-medium text-slate-700 mb-3">
                API 配置
                <span className="text-slate-400 font-normal ml-2">（可选）</span>
              </label>
              <div className="space-y-3">
                <div>
                  <label className="block text-xs text-slate-500 mb-1">API 密钥</label>
                  <input
                    type="password"
                    value={configForm.apiKey}
                    onChange={e => setConfigForm(prev => ({ ...prev, apiKey: e.target.value }))}
                    placeholder="输入 API Key"
                    className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:border-slate-300"
                  />
                </div>
                <div>
                  <label className="block text-xs text-slate-500 mb-1">API 端点</label>
                  <input
                    type="text"
                    value={configForm.apiEndpoint}
                    onChange={e => setConfigForm(prev => ({ ...prev, apiEndpoint: e.target.value }))}
                    placeholder="https://api.example.com"
                    className="w-full px-3 py-2 border border-slate-200 rounded-lg text-sm focus:outline-none focus:border-slate-300"
                  />
                </div>
              </div>
            </div>

            {/* 权限设置 */}
            <div className="mb-6">
              <label className="block text-sm font-medium text-slate-700 mb-3">权限设置</label>
              <div className="space-y-2">
                {['只读权限', '读写权限', '管理员权限'].map(permission => (
                  <label key={permission} className="flex items-center gap-2 p-2 hover:bg-slate-50 rounded cursor-pointer">
                    <input
                      type="checkbox"
                      checked={configForm.permissions.includes(permission)}
                      onChange={e => {
                        if (e.target.checked) {
                          setConfigForm(prev => ({ ...prev, permissions: [...prev.permissions, permission] }))
                        } else {
                          setConfigForm(prev => ({ ...prev, permissions: prev.permissions.filter(p => p !== permission) }))
                        }
                      }}
                      className="w-4 h-4 text-slate-900 border-slate-300 rounded focus:ring-slate-500"
                    />
                    <span className="text-sm text-slate-700">{permission}</span>
                  </label>
                ))}
              </div>
            </div>

            {/* 提示信息 */}
            <div className="bg-blue-50 border border-blue-100 rounded-lg p-3 mb-6">
              <div className="flex gap-2">
                <AlertCircle size={16} className="text-blue-600 shrink-0 mt-0.5" />
                <div className="text-xs text-blue-700">
                  <strong>提示：</strong>配置完成后，系统会自动检测并更新能力状态为"已就绪"。你可以随时回来修改配置。
                </div>
              </div>
            </div>

            {/* 操作按钮 */}
            <div className="flex gap-3">
              <button
                onClick={() => setShowConfigDialog(false)}
                className="flex-1 px-4 py-2.5 border border-slate-200 text-slate-600 rounded-lg text-sm font-medium hover:bg-slate-50 transition-colors"
              >
                取消
              </button>
              <button
                onClick={completeCapabilityConfig}
                disabled={uploadedFiles.length === 0 && !configForm.apiKey}
                className="flex-1 px-4 py-2.5 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
              >
                完成配置
              </button>
            </div>
          </div>
        </div>
      )}
      </div>
    </div>
  )
}
