<script setup lang="ts">
import { ref, watch } from 'vue'
import { Bottom, Top } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { managementApi, type Channel } from '../../api'
import StatusBadge from '../StatusBadge.vue'

const props = defineProps<{
  modelValue: boolean
  unitId: number | null
  unitName: string
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved'): void
}>()

const channels = ref<Channel[]>([])
const loading = ref(false)
const saving = ref(false)

watch(
  () => props.modelValue,
  async (visible) => {
    if (visible && props.unitId) {
      await loadChannels(props.unitId)
    }
  }
)

async function loadChannels(uId: number) {
  loading.value = true
  try {
    const result = await managementApi.channels({ unitId: uId, pageSize: 500 })
    channels.value = result.items || []
  } catch (err: any) {
    ElMessage.error(err.message || '加载单元通道失败')
  } finally {
    loading.value = false
  }
}

function moveChannel(index: number, direction: 'up' | 'down' | 'top' | 'bottom') {
  const list = [...channels.value]
  const target = list[index]
  if (!target) return

  list.splice(index, 1)
  if (direction === 'top') list.unshift(target)
  else if (direction === 'bottom') list.push(target)
  else if (direction === 'up') list.splice(Math.max(0, index - 1), 0, target)
  else if (direction === 'down') list.splice(Math.min(list.length, index + 1), 0, target)

  channels.value = list
}

async function saveSort() {
  if (!props.unitId) return
  saving.value = true
  try {
    const channelIds = channels.value.map(c => c.id)
    await managementApi.sortChannels(props.unitId, channelIds)
    ElMessage.success('通道排序已更新并生效')
    emit('update:modelValue', false)
    emit('saved')
  } catch (err: any) {
    ElMessage.error(err.message || '保存通道排序失败')
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="`单元通道排序 - ${unitName}`"
    width="640px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div style="margin-bottom: 14px; font-size: 13px; color: var(--text-secondary); line-height: 1.6;">
      调整当前单元下各监控通道（点位）的显示次序。保存后将实时联动同步至桌面端资源树与管理端界面。
    </div>

    <div v-loading="loading" style="min-height: 180px; max-height: 440px; overflow-y: auto; padding-right: 4px;">
      <div v-if="!loading && !channels.length" class="muted" style="text-align: center; padding: 48px 0;">
        当前单元下暂无已划拨的通道
      </div>
      <div
        v-for="(channel, idx) in channels"
        :key="channel.id"
        class="channel-entry"
        style="display: flex; align-items: center; justify-content: space-between; padding: 8px 12px; margin-bottom: 6px; background: #f8fafc; border: 1px solid var(--border); border-radius: var(--radius-md);"
      >
        <div style="display: flex; align-items: center; gap: 10px; flex: 1; min-width: 0;">
          <span style="font-weight: 700; font-size: 13px; color: var(--text-secondary); width: 24px; text-align: center;">
            {{ idx + 1 }}
          </span>
          <div style="flex: 1; min-width: 0;">
            <div style="font-weight: 600; font-size: 13px; color: var(--text-primary); overflow: hidden; text-overflow: ellipsis; white-space: nowrap;">
              {{ channel.alias || channel.name }}
              <span v-if="channel.alias" class="muted" style="font-weight: normal; font-size: 11.5px; margin-left: 6px;">
                ({{ channel.name }})
              </span>
            </div>
            <small class="muted" style="font-size: 11px;">
              {{ channel.deviceName }} / 通道 {{ channel.deviceChannel }}
            </small>
          </div>
          <StatusBadge :value="channel.status" style="margin-right: 8px;" />
        </div>

        <div style="display: flex; align-items: center; gap: 4px;">
          <el-tooltip content="置顶">
            <el-button link type="primary" :disabled="idx === 0" :icon="Top" @click="moveChannel(idx, 'top')" />
          </el-tooltip>
          <el-tooltip content="上移">
            <el-button link type="primary" :disabled="idx === 0" @click="moveChannel(idx, 'up')">上移</el-button>
          </el-tooltip>
          <el-tooltip content="下移">
            <el-button link type="primary" :disabled="idx === channels.length - 1" @click="moveChannel(idx, 'down')">下移</el-button>
          </el-tooltip>
          <el-tooltip content="置底">
            <el-button link type="primary" :disabled="idx === channels.length - 1" :icon="Bottom" @click="moveChannel(idx, 'bottom')" />
          </el-tooltip>
        </div>
      </div>
    </div>

    <template #footer>
      <div style="display: flex; justify-content: space-between; align-items: center;">
        <span class="muted" style="font-size: 12px;">共 {{ channels.length }} 路通道</span>
        <div>
          <el-button @click="emit('update:modelValue', false)">取消</el-button>
          <el-button type="primary" :loading="saving" :disabled="loading || !channels.length" @click="saveSort">
            保存排序
          </el-button>
        </div>
      </div>
    </template>
  </el-dialog>
</template>

