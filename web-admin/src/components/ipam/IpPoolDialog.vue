<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { ElMessage } from 'element-plus'
import { ipPoolApi, type IpPool, type IpPoolInput } from '../../api'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  pool?: IpPool | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', val: boolean): void
  (e: 'saved', pool?: IpPool): void
}>()

const { busy, run } = useAction()

const form = reactive<IpPoolInput>({
  name: '',
  cidr: '',
  startIp: '',
  endIp: '',
  gateway: '',
  netmask: '255.255.255.0',
  vlanId: null,
  description: '',
  autoScan: true
})

// 计算子网推演信息
const subnetPreview = computed(() => {
  if (!form.cidr?.trim()) return null
  try {
    const parts = form.cidr.trim().split('/')
    if (parts.length !== 2) return null
    const ipStr = parts[0]
    const prefix = parseInt(parts[1], 10)
    if (isNaN(prefix) || prefix < 16 || prefix > 30) return null

    const ipParts = ipStr.split('.').map(Number)
    if (ipParts.length !== 4 || ipParts.some(p => isNaN(p) || p < 0 || p > 255)) return null

    const ipUint = ((ipParts[0] << 24) >>> 0) + (ipParts[1] << 16) + (ipParts[2] << 8) + ipParts[3]
    const mask = prefix === 0 ? 0 : ((0xFFFFFFFF << (32 - prefix)) >>> 0)
    const netUint = (ipUint & mask) >>> 0
    const bcastUint = (netUint | (~mask >>> 0)) >>> 0

    function uintToIp(u: number) {
      return `${(u >>> 24) & 255}.${(u >>> 16) & 255}.${(u >>> 8) & 255}.${u & 255}`
    }

    const network = uintToIp(netUint)
    const broadcast = uintToIp(bcastUint)
    const netmask = uintToIp(mask)
    const start = uintToIp(netUint + 1)
    const end = uintToIp(bcastUint - 1)
    const total = bcastUint - netUint + 1
    const usable = Math.max(0, total - 2)

    return {
      network,
      broadcast,
      netmask,
      start,
      end,
      total,
      usable,
      prefix
    }
  } catch {
    return null
  }
})

// 当 CIDR 变化时自动填充起止 IP、掩码与网关默认值
watch(subnetPreview, (preview) => {
  if (preview) {
    form.netmask = preview.netmask
    if (!props.pool) {
      form.startIp = preview.start
      form.endIp = preview.end
      if (!form.gateway) form.gateway = preview.start
    }
  }
})

watch(
  () => props.modelValue,
  (visible) => {
    if (visible) {
      if (props.pool) {
        form.name = props.pool.name
        form.cidr = props.pool.cidr
        form.startIp = props.pool.startIp
        form.endIp = props.pool.endIp
        form.gateway = props.pool.gateway || ''
        form.netmask = props.pool.netmask
        form.vlanId = props.pool.vlanId ?? null
        form.description = props.pool.description || ''
        form.autoScan = props.pool.autoScan
      } else {
        form.name = ''
        form.cidr = '10.37.20.0/24'
        form.startIp = '10.37.20.1'
        form.endIp = '10.37.20.254'
        form.gateway = '10.37.20.1'
        form.netmask = '255.255.255.0'
        form.vlanId = null
        form.description = ''
        form.autoScan = true
      }
    }
  }
)

async function submit() {
  const success = await run(async () => {
    if (!form.name.trim()) throw new Error('请输入号段名称')
    if (!form.cidr?.trim()) throw new Error('请输入 CIDR 地址段')

    const payload: IpPoolInput = {
      name: form.name.trim(),
      cidr: form.cidr.trim(),
      startIp: form.startIp?.trim() || null,
      endIp: form.endIp?.trim() || null,
      gateway: form.gateway?.trim() || null,
      netmask: form.netmask?.trim() || '255.255.255.0',
      vlanId: form.vlanId,
      description: form.description?.trim() || null,
      autoScan: form.autoScan
    }

    const saved = await ipPoolApi.save(props.pool?.id || null, payload)
    emit('saved', saved)
  }, props.pool ? '号段已更新' : '号段已添加')

  if (success) {
    emit('update:modelValue', false)
  }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="pool ? '编辑 IP 号段' : '新增 IP 号段'"
    width="580px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <el-form-item label="号段名称" required>
        <el-input
          v-model="form.name"
          placeholder="例如：办公楼接入设备网段、中维录像机网段等"
          clearable
        />
      </el-form-item>

      <el-form-item label="CIDR 网络地址段 (支持 /22, /24 等大网段)" required>
        <el-input
          v-model="form.cidr"
          placeholder="例如：10.37.20.0/22 或 10.37.24.0/24"
          clearable
        />
        <div v-if="subnetPreview" class="cidr-preview-tip">
          <span>子网掩码：<b>{{ subnetPreview.netmask }}</b></span>
          <span>容量：<b>{{ subnetPreview.total }}</b> 个地址 (可用 <b>{{ subnetPreview.usable }}</b>)</span>
          <span>广播：<b>{{ subnetPreview.broadcast }}</b></span>
        </div>
      </el-form-item>

      <el-row :gutter="16">
        <el-col :span="12">
          <el-form-item label="网关 IP">
            <el-input
              v-model="form.gateway"
              placeholder="例如：10.37.20.1"
              clearable
            />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="VLAN 标识 (选填)">
            <el-input-number
              v-model="form.vlanId"
              :min="1"
              :max="4094"
              placeholder="例如：20"
              style="width: 100%;"
            />
          </el-form-item>
        </el-col>
      </el-row>

      <el-row :gutter="16">
        <el-col :span="12">
          <el-form-item label="分配起始 IP">
            <el-input
              v-model="form.startIp"
              placeholder="例如：10.37.20.1"
            />
          </el-form-item>
        </el-col>
        <el-col :span="12">
          <el-form-item label="分配结束 IP">
            <el-input
              v-model="form.endIp"
              placeholder="例如：10.37.23.254"
            />
          </el-form-item>
        </el-col>
      </el-row>

      <el-form-item label="用途说明与备注">
        <el-input
          v-model="form.description"
          type="textarea"
          :rows="2"
          placeholder="说明此网段承载的业务设备与部署范围..."
        />
      </el-form-item>

      <el-form-item label="自动后台巡检">
        <el-switch
          v-model="form.autoScan"
          active-text="纳入后台定时自动 Ping 巡检"
          inactive-text="仅手动触发扫描"
        />
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="submit">确认保存</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.cidr-preview-tip {
  margin-top: 6px;
  padding: 6px 10px;
  background: var(--el-fill-color-light);
  border-radius: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}
.cidr-preview-tip b {
  color: var(--el-color-primary);
}
</style>
