<script setup lang="ts">
import { computed } from 'vue'
const props = defineProps<{ value?: string | null }>()
const labels: Record<string, string> = { online: '在线', offline: '离线', active: '启用', disabled: '停用', locked: '锁定', new: '待处理', processing: '处理中', closed: '已关闭', queued: '排队中', running: '执行中', completed: '已完成', failed: '失败', cancelled: '已取消', published: '已发布', draft: '草稿', revoked: '已撤回', starting: '连接中', playing: '播放中', paused: '已暂停', stopped: '已停止', gap: '录像缺口', healthy: '正常', degraded: '异常', connected: '已连接', disconnected: '未连接', reconnecting: '重新连接', connecting: '连接中', enabled: '启用', up: '运行中', ok: '正常', unknown: '未知' }
const tone = computed(() => ['online', 'active', 'published', 'completed', 'healthy', 'connected', 'playing', 'enabled', 'ok', 'up'].includes(props.value || '') ? 'good' : ['failed', 'offline', 'degraded', 'new'].includes(props.value || '') ? 'danger' : ['processing', 'running', 'starting', 'connecting', 'reconnecting', 'queued', 'gap'].includes(props.value || '') ? 'warning' : 'neutral')
</script>
<template><span :class="['status-badge', tone]"><i />{{ labels[value || ''] || value || '未知' }}</span></template>
