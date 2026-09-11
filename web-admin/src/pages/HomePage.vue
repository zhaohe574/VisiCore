<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ArrowRight, Download, Refresh } from '@element-plus/icons-vue'
import { workflowApi, type PublicRelease } from '../api'
import { releasePackage } from '../lib/releases'
import { bytes, dateTime, errorMessage, safeDownloadUrl } from '../lib/format'
const release = ref<PublicRelease>(), releasesHistory = ref<PublicRelease[]>([]), loading = ref(true), error = ref('')
const msi = computed(() => releasePackage(release.value, 'msi')), zip = computed(() => releasePackage(release.value, 'zip'))
const productImage = ref('/product-workspace.png'), productReady = ref(false)
function productUnavailable() { productReady.value = false; productImage.value = '/visicore.svg' }
const displayHistory = computed(() => {
  if (releasesHistory.value && releasesHistory.value.length > 0) return releasesHistory.value
  if (release.value) return [release.value]
  return []
})
async function load() {
  loading.value = true; error.value = ''
  try {
    const [latestRes, historyRes] = await Promise.allSettled([
      workflowApi.latest(),
      workflowApi.publicReleases()
    ])
    if (latestRes.status === 'fulfilled') release.value = latestRes.value
    if (historyRes.status === 'fulfilled' && Array.isArray(historyRes.value)) releasesHistory.value = historyRes.value
  } catch (e) {
    error.value = errorMessage(e)
  } finally {
    loading.value = false
  }
}
onMounted(() => void load())
</script>
<template>
  <main class="public-page"><header class="public-header"><router-link class="public-brand" to="/"><img src="/visicore.svg" alt="VisiCore（视枢）标识" /><strong>VisiCore（视枢）</strong></router-link><router-link class="public-admin" to="/login">管理入口<el-icon><ArrowRight /></el-icon></router-link></header>
    <section :class="['public-hero', { 'has-product': productReady }]"><img :class="['public-product', { 'product-ready': productReady }]" :src="productImage" :alt="productReady ? 'VisiCore（视枢）管理工作区实况预览' : 'VisiCore（视枢）标识'" @load="productReady = productImage.endsWith('product-workspace.png')" @error="productUnavailable" /><div class="public-hero-copy"><p>视枢 · 视频值守</p><h1>VisiCore（视枢）</h1><p class="public-summary">实时视频、录像回放与报警处理</p><div class="public-hero-actions"><a v-if="msi" class="primary-link" :href="safeDownloadUrl(msi.downloadUrl, workflowApi.releaseUrl(msi.id))"><el-icon><Download /></el-icon>MSI 安装版</a><el-button v-else type="primary" size="large" disabled :loading="loading">{{ loading ? '正在获取版本' : error ? '版本暂不可用' : 'MSI 尚未发布' }}</el-button><a v-if="zip" class="secondary-link" :href="safeDownloadUrl(zip.downloadUrl, workflowApi.releaseUrl(zip.id))"><el-icon><Download /></el-icon>ZIP 便携版</a><el-button v-else size="large" disabled>ZIP 尚未发布</el-button><router-link to="/login">进入管理工作区<el-icon><ArrowRight /></el-icon></router-link></div><p v-if="productReady" class="muted">VisiCore（视枢）智能视频值守与管理工作区实况预览</p><span v-if="release" class="public-version">{{ release.version }} · Windows x64 · {{ bytes(release.fileSize) }}</span></div></section>
    <section class="public-release"><div><h2>客户端下载</h2><p class="muted">Windows x64</p></div><el-alert v-if="error" :title="error" type="error" :closable="false" show-icon /><el-button v-if="error" :icon="Refresh" @click="load">重新获取</el-button><template v-if="release"><dl class="release-specs"><div><dt>当前版本</dt><dd>{{ release.version }}</dd></div><div><dt>发布时间</dt><dd>{{ dateTime(release.publishedAt) }}</dd></div><div><dt>安装文件</dt><dd>{{ release.fileName }}</dd></div><div><dt>最低版本</dt><dd>{{ release.minimumVersion || '未限制' }}</dd></div></dl><div class="public-packages"><article v-for="item in [msi, zip].filter(Boolean)" :key="item!.id"><div><h3>{{ item!.fileName.toLowerCase().endsWith('.msi') ? 'MSI 安装版' : 'ZIP 便携版' }}</h3><p>{{ item!.fileName }} · {{ bytes(item!.fileSize) }}</p></div><a :href="safeDownloadUrl(item!.downloadUrl, workflowApi.releaseUrl(item!.id))" class="download-link"><el-icon><Download /></el-icon>下载</a><details><summary>文件校验值（SHA-256）</summary><code class="hash">{{ item!.sha256 }}</code></details></article></div><h3>版本说明</h3><p class="preserve-lines">{{ release.releaseNotes || '本版本未提供更新说明。' }}</p></template><el-empty v-else-if="!loading && !error" description="暂无已发布版本" :image-size="72" /></section>
    <section class="public-changelog"><div class="changelog-header"><h2>版本更新日志</h2><p class="muted">查看客户端版本演进与功能更新记录</p></div><div v-if="displayHistory.length" class="changelog-timeline"><article v-for="(item, idx) in displayHistory" :key="item.version" class="changelog-card"><header class="changelog-card-header"><div class="changelog-tag"><span class="version-name">v{{ item.version }}</span><el-tag v-if="idx === 0" type="success" size="small" effect="plain">最新版本</el-tag><el-tag v-if="item.forceUpdate" type="warning" size="small" effect="plain">强制升级</el-tag></div><time class="changelog-date">{{ dateTime(item.publishedAt) }}</time></header><div class="changelog-body"><p class="preserve-lines notes-content">{{ item.releaseNotes || '本版本未提供更新说明。' }}</p></div><footer v-if="item.packages && item.packages.length" class="changelog-downloads"><span class="downloads-label">安装包：</span><div class="downloads-chips"><a v-for="pkg in item.packages" :key="pkg.id" :href="safeDownloadUrl(pkg.downloadUrl, workflowApi.releaseUrl(pkg.id))" class="package-chip" :title="`下载 ${pkg.fileName} (${bytes(pkg.fileSize)})`"><el-icon><Download /></el-icon><span>{{ pkg.fileName.toLowerCase().endsWith('.msi') ? 'MSI 安装版' : 'ZIP 便携版' }} ({{ bytes(pkg.fileSize) }})</span></a></div></footer></article></div><el-empty v-else-if="!loading && !error" description="暂无版本更新日志" :image-size="72" /></section>
    <footer class="public-footer">VisiCore（视枢） <span>2.0.0</span></footer>
  </main>
</template>
