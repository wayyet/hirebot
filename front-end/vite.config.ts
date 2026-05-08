import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@eval': path.resolve(__dirname, './src/eval'),
    },
  },
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: false,
    proxy: {
      // 开发模式将 /api/ 和 /runtime-config.js 转发到后端
      // 后端未运行时 runtime-config.js 会 404， onerror 将回退为空配置
      '/api': {
        target: 'http://localhost:5280',
        changeOrigin: true,
      },
      '/runtime-config.js': {
        target: 'http://localhost:5280',
        changeOrigin: true,
      },
    },
  },
})
