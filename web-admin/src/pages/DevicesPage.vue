<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { Connection, Delete, Edit, Plus, Refresh, Search } from '@element-plus/icons-vue'
import { managementApi, type Device, type DeviceInput, type DevicePlugin } from '../api'
import { dateTime } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'

const auth = useAuth(), { busy, run, confirm } = useAction()
const { items, total, page, pageSize, search, loading, error, load } = usePaged(managementApi.devices, undefined, ['device.changed'])
const dialog = ref(false), editing = ref<number | null>(null), detail = ref<Device>()
const plugins = ref<DevicePlugin[]>([])
const form = reactive<DeviceInput>({ name: '', host: '', port: 8000, username: '', password: '', enabled: true, pluginId: 'hikvision' })

async function loadPlugins() {
  try { plugins.value = await managementApi.plugins() } catch {}
}

function edit(device?: Device) {
  editing.value = device?.id || null
  Object.assign(form, {
    name: device?.name || '',
    host: device?.host || '',
    port: device?.port || 8000,
    username: device?.username || '',
    password: '',
    enabled: device?.enabled ?? true,
    pluginId: device?.pluginId || 'hikvision'
  })
  void loadPlugins()
  dialog.value = true
}

async function save() {
  if (await run(async () => {
    if (!form.name.trim() || !form.host.trim() || !form.username.trim()) throw new Error('请填写设备名称、地址和登录账号')
    if (!editing.value && !form.password) throw new Error('新增设备必须填写密码')
    if (!Number.isInteger(form.port) || form.port < 1 || form.port > 65535) throw new Error('端口范围为 1～65535')
    await managementApi.saveDevice(editing.value, {
      ...form,
      name: form.name.trim(),
      host: form.host.trim(),
      username: form.username.trim(),
      password: form.password || undefined,
      pluginId: form.pluginId || 'hikvision'
    })
    await load()
  }, '设备已保存')) { dialog.value = false; form.password = '' }
}

async function test(device: Device) { await run(async () => { const result = await managementApi.testDevice(device.id) as { success?: boolean; message?: string } | undefined; if (result?.success === false) throw new Error(result.message || '设备连接失败'); await load() }, '设备连接检测完成') }
async function sync(device: Device) { await run(async () => { await managementApi.syncDevice(device.id); await load() }, '通道同步完成') }
async function remove(device: Device) { await confirm(`删除设备“${device.name}”？该设备的通道将无法继续访问。`, async () => { await managementApi.deleteDevice(device.id); await load() }, '设备已删除') }
async function show(device: Device) { await run(async () => { detail.value = await managementApi.device(device.id) }, '') }

onMounted(() => {
  void loadPlugins()
})
</script>
<template>
  <div>
    <PageHeader title="设备管理" :count="total">
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      <el-button v-if="auth.can('device.manage')" type="primary" :icon="Plus" @click="edit()">添加设备</el-button>
    </PageHeader>
    <form class="filter-bar" @submit.prevent="load(true)">
      <el-input v-model="search" clearable :prefix-icon="Search" placeholder="设备名称、地址或序列号" aria-label="搜索设备" @clear="load(true)" />
      <el-button native-type="submit" :icon="Search">查询</el-button>
    </form>
    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon />
    <el-table v-loading="loading" :data="items" row-key="id" empty-text="暂无设备">
      <el-table-column label="设备名称" min-width="160">
        <template #default="{ row }">
          <el-button link type="primary" @click="show(row as Device)">{{ row.name }}</el-button>
        </template>
      </el-table-column>
      <el-table-column label="连接地址" min-width="160">
        <template #default="{ row }">{{ row.host }}:{{ row.port }}</template>
      </el-table-column>
      <el-table-column label="驱动插件" min-width="150" show-overflow-tooltip>
        <template #default="{ row }">
          <el-tag size="small" type="info">{{ row.pluginName || '海康威视驱动' }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="model" label="型号" min-width="150" show-overflow-tooltip />
      <el-table-column label="状态" width="110">
        <template #default="{ row }"><StatusBadge :value="row.enabled ? row.status : 'disabled'" /></template>
      </el-table-column>
      <el-table-column label="在线通道" width="100">
        <template #default="{ row }">{{ row.onlineChannels }} / {{ row.channelCount }}</template>
      </el-table-column>
      <el-table-column label="最近在线" min-width="170">
        <template #default="{ row }">{{ dateTime(row.lastSeenAt) }}</template>
      </el-table-column>
      <el-table-column v-if="auth.can('device.manage')" label="操作" width="202" fixed="right">
        <template #default="{ row }">
          <div class="table-tools">
            <el-tooltip content="检测连接"><el-button text :icon="Connection" :disabled="busy" aria-label="检测连接" @click="test(row as Device)" /></el-tooltip>
            <el-tooltip content="同步通道"><el-button text :icon="Refresh" :disabled="busy" aria-label="同步通道" @click="sync(row as Device)" /></el-tooltip>
            <el-tooltip content="编辑设备"><el-button text :icon="Edit" aria-label="编辑设备" @click="edit(row as Device)" /></el-tooltip>
            <el-tooltip content="删除设备"><el-button text type="danger" :icon="Delete" :disabled="busy" aria-label="删除设备" @click="remove(row as Device)" /></el-tooltip>
          </div>
        </template>
      </el-table-column>
    </el-table>
    <div class="pagination">
      <el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[20,50,100]" layout="total, sizes, prev, pager, next" @current-change="load()" @size-change="load(true)" />
    </div>
    <el-dialog v-model="dialog" :title="editing ? '编辑设备' : '添加设备'" width="520px" destroy-on-close @closed="form.password = ''">
      <el-form label-position="top" @submit.prevent="save">
        <div class="form-grid">
          <el-form-item label="设备名称" required><el-input v-model="form.name" maxlength="100" /></el-form-item>
          <el-form-item label="设备驱动插件" required>
            <el-select v-model="form.pluginId" placeholder="请选择设备驱动插件" style="width: 100%">
              <el-option
                v-for="p in plugins"
                :key="p.id"
                :label="`${p.name} (${p.vendor})`"
                :value="p.id"
                :disabled="p.status === 'disabled'"
              />
            </el-select>
          </el-form-item>
          <el-form-item label="设备地址" required><el-input v-model="form.host" maxlength="253" /></el-form-item>
          <el-form-item label="SDK 端口" required><el-input-number v-model="form.port" :min="1" :max="65535" :precision="0" style="width: 100%" /></el-form-item>
          <el-form-item label="登录账号" required><el-input v-model="form.username" autocomplete="off" maxlength="80" /></el-form-item>
          <el-form-item :label="editing ? '新密码（留空保留原密码）' : '设备密码'" :required="!editing"><el-input v-model="form.password" type="password" show-password autocomplete="new-password" maxlength="256" /></el-form-item>
          <el-form-item label="启用设备"><el-switch v-model="form.enabled" /></el-form-item>
        </div>
      </el-form>
      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="save">保存</el-button>
      </template>
    </el-dialog>
    <el-drawer :model-value="!!detail" title="设备详情" size="480px" @close="detail = undefined">
      <dl v-if="detail" class="data-list">
        <div><dt>名称</dt><dd>{{ detail.name }}</dd></div>
        <div><dt>驱动插件</dt><dd><el-tag size="small" type="info">{{ detail.pluginName || '海康威视网络设备驱动' }}</el-tag></dd></div>
        <div><dt>地址</dt><dd>{{ detail.host }}:{{ detail.port }}</dd></div>
        <div><dt>型号</dt><dd>{{ detail.model || '—' }}</dd></div>
        <div><dt>序列号</dt><dd>{{ detail.serialNumber || '—' }}</dd></div>
        <div><dt>在线通道</dt><dd>{{ detail.onlineChannels }} / {{ detail.channelCount }}</dd></div>
        <div><dt>状态</dt><dd><StatusBadge :value="detail.status" /></dd></div>
      </dl>
      <router-link v-if="detail" :to="{ path: '/app/organization', query: { deviceId: detail.id } }">
        <el-button>查看通道</el-button>
      </router-link>
    </el-drawer>
  </div>
</template>
