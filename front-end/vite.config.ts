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
      // runtime-config.js 由后端生成（注入 OIDC / API 地址等配置）
      '/runtime-config.js': {
        target: 'http://localhost:5280',
        changeOrigin: true,
      },
      // 模板池请求转发到 BuildService（优先级高于下面的 /api 规则）
      '/api/store': {
        target: 'https://goodcrew-builder.ai4c.cn',
        changeOrigin: true,
      },
      '/api': {
        target: 'http://localhost:5280',
        changeOrigin: true,
      },
    },
  },
})
