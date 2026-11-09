<script setup lang="ts">
/**
 * The drawing surface of {@link UiLiveChart}: gridlines, one `<path>` per line, a marker for a line
 * with a single reading, the pointer's crosshair and the picked instant's marker. It knows nothing
 * of time: it reports where the pointer is as a fraction of its own width and the parent turns that
 * into an instant. Strokes are `non-scaling` because the viewBox is stretched to the card. The SVG
 * is `aria-hidden` (a pointer enhancement); the data is in the parent's table.
 */
import { ref, type Ref } from 'vue'
import { LIVE_CHART_GRID_FRACTIONS, LIVE_CHART_HEIGHT, LIVE_CHART_WIDTH } from '../../utils/liveChartGeometry'

/** One line ready to draw. */
export interface UiLiveChartLine {
  /** Stable key: the list key. */
  key: string
  /** The line's position in the series list; picks its colour and dash pattern. */
  index: number
  /** The path data, or `null` when the line has no segment to draw. */
  path: string | null
  /** Where a lone reading is marked, or `null`; one reading cannot make a line. */
  lone: { x: number; y: number } | null
}

/** Props accepted by {@link UiLiveChartPlot}. */
defineProps<{
  /** The lines to draw. */
  lines: UiLiveChartLine[]
  /** The pointer's crosshair as a fraction of the plot width, or `null` for none. */
  crosshairX: number | null
  /** The picked instant's marker as a fraction of the plot width, or `null` for none. */
  selectedX: number | null
}>()

/** Events emitted by {@link UiLiveChartPlot}. */
const emit = defineEmits<{
  /** The pointer moved over the plot, or left it (`null`); carries a fraction of the width in 0-1. */
  (e: 'track', fraction: number | null): void
  /** The reader clicked or tapped the plot; carries a fraction of the width in 0-1. */
  (e: 'pick', fraction: number): void
}>()

/** The plot's SVG, read for its box when the pointer moves. */
const svgElement: Ref<SVGSVGElement | null> = ref(null)

/**
 * Finds where a pointer event lies along the plot.
 * @param event The pointer event over the plot.
 * @returns A fraction of the width in 0-1, or `null` when the plot has no box to measure against.
 */
const fractionOf = (event: PointerEvent | MouseEvent): number | null => {
  const box = svgElement.value?.getBoundingClientRect()
  if (box === undefined || box.width === 0) {
    return null
  }
  return Math.min(Math.max((event.clientX - box.left) / box.width, 0), 1)
}

/**
 * Follows the pointer.
 * @param event The pointer move over the plot.
 * @returns Nothing.
 */
const onPointerMove = (event: PointerEvent): void => {
  emit('track', fractionOf(event))
}

/**
 * Reports that the pointer left.
 * @returns Nothing.
 */
const onPointerLeave = (): void => {
  emit('track', null)
}

/**
 * Reports the picked place.
 * @param event The click on the plot.
 * @returns Nothing.
 */
const onPick = (event: MouseEvent): void => {
  const fraction = fractionOf(event)
  if (fraction !== null) {
    emit('pick', fraction)
  }
}
</script>

<template>
  <svg
    ref="svgElement"
    :viewBox="`0 0 ${LIVE_CHART_WIDTH} ${LIVE_CHART_HEIGHT}`"
    preserveAspectRatio="none"
    class="block h-28 w-full touch-pan-y overflow-visible"
    aria-hidden="true"
    data-testid="live-chart-plot"
    @pointermove="onPointerMove"
    @pointerleave="onPointerLeave"
    @click="onPick"
  >
    <line
      v-for="fraction in LIVE_CHART_GRID_FRACTIONS"
      :key="fraction"
      x1="0"
      :x2="LIVE_CHART_WIDTH"
      :y1="fraction * LIVE_CHART_HEIGHT"
      :y2="fraction * LIVE_CHART_HEIGHT"
      class="ui-live-gridline"
      :class="{ 'ui-live-gridline-base': fraction === 1 }"
    />

    <template v-for="line in lines" :key="line.key">
      <path v-if="line.path !== null" :d="line.path" class="ui-live-line" :class="`ui-live-line-${line.index}`" />
      <circle
        v-if="line.lone !== null"
        :cx="line.lone.x"
        :cy="line.lone.y"
        r="4"
        class="ui-live-marker"
        :class="`ui-live-line-${line.index}`"
      />
    </template>

    <line
      v-if="crosshairX !== null"
      :x1="crosshairX * LIVE_CHART_WIDTH"
      :x2="crosshairX * LIVE_CHART_WIDTH"
      y1="0"
      :y2="LIVE_CHART_HEIGHT"
      class="ui-live-crosshair"
    />
    <line
      v-if="selectedX !== null"
      :x1="selectedX * LIVE_CHART_WIDTH"
      :x2="selectedX * LIVE_CHART_WIDTH"
      y1="0"
      :y2="LIVE_CHART_HEIGHT"
      class="ui-live-selected"
      data-testid="live-chart-selected"
    />
  </svg>
</template>

<style scoped>
/*
 * Six series styles, each a colour AND a dash pattern. The colours are the panel's tokens (the
 * accent is a graphic here, never text), the dashes are what keeps two lines apart without colour.
 */
.ui-live-line {
  fill: none;
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
  vector-effect: non-scaling-stroke;
}

.ui-live-line-0 { stroke: var(--color-accent); }
.ui-live-line-1 { stroke: var(--color-violet); stroke-dasharray: 6 3; }
.ui-live-line-2 { stroke: var(--color-success); stroke-dasharray: 2 3; }
.ui-live-line-3 { stroke: var(--color-warning); stroke-dasharray: 10 4 2 4; }
.ui-live-line-4 { stroke: var(--color-danger); stroke-dasharray: 1 4; }
.ui-live-line-5 { stroke: var(--color-text-secondary); stroke-dasharray: 12 3; }

.ui-live-marker {
  fill: var(--color-surface-1);
  stroke-width: 2;
  vector-effect: non-scaling-stroke;
}

.ui-live-gridline {
  stroke: var(--color-border-subtle);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}

.ui-live-gridline-base {
  stroke: var(--color-border-strong);
}

.ui-live-crosshair {
  stroke: var(--color-border-strong);
  stroke-width: 1;
  stroke-dasharray: 3 3;
  vector-effect: non-scaling-stroke;
}

.ui-live-selected {
  stroke: var(--color-accent);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}
</style>
