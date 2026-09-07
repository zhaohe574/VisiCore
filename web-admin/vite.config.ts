import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '')
  const apiProxyTarget = env.VITE_API_PROXY_TARGET || 'https://10.37.200.74'
  const proxy = { target: apiProxyTarget, changeOrigin: true, secure: false }
  return {
    plugins: [vue()],
    server: {
      host: '0.0.0.0',
      allowedHosts: ['terminal.local'],
      port: 4173,
      proxy: {
        '/api': proxy,
        '/internal': proxy,
        '/hubs': { ...proxy, ws: true },
        '/media': proxy,
      },
    },
  }
})
