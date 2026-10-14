<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { Box, Connection, Cpu, InfoFilled } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import {
  allPages,
  managementApi,
  ponApi,
  type Channel,
  type OnuDeviceDto,
  type TerminalBoxDto
} from '../../api'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  channels: Channel[]
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved'): void
}>()

const { busy, run } = useAction()

const loading = ref(false)
const terminalBoxes = ref<TerminalBoxDto[]>([])
const allOnus = ref<OnuDeviceDto[]>([])

const clearMode = ref(false)
const targetBoxId = ref<number | null>(null)
const targetOnuId = ref<number | null>(null)
const startPort = ref<number>(1)
const isSwitchPort = ref(false)

const filteredOnus = computed(() => {
  if (!targetBoxId.value) return allOnus.value
  return allOnus.value.filter(o => o.terminalBoxId === targetBoxId.value)
})

watch(
  () => props.modelValue,
  async (visible) => {
    if (visible) {
      clearMode.value = false
      targetBoxId.value = null
      targetOnuId.value = null
      startPort.value = 1
      isSwitchPort.value = false
      await loadOptions()
    }
  }
)

async function loadOptions() {
  loading.value = true
  try {
    const [boxesRes, onusRes] = await Promise.all([
      ponApi.terminalBoxes().catch(() => []),
      allPages<OnuDeviceDto>(q => ponApi.onus(q)).catch(() => [])
    ])
    terminalBoxes.value = boxesRes || []
    allOnus.value = onusRes || []
  } catch (err: any) {
    ElMessage.error(err.message || '加载网络设备数据失败')
  } finally {
    loading.value = false
  }
}

// 预览分配端口
const previewList = computed(() => {
  return props.channels.map((c, index) => {
    const portNum = (startPort.value || 1) + index
    const label = clearMode.value
      ? '（清空解除）'
      : (isSwitchPort.value ? `SW-P${portNum}` : `LAN${portNum}`)
    return {
      channel: c,
      targetPortLabel: label
    }
  })
})

async function submit() {
  if (props.channels.length === 0) {
    ElMessage.warning('未选择任何通道')
    return
  }

  if (!clearMode.value && !targetBoxId.value) {
    ElMessage.warning('请选择承载网络终端箱')
    return
  }

  await run(async () => {
    await managementApi.batchSetNetworkBindings({
      channelIds: props.channels.map(c => c.id),
      terminalBoxId: clearMode.value ? null : targetBoxId.value,
      onuDeviceId: clearMode.value ? null : targetOnuId.value,
      startPortNumber: clearMode.value ? null : startPort.value,
      isSwitchPort: clearMode.value ? null : isSwitchPort.value
    })
    ElMessage.success(`成功为 ${props.channels.length} 路通道更新网络承载！`)
    emit('saved')
    emit('update:modelValue', false)
  }, '批量设置网络承载')
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="批量设置网络承载"
    width="740px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div v-loading="loading">
      <el-alert
        title="为所选的多个监控通道一键批量设置物理网络终端箱承载链路。端口将按先后顺序自动顺延递增分配。"
        type="info"
        :closable="false"
        show-icon
        style="margin-bottom: 16px;"
      />

      <el-form label-position="top">
        <el-form-item label="操作模式">
          <el-radio-group v-model="clearMode">
            <el-radio :value="false">分配到网络终端箱与连续端口</el-radio>
            <el-radio :value="true">清空解除所选通道的网络承载</el-radio>
          </el-radio-group>
        </el-form-item>

        <template v-if="!clearMode">
          <el-row :gutter="14">
            <el-col :span="12">
              <el-form-item label="承载网络终端箱" required>
                <el-select
                  v-model="targetBoxId"
                  placeholder="选择网络终端箱"
                  clearable
                  filterable
                  style="width: 100%;"
                  @change="targetOnuId = null"
                >
                  <el-option
                    v-for="b in terminalBoxes"
                    :key="b.id"
                    :label="`${b.name} (${b.code})`"
                    :value="b.id"
                  />
                </el-select>
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="箱内 ONU 设备">
                <el-select
                  v-model="targetOnuId"
                  placeholder="选择箱内 ONU (可选)"
                  clearable
                  filterable
                  style="width: 100%;"
                >
                  <el-option
                    v-for="o in filteredOnus"
                    :key="o.id"
                    :label="`${o.alias || o.onuName} (${o.terminalType})`"
                    :value="o.id"
                  />
                </el-select>
              </el-form-item>
            </el-col>
          </el-row>

          <el-row :gutter="14">
            <el-col :span="12">
              <el-form-item label="起始端口号">
                <el-input-number
                  v-model="startPort"
                  :min="1"
                  :max="48"
                  controls-position="right"
                  style="width: 100%;"
                />
              </el-form-item>
            </el-col>
            <el-col :span="12">
              <el-form-item label="端口类型">
                <el-select v-model="isSwitchPort" style="width: 100%;">
                  <el-option label="ONU直连端口 (LAN)" :value="false" />
                  <el-option label="扩展交换机端口 (SW-P)" :value="true" />
                </el-select>
              </el-form-item>
            </el-col>
          </el-row>
        </template>
      </el-form>

      <!-- 端口映射效果预览 -->
      <div class="preview-header">
        <span>分配效果预览 (共 {{ channels.length }} 路通道)</span>
      </div>

      <el-table :data="previewList" size="small" max-height="240" border stripe>
        <el-table-column type="index" label="序号" width="60" align="center" />
        <el-table-column label="通道名称" min-width="160" show-overflow-tooltip>
          <template #default="{ row }">
            <span class="font-medium">{{ row.channel.alias || row.channel.name }}</span>
          </template>
        </el-table-column>
        <el-table-column label="摄像机 IP" width="130">
          <template #default="{ row }">
            <span class="monospace text-xs">{{ row.channel.ip || '-' }}</span>
          </template>
        </el-table-column>
        <el-table-column label="拟分配端口" width="130" align="center">
          <template #default="{ row }">
            <el-tag :type="clearMode ? 'info' : 'primary'" size="small">
              {{ row.targetPortLabel }}
            </el-tag>
          </template>
        </el-table-column>
      </el-table>
    </div>

    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button
        type="primary"
        :loading="busy"
        :disabled="!clearMode && !targetBoxId"
        @click="submit"
      >
        确认批量更新 ({{ channels.length }})
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.preview-header {
  font-size: 13px;
  font-weight: 600;
  color: #334155;
  margin-top: 14px;
  margin-bottom: 8px;
}
.monospace {
  font-family: monospace;
}
</style>
