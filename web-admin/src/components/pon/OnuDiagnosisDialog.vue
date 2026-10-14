<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import {
  CircleCheck,
  CircleClose,
  Connection,
  DocumentCopy,
  InfoFilled,
  Lightning,
  Monitor,
  Odometer,
  Refresh,
  Tools,
  VideoCamera,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ponApi, type OnuDeviceDto, type OnuDiagnosisDto, type OnuPortStatusDto } from '../../api'

const props = defineProps<{
  modelValue: boolean
  onu: OnuDeviceDto | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
}>()

const loading = ref(false)
const diagnosis = ref<OnuDiagnosisDto | null>(null)

watch(
  () => props.modelValue,
  async (visible) => {
    if (visible && props.onu) {
      await loadDiagnosis()
    } else {
      diagnosis.value = null
    }
  }
)

async function loadDiagnosis() {
  if (!props.onu) return
  loading.value = true
  try {
    diagnosis.value = await ponApi.diagnoseOnu(props.onu.id)
  } catch (err: any) {
    ElMessage.error(err.message || '查询光衰与端口诊断失败')
  } finally {
    loading.value = false
  }
}

// 设备在线状态
const isOnline = computed(() => {
  if (diagnosis.value) {
    return diagnosis.value.status === 'online' || diagnosis.value.status === '1'
  }
  return props.onu?.status === 'online' || props.onu?.runStatus === '1'
})

// 健康状态与定界
const healthStatus = computed(() => {
  return diagnosis.value?.healthStatus || props.onu?.healthStatus || (isOnline.value ? 'healthy' : 'fiber_cut')
})

// 光功率读数与健康级别
const opticalHealth = computed(() => {
  const rx = diagnosis.value?.rxPower ?? props.onu?.rxPower
  if (isOnline.value) {
    if (rx !== null && rx !== undefined) {
      if (rx >= -24.0 && rx <= -8.0) {
        return {
          level: 'normal',
          text: `${rx.toFixed(2)} dBm`,
          badge: '优良',
          type: 'success',
          tip: '接收光功率处于优良安全门限 (-8.0 ~ -24.0 dBm) 区间内'
        }
      }
      if (rx < -24.0 && rx >= -27.0) {
        return {
          level: 'warning',
          text: `${rx.toFixed(2)} dBm`,
          badge: '弱光预警',
          type: 'warning',
          tip: '接收光衰处于临界衰耗区间 (-24.0 ~ -27.0 dBm)，建议适时清洁端面'
        }
      }
      return {
        level: 'critical',
        text: `${rx.toFixed(2)} dBm`,
        badge: '光衰超标',
        type: 'danger',
        tip: '接收光衰严重超标，存在突发丢包脱网隐患'
      }
    }
    return {
      level: 'normal',
      text: diagnosis.value?.opticalStatus || '安全区间内 (-8~-24dBm)',
      badge: '正常',
      type: 'success',
      tip: '光路衰耗处于正常工作区间 (-8 ~ -24 dBm)'
    }
  }

  // 离线情形
  if (healthStatus.value === 'power_off') {
    return {
      level: 'power_off',
      text: '设备断电无读数',
      badge: '停电',
      type: 'info',
      tip: 'ONU 本地供电断开，光模块未通电发光（物理光纤完好）'
    }
  }
  return {
    level: 'critical',
    text: '无光信号 (脱网)',
    badge: '无光',
    type: 'danger',
    tip: '未检测到光输入信号，光路处于中断状态'
  }
})

// 物理光纤链路状态
const fiberLinkStatus = computed(() => {
  if (isOnline.value) {
    if (healthStatus.value === 'sub_health_weak_light') {
      return {
        text: '弱光预警 (Degraded)',
        category: '支干或分支衰减',
        type: 'warning'
      }
    }
    return {
      text: '链路连通 (Up)',
      category: '主干与分支正常',
      type: 'success'
    }
  }

  switch (healthStatus.value) {
    case 'power_off':
      return {
        text: '未激活 (终端断电)',
        category: '光路完好 / 电源未通',
        type: 'info'
      }
    case 'trunk_broken':
      return {
        text: '主干光缆中断 (Trunk Cut)',
        category: 'PON 口主干光缆',
        type: 'danger'
      }
    case 'splitter_broken':
      return {
        text: '分光器支干中断 (Branch Cut)',
        category: '同分光器上级光缆',
        type: 'danger'
      }
    case 'fiber_cut':
      return {
        text: '单台分支尾纤中断 (LOSi)',
        category: '入箱皮线/尾纤接头',
        type: 'danger'
      }
    default:
      return {
        text: diagnosis.value?.fiberStatus || '光路中断',
        category: '物理链路异常',
        type: 'danger'
      }
  }
})

// 下线原因语义化解析
const downCauseParsed = computed(() => {
  if (isOnline.value) {
    return {
      code: '正常',
      label: '稳定在线运行',
      detail: props.onu?.lastUpTime ? `上线时间: ${new Date(props.onu.lastUpTime).toLocaleString()}` : '设备当前正常连通',
      type: 'success'
    }
  }

  const causeRaw = (diagnosis.value?.lastDownCause || props.onu?.lastDownCause || '').trim()
  const hs = healthStatus.value

  // 1. 掉电判定（优先）
  if (hs === 'power_off' || causeRaw === '13' || causeRaw.toLowerCase().includes('dying') || causeRaw.toLowerCase().includes('power')) {
    return {
      code: 'Cause 13',
      label: '掉电告警 (Dying Gasp)',
      detail: '断电瞬间已接收告警遗言帧 · 供电中断',
      type: 'warning'
    }
  }

  // 2. 光信号丢失
  if (causeRaw === '1' || causeRaw.toLowerCase().includes('losi') || causeRaw.toLowerCase().includes('los')) {
    return {
      code: 'Cause 1',
      label: '光信号丢失 (LOSi)',
      detail: '物理光信号中断 · 光纤或分光器脱落',
      type: 'danger'
    }
  }

  // 3. 帧失步
  if (causeRaw === '2' || causeRaw.toLowerCase().includes('lofi')) {
    return {
      code: 'Cause 2',
      label: '光帧失步 (LOFi)',
      detail: '接收光微弱引起严重误码失步',
      type: 'warning'
    }
  }

  // 4. 网管去激活
  if (causeRaw === '15' || causeRaw.toLowerCase().includes('deact')) {
    return {
      code: 'Cause 15',
      label: '网管去激活 (Deactivated)',
      detail: 'OLT 端口去激活或认证注销',
      type: 'info'
    }
  }

  // 5. 设备重启
  if (causeRaw === '17' || causeRaw.toLowerCase().includes('reboot')) {
    return {
      code: 'Cause 17',
      label: '设备重启 (Reboot)',
      detail: '设备执行了本地或远程软重启',
      type: 'info'
    }
  }

  if (causeRaw) {
    return {
      code: `Cause ${causeRaw}`,
      label: `下线代号 ${causeRaw}`,
      detail: '华为网管下线事件代码',
      type: 'danger'
    }
  }

  return {
    code: '未上报',
    label: '异常离线 (未获取代码)',
    detail: '未记录下线原因代码',
    type: 'danger'
  }
})

// 智能排障 SOP 指引内容
const sopGuidance = computed(() => {
  if (isOnline.value) {
    if (healthStatus.value === 'sub_health_weak_light') {
      return {
        title: '【弱光亚健康预警】接收光功率处于衰耗临界值',
        tag: '光衰预警',
        tagType: 'warning',
        bg: '#fffbeb',
        border: '#fde68a',
        steps: [
          '使用专用无尘光纤擦拭纸或清洁笔清洁终端箱内 SC/UPC 尾纤插头与法兰盘内部积尘。',
          '排查箱内盘纤弯曲半径，确保弯曲直径大于 60mm，消除因过度受压挤弯产生的额外光损耗。',
          '使用高精度手持光功率计复测入户光功率，建议将其调整并恢复至 -22.0 dBm 以内的优良范围。'
        ]
      }
    }
    return {
      title: '【光网连通优良】全链路物理指标稳定正常',
      tag: '运行健康',
      tagType: 'success',
      bg: '#f0fdf4',
      border: '#bbf7d0',
      steps: [
        '接收光功率处于优良安全门限 (-8.0 ~ -24.0 dBm) 区间，光路物理损耗极低。',
        '下行以太网端口与监控 IPC/终端设备正常握手协商，传输误码率为 0。无需人工干预。'
      ]
    }
  }

  switch (healthStatus.value) {
    case 'power_off':
      return {
        title: '【故障定界：终端箱总供电中断 / 漏保跳闸】光纤线路完好 ⚡',
        tag: '供电故障',
        tagType: 'warning',
        bg: '#fff7ed',
        border: '#fed7aa',
        steps: [
          '设备在停电瞬间成功上报了 Dying Gasp 掉电告警帧，证明主干与分支光缆物理链路完全正常，严禁插拔拆卸光纤与法兰盘！',
          '前往现场检查该终端箱内交流 220V 空气开关或漏电保护器是否跳闸。',
          '检查箱内插排、PDU 供电与 ONU 配套的 12V 直流电源适配器，确认电源指示灯是否点亮。'
        ]
      }
    case 'splitter_broken':
      return {
        title: '【故障定界：同分光器支干光纤中断 / 批量失步】上级支干断纤 ✂️',
        tag: '支干断缆',
        tagType: 'danger',
        bg: '#fdf4ff',
        border: '#f5d0fe',
        steps: [
          '同分光器下的多台终端在短时间内集中批量失步离线且无断电遗言，判定为上级分光器主输入端或支干光缆损坏。',
          '前往所属光交箱（或二级光分配箱），检查该 1:8 / 1:16 分光器的上行主输入端尾纤是否脱落松动。',
          '排查由光交箱通往本终端箱片区的主干/分支光缆割接段，巡视沿线管道是否有市政道路施工或外力挖断。',
          '使用 OTDR 光时域反射仪从光交箱向终端箱方向打光打点，精准测定断点米数。'
        ]
      }
    case 'trunk_broken':
      return {
        title: '【故障定界：PON 口主干光缆整体中断】机房出局干缆故障 🚨',
        tag: '主干中断',
        tagType: 'danger',
        bg: '#fef2f2',
        border: '#fecaca',
        steps: [
          '该 PON 口下挂接的全部 ONU 设备整体全灭失步，判定为机房至主光交箱的骨干主缆阻断或 OLT 板卡光模块硬件故障。',
          '通报通信机房维护人员检查 OLT 对应 PON 口的板卡运行灯与光模块发光状态。',
          '线路抢修班组从 OLT ODF 架使用 OTDR 向室外骨干光缆测试断纤距离并紧急熔接。'
        ]
      }
    case 'fiber_cut':
    default:
      return {
        title: '【故障定界：单台分支尾纤中断 (LOSi)】仅该终端失步 🔍',
        tag: '单户断纤',
        tagType: 'danger',
        bg: '#fef2f2',
        border: '#fecaca',
        steps: [
          '同分光器下其余终端运行正常，仅该单台设备突发失步，属于终端箱内跳线或入箱皮线光纤故障。',
          '使用红光笔（可见光源）从终端箱内 SC/UPC 尾纤处打光，检查法兰盘对接是否牢固、尾纤是否存在锐角折断。',
          '检查箱外引入的入户皮线光缆是否有机械挤压、施工拉断或被老鼠啃咬。',
          '若接头损坏，使用光纤冷接子或光纤熔接机重新制作 SC/UPC 活动连接头。'
        ]
      }
  }
})

// 格式化时间
function formatTime(isoStr?: string | null) {
  if (!isoStr) return '—'
  try {
    const d = new Date(isoStr)
    return isNaN(d.getTime()) ? isoStr : d.toLocaleString()
  } catch {
    return isoStr
  }
}

// 复制排障工单文本
async function copyDiagnosisReport() {
  if (!props.onu) return
  const o = props.onu
  const diag = diagnosis.value
  const causeInfo = downCauseParsed.value
  const optInfo = opticalHealth.value
  const fiberInfo = fiberLinkStatus.value

  const reportText = `【ONU 排障诊断工单】
━━━━━━━━━━━━━━━━━━━━━━━━━━━━
■ 设备信息
· 设备名称: ${o.alias || o.onuName}
· 设备型号: ${o.terminalType || '—'} · SN: ${o.sn}
· 所属 OLT: ${o.oltName} (${o.oltIp}) · PON口: ${o.portId || o.neDn}
· 归属终端箱: ${o.terminalBoxName || '未关联终端箱'}

■ 实时状态与定界
· 运行状态: ${isOnline.value ? '🟢 光网在线' : '🔴 光网离线'}
· 故障定界: ${sopGuidance.value.title}
· 物理链路: ${fiberInfo.text} (${fiberInfo.category})
· 接收光功率: ${optInfo.text} [${optInfo.badge}]
· 下线原因: ${causeInfo.code} · ${causeInfo.label} (${causeInfo.detail})
· 最近离线时间: ${formatTime(o.lastDownTime)}
· 最近上线时间: ${formatTime(o.lastUpTime)}

■ 现场排查处置 SOP
${sopGuidance.value.steps.map((s, idx) => `${idx + 1}. ${s}`).join('\n')}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━
报告生成时间: ${new Date().toLocaleString()}`

  try {
    if (navigator?.clipboard?.writeText) {
      await navigator.clipboard.writeText(reportText)
    } else {
      const textarea = document.createElement('textarea')
      textarea.value = reportText
      document.body.appendChild(textarea)
      textarea.select()
      document.execCommand('copy')
      document.body.removeChild(textarea)
    }
    ElMessage.success('排障诊断工单已复制到剪贴板，可直接发送至运维群')
  } catch (e: any) {
    ElMessage.warning('复制失败，请手动选取复制')
  }
}

// 端口显示列表
const displayPorts = computed<OnuPortStatusDto[]>(() => {
  if (diagnosis.value?.ports && diagnosis.value.ports.length > 0) {
    return diagnosis.value.ports
  }
  return [1, 2, 3, 4].map((i): OnuPortStatusDto => ({
    portId: i,
    name: `LAN${i}`,
    status: 'down',
    speed: '--',
    duplex: '--',
    terminalMac: null,
    deviceName: null,
    deviceIp: null,
    deviceType: null,
    deviceStatus: null
  }))
})
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="ONU 实时光衰与端口排障诊断"
    width="780px"
    destroy-on-close
    class="onu-diagnosis-dialog"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <div v-loading="loading" class="dialog-body">
      <!-- 1. 基础信息卡片 (Header Card) -->
      <div v-if="onu" class="hero-card">
        <div class="hero-left">
          <div class="hero-title-row">
            <span class="hero-status-dot" :class="isOnline ? 'online' : 'offline'" />
            <span class="hero-title">{{ onu.alias || onu.onuName }}</span>
            <el-tag size="small" type="info" effect="plain" class="hero-tag">
              {{ onu.terminalType || 'HG8310M' }}
            </el-tag>
          </div>
          <div class="hero-meta-row">
            <span class="meta-item">
              <span class="meta-label">SN:</span>
              <span class="meta-val monospace">{{ onu.sn }}</span>
            </span>
            <span class="meta-divider">|</span>
            <span class="meta-item">
              <span class="meta-label">OLT:</span>
              <span class="meta-val">{{ onu.oltName }} ({{ onu.oltIp }})</span>
            </span>
            <span class="meta-divider">|</span>
            <span class="meta-item">
              <span class="meta-label">PON口:</span>
              <span class="meta-val monospace">{{ onu.portId || onu.neDn }}</span>
            </span>
            <span class="meta-divider">|</span>
            <span class="meta-item">
              <span class="meta-label">终端箱:</span>
              <span class="meta-val">{{ onu.terminalBoxName || '未归属终端箱' }}</span>
            </span>
          </div>
        </div>
        <div class="hero-right">
          <el-tag
            :type="isOnline ? 'success' : (healthStatus === 'power_off' ? 'warning' : 'danger')"
            size="large"
            effect="dark"
            class="status-pill"
          >
            <el-icon v-if="isOnline" style="margin-right: 4px;"><CircleCheck /></el-icon>
            <el-icon v-else-if="healthStatus === 'power_off'" style="margin-right: 4px;"><Lightning /></el-icon>
            <el-icon v-else style="margin-right: 4px;"><CircleClose /></el-icon>
            {{ isOnline ? '光网在线' : (healthStatus === 'power_off' ? '终端箱掉电' : '光纤离线') }}
          </el-tag>
        </div>
      </div>

      <!-- 2. 光功率与链路指标看板 (Top 3 Metric Cards) -->
      <div class="metrics-grid">
        <!-- 卡片 1: 接收光衰 -->
        <div class="metric-card" :class="`border-${opticalHealth.type}`">
          <div class="metric-header">
            <div class="metric-label">
              <el-icon><Odometer /></el-icon>
              <span>接收光功率 (Rx Power)</span>
            </div>
            <el-tag size="small" :type="opticalHealth.type as any" effect="light">
              {{ opticalHealth.badge }}
            </el-tag>
          </div>
          <div class="metric-val monospace" :class="`color-${opticalHealth.type}`">
            {{ opticalHealth.text }}
          </div>
          <div class="metric-footer">
            <span class="muted-sub">安全标准门限: -8.0 ~ -24.0 dBm</span>
          </div>
        </div>

        <!-- 卡片 2: 光纤物理链路 -->
        <div class="metric-card" :class="`border-${fiberLinkStatus.type}`">
          <div class="metric-header">
            <div class="metric-label">
              <el-icon><Connection /></el-icon>
              <span>光纤物理链路</span>
            </div>
            <el-tag size="small" :type="fiberLinkStatus.type as any" effect="light">
              {{ isOnline ? '连通' : '阻断' }}
            </el-tag>
          </div>
          <div class="metric-val" :class="`color-${fiberLinkStatus.type}`" style="font-size: 15px;">
            {{ fiberLinkStatus.text }}
          </div>
          <div class="metric-footer">
            <span class="muted-sub">定界层次: {{ fiberLinkStatus.category }}</span>
          </div>
        </div>

        <!-- 卡片 3: 网管下线原因 -->
        <div class="metric-card" :class="`border-${downCauseParsed.type}`">
          <div class="metric-header">
            <div class="metric-label">
              <el-icon><Warning /></el-icon>
              <span>网管状态与下线原因</span>
            </div>
            <el-tag size="small" :type="downCauseParsed.type as any" effect="light">
              {{ downCauseParsed.code }}
            </el-tag>
          </div>
          <div class="metric-val" :class="`color-${downCauseParsed.type}`" style="font-size: 14px;">
            {{ downCauseParsed.label }}
          </div>
          <div class="metric-footer">
            <span class="muted-sub" :title="downCauseParsed.detail">
              {{ downCauseParsed.detail }}
            </span>
          </div>
        </div>
      </div>

      <!-- 3. 智能排障 SOP 指引横幅 (Actionable Guidance) -->
      <div
        class="sop-banner"
        :style="{ background: sopGuidance.bg, borderColor: sopGuidance.border }"
      >
        <div class="sop-header">
          <div class="sop-title-wrap">
            <el-icon v-if="healthStatus === 'power_off'" style="color: #d97706; font-size: 16px;"><Lightning /></el-icon>
            <el-icon v-else-if="isOnline" style="color: #10b981; font-size: 16px;"><CircleCheck /></el-icon>
            <el-icon v-else style="color: #ef4444; font-size: 16px;"><Warning /></el-icon>
            <span class="sop-title">{{ sopGuidance.title }}</span>
          </div>
          <el-tag size="small" :type="sopGuidance.tagType as any" effect="dark">
            {{ sopGuidance.tag }}
          </el-tag>
        </div>
        <div class="sop-content">
          <div class="sop-steps-label">现场排查指引 (SOP)：</div>
          <ol class="sop-steps-list">
            <li v-for="(step, sIdx) in sopGuidance.steps" :key="sIdx">
              {{ step }}
            </li>
          </ol>
        </div>
      </div>

      <!-- 4. 华为网管全链路实时排障检测项 (Diagnosis Items Table) -->
      <div v-if="diagnosis?.items && diagnosis.items.length > 0" class="section-block">
        <div class="section-title-row">
          <div class="section-title">
            <el-icon><Tools /></el-icon>
            <span>华为网管全链路实时排障检测项</span>
          </div>
          <el-button link type="primary" size="small" :icon="Refresh" :loading="loading" @click="loadDiagnosis">
            重新诊断
          </el-button>
        </div>

        <el-table
          :data="diagnosis.items"
          size="small"
          border
          class="diagnosis-table"
        >
          <el-table-column prop="key" label="检测项目" width="170" />
          <el-table-column label="状态" width="85" align="center">
            <template #default="{ row }">
              <el-tag
                size="small"
                :type="row.status === 0 ? 'success' : (row.status === 1 ? 'warning' : 'danger')"
                effect="light"
              >
                {{ row.status === 0 ? '正常' : (row.status === 1 ? '提示' : '告警') }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column prop="value" label="检测结果 / 读数" width="210">
            <template #default="{ row }">
              <span
                :style="{
                  color: row.status === 0 ? '#10b981' : (row.status === 1 ? '#d97706' : '#ef4444'),
                  fontWeight: '600'
                }"
              >
                {{ row.value || '指标正常' }}
              </span>
            </template>
          </el-table-column>
          <el-table-column prop="suggestion" label="排障说明 / 处置指引" min-width="200">
            <template #default="{ row }">
              <span v-if="row.suggestion" style="color: #475569; font-size: 12px;">
                {{ row.suggestion }}
              </span>
              <span v-else class="muted-sub">指标正常，无需干预</span>
            </template>
          </el-table-column>
        </el-table>
      </div>

      <!-- 5. 下行以太网 LAN 端口面板与对端设备 -->
      <div class="section-block">
        <div class="section-title-row">
          <div class="section-title">
            <el-icon><Connection /></el-icon>
            <span>下行 LAN 端口物理协商与接入设备</span>
          </div>
          <span class="muted-sub" style="font-size: 11.5px;">
            {{ isOnline ? '本地 4 端口已上电握手' : 'ONU 停电或脱网，端口随之关闭' }}
          </span>
        </div>

        <!-- 4 端口网格面板 -->
        <div class="ports-grid">
          <div
            v-for="port in displayPorts"
            :key="port.portId"
            class="port-card"
            :class="{ 'port-up': port.status === 'up' && isOnline }"
          >
            <div class="port-card-top">
              <span class="port-name">{{ port.name }}</span>
              <el-tag
                size="small"
                :type="port.status === 'up' && isOnline ? 'success' : 'info'"
                effect="plain"
                class="port-status-tag"
              >
                {{ port.status === 'up' && isOnline ? 'Up' : 'Down' }}
              </el-tag>
            </div>

            <!-- 连接的设备 -->
            <div class="port-device-info">
              <template v-if="port.deviceName">
                <div class="device-name-row" :title="port.deviceName">
                  <el-icon v-if="port.deviceType === 'camera'" style="color: #3b82f6;"><VideoCamera /></el-icon>
                  <el-icon v-else style="color: #6366f1;"><Monitor /></el-icon>
                  <span class="device-name-text">{{ port.deviceName }}</span>
                </div>
                <div v-if="port.deviceIp" class="device-ip-text monospace">
                  {{ port.deviceIp }}
                </div>
              </template>
              <template v-else>
                <div class="device-empty-text">
                  {{ isOnline ? '空闲备用端口' : '未连接设备' }}
                </div>
              </template>
            </div>

            <!-- 协商速率与双工 -->
            <div class="port-card-foot">
              <span class="port-speed">{{ isOnline && port.status === 'up' ? (port.speed || '1000M') : '--' }}</span>
              <span class="port-duplex">{{ isOnline && port.status === 'up' ? (port.duplex || 'Full') : '--' }}</span>
            </div>
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <div class="dialog-footer">
        <el-button :icon="DocumentCopy" @click="copyDiagnosisReport">
          一键复制排障工单
        </el-button>
        <div style="flex: 1;" />
        <el-button @click="emit('update:modelValue', false)">关闭</el-button>
        <el-button type="primary" :icon="Refresh" :loading="loading" @click="loadDiagnosis">
          刷新实时诊断
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.onu-diagnosis-dialog :deep(.el-dialog__body) {
  padding: 16px 20px 20px;
}

.dialog-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

/* 1. Header Hero Card */
.hero-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
}

.hero-title-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.hero-status-dot {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  display: inline-block;
}
.hero-status-dot.online {
  background: #10b981;
  box-shadow: 0 0 6px rgba(16, 185, 129, 0.6);
}
.hero-status-dot.offline {
  background: #ef4444;
  box-shadow: 0 0 6px rgba(239, 68, 68, 0.6);
}

.hero-title {
  font-weight: 700;
  font-size: 15.5px;
  color: #0f172a;
}

.hero-meta-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: #64748b;
  margin-top: 6px;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 4px;
}
.meta-label {
  color: #94a3b8;
}
.meta-val {
  color: #334155;
  font-weight: 500;
}
.meta-divider {
  color: #cbd5e1;
}

.status-pill {
  font-weight: 600;
  padding: 8px 14px;
  border-radius: 6px;
}

/* 2. Top Metrics Grid */
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}

.metric-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px 14px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.03);
  transition: all 0.2s ease;
}

.metric-card.border-success { border-color: #86efac; background: #f0fdf4; }
.metric-card.border-warning { border-color: #fde68a; background: #fffbeb; }
.metric-card.border-danger  { border-color: #fca5a5; background: #fef2f2; }
.metric-card.border-info    { border-color: #e2e8f0; background: #f8fafc; }

.metric-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.metric-label {
  display: flex;
  align-items: center;
  gap: 5px;
  font-size: 12px;
  font-weight: 600;
  color: #475569;
}

.metric-val {
  font-size: 17px;
  font-weight: 700;
  line-height: 1.3;
  margin-bottom: 6px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.color-success { color: #15803d; }
.color-warning { color: #b45309; }
.color-danger  { color: #b91c1c; }
.color-info    { color: #475569; }

.metric-footer {
  margin-top: auto;
}

.muted-sub {
  font-size: 11px;
  color: #64748b;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  display: block;
}

/* 3. SOP Banner */
.sop-banner {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 12px 16px;
}

.sop-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.sop-title-wrap {
  display: flex;
  align-items: center;
  gap: 6px;
}

.sop-title {
  font-size: 13.5px;
  font-weight: 700;
  color: #0f172a;
}

.sop-steps-label {
  font-size: 12px;
  font-weight: 600;
  color: #334155;
  margin-bottom: 4px;
}

.sop-steps-list {
  margin: 0;
  padding-left: 18px;
  font-size: 12px;
  line-height: 1.6;
  color: #334155;
}

.sop-steps-list li {
  margin-bottom: 2px;
}

/* 4. Section Blocks */
.section-block {
  margin-top: 2px;
}

.section-title-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.section-title {
  font-size: 13px;
  font-weight: 600;
  color: #1e293b;
  display: flex;
  align-items: center;
  gap: 6px;
}

.diagnosis-table {
  border-radius: 6px;
  overflow: hidden;
}

/* 5. LAN Ports Grid */
.ports-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 10px;
}

.port-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  min-height: 85px;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.02);
}

.port-card.port-up {
  border-color: #86efac;
  background: #f0fdf4;
}

.port-card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 6px;
}

.port-name {
  font-size: 12px;
  font-weight: 700;
  color: #1e293b;
}

.port-status-tag {
  font-size: 10px;
  height: 18px;
  padding: 0 4px;
}

.port-device-info {
  margin-bottom: 6px;
  flex: 1;
}

.device-name-row {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 11.5px;
  font-weight: 600;
  color: #0f172a;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.device-name-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.device-ip-text {
  font-size: 10.5px;
  color: #64748b;
  margin-top: 2px;
}

.device-empty-text {
  font-size: 11px;
  color: #94a3b8;
  font-style: italic;
  padding-top: 4px;
}

.port-card-foot {
  display: flex;
  justify-content: space-between;
  font-size: 10.5px;
  color: #64748b;
  padding-top: 4px;
  border-top: 1px dashed #e2e8f0;
}

/* Dialog footer */
.dialog-footer {
  display: flex;
  align-items: center;
  width: 100%;
}

.monospace {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
}
</style>
