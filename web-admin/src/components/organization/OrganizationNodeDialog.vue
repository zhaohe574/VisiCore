<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { managementApi, type Organization, type OrganizationKind, type OrganizationNode } from '../../api'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  kind: OrganizationKind
  editingNode?: OrganizationNode | null
  parents?: OrganizationNode[]
  organization?: Organization
  initialParentId?: number | null
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved', node?: OrganizationNode): void
}>()

const { busy, run } = useAction()

const names: Record<OrganizationKind, string> = { workshops: '车间', areas: '区域', units: '单元' }

const form = reactive({
  name: '',
  code: '',
  status: 'active',
  parentId: null as number | null
})

// 计算可用的上级组织选项
const availableParents = computed<OrganizationNode[]>(() => {
  if (props.kind === 'workshops') return []
  if (props.organization) {
    if (props.kind === 'areas') {
      return props.organization.workshops || []
    }
    if (props.kind === 'units') {
      return props.organization.areas || []
    }
  }
  return props.parents || []
})

function getParentLabel(parent: OrganizationNode): string {
  if (props.kind === 'units' && props.organization?.workshops) {
    const ws = props.organization.workshops.find(w => w.id === parent.parentId)
    return ws ? `${parent.name}（${ws.name}）` : parent.name
  }
  return parent.name
}

watch(
  () => props.modelValue,
  (visible) => {
    if (visible) {
      if (props.editingNode) {
        form.name = props.editingNode.name || ''
        form.code = props.editingNode.code || ''
        form.status = props.editingNode.status || 'active'
        form.parentId = props.editingNode.parentId ?? null
      } else {
        form.name = ''
        form.code = ''
        form.status = 'active'
        form.parentId = props.initialParentId ?? null
      }
    }
  }
)

async function save() {
  const success = await run(async () => {
    if (!form.name.trim() || !form.code.trim()) throw new Error('请填写组织名称和编码')
    if (props.kind !== 'workshops' && !form.parentId) throw new Error(`请选择所属上级${props.kind === 'areas' ? '车间' : '区域'}`)
    await managementApi.saveNode(props.kind, props.editingNode?.id ?? null, {
      ...form,
      name: form.name.trim(),
      code: form.code.trim(),
      parentId: props.kind === 'workshops' ? null : form.parentId
    })
  }, '组织节点已保存')

  if (success) {
    emit('update:modelValue', false)
    emit('saved')
  }
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="`${editingNode ? '编辑' : '新增'}${names[kind]}`"
    width="480px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top">
      <el-form-item :label="`${names[kind]}名称`" required>
        <el-input
          v-model="form.name"
          maxlength="100"
          :placeholder="kind === 'workshops' ? '例如：总装一号车间' : kind === 'areas' ? '例如：冲压焊接区' : '例如：质检包装单元'"
          clearable
        />
      </el-form-item>

      <el-form-item label="编码标识" required>
        <el-input
          v-model="form.code"
          maxlength="64"
          :placeholder="kind === 'workshops' ? '例如：WS-01' : kind === 'areas' ? '例如：AR-101' : '例如：UN-1001'"
          clearable
        />
      </el-form-item>

      <el-form-item v-if="kind !== 'workshops'" :label="`所属上级${kind === 'areas' ? '车间' : '区域'}`" required>
        <el-select
          v-model="form.parentId"
          filterable
          :placeholder="`选择上级${kind === 'areas' ? '车间' : '区域'}`"
          style="width: 100%;"
        >
          <el-option
            v-for="parent in availableParents"
            :key="parent.id"
            :value="parent.id"
            :label="getParentLabel(parent)"
          />
        </el-select>
      </el-form-item>

      <el-form-item label="组织启用状态">
        <el-switch
          v-model="form.status"
          active-value="active"
          inactive-value="disabled"
          active-text="正常启用"
          inactive-text="停用"
        />
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="save">确认保存</el-button>
    </template>
  </el-dialog>
</template>

