<script setup lang="ts">
import { computed } from 'vue'
import type { SysDictDto, SysDictGroupDto } from '@/api/system'
import { dictPath, dictTree, type DictTreeNode } from '@/utils/dicts'

/**
 * 系統字典的樹形下拉：上級是父節點、下級掛在底下，展開就看得到層級，不用猜「一樓」屬於哪個地盤。
 * 選中下級後輸入框顯示完整路徑（牛房 / 一樓）；停用的項顯示「（停用）」。
 */
const model = defineModel<string>({ default: '' })
const props = withDefaults(
  defineProps<{
    dicts: SysDictDto[]
    /** 給分組時多一層「分組 → 字典」；分組本身只是標題，不能選（文件庫要跨分組選類別）。 */
    groups?: SysDictGroupDto[]
    /** 允許選停用的項：查歷史資料時要能按已停用的地盤／分類篩選。 */
    allowDisabled?: boolean
    /** 已選中的停用項維持可選（否則手一抖清空就再也選不回來）。 */
    keepDisabledValue?: boolean
    /** 額外不可選的識別碼，例如「待分類」不能保存。 */
    disabledCodes?: string[]
    placeholder?: string
    clearable?: boolean
    disabled?: boolean
  }>(),
  {
    groups: undefined,
    allowDisabled: false,
    keepDisabledValue: true,
    disabledCodes: () => [],
    placeholder: '請選擇',
    clearable: false,
    disabled: false,
  },
)

const byId = computed(() => new Map(props.dicts.map((dict) => [dict.id, dict])))
const groupNameByCode = computed(() => new Map((props.groups ?? []).map((group) => [group.code, group.name])))

function isNodeDisabled(node: DictTreeNode): boolean {
  const dict = node.sysDict
  // 分組節點只是標題
  if (!dict) return true
  if (props.disabledCodes.includes(dict.value)) return true
  if (dict.enabled || props.allowDisabled) return false
  // 已選中的停用項維持可選，否則存檔後這筆資料的值就再也選不回來
  return !(props.keepDisabledValue && dict.id === model.value)
}

/** 丟棄字典項時（例如上級被停用）跟著失效的分支：上級停用 = 整條分支都不再用。 */
function ancestorDisabled(dict: SysDictDto): boolean {
  const seen = new Set<string>([dict.id])
  let parentId = dict.parentId
  while (parentId && seen.add(parentId)) {
    const parent = byId.value.get(parentId)
    if (!parent) return false
    if (!parent.enabled) return true
    parentId = parent.parentId
  }
  return false
}

function applyPolicy(nodes: DictTreeNode[], offByAncestor = false): DictTreeNode[] {
  return nodes.map((node) => {
    // allowDisabled（歷史篩選）只看自己：上級停用時仍要能篩到掛在舊子項上的資料
    const disabled = offByAncestor || isNodeDisabled(node)
    return { ...node, disabled, children: applyPolicy(node.children, disabled) }
  })
}

const tree = computed<DictTreeNode[]>(() => {
  const groups = props.groups ?? []
  if (groups.length === 0) return applyPolicy(dictTree(props.dicts))

  const dictsByGroup = new Map<string, SysDictDto[]>()
  for (const dict of props.dicts) {
    const list = dictsByGroup.get(dict.groupName) ?? []
    list.push(dict)
    dictsByGroup.set(dict.groupName, list)
  }
  return groups
    .map((group) => ({
      value: `group:${group.code}`,
      label: group.name,
      disabled: true,
      sysDict: null,
      children: applyPolicy(dictTree(dictsByGroup.get(group.code) ?? [])),
    }))
    .filter((node) => node.children.length > 0)
})

/** 選中後輸入框要顯示的文字：分組 / 上級 / 自己。找不到（字典還沒載入或已刪）就不顯示裸 ID。 */
function selectedText(value: unknown): string {
  const dict = byId.value.get(String(value ?? ''))
  if (!dict) return ''
  const parts = [groupNameByCode.value.get(dict.groupName), dictPath(dict, props.dicts)].filter(
    (part): part is string => !!part,
  )
  if (!dict.enabled) parts.push('（停用）')
  else if (ancestorDisabled(dict)) parts.push('（上級停用）')
  return parts.join(' / ')
}
</script>

<template>
  <el-tree-select
    v-model="model"
    :data="tree"
    :placeholder="placeholder"
    :clearable="clearable"
    :disabled="disabled"
    node-key="value"
    check-strictly
    filterable
    :expand-on-click-node="false"
  >
    <template #label="{ value }">{{ selectedText(value) }}</template>
  </el-tree-select>
</template>
