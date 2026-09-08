import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import Components from 'unplugin-vue-components/vite'
import { ElementPlusResolver } from 'unplugin-vue-components/resolvers'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '')
  const apiProxyTarget = env.VITE_API_PROXY_TARGET || 'https://10.37.200.74'
  const proxy = { target: apiProxyTarget, changeOrigin: true, secure: true }
  return {
    plugins: [vue(), Components({ resolvers: [ElementPlusResolver({ importStyle: 'css' })], dts: 'src/generated/components.d.ts' })],
    server: {
      host: '0.0.0.0',
      allowedHosts: ['terminal.local'],
      port: 4173,
      proxy: {
        '/api': proxy,
        '/hubs': { ...proxy, ws: true },
        '/media': proxy,
      },
    },
    build: {
      rollupOptions: {
        output: {
          manualChunks: { 'vue-vendor': ['vue', 'vue-router', 'pinia'] },
        },
      },
    },
  }
})
