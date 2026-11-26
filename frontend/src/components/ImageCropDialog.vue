<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, reactive, ref, watch } from 'vue'
import { ElMessage } from 'element-plus'
import AppIcon from '@/components/AppIcon.vue'
import { cropImage, fitRatio, initialRect, loadImage, MAX_UPLOAD_BYTES, toJpegFile, toSourceRect,
  type CropRect, type LoadedImage } from '@/utils/cropImage'

/**
 * 通用圖片裁剪對話框：給定一張圖，拉框裁出需要的部分，回傳裁好的 JPEG File。
 * 純 Canvas 實現（不引入裁剪庫）。遮罩用「超大陰影挖洞」，不做 CSS transform，座標換算簡單不易錯。
 */
const props = withDefaults(defineProps<{
  /** 要裁剪的原圖；null 時顯示空狀態。 */
  source: Blob | null
  /** 來源檔名，裁切結果沿用主檔名（副檔名改 .jpg）。 */
  fileName?: string
  /** 預設比例（w / h），null 為自由裁剪。 */
  defaultRatio?: number | null
  /** 提示文字，例如「請把票據四邊拉進框內」。 */
  hint?: string
}>(), { fileName: 'receipt.jpg', defaultRatio: null, hint: '' })

const emit = defineEmits<{ confirm: [file: File] }>()
const visible = defineModel<boolean>({ default: false })

type Handle = 'nw' | 'n' | 'ne' | 'e' | 'se' | 's' | 'sw' | 'w'

interface Snap {
  pointerX: number
  pointerY: number
  rect: CropRect
  /** null 代表從框內拖曳（整體平移）。 */
  handle: Handle | null
}

interface PreviewState extends LoadedImage {
  /** 預覽畫布的顯示寬高（CSS 像素）。 */
  shownWidth: number
  shownHeight: number
  /** 原圖轉成 ObjectURL 用的 Blob，旋轉時要重新解碼。 */
  blob: Blob
}

const ratios: { value: number | null; label: string }[] = [
  { value: null, label: '自由' },
  { value: 1, label: '1:1' },
  { value: 4 / 3, label: '4:3' },
  { value: 3 / 4, label: '3:4' },
  { value: 16 / 9, label: '16:9' },
]
/** 裁剪框最小邊（CSS 像素），太小會抓不住控制點。 */
const MIN_SIZE = 16

const stage = ref<HTMLDivElement>()
const canvasRef = ref<HTMLCanvasElement>()
const state = ref<PreviewState | null>(null)
const rect = reactive<CropRect>({ x: 0, y: 0, width: 1, height: 1 })
const ratio = ref<number | null>(props.defaultRatio)
const loading = ref(false)
const busy = ref(false)
const error = ref('')
const progress = ref('')
const snap = ref<Snap | null>(null)

const handles: Handle[] = ['nw', 'n', 'ne', 'e', 'se', 's', 'sw', 'w']
/** 每個控制點拖動時哪幾條邊跟著動：[左 1 / 右 -1 / 不動 0, 上 1 / 下 -1 / 不動 0]。 */
const directions: Record<Handle, [number, number]> = {
  nw: [1, 1], n: [0, 1], ne: [-1, 1], e: [-1, 0], se: [-1, -1], s: [0, -1], sw: [1, -1], w: [1, 0],
}

const boxStyle = computed(() => ({
  left: `${Math.round(rect.x)}px`, top: `${Math.round(rect.y)}px`,
  width: `${Math.round(rect.width)}px`, height: `${Math.round(rect.height)}px`,
}))
/** 裁切結果的實際像素尺寸，讓使用者知道上傳的是多大。 */
const resultSize = computed(() => {
  const current = state.value
  if (!current) return ''
  const scale = current.width / current.shownWidth
  return `${Math.round(rect.width * scale)} × ${Math.round(rect.height * scale)} px`
})
const limitText = `輸出 JPEG，上限 ${Math.round(MAX_UPLOAD_BYTES / 1024 / 1024)} MB`

/** 按容器寬度排版預覽，並把縮圖畫到畫布上；首屏和視窗縮放都走這裡。 */
async function layout() {
  const current = state.value
  const element = stage.value
  if (!current || !element) return
  const available = Math.max(160, element.clientWidth)
  const maxHeight = Math.max(240, Math.min(520, Math.round(window.innerHeight * 0.55)))
  const scale = Math.min(available / current.preview.width, maxHeight / current.preview.height, 1)
  current.shownWidth = Math.max(1, Math.round(current.preview.width * scale))
  current.shownHeight = Math.max(1, Math.round(current.preview.height * scale))
  await nextTick()
  const canvas = canvasRef.value
  const context = canvas?.getContext('2d')
  if (!canvas || !context) return
  canvas.width = current.shownWidth
  canvas.height = current.shownHeight
  context.imageSmoothingQuality = 'high'
  context.drawImage(current.preview, 0, 0, canvas.width, canvas.height)
  applyRatio()
}

function applyRatio() {
  const current = state.value
  if (!current) return
  Object.assign(rect, fitRatio(rect, ratio.value, current.shownWidth, current.shownHeight))
}

function show(image: LoadedImage, blob: Blob) {
  state.value = { ...image, blob, shownWidth: 1, shownHeight: 1 }
  Object.assign(rect, initialRect(image.preview.width, image.preview.height, ratio.value))
  void layout()
}

function release() {
  state.value?.bitmap.close()
  state.value = null
}

async function load(source: Blob | null) {
  release()
  error.value = ''
  progress.value = ''
  if (!source) return
  loading.value = true
  try {
    show(await loadImage(source), source)
  } catch (exception) {
    error.value = exception instanceof Error ? exception.message : '無法讀取圖片'
  } finally {
    loading.value = false
  }
}

/** 指標移動：框內平移，或按控制點改某一條 / 兩條邊；鎖比例時以拖動位移推另一邊。 */
function move(event: PointerEvent) {
  const current = snap.value
  const image = state.value
  if (!current || !image) return
  const dx = event.clientX - current.pointerX
  const dy = event.clientY - current.pointerY
  const width = image.shownWidth
  const height = image.shownHeight
  if (!current.handle) {
    Object.assign(rect, {
      x: Math.min(Math.max(0, current.rect.x + dx), Math.max(0, width - rect.width)),
      y: Math.min(Math.max(0, current.rect.y + dy), Math.max(0, height - rect.height)),
      width: current.rect.width,
      height: current.rect.height,
    })
    return
  }
  const [horizontal, vertical] = directions[current.handle]
  const base = current.rect
  const anchorX = horizontal === 1 ? base.x + base.width : base.x
  const anchorY = vertical === 1 ? base.y + base.height : base.y
  const draggingX = Math.min(Math.max(0,
    horizontal === 1 ? base.x + dx : horizontal === -1 ? base.x + base.width + dx : base.x), width)
  const draggingY = Math.min(Math.max(0,
    vertical === 1 ? base.y + dy : vertical === -1 ? base.y + base.height + dy : base.y), height)
  let w = horizontal === 1 ? anchorX - draggingX : horizontal === -1 ? draggingX - anchorX : base.width
  let h = vertical === 1 ? anchorY - draggingY : vertical === -1 ? draggingY - anchorY : base.height

  if (ratio.value) {
    const limitW = Math.min(horizontal === 1 ? anchorX : width - anchorX, (vertical === 1 ? anchorY : height - anchorY) * ratio.value)
    if (horizontal && vertical) w = Math.max(w, h * ratio.value)
    else if (horizontal) w = Math.max(w, base.height * ratio.value)
    else h = Math.max(h, base.width / ratio.value)
    w = Math.min(w, limitW)
    h = w / ratio.value
  }
  // 用正值算尺寸，再按拖動方向反推座標，框不會翻面
  w = Math.max(MIN_SIZE, w)
  h = Math.max(MIN_SIZE, h)
  const x = horizontal === 1 ? anchorX - w : horizontal === -1 ? anchorX : base.x
  const y = vertical === 1 ? anchorY - h : vertical === -1 ? anchorY : base.y
  Object.assign(rect, fitRatio({ x, y, width: w, height: h }, ratio.value, width, height))
}

function start(event: PointerEvent, handle: Handle | null = null) {
  if (!state.value || event.button !== 0) return
  event.preventDefault()
  event.stopPropagation()
  ;(event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId)
  snap.value = { pointerX: event.clientX, pointerY: event.clientY, rect: { ...rect }, handle }
}

function end() {
  snap.value = null
}

function setRatio(value: number | null) {
  ratio.value = value
  applyRatio()
}

/** 轉正手機拍橫的照片：整張原圖與預覽一起轉 90°，再按原比例重排。 */
async function rotate() {
  const current = state.value
  if (!current || busy.value) return
  busy.value = true
  progress.value = '正在旋轉…'
  try {
    const source = document.createElement('canvas')
    source.width = current.width
    source.height = current.height
    source.getContext('2d')?.drawImage(current.bitmap, 0, 0)
    current.bitmap.close()
    const rotated = document.createElement('canvas')
    rotated.width = current.height
    rotated.height = current.width
    const context = rotated.getContext('2d')
    if (!context) throw new Error('瀏覽器不支援圖片旋轉')
    context.translate(rotated.width / 2, rotated.height / 2)
    context.rotate(Math.PI / 2)
    context.drawImage(source, -source.width / 2, -source.height / 2)
    const blob = await new Promise<Blob>((resolve, reject) =>
      rotated.toBlob(result => result ? resolve(result) : reject(new Error('圖片輸出失敗')), 'image/jpeg', 0.95))
    show(await loadImage(blob), blob)
  } catch (exception) {
    ElMessage.error(exception instanceof Error ? exception.message : '旋轉失敗')
  } finally {
    busy.value = false
    progress.value = ''
  }
}

async function confirm() {
  const image = state.value
  if (!image || busy.value) return
  busy.value = true
  progress.value = '正在裁剪…'
  // 大圖縮放與編碼要一兩秒，先讓遮罩畫出來
  await new Promise(resolve => window.setTimeout(resolve, 30))
  try {
    const scale = image.width / image.shownWidth
    const result = await cropImage(image, toSourceRect(rect, scale, image.width, image.height))
    emit('confirm', toJpegFile(result, props.fileName))
    ElMessage.success(`已裁剪為 ${result.width} × ${result.height} px`)
    visible.value = false
  } catch (exception) {
    ElMessage.error(exception instanceof Error ? exception.message : '裁剪失敗，請重試')
  } finally {
    busy.value = false
    progress.value = ''
  }
}

watch(() => props.source, source => { if (visible.value) void load(source) })
watch(visible, open => { if (open) void load(props.source); else release() })
watch(ratio, () => applyRatio())

const onResize = () => { if (visible.value) void layout() }
window.addEventListener('resize', onResize)
onBeforeUnmount(() => {
  window.removeEventListener('resize', onResize)
  release()
})
</script>

<template>
  <el-dialog v-model="visible" title="裁剪圖片" width="min(760px, calc(100vw - 32px))" append-to-body
    :show-close="!busy" :close-on-click-modal="!busy" :close-on-press-escape="!busy" @opened="layout">
    <p v-if="hint" class="crop-hint">{{ hint }}</p>
    <div v-loading="loading || busy" :element-loading-text="progress || '讀取圖片…'" class="crop-workbench">
      <div ref="stage" class="crop-stage">
        <div v-if="state" class="crop-canvas" :style="{ width: `${state.shownWidth}px`, height: `${state.shownHeight}px` }"
          @pointerdown="start" @pointermove="move" @pointerup="end" @pointercancel="end">
          <canvas ref="canvasRef" class="crop-preview" />
          <div class="crop-box" :style="boxStyle">
            <span v-for="handle in handles" :key="handle" :class="['crop-handle', `crop-handle--${handle}`]"
              @pointerdown="start($event, handle)" @pointermove="move" @pointerup="end" @pointercancel="end" />
          </div>
        </div>
        <el-empty v-else-if="error" :description="error" :image-size="72" />
        <p v-else-if="!loading" class="crop-hint">尚未選擇圖片</p>
      </div>
      <div class="crop-toolbar">
        <el-radio-group :model-value="ratio" size="small" role="group" aria-label="裁剪比例"
          @change="(value: string | number | boolean | undefined) => setRatio(value as number | null)">
          <el-radio-button v-for="item in ratios" :key="String(item.value)" :value="item.value">{{ item.label }}</el-radio-button>
        </el-radio-group>
        <el-button size="small" :disabled="busy || loading || !state" @click="rotate"><AppIcon name="refresh-cw" :size="14" />旋轉 90°</el-button>
        <span class="crop-meta">{{ resultSize }} · {{ limitText }}</span>
      </div>
      <p class="crop-tip">拖曳框內可移動，拖曳四角 / 四邊可調整大小；框外的部分不會上傳。</p>
    </div>
    <template #footer>
      <el-button :disabled="busy" @click="visible = false">取消</el-button>
      <el-button type="primary" :disabled="busy || loading || !state" :loading="busy" @click="confirm">使用裁切結果</el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.crop-hint { margin: 0 0 12px; font-size: 12px; line-height: 1.6; color: var(--el-text-color-secondary); }
.crop-workbench { display: grid; gap: 12px; }
.crop-stage { display: flex; justify-content: center; align-items: center; min-height: 240px; padding: 8px;
  background: var(--el-fill-color-lighter); border-radius: var(--el-border-radius-base); overflow: hidden; }
.crop-canvas { position: relative; max-width: 100%; touch-action: none; user-select: none; cursor: move; }
.crop-preview { display: block; width: 100%; height: 100%; }
/* 遮罩挖洞：超大 box-shadow 蓋住框外，不用拼四個 div */
.crop-box { position: absolute; border: 1px solid var(--mc-card); cursor: move;
  box-shadow: 0 0 0 9999px rgb(15 23 42 / 55%); }
/* 三分線輔助對齊 */
.crop-box::before, .crop-box::after { content: ''; position: absolute; inset: 0; pointer-events: none; }
.crop-box::before { border-left: 1px solid rgb(255 255 255 / 35%); border-right: 1px solid rgb(255 255 255 / 35%); left: 33.33%; width: 33.34%; }
.crop-box::after { border-top: 1px solid rgb(255 255 255 / 35%); border-bottom: 1px solid rgb(255 255 255 / 35%); top: 33.33%; height: 33.34%; }
.crop-handle { position: absolute; width: 14px; height: 14px; background: var(--mc-card); border: 2px solid var(--el-color-primary);
  border-radius: 3px; }
.crop-handle--nw { left: -8px; top: -8px; cursor: nwse-resize; }
.crop-handle--n { left: calc(50% - 7px); top: -8px; cursor: ns-resize; }
.crop-handle--ne { right: -8px; top: -8px; cursor: nesw-resize; }
.crop-handle--e { right: -8px; top: calc(50% - 7px); cursor: ew-resize; }
.crop-handle--se { right: -8px; bottom: -8px; cursor: nwse-resize; }
.crop-handle--s { left: calc(50% - 7px); bottom: -8px; cursor: ns-resize; }
.crop-handle--sw { left: -8px; bottom: -8px; cursor: nesw-resize; }
.crop-handle--w { left: -8px; top: calc(50% - 7px); cursor: ew-resize; }
.crop-toolbar { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
.crop-toolbar .el-button { gap: 6px; margin-left: 0; }
.crop-meta { font-size: 12px; font-variant-numeric: tabular-nums; color: var(--el-text-color-secondary); }
.crop-tip { margin: 0; font-size: 12px; line-height: 1.6; color: var(--el-text-color-secondary); }
@media (max-width: 520px) { .crop-meta { width: 100%; } }
</style>
