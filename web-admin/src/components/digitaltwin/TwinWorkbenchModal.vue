<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  Aim,
  Box,
  Camera,
  Check,
  Close,
  MagicStick,
  Monitor,
  Platform,
  Refresh,
  Search
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { digitalTwinApi } from '../../api'
import type { CandidateNodeDto, DigitalTwinNodeDto, DigitalTwinSceneDto, SaveCoordinateRequest } from '../../types'

const props = defineProps<{
  visible: boolean
  scene: DigitalTwinSceneDto
  currentNodes: DigitalTwinNodeDto[]
}>()

const emit = defineEmits<{
  (e: 'update:visible', val: boolean): void
  (e: 'refresh'): void
  (e: 'placePendingNode', candidate: CandidateNodeDto): void
}>()

const loading = ref(false)
const alignLoading = ref(false)
const candidates = ref<CandidateNodeDto[]>([])
const searchKeyword = ref('')
const typeFilter = ref<'all' | 'terminal_box' | 'channel' | 'computer' | 'network_device'>('all')

async function loadCandidates() {
  loading.value = true
  try {
    candidates.value = await digitalTwinApi.candidates(props.scene.id)
  } catch (err: any) {
    ElMessage.error(err.message || '加载标定候选设备失败')
  } finally {
    loading.value = false
  }
}

const filteredCandidates = computed(() => {
  return candidates.value.filter(c => {
    if (typeFilter.value !== 'all' && c.targetType !== typeFilter.value) return false
    if (!searchKeyword.value) return true
    const kw = searchKeyword.value.toLowerCase()
    return (
      c.name.toLowerCase().includes(kw) ||
      (c.code ? c.code.toLowerCase().includes(kw) : false) ||
      (c.ip ? c.ip.toLowerCase().includes(kw) : false)
    )
  })
})

const unplacedCount = computed(() => {
  return candidates.value.filter(c => !c.isPlaced).length
})

async function handleAutoAlign() {
  alignLoading.value = true
  try {
    const res = await digitalTwinApi.autoAlignCad(props.scene.id)
    ElMessage.success(`智能对齐完成，已自动精准对齐 ${res.count} 个空间点位`)
    await loadCandidates()
    emit('refresh')
  } catch (err: any) {
    ElMessage.error(err.message || '自动对齐失败')
  } finally {
    alignLoading.value = false
  }
}

function handleSelectCandidate(c: CandidateNodeDto) {
  emit('placePendingNode', c)
}

onMounted(() => {
  if (props.visible) loadCandidates()
})
</script>

<template>
  <el-dialog
    :model-value="visible"
    title="空间资产可视化标定工作台"
    width="800px"
    destroy-on-close
    append-to-body
    class="workbench-dialog"
    @update:model-value="emit('update:visible', $event)"
  >
    <div class="workbench-body">
      <!-- 顶部操作条 -->
      <div class="workbench-top-bar">
        <div class="top-tip">
          <el-icon class="text-cyan"><Aim /></el-icon>
          <span>当前场景: <strong>{{ scene.name }}</strong> (未标定设备: {{ unplacedCount }} 台)</span>
        </div>
        <div class="top-actions">
          <el-button
            type="primary"
            :icon="MagicStick"
            :loading="alignLoading"
            @click="handleAutoAlign"
          >
            一键 CAD 智能吸附对齐
          </el-button>
          <el-button :icon="Refresh" @click="loadCandidates">
            刷新候选
          </el-button>
        </div>
      </div>

      <!-- 筛选与搜索 -->
      <div class="filter-bar">
        <el-radio-group v-model="typeFilter" size="small">
          <el-radio-button label="all">全部</el-radio-button>
          <el-radio-button label="terminal_box">终端箱</el-radio-button>
          <el-radio-button label="channel">摄像头</el-radio-button>
          <el-radio-button label="computer">工位电脑</el-radio-button>
          <el-radio-button label="network_device">网络核心</el-radio-button>
        </el-radio-group>

        <el-input
          v-model="searchKeyword"
          placeholder="搜索名称 / 编码 / IP..."
          size="small"
          clearable
          :prefix-icon="Search"
          style="width: 220px;"
        />
      </div>

      <!-- 候选设备列表 -->
      <div v-loading="loading" class="candidate-list">
        <div
          v-for="c in filteredCandidates"
          :key="`${c.targetType}-${c.targetId}`"
          class="candidate-card"
          :class="{ 'is-placed': c.isPlaced }"
          @click="handleSelectCandidate(c)"
        >
          <div class="card-icon">
            <el-icon v-if="c.targetType === 'terminal_box'"><Box /></el-icon>
            <el-icon v-else-if="c.targetType === 'channel'"><Camera /></el-icon>
            <el-icon v-else-if="c.targetType === 'computer'"><Monitor /></el-icon>
            <el-icon v-else><Platform /></el-icon>
          </div>
          <div class="card-info">
            <div class="card-name-row">
              <span class="card-name">{{ c.name }}</span>
              <el-tag size="small" :type="c.isPlaced ? 'success' : 'info'">
                {{ c.isPlaced ? '已标定' : '待放置' }}
              </el-tag>
            </div>
            <div class="card-sub-row">
              <span v-if="c.code" class="card-code">{{ c.code }}</span>
              <span v-if="c.ip" class="card-ip font-mono">{{ c.ip }}</span>
              <span v-if="c.location" class="card-loc">({{ c.location }})</span>
            </div>
          </div>
        </div>
      </div>

      <div class="workbench-footer-note">
        💡 提示：点击任意待放置设备后，在地图主画布上单击即可快速落点并持久化；也可在画布中直接拖拽已标定微图标微调位置。
      </div>
    </div>
  </el-dialog>
</template>

<style scoped>
.workbench-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.workbench-top-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: rgba(15, 23, 42, 0.7);
  padding: 10px 14px;
  border-radius: 6px;
  border: 1px solid rgba(56, 189, 248, 0.2);
}

.top-tip {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  color: #cbd5e1;
}

.filter-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.candidate-list {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 8px;
  max-height: 380px;
  overflow-y: auto;
  padding-right: 4px;
}

.candidate-card {
  display: flex;
  align-items: center;
  gap: 10px;
  background: rgba(15, 23, 42, 0.5);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 6px;
  padding: 8px 10px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.candidate-card:hover {
  background: rgba(56, 189, 248, 0.1);
  border-color: #38bdf8;
}

.candidate-card.is-placed {
  opacity: 0.75;
}

.card-icon {
  width: 28px;
  height: 28px;
  border-radius: 6px;
  background: rgba(56, 189, 248, 0.15);
  color: #38bdf8;
  display: flex;
  align-items: center;
  justify-content: center;
}

.card-info {
  flex: 1;
  overflow: hidden;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.card-name-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.card-name {
  font-size: 12px;
  font-weight: 700;
  color: #f8fafc;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-sub-row {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 11px;
  color: #94a3b8;
}

.font-mono {
  font-family: monospace;
}

.workbench-footer-note {
  font-size: 12px;
  color: #94a3b8;
  background: rgba(56, 189, 248, 0.05);
  border-left: 3px solid #38bdf8;
  padding: 8px 12px;
  border-radius: 0 4px 4px 0;
}
</style>
