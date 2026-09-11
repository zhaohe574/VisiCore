<script setup lang="ts">
import { computed, reactive, ref } from 'vue'
import { Delete, Download, Edit, Plus, Refresh, Upload } from '@element-plus/icons-vue'
import { workflowApi, type Release } from '../api'
import { bytes, dateTime } from '../lib/format'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'

interface VersionGroup {
  version: string
  status: 'draft' | 'published' | 'revoked'
  minimumVersion?: string | null
  forceUpdate: boolean
  releaseNotes: string
  publishedAt?: string | null
  createdAt: string
  totalFileSize: number
  totalDownloadCount: number
  msi?: Release
  zip?: Release
  items: Release[]
}

const { busy, run, confirm } = useAction()
const dialog = ref(false), publishDialog = ref(false), editDialog = ref(false)
const selectedGroup = ref<VersionGroup>()
const msiFile = ref<File>(), zipFile = ref<File>()
const form = reactive({ version: '', releaseNotes: '', minimumVersion: '', forceUpdate: false })
const editForm = reactive({ oldVersion: '', version: '', releaseNotes: '', minimumVersion: '', forceUpdate: false, targetId: 0 })
const publishForm = reactive({ minimumVersion: '', forceUpdate: false })

const { items, total, page, pageSize, loading, error, load } = usePaged(workflowApi.releases)

function validVersion(value: string) {
  return /^\d+\.\d+\.\d+(?:\.\d+)?$/.test(value.trim())
}

function compareVersion(v1: string, v2: string): number {
  const p1 = v1.trim().split('.').map(Number)
  const p2 = v2.trim().split('.').map(Number)
  for (let i = 0; i < Math.max(p1.length, p2.length); i++) {
    const n1 = p1[i] || 0
    const n2 = p2[i] || 0
    if (n1 > n2) return 1
    if (n1 < n2) return -1
  }
  return 0
}

const versionGroups = computed<VersionGroup[]>(() => {
  const map = new Map<string, VersionGroup>()
  for (const item of items.value) {
    let group = map.get(item.version)
    if (!group) {
      group = {
        version: item.version,
        status: item.status as any,
        minimumVersion: item.minimumVersion,
        forceUpdate: item.forceUpdate,
        releaseNotes: item.releaseNotes,
        publishedAt: item.publishedAt,
        createdAt: item.createdAt,
        totalFileSize: 0,
        totalDownloadCount: 0,
        items: []
      }
      map.set(item.version, group)
    }
    group.items.push(item)
    group.totalFileSize += item.fileSize
    group.totalDownloadCount += item.downloadCount
    if (item.fileName.toLowerCase().endsWith('.msi')) {
      group.msi = item
      if (item.forceUpdate) group.forceUpdate = true
    } else if (item.fileName.toLowerCase().endsWith('.zip')) {
      group.zip = item
    }
    if (item.status === 'published') group.status = 'published'
    else if (item.status === 'revoked' && group.status !== 'published') group.status = 'revoked'
    if (item.publishedAt && (!group.publishedAt || item.publishedAt > group.publishedAt)) {
      group.publishedAt = item.publishedAt
    }
  }
  return Array.from(map.values())
})

function pickMsi(event: Event) {
  const f = (event.target as HTMLInputElement).files?.[0]
  if (f && !f.name.toLowerCase().endsWith('.msi')) {
    msiFile.value = undefined
    throw new Error('请选择扩展名为 .msi 的安装包文件')
  }
  msiFile.value = f
}

function pickZip(event: Event) {
  const f = (event.target as HTMLInputElement).files?.[0]
  if (f && !f.name.toLowerCase().endsWith('.zip')) {
    zipFile.value = undefined
    throw new Error('请选择扩展名为 .zip 的便携包文件')
  }
  zipFile.value = f
}

function openUpload() {
  Object.assign(form, { version: '', releaseNotes: '', minimumVersion: '', forceUpdate: false })
  msiFile.value = undefined
  zipFile.value = undefined
  dialog.value = true
}

async function upload() {
  if (await run(async () => {
    if (!msiFile.value) throw new Error('请选择 MSI 安装包')
    if (!msiFile.value.name.toLowerCase().endsWith('.msi')) throw new Error('MSI 安装包扩展名必须为 .msi')
    if (!zipFile.value) throw new Error('请选择 ZIP 便携包')
    if (!zipFile.value.name.toLowerCase().endsWith('.zip')) throw new Error('ZIP 便携包扩展名必须为 .zip')

    const v = form.version.trim()
    if (!v) throw new Error('请输入版本号')
    if (!validVersion(v)) throw new Error('请输入有效的数字版本号，例如 2.0.4')
    if (versionGroups.value.some(g => g.version === v)) throw new Error(`版本 ${v} 已存在，不能重复上传`)

    const minV = form.minimumVersion.trim()
    if (minV) {
      if (!validVersion(minV)) throw new Error('最低支持版本号格式无效')
      if (compareVersion(minV, v) > 0) throw new Error('最低支持版本不能高于当前版本号')
    }

    const data = new FormData()
    data.append('msiFile', msiFile.value)
    data.append('zipFile', zipFile.value)
    data.append('version', v)
    data.append('releaseNotes', form.releaseNotes)
    data.append('minimumVersion', minV)
    data.append('forceUpdate', String(form.forceUpdate))

    await workflowApi.uploadRelease(data)
    await load(true)
  }, '版本安装包（MSI 与 ZIP）已联合上传为草稿')) {
    dialog.value = false
  }
}

function openEdit(group: VersionGroup) {
  selectedGroup.value = group
  Object.assign(editForm, {
    oldVersion: group.version,
    version: group.version,
    releaseNotes: group.releaseNotes || '',
    minimumVersion: group.minimumVersion || '',
    forceUpdate: group.forceUpdate,
    targetId: group.items[0]?.id || 0
  })
  editDialog.value = true
}

async function updateDraft() {
  if (await run(async () => {
    const v = editForm.version.trim()
    if (!v) throw new Error('请输入版本号')
    if (!validVersion(v)) throw new Error('请输入有效的数字版本号，例如 2.0.4')
    if (v !== editForm.oldVersion && versionGroups.value.some(g => g.version === v)) {
      throw new Error(`版本 ${v} 已存在`)
    }

    const minV = editForm.minimumVersion.trim()
    if (minV) {
      if (!validVersion(minV)) throw new Error('最低支持版本号格式无效')
      if (compareVersion(minV, v) > 0) throw new Error('最低支持版本不能高于当前版本号')
    }

    await workflowApi.updateRelease(editForm.targetId, {
      version: v,
      minimumVersion: minV,
      forceUpdate: editForm.forceUpdate,
      releaseNotes: editForm.releaseNotes
    })
    await load()
  }, '未发布版本已更新')) {
    editDialog.value = false
  }
}

async function removeDraft(group: VersionGroup) {
  await confirm(`确定删除未发布版本 ${group.version}？此操作将同时删除其关联的 MSI 和 ZIP 安装包文件且无法撤销。`, async () => {
    await workflowApi.deleteReleaseVersion(group.version)
    await load()
  }, '未发布版本及关联文件已删除')
}

function openPublish(group: VersionGroup) {
  selectedGroup.value = group
  Object.assign(publishForm, {
    minimumVersion: group.minimumVersion || '',
    forceUpdate: group.forceUpdate
  })
  publishDialog.value = true
}

async function publish() {
  if (!selectedGroup.value) return
  if (await run(async () => {
    const minV = publishForm.minimumVersion.trim()
    if (minV) {
      if (!validVersion(minV)) throw new Error('最低版本号格式无效')
      if (compareVersion(minV, selectedGroup.value!.version) > 0) throw new Error('最低版本号不能高于当前版本号')
    }
    await workflowApi.publishVersion(selectedGroup.value!.version, minV, publishForm.forceUpdate)
    await load()
  }, '版本已正式发布')) {
    publishDialog.value = false
  }
}

async function revoke(group: VersionGroup) {
  await confirm(`撤回版本 ${group.version}？撤回后客户端将无法获取该版本。`, async () => {
    for (const item of group.items) {
      if (item.status === 'published') await workflowApi.revokeRelease(item.id)
    }
    await load()
  }, '版本已撤回')
}
</script>

<template>
  <div>
    <PageHeader title="版本发布" :count="versionGroups.length">
      <el-button :icon="Refresh" :loading="loading" @click="load()">刷新</el-button>
      <el-button type="primary" :icon="Plus" @click="openUpload">新建版本（上传MSI与ZIP）</el-button>
    </PageHeader>

    <el-alert v-if="error" :title="error" type="error" :closable="false" />

    <el-table v-loading="loading" :data="versionGroups" empty-text="暂无客户端版本">
      <el-table-column type="expand">
        <template #default="{ row }">
          <div class="expanded-release">
            <h3>版本更新说明</h3>
            <p class="preserve-lines">{{ row.releaseNotes || '未提供更新说明' }}</p>
            <div class="package-details-grid">
              <div v-if="row.msi" class="pkg-detail-box">
                <h4><el-icon><Download /></el-icon> MSI 安装包</h4>
                <dl class="data-list">
                  <div><dt>文件名</dt><dd>{{ row.msi.fileName }}</dd></div>
                  <div><dt>大小</dt><dd>{{ bytes(row.msi.fileSize) }}</dd></div>
                  <div><dt>SHA-256</dt><dd class="hash">{{ row.msi.sha256 }}</dd></div>
                  <div><dt>下载地址</dt><dd><a :href="workflowApi.releaseUrl(row.msi.id)" class="download-link">点击下载</a></dd></div>
                </dl>
              </div>
              <div v-if="row.zip" class="pkg-detail-box">
                <h4><el-icon><Download /></el-icon> ZIP 便携包</h4>
                <dl class="data-list">
                  <div><dt>文件名</dt><dd>{{ row.zip.fileName }}</dd></div>
                  <div><dt>大小</dt><dd>{{ bytes(row.zip.fileSize) }}</dd></div>
                  <div><dt>SHA-256</dt><dd class="hash">{{ row.zip.sha256 }}</dd></div>
                  <div><dt>下载地址</dt><dd><a :href="workflowApi.releaseUrl(row.zip.id)" class="download-link">点击下载</a></dd></div>
                </dl>
              </div>
            </div>
          </div>
        </template>
      </el-table-column>

      <el-table-column prop="version" label="版本" min-width="110" />
      <el-table-column label="状态" width="110">
        <template #default="{ row }">
          <StatusBadge :value="row.status" />
        </template>
      </el-table-column>

      <el-table-column label="安装包" min-width="180">
        <template #default="{ row }">
          <div class="tag-list">
            <el-tag v-if="row.msi" type="primary" size="small">MSI ({{ bytes(row.msi.fileSize) }})</el-tag>
            <el-tag v-else type="info" size="small">缺少 MSI</el-tag>
            <el-tag v-if="row.zip" type="success" size="small">ZIP ({{ bytes(row.zip.fileSize) }})</el-tag>
            <el-tag v-else type="info" size="small">缺少 ZIP</el-tag>
          </div>
        </template>
      </el-table-column>

      <el-table-column prop="minimumVersion" label="最低版本" min-width="100">
        <template #default="{ row }">{{ row.minimumVersion || '未限制' }}</template>
      </el-table-column>
      <el-table-column label="强制更新" width="100">
        <template #default="{ row }">{{ row.forceUpdate ? '是' : '否' }}</template>
      </el-table-column>
      <el-table-column prop="totalDownloadCount" label="下载次数" width="100" />
      <el-table-column label="发布时间" min-width="168">
        <template #default="{ row }">{{ row.publishedAt ? dateTime(row.publishedAt) : '未发布' }}</template>
      </el-table-column>

      <el-table-column label="操作" width="220" fixed="right">
        <template #default="{ row }">
          <div class="table-tools">
            <template v-if="row.status === 'draft'">
              <el-button link type="primary" :disabled="busy" @click="openPublish(row as VersionGroup)">发布</el-button>
              <el-button link type="primary" :icon="Edit" :disabled="busy" @click="openEdit(row as VersionGroup)">修改</el-button>
              <el-button link type="danger" :icon="Delete" :disabled="busy" @click="removeDraft(row as VersionGroup)">删除</el-button>
            </template>
            <template v-else-if="row.status === 'published'">
              <el-button link type="danger" :disabled="busy" @click="revoke(row as VersionGroup)">撤回</el-button>
            </template>
            <template v-else>
              <span class="muted" style="font-size: 12px;">已撤回</span>
            </template>

            <el-tooltip v-if="row.msi" content="下载 MSI 安装包">
              <a :href="workflowApi.releaseUrl(row.msi.id)" class="icon-link" aria-label="下载 MSI 安装包"><el-icon><Download /></el-icon></a>
            </el-tooltip>
            <el-tooltip v-if="row.zip" content="下载 ZIP 便携包">
              <a :href="workflowApi.releaseUrl(row.zip.id)" class="icon-link" aria-label="下载 ZIP 便携包"><el-icon><Download /></el-icon></a>
            </el-tooltip>
          </div>
        </template>
      </el-table-column>
    </el-table>

    <div class="pagination">
      <el-pagination
        v-model:current-page="page"
        v-model:page-size="pageSize"
        :total="total"
        :page-sizes="[20, 50, 100]"
        layout="total, sizes, prev, pager, next"
        @current-change="load()"
        @size-change="load(true)"
      />
    </div>

    <!-- 上传弹窗：同时选择并上传 MSI 和 ZIP -->
    <el-dialog v-model="dialog" title="新建客户端版本（联合上传 MSI 与 ZIP）" width="600px" destroy-on-close :close-on-click-modal="!busy" :show-close="!busy">
      <el-form label-position="top">
        <el-form-item label="MSI 安装包 (.msi)" required>
          <input class="file-input" type="file" accept=".msi" :disabled="busy" aria-label="MSI 安装包" @change="pickMsi" />
          <span v-if="msiFile" class="muted">{{ msiFile.name }} · {{ bytes(msiFile.size) }}</span>
        </el-form-item>

        <el-form-item label="ZIP 便携包 (.zip)" required>
          <input class="file-input" type="file" accept=".zip" :disabled="busy" aria-label="ZIP 便携包" @change="pickZip" />
          <span v-if="zipFile" class="muted">{{ zipFile.name }} · {{ bytes(zipFile.size) }}</span>
        </el-form-item>

        <div class="form-grid">
          <el-form-item label="版本号" required>
            <el-input v-model="form.version" placeholder="例如 2.0.4" />
          </el-form-item>
          <el-form-item label="最低支持版本">
            <el-input v-model="form.minimumVersion" placeholder="例如 2.0.0" />
          </el-form-item>
        </div>

        <el-form-item label="版本更新说明">
          <el-input v-model="form.releaseNotes" type="textarea" :rows="4" maxlength="4096" placeholder="输入本版本的更新日志与特性说明..." />
        </el-form-item>

        <el-form-item label="强制更新（仅对旧版生效）">
          <el-switch v-model="form.forceUpdate" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="busy" @click="dialog = false">取消</el-button>
        <el-button type="primary" :icon="Upload" :loading="busy" @click="upload">同时上传为草稿</el-button>
      </template>
    </el-dialog>

    <!-- 修改未发布草稿弹窗 -->
    <el-dialog v-model="editDialog" :title="`修改未发布版本 ${editForm.oldVersion}`" width="560px" destroy-on-close :close-on-click-modal="!busy" :show-close="!busy">
      <el-form label-position="top">
        <div class="form-grid">
          <el-form-item label="版本号" required>
            <el-input v-model="editForm.version" placeholder="例如 2.0.4" />
          </el-form-item>
          <el-form-item label="最低支持版本">
            <el-input v-model="editForm.minimumVersion" placeholder="例如 2.0.0" />
          </el-form-item>
        </div>

        <el-form-item label="版本更新说明">
          <el-input v-model="editForm.releaseNotes" type="textarea" :rows="5" maxlength="4096" placeholder="输入本版本的更新日志与特性说明..." />
        </el-form-item>

        <el-form-item label="强制更新">
          <el-switch v-model="editForm.forceUpdate" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button :disabled="busy" @click="editDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="updateDraft">保存修改</el-button>
      </template>
    </el-dialog>

    <!-- 发布弹窗 -->
    <el-dialog v-model="publishDialog" :title="`发布版本 ${selectedGroup?.version || ''}`" width="460px">
      <el-form label-position="top">
        <el-form-item label="最低支持版本">
          <el-input v-model="publishForm.minimumVersion" placeholder="例如 2.0.0" />
        </el-form-item>
        <el-form-item label="强制更新">
          <el-switch v-model="publishForm.forceUpdate" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="publishDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="publish">确认发布全包</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.package-details-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 16px;
  margin-top: 14px;
}
.pkg-detail-box {
  background: #f8fafb;
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 14px;
}
.pkg-detail-box h4 {
  margin: 0 0 10px;
  font-size: 13px;
  display: flex;
  align-items: center;
  gap: 6px;
}
@media (max-width: 767px) {
  .package-details-grid {
    grid-template-columns: 1fr;
  }
}
</style>
