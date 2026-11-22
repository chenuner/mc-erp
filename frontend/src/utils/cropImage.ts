/**
 * 圖片裁剪工具（會計票據原圖用）。
 *
 * 純 Canvas + ImageBitmap，不引入任何裁剪庫（specs/tech-stack.md 未列出的依賴不能用）。
 * 流程：解碼（手機照片按 EXIF 自動轉正）→ 產生預覽用的縮圖 → 使用者拉框 →
 * 按原圖像素裁切 → 輸出 JPEG（必要時逐級降質、縮小，保證不超過後端 4 MB 上限）。
 */

/** 後端單圖上限（ReceiptService.CheckImageAsync）；留一點餘量，避免邊界值被拒。 */
export const MAX_UPLOAD_BYTES = 4 * 1024 * 1024
const SAFE_UPLOAD_BYTES = MAX_UPLOAD_BYTES - 64 * 1024
/** 輸出 JPEG 的品質序列：由高到低，第一個塞得進上限的就用。 */
const QUALITY_STEPS = [0.92, 0.85, 0.75, 0.65, 0.55]

/** 矩形：x / y / width / height 都用同一套座標系（原圖像素或預覽像素，看呼叫處）。 */
export interface CropRect {
  x: number
  y: number
  width: number
  height: number
}

export interface CropResult {
  blob: Blob
  width: number
  height: number
}

export interface LoadedImage {
  /** 已按 EXIF 方向轉正；用完要 bitmap.close()。 */
  bitmap: ImageBitmap
  /** 原圖寬高（轉正後）。 */
  width: number
  height: number
  /** 預覽用的縮圖（等比縮到 maxEdge 以內），裁剪框在它上面拖。 */
  preview: HTMLCanvasElement
}

const clamp = (value: number, min: number, max: number) => Math.min(Math.max(value, min), max)

/**
 * 把矩形夾回圖片邊界並取整。
 * 先夾左上角，再讓寬高不超出右 / 下邊，所以任何輸入都回得出合法的整數區域。
 */
export function clampRect(rect: CropRect, width: number, height: number): CropRect {
  const x = clamp(Math.round(rect.x), 0, Math.max(0, width - 1))
  const y = clamp(Math.round(rect.y), 0, Math.max(0, height - 1))
  const right = clamp(Math.round(rect.x + rect.width), Math.min(x + 1, width), width)
  const bottom = clamp(Math.round(rect.y + rect.height), Math.min(y + 1, height), height)
  return { x, y, width: right - x, height: bottom - y }
}

/** 預覽圖座標 → 原圖像素。scale = 原圖寬 / 預覽顯示寬。 */
export function toSourceRect(rect: CropRect, scale: number, width: number, height: number): CropRect {
  return clampRect({ x: rect.x * scale, y: rect.y * scale, width: rect.width * scale, height: rect.height * scale },
    width, height)
}

/**
 * 依比例修正矩形：保持中心點不變，寬高按 ratio（w / h）調整，再夾回邊界。
 * ratio 為 null 代表自由裁剪。返回的矩形永遠在 (0, 0, width, height) 之內。
 */
export function fitRatio(rect: CropRect, ratio: number | null, width: number, height: number): CropRect {
  const centerX = rect.x + rect.width / 2
  const centerY = rect.y + rect.height / 2
  let w = Math.max(1, rect.width)
  let h = Math.max(1, rect.height)
  if (ratio) {
    if (w / h > ratio) w = h * ratio
    else h = w / ratio
    // 超出邊界時先縮到放得下，比例不變
    const shrink = Math.min(1, width / w, height / h)
    w *= shrink
    h *= shrink
  }
  const x = clamp(centerX - w / 2, 0, Math.max(0, width - w))
  const y = clamp(centerY - h / 2, 0, Math.max(0, height - h))
  return { x, y, width: w, height: h }
}

/** 圖片中央、指定比例的初始框；ratio 為 null 時留 90% 邊距。 */
export function initialRect(width: number, height: number, ratio: number | null): CropRect {
  const w = ratio ? (width / height > ratio ? height * ratio : width) : width * 0.9
  const h = ratio ? w / ratio : height * 0.9
  return { x: (width - w) / 2, y: (height - h) / 2, width: w, height: h }
}

/** 解碼一張圖並轉正；瀏覽器沒有 ImageBitmap 時退回 <img>。 */
async function decode(file: Blob): Promise<{ width: number; height: number; source: CanvasImageSource }> {
  if (typeof createImageBitmap === 'function') {
    const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' })
    return { width: bitmap.width, height: bitmap.height, source: bitmap }
  }
  const url = URL.createObjectURL(file)
  try {
    const image = await new Promise<HTMLImageElement>((resolve, reject) => {
      const element = new Image()
      element.onload = () => resolve(element)
      element.onerror = () => reject(new Error('無法讀取圖片'))
      element.src = url
    })
    return { width: image.naturalWidth, height: image.naturalHeight, source: image }
  } finally {
    URL.revokeObjectURL(url)
  }
}

function paint(source: CanvasImageSource, width: number, height: number, maxEdge: number) {
  const scale = Math.min(1, maxEdge / Math.max(1, width, height))
  const canvas = document.createElement('canvas')
  canvas.width = Math.max(1, Math.round(width * scale))
  canvas.height = Math.max(1, Math.round(height * scale))
  const context = canvas.getContext('2d')
  if (!context) throw new Error('瀏覽器不支援圖片裁剪')
  context.imageSmoothingQuality = 'high'
  context.drawImage(source, 0, 0, canvas.width, canvas.height)
  return canvas
}

/**
 * 讀入使用者選的圖片：回傳轉正後的原圖尺寸與預覽縮圖。
 * 手機原圖可能上萬像素，預覽一律縮到 previewMaxEdge 以內；真正落盤時只渲染使用者框住的區域。
 */
export async function loadImage(file: Blob, previewMaxEdge = 1280): Promise<LoadedImage> {
  const { width, height, source } = await decode(file)
  return {
    bitmap: source as ImageBitmap,
    width,
    height,
    preview: paint(source, width, height, previewMaxEdge),
  }
}

function canvasToBlob(canvas: HTMLCanvasElement, quality: number): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob(blob => blob ? resolve(blob) : reject(new Error('圖片輸出失敗')), 'image/jpeg', quality)
  })
}

/** 逐級降質，仍超過上限就縮小尺寸重試；保證回傳 ≤ 後端上限。 */
async function encode(source: HTMLCanvasElement): Promise<Blob> {
  let canvas = source
  for (let attempt = 0; attempt < 8; attempt++) {
    for (const quality of QUALITY_STEPS) {
      const blob = await canvasToBlob(canvas, quality)
      if (blob.size <= SAFE_UPLOAD_BYTES) return blob
    }
    const longer = Math.max(canvas.width, canvas.height)
    // 尺寸按 0.85 縮（像素約 0.72 倍），一般 1–2 輪就夠
    if (longer <= 1200) break
    canvas = paint(canvas, canvas.width, canvas.height, Math.round(longer * 0.85))
  }
  throw new Error('圖片過大，裁剪後仍超過 4 MB，請縮小裁剪範圍')
}

/**
 * 按原圖像素座標裁切並輸出 JPEG（先填白底，PNG 透明區域不會變黑）。
 * rect 超出邊界會自動夾回；輸出保證 ≤ MAX_UPLOAD_BYTES。
 */
export async function cropImage(image: LoadedImage, rect: CropRect): Promise<CropResult> {
  const area = clampRect(rect, image.width, image.height)
  const canvas = document.createElement('canvas')
  canvas.width = area.width
  canvas.height = area.height
  const context = canvas.getContext('2d')
  if (!context) throw new Error('瀏覽器不支援圖片裁剪')
  context.fillStyle = '#ffffff'
  context.fillRect(0, 0, area.width, area.height)
  context.imageSmoothingQuality = 'high'
  context.drawImage(image.bitmap, area.x, area.y, area.width, area.height, 0, 0, area.width, area.height)
  return { blob: await encode(canvas), width: area.width, height: area.height }
}

/** 裁切結果的檔名：沿用原檔名，副檔名改成 .jpg（輸出是 JPEG）。 */
export function croppedFileName(original: string) {
  const base = original.replace(/\.[^.]+$/, '').trim() || 'receipt'
  return `${base}.jpg`
}

/** 包成 File 給上傳接口；後端按檔頭判斷類型，這裡的 type 只影響表單。 */
export function toJpegFile(result: CropResult, originalName: string) {
  return new File([result.blob], croppedFileName(originalName), { type: 'image/jpeg', lastModified: Date.now() })
}
