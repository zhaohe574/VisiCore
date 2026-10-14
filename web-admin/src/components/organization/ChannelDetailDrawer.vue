<script setup lang="ts">
import { computed } from 'vue'
import { useRouter } from 'vue-router'
import type { Channel, Device, Organization } from '../../api'
import { getUnitArea, getUnitName, getUnitWorkshop } from '../../lib/organization'

const props = defineProps<{
  modelValue: boolean
  channel: Channel | null
  organization: Organization
  devices: Device[]
  canAssign: boolean
}>()
const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'edit', channel: Channel): void
}>()
const router = useRouter()
const device = computed(() => props.devices.find(item => item.id === props.channel?.deviceId))
const unit = computed(() => getUnitName(props.organization, props.channel?.unitId))
const area = computed(() => getUnitArea(props.organization, props.channel?.unitId)?.name || '未分配')
const workshop = computed(() => getUnitWorkshop(props.organization, props.channel?.unitId)?.name || '未分配')
</script>

<template>
  <el-drawer :model-value="modelValue" title="通道详细信息" size="560px" @update:model-value="emit('update:modelValue', $event)">
    <template v-if="channel">
      <h3>{{ channel.alias || channel.name }}</h3>
      <el-descriptions :column="1" border>
        <el-descriptions-item label="原始名称">{{ channel.name }}</el-descriptions-item>
        <el-descriptions-item label="运行状态">{{ channel.status === 'online' ? '在线' : '离线' }}</el-descriptions-item>
        <el-descriptions-item label="所属车间">{{ workshop }}</el-descriptions-item>
        <el-descriptions-item label="所属区域">{{ area }}</el-descriptions-item>
        <el-descriptions-item label="所属单元">{{ unit || '未分配' }}</el-descriptions-item>
        <el-descriptions-item label="录像机">{{ device?.name || channel.deviceName }}</el-descriptions-item>
        <el-descriptions-item label="设备通道号">{{ channel.deviceChannel }}</el-descriptions-item>
        <el-descriptions-item label="摄像头型号">{{ channel.model || '未知' }}</el-descriptions-item>
        <el-descriptions-item label="摄像头 IP">{{ channel.ip || '未配置' }}</el-descriptions-item>
        <el-descriptions-item label="独立访问账号">{{ channel.username || '未配置' }}</el-descriptions-item>
        <el-descriptions-item label="访问密码">{{ channel.hasPassword ? '已配置（不会回显）' : '未配置' }}</el-descriptions-item>
        <el-descriptions-item label="视频编码">{{ channel.codec || '未知' }}</el-descriptions-item>
        <el-descriptions-item label="云台控制">{{ channel.ptzCapable ? '支持' : '不支持' }}</el-descriptions-item>
        <el-descriptions-item label="终端箱">
          <el-button v-if="channel.terminalBoxId" link type="primary" @click="router.push({ path: '/app/terminal-boxes', query: { boxId: channel.terminalBoxId } })">{{ channel.terminalBoxName || channel.terminalBoxCode }}</el-button>
          <span v-else>未绑定</span>
        </el-descriptions-item>
        <el-descriptions-item label="承载 ONU">
          <el-button v-if="channel.onuDeviceId" link type="primary" @click="router.push({ path: '/app/onus', query: { search: channel.onuDeviceId } })">{{ channel.onuAlias || channel.onuName }}</el-button>
          <span v-else>未绑定</span>
        </el-descriptions-item>
        <el-descriptions-item label="网络端口">{{ channel.portLabel || channel.networkPort || '未绑定' }}</el-descriptions-item>
        <el-descriptions-item label="ONU 光衰">{{ channel.onuRxPower == null ? '未知' : `${channel.onuRxPower} dBm` }}</el-descriptions-item>
        <el-descriptions-item label="光网健康">{{ channel.onuHealthStatus || '未知' }}</el-descriptions-item>
        <el-descriptions-item label="故障原因">{{ channel.onuRootCause || '无' }}</el-descriptions-item>
        <el-descriptions-item label="备注">{{ channel.remark || '无' }}</el-descriptions-item>
      </el-descriptions>
      <el-button v-if="canAssign" type="primary" style="margin-top: 20px;" @click="emit('edit', channel)">编辑通道与网络绑定</el-button>
    </template>
    <el-empty v-else description="请选择通道" />
  </el-drawer>
</template>
