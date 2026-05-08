import { renderToStaticMarkup } from 'react-dom/server'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { UxOverlayProvider } from '@/app/context/UxOverlayContext'

import Layout from './Layout'

function createStorageMock(): Storage {
  const store = new Map<string, string>()

  return {
    get length() {
      return store.size
    },
    clear() {
      store.clear()
    },
    getItem(key: string) {
      return store.get(key) ?? null
    },
    key(index: number) {
      return Array.from(store.keys())[index] ?? null
    },
    removeItem(key: string) {
      store.delete(key)
    },
    setItem(key: string, value: string) {
      store.set(key, value)
    },
  }
}

describe('Layout', () => {
  beforeEach(() => {
    vi.stubGlobal('localStorage', createStorageMock())
    vi.stubGlobal('sessionStorage', createStorageMock())
  })

  it('renders a global logout action inside the top navigation', () => {
    const html = renderToStaticMarkup(
      <MemoryRouter initialEntries={['/department-employees']}>
        <UxOverlayProvider>
          <Layout>
            <div>content</div>
          </Layout>
        </UxOverlayProvider>
      </MemoryRouter>,
    )

    expect(html).toContain('退出登录')
  })
})
