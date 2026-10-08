import { createApp } from 'vue'
import 'element-plus/theme-chalk/base.css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import { createPinia } from 'pinia'
import { router } from './router'
import App from './App.vue'
import './styles.css'
import { isMobileUserAgent } from './lib/platform'

function renderDesktopOnlyNotice() {
  const mount = document.querySelector('#app')
  if (!mount) return
  mount.replaceChildren()
  const notice = document.createElement('main')
  notice.className = 'desktop-only-page'
  notice.innerHTML = '<h1>请使用 PC 端访问</h1><p>VisiCore（视枢）管理平台已停止提供移动端页面，请在内网电脑上打开此地址。</p>'
  mount.append(notice)
}

if (isMobileUserAgent()) renderDesktopOnlyNotice()
else createApp(App).use(createPinia()).use(router).mount('#app')
