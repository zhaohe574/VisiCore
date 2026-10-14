<script setup lang="ts">
import { computed, reactive, watch } from 'vue'
import { CircleCheck } from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { ponApi, type Organization, type TerminalBoxDto } from '../../api'
import { buildAreaCascaderOptions } from '../../lib/organization'
import { useAction } from '../../composables/useAction'

const props = defineProps<{
  modelValue: boolean
  box: TerminalBoxDto | null
  organization: Organization
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: boolean): void
  (e: 'saved', box: TerminalBoxDto): void
}>()

const { busy, run } = useAction()

const form = reactive({
  code: '',
  name: '',
  location: '',
  areaId: null as number | null,
  remarks: ''
})

const areaCascaderOptions = computed(() => buildAreaCascaderOptions(props.organization))

watch(
  () => props.modelValue,
  (visible) => {
    if (visible) {
      if (props.box) {
        form.code = props.box.code
        form.name = props.box.name
        form.location = props.box.location || ''
        form.areaId = props.box.areaId ?? null
        form.remarks = props.box.remarks || ''
      } else {
        form.code = ''
        form.name = ''
        form.location = ''
        form.areaId = null
        form.remarks = ''
      }
    }
  }
)

async function submit() {
  if (!form.code.trim()) {
    ElMessage.warning('请输入终端箱编号')
    return
  }
  if (!form.name.trim()) {
    ElMessage.warning('请输入终端箱名称')
    return
  }

  await run(async () => {
    const saved = await ponApi.saveTerminalBox(props.box?.id || null, {
      code: form.code.trim(),
      name: form.name.trim(),
      location: form.location.trim() || null,
      areaId: form.areaId,
      remarks: form.remarks.trim() || null
    })
    ElMessage.success(`网络终端箱【${saved.name}】已成功保存`)
    emit('saved', saved)
    emit('update:modelValue', false)
  }, '保存终端箱')
}
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    :title="box ? '编辑网络终端箱台账' : '新增网络终端箱台账'"
    width="520px"
    destroy-on-close
    @update:model-value="emit('update:modelValue', $event)"
  >
    <el-form label-position="top" :disabled="busy">
      <div style="display: grid; grid-template-columns: 1fr 1.5fr; gap: 12px;">
        <el-form-item label="终端箱编号" required>
          <el-input
            v-model="form.code"
            placeholder="如：TB-01"
            maxlength="50"
            clearable
          />
        </el-form-item>
        <el-form-item label="终端箱名称" required>
          <el-input
            v-model="form.name"
            placeholder="如：1号车间东门网络箱"
            maxlength="100"
            clearable
          />
        </el-form-item>
      </div>

      <el-form-item label="归属业务区域">
        <el-cascader
          v-model="form.areaId"
          :options="areaCascaderOptions"
          :props="{ emitPath: false, checkStrictly: false }"
          clearable
          filterable
          placeholder="选择所属车间/区域（选填）"
          style="width: 100%;"
        />
        <div class="form-tip">终端箱仅需归属到车间/区域，区域内多个箱体通过物理安装位置区分。</div>
      </el-form-item>

      <el-form-item label="物理安装位置">
        <el-input
          v-model="form.location"
          placeholder="如：1号动力车间东侧配电柱#3 离地2.5米（用于同区域内精准辨识）"
          maxlength="200"
          clearable
        />
      </el-form-item>

      <el-form-item label="备注说明">
        <el-input
          v-model="form.remarks"
          type="textarea"
          :rows="2"
          maxlength="300"
          show-word-limit
          placeholder="可记录箱体钥匙责任人、供电空开回路等信息..."
        />
      </el-form-item>
    </el-form>

    <template #footer>
      <el-button :disabled="busy" @click="emit('update:modelValue', false)">取消</el-button>
      <el-button type="primary" :loading="busy" @click="submit">
        <el-icon style="margin-right: 4px;"><CircleCheck /></el-icon>
        保存终端箱
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.form-tip {
  margin-top: 4px;
  font-size: 12px;
  color: var(--text-muted, #64748b);
  line-height: 1.4;
}
</style>

