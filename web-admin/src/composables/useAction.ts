import { ref } from 'vue'
import { ElMessage, ElMessageBox } from 'element-plus'
import { errorMessage } from '../lib/format'
export function useAction() {
  const busy = ref(false)
  async function run(action: () => Promise<unknown>, success = '操作成功') {
    if (busy.value) return false
    busy.value = true
    try { await action(); if (success) ElMessage.success(success); return true }
    catch (error) { ElMessage.error(errorMessage(error)); return false }
    finally { busy.value = false }
  }
  async function confirm(message: string, action: () => Promise<unknown>, success?: string) {
    try { await ElMessageBox.confirm(message, '确认操作', { type: 'warning', confirmButtonText: '确认', cancelButtonText: '取消' }) }
    catch { return false }
    return run(action, success)
  }
  return { busy, run, confirm }
}
