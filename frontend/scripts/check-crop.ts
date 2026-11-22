/**
 * 票據裁剪工具的純函數檢查（不依賴 DOM / Canvas）。
 * 跑法：pnpm -C frontend run check
 *
 * 用 Node 24 的原生 TypeScript 型別剝離直接 import src/utils/cropImage.ts，
 * 不改前端建置設定、不引入測試框架（specs/tech-stack.md 沒有的依賴不能用）。
 */
import assert from 'node:assert/strict'
import { clampRect, croppedFileName, fitRatio, initialRect, toSourceRect, MAX_UPLOAD_BYTES } from '../src/utils/cropImage.ts'

let checks = 0
const check = (condition: boolean, message: string) => {
  assert.ok(condition, message)
  checks++
}

// clampRect：框超出圖片邊界時夾回，尺寸至少 1px 且不為負
check(JSON.stringify(clampRect({ x: -20, y: -5, width: 50, height: 40 }, 100, 80)) === '{"x":0,"y":0,"width":30,"height":35}',
  'clampRect trims overflow on the top-left')
check(JSON.stringify(clampRect({ x: 90, y: 70, width: 50, height: 40 }, 100, 80)) === '{"x":90,"y":70,"width":10,"height":10}',
  'clampRect trims overflow on the bottom-right')
check(JSON.stringify(clampRect({ x: 500, y: 500, width: 10, height: 10 }, 100, 80)) === '{"x":99,"y":79,"width":1,"height":1}',
  'clampRect keeps at least one pixel inside the image')
const tiny = clampRect({ x: 10.4, y: 10.6, width: 0.2, height: 0.2 }, 100, 100)
check(tiny.width >= 1 && tiny.height >= 1, 'clampRect never produces a zero-size crop')

// toSourceRect：預覽世界 → 原圖像素；scale = 原圖寬 / 預覽顯示寬
check(JSON.stringify(toSourceRect({ x: 10, y: 20, width: 100, height: 50 }, 4, 4000, 3000))
  === '{"x":40,"y":80,"width":400,"height":200}', 'toSourceRect scales preview coordinates to source pixels')
check(toSourceRect({ x: 0, y: 0, width: 2000, height: 2000 }, 4, 4000, 3000).height === 3000,
  'toSourceRect never exceeds the source height')

// fitRatio：鎖比例後保持中心、不超出邊界
const locked = fitRatio({ x: 10, y: 10, width: 200, height: 100 }, 1, 300, 200)
check(Math.abs(locked.width - locked.height) < 0.001, 'fitRatio makes the rect square')
check(Math.abs((locked.x + locked.width / 2) - 110) < 0.001 && Math.abs((locked.y + locked.height / 2) - 60) < 0.001,
  'fitRatio keeps the centre')
const oversized = fitRatio({ x: 0, y: 0, width: 300, height: 300 }, 16 / 9, 300, 200)
check(oversized.x >= 0 && oversized.y >= 0 && oversized.x + oversized.width <= 300.001 && oversized.y + oversized.height <= 200.001,
  'fitRatio shrinks to fit the image instead of leaving the bounds')
check(Math.abs(oversized.width / oversized.height - 16 / 9) < 0.001, 'fitRatio keeps the aspect ratio when shrinking')
const free = fitRatio({ x: 0, y: 0, width: 120, height: 80 }, null, 300, 200)
check(free.width === 120 && free.height === 80, 'fitRatio leaves free cropping untouched')

// initialRect：預設框置中
const centered = initialRect(400, 200, 1)
check(centered.width === 200 && centered.height === 200 && centered.x === 100 && centered.y === 0,
  'initialRect centres the default square')
const freeStart = initialRect(400, 200, null)
check(Math.abs(freeStart.width - 360) < 0.001 && Math.abs(freeStart.x - 20) < 0.001, 'initialRect leaves a 5% margin when free')

// 檔名與上限常數
check(croppedFileName('IMG_0001.HEIC.jpg') === 'IMG_0001.HEIC.jpg', 'croppedFileName keeps a single extension')
check(croppedFileName('scan.png') === 'scan.jpg', 'croppedFileName switches to .jpg')
check(croppedFileName('  ') === 'receipt.jpg', 'croppedFileName falls back to receipt.jpg')
check(MAX_UPLOAD_BYTES === 4 * 1024 * 1024, 'MAX_UPLOAD_BYTES matches the backend limit')

console.log(`PASS: ${checks} crop checks (no DOM)`)
