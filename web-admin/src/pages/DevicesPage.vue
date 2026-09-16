<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  Camera,
  CircleCheck,
  Connection,
  Delete,
  Edit,
  Plus,
  Refresh,
  Search,
  VideoCamera,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { managementApi, type Channel, type Device, type DeviceInput, type DevicePlugin } from '../api'
import { dateTime } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'

const auth = useAuth()
const { busy, run, confirm } = useAction()

// 过滤参数
const statusFilter = ref<string>('')
const pluginFilter = ref<string>('')

const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  managementApi.devices,
  () => ({
    status: statusFilter.value || undefined,
    pluginId: pluginFilter.value || undefined
  }),
  ['device.changed']
)

const dialog = ref(false)
const editing = ref<number | null>(null)
const detail = ref<Device>()
const detailChannels = ref<Channel[]>([])
const detailChannelsLoading = ref(false)

const plugins = ref<DevicePlugin[]>([])
const selectedDevices = ref<Device[]>([])

const form = reactive<DeviceInput>({
  name: '',
  host: '',
  port: 8000,
  username: '',
  password: '',
  enabled: true,
  pluginId: 'hikvision'
})

// KPI 统计
const stats = computed(() => {
  const all = items.value as Device[]
  const online = all.filter(d => d.enabled && d.status === 'online').length
  const offline = all.filter(d => !d.enabled || d.status !== 'online').length
  const totalCh = all.reduce((sum, d) => sum + (d.channelCount || 0), 0)
  const onlineCh = all.reduce((sum, d) => sum + (d.onlineChannels || 0), 0)
  return { online, offline, totalCh, onlineCh }
})

async function loadPlugins() {
  try {
    plugins.value = await managementApi.plugins()
  } catch {}
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
  if (
    await run(async () => {
      if (!form.name.trim() || !form.host.trim() || !form.username.trim()) {
        throw new Error('请填写设备名称、IP/域名地址和登录账号')
      }
      if (!editing.value && !form.password) throw new Error('新增设备必须填写访问密码')
      if (!Number.isInteger(form.port) || form.port < 1 || form.port > 65535) {
        throw new Error('端口范围应为 1～65535')
      }
      await managementApi.saveDevice(editing.value, {
        ...form,
        name: form.name.trim(),
        host: form.host.trim(),
        username: form.username.trim(),
        password: form.password || undefined,
        pluginId: form.pluginId || 'hikvision'
      })
      await load()
    }, '设备已保存')
  ) {
    dialog.value = false
    form.password = ''
  }
}

async function test(device: Device) {
  await run(async () => {
    const result = (await managementApi.testDevice(device.id)) as { success?: boolean; message?: string } | undefined
    if (result?.success === false) throw new Error(result.message || '设备网络连接或认证失败')
    await load()
  }, `设备「${device.name}」连接检测成功`)
}

async function sync(device: Device) {
  await run(async () => {
    await managementApi.syncDevice(device.id)
    await load()
  }, `设备「${device.name}」通道同步完成`)
}

async function remove(device: Device) {
  await confirm(
    `确认删除设备“${device.name}”？\n删除后该设备所有接入通道将从平台解绑。`,
    async () => {
      await managementApi.deleteDevice(device.id)
      await load()
    },
    '设备已删除'
  )
}

async function showDetail(device: Device) {
  detail.value = device
  detailChannels.value = []
  detailChannelsLoading.value = true
  try {
    const [devInfo, chRes] = await Promise.all([
      managementApi.device(device.id),
      managementApi.channels({ deviceId: device.id, pageSize: 100 })
    ])
    detail.value = devInfo
    detailChannels.value = chRes.items || []
  } catch (e: any) {
    ElMessage.error(e.message || '获取设备明细失败')
  } finally {
    detailChannelsLoading.value = false
  }
}

// 批量操作
async function batchSync() {
  if (!selectedDevices.value.length) return
  await run(async () => {
    for (const d of selectedDevices.value) {
      await managementApi.syncDevice(d.id)
    }
    await load()
  }, `已完成 ${selectedDevices.value.length} 台设备的通道同步`)
}

async function batchTest() {
  if (!selectedDevices.value.length) return
  await run(async () => {
    let successCount = 0
    for (const d of selectedDevices.value) {
      try {
        const res = (await managementApi.testDevice(d.id)) as any
        if (res?.success !== false) successCount++
      } catch {}
    }
    await load()
    ElMessage.success(`批量检测完成：${successCount}/${selectedDevices.value.length} 台设备通信正常`)
  }, '')
}

function resetFilter() {
  search.value = ''
  statusFilter.value = ''
  pluginFilter.value = ''
  void load(true)
}

onMounted(() => {
  void loadPlugins()
})
</script>

<template>
  <div>
    <PageHeader title="设备管理" :count="total" description="管理接入平台的网络录像机 (NVR) 与摄像头 (IPC)，配置驱动插件与通道同步">
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      <el-button v-if="auth.can('device.manage')" type="primary" :icon="Plus" @click="edit()">添加设备</el-button>
    </PageHeader>

    <!-- 顶部 KPI 统计卡片 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><Camera /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">设备总数</div>
          <div class="kpi-value">{{ total }}</div>
          <div class="kpi-sub">已录入平台的视频源设备</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><CircleCheck /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">在线设备</div>
          <div class="kpi-value">{{ stats.online }}</div>
          <div class="kpi-sub">网络连接正常通信</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap red"><el-icon><Warning /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">离线 / 停用</div>
          <div class="kpi-value">{{ stats.offline }}</div>
          <div class="kpi-sub">网络异常或已禁用</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap purple"><el-icon><VideoCamera /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">在线通道占比</div>
          <div class="kpi-value">
            {{ stats.onlineCh }}<small>/ {{ stats.totalCh }}</small>
          </div>
          <div class="kpi-sub">当前页通道状态分布</div>
        </div>
      </div>
    </div>

    <!-- 高级筛选卡片 -->
    <div class="filter-card">
      <form class="filter-bar" @submit.prevent="load(true)">
        <el-input
          v-model="search"
          clearable
          :prefix-icon="Search"
          placeholder="设备名称、IP地址或序列号..."
          aria-label="搜索设备"
          @clear="load(true)"
        />

        <el-select v-model="statusFilter" clearable placeholder="全部设备状态" style="width: 150px;" @change="load(true)">
          <el-option label="在线" value="online" />
          <el-option label="离线" value="offline" />
          <el-option label="已停用" value="disabled" />
        </el-select>

        <el-select v-model="pluginFilter" clearable placeholder="全部驱动插件" style="width: 180px;" @change="load(true)">
          <el-option
            v-for="p in plugins"
            :key="p.id"
            :label="`${p.name} (${p.vendor})`"
            :value="p.id"
          />
        </el-select>

        <el-button type="primary" native-type="submit" :icon="Search">查询</el-button>
        <el-button @click="resetFilter">重置</el-button>

        <div v-if="auth.can('device.manage') && selectedDevices.length" class="filter-actions">
          <span class="muted" style="font-size: 12.5px;">已选择 {{ selectedDevices.length }} 项：</span>
          <el-button size="small" :icon="Refresh" :disabled="busy" @click="batchSync">批量同步通道</el-button>
          <el-button size="small" :icon="Connection" :disabled="busy" @click="batchTest">批量检测连接</el-button>
        </div>
      </form>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 14px;" />

    <!-- 设备数据表格卡片 -->
    <div class="table-card">
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        empty-text="未找到匹配的设备记录"
        @selection-change="selectedDevices = $event"
      >
        <el-table-column v-if="auth.can('device.manage')" type="selection" width="44" />

        <el-table-column label="设备名称" min-width="170">
          <template #default="{ row }">
            <el-button link type="primary" style="font-weight: 600;" @click="showDetail(row as Device)">
              {{ row.name }}
            </el-button>
          </template>
        </el-table-column>

        <el-table-column label="连接地址" min-width="170">
          <template #default="{ row }">
            <span class="hash">{{ row.host }}:{{ row.port }}</span>
          </template>
        </el-table-column>

        <el-table-column label="驱动插件" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">
            <el-tag size="small" type="info">{{ row.pluginName || '海康威视驱动' }}</el-tag>
          </template>
        </el-table-column>

        <el-table-column prop="model" label="设备型号" min-width="140" show-overflow-tooltip>
          <template #default="{ row }">
            {{ row.model || '—' }}
          </template>
        </el-table-column>

        <el-table-column label="设备状态" width="110">
          <template #default="{ row }">
            <StatusBadge :value="row.enabled ? row.status : 'disabled'" />
          </template>
        </el-table-column>

        <el-table-column label="在线通道" width="110">
          <template #default="{ row }">
            <span :style="{ color: row.onlineChannels > 0 ? 'var(--teal)' : 'var(--text-muted)', fontWeight: 600 }">
              {{ row.onlineChannels }}
            </span>
            <span class="muted"> / {{ row.channelCount }}</span>
          </template>
        </el-table-column>

        <el-table-column label="最近心跳" min-width="170">
          <template #default="{ row }">
            <span class="muted">{{ dateTime(row.lastSeenAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column v-if="auth.can('device.manage')" label="操作" width="220" fixed="right">
          <template #default="{ row }">
            <div class="table-tools">
              <el-tooltip content="测试与设备的网络及鉴权连通性">
                <el-button link type="primary" :icon="Connection" :disabled="busy" @click="test(row as Device)">
                  检测
                </el-button>
              </el-tooltip>
              <el-tooltip content="从设备拉取同步最新视频通道列表">
                <el-button link type="primary" :icon="Refresh" :disabled="busy" @click="sync(row as Device)">
                  同步
                </el-button>
              </el-tooltip>
              <el-button link type="primary" :icon="Edit" @click="edit(row as Device)">
                编辑
              </el-button>
              <el-button link type="danger" :icon="Delete" :disabled="busy" @click="remove(row as Device)">
                删除
              </el-button>
            </div>
          </template>
        </el-table-column>
      </el-table>

      <div class="pagination-bar">
        <el-pagination
          v-model:current-page="page"
          v-model:page-size="pageSize"
          :total="total"
          :page-sizes="[15, 30, 50, 100]"
          layout="total, sizes, prev, pager, next, jumper"
          @current-change="load()"
          @size-change="load(true)"
        />
      </div>
    </div>

    <!-- 添加 / 编辑设备弹窗 -->
    <el-dialog
      v-model="dialog"
      :title="editing ? '编辑监控设备' : '添加接入设备'"
      width="560px"
      destroy-on-close
      @closed="form.password = ''"
    >
      <el-form label-position="top" @submit.prevent="save">
        <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 0 16px;">
          <el-form-item label="设备名称" required>
            <el-input v-model="form.name" maxlength="100" placeholder="例如：一号车间主机" clearable />
          </el-form-item>

          <el-form-item label="设备驱动插件" required>
            <el-select v-model="form.pluginId" placeholder="选择驱动协议" style="width: 100%;">
              <el-option
                v-for="p in plugins"
                :key="p.id"
                :label="`${p.name} (${p.vendor})`"
                :value="p.id"
                :disabled="p.status === 'disabled'"
              />
            </el-select>
          </el-form-item>

          <el-form-item label="设备 IP / 域名" required>
            <el-input v-model="form.host" maxlength="253" placeholder="192.168.1.100" clearable />
          </el-form-item>

          <el-form-item label="SDK 服务端口" required>
            <el-input-number v-model="form.port" :min="1" :max="65535" :precision="0" style="width: 100%;" />
          </el-form-item>

          <el-form-item label="登录账号" required>
            <el-input v-model="form.username" autocomplete="off" maxlength="80" placeholder="admin" clearable />
          </el-form-item>

          <el-form-item :label="editing ? '新密码（留空保持不变）' : '设备密码'" :required="!editing">
            <el-input
              v-model="form.password"
              type="password"
              show-password
              autocomplete="new-password"
              maxlength="256"
              placeholder="请输入密码"
            />
          </el-form-item>
        </div>

        <el-form-item label="启用该设备">
          <el-switch v-model="form.enabled" active-text="启用连接" inactive-text="暂停接入" />
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="dialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="save">确认保存</el-button>
      </template>
    </el-dialog>

    <!-- 设备详情与内置通道抽屉 -->
    <el-drawer
      :model-value="!!detail"
      title="设备详情与通道明细"
      size="640px"
      @close="detail = undefined"
    >
      <div v-if="detail">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 16px;">
          <div>
            <h2 style="margin: 0; font-size: 16px;">{{ detail.name }}</h2>
            <span class="muted" style="font-size: 12px;">型号：{{ detail.model || '标准流媒体设备' }}</span>
          </div>
          <StatusBadge :value="detail.status" />
        </div>

        <dl class="data-list" style="margin-bottom: 24px;">
          <div><dt>驱动插件</dt><dd><el-tag size="small" type="info">{{ detail.pluginName || '默认驱动' }}</el-tag></dd></div>
          <div><dt>网络地址</dt><dd class="hash">{{ detail.host }}:{{ detail.port }}</dd></div>
          <div><dt>序列号</dt><dd class="hash">{{ detail.serialNumber || '—' }}</dd></div>
          <div><dt>在线通道</dt><dd>{{ detail.onlineChannels }} / {{ detail.channelCount }}</dd></div>
          <div><dt>最近在线心跳</dt><dd>{{ dateTime(detail.lastSeenAt) }}</dd></div>
        </dl>

        <!-- 关联通道列表 -->
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 12px;">
          <h3 style="margin: 0;">已同步通道明细 ({{ detailChannels.length }})</h3>
          <router-link :to="{ path: '/app/organization', query: { deviceId: detail.id } }">
            <el-button size="small" link type="primary">去组织架构分配通道 →</el-button>
          </router-link>
        </div>

        <el-table v-loading="detailChannelsLoading" :data="detailChannels" max-height="360" empty-text="该设备暂无同步通道">
          <el-table-column prop="deviceChannel" label="通道号" width="70" />
          <el-table-column prop="name" label="通道名称" min-width="130" show-overflow-tooltip>
            <template #default="{ row }">
              <strong>{{ row.alias || row.name }}</strong>
            </template>
          </el-table-column>
          <el-table-column label="状态" width="80">
            <template #default="{ row }">
              <StatusBadge :value="row.status" />
            </template>
          </el-table-column>
          <el-table-column prop="codec" label="编码" width="75" />
          <el-table-column label="快速预览" width="90" fixed="right">
            <template #default="{ row }">
              <router-link :to="{ path: '/app/live', query: { channelId: row.id } }">
                <el-button size="small" link type="primary" :icon="VideoPlay">播放</el-button>
              </router-link>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </el-drawer>
  </div>
</template>
