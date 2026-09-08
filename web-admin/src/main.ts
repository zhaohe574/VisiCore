import { createApp } from 'vue'
import 'element-plus/theme-chalk/base.css'
import 'element-plus/es/components/message/style/css'
import 'element-plus/es/components/message-box/style/css'
import { createPinia } from 'pinia'
import { router } from './router'
import App from './App.vue'
import './styles.css'

createApp(App).use(createPinia()).use(router).mount('#app')
