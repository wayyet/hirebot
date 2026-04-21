import { useState, useRef, useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Sparkles, Send, CheckCircle, AlertCircle, Loader2, User, History, Trash2, Plus } from 'lucide-react'
import {
  loadConversations,
  saveConversation,
  deleteConversation,
  getConversation,
  createConversation,
  type DiscoveryConversation,
} from '../utils/storage'

type ConversationRound = 'intro' | 'round0' | 'round1' | 'round2' | 'round3' | 'round4' | 'round5' | 'confirm' | 'creating'

interface Message {
  role: 'system' | 'user'
  content: string
  options?: string[]
  isThinking?: boolean
}

export default function CustomEmployeePage() {
  const navigate = useNavigate()
  const { conversationId } = useParams()
  const messagesEndRef = useRef<HTMLDivElement>(null)
  
  // 会话列表相关
  const [showHistory, setShowHistory] = useState(false)
  const [conversations, setConversations] = useState<DiscoveryConversation[]>([])
  const [currentConversationId, setCurrentConversationId] = useState<string>('')
  
  // 对话状态
  const [round, setRound] = useState<ConversationRound>('intro')
  const [messages, setMessages] = useState<Message[]>([
    {
      role: 'system',
      content: '你好！我是 NCrew 的场景发现助手。\n\n我会像一个 discovery bot 一样，一轮一轮帮你把场景收敛清楚；每轮只问少量关键问题，不会一下子铺开。\n\n最后我会给你一份简报，回答这几个问题：\n1. 用哪个数字员工解决什么问题\n2. 这个场景需要什么 Ontology 切片\n3. 需要哪些 Skills\n4. 每个 Skill 需要怎样的 CLI / 系统 / 数据对接\n\n准备好了吗？我们开始吧！',
    }
  ])
  const [input, setInput] = useState('')
  const [discoveryData, setDiscoveryData] = useState({
    scenario: '',
    beneficiary: '',
    realCase: {
      when: '',
      who: '',
      steps: '',
      pain: '',
      consequence: '',
    },
    employee: {
      name: '',
      roleMetaphor: '',
      mission: '',
      keyJudgment: '',
      autonomy: '',
      deliverable: '',
      criticalFailure: '',
    },
    ontology: {
      entities: [] as string[],
      actions: [] as string[],
      resources: [] as string[],
      constraints: [] as string[],
    },
    skills: [] as Array<{
      name: string
      purpose: string
      trigger: string
      input: string
      output: string
      autonomy: string
    }>,
    clis: [] as Array<{
      skill: string
      system: string
      action: string
      interface: string
      auth: string
    }>,
  })

  // 加载会话列表
  useEffect(() => {
    const loadedConversations = loadConversations()
    setConversations(loadedConversations)
    
    // 如果有 conversationId 参数，加载对应的会话
    if (conversationId) {
      const conversation = getConversation(conversationId)
      if (conversation) {
        loadConversationState(conversation)
      }
    } else if (loadedConversations.length === 0) {
      // 如果没有历史会话，创建新会话
      startNewConversation()
    }
  }, [conversationId])

  // 监听 Escape 键关闭抽屉
  useEffect(() => {
    const handleEscape = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && showHistory) {
        setShowHistory(false)
      }
    }
    
    window.addEventListener('keydown', handleEscape)
    return () => window.removeEventListener('keydown', handleEscape)
  }, [showHistory])

  // 抽屉打开时禁止背景滚动
  useEffect(() => {
    if (showHistory) {
      document.body.style.overflow = 'hidden'
    } else {
      document.body.style.overflow = 'unset'
    }
    
    return () => {
      document.body.style.overflow = 'unset'
    }
  }, [showHistory])

  // 加载会话状态
  const loadConversationState = (conversation: DiscoveryConversation) => {
    setCurrentConversationId(conversation.id)
    setRound(conversation.round as ConversationRound)
    setMessages(conversation.messages)
    setDiscoveryData(conversation.discoveryData)
  }

  // 开始新对话
  const startNewConversation = () => {
    const newConv = createConversation()
    newConv.messages = [
      {
        role: 'system',
        content: '你好！我是 NCrew 的场景发现助手。\n\n我会像一个 discovery bot 一样，一轮一轮帮你把场景收敛清楚；每轮只问少量关键问题，不会一下子铺开。\n\n最后我会给你一份简报，回答这几个问题：\n1. 用哪个数字员工解决什么问题\n2. 这个场景需要什么 Ontology 切片\n3. 需要哪些 Skills\n4. 每个 Skill 需要怎样的 CLI / 系统 / 数据对接\n\n准备好了吗？我们开始吧！',
      }
    ]
    saveConversation(newConv)
    setConversations([newConv, ...loadConversations()])
    loadConversationState(newConv)
    navigate(`/custom-employee/${newConv.id}`)
  }

  // 保存当前会话状态
  const saveCurrentConversation = () => {
    if (!currentConversationId) return
    
    const conversation = getConversation(currentConversationId)
    if (!conversation) return
    
    conversation.round = round
    conversation.messages = messages
    conversation.discoveryData = discoveryData
    
    // 自动生成会话名称
    if (conversation.name === '新对话' && discoveryData.scenario) {
      conversation.name = discoveryData.scenario.substring(0, 30) + (discoveryData.scenario.length > 30 ? '...' : '')
    }
    
    saveConversation(conversation)
  }

  // 每次状态变化时保存
  useEffect(() => {
    if (currentConversationId && messages.length > 1) {
      saveCurrentConversation()
    }
  }, [messages, discoveryData, round])

  // 删除会话
  const handleDeleteConversation = (id: string, e: React.MouseEvent) => {
    e.stopPropagation()
    if (confirm('确定要删除这个对话吗？')) {
      deleteConversation(id)
      const updated = loadConversations()
      setConversations(updated)
      
      if (id === currentConversationId) {
        if (updated.length > 0) {
          loadConversationState(updated[0])
          navigate(`/custom-employee/${updated[0].id}`)
        } else {
          startNewConversation()
        }
      }
    }
  }

  // 切换会话
  const handleSwitchConversation = (conversation: DiscoveryConversation) => {
    loadConversationState(conversation)
    navigate(`/custom-employee/${conversation.id}`)
    setShowHistory(false)
  }

  // 自动滚动到底部
  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' })
  }, [messages])

  const handleSend = () => {
    if (!input.trim()) return

    // 添加用户消息
    const userMessage: Message = { role: 'user', content: input }
    setMessages(prev => [...prev, userMessage])

    // 显示思考状态
    setMessages(prev => [...prev, { role: 'system', content: '思考中...', isThinking: true }])

    // 根据当前轮次生成系统回复
    setTimeout(() => {
      handleSystemResponse(input)
    }, 1200)

    setInput('')
  }

  const handleSystemResponse = (userInput: string) => {
    // 移除思考状态
    setMessages(prev => prev.filter(m => !m.isThinking))

    let systemMessage: Message
    let summary: string[] = []

    switch (round) {
      case 'intro':
        // Round 0: 锁定场景和框架
        systemMessage = {
          role: 'system',
          content: '很好！我先锁定场景和表达框架。\n\n请回答：\n1. 你现在最想梳理的是哪个场景？\n2. 这个场景如果理清楚了，谁会最先受益？\n\n我们会按 NCrew 的四层来聊：数字员工、Ontology slice、Skills、CLI，这样可以吗？',
        }
        setRound('round0')
        break

      case 'round0':
        // Round 1: 问题和当前人工角色
        summary = [
          `场景：${userInput.split('\n')[0] || userInput.substring(0, 50)}`,
          '框架：按 NCrew 四层收敛',
        ]
        systemMessage = {
          role: 'system',
          content: `我理解了：\n${summary.map(s => `• ${s}`).join('\n')}\n\n这一轮我先不谈方案，我先抓一个最近真实发生的例子。\n\n请告诉我：\n1. 最近一次这个场景真实发生是什么时候？\n2. 当时是谁接手的？第一步做了什么？\n3. 哪一步最麻烦、最容易卡住，或者最容易判断不一致？\n4. 一旦这里做错或做慢，后果是什么？`,
        }
        setDiscoveryData(prev => ({ ...prev, scenario: userInput }))
        setRound('round1')
        break

      case 'round1':
        // Round 2: 数字员工定义
        summary = [
          '已捕获真实案例',
          '识别了关键痛点和后果',
        ]
        systemMessage = {
          role: 'system',
          content: `明白了：\n${summary.map(s => `• ${s}`).join('\n')}\n\n现在我先把这个数字员工本人定义清楚，让它像一个可信的同事。\n\n请回答：\n1. 如果把这件事交给一个数字员工，它最像团队里的哪一类员工？\n2. 一个优秀的人做这件事时，最关键的判断力体现在哪里？\n3. 哪些动作它能自主做到什么程度？哪些必须保留给人？\n4. 它最直接的交付结果是什么？最不能犯的错误又是什么？`,
        }
        setDiscoveryData(prev => ({
          ...prev,
          realCase: {
            when: userInput.split('\n')[0] || '',
            who: userInput.split('\n')[1] || '',
            steps: userInput.split('\n')[2] || '',
            pain: userInput.split('\n')[3] || '',
            consequence: userInput.split('\n')[4] || '',
          }
        }))
        setRound('round2')
        break

      case 'round2':
        // Round 3: Ontology slice
        const employeeName = generateEmployeeName(userInput)
        summary = [
          `数字员工：${employeeName}`,
          '已定义角色、使命和边界',
        ]
        systemMessage = {
          role: 'system',
          content: `很好：\n${summary.map(s => `• ${s}`).join('\n')}\n\n这一轮我只定义它完成这个场景所需的最小 Ontology slice，而且会用刚才那个真实案例来落地。\n\n请告诉我：\n1. 在那个真实案例里，它必须理解哪些业务对象？（如：客户、订单、合同）\n2. 它要执行哪些动作？（如：审核、通知、生成）\n3. 它依赖哪些系统、知识源、沟通渠道？\n4. 它必须遵守哪些规则或红线？`,
        }
        setDiscoveryData(prev => ({
          ...prev,
          employee: {
            name: employeeName,
            roleMetaphor: userInput.split('\n')[0] || '',
            mission: userInput.split('\n')[1] || '',
            keyJudgment: userInput.split('\n')[2] || '',
            autonomy: userInput.split('\n')[3] || '',
            deliverable: userInput.split('\n')[4] || '',
            criticalFailure: userInput.split('\n')[5] || '',
          }
        }))
        setRound('round3')
        break

      case 'round3':
        // Round 4: Skills
        summary = [
          'Ontology slice 已定义',
          '包含实体、动作、资源、约束',
        ]
        systemMessage = {
          role: 'system',
          content: `明白了：\n${summary.map(s => `• ${s}`).join('\n')}\n\n现在不谈平台模块，我只拆这个数字员工需要哪些 Skills。\n\n请告诉我：\n1. 为了完成这个场景，这个数字员工需要哪些 Skills？\n2. 每个 Skill 的输入输出分别是什么？\n3. 哪些 Skill 可以复用到相邻场景？\n\n（建议 3-7 个 Skills，不要太细碎）`,
        }
        const ontologyLines = userInput.split('\n')
        setDiscoveryData(prev => ({
          ...prev,
          ontology: {
            entities: ontologyLines[0]?.split('、') || [],
            actions: ontologyLines[1]?.split('、') || [],
            resources: ontologyLines[2]?.split('、') || [],
            constraints: ontologyLines[3]?.split('、') || [],
          }
        }))
        setRound('round4')
        break

      case 'round4':
        // Round 5: CLI 和数据对接
        summary = [
          `已识别 ${userInput.split('\n').length} 个 Skills`,
          '每个 Skill 都有明确的输入输出',
        ]
        systemMessage = {
          role: 'system',
          content: `很好：\n${summary.map(s => `• ${s}`).join('\n')}\n\n最后一轮我只确认工具层，也就是 CLI / 系统 / 数据对接。\n\n请告诉我：\n1. 哪些 Skill 要访问系统或外部数据？\n2. 对这些访问，理想的 CLI 或接口调用长什么样？\n3. 它们是只读、可写，还是需要通知/审批？\n\n（例如：crm:search-customer(name, region) -> accounts[]）`,
        }
        // 简单解析 skills
        const skillLines = userInput.split('\n').filter(l => l.trim())
        setDiscoveryData(prev => ({
          ...prev,
          skills: skillLines.map(line => ({
            name: line.split('：')[0] || line,
            purpose: line.split('：')[1] || '',
            trigger: '',
            input: '',
            output: '',
            autonomy: '',
          }))
        }))
        setRound('round5')
        break

      case 'round5':
        // 最终确认
        summary = [
          'CLI 和系统对接已明确',
          '所有四层都已收敛',
        ]
        systemMessage = {
          role: 'system',
          content: `完成了：\n${summary.map(s => `• ${s}`).join('\n')}\n\n让我为你生成最终的数字员工发现简报...\n\n这份简报会包含：\n• 为什么需要这个数字员工\n• 数字员工画像卡\n• Day-1 工作方式\n• Ontology slice\n• 所需 Skills\n• CLI 和系统对接\n• 人机协作方式\n• 预期业务效果`,
          options: ['生成简报', '我想修改']
        }
        setRound('confirm')
        break

      default:
        systemMessage = { role: 'system', content: '处理中...' }
    }

    setMessages(prev => [...prev, systemMessage])
  }

  const handleOptionClick = (option: string) => {
    // 添加用户选择
    const userMessage: Message = { role: 'user', content: option }
    setMessages(prev => [...prev, userMessage])

    if (option === '生成简报') {
      // 开始创建
      setRound('creating')
      setMessages(prev => [...prev, {
        role: 'system',
        content: '正在生成数字员工发现简报...',
        isThinking: true
      }])

      setTimeout(() => {
        setMessages(prev => prev.filter(m => !m.isThinking))
        setMessages(prev => [...prev, {
          role: 'system',
          content: `✅ 简报生成完成！\n\n数字员工：${discoveryData.employee.name}\n场景：${discoveryData.scenario.substring(0, 50)}...\n\n我会为你创建一个企业专属的数字员工实例。\n\n💡 提示：创建后，你可以选择：\n• 直接使用（企业私有）\n• 保存为企业模板（企业内复用）\n• 贡献到公共市场（分享给其他企业）`,
          options: ['创建实例', '先保存为模板']
        }])
      }, 2000)
    } else if (option === '创建实例') {
      // 直接创建实例
      setMessages(prev => [...prev, {
        role: 'system',
        content: '正在创建数字员工实例...',
        isThinking: true
      }])

      setTimeout(() => {
        navigate('/hiring', {
          state: {
            customEmployee: {
              name: discoveryData.employee.name,
              description: discoveryData.scenario,
              discoveryData: discoveryData,
              isTemplate: false, // 标记为直接实例
            }
          }
        })
      }, 1500)
    } else if (option === '先保存为模板') {
      // 保存为企业模板
      setMessages(prev => [...prev, {
        role: 'system',
        content: '正在保存为企业模板...\n\n企业模板的优势：\n• 可以在企业内多次雇佣\n• 支持版本管理和迭代\n• 可以分享给团队其他成员\n• 未来可选择发布到公共市场',
        isThinking: true
      }])

      setTimeout(() => {
        navigate('/hiring', {
          state: {
            customEmployee: {
              name: discoveryData.employee.name,
              description: discoveryData.scenario,
              discoveryData: discoveryData,
              isTemplate: true, // 标记为模板
            }
          }
        })
      }, 1500)
    } else if (option === '我想修改') {
      // 重新开始
      setRound('intro')
      setMessages([{
        role: 'system',
        content: '好的，让我们重新开始。你想修改哪一部分？\n\n我们可以从头开始，或者你告诉我想调整哪个环节。',
      }])
    } else {
      // 其他选项，继续对话
      setTimeout(() => {
        handleSystemResponse(option)
      }, 800)
    }
  }

  const generateEmployeeName = (description: string) => {
    // 基于用户描述生成员工名称
    if (description.includes('会议') || description.includes('纪要')) return '会议纪要助手'
    if (description.includes('报表') || description.includes('数据')) return '数据报表助手'
    if (description.includes('客服') || description.includes('咨询')) return '智能客服助手'
    if (description.includes('审核') || description.includes('审批')) return '智能审核助手'
    if (description.includes('销售') || description.includes('商机')) return '销售跟进助手'
    if (description.includes('合同') || description.includes('法务')) return '合同审核助手'
    if (description.includes('招聘') || description.includes('简历')) return '招聘初筛助手'
    return '定制数字员工'
  }

  return (
    <div className="max-w-4xl mx-auto px-6 py-6">
      <div className="flex items-center justify-between mb-5">
        <button 
          onClick={() => navigate('/market')} 
          className="flex items-center gap-1.5 text-sm text-slate-500 hover:text-slate-700 transition-colors"
        >
          <ArrowLeft size={14} />
          返回市场
        </button>
        
        <div className="flex items-center gap-2">
          <button
            onClick={() => setShowHistory(!showHistory)}
            className="relative p-2 text-slate-600 hover:text-slate-800 hover:bg-slate-100 rounded-lg transition-colors"
            title="历史会话"
          >
            <History size={20} />
            {conversations.length > 0 && (
              <span className="absolute -top-1 -right-1 min-w-[18px] h-[18px] flex items-center justify-center px-1 bg-slate-900 text-white text-[10px] font-semibold rounded-full">
                {conversations.length}
              </span>
            )}
          </button>
          <button
            onClick={startNewConversation}
            className="flex items-center gap-1.5 px-3 py-1.5 text-sm text-white bg-slate-900 rounded-lg hover:bg-slate-800 transition-colors"
          >
            <Plus size={14} />
            新对话
          </button>
        </div>
      </div>

      {/* 历史会话抽屉 - 遮罩层 */}
      {showHistory && (
        <div
          className="fixed inset-0 bg-black/20 z-40 transition-opacity"
          onClick={() => setShowHistory(false)}
        />
      )}

      {/* 历史会话抽屉 - 右侧滑出 */}
      <div
        className={`fixed top-0 right-0 h-full w-96 bg-white shadow-2xl z-50 transform transition-transform duration-300 ease-in-out ${
          showHistory ? 'translate-x-0' : 'translate-x-full'
        }`}
      >
        <div className="flex flex-col h-full">
          {/* 抽屉头部 */}
          <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200">
            <h2 className="text-lg font-semibold text-slate-800">历史会话</h2>
            <button
              onClick={() => setShowHistory(false)}
              className="p-1.5 text-slate-400 hover:text-slate-600 hover:bg-slate-100 rounded-lg transition-colors"
            >
              <svg width="20" height="20" viewBox="0 0 20 20" fill="none">
                <path d="M15 5L5 15M5 5L15 15" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
              </svg>
            </button>
          </div>

          {/* 抽屉内容 */}
          <div className="flex-1 overflow-y-auto">
            {conversations.length === 0 ? (
              <div className="flex flex-col items-center justify-center h-full px-6 text-center">
                <div className="w-16 h-16 bg-slate-100 rounded-full flex items-center justify-center mb-4">
                  <History size={28} className="text-slate-400" />
                </div>
                <p className="text-sm text-slate-500 mb-2">暂无历史会话</p>
                <p className="text-xs text-slate-400">开始新对话来创建你的第一个数字员工</p>
              </div>
            ) : (
              <div className="divide-y divide-slate-100">
                {conversations.map(conv => (
                  <div
                    key={conv.id}
                    onClick={() => handleSwitchConversation(conv)}
                    className={`px-6 py-4 hover:bg-slate-50 cursor-pointer transition-colors ${
                      conv.id === currentConversationId ? 'bg-slate-50 border-l-4 border-slate-900' : ''
                    }`}
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-1.5">
                          <h4 className="text-sm font-medium text-slate-800 truncate">
                            {conv.name}
                          </h4>
                          {conv.status === 'completed' && (
                            <CheckCircle size={14} className="text-emerald-500 shrink-0" />
                          )}
                        </div>
                        <div className="flex items-center gap-2 text-xs text-slate-500 mb-2">
                          <span>{new Date(conv.updatedAt).toLocaleString('zh-CN', {
                            month: 'numeric',
                            day: 'numeric',
                            hour: '2-digit',
                            minute: '2-digit'
                          })}</span>
                          <span>·</span>
                          <span>{conv.messages.length} 条消息</span>
                        </div>
                        {conv.discoveryData.scenario && (
                          <p className="text-xs text-slate-400 line-clamp-2">
                            {conv.discoveryData.scenario}
                          </p>
                        )}
                      </div>
                      <button
                        onClick={(e) => handleDeleteConversation(conv.id, e)}
                        className="p-1.5 text-slate-400 hover:text-red-500 hover:bg-red-50 rounded transition-colors shrink-0"
                      >
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>

          {/* 抽屉底部 */}
          <div className="px-6 py-4 border-t border-slate-200 bg-slate-50">
            <button
              onClick={() => {
                startNewConversation()
                setShowHistory(false)
              }}
              className="w-full flex items-center justify-center gap-2 px-4 py-2.5 bg-slate-900 text-white rounded-lg hover:bg-slate-800 transition-colors text-sm font-medium"
            >
              <Plus size={16} />
              开始新对话
            </button>
          </div>
        </div>
      </div>

      <div className="bg-white rounded-2xl border border-slate-100 overflow-hidden">
        {/* Header */}
        <div className="bg-slate-900 px-6 py-5 text-white">
          <div className="flex items-center gap-2 mb-2">
            <Sparkles size={18} />
            <h1 className="text-lg font-bold">定制数字员工</h1>
          </div>
          <p className="text-sm text-slate-300">
            通过对话了解你的需求，为你生成专属的数字员工配置
          </p>
        </div>

        {/* Conversation */}
        <div className="h-[500px] overflow-y-auto p-6 space-y-4 bg-slate-50">
          {messages.map((msg, index) => (
            <div key={index} className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}>
              <div className={`max-w-[85%] ${msg.role === 'user' ? 'bg-slate-900 text-white' : 'bg-white border border-slate-200 text-slate-700'} rounded-2xl px-4 py-3 ${msg.isThinking ? 'opacity-70' : ''}`}>
                {msg.role === 'system' && (
                  <div className="flex items-center gap-2 mb-2">
                    <div className="w-6 h-6 rounded-full bg-slate-100 flex items-center justify-center">
                      {msg.isThinking ? (
                        <Loader2 size={12} className="text-slate-600 animate-spin" />
                      ) : (
                        <Sparkles size={12} className="text-slate-600" />
                      )}
                    </div>
                    <span className="text-xs font-medium text-slate-500">Discovery Bot</span>
                  </div>
                )}
                <div className="text-sm whitespace-pre-line leading-relaxed">{msg.content}</div>
                
                {/* Options */}
                {msg.options && index === messages.length - 1 && !msg.isThinking && (
                  <div className="mt-3 space-y-2">
                    {msg.options.map((option, i) => (
                      <button
                        key={i}
                        onClick={() => handleOptionClick(option)}
                        className="w-full text-left px-3 py-2 bg-slate-50 hover:bg-slate-100 border border-slate-200 hover:border-slate-300 rounded-lg text-sm text-slate-700 hover:text-slate-900 transition-all"
                      >
                        {option}
                      </button>
                    ))}
                  </div>
                )}
              </div>
            </div>
          ))}

          {/* Auto scroll anchor */}
          <div ref={messagesEndRef} />
        </div>

        {/* Input */}
        {round !== 'creating' && !messages[messages.length - 1]?.options && !messages[messages.length - 1]?.isThinking && (
          <div className="border-t border-slate-200 p-4 bg-white">
            <div className="flex gap-3">
              <input
                type="text"
                value={input}
                onChange={e => setInput(e.target.value)}
                onKeyDown={e => e.key === 'Enter' && handleSend()}
                placeholder="输入你的回答..."
                className="flex-1 px-4 py-2.5 border border-slate-200 rounded-lg text-sm outline-none focus:border-slate-300 focus:ring-1 focus:ring-slate-200 transition-all"
                autoFocus
              />
              <button
                onClick={handleSend}
                disabled={!input.trim()}
                className="px-5 py-2.5 bg-slate-900 text-white rounded-lg text-sm font-medium hover:bg-slate-800 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
              >
                <Send size={14} />
                发送
              </button>
            </div>
            <div className="mt-2 flex items-start gap-2">
              <AlertCircle size={12} className="text-slate-500 mt-0.5 shrink-0" />
              <div className="text-xs text-slate-500">
                提示：详细描述真实场景，包括具体的触发时机、处理步骤和痛点，这样我能更准确地帮你定义数字员工
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Info cards */}
      <div className="grid grid-cols-3 gap-4 mt-6">
        <div className="bg-white rounded-xl border border-slate-100 p-4">
          <div className="flex items-center gap-2 mb-2">
            <User size={16} className="text-slate-600" />
            <span className="text-sm font-medium text-slate-700">场景驱动</span>
          </div>
          <p className="text-xs text-slate-500">
            从真实案例出发，而非抽象需求，确保数字员工贴合实际工作
          </p>
        </div>
        <div className="bg-white rounded-xl border border-slate-100 p-4">
          <div className="flex items-center gap-2 mb-2">
            <CheckCircle size={16} className="text-emerald-500" />
            <span className="text-sm font-medium text-slate-700">四层收敛</span>
          </div>
          <p className="text-xs text-slate-500">
            按 NCrew 框架：数字员工、Ontology、Skills、CLI 逐层明确
          </p>
        </div>
        <div className="bg-white rounded-xl border border-slate-100 p-4">
          <div className="flex items-center gap-2 mb-2">
            <Sparkles size={16} className="text-amber-500" />
            <span className="text-sm font-medium text-slate-700">自动匹配</span>
          </div>
          <p className="text-xs text-slate-500">
            系统自动匹配已有 Skills 和 CLI，无需手动配置技术细节
          </p>
        </div>
      </div>
    </div>
  )
}
