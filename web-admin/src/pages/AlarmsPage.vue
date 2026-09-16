<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import {
  Bell,
  Check,
  CircleClose,
  MuteNotification,
  Notification,
  Refresh,
  Right,
  Search,
  VideoPlay,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { allPages, managementApi, workflowApi, type Alarm, type AlarmDetail, type Device } from '../api'
import { dateTime, errorMessage } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import { useEvents } from '../stores/events'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ChannelPicker from '../components/ChannelPicker.vue'

const route = useRoute()
const auth = useAuth()
const { busy, run } = useAction()

const state = ref('new') // 默认看待处理，操作更加符合安防值守逻辑
const deviceId = ref<number>()
const channelId = ref<number>()
const eventType = ref('')
const range = ref<[Date, Date] | null>(null)
const devices = ref<Device[]>([])

const { items, total, page, pageSize, search, loading, error, load } = usePaged(
  workflowApi.alarms,
  () => ({
    state: state.value || undefined,
    deviceId: deviceId.value,
    channelId: channelId.value,
    eventType: eventType.value || undefined,
    from: range.value?.[0].toISOString(),
    to: range.value?.[1].toISOString()
  }),
  ['alarm.changed', 'access.changed']
)

// 多选与批量操作
const selectedAlarms = ref<Alarm[]>([])

// 报警声音提醒开关
const audioAlertEnabled = ref(localStorage.getItem('visicore-alarm-sound') === 'true')

function toggleAudioAlert() {
  audioAlertEnabled.value = !audioAlertEnabled.value
  localStorage.setItem('visicore-alarm-sound', String(audioAlertEnabled.value))
}

function playAlarmBeep() {
  if (!audioAlertEnabled.value) return
  try {
    const ctx = new (window.AudioContext || (window as any).webkitAudioContext)()
    const osc = ctx.createOscillator()
    const gain = ctx.createGain()
    osc.type = 'sine'
    osc.frequency.setValueAtTime(880, ctx.currentTime)
    gain.gain.setValueAtTime(0.1, ctx.currentTime)
    gain.gain.exponentialRampToValueAtTime(0.01, ctx.currentTime + 0.3)
    osc.connect(gain)
    gain.connect(ctx.destination)
    osc.start()
    osc.stop(ctx.currentTime + 0.3)
  } catch {}
}

const detail = ref<AlarmDetail>()
const detailId = ref<number>()
const detailLoading = ref(false)
const detailError = ref('')
const note = ref('')
const imageFailed = ref(false)

let sequence = 0

async function open(id: number) {
  const current = ++sequence
  detailId.value = id
  detailLoading.value = true
  detailError.value = ''
  imageFailed.value = false
  try {
    const data = await workflowApi.alarm(id)
    if (current === sequence) detail.value = data
  } catch (e) {
    if (current === sequence) detailError.value = errorMessage(e)
  } finally {
    if (current === sequence) detailLoading.value = false
  }
}

function closeDetail() {
  sequence++
  detailId.value = undefined
  detail.value = undefined
  note.value = ''
}

async function action(value: 'claim' | 'note' | 'close' | 'reopen') {
  if (!detail.value) return
  const id = detail.value.id
  if (
    await run(async () => {
      if (value === 'note' && !note.value.trim()) throw new Error('请输入处理备注内容')
      await workflowApi.alarmAction(id, value, note.value.trim() || undefined)
      await open(id)
      await load()
    }, '报警处理记录已更新')
  ) {
    note.value = ''
  }
}

// 批量认领 / 批量关闭
async function batchClaim() {
  if (!selectedAlarms.value.length) return
  await run(async () => {
    for (const a of selectedAlarms.value) {
      if (a.state === 'new') {
        await workflowApi.alarmAction(a.id, 'claim', '批量快速认领')
      }
    }
    await load()
    selectedAlarms.value = []
  }, `已完成 ${selectedAlarms.value.length} 条报警的批量认领`)
}

async function batchClose() {
  if (!selectedAlarms.value.length) return
  await run(async () => {
    for (const a of selectedAlarms.value) {
      if (a.state !== 'closed') {
        await workflowApi.alarmAction(a.id, 'close', '批量确认关闭')
      }
    }
    await load()
    selectedAlarms.value = []
  }, `已完成 ${selectedAlarms.value.length} 条报警的批量关闭`)
}

function resetFilters() {
  search.value = ''
  state.value = ''
  deviceId.value = undefined
  channelId.value = undefined
  eventType.value = ''
  range.value = null
  void load(true)
}

const stats = computed(() => {
  const all = items.value as Alarm[]
  const pending = all.filter(a => a.state === 'new').length
  const processing = all.filter(a => a.state === 'processing').length
  const closed = all.filter(a => a.state === 'closed').length
  return { pending, processing, closed }
})

const unsubscribe = useEvents().subscribe(['alarm.changed', 'reconnected'], async () => {
  playAlarmBeep()
  if (detailId.value) await open(detailId.value)
})

onMounted(async () => {
  if (auth.can('device.read')) {
    await run(async () => {
      devices.value = await allPages(managementApi.devices)
    }, '')
  }
  if (Number(route.query.id)) await open(Number(route.query.id))
})

onBeforeUnmount(() => {
  sequence++
  unsubscribe()
})

const actions: Record<string, string> = {
  claim: '认领报警',
  note: '添加处理备注',
  close: '关闭报警',
  reopen: '重新打开',
  recovered: '设备自动恢复'
}
</script>

<template>
  <div>
    <PageHeader title="报警中心" :count="total" description="集中处理视频周界防范、移动侦测、设备掉线与异常安全告警事件">
      <!-- 报警提示音开关 -->
      <el-tooltip :content="audioAlertEnabled ? '已开启报警蜂鸣提示音（点击静音）' : '已静音（点击开启声音提示）'">
        <el-button
          :type="audioAlertEnabled ? 'warning' : 'default'"
          :icon="audioAlertEnabled ? Notification : MuteNotification"
          @click="toggleAudioAlert"
        >
          {{ audioAlertEnabled ? '提示音已开' : '提示音已关' }}
        </el-button>
      </el-tooltip>
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
    </PageHeader>

    <!-- 顶部 KPI 统计 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap red"><el-icon><Bell /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">当前待处理报警</div>
          <div class="kpi-value">{{ stats.pending }}</div>
          <div class="kpi-sub">需要值守人员确认与核实</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap amber"><el-icon><Warning /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">正在处理中</div>
          <div class="kpi-value">{{ stats.processing }}</div>
          <div class="kpi-sub">已认领并跟进处置中</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><Check /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">已处置关闭</div>
          <div class="kpi-value">{{ stats.closed }}</div>
          <div class="kpi-sub">当前查询范围内已闭环</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><Refresh /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">检索事件总数</div>
          <div class="kpi-value">{{ total }}</div>
          <div class="kpi-sub">满足筛选条件的事件数</div>
        </div>
      </div>
    </div>

    <!-- 筛选卡片 -->
    <div class="filter-card">
      <form class="filter-bar" @submit.prevent="load(true)">
        <el-input
          v-model="search"
          :prefix-icon="Search"
          clearable
          placeholder="搜索报警事件或通道..."
          aria-label="搜索报警"
          style="width: 220px;"
          @clear="load(true)"
        />

        <el-select v-model="state" clearable placeholder="全部处理状态" style="width: 150px;" @change="load(true)">
          <el-option label="待处理" value="new" />
          <el-option label="处理中" value="processing" />
          <el-option label="已关闭" value="closed" />
        </el-select>

        <el-select
          v-if="devices.length"
          v-model="deviceId"
          clearable
          placeholder="全部设备"
          style="width: 170px;"
          aria-label="报警设备筛选"
          @change="load(true)"
        >
          <el-option v-for="device in devices" :key="device.id" :value="device.id" :label="device.name" />
        </el-select>

        <ChannelPicker v-model="channelId" />

        <el-input v-model="eventType" clearable placeholder="事件类型（如 motion）" style="width: 180px;" />

        <el-date-picker
          v-model="range"
          type="datetimerange"
          start-placeholder="开始时间"
          end-placeholder="结束时间"
          format="YYYY-MM-DD HH:mm"
          style="width: 320px;"
        />

        <el-button type="primary" native-type="submit" :icon="Search">查询</el-button>
        <el-button @click="resetFilters">重置</el-button>

        <!-- 批量操作 -->
        <div v-if="auth.can('alarm.ack') && selectedAlarms.length" class="filter-actions">
          <span class="muted" style="font-size: 12.5px;">已选 {{ selectedAlarms.length }} 项：</span>
          <el-button size="small" type="primary" :disabled="busy" @click="batchClaim">批量认领</el-button>
          <el-button size="small" type="success" :disabled="busy" @click="batchClose">批量关闭</el-button>
        </div>
      </form>
    </div>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 12px;" />

    <!-- 报警数据表格卡片 -->
    <div class="table-card">
      <el-table
        v-loading="loading"
        :data="items"
        row-key="id"
        empty-text="暂无匹配的报警记录"
        @selection-change="selectedAlarms = $event"
      >
        <el-table-column v-if="auth.can('alarm.ack')" type="selection" width="44" />

        <el-table-column label="发生时间" min-width="168">
          <template #default="{ row }">
            <span style="font-weight: 500;">{{ dateTime(row.occurredAt) }}</span>
          </template>
        </el-table-column>

        <el-table-column prop="deviceName" label="归属设备" min-width="140" show-overflow-tooltip />

        <el-table-column prop="channelName" label="触发通道" min-width="150" show-overflow-tooltip>
          <template #default="{ row }">
            <strong>{{ row.channelName || '设备内部事件' }}</strong>
          </template>
        </el-table-column>

        <el-table-column prop="eventType" label="事件类型" min-width="140">
          <template #default="{ row }">
            <el-tag size="small" :type="row.eventType.includes('motion') ? 'danger' : 'warning'">
              {{ row.eventType }}
            </el-tag>
          </template>
        </el-table-column>

        <el-table-column label="处理状态" width="110">
          <template #default="{ row }">
            <StatusBadge :value="row.state" />
          </template>
        </el-table-column>

        <el-table-column label="设备恢复" width="100">
          <template #default="{ row }">
            <span :class="row.recovered ? 'teal' : 'muted'">{{ row.recovered ? '已恢复' : '未恢复' }}</span>
          </template>
        </el-table-column>

        <el-table-column prop="ownerName" label="责任处理人" min-width="120">
          <template #default="{ row }">
            <span v-if="row.ownerName">{{ row.ownerName }}</span>
            <span v-else class="muted">未认领</span>
          </template>
        </el-table-column>

        <el-table-column label="操作" width="110" fixed="right">
          <template #default="{ row }">
            <el-button link type="primary" @click="open(row.id)">详情与处置</el-button>
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

    <!-- 报警详情与交互处置抽屉 -->
    <el-drawer :model-value="!!detailId" title="报警事件处置" size="640px" @close="closeDetail">
      <div v-loading="detailLoading">
        <el-alert v-if="detailError" :title="detailError" type="error" :closable="false" style="margin-bottom: 16px;" />

        <template v-if="detail && detail.id === detailId">
          <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px;">
            <div>
              <h2 style="margin: 0; font-size: 17px; color: var(--text-primary);">{{ detail.eventType }}</h2>
              <span class="muted" style="font-size: 12px;">编号: {{ detail.id }}</span>
            </div>
            <StatusBadge :value="detail.state" />
          </div>

          <!-- 快捷直达实时视频按钮 -->
          <div
            v-if="detail.channelId"
            style="background: #eff6ff; border: 1px solid #bfdbfe; border-radius: var(--radius-md); padding: 12px 16px; display: flex; align-items: center; justify-content: space-between; margin-bottom: 18px;"
          >
            <div>
              <strong style="color: #1e40af; font-size: 13.5px; display: block;">现场通道实时联动</strong>
              <small class="muted">{{ detail.channelName }} (设备 {{ detail.deviceName }})</small>
            </div>
            <router-link :to="{ path: '/app/live', query: { channelId: detail.channelId } }">
              <el-button type="primary" :icon="VideoPlay">查看现场画面</el-button>
            </router-link>
          </div>

          <dl class="data-list" style="margin-bottom: 20px;">
            <div><dt>所属设备 / 通道</dt><dd>{{ detail.deviceName }} / {{ detail.channelName || '设备内部事件' }}</dd></div>
            <div><dt>触发时间</dt><dd>{{ dateTime(detail.occurredAt) }}</dd></div>
            <div><dt>恢复状态</dt><dd>{{ detail.recovered ? '已恢复正常' : '持续告警中' }}</dd></div>
            <div><dt>责任人</dt><dd>{{ detail.ownerName || '尚未认领' }}</dd></div>
          </dl>

          <!-- 报警抓拍大图 -->
          <div v-if="detail.imageAvailable && !imageFailed" style="margin-bottom: 20px;">
            <h3 style="margin-bottom: 8px;">现场抓拍图像</h3>
            <div style="border-radius: var(--radius-md); overflow: hidden; background: #0f172a; border: 1px solid var(--border); text-align: center;">
              <el-image
                :src="workflowApi.alarmImage(detail.id)"
                :preview-src-list="[workflowApi.alarmImage(detail.id)]"
                fit="contain"
                style="width: 100%; max-height: 360px; display: block;"
                @error="imageFailed = true"
              />
            </div>
          </div>
          <el-alert v-if="imageFailed" title="现场抓拍图片暂时无法读取或未同步" type="warning" :closable="false" style="margin-bottom: 16px;" />

          <!-- 处理操作区 -->
          <section v-if="auth.can('alarm.ack')" class="filter-card" style="margin-bottom: 20px; padding: 18px;">
            <h3 style="margin-top: 0; margin-bottom: 12px;">处置操作与批注</h3>
            <el-input
              v-model="note"
              type="textarea"
              :rows="3"
              maxlength="2000"
              show-word-limit
              placeholder="输入核实情况或处置记录说明..."
              aria-label="处理备注"
            />
            <div style="display: flex; gap: 10px; margin-top: 14px; flex-wrap: wrap;">
              <el-button v-if="detail.state === 'new'" type="primary" :disabled="busy" @click="action('claim')">
                认领此报警
              </el-button>
              <el-button :disabled="busy || !note.trim()" @click="action('note')">
                添加处理备注
              </el-button>
              <el-button v-if="detail.state !== 'closed'" type="success" :disabled="busy" @click="action('close')">
                处理完成并关闭
              </el-button>
              <el-button v-else type="warning" :disabled="busy" @click="action('reopen')">
                重新打开报警
              </el-button>
            </div>
          </section>

          <!-- 处理历史时间轴 -->
          <h3 style="margin-bottom: 14px;">流转记录</h3>
          <el-timeline style="padding-left: 6px;">
            <el-timeline-item
              v-for="entry in detail.history"
              :key="entry.id"
              :timestamp="dateTime(entry.createdAt)"
              placement="top"
              type="primary"
            >
              <div style="font-weight: 600; font-size: 13px; color: var(--text-primary);">
                {{ entry.username }} · {{ actions[entry.action] || entry.action }}
              </div>
              <p v-if="entry.note" class="preserve-lines muted" style="margin: 4px 0 0; font-size: 12.5px;">
                {{ entry.note }}
              </p>
            </el-timeline-item>
          </el-timeline>

          <details style="margin-top: 20px;">
            <summary class="muted" style="font-size: 12px; cursor: pointer;">展开原始事件 JSON 报文</summary>
            <pre class="hash" style="max-height: 200px; overflow-y: auto; margin-top: 8px;">{{ typeof detail.payload === 'string' ? detail.payload : JSON.stringify(detail.payload, null, 2) }}</pre>
          </details>
        </template>
      </div>
    </el-drawer>
  </div>
</template>
