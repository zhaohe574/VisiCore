<script setup lang="ts">
import { Refresh, TrendCharts } from '@element-plus/icons-vue'
import type { OpticalHealthIndexDto } from '../../../api'

const props = defineProps<{
  healthIndexData: OpticalHealthIndexDto | null
  loading: boolean
  syncingEsight: boolean
}>()

const emit = defineEmits<{
  (e: 'refresh'): void
  (e: 'sync'): void
}>()
</script>

<template>
  <div v-loading="loading" class="health-screen-panel">
    <!-- 顶部综合评分横幅 -->
    <div class="health-score-banner">
      <div class="score-main">
        <div class="score-circle">
          <div class="score-num">{{ healthIndexData?.score ?? 100 }}</div>
          <div class="score-unit">综合评分</div>
        </div>
        <div class="score-desc">
          <div class="score-title">
            光网络总体健康状况：
            <el-tag
              :type="healthIndexData?.score && healthIndexData.score >= 90 ? 'success' : (healthIndexData?.score && healthIndexData.score >= 80 ? 'primary' : 'warning')"
              size="large"
              effect="dark"
              style="font-size: 14px; font-weight: 700; margin-left: 6px;"
            >
              {{ healthIndexData?.grade ?? '优秀' }}
            </el-tag>
          </div>
          <p class="muted" style="margin: 6px 0 0; font-size: 13px; line-height: 1.6;">
            系统基于华为 NeoSight 官方北向 OpenAPI 网关实时监测。全网 188 台 ONU 实时通畅率为 <strong>{{ healthIndexData?.availabilityRate ?? 100 }}%</strong>，
            平均接收光衰为 <strong>{{ healthIndexData?.avgRxPower ? `${healthIndexData.avgRxPower} dBm` : '良好' }}</strong>。
            已启用 30 秒闪断平滑防抖窗口，消除工频冲击引起的误报。
          </p>
        </div>
      </div>
      <div class="score-actions">
        <el-button type="primary" :icon="Refresh" :loading="loading" @click="emit('refresh')">刷新健康指数</el-button>
        <el-button type="warning" :loading="syncingEsight" @click="emit('sync')">同步网管最新数据</el-button>
      </div>
    </div>

    <!-- 六大状态分布网格 -->
    <div class="health-metrics-grid">
      <div class="health-metric-box healthy">
        <div class="metric-top">
          <span class="metric-tag">正常运行 🟢</span>
          <span class="metric-val">{{ healthIndexData?.healthyOnus ?? 0 }}</span>
        </div>
        <div class="metric-desc">工作于正常安全光功率区间 (-8~-24 dBm)</div>
      </div>

      <div class="health-metric-box subhealth">
        <div class="metric-top">
          <span class="metric-tag">弱光亚健康 🟡</span>
          <span class="metric-val">{{ healthIndexData?.subHealthOnus ?? 0 }}</span>
        </div>
        <div class="metric-desc">光功率处于 -25~-27 dBm 劣化边缘，易诱发断流</div>
      </div>

      <div class="health-metric-box poweroff">
        <div class="metric-top">
          <span class="metric-tag">终端箱掉电 ⚡</span>
          <span class="metric-val">{{ healthIndexData?.powerOffOnus ?? 0 }}</span>
        </div>
        <div class="metric-desc">检测到网管 Dying Gasp 掉电遗言，属于市电/断路器掉电</div>
      </div>

      <div class="health-metric-box splitterbroken">
        <div class="metric-top">
          <span class="metric-tag">分光/支干中断 💥</span>
          <span class="metric-val">{{ healthIndexData?.splitterBrokenOnus ?? 0 }}</span>
        </div>
        <div class="metric-desc">同 PON 口短时间内并发断光，疑似二级分光器或支干光缆受损</div>
      </div>

      <div class="health-metric-box fibercut">
        <div class="metric-top">
          <span class="metric-tag">分支断纤 🔴</span>
          <span class="metric-val">{{ healthIndexData?.fiberCutOnus ?? 0 }}</span>
        </div>
        <div class="metric-desc">单台分支跳线受折断、LOSi 物理失光或末端法兰松脱</div>
      </div>

      <div class="health-metric-box trunkbroken">
        <div class="metric-top">
          <span class="metric-tag">主干光缆中断 🚨</span>
          <span class="metric-val">{{ healthIndexData?.trunkBrokenOnus ?? 0 }}</span>
        </div>
        <div class="metric-desc">同一 PON 端口全量设备全灭，主干光缆割接中断或板卡故障</div>
      </div>

      <div class="health-metric-box totaloffline">
        <div class="metric-top">
          <span class="metric-tag">离线终端总计</span>
          <span class="metric-val">{{ healthIndexData?.offlineOnus ?? 0 }}</span>
        </div>
        <div class="metric-desc">占全厂光终端 {{ healthIndexData?.totalOnus ? Math.round(((healthIndexData.offlineOnus || 0) / healthIndexData.totalOnus) * 100) : 0 }}%</div>
      </div>
    </div>

    <!-- 24 小时健康指数趋势快照列表 -->
    <div class="card" style="padding: 18px; margin-top: 16px;">
      <div style="font-weight: 600; font-size: 14.5px; margin-bottom: 12px; display: flex; align-items: center; justify-content: space-between;">
        <div style="display: flex; align-items: center; gap: 6px;">
          <el-icon><TrendCharts /></el-icon>
          <span>全网光链路健康度历史波动趋势 (快照记录)</span>
        </div>
        <span class="muted" style="font-size: 12px; font-weight: normal;">记录周期：实时同步触发与每日基线归档</span>
      </div>

      <el-table :data="healthIndexData?.trend ?? []" size="small" style="width: 100%;">
        <el-table-column prop="time" label="采样时间" min-width="170">
          <template #default="{ row }">
            <span class="monospace">{{ new Date(row.time).toLocaleString('zh-CN', { hour12: false }) }}</span>
          </template>
        </el-table-column>
        <el-table-column prop="score" label="健康指数" width="120" align="center">
          <template #default="{ row }">
            <el-tag :type="row.score >= 90 ? 'success' : (row.score >= 80 ? 'primary' : 'warning')" size="small" effect="plain" style="font-weight: 700;">
              {{ row.score }} 分
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column prop="availabilityRate" label="链路通畅率" width="130" align="center">
          <template #default="{ row }">
            <span class="monospace" style="font-weight: 600;">{{ row.availabilityRate }}%</span>
          </template>
        </el-table-column>
        <el-table-column prop="offlineCount" label="离线设备数" width="120" align="center">
          <template #default="{ row }">
            <span :style="{ color: row.offlineCount > 0 ? '#ef4444' : '#10b981', fontWeight: '600' }">{{ row.offlineCount }} 台</span>
          </template>
        </el-table-column>
        <el-table-column prop="subHealthCount" label="弱光亚健康数" width="130" align="center">
          <template #default="{ row }">
            <span :style="{ color: row.subHealthCount > 0 ? '#f59e0b' : '#10b981', fontWeight: '600' }">{{ row.subHealthCount }} 台</span>
          </template>
        </el-table-column>
      </el-table>
    </div>
  </div>
</template>

<style scoped>
.health-screen-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.health-score-banner {
  background: linear-gradient(135deg, #ffffff 0%, #f8fafc 100%);
  border: 1px solid #e2e8f0;
  border-radius: 10px;
  padding: 22px 24px;
  display: flex;
  align-items: center;
  justify-content: space-between;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.03);
}
.score-main {
  display: flex;
  align-items: center;
  gap: 20px;
}
.score-circle {
  width: 90px;
  height: 90px;
  border-radius: 50%;
  background: linear-gradient(135deg, #0d9488, #14b8a6);
  color: #ffffff;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  box-shadow: 0 4px 14px rgba(13, 148, 136, 0.35);
}
.score-num {
  font-size: 32px;
  font-weight: 800;
  line-height: 1;
  font-family: 'JetBrains Mono', monospace;
}
.score-unit {
  font-size: 11px;
  opacity: 0.9;
  margin-top: 3px;
}
.score-title {
  font-size: 17px;
  font-weight: 700;
  color: #0f172a;
}
.score-actions {
  display: flex;
  gap: 10px;
}

.health-metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 14px;
}
.health-metric-box {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.health-metric-box.healthy { border-left: 4px solid #10b981; }
.health-metric-box.subhealth { border-left: 4px solid #f59e0b; background: #fffbeb; }
.health-metric-box.poweroff { border-left: 4px solid #ef4444; background: #fef2f2; }
.health-metric-box.splitterbroken { border-left: 4px solid #9333ea; background: #faf5ff; }
.health-metric-box.fibercut { border-left: 4px solid #f97316; }
.health-metric-box.trunkbroken { border-left: 4px solid #8b0000; background: #fff1f2; }
.health-metric-box.totaloffline { border-left: 4px solid #64748b; }

.metric-top {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.metric-tag {
  font-size: 12.5px;
  font-weight: 600;
  color: #334155;
}
.metric-val {
  font-size: 24px;
  font-weight: 800;
  font-family: 'JetBrains Mono', monospace;
}
.metric-desc {
  font-size: 11px;
  color: #64748b;
  line-height: 1.4;
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
