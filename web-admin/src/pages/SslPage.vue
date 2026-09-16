<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import {
  Check,
  CopyDocument,
  Delete,
  Document,
  Download,
  Edit,
  InfoFilled,
  Key,
  Link,
  Lock,
  Plus,
  Refresh,
  Search,
  Star,
  StarFilled,
  TopRight,
  UploadFilled,
  WarningFilled
} from '@element-plus/icons-vue'
import { ElMessage, ElMessageBox, type UploadFile, type UploadUserFile } from 'element-plus'
import {
  sslApi,
  type NginxConfig,
  type SslCertificate,
  type SslCertificateUploadInput,
  type SslDomain,
  type SslDomainInput,
  type SslOverview
} from '../api'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import { errorMessage } from '../lib/format'
import PageHeader from '../components/PageHeader.vue'

const auth = useAuth()
const { busy, run, confirm } = useAction()

const activeTab = ref<'domains' | 'certs' | 'nginx'>('domains')
const loading = ref(false)
const overview = ref<SslOverview | null>(null)
const domains = ref<SslDomain[]>([])
const certificates = ref<SslCertificate[]>([])
const nginxConfig = ref<NginxConfig | null>(null)

const domainSearch = ref('')
const certSearch = ref('')

// 网址表单弹窗
const domainDialogOpen = ref(false)
const domainEditingId = ref<number | null>(null)
const domainForm = ref<SslDomainInput>({
  domain: '',
  port: 443,
  protocol: 'https',
  description: '',
  isPrimary: false,
  forceHttps: true,
  hstsEnabled: true,
  certificateId: null,
  enabled: true
})

// 证书上传弹窗
const certDialogOpen = ref(false)
const certUploadTab = ref<'file' | 'paste'>('file')
const certFileList = ref<UploadUserFile[]>([])
const keyFileList = ref<UploadUserFile[]>([])
const certPasteForm = ref<SslCertificateUploadInput>({
  name: '',
  certPem: '',
  keyPem: ''
})

// 证书详情抽屉
const detailDrawerOpen = ref(false)
const viewingCert = ref<SslCertificate | null>(null)
const loadingDetail = ref(false)

// 过滤后的网址列表
const filteredDomains = computed(() => {
  const q = domainSearch.value.trim().toLowerCase()
  if (!q) return domains.value
  return domains.value.filter(d =>
    d.domain.toLowerCase().includes(q) ||
    d.description.toLowerCase().includes(q) ||
    (d.certificateName && d.certificateName.toLowerCase().includes(q))
  )
})

// 过滤后的证书列表
const filteredCerts = computed(() => {
  const q = certSearch.value.trim().toLowerCase()
  if (!q) return certificates.value
  return certificates.value.filter(c =>
    c.name.toLowerCase().includes(q) ||
    c.commonName.toLowerCase().includes(q) ||
    c.dnsNames.some(d => d.toLowerCase().includes(q))
  )
})

async function loadAll() {
  loading.value = true
  try {
    const [ov, dList, cList] = await Promise.all([
      sslApi.overview(),
      sslApi.domains(),
      sslApi.certificates()
    ])
    overview.value = ov
    domains.value = dList
    certificates.value = cList
  } catch (e) {
    ElMessage.error(errorMessage(e))
  } finally {
    loading.value = false
  }
}

async function loadNginxConfig() {
  try {
    nginxConfig.value = await sslApi.nginxConfig()
  } catch (e) {
    ElMessage.error(errorMessage(e))
  }
}

// 网址管理操作
function openCreateDomain() {
  domainEditingId.value = null
  domainForm.value = {
    domain: '',
    port: 443,
    protocol: 'https',
    description: '',
    isPrimary: domains.value.length === 0,
    forceHttps: true,
    hstsEnabled: true,
    certificateId: overview.value?.activeCertificate?.id || null,
    enabled: true
  }
  domainDialogOpen.value = true
}

function openEditDomain(d: SslDomain) {
  domainEditingId.value = d.id
  domainForm.value = {
    domain: d.domain,
    port: d.port,
    protocol: d.protocol,
    description: d.description,
    isPrimary: d.isPrimary,
    forceHttps: d.forceHttps,
    hstsEnabled: d.hstsEnabled,
    certificateId: d.certificateId || null,
    enabled: d.enabled
  }
  domainDialogOpen.value = true
}

async function submitDomain() {
  if (!domainForm.value.domain.trim()) {
    ElMessage.warning('请输入域名或 IP 访问地址')
    return
  }
  await run(async () => {
    await sslApi.saveDomain(domainEditingId.value, domainForm.value)
    domainDialogOpen.value = false
    await loadAll()
  }, domainEditingId.value ? '网址配置已更新' : '已成功添加网址')
}

async function setPrimary(d: SslDomain) {
  if (d.isPrimary) return
  await run(async () => {
    await sslApi.setPrimaryDomain(d.id)
    await loadAll()
  }, `已将 ${d.domain} 设为主访问网址`)
}

async function removeDomain(d: SslDomain) {
  await confirm(`确定删除受控网址 ${d.domain} 吗？`, async () => {
    await sslApi.deleteDomain(d.id)
    await loadAll()
  }, '受控网址已删除')
}

// 证书管理操作
function openUploadCert() {
  certFileList.value = []
  keyFileList.value = []
  certPasteForm.value = {
    name: '',
    certPem: '',
    keyPem: ''
  }
  certDialogOpen.value = true
}

async function readFileText(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(reader.result as string)
    reader.onerror = reject
    reader.readAsText(file, 'utf-8')
  })
}

async function submitCert() {
  let name = ''
  let certPem = ''
  let keyPem = ''

  if (certUploadTab.value === 'file') {
    const certFile = certFileList.value[0]?.raw
    const keyFile = keyFileList.value[0]?.raw
    if (!certFile) {
      ElMessage.warning('请选择证书公钥文件 (.crt / .cer / .pem)')
      return
    }
    if (!keyFile) {
      ElMessage.warning('请选择私钥文件 (.key / .pem)')
      return
    }
    name = certPasteForm.value.name.trim() || certFile.name.replace(/\.[^/.]+$/, '')
    try {
      certPem = await readFileText(certFile)
      keyPem = await readFileText(keyFile)
    } catch {
      ElMessage.error('读取证书或私钥文件失败')
      return
    }
  } else {
    name = certPasteForm.value.name.trim()
    certPem = certPasteForm.value.certPem.trim()
    keyPem = certPasteForm.value.keyPem.trim()
    if (!name) {
      ElMessage.warning('请输入证书备注名称')
      return
    }
    if (!certPem) {
      ElMessage.warning('请粘贴证书内容 (CRT/PEM)')
      return
    }
    if (!keyPem) {
      ElMessage.warning('请粘贴私钥内容 (KEY)')
      return
    }
  }

  await run(async () => {
    await sslApi.uploadCertificate({ name, certPem, keyPem })
    certDialogOpen.value = false
    await loadAll()
  }, 'SSL 证书已成功导入并解析')
}

async function activateCert(c: SslCertificate) {
  if (c.isActive) return
  await confirm(`确定将证书 [${c.name}] (${c.commonName}) 设为系统当前生效的 SSL 证书吗？系统将自动同步更新服务器证书文件。`, async () => {
    await sslApi.setActiveCertificate(c.id)
    await loadAll()
  }, 'SSL 证书已切换生效并落盘')
}

async function viewCertDetail(c: SslCertificate) {
  loadingDetail.value = true
  detailDrawerOpen.value = true
  try {
    viewingCert.value = await sslApi.certificate(c.id)
  } catch (e) {
    ElMessage.error(errorMessage(e))
  } finally {
    loadingDetail.value = false
  }
}

function downloadCert(c: SslCertificate) {
  window.open(sslApi.downloadCertUrl(c.id), '_blank')
}

async function removeCert(c: SslCertificate) {
  if (c.isActive) {
    ElMessage.warning('当前生效的证书不能删除')
    return
  }
  await confirm(`确定删除证书 [${c.name}] (${c.commonName}) 吗？`, async () => {
    await sslApi.deleteCertificate(c.id)
    await loadAll()
  }, 'SSL 证书已删除')
}

function copyText(text: string, tip = '已复制到剪贴板') {
  navigator.clipboard.writeText(text).then(() => {
    ElMessage.success(tip)
  }).catch(() => {
    ElMessage.error('复制失败，请手动复制')
  })
}

onMounted(() => {
  void loadAll()
})
</script>

<template>
  <div class="ssl-page">
    <PageHeader title="SSL与域名管控">
      <el-button :icon="Refresh" :loading="loading" @click="loadAll">重新加载</el-button>
      <el-button v-if="auth.can('ssl.manage')" type="primary" :icon="Plus" @click="openCreateDomain">添加网址</el-button>
      <el-button v-if="auth.can('ssl.manage')" type="success" :icon="UploadFilled" @click="openUploadCert">上传证书</el-button>
    </PageHeader>

    <!-- 到期/异常风险提示 -->
    <el-alert
      v-if="overview?.activeCertificate?.status === 'expired'"
      title="当前生效的 SSL 证书已过期！浏览器和客户端将出现安全阻断，请立即上传并激活新证书。"
      type="error"
      :closable="false"
      show-icon
      class="notice-bar"
    />
    <el-alert
      v-else-if="overview?.activeCertificate?.status === 'expiring_soon'"
      :title="`当前生效的 SSL 证书将于 ${overview.activeCertificate.daysRemaining} 天后到期（${overview.activeCertificate.validTo.slice(0, 10)}），请及时更新证书！`"
      type="warning"
      :closable="false"
      show-icon
      class="notice-bar"
    />

    <!-- 总览统计卡片 -->
    <div class="overview-cards">
      <div class="kpi-card">
        <div class="kpi-icon ssl-icon"><el-icon><Lock /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">当前运行 SSL 证书</div>
          <div class="kpi-value">
            <template v-if="overview?.activeCertificate">
              <span class="cert-title" :title="overview.activeCertificate.name">{{ overview.activeCertificate.name }}</span>
              <el-tag
                :type="overview.activeCertificate.status === 'valid' ? 'success' : overview.activeCertificate.status === 'expiring_soon' ? 'warning' : 'danger'"
                size="small"
                effect="dark"
              >
                {{ overview.activeCertificate.status === 'valid' ? `剩余 ${overview.activeCertificate.daysRemaining} 天` : overview.activeCertificate.status === 'expiring_soon' ? `即将到期 (${overview.activeCertificate.daysRemaining}天)` : '已过期' }}
              </el-tag>
            </template>
            <span v-else class="text-muted">未激活任何证书</span>
          </div>
          <div class="kpi-desc">
            主域名：{{ overview?.activeCertificate?.commonName || '未配置' }}
            <span v-if="overview?.fileSynced" class="sync-badge ok">✓ 磁盘证书已同步</span>
            <span v-else-if="overview?.activeCertificate" class="sync-badge pending">! 文件待落盘</span>
          </div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon domain-icon"><el-icon><Link /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">主访问网址</div>
          <div class="kpi-value">
            <template v-if="overview?.primaryDomain">
              <span class="domain-title">{{ overview.primaryDomain.protocol }}://{{ overview.primaryDomain.domain }}:{{ overview.primaryDomain.port }}</span>
              <el-tag type="primary" size="small">主入口</el-tag>
            </template>
            <span v-else class="text-muted">未配置默认入口</span>
          </div>
          <div class="kpi-desc">
            强制 HTTPS: {{ overview?.httpsEnforced ? '全部启用' : '部分配置' }} · 受控网址共 {{ overview?.totalDomains || 0 }} 个
          </div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon cert-count-icon"><el-icon><Key /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">证书储备与安全</div>
          <div class="kpi-value">
            <span class="count-number">{{ overview?.totalCertificates || 0 }}</span>
            <span class="count-unit">张证书</span>
            <el-tag v-if="overview && overview.expiringCertificates > 0" type="danger" size="small">
              {{ overview.expiringCertificates }} 张预警
            </el-tag>
          </div>
          <div class="kpi-desc">
            支持 RSA/ECC 证书 · SAN 多域名覆盖探测
          </div>
        </div>
      </div>
    </div>

    <!-- 主选项卡 -->
    <el-tabs v-model="activeTab" class="ssl-tabs" @tab-change="t => { if (t === 'nginx' && !nginxConfig) void loadNginxConfig() }">
      <!-- 网址与访问管理 -->
      <el-tab-pane label="网址与访问管理" name="domains">
        <div class="table-toolbar">
          <el-input
            v-model="domainSearch"
            placeholder="搜索网址、IP 或备注..."
            :prefix-icon="Search"
            clearable
            style="max-width: 320px;"
          />
          <el-button v-if="auth.can('ssl.manage')" type="primary" :icon="Plus" @click="openCreateDomain">添加受控网址</el-button>
        </div>

        <el-table v-loading="loading" :data="filteredDomains" row-key="id" stripe style="width: 100%">
          <el-table-column label="受控访问网址" min-width="220">
            <template #default="{ row }">
              <div class="domain-cell">
                <span class="domain-text">
                  <el-tag :type="row.protocol === 'https' ? 'success' : 'info'" size="small" class="proto-tag">
                    {{ row.protocol.toUpperCase() }}
                  </el-tag>
                  <strong>{{ row.domain }}</strong>
                  <span v-if="row.port !== 80 && row.port !== 443" class="port-tag">:{{ row.port }}</span>
                </span>
                <el-tag v-if="row.isPrimary" type="warning" size="small" effect="dark" class="primary-tag">
                  <el-icon><StarFilled /></el-icon> 主网址
                </el-tag>
              </div>
            </template>
          </el-table-column>

          <el-table-column prop="description" label="备注说明" min-width="160">
            <template #default="{ row }">
              <span>{{ row.description || '-' }}</span>
            </template>
          </el-table-column>

          <el-table-column label="关联 SSL 证书" min-width="180">
            <template #default="{ row }">
              <div v-if="row.certificateName" class="bound-cert-info">
                <span class="cert-name">{{ row.certificateName }}</span>
                <small class="cert-cn text-muted">({{ row.certificateCommonName }})</small>
              </div>
              <span v-else class="text-muted">未绑定（使用默认活动证书）</span>
            </template>
          </el-table-column>

          <el-table-column label="证书匹配状态" width="160">
            <template #default="{ row }">
              <el-tag v-if="row.certMatchStatus === 'matched'" type="success" size="small">
                ✓ 证书匹配
              </el-tag>
              <el-tooltip v-else-if="row.certMatchStatus === 'mismatched'" content="当前域名的主机名不在证书 SAN/CN 列表中，访问可能提示证书不匹配！">
                <el-tag type="danger" size="small">
                  <el-icon><WarningFilled /></el-icon> 域名不匹配
                </el-tag>
              </el-tooltip>
              <el-tag v-else type="info" size="small">未绑定</el-tag>
            </template>
          </el-table-column>

          <el-table-column label="强制 HTTPS" width="110" align="center">
            <template #default="{ row }">
              <el-tag :type="row.forceHttps ? 'success' : 'info'" size="small">
                {{ row.forceHttps ? '已开启' : '关闭' }}
              </el-tag>
            </template>
          </el-table-column>

          <el-table-column label="HSTS" width="100" align="center">
            <template #default="{ row }">
              <el-tag :type="row.hstsEnabled ? 'success' : 'info'" size="small">
                {{ row.hstsEnabled ? '已开启' : '关闭' }}
              </el-tag>
            </template>
          </el-table-column>

          <el-table-column label="状态" width="90" align="center">
            <template #default="{ row }">
              <el-tag :type="row.enabled ? 'success' : 'danger'" size="small">
                {{ row.enabled ? '正常' : '禁用' }}
              </el-tag>
            </template>
          </el-table-column>

          <el-table-column label="操作" width="220" fixed="right">
            <template #default="{ row }">
              <div class="action-group">
                <el-button
                  v-if="!row.isPrimary && auth.can('ssl.manage')"
                  text
                  size="small"
                  type="warning"
                  :icon="Star"
                  @click="setPrimary(row as SslDomain)"
                >
                  设为主网址
                </el-button>
                <el-button
                  v-if="auth.can('ssl.manage')"
                  text
                  size="small"
                  type="primary"
                  :icon="Edit"
                  @click="openEditDomain(row as SslDomain)"
                >
                  编辑
                </el-button>
                <el-button
                  text
                  size="small"
                  :icon="CopyDocument"
                  @click="copyText(`${row.protocol}://${row.domain}${row.port === 80 || row.port === 443 ? '' : ':' + row.port}`, '已复制访问链接')"
                >
                  复制
                </el-button>
                <el-button
                  v-if="auth.can('ssl.manage')"
                  text
                  size="small"
                  type="danger"
                  :icon="Delete"
                  @click="removeDomain(row as SslDomain)"
                >
                  删除
                </el-button>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- SSL 证书管理 -->
      <el-tab-pane label="SSL 证书管理" name="certs">
        <div class="table-toolbar">
          <el-input
            v-model="certSearch"
            placeholder="搜索证书名称、主域名或 SAN..."
            :prefix-icon="Search"
            clearable
            style="max-width: 320px;"
          />
          <el-button v-if="auth.can('ssl.manage')" type="success" :icon="UploadFilled" @click="openUploadCert">上传/导入证书</el-button>
        </div>

        <el-table v-loading="loading" :data="filteredCerts" row-key="id" stripe style="width: 100%">
          <el-table-column label="证书名称" min-width="180">
            <template #default="{ row }">
              <div class="cert-name-cell">
                <strong>{{ row.name }}</strong>
                <el-tag v-if="row.isActive" type="success" effect="dark" size="small" class="active-badge">
                  <el-icon><StarFilled /></el-icon> 当前生效
                </el-tag>
              </div>
            </template>
          </el-table-column>

          <el-table-column prop="commonName" label="主域名 (CN)" min-width="160" />

          <el-table-column label="覆盖备用域名 (SANs)" min-width="220">
            <template #default="{ row }">
              <div class="san-tags">
                <el-tag
                  v-for="san in row.dnsNames.slice(0, 3)"
                  :key="san"
                  size="small"
                  class="san-tag"
                >
                  {{ san }}
                </el-tag>
                <el-tooltip v-if="row.dnsNames.length > 3" :content="row.dnsNames.join(', ')">
                  <el-tag size="small" type="info">+{{ row.dnsNames.length - 3 }}</el-tag>
                </el-tooltip>
                <span v-if="row.dnsNames.length === 0" class="text-muted">无</span>
              </div>
            </template>
          </el-table-column>

          <el-table-column prop="issuerDn" label="颁发机构" min-width="180" show-overflow-tooltip />

          <el-table-column label="有效期" width="220">
            <template #default="{ row }">
              <div class="validity-cell">
                <span>{{ row.validFrom.slice(0, 10) }} 至 {{ row.validTo.slice(0, 10) }}</span>
                <div>
                  <el-tag
                    :type="row.status === 'valid' ? 'success' : row.status === 'expiring_soon' ? 'warning' : 'danger'"
                    size="small"
                  >
                    {{ row.status === 'valid' ? `剩余 ${row.daysRemaining} 天` : row.status === 'expiring_soon' ? `即将到期 (${row.daysRemaining}天)` : '已过期' }}
                  </el-tag>
                </div>
              </div>
            </template>
          </el-table-column>

          <el-table-column label="绑定网址" width="90" align="center">
            <template #default="{ row }">
              <el-tag type="info" size="small">{{ row.boundDomainCount }} 个</el-tag>
            </template>
          </el-table-column>

          <el-table-column label="操作" width="240" fixed="right">
            <template #default="{ row }">
              <div class="action-group">
                <el-button
                  v-if="!row.isActive && auth.can('ssl.manage')"
                  text
                  size="small"
                  type="success"
                  :icon="Check"
                  @click="activateCert(row as SslCertificate)"
                >
                  设为生效
                </el-button>
                <el-button
                  text
                  size="small"
                  type="primary"
                  :icon="InfoFilled"
                  @click="viewCertDetail(row as SslCertificate)"
                >
                  详情
                </el-button>
                <el-button
                  text
                  size="small"
                  :icon="Download"
                  @click="downloadCert(row as SslCertificate)"
                >
                  公钥
                </el-button>
                <el-button
                  v-if="!row.isActive && auth.can('ssl.manage')"
                  text
                  size="small"
                  type="danger"
                  :icon="Delete"
                  @click="removeCert(row as SslCertificate)"
                >
                  删除
                </el-button>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </el-tab-pane>

      <!-- 服务与部署配置 -->
      <el-tab-pane label="服务与部署配置" name="nginx">
        <div class="nginx-section">
          <div class="nginx-card">
            <h3>服务器证书落盘信息</h3>
            <el-descriptions :column="1" border size="small">
              <el-descriptions-item label="SSL 证书公钥路径 (CRT)">
                <code>{{ overview?.certFilePath }}</code>
              </el-descriptions-item>
              <el-descriptions-item label="SSL 私钥路径 (KEY)">
                <code>{{ overview?.keyFilePath }}</code>
              </el-descriptions-item>
              <el-descriptions-item label="落盘同步状态">
                <span v-if="overview?.fileSynced" class="sync-badge ok">✓ 磁盘文件与当前生效证书 SHA256 指纹一致</span>
                <span v-else class="sync-badge pending">! 磁盘文件尚未同步，请将任意证书设为生效以触发写入</span>
              </el-descriptions-item>
              <el-descriptions-item label="Nginx 重载命令">
                <div class="code-line">
                  <code>{{ nginxConfig?.reloadCommand || 'sudo nginx -t && sudo systemctl reload nginx' }}</code>
                  <el-button size="small" text :icon="CopyDocument" @click="copyText(nginxConfig?.reloadCommand || 'sudo nginx -t && sudo systemctl reload nginx')">复制</el-button>
                </div>
              </el-descriptions-item>
            </el-descriptions>
          </div>

          <div class="nginx-card">
            <div class="card-header-flex">
              <div>
                <h3>动态生成 Nginx 站点配置</h3>
                <small class="text-muted">根据后台配置的受控网址与 SSL 证书自动生成标准反向代理与强安全配置。</small>
              </div>
              <el-button
                v-if="nginxConfig"
                type="primary"
                :icon="CopyDocument"
                @click="copyText(nginxConfig.content, 'Nginx 配置已复制到剪贴板')"
              >
                一键复制 Nginx 配置
              </el-button>
            </div>
            <pre class="nginx-code"><code>{{ nginxConfig?.content || '正在生成配置...' }}</code></pre>
          </div>
        </div>
      </el-tab-pane>
    </el-tabs>

    <!-- 网址管理弹窗 -->
    <el-dialog
      v-model="domainDialogOpen"
      :title="domainEditingId ? '编辑受控网址' : '添加受控网址'"
      width="560px"
      destroy-on-close
    >
      <el-form label-position="top">
        <el-form-item label="访问域名或主机名 / IP 地址" required>
          <el-input
            v-model="domainForm.domain"
            placeholder="例如：video.example.com 或 192.168.1.100"
            clearable
          />
          <small class="form-hint">无需填写 http://、https:// 或端口，在下方单独特供。</small>
        </el-form-item>

        <div class="form-row-2">
          <el-form-item label="访问协议" required>
            <el-select v-model="domainForm.protocol" style="width: 100%;">
              <el-option label="HTTPS（推荐加密）" value="https" />
              <el-option label="HTTP（未加密）" value="http" />
            </el-select>
          </el-form-item>

          <el-form-item label="端口" required>
            <el-input-number v-model="domainForm.port" :min="1" :max="65535" style="width: 100%;" />
          </el-form-item>
        </div>

        <el-form-item label="绑定 SSL 证书">
          <el-select v-model="domainForm.certificateId" placeholder="选择关联的 SSL 证书（可选）" clearable style="width: 100%;">
            <el-option
              v-for="c in certificates"
              :key="c.id"
              :label="`${c.name} (${c.commonName})`"
              :value="c.id"
            />
          </el-select>
          <small class="form-hint">未指定证书时将默认采用系统当前生效的全局 SSL 证书。</small>
        </el-form-item>

        <el-form-item label="备注说明">
          <el-input v-model="domainForm.description" placeholder="例如：总部内网主访问入口、专网映射域名" />
        </el-form-item>

        <div class="switches-grid">
          <el-form-item label="设为主访问网址">
            <el-switch v-model="domainForm.isPrimary" />
          </el-form-item>
          <el-form-item label="强制 HTTP 重定向至 HTTPS">
            <el-switch v-model="domainForm.forceHttps" />
          </el-form-item>
          <el-form-item label="开启 HSTS 严格安全传输">
            <el-switch v-model="domainForm.hstsEnabled" />
          </el-form-item>
          <el-form-item label="启用该网址">
            <el-switch v-model="domainForm.enabled" />
          </el-form-item>
        </div>
      </el-form>

      <template #footer>
        <el-button @click="domainDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="submitDomain">保存</el-button>
      </template>
    </el-dialog>

    <!-- 证书上传/导入弹窗 -->
    <el-dialog
      v-model="certDialogOpen"
      title="上传 / 导入 SSL 证书"
      width="640px"
      destroy-on-close
    >
      <el-tabs v-model="certUploadTab">
        <el-tab-pane label="文件上传" name="file">
          <el-form label-position="top">
            <el-form-item label="证书备注名称">
              <el-input v-model="certPasteForm.name" placeholder="留空时自动以证书文件名命名" />
            </el-form-item>

            <el-form-item label="证书公钥文件 (.crt / .cer / .pem)" required>
              <el-upload
                v-model:file-list="certFileList"
                action="#"
                :auto-upload="false"
                :limit="1"
                accept=".crt,.cer,.pem"
                class="upload-box"
              >
                <el-button :icon="UploadFilled">选择证书文件</el-button>
                <template #tip>
                  <div class="el-upload__tip">包含完备证书链的 fullchain.pem 或 server.crt</div>
                </template>
              </el-upload>
            </el-form-item>

            <el-form-item label="证书私钥文件 (.key / .pem)" required>
              <el-upload
                v-model:file-list="keyFileList"
                action="#"
                :auto-upload="false"
                :limit="1"
                accept=".key,.pem"
                class="upload-box"
              >
                <el-button :icon="UploadFilled">选择私钥文件</el-button>
                <template #tip>
                  <div class="el-upload__tip">与公钥匹配的 RSA / ECC 私钥文件 server.key</div>
                </template>
              </el-upload>
            </el-form-item>
          </el-form>
        </el-tab-pane>

        <el-tab-pane label="直接文本粘贴" name="paste">
          <el-form label-position="top">
            <el-form-item label="证书备注名称" required>
              <el-input v-model="certPasteForm.name" placeholder="例如：2026 通配符域名证书" />
            </el-form-item>

            <el-form-item label="证书公钥内容 (PEM)" required>
              <el-input
                v-model="certPasteForm.certPem"
                type="textarea"
                :rows="5"
                placeholder="-----BEGIN CERTIFICATE-----&#10;...&#10;-----END CERTIFICATE-----"
                font-family="monospace"
              />
            </el-form-item>

            <el-form-item label="私钥内容 (KEY)" required>
              <el-input
                v-model="certPasteForm.keyPem"
                type="textarea"
                :rows="5"
                placeholder="-----BEGIN RSA PRIVATE KEY----- 或 -----BEGIN PRIVATE KEY-----&#10;...&#10;-----END PRIVATE KEY-----"
                font-family="monospace"
              />
            </el-form-item>
          </el-form>
        </el-tab-pane>
      </el-tabs>

      <template #footer>
        <el-button @click="certDialogOpen = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="submitCert">导入并解析证书</el-button>
      </template>
    </el-dialog>

    <!-- 证书详情抽屉 -->
    <el-drawer
      v-model="detailDrawerOpen"
      title="SSL 证书详情"
      size="560px"
    >
      <div v-loading="loadingDetail">
        <template v-if="viewingCert">
          <el-descriptions :column="1" border size="small" class="detail-descriptions">
            <el-descriptions-item label="证书名称">
              <strong>{{ viewingCert.name }}</strong>
            </el-descriptions-item>
            <el-descriptions-item label="当前生效">
              <el-tag :type="viewingCert.isActive ? 'success' : 'info'" size="small">
                {{ viewingCert.isActive ? '当前生效' : '未激活' }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="主域名 (CN)">
              {{ viewingCert.commonName }}
            </el-descriptions-item>
            <el-descriptions-item label="覆盖备用域名 (SANs)">
              <div class="san-list">
                <el-tag v-for="san in viewingCert.dnsNames" :key="san" size="small">{{ san }}</el-tag>
                <span v-if="viewingCert.dnsNames.length === 0" class="text-muted">无</span>
              </div>
            </el-descriptions-item>
            <el-descriptions-item label="颁发者 (Issuer)">
              {{ viewingCert.issuerDn }}
            </el-descriptions-item>
            <el-descriptions-item label="主题 (Subject)">
              {{ viewingCert.subjectDn }}
            </el-descriptions-item>
            <el-descriptions-item label="有效期">
              {{ viewingCert.validFrom.slice(0, 10) }} 至 {{ viewingCert.validTo.slice(0, 10) }}
              <el-tag
                :type="viewingCert.status === 'valid' ? 'success' : viewingCert.status === 'expiring_soon' ? 'warning' : 'danger'"
                size="small"
                style="margin-left: 8px;"
              >
                {{ viewingCert.status === 'valid' ? `剩余 ${viewingCert.daysRemaining} 天` : viewingCert.status === 'expiring_soon' ? `即将到期 (${viewingCert.daysRemaining}天)` : '已过期' }}
              </el-tag>
            </el-descriptions-item>
            <el-descriptions-item label="序列号">
              <code>{{ viewingCert.serialNumber }}</code>
            </el-descriptions-item>
            <el-descriptions-item label="SHA-256 指纹">
              <code>{{ viewingCert.thumbprint }}</code>
            </el-descriptions-item>
          </el-descriptions>

          <div v-if="viewingCert.certPem" class="pem-view">
            <div class="pem-header">
              <span>证书公钥内容 (CRT/PEM)</span>
              <el-button size="small" text :icon="CopyDocument" @click="copyText(viewingCert.certPem!, '公钥内容已复制')">
                复制公钥
              </el-button>
            </div>
            <pre class="pem-box"><code>{{ viewingCert.certPem }}</code></pre>
          </div>
        </template>
      </div>
    </el-drawer>
  </div>
</template>

<style scoped>
.ssl-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.notice-bar {
  margin-bottom: 4px;
}

.overview-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 16px;
}

.kpi-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 20px;
  background: var(--el-bg-color, #ffffff);
  border: 1px solid var(--el-border-color-lighter, #ebeef5);
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.04);
}

.kpi-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 48px;
  height: 48px;
  border-radius: 10px;
  font-size: 24px;
}

.ssl-icon {
  background: #ecf5ff;
  color: #409eff;
}

.domain-icon {
  background: #f0f9eb;
  color: #67c23a;
}

.cert-count-icon {
  background: #fdf6ec;
  color: #e6a23c;
}

.kpi-content {
  flex: 1;
  min-width: 0;
}

.kpi-label {
  font-size: 13px;
  color: var(--el-text-color-secondary, #909399);
}

.kpi-value {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 4px 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--el-text-color-primary, #303133);
}

.cert-title, .domain-title {
  max-width: 180px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.count-number {
  font-size: 20px;
}

.count-unit {
  font-size: 13px;
  color: var(--el-text-color-secondary, #909399);
}

.kpi-desc {
  font-size: 12px;
  color: var(--el-text-color-secondary, #909399);
}

.sync-badge {
  display: inline-block;
  margin-left: 8px;
  font-size: 11px;
}

.sync-badge.ok {
  color: #67c23a;
}

.sync-badge.pending {
  color: #e6a23c;
}

.ssl-tabs {
  background: var(--el-bg-color, #ffffff);
  border: 1px solid var(--el-border-color-lighter, #ebeef5);
  border-radius: 8px;
  padding: 16px 20px;
}

.table-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  gap: 12px;
}

.domain-cell {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.domain-text {
  display: flex;
  align-items: center;
  gap: 4px;
}

.proto-tag {
  font-weight: bold;
}

.port-tag {
  color: var(--el-text-color-secondary, #909399);
}

.primary-tag {
  display: flex;
  align-items: center;
  gap: 2px;
}

.bound-cert-info {
  display: flex;
  flex-direction: column;
}

.cert-name {
  font-weight: 500;
}

.cert-name-cell {
  display: flex;
  align-items: center;
  gap: 8px;
}

.active-badge {
  display: flex;
  align-items: center;
  gap: 2px;
}

.san-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.validity-cell {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 13px;
}

.action-group {
  display: flex;
  align-items: center;
  gap: 4px;
  flex-wrap: wrap;
}

.form-hint {
  display: block;
  margin-top: 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary, #909399);
}

.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.switches-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px 16px;
  background: var(--el-fill-color-light, #f5f7fa);
  padding: 12px 16px;
  border-radius: 6px;
}

.upload-box {
  width: 100%;
}

.nginx-section {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.nginx-card {
  padding: 16px;
  background: var(--el-fill-color-blank, #ffffff);
  border: 1px solid var(--el-border-color-lighter, #ebeef5);
  border-radius: 6px;
}

.nginx-card h3 {
  margin: 0 0 12px 0;
  font-size: 15px;
  font-weight: 600;
}

.card-header-flex {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.card-header-flex h3 {
  margin: 0;
}

.code-line {
  display: flex;
  align-items: center;
  gap: 8px;
}

.nginx-code {
  background: #1e1e1e;
  color: #d4d4d4;
  padding: 16px;
  border-radius: 6px;
  overflow-x: auto;
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 13px;
  line-height: 1.5;
  max-height: 480px;
}

.san-list {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.pem-view {
  margin-top: 20px;
}

.pem-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
  font-weight: 600;
  font-size: 13px;
}

.pem-box {
  background: #f5f7fa;
  border: 1px solid #dcdfe6;
  border-radius: 4px;
  padding: 12px;
  font-family: 'Consolas', monospace;
  font-size: 11px;
  max-height: 240px;
  overflow-y: auto;
  white-space: pre-wrap;
  word-break: break-all;
}

.text-muted {
  color: var(--el-text-color-secondary, #909399);
}
</style>
