import type { SysDictDto } from '../api/system'

/** 樹形下拉的節點（給 el-tree-select 用）。分組節點只是標題，沒有對應字典，sysDict 為 null。 */
export interface DictTreeNode {
  value: string
  label: string
  disabled: boolean
  children: DictTreeNode[]
  sysDict: SysDictDto | null
}

/**
 * 把同一分組的字典項排成樹：有上級的項掛在上級底下，其餘當根節點。
 * 上級不在這批資料裡（被刪或沒載入）就當根節點，不能讓它憑空消失。
 * 同層按識別碼自然順序排，1.1 排在 1.2 前、1.10 排在 1.9 後。
 */
export function dictTree(dicts: SysDictDto[]): DictTreeNode[] {
  const byId = new Map(dicts.map((dict) => [dict.id, dict]))
  const childrenOf = new Map<string | null, SysDictDto[]>()
  for (const dict of dicts) {
    const parentId = attachedParentId(dict, byId)
    const siblings = childrenOf.get(parentId) ?? []
    siblings.push(dict)
    childrenOf.set(parentId, siblings)
  }
  for (const siblings of childrenOf.values()) siblings.sort((left, right) => compareCode(left.value, right.value))

  const build = (parentId: string | null, ancestors: ReadonlySet<string>): DictTreeNode[] =>
    (childrenOf.get(parentId) ?? [])
      .filter((dict) => !ancestors.has(dict.id))
      .map((dict) => ({
        value: dict.id,
        label: dict.label,
        disabled: !dict.enabled,
        sysDict: dict,
        children: build(dict.id, new Set([...ancestors, dict.id])),
      }))
  return build(null, new Set())
}

/** 上級真的存在、而且往上找不會走回自己（成環）時才掛上去，否則當根節點。 */
function attachedParentId(dict: SysDictDto, byId: Map<string, SysDictDto>): string | null {
  const seen = new Set([dict.id])
  let parentId = dict.parentId
  while (parentId) {
    if (seen.has(parentId)) return null
    const parent = byId.get(parentId)
    if (!parent) return null
    seen.add(parent.id)
    parentId = parent.parentId
  }
  return dict.parentId
}

/** 「1.10」要排成 1.10 而不是字串排序的 1.10 < 1.9，所以逐段比較數字。 */
function compareCode(left: string, right: string): number {
  const leftParts = left.split('.')
  const rightParts = right.split('.')
  const length = Math.max(leftParts.length, rightParts.length)
  for (let index = 0; index < length; index += 1) {
    const leftPart = leftParts[index] ?? ''
    const rightPart = rightParts[index] ?? ''
    const leftNumber = Number.parseInt(leftPart, 10)
    const rightNumber = Number.parseInt(rightPart, 10)
    if (Number.isNaN(leftNumber) || Number.isNaN(rightNumber)) {
      const text = leftPart.localeCompare(rightPart)
      if (text !== 0) return text
    } else if (leftNumber !== rightNumber) {
      return leftNumber - rightNumber
    }
  }
  return 0
}

/** 由字典項往上找每一層上級，回傳「牛房 / 一樓」這樣的完整路徑。 */
export function dictPath(dict: SysDictDto, dicts: SysDictDto[]): string {
  const byId = new Map(dicts.map((item) => [item.id, item]))
  const labels = [dict.label]
  const seen = new Set([dict.id])
  let parentId = dict.parentId
  while (parentId && !seen.has(parentId)) {
    const parent = byId.get(parentId)
    if (!parent) break
    labels.unshift(parent.label)
    seen.add(parent.id)
    parentId = parent.parentId
  }
  return labels.join(' / ')
}

/**
 * 清單顯示用的名稱：能用 id 找到字典項就顯示完整路徑（牛房 / 一樓），
 * 免得兩個上級底下同名的「一樓」分不清；找不到就退回後端保存的名稱快照。
 */
export function dictText(id: string | null | undefined, fallback: string | null | undefined,
  dicts: SysDictDto[]): string {
  const dict = id ? dicts.find((item) => item.id === id) : undefined
  return (dict ? dictPath(dict, dicts) : fallback) || '—'
}
