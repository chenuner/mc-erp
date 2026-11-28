<script setup lang="ts">
import { computed, nextTick, onMounted, reactive, ref } from 'vue'
import { ElMessage, ElMessageBox, type FormInstance, type FormRules, type TableInstance } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import DictTreeSelect from '@/components/DictTreeSelect.vue'
import {
  createSysDict,
  createSysDictGroup,
  deleteSysDict,
  deleteSysDictGroup,
  listSysDictGroups,
  listSysDictsByGroup,
  reorderSysDicts,
  updateSysDict,
  updateSysDictGroup,
  type SysDictDto,
  type SysDictGroupDto,
} from '@/api/system'

type StatusFilter = 'all' | 'enabled' | 'disabled'

const groups = ref<SysDictGroupDto[]>([])
const groupsLoading = ref(false)
const groupKeyword = ref('')
const selectedCode = ref('')

const selectedGroup = computed(() => groups.value.find((group) => group.code === selectedCode.value) ?? null)
const totalItems = computed(() => groups.value.reduce((total, group) => total + group.itemCount, 0))
const filteredGroups = computed(() => {
  const keyword = groupKeyword.value.trim().toLowerCase()
  if (!keyword) return groups.value
  return groups.value.filter(
    (group) => group.name.toLowerCase().includes(keyword) || group.code.toLowerCase().includes(keyword),
  )
})

const items = ref<SysDictDto[]>([])
const itemsLoading = ref(false)
const itemKeyword = ref('')
const statusFilter = ref<StatusFilter>('all')
const togglingIds = ref<string[]>([])
const reordering = ref(false)

const orderedItems = computed(() =>
  [...items.value].sort((left, right) => left.sort - right.sort || left.label.localeCompare(right.label)),
)

const visibleItems = computed(() => {
  const keyword = itemKeyword.value.trim().toLowerCase()
  return orderedItems.value.filter((dict) => {
    if (statusFilter.value === 'enabled' && !dict.enabled) return false
    if (statusFilter.value === 'disabled' && dict.enabled) return false
    if (!keyword) return true
    return (
      dict.label.toLowerCase().includes(keyword) ||
      dict.value.toLowerCase().includes(keyword) ||
      (dict.remark ?? '').toLowerCase().includes(keyword)
    )
  })
})

function selectGroup(code: string) {
  if (code === selectedCode.value) return
  selectedCode.value = code
  itemKeyword.value = ''
  statusFilter.value = 'all'
  void loadItems()
}

const childIds = computed(() => new Set(items.value.map((dict) => dict.parentId).filter((id): id is string => !!id)))
const parentById = computed(() => new Map(items.value.map((dict) => [dict.id, dict])))

/** 上級停用 = 整條分支都不再用；這裡找出第一個讓這一項失效的停用上級。 */
function disabledAncestor(row: SysDictDto): SysDictDto | undefined {
  const seen = new Set<string>([row.id])
  let parentId = row.parentId
  while (parentId && seen.add(parentId)) {
    const parent = parentById.value.get(parentId)
    if (!parent) return undefined
    if (!parent.enabled) return parent
    parentId = parent.parentId
  }
  return undefined
}

/**
 * 表格用樹形顯示（Element Plus 只認 children 欄位，所以在這裡掛上）。
 * 同層先按手動排序、再看識別碼，上移/下移才會跟著動。
 */
type TreeNode = SysDictDto & { children: TreeNode[] }

const treeItems = computed(() => {
  const rows = visibleItems.value.map((dict) => ({ ...dict, children: [] as TreeNode[] }))
  const nodeById = new Map(rows.map((row) => [row.id, row]))
  const roots: TreeNode[] = []
  for (const row of rows) {
    const parent = row.parentId ? nodeById.get(row.parentId) : undefined
    if (parent && parent !== row) parent.children.push(row)
    else roots.push(row)
  }
  roots.sort(compareSiblings)
  for (const row of rows) {
    if (row.children.length) row.children.sort(compareSiblings)
  }
  return roots
})

function compareSiblings(left: TreeNode, right: TreeNode) {
  return left.sort - right.sort || compareCodes(left.value, right.value)
}

function compareCodes(left: string, right: string) {
  return left.localeCompare(right, undefined, { numeric: true })
}

/** 樹形展開：預設全部展開；用戶收起某些行後，重新載入仍保持收起的狀態。 */
const tableRef = ref<TableInstance>()
const expandAll = ref(true)
/** 用戶手動點開的行（只有預設收起時才有意義）。 */
const expandedIds = new Set<string>()
/** 用戶手動收起的行（預設展開時才有意義）。 */
const collapsedIds = new Set<string>()
/** 程式碼正批次展開/收起的期間，忽略表格回報的 expand-change。 */
let applyingExpansion = false

function isExpanded(row: SysDictDto) {
  return expandAll.value ? !collapsedIds.has(row.id) : expandedIds.has(row.id)
}

function toggleExpanded(row: SysDictDto, expanded: boolean) {
  const bucket = expandAll.value ? collapsedIds : expandedIds
  if (expanded) bucket.delete(row.id)
  else bucket.add(row.id)
}

function trackExpand(row: SysDictDto, expanded: boolean) {
  // el-table 的 expand-change 回傳 (row, expanded)，直接以「這一行展開/收起」為準
  if (applyingExpansion) return
  toggleExpanded(row, expanded)
}

/** 樹形資料是巢狀的，展開要走到每一層的父節點。 */
function flattenRows(nodes: TreeNode[]): TreeNode[] {
  return nodes.flatMap((node) => [node, ...flattenRows(node.children)])
}

/** expanded 有值就整批展開或收起；不傳則按每一行記錄的手動狀態還原。 */
async function applyRowExpansion(expanded?: boolean) {
  const parents = flattenRows(treeItems.value).filter((row) => row.children.length)
  applyingExpansion = true
  // 收起時由深到淺，讓「收起全部」之後每一層都是收起的，而不是只收起最上層
  for (const row of expanded === false ? parents.reverse() : parents) {
    tableRef.value?.toggleRowExpansion(row, expanded ?? isExpanded(row))
  }
  await nextTick()
  applyingExpansion = false
}

/** 重新載入後表格資料換了，把手動展開/收起的行重新套用一次。 */
async function restoreExpandedRows() {
  await nextTick()
  await applyRowExpansion()
}

async function applyExpandAll(value: string | number | boolean) {
  expandAll.value = Boolean(value)
  expandedIds.clear()
  collapsedIds.clear()
  await applyRowExpansion(Boolean(value))
}

async function refreshGroups() {
  const result = await listSysDictGroups()
  groups.value = result
  if (!result.some((group) => group.code === selectedCode.value)) {
    selectedCode.value = result[0]?.code ?? ''
  }
}

async function loadItems() {
  if (!selectedCode.value) {
    items.value = []
    return
  }
  itemsLoading.value = true
  try {
    items.value = await listSysDictsByGroup(selectedCode.value)
  } catch {
    items.value = []
  } finally {
    itemsLoading.value = false
  }
  await restoreExpandedRows()
}

// ---------- 分組 ----------
const groupDialogVisible = ref(false)
const editingGroupId = ref<string | null>(null)
const editingGroupCode = ref('')
const editingGroupIsSystem = ref(false)
const groupSubmitting = ref(false)
const groupFormRef = ref<FormInstance>()
const groupForm = reactive({ name: '', remark: '' })
const groupRules: FormRules<typeof groupForm> = {
  name: [{ required: true, message: '請輸入分組名稱', trigger: 'blur' }],
}

function openGroupCreate() {
  editingGroupId.value = null
  editingGroupCode.value = ''
  editingGroupIsSystem.value = false
  groupForm.name = ''
  groupForm.remark = ''
  groupDialogVisible.value = true
}

function openGroupEdit() {
  const group = selectedGroup.value
  if (!group) return
  editingGroupId.value = group.id
  editingGroupCode.value = group.code
  editingGroupIsSystem.value = group.isSystem
  groupForm.name = group.name
  groupForm.remark = group.remark ?? ''
  groupDialogVisible.value = true
}

async function submitGroup() {
  const valid = await groupFormRef.value?.validate().catch(() => false)
  if (!valid) return
  groupSubmitting.value = true
  try {
    const remark = groupForm.remark.trim()
    if (editingGroupId.value) {
      await updateSysDictGroup(editingGroupId.value, { name: groupForm.name.trim(), remark })
      ElMessage.success('分組已更新')
    } else {
      const created = await createSysDictGroup({ name: groupForm.name.trim(), remark: remark || null })
      selectedCode.value = created.code
      ElMessage.success('分組已建立，現在可以新增字典項')
    }
    groupDialogVisible.value = false
    await refreshGroups()
    await loadItems()
  } catch {
    /* 攔截器已提示 */
  } finally {
    groupSubmitting.value = false
  }
}

async function removeGroup() {
  const group = selectedGroup.value
  if (!group) return
  try {
    await ElMessageBox.confirm(
      `確定刪除分組「${group.name}」嗎？只有空分組可以刪除。`,
      '刪除分組',
      { confirmButtonText: '刪除', cancelButtonText: '取消', type: 'warning' },
    )
    await deleteSysDictGroup(group.id)
    ElMessage.success('分組已刪除')
    selectedCode.value = ''
    await refreshGroups()
    await loadItems()
  } catch {
    /* 已取消或攔截器已提示 */
  }
}

// ---------- 字典項 ----------
const itemDialogVisible = ref(false)
const editingItemId = ref<string | null>(null)
const itemSubmitting = ref(false)
const itemFormRef = ref<FormInstance>()
const itemForm = reactive({
  groupName: '',
  label: '',
  value: '',
  sort: 0,
  enabled: true,
  remark: '',
  // 空字串 = 頂層（下拉清空時就是這個值）
  parentId: '',
})
const itemRules: FormRules<typeof itemForm> = {
  groupName: [{ required: true, message: '請選擇分組', trigger: 'change' }],
  label: [{ required: true, message: '請輸入標籤', trigger: 'blur' }],
}
const dialogGroup = computed(() => groups.value.find((group) => group.code === itemForm.groupName) ?? null)
/**
 * 可選的上級（畫成樹）：同分組的項都可以當上級，但要排除自己和自己整棵子樹（否則會成環）。
 */
const parentDicts = computed(() => {
  if (!itemForm.groupName) return []
  const excluded = new Set<string>()
  if (editingItemId.value) {
    excluded.add(editingItemId.value)
    const pending = [editingItemId.value]
    while (pending.length) {
      const current = pending.pop()!
      for (const dict of items.value) {
        if (dict.parentId === current && !excluded.has(dict.id)) {
          excluded.add(dict.id)
          pending.push(dict.id)
        }
      }
    }
  }
  return items.value.filter((dict) => !excluded.has(dict.id))
})
const nextValuePreview = computed(() => {
  if (editingItemId.value) return itemForm.value
  if (itemForm.groupName !== selectedCode.value) return '自動生成'
  const parent = itemForm.parentId ? items.value.find((dict) => dict.id === itemForm.parentId) : null
  if (parent) {
    // 下級識別碼跟在上級後面：1.1、1.2…
    const prefix = `${parent.value}.`
    const max = items.value.reduce((result, dict) => {
      if (!dict.value.startsWith(prefix)) return result
      const parsed = Number.parseInt(dict.value.slice(prefix.length), 10)
      return Number.isNaN(parsed) ? result : Math.max(result, parsed)
    }, 0)
    return `${prefix}${max + 1}`
  }
  const max = items.value.reduce((result, dict) => {
    if (dict.parentId) return result
    const parsed = Number.parseInt(dict.value, 10)
    return Number.isNaN(parsed) ? result : Math.max(result, parsed)
  }, 0)
  return String(max + 1)
})

function openItemCreate(parentId: string | null = null) {
  editingItemId.value = null
  itemForm.groupName = selectedCode.value
  itemForm.label = ''
  itemForm.value = ''
  itemForm.sort = 0
  itemForm.enabled = true
  itemForm.remark = ''
  // 從某一行按「加下級」時直接帶入上級
  itemForm.parentId = parentId && items.value.some((dict) => dict.id === parentId) ? parentId : ''
  itemDialogVisible.value = true
}

/** 行內「加下級」：沿用該行的分組，上級就是該行。 */
function addChild(row: SysDictDto) {
  openItemCreate(row.id)
}

function openItemEdit(row: SysDictDto) {
  editingItemId.value = row.id
  itemForm.groupName = row.groupName
  itemForm.label = row.label
  itemForm.value = row.value
  itemForm.sort = row.sort
  itemForm.enabled = row.enabled
  itemForm.remark = row.remark ?? ''
  itemForm.parentId = row.parentId ?? ''
  itemDialogVisible.value = true
}

async function submitItem() {
  const valid = await itemFormRef.value?.validate().catch(() => false)
  if (!valid) return
  itemSubmitting.value = true
  try {
    const remark = itemForm.remark.trim()
    const parentId = itemForm.parentId || null
    if (editingItemId.value) {
      const parentChanged = parentId !== (items.value.find((dict) => dict.id === editingItemId.value)?.parentId ?? null)
      const updated = await updateSysDict(editingItemId.value, {
        label: itemForm.label.trim(),
        sort: itemForm.sort,
        enabled: itemForm.enabled,
        remark,
        // 只有真的換了層級才送 parentId，避免無謂改寫識別碼
        ...(parentChanged ? { parentId } : {}),
      })
      ElMessage.success(parentChanged ? `字典項已更新，識別碼重新編為 ${updated.value}` : '字典項已更新')
    } else {
      await createSysDict({
        groupName: itemForm.groupName,
        label: itemForm.label.trim(),
        enabled: itemForm.enabled,
        remark: remark || null,
        parentId,
      })
      selectedCode.value = itemForm.groupName
      ElMessage.success('字典項已建立，識別碼由系統自動分配')
    }
    itemDialogVisible.value = false
    await loadItems()
    await refreshGroups()
  } catch {
    /* 攔截器已提示 */
  } finally {
    itemSubmitting.value = false
  }
}

async function toggleEnabled(row: SysDictDto, enabled: boolean) {
  if (togglingIds.value.includes(row.id)) return
  togglingIds.value = [...togglingIds.value, row.id]
  // 表格的 row 是樹形節點的副本，改它不會觸發重新渲染，要改回原始清單那一項
  const target = items.value.find((item) => item.id === row.id)
  const previous = target?.enabled
  if (target) target.enabled = enabled
  try {
    const updated = await updateSysDict(row.id, { enabled })
    if (target) target.enabled = updated.enabled
    await refreshGroups()
  } catch {
    if (target && previous !== undefined) target.enabled = previous
  } finally {
    togglingIds.value = togglingIds.value.filter((id) => id !== row.id)
  }
}

async function handleEnabledChange(row: SysDictDto, value: string | number | boolean) {
  const enabled = Boolean(value)
  if (isRequiredDict(row) && !enabled) {
    ElMessage.warning('「待分類」是票據上傳流程必需項，不能停用')
    return
  }
  // 停用上級 = 整條分支一起失效，先講清楚再動手（歷史資料不受影響）
  if (!enabled && childIds.value.has(row.id)) {
    try {
      await ElMessageBox.confirm(
        `停用「${row.label}」後，它底下的所有下級也一併不可選（已存的資料照樣顯示與查詢）。`,
        '停用上級分類',
        { confirmButtonText: '停用', cancelButtonText: '取消', type: 'warning' },
      )
    } catch {
      return
    }
  }
  await toggleEnabled(row, enabled)
}

/** 同一層的兄弟項：上移/下移只在同一層內換位，不會把子項搬去別的上級底下。 */
function siblingsOf(row: SysDictDto) {
  return orderedItems.value.filter((item) => (item.parentId ?? null) === (row.parentId ?? null))
}

function canMove(id: string, delta: number) {
  if (reordering.value || itemKeyword.value.trim() || statusFilter.value !== 'all') return false
  const row = orderedItems.value.find((item) => item.id === id)
  if (!row) return false
  const siblings = siblingsOf(row)
  const index = siblings.findIndex((item) => item.id === id)
  const target = index + delta
  return index >= 0 && target >= 0 && target < siblings.length
}

async function moveItem(row: SysDictDto, delta: number) {
  if (!canMove(row.id, delta)) return
  const siblings = siblingsOf(row)
  const otherId = siblings[siblings.findIndex((item) => item.id === row.id) + delta].id
  // 後端按整組 id 的順序重排：在整份清單裡互換同層這兩項的位置即可
  const ids = orderedItems.value.map((item) => item.id)
  const from = ids.indexOf(row.id)
  const to = ids.indexOf(otherId)
  ;[ids[from], ids[to]] = [ids[to], ids[from]]
  reordering.value = true
  try {
    await reorderSysDicts(selectedCode.value, ids)
    await loadItems()
  } catch {
    /* 攔截器已提示 */
  } finally {
    reordering.value = false
  }
}

/** 票據上傳流程固定使用 Accounting/pending，停用或刪除會令上傳失敗。 */
function isRequiredDict(dict: SysDictDto) {
  return dict.groupName === 'Accounting' && dict.value === 'pending'
}

const requiredWarning = computed(() => {
  if (selectedCode.value !== 'Accounting') return ''
  const pendingItems = items.value.filter(isRequiredDict)
  if (pendingItems.length === 0) return '目前沒有「待分類」字典項，票據上傳會失敗。'
  if (!pendingItems.some((dict) => dict.enabled)) return '「待分類」目前停用，票據上傳會失敗，請將它重新啟用。'
  return ''
})

async function removeItem(row: SysDictDto) {
  try {
    await ElMessageBox.confirm(
      `確定刪除字典項「${row.label}」嗎？若已被票據引用，系統會阻止刪除並建議停用。`,
      '刪除字典項',
      { confirmButtonText: '刪除', cancelButtonText: '取消', type: 'warning' },
    )
    await deleteSysDict(row.id)
    ElMessage.success('字典項已刪除')
    await loadItems()
    await refreshGroups()
  } catch {
    /* 已取消或攔截器已提示 */
  }
}

onMounted(async () => {
  groupsLoading.value = true
  try {
    await refreshGroups()
  } catch {
    /* 攔截器已提示 */
  } finally {
    groupsLoading.value = false
  }
  await loadItems()
})
</script>

<template>
  <div class="categories-view">
    <div class="page-header">
      <div>
        <h1 class="page-title">系統字典</h1>
        <p class="page-desc">先選分組，再維護分組下的選項；識別碼由系統自動編號，用戶只會看到標籤。</p>
      </div>
      <div class="page-summary">{{ groups.length }} 個分組 · {{ totalItems }} 個字典項</div>
    </div>

    <div class="dict-shell">
      <!-- 分組列表 -->
      <aside class="group-panel mc-card" v-loading="groupsLoading">
        <div class="group-panel-header">
          <span class="group-panel-title">分組</span>
          <el-button link type="primary" @click="openGroupCreate">
            <AppIcon name="plus" :size="14" style="margin-right: 4px" />新增
          </el-button>
        </div>
        <div class="group-search">
          <el-input v-model="groupKeyword" clearable placeholder="搜尋分組">
            <template #prefix><AppIcon name="search" :size="14" /></template>
          </el-input>
        </div>
        <div class="group-list">
          <button
            v-for="group in filteredGroups"
            :key="group.id"
            type="button"
            class="group-item"
            :class="{ active: group.code === selectedCode }"
            :aria-current="group.code === selectedCode ? 'true' : undefined"
            @click="selectGroup(group.code)"
          >
            <span class="group-name">{{ group.name }}</span>
            <span class="group-count">{{ group.itemCount }}</span>
          </button>
          <el-empty
            v-if="!groupsLoading && filteredGroups.length === 0"
            :image-size="56"
            :description="groups.length === 0 ? '尚未建立分組' : '沒有符合的分組'"
          >
            <el-button v-if="groups.length === 0" type="primary" size="small" @click="openGroupCreate">
              建立第一個分組
            </el-button>
          </el-empty>
        </div>
      </aside>

      <!-- 字典項 -->
      <section v-if="selectedGroup" class="items-panel mc-card">
        <div class="items-header">
          <div class="items-title-block">
            <div class="items-title">
              <h2>{{ selectedGroup.name }}</h2>
              <el-tag v-if="selectedGroup.isSystem" size="small" effect="plain" type="info">系統內建</el-tag>
            </div>
            <p class="items-desc">
              {{ selectedGroup.remark || `共 ${selectedGroup.itemCount} 項，其中 ${selectedGroup.enabledCount} 項啟用` }}
            </p>
          </div>
          <div class="items-actions">
            <el-button link type="primary" @click="openGroupEdit">編輯分組</el-button>
            <el-button v-if="!selectedGroup.isSystem" link type="danger" :disabled="selectedGroup.itemCount > 0" @click="removeGroup">刪除分組</el-button>
            <el-button type="primary" @click="openItemCreate()">
              <AppIcon name="plus" :size="14" style="margin-right: 4px" />新增字典項
            </el-button>
          </div>
        </div>

        <el-alert
          v-if="requiredWarning && !itemsLoading"
          type="warning"
          :closable="false"
          show-icon
          :title="requiredWarning"
          class="required-alert"
        />

        <div class="items-toolbar">
          <el-input v-model="itemKeyword" clearable placeholder="搜尋標籤、識別碼或備註" class="search-input">
            <template #prefix><AppIcon name="search" :size="16" /></template>
          </el-input>
          <el-radio-group v-model="statusFilter" size="default">
            <el-radio-button value="all">全部</el-radio-button>
            <el-radio-button value="enabled">啟用</el-radio-button>
            <el-radio-button value="disabled">停用</el-radio-button>
          </el-radio-group>
          <div class="tree-switch">
            <el-switch v-model="expandAll" active-text="展開全部" @change="applyExpandAll" />
          </div>
        </div>

        <!-- 第一欄是標籤：Element Plus 的樹形縮進落在第一欄，這樣父子關係才看得出來 -->
        <el-table
          ref="tableRef"
          v-loading="itemsLoading"
          :data="treeItems"
          row-key="id"
          :default-expand-all="expandAll"
          :tree-props="{ children: 'children' }"
          :indent="20"
          @expand-change="trackExpand"
        >
          <el-table-column label="標籤" min-width="240">
            <template #default="{ row }">
              <span class="dict-label">
                <span :class="{ 'dict-parent': row.children.length }">{{ row.label }}</span>
                <el-tag v-if="isRequiredDict(row)" size="small" type="warning" effect="plain">上傳必需</el-tag>
                <el-tooltip v-if="disabledAncestor(row)" content="上級已停用，這一項也一併不可選" placement="top">
                  <el-tag size="small" type="info" effect="plain">上級停用</el-tag>
                </el-tooltip>
              </span>
            </template>
          </el-table-column>
          <el-table-column label="識別碼" width="100">
            <template #default="{ row }"><span class="dict-code">{{ row.value }}</span></template>
          </el-table-column>
          <el-table-column label="排序" width="64" align="center">
            <template #default="{ row }">
              <div class="sort-buttons">
                <el-button link type="primary" :disabled="!canMove(row.id, -1)" aria-label="上移" title="上移" @click="moveItem(row, -1)">
                  <AppIcon name="arrow-up" :size="14" />
                </el-button>
                <el-button link type="primary" :disabled="!canMove(row.id, 1)" aria-label="下移" title="下移" @click="moveItem(row, 1)">
                  <AppIcon name="arrow-down" :size="14" />
                </el-button>
              </div>
            </template>
          </el-table-column>
          <el-table-column label="備註" min-width="140" show-overflow-tooltip>
            <template #default="{ row }">
              <span :class="{ 'cell-empty': !row.remark }">{{ row.remark || '—' }}</span>
            </template>
          </el-table-column>
          <el-table-column label="狀態" width="90" align="center">
            <template #default="{ row }">
              <el-switch
                :model-value="row.enabled"
                :loading="togglingIds.includes(row.id)"
                @change="handleEnabledChange(row, $event)"
              />
            </template>
          </el-table-column>
          <el-table-column label="操作" width="190" align="center" fixed="right" class-name="actions-cell">
            <template #default="{ row }">
              <el-button link type="primary" @click="openItemEdit(row)">編輯</el-button>
              <el-button link type="primary" @click="addChild(row)">加下級</el-button>
              <el-tooltip v-if="isRequiredDict(row)" content="票據上傳流程必需，不能刪除" placement="top">
                <span class="locked-action"><AppIcon name="lock" :size="12" />不可刪除</span>
              </el-tooltip>
              <el-button v-else link type="danger" @click="removeItem(row)">刪除</el-button>
            </template>
          </el-table-column>
          <template #empty>
            <el-empty :description="itemKeyword || statusFilter !== 'all' ? '沒有符合的字典項' : '此分組還沒有字典項'">
              <el-button v-if="!itemKeyword && statusFilter === 'all'" type="primary" @click="openItemCreate()">
                新增第一個字典項
              </el-button>
            </el-empty>
          </template>
        </el-table>
      </section>

      <section v-else class="items-panel empty-panel mc-card">
        <el-empty description="先建立分組，再維護分組下的字典項">
          <el-button type="primary" @click="openGroupCreate">建立分組</el-button>
        </el-empty>
      </section>
    </div>

    <!-- 新增 / 編輯分組 -->
    <el-dialog
      v-model="groupDialogVisible"
      :title="editingGroupId ? '編輯分組' : '新增分組'"
      width="440px"
      :close-on-click-modal="false"
      @closed="groupFormRef?.clearValidate()"
    >
      <el-form ref="groupFormRef" :model="groupForm" :rules="groupRules" label-position="top">
        <el-form-item v-if="editingGroupId && editingGroupIsSystem" label="識別碼">
          <el-input :model-value="editingGroupCode" disabled />
          <div class="form-hint">識別碼由程式使用，建立後不可修改。</div>
        </el-form-item>
        <el-form-item label="分組名稱" prop="name">
          <el-input v-model="groupForm.name" maxlength="100" placeholder="例如：支出分類" />
        </el-form-item>
        <el-form-item label="說明" prop="remark">
          <el-input
            v-model="groupForm.remark"
            type="textarea"
            :rows="2"
            maxlength="255"
            placeholder="可選，說明此分組的用途"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="groupDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="groupSubmitting" @click="submitGroup">
          {{ editingGroupId ? '保存' : '建立' }}
        </el-button>
      </template>
    </el-dialog>

    <!-- 新增 / 編輯字典項 -->
    <el-dialog
      v-model="itemDialogVisible"
      :title="editingItemId ? '編輯字典項' : '新增字典項'"
      width="480px"
      :close-on-click-modal="false"
      @closed="itemFormRef?.clearValidate()"
    >
      <el-form ref="itemFormRef" :model="itemForm" :rules="itemRules" label-position="top">
        <el-form-item label="分組" prop="groupName">
          <el-select v-model="itemForm.groupName" :disabled="!!editingItemId" filterable style="width: 100%">
            <el-option v-for="group in groups" :key="group.id" :label="group.name" :value="group.code" />
          </el-select>
        </el-form-item>
        <el-form-item label="標籤" prop="label">
          <el-input v-model="itemForm.label" maxlength="100" placeholder="用戶看到的文字，例如：材料" />
        </el-form-item>
        <el-form-item label="上級（可選）">
          <DictTreeSelect
            v-model="itemForm.parentId"
            :dicts="parentDicts"
            clearable
            placeholder="不選 = 頂層"
            :disabled="!!editingItemId && childIds.has(editingItemId)"
          />
          <div class="form-hint">
            例如地盤「牛房」是頂層，「一樓」「二樓」的上級選「牛房」；識別碼會自動編成 1.1、1.2。
            有下級的項要先清空下級才能換上級。
          </div>
        </el-form-item>
        <el-form-item label="識別碼">
          <el-input :model-value="editingItemId ? itemForm.value : nextValuePreview" disabled />
          <div class="form-hint">
            <template v-if="editingItemId">
              識別碼由系統按層級自動生成（頂層 1、2，下級 1.1、1.2），不可手動修改；換上級時會自動重新編號。
            </template>
            <template v-else>由系統自動編號（頂層 1、2、3…，下級 1.1、1.2…），不需要手動輸入。</template>
          </div>
        </el-form-item>
        <el-form-item v-if="editingItemId" label="排序" prop="sort">
          <el-input-number v-model="itemForm.sort" :min="0" :precision="0" />
          <div class="form-hint">數字越小越靠前。</div>
        </el-form-item>
        <el-form-item label="啟用" prop="enabled">
          <el-switch v-model="itemForm.enabled" />
          <span class="form-inline-hint">停用後不會出現在新表單的選項中，歷史資料保留。</span>
        </el-form-item>
        <el-form-item label="備註" prop="remark">
          <el-input v-model="itemForm.remark" type="textarea" :rows="2" maxlength="255" placeholder="可選" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="itemDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="itemSubmitting" @click="submitItem">
          {{ editingItemId ? '保存' : '建立' }}
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.categories-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.page-title {
  margin: 0 0 4px;
  font-size: 24px;
  font-weight: 600;
}

.page-desc {
  margin: 0;
  color: var(--mc-text-secondary);
}

.page-summary {
  flex: none;
  padding-top: 4px;
  color: var(--mc-text-secondary);
  font-variant-numeric: tabular-nums;
}

.dict-shell {
  display: grid;
  grid-template-columns: 272px minmax(0, 1fr);
  gap: 16px;
  align-items: stretch;
}

.group-panel {
  display: flex;
  flex-direction: column;
  min-height: 520px;
  overflow: hidden;
}

.group-panel-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 16px 8px;
}

.group-panel-title {
  font-size: 15px;
  font-weight: 600;
}

.group-search {
  padding: 0 12px 8px;
}

.group-list {
  flex: 1;
  overflow-y: auto;
  padding: 0 8px 12px;
}

.group-item {
  display: grid;
  grid-template-columns: minmax(0, 1fr) auto;
  width: 100%;
  margin-bottom: 2px;
  padding: 9px 12px;
  border: 0;
  border-radius: var(--mc-radius);
  background: transparent;
  color: var(--mc-text);
  font: inherit;
  text-align: left;
  cursor: pointer;
  transition: background-color 0.2s ease;
}

.group-item:hover {
  background: var(--mc-bg);
}

.group-item.active {
  background: var(--mc-primary-bg);
}

.group-name {
  overflow: hidden;
  font-weight: 500;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.group-count {
  min-width: 24px;
  padding: 0 7px;
  border-radius: 999px;
  background: var(--mc-bg);
  color: var(--mc-text-secondary);
  font-size: 12px;
  line-height: 20px;
  text-align: center;
  font-variant-numeric: tabular-nums;
}

.group-item.active .group-count {
  background: var(--mc-card);
  color: var(--mc-primary);
}

.items-panel {
  display: flex;
  flex-direction: column;
  min-height: 520px;
  padding: 16px;
}

.items-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.items-title {
  display: flex;
  align-items: center;
  gap: 8px;
}

.items-title h2 {
  margin: 0;
  font-size: 17px;
  font-weight: 600;
}

.items-desc {
  margin: 4px 0 0;
  color: var(--mc-text-secondary);
  font-size: 13px;
}

.items-actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 4px;
}

.items-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  margin: 16px 0 12px;
}

.required-alert {
  margin-top: 16px;
}

.search-input {
  width: 280px;
}

.dict-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-weight: 400;
}

/* 有下級的項加粗，配合縮進一眼看出層級 */
.dict-parent {
  font-weight: 600;
}

.sort-buttons {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 2px;
}

.sort-buttons :deep(.el-button + .el-button) {
  margin-left: 0;
}

.dict-code {
  color: var(--mc-text-secondary);
  font-size: 13px;
  font-variant-numeric: tabular-nums;
}

.cell-empty {
  color: var(--mc-text-secondary);
}

.tree-switch {
  display: flex;
  align-items: center;
  margin-left: auto;
  white-space: nowrap;
}

.locked-action {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  color: var(--mc-text-secondary);
  font-size: 12px;
  white-space: nowrap;
  cursor: not-allowed;
}

:deep(.actions-cell .cell) {
  white-space: nowrap;
}

.empty-panel {
  align-items: center;
  justify-content: center;
}

.form-hint {
  margin-top: 4px;
  color: var(--mc-text-secondary);
  font-size: 12px;
  line-height: 1.5;
}

.form-inline-hint {
  margin-left: 10px;
  color: var(--mc-text-secondary);
  font-size: 12px;
}

@media (max-width: 1280px) {
  .dict-shell {
    grid-template-columns: 1fr;
  }

  .group-panel {
    min-height: 0;
  }

  .group-list {
    display: flex;
    flex-wrap: wrap;
    gap: 4px;
  }

  .group-item {
    width: auto;
    min-width: 160px;
  }

  .items-panel {
    min-height: 0;
  }

  .items-header,
  .items-toolbar {
    flex-wrap: wrap;
  }
}
</style>
