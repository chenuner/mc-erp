/**
 * 系統字典多層結構的純函數檢查（不依賴 DOM）。
 * 跑法：pnpm -C frontend run check
 *
 * 用 Node 24 的原生 TypeScript 型別剝離直接 import src/utils/dicts.ts，
 * 不改前端建置設定、不引入測試框架（specs/tech-stack.md 沒有的依賴不能用）。
 */
import assert from 'node:assert/strict'
import { dictPath, dictText, dictTree, type DictTreeNode } from '../src/utils/dicts.ts'

const dict = (id: string, label: string, value: string, parentId: string | null = null, enabled = true) =>
  ({ id, groupName: 'AccountingSiteCategory', label, value, sort: 0, enabled, remark: null, parentId })

let checks = 0
const check = (condition: boolean, message: string) => {
  assert.ok(condition, message)
  checks++
}

/** 把樹壓平成「值(層級)」字串，方便比對順序。 */
const flatten = (nodes: DictTreeNode[], depth = 0): string[] =>
  nodes.flatMap((node) => [`${node.sysDict?.value ?? node.label}@${depth}`, ...flatten(node.children, depth + 1)])

// 牛房（1）／一樓（1.1）／二樓（1.2）／貨倉（2）
const sites = [
  dict('barn', '牛房', '1'),
  dict('floor1', '一樓', '1.1', 'barn'),
  dict('floor2', '二樓', '1.2', 'barn'),
  dict('warehouse', '貨倉', '2'),
]
const tree = dictTree(sites)
check(tree.length === 2, 'top-level dicts become root nodes')
check(tree[0].label === '牛房' && tree[0].children.length === 2, 'children are nested under the parent')
check(tree[0].label === '牛房' && !tree[0].label.includes('·'), 'tree labels stay plain; the tree itself shows the hierarchy')
check(tree[0].children[0].sysDict?.id === 'floor1', 'node keeps the whole dict')
check(flatten(tree).join(',') === '1@0,1.1@1,1.2@1,2@0', 'children follow their parent, siblings keep code order')

// 1.10 要排在 1.9 之後（字串排序會排錯）
const many = [dict('a', 'A', '1'), ...Array.from({ length: 10 }, (_, index) => dict(`c${index + 1}`, `F${index + 1}`, `1.${index + 1}`, 'a'))]
const manyCodes = dictTree(many)[0].children.map((node) => node.sysDict?.value)
check(manyCodes.at(-1) === '1.10', 'numeric code segments sort numerically not alphabetically')

// 用戶調過排序值時，同層以 Sort 為先、識別碼為後（1.2 排在 1.1 前面）
const reordered = [
  dict('barn', '牛房', '1'),
  { ...dict('floor1', '一樓', '1.1', 'barn'), sort: 2 },
  { ...dict('floor2', '二樓', '1.2', 'barn'), sort: 1 },
]
check(flatten(dictTree(reordered)).join(',') === '1@0,1.2@1,1.1@1', 'manual sort wins over the code inside one level')

// 上級被停用／不存在時，下級仍要列出來，不能憑空消失
const orphan = dictTree([dict('lost-child', '一樓', '1.1', 'missing')])
check(orphan.length === 1 && orphan[0].label === '一樓', 'child with missing parent becomes a root node')

// 停用的項仍然回傳，但標成 disabled
const disabledTree = dictTree([dict('x', '舊地盤', '1', null, false)])
check(disabledTree[0].disabled === true, 'disabled dict marked as disabled')

// 資料萬一成環（A 上級 B、B 上級 A）也不能無限遞迴，兩個都當根節點
const cyclic = [dict('a', 'A', '1', 'b'), dict('b', 'B', '2', 'a')]
const cyclicTree = dictTree(cyclic)
check(cyclicTree.length === 2 && cyclicTree.every((node) => node.children.length === 0), 'cyclic parents fall back to root nodes')
const nestedCycle = [dict('a', 'A', '1', 'c'), dict('b', 'B', '2', 'a'), dict('c', 'C', '3', 'b')]
check(dictTree(nestedCycle).length === 3, 'a longer cycle also falls back to root nodes')

// dictPath：一路往上拼出完整路徑
check(dictPath(sites[2], sites) === '牛房 / 二樓', 'dictPath joins ancestors with a slash')
check(dictPath(sites[3], sites) === '貨倉', 'dictPath of a top-level dict is its own label')

// dictText：清單顯示用，找得到就顯示路徑，找不到退回名稱快照，兩者皆無顯示 —
check(dictText('floor2', '舊快照', sites) === '牛房 / 二樓', 'dictText prefers the full path when the dict exists')
check(dictText('gone', '舊快照', sites) === '舊快照', 'dictText falls back to the stored label for a missing dict')
check(dictText('gone', null, sites) === '—', 'dictText shows an em dash when nothing is available')
check(dictText(null, null, sites) === '—', 'dictText handles a null id')

console.log(`PASS: ${checks} dict checks (no DOM)`)
