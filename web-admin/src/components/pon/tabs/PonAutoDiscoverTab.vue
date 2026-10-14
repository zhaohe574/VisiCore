<script setup lang="ts">
import { ref } from 'vue'
import { Check, Search } from '@element-plus/icons-vue'
import type { AutoTopologyBindingSuggestionDto, AutoTopologyDiscoverResultDto } from '../../../api'

const props = defineProps<{
  result: AutoTopologyDiscoverResultDto | null
  loading: boolean
  applying: boolean
}>()

const emit = defineEmits<{
  (e: 'discover'): void
  (e: 'apply', items: AutoTopologyBindingSuggestionDto[]): void
}>()

const selectedCorrections = ref<AutoTopologyBindingSuggestionDto[]>([])

function onApplySingle(row: AutoTopologyBindingSuggestionDto) {
  emit('apply', [row])
}

function onApplyBatch() {
  emit('apply', selectedCorrections.value)
}
</script>

<template>
  <div class="autodiscover-panel">
    <div class="autodiscover-toolbar">
      <div style="display: flex; align-items: center; gap: 10px;">
        <el-button type="primary" :icon="Search" :loading="loading" @click="emit('discover')">
          开始智能拓扑分析
        </el-button>
        <span class="muted" style="font-size: 13px;">
          自动比对摄像机真实 IP、子网段 (/24)、组织单元与机组特征，给出承载纠偏建议
        </span>
      </div>

      <div v-if="result?.suggestions && result.suggestions.length > 0">
        <el-button
          type="success"
          :icon="Check"
          :loading="applying"
          :disabled="selectedCorrections.length === 0"
          @click="onApplyBatch"
        >
          批量采纳纠偏 (已选 {{ selectedCorrections.length }} 项)
        </el-button>
      </div>
    </div>

    <div v-if="result" class="autodiscover-results card" style="margin-top: 14px; padding: 14px;">
      <div style="margin-bottom: 10px; font-weight: 600; font-size: 13.5px; color: var(--text-primary);">
        拓扑纠偏建议列表 (共评估 {{ result.totalEvaluated }} 路摄像头，生成 {{ result.suggestedCount }} 条匹配推荐)
      </div>

      <el-table
        :data="result.suggestions"
        size="default"
        @selection-change="selectedCorrections = $event"
      >
        <el-table-column type="selection" width="45" />

        <el-table-column prop="channelName" label="摄像头通道" min-width="170">
          <template #default="{ row }">
            <div style="line-height: 1.4;">
              <strong style="color: var(--primary);">{{ row.channelAlias || row.channelName }}</strong>
              <div class="muted" style="font-size: 11px;">原始名: {{ row.channelName }} (ID: {{ row.channelId }})</div>
            </div>
          </template>
        </el-table-column>

        <el-table-column prop="channelIp" label="摄像头 IP" width="135">
          <template #default="{ row }">
            <span class="monospace" style="font-size: 12px; font-weight: 600;">{{ row.channelIp || '—' }}</span>
          </template>
        </el-table-column>

        <el-table-column label="当前承载 ONU" min-width="150">
          <template #default="{ row }">
            <span v-if="row.currentOnuName" class="muted">{{ row.currentOnuName }}</span>
            <el-tag v-else size="small" type="info">未关联</el-tag>
          </template>
        </el-table-column>

        <el-table-column label="推荐承载 ONU 与端口" min-width="190">
          <template #default="{ row }">
            <div>
              <span style="font-weight: 700; color: #0284c7;">{{ row.suggestedOnuAlias || row.suggestedOnuName }}</span>
              <el-tag size="small" type="success" effect="plain" style="margin-left: 6px;">LAN{{ row.suggestedPortNumber }}</el-tag>
            </div>
            <div v-if="row.suggestedTerminalBoxName" class="muted" style="font-size: 11px;">
              箱体: {{ row.suggestedTerminalBoxName }}
            </div>
          </template>
        </el-table-column>

        <el-table-column label="匹配置信度" width="120" align="center">
          <template #default="{ row }">
            <el-progress
              :percentage="row.confidenceScore"
              :status="row.confidenceScore >= 80 ? 'success' : (row.confidenceScore >= 60 ? 'warning' : 'exception')"
              :stroke-width="8"
            />
          </template>
        </el-table-column>

        <el-table-column prop="matchReason" label="匹配判定依据" min-width="210" show-overflow-tooltip>
          <template #default="{ row }">
            <span style="font-size: 12px; color: #475569;">{{ row.matchReason }}</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="120" align="center">
          <template #default="{ row }">
            <el-button size="small" type="primary" link @click="onApplySingle(row as AutoTopologyBindingSuggestionDto)">
              采纳此纠偏
            </el-button>
          </template>
        </el-table-column>
      </el-table>
    </div>
    <div v-else class="card" style="padding: 40px; text-align: center; margin-top: 14px;">
      <el-empty description="点击上方【开始智能拓扑分析】按钮，系统将自动基于网段与命名特征推演最优承载关系" />
    </div>
  </div>
</template>

<style scoped>
.autodiscover-panel {
  display: flex;
  flex-direction: column;
}
.autodiscover-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px 16px;
}
.card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
}
.monospace {
  font-family: 'JetBrains Mono', monospace;
}
.muted {
  color: var(--text-muted, #64748b);
}
</style>
