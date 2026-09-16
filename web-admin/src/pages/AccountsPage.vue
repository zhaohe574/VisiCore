<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import {
  CircleCheck,
  Delete,
  Edit,
  Key,
  Lock,
  Plus,
  Refresh,
  Search,
  User as UserIcon,
  Warning
} from '@element-plus/icons-vue'
import { ElMessage } from 'element-plus'
import { managementApi, type Permission, type Role, type User } from '../api'
import { usePaged } from '../composables/usePaged'
import { useAction } from '../composables/useAction'
import { useAuth } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
import StatusBadge from '../components/StatusBadge.vue'
import ScopeEditor from '../components/ScopeEditor.vue'

const auth = useAuth()
const { busy, run, confirm } = useAction()
const tab = ref(auth.can('user.read') ? 'users' : 'roles')

const users = usePaged(
  managementApi.users,
  undefined,
  ['access.changed'],
  () => auth.can('user.read')
)

const roles = ref<Role[]>([])
const permissions = ref<Permission[]>([])

const userDialog = ref(false)
const roleDialog = ref(false)
const permissionDialog = ref(false)
const resetPasswordDialog = ref(false)

const editingUser = ref<number | null>(null)
const editingRole = ref<number | null>(null)
const resetTargetUser = ref<User | null>(null)
const resetNewPassword = ref('')

const scope = ref<{ target: 'user' | 'role'; id: number; name: string }>()

const userForm = reactive({
  username: '',
  password: '',
  displayName: '',
  phone: '',
  status: 'active',
  roleIds: [] as number[]
})

const roleForm = reactive({
  name: '',
  code: '',
  status: 'active',
  permissionCodes: [] as string[]
})

const permissionRole = ref<Role>()
const selectedCodes = ref<string[]>([])

// 业务权限分组
const permissionGroups = [
  {
    title: '视频监控与值守',
    icon: '📺',
    prefixes: ['live.', 'playback.', 'ptz.', 'layout.']
  },
  {
    title: '安全报警与处置',
    icon: '🚨',
    prefixes: ['alarm.']
  },
  {
    title: '设备、组织与通道',
    icon: '📹',
    prefixes: ['device.', 'channel.', 'area.', 'plugin.']
  },
  {
    title: '账号、角色与会话',
    icon: '👤',
    prefixes: ['user.', 'role.', 'session.']
  },
  {
    title: '系统运维与审计',
    icon: '⚙️',
    prefixes: ['statistics.', 'audit.', 'export.', 'desktop.', 'ssl.', 'settings.']
  }
]

// KPI 统计
const userStats = computed(() => {
  const all = users.items.value as User[]
  const active = all.filter(u => u.status === 'active').length
  const locked = all.filter(u => u.status === 'locked' || u.status === 'disabled').length
  return { active, locked }
})

async function loadRoles() {
  if (auth.can('role.read')) roles.value = await managementApi.roles()
}

async function loadOptions() {
  await run(async () => {
    await loadRoles()
    if (auth.can('role.manage')) permissions.value = await managementApi.permissions()
  }, '')
}

function editUser(user?: User) {
  editingUser.value = user?.id || null
  Object.assign(userForm, {
    username: user?.username || '',
    password: '',
    displayName: user?.displayName || '',
    phone: user?.phone || '',
    status: user?.status || 'active',
    roleIds: [...(user?.roleIds || [])]
  })
  userDialog.value = true
}

function editRole(role?: Role) {
  editingRole.value = role?.id || null
  Object.assign(roleForm, {
    name: role?.name || '',
    code: role?.code || '',
    status: role?.status || 'active',
    permissionCodes: [...(role?.permissionCodes || [])]
  })
  roleDialog.value = true
}

async function saveUser() {
  if (
    await run(async () => {
      if (!userForm.username.trim()) throw new Error('请输入登录账号')
      if (!editingUser.value && !userForm.password) throw new Error('新增账号必须设置初始密码')
      await managementApi.saveUser(editingUser.value, {
        ...userForm,
        username: userForm.username.trim(),
        password: userForm.password || undefined
      })
      await users.load()
      await loadRoles()
    }, '账号已保存')
  ) {
    userDialog.value = false
    userForm.password = ''
  }
}

async function saveRole() {
  if (
    await run(async () => {
      if (!roleForm.name.trim() || !roleForm.code.trim()) throw new Error('请填写角色名称和编码')
      await managementApi.saveRole(editingRole.value, {
        ...roleForm,
        name: roleForm.name.trim(),
        code: roleForm.code.trim()
      })
      await loadRoles()
    }, '角色已保存')
  ) {
    roleDialog.value = false
  }
}

async function removeRole(role: Role) {
  await confirm(`确认删除角色“${role.name}”？\n属于该角色的账号将失去对应权限绑定。`, async () => {
    await managementApi.deleteRole(role.id)
    await loadRoles()
  }, '角色已删除')
}

function editPermissions(role: Role) {
  permissionRole.value = role
  selectedCodes.value = [...role.permissionCodes]
  permissionDialog.value = true
}

async function savePermissions() {
  if (!permissionRole.value) return
  if (
    await run(async () => {
      await managementApi.rolePermissions(permissionRole.value!.id, selectedCodes.value)
      await loadRoles()
    }, '角色功能权限已保存')
  ) {
    permissionDialog.value = false
  }
}

// 分组权限相关逻辑
function getPermissionsForGroup(group: typeof permissionGroups[0]) {
  return permissions.value.filter(p => group.prefixes.some(prefix => p.code.startsWith(prefix)))
}

function isGroupAllSelected(group: typeof permissionGroups[0]) {
  const groupCodes = getPermissionsForGroup(group).map(p => p.code)
  return groupCodes.length > 0 && groupCodes.every(c => selectedCodes.value.includes(c))
}

function isGroupIndeterminate(group: typeof permissionGroups[0]) {
  const groupCodes = getPermissionsForGroup(group).map(p => p.code)
  const count = groupCodes.filter(c => selectedCodes.value.includes(c)).length
  return count > 0 && count < groupCodes.length
}

function toggleGroupSelection(group: typeof permissionGroups[0], checked: boolean) {
  const groupCodes = getPermissionsForGroup(group).map(p => p.code)
  if (checked) {
    selectedCodes.value = Array.from(new Set([...selectedCodes.value, ...groupCodes]))
  } else {
    selectedCodes.value = selectedCodes.value.filter(c => !groupCodes.includes(c))
  }
}

// 密码重置快捷操作
function openResetPassword(user: User) {
  resetTargetUser.value = user
  resetNewPassword.value = ''
  resetPasswordDialog.value = true
}

async function submitResetPassword() {
  if (!resetTargetUser.value || !resetNewPassword.value.trim()) {
    ElMessage.warning('请输入新的密码')
    return
  }
  await run(async () => {
    await managementApi.saveUser(resetTargetUser.value!.id, {
      username: resetTargetUser.value!.username,
      displayName: resetTargetUser.value!.displayName,
      phone: resetTargetUser.value!.phone,
      status: resetTargetUser.value!.status,
      roleIds: resetTargetUser.value!.roleIds,
      password: resetNewPassword.value.trim()
    })
    ElMessage.success(`用户「${resetTargetUser.value?.username}」的密码已重置`)
    resetPasswordDialog.value = false
  }, '')
}

onMounted(loadOptions)
</script>

<template>
  <div>
    <PageHeader title="账号与角色" description="管理系统操作员账号、RBAC角色模型、功能权限矩阵与组织通道数据权限范围">
      <el-button :icon="Refresh" @click="users.load(); loadOptions()">刷新</el-button>
      <el-button v-if="tab === 'users' && auth.can('user.manage')" type="primary" :icon="Plus" @click="editUser()">
        新增账号
      </el-button>
      <el-button v-if="tab === 'roles' && auth.can('role.manage')" type="primary" :icon="Plus" @click="editRole()">
        新增角色
      </el-button>
    </PageHeader>

    <!-- 顶部 KPI 统计 -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-icon-wrap blue"><el-icon><UserIcon /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">平台账号总数</div>
          <div class="kpi-value">{{ users.total.value }}</div>
          <div class="kpi-sub">已注册认证人员</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap teal"><el-icon><CircleCheck /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">正常启用账号</div>
          <div class="kpi-value">{{ userStats.active }}</div>
          <div class="kpi-sub">允许正常登录系统</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap red"><el-icon><Warning /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">锁定 / 停用</div>
          <div class="kpi-value">{{ userStats.locked }}</div>
          <div class="kpi-sub">安全风控或离职冻结</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon-wrap purple"><el-icon><Key /></el-icon></div>
        <div class="kpi-content">
          <div class="kpi-label">安全角色种类</div>
          <div class="kpi-value">{{ roles.length }}</div>
          <div class="kpi-sub">权限模型体系</div>
        </div>
      </div>
    </div>

    <!-- 标签页切换 -->
    <el-tabs v-model="tab" style="margin-bottom: 16px;">
      <el-tab-pane v-if="auth.can('user.read')" label="系统账号列表" name="users" />
      <el-tab-pane v-if="auth.can('role.read') || auth.can('role.manage')" label="权限角色管理" name="roles" />
    </el-tabs>

    <!-- 账号列表模式 -->
    <template v-if="tab === 'users'">
      <div class="filter-card">
        <form class="filter-bar" @submit.prevent="users.load(true)">
          <el-input
            v-model="users.search.value"
            clearable
            :prefix-icon="Search"
            placeholder="搜索账号名、姓名或手机号..."
            aria-label="搜索账号"
            style="width: 260px;"
            @clear="users.load(true)"
          />
          <el-button type="primary" native-type="submit" :icon="Search">查询</el-button>
          <el-button @click="users.search.value = ''; users.load(true)">重置</el-button>
        </form>
      </div>

      <el-alert v-if="users.error.value" :title="users.error.value" type="error" :closable="false" show-icon style="margin-bottom: 12px;" />

      <div class="table-card">
        <el-table v-loading="users.loading.value" :data="users.items.value" row-key="id" empty-text="暂无账号记录">
          <el-table-column prop="username" label="登录账号" min-width="140">
            <template #default="{ row }">
              <strong>{{ row.username }}</strong>
            </template>
          </el-table-column>

          <el-table-column prop="displayName" label="真实姓名" min-width="130">
            <template #default="{ row }">
              {{ row.displayName || '—' }}
            </template>
          </el-table-column>

          <el-table-column prop="phone" label="联系手机" min-width="140">
            <template #default="{ row }">
              <span class="hash">{{ row.phone || '—' }}</span>
            </template>
          </el-table-column>

          <el-table-column label="关联权限角色" min-width="220">
            <template #default="{ row }">
              <div style="display: flex; flex-wrap: wrap; gap: 4px;">
                <el-tag v-for="id in row.roleIds" :key="id" type="info" size="small">
                  {{ roles.find(role => role.id === id)?.name || `角色 ${id}` }}
                </el-tag>
                <span v-if="!row.roleIds.length" class="muted" style="font-size: 12px;">未分配任何角色</span>
              </div>
            </template>
          </el-table-column>

          <el-table-column label="账号状态" width="100">
            <template #default="{ row }">
              <StatusBadge :value="row.status" />
            </template>
          </el-table-column>

          <el-table-column v-if="auth.can('user.manage')" label="操作" width="220" fixed="right">
            <template #default="{ row }">
              <div class="table-tools">
                <el-button link type="primary" @click="scope = { target: 'user', id: row.id, name: row.username }">
                  数据范围
                </el-button>
                <el-button link type="primary" :icon="Edit" @click="editUser(row as User)">
                  编辑
                </el-button>
                <el-button link type="warning" :icon="Lock" @click="openResetPassword(row as User)">
                  改密
                </el-button>
              </div>
            </template>
          </el-table-column>
        </el-table>

        <div class="pagination-bar">
          <el-pagination
            v-model:current-page="users.page.value"
            v-model:page-size="users.pageSize.value"
            :total="users.total.value"
            :page-sizes="[15, 30, 50, 100]"
            layout="total, sizes, prev, pager, next, jumper"
            @current-change="users.load()"
            @size-change="users.load(true)"
          />
        </div>
      </div>
    </template>

    <!-- 角色管理模式 -->
    <template v-else>
      <div class="table-card">
        <el-table :data="roles" row-key="id" empty-text="暂无角色记录">
          <el-table-column prop="name" label="角色名称" min-width="160">
            <template #default="{ row }">
              <strong>{{ row.name }}</strong>
            </template>
          </el-table-column>

          <el-table-column prop="code" label="权限编码" min-width="150">
            <template #default="{ row }">
              <span class="hash">{{ row.code }}</span>
            </template>
          </el-table-column>

          <el-table-column prop="userCount" label="绑定账号数" width="120">
            <template #default="{ row }">
              <el-tag size="small" type="info">{{ row.userCount }} 人</el-tag>
            </template>
          </el-table-column>

          <el-table-column label="功能权限项" width="120">
            <template #default="{ row }">
              <span style="font-weight: 600; color: var(--primary);">{{ row.permissionCodes.length }}</span> 项
            </template>
          </el-table-column>

          <el-table-column label="状态" width="100">
            <template #default="{ row }">
              <StatusBadge :value="row.status" />
            </template>
          </el-table-column>

          <el-table-column v-if="auth.can('role.manage')" label="操作" width="280" fixed="right">
            <template #default="{ row }">
              <div class="table-tools">
                <el-button link type="primary" :icon="Key" @click="editPermissions(row as Role)">
                  分配权限
                </el-button>
                <el-button link type="primary" @click="scope = { target: 'role', id: row.id, name: row.name }">
                  数据范围
                </el-button>
                <el-button link type="primary" :icon="Edit" @click="editRole(row as Role)">
                  编辑
                </el-button>
                <el-button link type="danger" :icon="Delete" :disabled="busy" @click="removeRole(row as Role)">
                  删除
                </el-button>
              </div>
            </template>
          </el-table-column>
        </el-table>
      </div>
    </template>

    <!-- 账号编辑弹窗 -->
    <el-dialog
      v-model="userDialog"
      :title="editingUser ? '编辑系统账号' : '新增系统账号'"
      width="580px"
      destroy-on-close
      @closed="userForm.password = ''"
    >
      <el-form label-position="top">
        <div style="display: grid; grid-template-columns: repeat(2, 1fr); gap: 0 16px;">
          <el-form-item label="登录账号" required>
            <el-input v-model="userForm.username" maxlength="80" autocomplete="off" placeholder="例如：operator01" clearable />
          </el-form-item>

          <el-form-item label="真实姓名">
            <el-input v-model="userForm.displayName" maxlength="80" placeholder="例如：张三" clearable />
          </el-form-item>

          <el-form-item label="手机号码">
            <el-input v-model="userForm.phone" maxlength="30" placeholder="13800000000" clearable />
          </el-form-item>

          <el-form-item :label="editingUser ? '新密码（留空保持不变）' : '初始登录密码'" :required="!editingUser">
            <el-input
              v-model="userForm.password"
              type="password"
              show-password
              autocomplete="new-password"
              maxlength="256"
              placeholder="请输入密码"
            />
          </el-form-item>
        </div>

        <el-form-item label="关联权限角色">
          <el-select v-model="userForm.roleIds" multiple filterable placeholder="选择分配的角色" style="width: 100%;">
            <el-option
              v-for="role in roles"
              :key="role.id"
              :label="role.name"
              :value="role.id"
              :disabled="role.status !== 'active'"
            />
          </el-select>
        </el-form-item>

        <el-form-item label="账号状态">
          <el-radio-group v-model="userForm.status">
            <el-radio-button value="active">正常启用</el-radio-button>
            <el-radio-button value="disabled">暂时停用</el-radio-button>
            <el-radio-button value="locked">安全锁定</el-radio-button>
          </el-radio-group>
        </el-form-item>
      </el-form>

      <template #footer>
        <el-button @click="userDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="saveUser">确认保存</el-button>
      </template>
    </el-dialog>

    <!-- 角色编辑弹窗 -->
    <el-dialog v-model="roleDialog" :title="editingRole ? '编辑权限角色' : '新增权限角色'" width="480px">
      <el-form label-position="top">
        <el-form-item label="角色名称" required>
          <el-input v-model="roleForm.name" maxlength="80" placeholder="例如：车间监控专员" clearable />
        </el-form-item>
        <el-form-item label="角色唯一编码" required>
          <el-input v-model="roleForm.code" maxlength="64" placeholder="例如：workshop_viewer" clearable />
        </el-form-item>
        <el-form-item label="启用状态">
          <el-switch v-model="roleForm.status" active-value="active" inactive-value="disabled" active-text="启用角色" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="roleDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="saveRole">确认保存</el-button>
      </template>
    </el-dialog>

    <!-- 权限矩阵卡片分组弹窗 -->
    <el-dialog
      v-model="permissionDialog"
      :title="`配置角色功能权限 · ${permissionRole?.name || ''}`"
      width="780px"
    >
      <p class="muted" style="font-size: 13px; margin: 0 0 16px;">
        勾选允许该角色访问的操作接口与界面菜单权限。变更后属于该角色的在线客户端将在下一次鉴权时自动同步生效。
      </p>

      <div style="display: flex; flex-direction: column; gap: 16px; max-height: 480px; overflow-y: auto; padding-right: 4px;">
        <div
          v-for="group in permissionGroups"
          :key="group.title"
          class="filter-card"
          style="margin-bottom: 0; padding: 14px 18px;"
        >
          <div style="display: flex; align-items: center; justify-content: space-between; border-bottom: 1px solid var(--border-light); padding-bottom: 8px; margin-bottom: 12px;">
            <div style="display: flex; align-items: center; gap: 8px; font-weight: 600; font-size: 14px;">
              <span>{{ group.icon }}</span>
              <span>{{ group.title }}</span>
            </div>
            <el-checkbox
              :model-value="isGroupAllSelected(group)"
              :indeterminate="isGroupIndeterminate(group)"
              @change="toggleGroupSelection(group, $event as boolean)"
            >
              全选本模块
            </el-checkbox>
          </div>

          <el-checkbox-group v-model="selectedCodes" class="permission-grid">
            <el-checkbox
              v-for="p in getPermissionsForGroup(group)"
              :key="p.code"
              :value="p.code"
            >
              {{ p.name }} <small class="muted" style="font-size: 10px;">({{ p.code }})</small>
            </el-checkbox>
          </el-checkbox-group>
        </div>
      </div>

      <template #footer>
        <div style="display: flex; justify-content: space-between; align-items: center;">
          <span class="muted" style="font-size: 12.5px;">已勾选 {{ selectedCodes.length }} / {{ permissions.length }} 项功能权限</span>
          <div>
            <el-button @click="permissionDialog = false">取消</el-button>
            <el-button type="primary" :loading="busy" @click="savePermissions">确认保存权限</el-button>
          </div>
        </div>
      </template>
    </el-dialog>

    <!-- 快捷重置密码弹窗 -->
    <el-dialog v-model="resetPasswordDialog" title="快捷重置用户登录密码" width="420px">
      <el-form label-position="top">
        <el-form-item label="目标用户账号">
          <el-input :model-value="resetTargetUser?.username" disabled />
        </el-form-item>
        <el-form-item label="重置为新密码" required>
          <el-input
            v-model="resetNewPassword"
            type="password"
            show-password
            autocomplete="new-password"
            placeholder="输入新的安全密码"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="resetPasswordDialog = false">取消</el-button>
        <el-button type="primary" :loading="busy" @click="submitResetPassword">确认重置</el-button>
      </template>
    </el-dialog>

    <!-- 数据范围配置弹窗 -->
    <ScopeEditor
      v-if="scope"
      :target="scope.target"
      :target-id="scope.id"
      :name="scope.name"
      @close="scope = undefined"
    />
  </div>
</template>
