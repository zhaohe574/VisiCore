<script setup lang="ts">
import { ref } from 'vue'
import { workflowApi } from '../api'
import { bytes, dateTime } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
const state = ref('')
const { items, total, page, pageSize, search, loading, error, load } = usePaged(workflowApi.exports, () => ({ status: state.value || undefined }), ['export.changed', 'access.changed'])
</script>
<template>
  <div>
    <PageHeader title="导出任务查询" :count="total" description="查看录像导出进度、结果与保留期限；创建、取消、重试及下载请在桌面值守端操作。"><el-button @click="load()">刷新</el-button><router-link to="/"><el-button>获取桌面客户端</el-button></router-link></PageHeader>
    <form class="filter-card filter-bar" @submit.prevent="load(true)">
      <el-input v-model="search" clearable placeholder="搜索通道名称" style="width: 260px;" />
      <el-select v-model="state" clearable placeholder="全部任务状态" style="width: 160px;" @change="load(true)"><el-option label="排队中" value="queued" /><el-option label="执行中" value="running" /><el-option label="已完成" value="completed" /><el-option label="失败" value="failed" /><el-option label="已取消" value="cancelled" /></el-select>
      <el-button type="primary" native-type="submit">查询</el-button>
    </form>
    <el-alert v-if="error" :title="error" type="error" :closable="false" />
    <div class="table-card">
      <el-table :data="items" v-loading="loading" empty-text="暂无授权导出任务">
        <el-table-column prop="channelName" label="通道" min-width="180" />
        <el-table-column label="录像范围" min-width="230"><template #default="{ row }">{{ dateTime(row.start) }}<br />至 {{ dateTime(row.end) }}</template></el-table-column>
        <el-table-column label="状态" width="120"><template #default="{ row }"><StatusBadge :value="row.state" /></template></el-table-column>
        <el-table-column label="进度" min-width="160"><template #default="{ row }"><el-progress :percentage="Math.max(0, Math.min(100, Math.round(row.progress)))" /><small v-if="row.error" class="field-error">{{ row.error }}</small></template></el-table-column>
        <el-table-column label="大小" width="120"><template #default="{ row }">{{ row.fileSize ? bytes(row.fileSize) : '—' }}</template></el-table-column>
        <el-table-column label="保留期限" min-width="160"><template #default="{ row }">{{ dateTime(row.expiresAt) }}</template></el-table-column>
      </el-table>
      <div class="pagination-bar"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" layout="total, sizes, prev, pager, next" @current-change="load()" @size-change="load(true)" /></div>
    </div>
  </div>
</template>
