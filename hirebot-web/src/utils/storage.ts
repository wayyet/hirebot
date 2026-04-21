import type { DigitalEmployee, Template } from '../mock/data'

const STORAGE_KEY = 'ncrew_digital_employees'

// ===== 用户自建模板管理 =====

const USER_TEMPLATES_KEY = 'ncrew_user_templates'

export interface UserTemplate extends Template {
  isUserCreated: true
  publishedAt: string
  sourceConversationId?: string
  creatorTeam?: string
}

export function loadUserTemplates(): UserTemplate[] {
  try {
    const data = localStorage.getItem(USER_TEMPLATES_KEY)
    return data ? JSON.parse(data) : []
  } catch {
    return []
  }
}

export function saveUserTemplate(template: UserTemplate): void {
  const existing = loadUserTemplates()
  const idx = existing.findIndex(t => t.id === template.id)
  if (idx !== -1) {
    existing[idx] = template
  } else {
    existing.push(template)
  }
  localStorage.setItem(USER_TEMPLATES_KEY, JSON.stringify(existing))
}

export function deleteUserTemplate(id: string): void {
  const filtered = loadUserTemplates().filter(t => t.id !== id)
  localStorage.setItem(USER_TEMPLATES_KEY, JSON.stringify(filtered))
}

// 从 localStorage 读取用户创建的数字员工
export function loadUserEmployees(): DigitalEmployee[] {
  try {
    const data = localStorage.getItem(STORAGE_KEY)
    return data ? JSON.parse(data) : []
  } catch (error) {
    console.error('Failed to load user employees:', error)
    return []
  }
}

// 保存用户创建的数字员工到 localStorage
export function saveUserEmployees(employees: DigitalEmployee[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(employees))
  } catch (error) {
    console.error('Failed to save user employees:', error)
  }
}

// 添加一个新的数字员工
export function addUserEmployee(employee: DigitalEmployee): void {
  const employees = loadUserEmployees()
  employees.push(employee)
  saveUserEmployees(employees)
}

// 更新数字员工信息
// 注意：这个函数会更新用户创建的员工，对于 mock 数据中的员工，需要将其复制到 localStorage 后再更新
export function updateUserEmployee(id: string, updates: Partial<DigitalEmployee>): void {
  const employees = loadUserEmployees()
  const index = employees.findIndex(e => e.id === id)
  if (index !== -1) {
    employees[index] = { ...employees[index], ...updates }
    saveUserEmployees(employees)
  } else {
    // 如果在用户数据中找不到，说明是 mock 数据，需要先复制到 localStorage
    // 这个逻辑由调用方处理，这里只是标记
    console.warn(`Employee ${id} not found in user data. It might be a mock employee.`)
  }
}

// 更新任意数字员工（包括 mock 数据中的员工）
export function updateAnyEmployee(id: string, mockEmployees: DigitalEmployee[], updates: Partial<DigitalEmployee>): void {
  console.log('updateAnyEmployee 被调用')
  console.log('员工 ID:', id)
  console.log('更新内容:', updates)
  
  const userEmployees = loadUserEmployees()
  console.log('当前用户员工数量:', userEmployees.length)
  
  const userIndex = userEmployees.findIndex(e => e.id === id)
  
  if (userIndex !== -1) {
    // 如果是用户创建的员工，直接更新
    console.log('找到用户员工，索引:', userIndex)
    userEmployees[userIndex] = { ...userEmployees[userIndex], ...updates }
    saveUserEmployees(userEmployees)
    console.log('用户员工更新完成')
  } else {
    // 如果是 mock 员工，先复制到 localStorage，然后更新
    console.log('未找到用户员工，查找 mock 员工')
    const mockEmployee = mockEmployees.find(e => e.id === id)
    if (mockEmployee) {
      console.log('找到 mock 员工:', mockEmployee.nickname)
      const updatedEmployee = { ...mockEmployee, ...updates }
      userEmployees.push(updatedEmployee)
      saveUserEmployees(userEmployees)
      console.log('mock 员工已复制并更新到 localStorage')
    } else {
      console.error('未找到员工:', id)
    }
  }
}

// 删除数字员工
export function deleteUserEmployee(id: string): void {
  const employees = loadUserEmployees()
  const filtered = employees.filter(e => e.id !== id)
  saveUserEmployees(filtered)
}

// 生成唯一 ID
export function generateEmployeeId(): string {
  return `e_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`
}

// ===== 对话历史记录管理 =====

const CONVERSATIONS_KEY = 'ncrew_discovery_conversations'

export interface DiscoveryConversation {
  id: string
  name: string
  createdAt: string
  updatedAt: string
  round: string
  messages: Array<{
    role: 'system' | 'user'
    content: string
    options?: string[]
    isThinking?: boolean
  }>
  discoveryData: {
    scenario: string
    beneficiary: string
    realCase: {
      when: string
      who: string
      steps: string
      pain: string
      consequence: string
    }
    employee: {
      name: string
      roleMetaphor: string
      mission: string
      keyJudgment: string
      autonomy: string
      deliverable: string
      criticalFailure: string
    }
    ontology: {
      entities: string[]
      actions: string[]
      resources: string[]
      constraints: string[]
    }
    skills: Array<{
      name: string
      purpose: string
      trigger: string
      input: string
      output: string
      autonomy: string
    }>
    clis: Array<{
      skill: string
      system: string
      action: string
      interface: string
      auth: string
    }>
  }
  status: 'in_progress' | 'completed' | 'abandoned'
}

// 加载所有对话历史
export function loadConversations(): DiscoveryConversation[] {
  try {
    const data = localStorage.getItem(CONVERSATIONS_KEY)
    return data ? JSON.parse(data) : []
  } catch (error) {
    console.error('Failed to load conversations:', error)
    return []
  }
}

// 保存所有对话历史
export function saveConversations(conversations: DiscoveryConversation[]): void {
  try {
    localStorage.setItem(CONVERSATIONS_KEY, JSON.stringify(conversations))
  } catch (error) {
    console.error('Failed to save conversations:', error)
  }
}

// 创建新对话
export function createConversation(): DiscoveryConversation {
  const now = new Date().toISOString()
  return {
    id: `conv_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`,
    name: '新对话',
    createdAt: now,
    updatedAt: now,
    round: 'intro',
    messages: [],
    discoveryData: {
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
        entities: [],
        actions: [],
        resources: [],
        constraints: [],
      },
      skills: [],
      clis: [],
    },
    status: 'in_progress',
  }
}

// 保存对话
export function saveConversation(conversation: DiscoveryConversation): void {
  const conversations = loadConversations()
  const index = conversations.findIndex(c => c.id === conversation.id)
  
  conversation.updatedAt = new Date().toISOString()
  
  if (index !== -1) {
    conversations[index] = conversation
  } else {
    conversations.push(conversation)
  }
  
  saveConversations(conversations)
}

// 删除对话
export function deleteConversation(id: string): void {
  const conversations = loadConversations()
  const filtered = conversations.filter(c => c.id !== id)
  saveConversations(filtered)
}

// 获取单个对话
export function getConversation(id: string): DiscoveryConversation | null {
  const conversations = loadConversations()
  return conversations.find(c => c.id === id) || null
}
