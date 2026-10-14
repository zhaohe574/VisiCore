<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { managementApi, type Channel, type Organization } from '../../api'
import { buildUnitCascaderOptions, getUnitArea, getUnitName } from '../../lib/organization'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  channels: Channel[]
  organization: Organization
  initialUnitId?: number | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved'): void
}>()

const { busy, run } = useAction()
const assignTo = ref<number | null>(null)

const unitCascaderOptions = computed(() => buildUnitCascaderOptions(props.organization))

watch(
  () => props.modelValue,
  (visible) => {
    if (visible) assignTo.value = props.initialUnitId ?? null
  }
)

async function assign() {
  const success = await run(async () => {
    await managementApi.assign(props.channels.map(c => c.id), assignTo.value ?? null)
  }, '通道分配已成功更新')

  if (success) {
    emit('update:modelValue', false)
    emit('saved')
  }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="批量划拨通道至单元"
    width="480px"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <el-form-item label="目标单元（支持车间 / 区域层级下钻与名称搜索）">
        <el-cascader
          v-model="assignTo"
          :options="unitCascaderOptions"
          :props="{ emitPath: false, checkStrictly: false }"
          clearable
          filterable
          placeholder="请选择划拨目标单元（留空解除划拨）"
          style="width: 100%;"
        />
      </el-form-item>
      <div v-if="assignTo" style="margin-bottom: 12px; font-size: 13px; display: flex; gap: 16px; align-items: center;">
        <span>归属区域: <strong>{{ getUnitArea(organization, assignTo)?.name || '—' }}</strong></span>
        <span>划拨单元: <el-tag type="success" size="small">{{ getUnitName(organization, assignTo) }}</el-tag></span>
      </div>
      <div v-else style="margin-bottom: 12px; font-size: 13px;">
        <el-tag type="warning" size="small">未选择单元：若点击确认，将解除所选通道的单元关联</el-tag>
      </div>
    </el-form>
    <p class="muted" style="font-size: 13px; margin: 0;">
      本次操作将批量更新所勾选的 <strong>{{ channels.length }}</strong> 路通道的归属单元。
    </p>
    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="assign">确认划拨</el-button>
    </template>
  </el-dialog>
</template>

