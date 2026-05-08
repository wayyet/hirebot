import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import path from 'path'

function runtimeConfigFallback() {
  return {
    name: 'runtime-config-fallback',
    configureServer(server: any) {
      server.middlewares.use('/runtime-config.js', (_req: any, res: any, next: any) => {
        if (_req?.url?.startsWith('/runtime-config.js')) {
          res.statusCode = 200
          res.setHeader('Content-Type', 'application/javascript; charset=utf-8')
          res.end('window.__AUTH_CONFIG__ = window.__AUTH_CONFIG__ || {};')
          return
        }

        next()
      })
    },
  }
}

export default defineConfig({
  plugins: [react(), tailwindcss(), runtimeConfigFallback()],
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
      '/api': {
        target: 'http://localhost:5280',
        changeOrigin: true,
      },
    },
  },
})
