<script setup lang="ts">
import { reactive } from 'vue'
import { Check, Lock, User } from '@element-plus/icons-vue'
import { authApi } from '../api'
import { useAuth } from '../stores/auth'
import { useAction } from '../composables/useAction'
import PageHeader from '../components/PageHeader.vue'

const auth = useAuth()
const { busy, run } = useAction()

const profile = reactive({
  displayName: auth.user?.displayName || '',
  phone: auth.user?.phone || ''
})

const password = reactive({
  current: '',
  next: '',
  confirm: ''
})

async function saveProfile() {
  await run(async () => {
    await authApi.profile(profile.displayName.trim(), profile.phone.trim())
    await auth.loadUser()
  }, '个人基本资料已保存')
}

async function savePassword() {
  if (
    await run(async () => {
      if (!password.current || !password.next) throw new Error('请填写当前密码与新密码')
      if (password.next !== password.confirm) throw new Error('两次输入的新密码不一致')
      if (password.current === password.next) throw new Error('新密码不能与原旧密码相同')
      await authApi.password(password.current, password.next)
    }, '登录密码已成功更新，下次请使用新密码登录')
  ) {
    Object.assign(password, { current: '', next: '', confirm: '' })
  }
}
</script>

<template>
  <div>
    <PageHeader title="个人账号" description="查看当前登录凭据基本信息、更新姓名与联系方式、定期修改安全访问密码" />

    <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(380px, 1fr)); gap: 20px; max-width: 1080px;">
      <!-- 个人信息卡片 -->
      <section class="filter-card" style="padding: 24px;">
        <div style="display: flex; align-items: center; gap: 10px; margin-bottom: 20px;">
          <el-icon class="blue" style="font-size: 20px;"><User /></el-icon>
          <h2 style="margin: 0; font-size: 16px;">个人资料</h2>
        </div>

        <el-form label-position="top" @submit.prevent="saveProfile">
          <el-form-item label="登录账号">
            <el-input :model-value="auth.user?.username" disabled />
          </el-form-item>

          <el-form-item label="真实姓名">
            <el-input v-model="profile.displayName" maxlength="80" autocomplete="name" placeholder="请输入姓名" clearable />
          </el-form-item>

          <el-form-item label="手机号码">
            <el-input v-model="profile.phone" maxlength="30" autocomplete="tel" placeholder="请输入联系电话" clearable />
          </el-form-item>

          <div style="margin-top: 24px;">
            <el-button type="primary" :icon="Check" :loading="busy" @click="saveProfile">
              保存资料
            </el-button>
          </div>
        </el-form>
      </section>

      <!-- 修改密码卡片 -->
      <section class="filter-card" style="padding: 24px;">
        <div style="display: flex; align-items: center; gap: 10px; margin-bottom: 20px;">
          <el-icon class="amber" style="font-size: 20px;"><Lock /></el-icon>
          <h2 style="margin: 0; font-size: 16px;">安全密码修改</h2>
        </div>

        <el-form label-position="top" @submit.prevent="savePassword">
          <el-form-item label="原当前密码" required>
            <el-input
              v-model="password.current"
              type="password"
              show-password
              autocomplete="current-password"
              placeholder="请输入当前正在使用的密码"
            />
          </el-form-item>

          <el-form-item label="设置新密码" required>
            <el-input
              v-model="password.next"
              type="password"
              show-password
              autocomplete="new-password"
              maxlength="256"
              placeholder="建议包含大小写字母、数字及特殊字符"
            />
          </el-form-item>

          <el-form-item label="再次确认新密码" required>
            <el-input
              v-model="password.confirm"
              type="password"
              show-password
              autocomplete="new-password"
              maxlength="256"
              placeholder="请重复输入新密码"
            />
          </el-form-item>

          <div style="margin-top: 24px;">
            <el-button type="warning" :icon="Lock" :loading="busy" @click="savePassword">
              更新安全密码
            </el-button>
          </div>
        </el-form>
      </section>
    </div>
  </div>
</template>
