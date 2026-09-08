<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Connection, Cpu, InfoFilled, Refresh } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { managementApi, type DevicePlugin } from '../api'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'

const auth = useAuth(), { busy, run, confirm } = useAction()
const plugins = ref<DevicePlugin[]>([])
const loading = ref(false)

const capabilityNames: Record<string, string> = {
  live: '实时预览',
  playback: '录像回放',
  recordings: '录像检索',
  ptz: '云台控制',
  alarms: '报警上报',
  presets: '预置位'
}

async function load() {
  loading.value = true
  try {
    plugins.value = await managementApi.plugins()
  } catch (e: any) {
    ElMessage.error(e.message || '加载驱动插件失败')
  } finally {
    loading.value = false
  }
}

async function toggleStatus(plugin: DevicePlugin) {
  const nextStatus = plugin.status === 'active' ? 'disabled' : 'active'
  const prompt = nextStatus === 'disabled'
    ? `停用插件“${plugin.name}”？停用后使用该驱动的 ${plugin.deviceCount} 台设备将无法进行通道同步、视频拉流与控制。`
    : `启用插件“${plugin.name}”？`

  await confirm(prompt, async () => {
    await managementApi.updatePluginStatus(plugin.id, nextStatus)
    await load()
  }, nextStatus === 'active' ? '驱动插件已启用' : '驱动插件已停用')
}

async function checkHealth(plugin: DevicePlugin) {
  await run(async () => {
    const res = await managementApi.pluginHealth(plugin.id) as { status?: string; service?: string; version?: string } | undefined
    if (res?.status === 'ok') {
      ElMessage.success(`驱动插件 [${plugin.name}] 运行正常（版本：${res.version || plugin.version}）`)
    } else {
      ElMessage.warning(`驱动插件 [${plugin.name}] 状态异常`)
    }
    await load()
  }, '')
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="plugins-page">
    <PageHeader title="插件管理" :count="plugins.length">
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
    </PageHeader>

    <el-alert
      type="info"
      :closable="false"
      show-icon
      class="plugin-tip"
    >
      <template #title>
        <strong>插件微内核 & 纯透传架构说明</strong>
      </template>
      <span>
        核心平台全面采用零解码、零转码纯透传架构，服务端不进行任何音视频重编码计算，最大化释放服务器算力。
        不同厂商与型号的设备协议通过独立插件驱动承载，可按需动态启用或停用。
      </span>
    </el-alert>

    <el-table
      v-loading="loading"
      :data="plugins"
      row-key="id"
      empty-text="暂无驱动插件"
      style="margin-top: 16px"
    >
      <el-table-column label="驱动名称" min-width="180">
        <template #default="{ row }">
          <div class="plugin-name-cell">
            <el-icon class="plugin-icon"><Cpu /></el-icon>
            <div>
              <strong>{{ row.name }}</strong>
              <div class="sub-text">{{ row.id }}</div>
            </div>
          </div>
        </template>
      </el-table-column>

      <el-table-column label="厂商" width="120">
        <template #default="{ row }">
          <el-tag size="small" type="info">{{ row.vendor }}</el-tag>
        </template>
      </el-table-column>

      <el-table-column prop="version" label="版本" width="90" />

      <el-table-column label="服务端点" min-width="180" prop="endpointUrl" show-overflow-tooltip />

      <el-table-column label="设备数" width="90" align="center">
        <template #default="{ row }">
          <el-badge :value="row.deviceCount" :type="row.deviceCount > 0 ? 'primary' : 'info'" />
        </template>
      </el-table-column>

      <el-table-column label="服务健康" width="100" align="center">
        <template #default="{ row }">
          <el-tag v-if="row.status === 'disabled'" size="small" type="info">已停用</el-tag>
          <el-tag v-else-if="row.healthStatus === 'online'" size="small" type="success">正常</el-tag>
          <el-tag v-else size="small" type="danger">离线</el-tag>
        </template>
      </el-table-column>

      <el-table-column label="支持能力" min-width="220">
        <template #default="{ row }">
          <div class="capabilities-tags">
            <el-tag
              v-for="cap in (row.capabilities || [])"
              :key="cap"
              size="small"
              class="cap-tag"
            >
              {{ capabilityNames[cap] || cap }}
            </el-tag>
          </div>
        </template>
      </el-table-column>

      <el-table-column label="状态" width="100" align="center">
        <template #default="{ row }">
          <el-switch
            :model-value="row.status === 'active'"
            :disabled="!auth.can('device.manage') || busy"
            @change="toggleStatus(row as DevicePlugin)"
          />
        </template>
      </el-table-column>

      <el-table-column
        v-if="auth.can('device.manage')"
        label="操作"
        width="110"
        fixed="right"
        align="center"
      >
        <template #default="{ row }">
          <el-tooltip content="检测驱动健康">
            <el-button
              text
              :icon="Connection"
              :disabled="busy || row.status === 'disabled'"
              aria-label="检测健康"
              @click="checkHealth(row as DevicePlugin)"
            >
              检测
            </el-button>
          </el-tooltip>
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<style scoped>
.plugin-tip {
  margin-bottom: 8px;
}
.plugin-name-cell {
  display: flex;
  align-items: center;
  gap: 10px;
}
.plugin-icon {
  font-size: 24px;
  color: var(--el-color-primary);
}
.sub-text {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}
.capabilities-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}
.cap-tag {
  margin-right: 0;
}
</style>
