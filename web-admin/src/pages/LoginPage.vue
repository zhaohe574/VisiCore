<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, Lock, Right, User } from '@element-plus/icons-vue'
import { useAuth } from '../stores/auth'
import { errorMessage, safeRedirect } from '../lib/format'

const auth = useAuth()
const router = useRouter()
const route = useRoute()

const username = ref('')
const password = ref('')
const busy = ref(false)
const error = ref('')

async function submit() {
  if (!username.value.trim() || !password.value) {
    error.value = '请输入登录账号与访问密码'
    return
  }
  busy.value = true
  error.value = ''
  try {
    await auth.login(username.value.trim(), password.value)
    password.value = ''
    await router.replace(safeRedirect(route.query.redirect))
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <main class="login-page">
    <router-link class="login-back" to="/">
      <el-icon><ArrowLeft /></el-icon>
      <span>返回客户端下载门户</span>
    </router-link>

    <div class="login-card">
      <img class="login-brand-icon" src="/visicore.svg" alt="VisiCore 标识" />
      <h1>VisiCore 视枢</h1>
      <p class="login-subtitle">工业级智能视频值守与管理工作区</p>

      <form class="login-form" @submit.prevent="submit">
        <el-alert
          v-if="error || auth.error"
          :title="error || auth.error"
          type="error"
          :closable="false"
          show-icon
          style="margin-bottom: 16px;"
        />

        <label class="form-label" for="username">管理员账号</label>
        <el-input
          id="username"
          v-model="username"
          :prefix-icon="User"
          placeholder="请输入用户名"
          autocomplete="username"
          maxlength="80"
          autofocus
          size="large"
        />

        <label class="form-label" for="password">访问密码</label>
        <el-input
          id="password"
          v-model="password"
          :prefix-icon="Lock"
          placeholder="请输入登录密码"
          type="password"
          show-password
          autocomplete="current-password"
          maxlength="256"
          size="large"
        />

        <el-button
          class="login-submit-btn"
          type="primary"
          size="large"
          native-type="submit"
          :loading="busy"
        >
          <span>进入管理工作区</span>
          <el-icon style="margin-left: 6px;"><Right /></el-icon>
        </el-button>
      </form>

      <footer>
        <span>VisiCore（视枢）企业视频监控平台 · v2.1.0</span>
      </footer>
    </div>
  </main>
</template>
