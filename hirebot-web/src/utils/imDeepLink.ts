/**
 * 企业 IM Deep Link 工具
 * 支持飞书、钉钉、企业微信的群聊跳转
 */

export type IMPlatform = '飞书' | '钉钉' | '企业微信'

export interface IMGroupInfo {
  platform: IMPlatform
  groupId: string
  groupName: string
}

/**
 * 生成飞书群聊 Deep Link
 * 格式: feishu://open?type=chat&chatId={groupId}
 */
function generateFeishuLink(groupId: string): string {
  return `feishu://open?type=chat&chatId=${groupId}`
}

/**
 * 生成钉钉群聊 Deep Link
 * 格式: dingtalk://dingtalkclient/page/link?url=https://qr.dingtalk.com/{groupId}
 */
function generateDingTalkLink(groupId: string): string {
  return `dingtalk://dingtalkclient/page/link?url=https://qr.dingtalk.com/${groupId}`
}

/**
 * 生成企业微信群聊 Deep Link
 * 格式: wxwork://message/?chatid={groupId}
 */
function generateWeComLink(groupId: string): string {
  return `wxwork://message/?chatid=${groupId}`
}

/**
 * 根据平台生成对应的 Deep Link
 */
export function generateIMDeepLink(info: IMGroupInfo): string {
  switch (info.platform) {
    case '飞书':
      return generateFeishuLink(info.groupId)
    case '钉钉':
      return generateDingTalkLink(info.groupId)
    case '企业微信':
      return generateWeComLink(info.groupId)
    default:
      console.warn(`未知的 IM 平台: ${info.platform}`)
      return '#'
  }
}

/**
 * 打开 IM 群聊
 * 如果 Deep Link 不可用，则在新标签页打开 Web 版本
 */
export function openIMGroup(info: IMGroupInfo): void {
  const deepLink = generateIMDeepLink(info)
  
  // 尝试打开 Deep Link
  window.location.href = deepLink
  
  // 如果 Deep Link 失败，2秒后提供 Web 版本备选
  setTimeout(() => {
    const webFallback = getWebFallbackURL(info)
    if (webFallback && !document.hidden) {
      const shouldOpenWeb = confirm(
        `未检测到 ${info.platform} 客户端，是否在浏览器中打开？`
      )
      if (shouldOpenWeb) {
        window.open(webFallback, '_blank')
      }
    }
  }, 2000)
}

/**
 * 获取 Web 版本的备选 URL
 */
function getWebFallbackURL(info: IMGroupInfo): string | null {
  switch (info.platform) {
    case '飞书':
      return `https://open.feishu.cn/open-apis/im/v1/chats/${info.groupId}`
    case '钉钉':
      return `https://aflow.dingtalk.com/dingtalk/mobile/homepage.htm`
    case '企业微信':
      return `https://work.weixin.qq.com/`
    default:
      return null
  }
}

/**
 * 检测是否安装了对应的 IM 客户端
 * 注意：这是一个简化的检测，实际生产环境可能需要更复杂的逻辑
 */
export function isIMClientInstalled(platform: IMPlatform): Promise<boolean> {
  return new Promise((resolve) => {
    const iframe = document.createElement('iframe')
    iframe.style.display = 'none'
    
    const deepLink = generateIMDeepLink({
      platform,
      groupId: 'test',
      groupName: 'test'
    })
    
    iframe.src = deepLink
    document.body.appendChild(iframe)
    
    // 简单的超时检测
    setTimeout(() => {
      document.body.removeChild(iframe)
      resolve(false)
    }, 1000)
  })
}

/**
 * 获取 IM 平台的图标
 */
export function getIMPlatformIcon(platform: IMPlatform): string {
  switch (platform) {
    case '飞书':
      return '🚀'
    case '钉钉':
      return '💼'
    case '企业微信':
      return '💬'
    default:
      return '📱'
  }
}

/**
 * 获取 IM 平台的颜色主题
 */
export function getIMPlatformColor(platform: IMPlatform): string {
  switch (platform) {
    case '飞书':
      return 'text-blue-600 bg-blue-50'
    case '钉钉':
      return 'text-cyan-600 bg-cyan-50'
    case '企业微信':
      return 'text-green-600 bg-green-50'
    default:
      return 'text-slate-600 bg-slate-50'
  }
}
