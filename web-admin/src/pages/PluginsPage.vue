<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { Connection, Cpu, Delete, Download, InfoFilled, Plus, Refresh, Search, UploadFilled } from '@element-plus/icons-vue'
import { ElMessage, type UploadUserFile } from 'element-plus'
import { managementApi, type DevicePlugin, type PluginCreateInput } from '../api'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'

const auth = useAuth(), { busy, run, confirm } = useAction()
const plugins = ref<DevicePlugin[]>([])
const loading = ref(false)

// 安装对话框状态
const installDialogOpen = ref(false)
const installTab = ref<'package' | 'remote'>('package')
const uploadFileList = ref<UploadUserFile[]>([])
const uploadEndpointOverride = ref('')
const probing = ref(false)
const remoteForm = ref<PluginCreateInput>({
  id: '',
  name: '',
  vendor: '',
  version: '1.0.0',
  description: '',
  endpointUrl: '',
  capabilities: ['live', 'playback', 'recordings', 'ptz', 'alarms', 'presets']
})

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

function openInstallDialog() {
  uploadFileList.value = []
  uploadEndpointOverride.value = ''
  remoteForm.value = {
    id: '',
    name: '',
    vendor: '',
    version: '1.0.0',
    description: '',
    endpointUrl: '',
    capabilities: ['live', 'playback', 'recordings', 'ptz', 'alarms', 'presets']
  }
  installTab.value = 'package'
  installDialogOpen.value = true
}

function onUploadChange(file: UploadUserFile) {
  uploadFileList.value = [file]
}

async function probeRemoteEndpoint() {
  const url = remoteForm.value.endpointUrl.trim()
  if (!url) {
    ElMessage.warning('请先输入待探测的驱动微服务地址')
    return
  }
  probing.value = true
  try {
    const res = await managementApi.probePlugin(url) as any
    if (res) {
      if (res.id) remoteForm.value.id = res.id
      if (res.name) remoteForm.value.name = res.name
      if (res.vendor) remoteForm.value.vendor = res.vendor
      if (res.version) remoteForm.value.version = res.version
      if (res.description) remoteForm.value.description = res.description
      if (Array.isArray(res.capabilities) && res.capabilities.length > 0) {
        remoteForm.value.capabilities = res.capabilities
      }
      ElMessage.success(`成功识别驱动元数据：${res.name || res.id} (${res.version || '1.0.0'})`)
    }
  } catch (e: any) {
    ElMessage.error(e.message || '探测端点失败，请检查服务是否运行或网络是否可达')
  } finally {
    probing.value = false
  }
}

async function submitInstallPackage() {
  if (uploadFileList.value.length === 0 || !uploadFileList.value[0].raw) {
    ElMessage.warning('请先选择要上传的 .zip 插件安装包')
    return
  }
  const fd = new FormData()
  fd.append('file', uploadFileList.value[0].raw)
  if (uploadEndpointOverride.value.trim()) {
    fd.append('endpointUrl', uploadEndpointOverride.value.trim())
  }
  await run(async () => {
    await managementApi.installPlugin(fd)
    installDialogOpen.value = false
    await load()
  }, '驱动插件安装成功')
}

async function submitRegisterRemote() {
  const form = remoteForm.value
  if (!form.id.trim()) {
    ElMessage.warning('请输入插件标识 (ID)')
    return
  }
  if (!form.name.trim()) {
    ElMessage.warning('请输入驱动显示名称')
    return
  }
  if (!form.endpointUrl.trim()) {
    ElMessage.warning('请输入驱动微服务地址')
    return
  }
  await run(async () => {
    await managementApi.createPlugin(form)
    installDialogOpen.value = false
    await load()
  }, '驱动端点登记成功')
}

async function exportPlugin(plugin: DevicePlugin) {
  await run(async () => {
    const res = await fetch(managementApi.exportPluginUrl(plugin.id), { credentials: 'include' })
    if (!res.ok) throw new Error('导出驱动安装包失败')
    const blob = await res.blob()
    const url = window.URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${plugin.id}-${plugin.version}.zip`
    document.body.appendChild(a)
    a.click()
    a.remove()
    window.URL.revokeObjectURL(url)
  }, `已成功导出驱动安装包：${plugin.name}`)
}

async function deletePlugin(plugin: DevicePlugin) {
  if (plugin.deviceCount > 0) {
    ElMessage.warning(`驱动插件“${plugin.name}”当前正被 ${plugin.deviceCount} 台设备使用，无法删除`)
    return
  }
  await confirm(
    `确定要卸载并彻底删除驱动插件“${plugin.name}”？该操作将清理对应驱动文件及服务注册。`,
    async () => {
      await managementApi.deletePlugin(plugin.id)
      await load()
    },
    '驱动插件已成功卸载并删除'
  )
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
      <el-button
        v-if="auth.can('plugin.manage')"
        type="primary"
        :icon="Plus"
        @click="openInstallDialog"
      >
        安装插件
      </el-button>
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
        不同厂商与型号的设备协议通过独立插件驱动承载，可按需动态安装、卸载、导出、启用或停用。
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

      <el-table-column label="支持能力" min-width="200">
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

      <el-table-column label="状态" width="90" align="center">
        <template #default="{ row }">
          <el-switch
            :model-value="row.status === 'active'"
            :disabled="!auth.can('plugin.manage') || busy"
            @change="toggleStatus(row as DevicePlugin)"
          />
        </template>
      </el-table-column>

      <el-table-column
        v-if="auth.can('plugin.manage')"
        label="操作"
        width="210"
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

          <el-tooltip content="导出驱动安装包">
            <el-button
              text
              :icon="Download"
              :disabled="busy"
              aria-label="导出驱动"
              @click="exportPlugin(row as DevicePlugin)"
            >
              导出
            </el-button>
          </el-tooltip>

          <el-tooltip
            :content="row.deviceCount > 0 ? `正被 ${row.deviceCount} 台设备使用，无法删除` : '卸载并删除驱动'"
            placement="top"
          >
            <span>
              <el-button
                text
                type="danger"
                :icon="Delete"
                :disabled="busy || row.deviceCount > 0"
                aria-label="删除驱动"
                @click="deletePlugin(row as DevicePlugin)"
              >
                删除
              </el-button>
            </span>
          </el-tooltip>
        </template>
      </el-table-column>
    </el-table>

    <!-- 安装插件对话框 -->
    <el-dialog
      v-model="installDialogOpen"
      title="安装设备驱动插件"
      width="640px"
      destroy-on-close
    >
      <el-tabs v-model="installTab">
        <el-tab-pane label="安装包上传 (.zip)" name="package">
          <el-form label-position="top">
            <el-form-item label="插件安装包文件">
              <el-upload
                drag
                action="#"
                :auto-upload="false"
                :limit="1"
                :file-list="uploadFileList"
                accept=".zip"
                @change="onUploadChange"
                @remove="() => uploadFileList = []"
                class="plugin-uploader"
              >
                <el-icon class="el-icon--upload"><UploadFilled /></el-icon>
                <div class="el-upload__text">
                  将 .zip 插件包拖到此处，或 <em>点击上传</em>
                </div>
                <template #tip>
                  <div class="el-upload__tip">
                    必须包含 plugin.json 清单文件及相关驱动程序，单包不超过 500 MB。
                  </div>
                </template>
              </el-upload>
            </el-form-item>

            <el-form-item label="覆盖服务地址（可选）">
              <el-input
                v-model="uploadEndpointOverride"
                placeholder="若留空，则使用安装包内指定的默认端口或本地回环地址"
              />
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="网络端点登记" name="remote">
          <el-form :model="remoteForm" label-position="top">
            <el-form-item label="驱动微服务地址" required>
              <div style="display: flex; gap: 8px; width: 100%;">
                <el-input
                  v-model="remoteForm.endpointUrl"
                  placeholder="例如：http://127.0.0.1:5093 或 http://192.168.1.50:5093"
                />
                <el-button
                  :icon="Search"
                  :loading="probing"
                  @click="probeRemoteEndpoint"
                >
                  探测与识别
                </el-button>
              </div>
            </el-form-item>

            <el-row :gutter="16">
              <el-col :span="12">
                <el-form-item label="插件标识 (ID)" required>
                  <el-input v-model="remoteForm.id" placeholder="如：jovision, dahua" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="驱动名称" required>
                  <el-input v-model="remoteForm.name" placeholder="如：中维世纪网络设备驱动" />
                </el-form-item>
              </el-col>
            </el-row>

            <el-row :gutter="16">
              <el-col :span="12">
                <el-form-item label="厂商">
                  <el-input v-model="remoteForm.vendor" placeholder="如：Jovision" />
                </el-form-item>
              </el-col>
              <el-col :span="12">
                <el-form-item label="版本号">
                  <el-input v-model="remoteForm.version" placeholder="1.0.0" />
                </el-form-item>
              </el-col>
            </el-row>

            <el-form-item label="驱动描述">
              <el-input
                v-model="remoteForm.description"
                type="textarea"
                :rows="2"
                placeholder="简述该驱动支持的设备类型与协议..."
              />
            </el-form-item>

            <el-form-item label="支持能力">
              <el-checkbox-group v-model="remoteForm.capabilities">
                <el-checkbox
                  v-for="(label, key) in capabilityNames"
                  :key="key"
                  :value="key"
                >
                  {{ label }}
                </el-checkbox>
              </el-checkbox-group>
            </el-form-item>
          </el-form>
        </el-tab-pane>
      </el-tabs>

      <template #footer>
        <span class="dialog-footer">
          <el-button @click="installDialogOpen = false">取消</el-button>
          <el-button
            type="primary"
            :loading="busy"
            @click="installTab === 'package' ? submitInstallPackage() : submitRegisterRemote()"
          >
            确认安装
          </el-button>
        </span>
      </template>
    </el-dialog>
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
.plugin-uploader {
  width: 100%;
}
.plugin-uploader :deep(.el-upload) {
  width: 100%;
}
.plugin-uploader :deep(.el-upload-dragger) {
  width: 100%;
}
</style>
