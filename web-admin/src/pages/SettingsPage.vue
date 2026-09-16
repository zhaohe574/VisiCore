<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { Check, Refresh, Setting } from '@element-plus/icons-vue'
import { managementApi, type Settings } from '../api'
import { useAction } from '../composables/useAction'
import { errorMessage } from '../lib/format'
import PageHeader from '../components/PageHeader.vue'

const { busy, run } = useAction()
const settings = ref<Settings>()
const loading = ref(false)
const error = ref('')

const activeTab = ref('media')

const limits: Record<keyof Settings, [number, number]> = {
  livePerUser: [1, 64],
  playbackPerUser: [1, 16],
  playbackPerDevice: [1, 128],
  playbackGlobal: [1, 256],
  transcodeGlobal: [0, 32],
  exportGlobal: [1, 16],
  exportPerDevice: [1, 4],
  exportRetentionDays: [1, 365],
  exportQuotaGb: [1, 10000],
  alarmRetentionDays: [1, 3650],
  auditRetentionDays: [1, 3650]
}

const groups = [
  {
    key: 'media',
    title: '视频分发与并发限制',
    desc: '控制单用户与全局实时预览、录像回放的最大并发路数，防止流媒体拥塞与带宽耗尽',
    fields: [
      { key: 'livePerUser' as keyof Settings, label: '单用户实时预览上限', unit: '路', tip: '单个登录用户在客户端或网页端同时开启的最大实时画面数' },
      { key: 'playbackPerUser' as keyof Settings, label: '单用户录像回放上限', unit: '路', tip: '单个用户同时发起的最大录像流数' },
      { key: 'playbackPerDevice' as keyof Settings, label: '单设备录像回放上限', unit: '路', tip: '单个 NVR 录像机允许同时调取的最大回放并发通道数' },
      { key: 'playbackGlobal' as keyof Settings, label: '全平台录像回放总上限', unit: '路', tip: '服务器全局限制的最大录像回放并发总路数' },
      { key: 'transcodeGlobal' as keyof Settings, label: '全局转码通道上限', unit: '路', tip: '服务器 CPU/GPU 允许同时进行 H.265->H.264 兼容转码的最大通道数' }
    ]
  },
  {
    key: 'export',
    title: '录像导出与空间配额',
    desc: '管理 Worker 异步录像剪切任务的并发数、下载文件保留天数及专用磁盘空间配额',
    fields: [
      { key: 'exportGlobal' as keyof Settings, label: '全局并发导出任务数', unit: '个', tip: 'Worker 线程池允许同时执行的 MP4 剪切合成任务数' },
      { key: 'exportPerDevice' as keyof Settings, label: '单设备并发提取上限', unit: '个', tip: '对单一录像机同时发起的抓取任务上限，避免NVR过载' },
      { key: 'exportRetentionDays' as keyof Settings, label: '导出成品保留周期', unit: '天', tip: '合成后的 MP4 文件在服务器暂存保留时间，过期自动清理' },
      { key: 'exportQuotaGb' as keyof Settings, label: '导出目录磁盘配额', unit: 'GB', tip: '允许暂存导出视频的最大磁盘空间占用容量' }
    ]
  },
  {
    key: 'retention',
    title: '数据归档与审计生命周期',
    desc: '配置安全告警事件与关键业务审计日志的数据库保留时长',
    fields: [
      { key: 'alarmRetentionDays' as keyof Settings, label: '报警记录数据保留', unit: '天', tip: '历史报警事件与流转记录保存天数' },
      { key: 'auditRetentionDays' as keyof Settings, label: '操作审计日志保留', unit: '天', tip: '操作审计、登录记录及安全日志保存天数' }
    ]
  }
]

async function load() {
  loading.value = true
  error.value = ''
  try {
    settings.value = await managementApi.settings()
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = false
  }
}

async function save() {
  if (!settings.value) return
  await run(async () => {
    for (const [key, value] of Object.entries(settings.value!) as [keyof Settings, number][]) {
      if (!Number.isInteger(value) || value < limits[key][0] || value > limits[key][1]) {
        throw new Error(`配置项 ${key} 的数值超出合法范围 [${limits[key][0]} ~ ${limits[key][1]}]`)
      }
    }
    await managementApi.saveSettings(settings.value!)
    await load()
  }, '系统配置已保存生效')
}

onMounted(load)
</script>

<template>
  <div>
    <PageHeader title="系统设置" description="配置全局流媒体并发能力阈值、录像剪切导出配额与数据生命周期策略">
      <el-button :icon="Refresh" :loading="loading" @click="load">重新加载</el-button>
      <el-button type="primary" :icon="Check" :loading="busy" :disabled="!settings || loading" @click="save">
        保存全部设置
      </el-button>
    </PageHeader>

    <el-alert v-if="error" :title="error" type="error" :closable="false" show-icon style="margin-bottom: 16px;" />

    <el-tabs v-model="activeTab" style="margin-bottom: 20px;">
      <el-tab-pane v-for="group in groups" :key="group.key" :label="group.title" :name="group.key" />
    </el-tabs>

    <div v-loading="loading">
      <el-form v-if="settings" label-position="top">
        <div v-for="group in groups" v-show="activeTab === group.key" :key="group.key" class="filter-card" style="padding: 24px;">
          <div style="margin-bottom: 20px;">
            <h2 style="margin: 0 0 6px; font-size: 16px;">{{ group.title }}</h2>
            <p class="muted" style="margin: 0; font-size: 13px;">{{ group.desc }}</p>
          </div>

          <div style="display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 20px 32px;">
            <div
              v-for="field in group.fields"
              :key="field.key"
              style="background: #f8fafc; border: 1px solid var(--border); border-radius: var(--radius-md); padding: 16px 20px;"
            >
              <div style="display: flex; justify-content: space-between; align-items: baseline; margin-bottom: 6px;">
                <label style="font-weight: 600; font-size: 13.5px; color: var(--text-primary);">
                  {{ field.label }}
                </label>
                <span class="muted" style="font-size: 12px;">合法范围: {{ limits[field.key][0] }} ~ {{ limits[field.key][1] }} {{ field.unit }}</span>
              </div>

              <p class="muted" style="font-size: 12px; margin: 0 0 14px; min-height: 20px;">
                {{ field.tip }}
              </p>

              <div style="display: flex; align-items: center; gap: 16px;">
                <el-slider
                  v-model="settings[field.key]"
                  :min="limits[field.key][0]"
                  :max="limits[field.key][1]"
                  :step="1"
                  style="flex: 1;"
                />
                <div style="display: flex; align-items: center; gap: 6px; width: 140px;">
                  <el-input-number
                    v-model="settings[field.key]"
                    :min="limits[field.key][0]"
                    :max="limits[field.key][1]"
                    :precision="0"
                    controls-position="right"
                    style="width: 100%;"
                  />
                  <span class="muted" style="font-size: 12px; flex-shrink: 0;">{{ field.unit }}</span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </el-form>
    </div>
  </div>
</template>
