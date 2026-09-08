<script setup lang="ts">
import { ref } from 'vue'
import { Refresh, Search } from '@element-plus/icons-vue'
import { managementApi } from '../api'
import { dateTime } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import PageHeader from '../components/PageHeader.vue'
const range = ref<[Date, Date] | null>(null), action = ref('')
const { items, total, page, pageSize, search, loading, error, load } = usePaged(managementApi.audit, () => ({ action: action.value, from: range.value?.[0].toISOString(), to: range.value?.[1].toISOString() }))
</script>
<template><div><PageHeader title="操作审计" :count="total"><el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button></PageHeader><form class="filter-bar" @submit.prevent="load(true)"><el-input v-model="search" clearable :prefix-icon="Search" placeholder="账号、资源或摘要" aria-label="搜索审计" /><el-input v-model="action" clearable placeholder="操作类型" aria-label="操作类型" /><el-date-picker v-model="range" type="datetimerange" start-placeholder="开始时间" end-placeholder="结束时间" format="YYYY-MM-DD HH:mm" /><el-button :icon="Search" native-type="submit">查询</el-button></form><el-alert v-if="error" :title="error" type="error" :closable="false" /><el-table v-loading="loading" :data="items" empty-text="暂无审计记录"><el-table-column label="时间" min-width="170"><template #default="{ row }">{{ dateTime(row.createdAt) }}</template></el-table-column><el-table-column prop="username" label="账号" min-width="110" /><el-table-column prop="action" label="操作" min-width="150" show-overflow-tooltip /><el-table-column prop="resource" label="资源" min-width="130" show-overflow-tooltip /><el-table-column prop="summary" label="摘要" min-width="260" show-overflow-tooltip /><el-table-column prop="clientIp" label="来源地址" min-width="140" /></el-table><div class="pagination"><el-pagination v-model:current-page="page" v-model:page-size="pageSize" :total="total" :page-sizes="[20,50,100]" layout="total, sizes, prev, pager, next" @current-change="load()" @size-change="load(true)" /></div></div></template>
