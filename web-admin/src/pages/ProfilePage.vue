<script setup lang="ts">
import { reactive } from 'vue'
import { Check, Lock } from '@element-plus/icons-vue'
import { authApi } from '../api'
import { useAuth } from '../stores/auth'
import { useAction } from '../composables/useAction'
import PageHeader from '../components/PageHeader.vue'
const auth = useAuth(), { busy, run } = useAction()
const profile = reactive({ displayName: auth.user?.displayName || '', phone: auth.user?.phone || '' }), password = reactive({ current: '', next: '', confirm: '' })
async function saveProfile() { await run(async () => { await authApi.profile(profile.displayName.trim(), profile.phone.trim()); await auth.loadUser() }, '个人资料已保存') }
async function savePassword() { if (await run(async () => { if (!password.current || !password.next) throw new Error('请填写当前密码和新密码'); if (password.next !== password.confirm) throw new Error('两次输入的新密码不一致'); if (password.current === password.next) throw new Error('新密码不能与当前密码相同'); await authApi.password(password.current, password.next) }, '密码已更新')) Object.assign(password, { current: '', next: '', confirm: '' }) }
</script>
<template><div><PageHeader title="个人账号" /><div class="profile-columns"><section class="page-section"><h2>个人资料</h2><el-form label-position="top" @submit.prevent="saveProfile"><el-form-item label="账号"><el-input :model-value="auth.user?.username" disabled /></el-form-item><el-form-item label="姓名"><el-input v-model="profile.displayName" maxlength="80" autocomplete="name" /></el-form-item><el-form-item label="手机号码"><el-input v-model="profile.phone" maxlength="30" autocomplete="tel" /></el-form-item><el-button type="primary" :icon="Check" :loading="busy" @click="saveProfile">保存资料</el-button></el-form></section><section class="page-section"><h2>修改密码</h2><el-form label-position="top" @submit.prevent="savePassword"><el-form-item label="当前密码" required><el-input v-model="password.current" type="password" show-password autocomplete="current-password" /></el-form-item><el-form-item label="新密码" required><el-input v-model="password.next" type="password" show-password autocomplete="new-password" maxlength="256" /></el-form-item><el-form-item label="确认新密码" required><el-input v-model="password.confirm" type="password" show-password autocomplete="new-password" maxlength="256" /></el-form-item><el-button :icon="Lock" :loading="busy" @click="savePassword">更新密码</el-button></el-form></section></div></div></template>
