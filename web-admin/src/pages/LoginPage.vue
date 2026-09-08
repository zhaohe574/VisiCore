<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ArrowLeft, Lock, User } from '@element-plus/icons-vue'
import { useAuth } from '../stores/auth'
import { errorMessage, safeRedirect } from '../lib/format'
const auth = useAuth(), router = useRouter(), route = useRoute()
const username = ref(''), password = ref(''), busy = ref(false), error = ref('')
async function submit() {
  if (!username.value.trim() || !password.value) { error.value = '请输入账号和密码'; return }
  busy.value = true; error.value = ''
  try { await auth.login(username.value.trim(), password.value); password.value = ''; await router.replace(safeRedirect(route.query.redirect)) }
  catch (e) { error.value = errorMessage(e) } finally { busy.value = false }
}
</script>
<template><main class="login-page"><router-link class="login-back" to="/"><el-icon><ArrowLeft /></el-icon>客户端下载</router-link><div class="login-content"><img class="login-brand" src="/visicore.svg" alt="VisiCore（视枢）标识" /><h1>VisiCore（视枢）</h1><p class="login-subtitle">管理工作区</p><form class="login-form" @submit.prevent="submit"><el-alert v-if="error || auth.error" :title="error || auth.error" type="error" :closable="false" show-icon /><label for="username">账号</label><el-input id="username" v-model="username" :prefix-icon="User" autocomplete="username" maxlength="80" autofocus size="large" /><label for="password">密码</label><el-input id="password" v-model="password" :prefix-icon="Lock" type="password" show-password autocomplete="current-password" maxlength="256" size="large" /><el-button class="login-submit" type="primary" size="large" native-type="submit" :loading="busy">登录</el-button></form><small>VisiCore（视枢） · 2.0.0</small></div></main></template>
