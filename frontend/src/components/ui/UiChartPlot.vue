<script setup lang="ts">
/**
 * The drawn part of {@link UiChart}: gridlines, a filled area under a line, a marker for a
 * single-point series, and the hover readout. The one hand-drawn, non-icon SVG in the kit
 * (rules/vue.md: "icon SVG comes only from lucide via `UiIcon`; `UiChart` is the single non-icon SVG
 * site") — a data plot has no lucide equivalent to reach for.
 *
 * Look: ONE series, drawn in the accent (`--ac`, a graphic, never a surface under text) with a soft
 * fade beneath it, on faint gridlines. The gridlines are `--b1`, about 1.2:1 against a panel —
 * decoration only; the values are carried by the labels. The line against a panel is 4.81:1 dark and
 * 3.89:1 light, over the 3:1 a graphic needs. Strokes use `vector-effect: non-scaling-stroke`,
 * because the viewBox is scaled to the card's width and a 2 px line would otherwise thin to about
 * 1 px on a phone. The viewBox is CROPPED to the plot ({@link CHART_VIEW}) because the left margin
 * and the bottom strip of the geometry module's canvas belong to the HTML axis labels.
 *
 * A single-point series draws one marker and no line: a line drawn "through" one point has no
 * direction to take, so it is not faked.
 *
 * Hover readout: drawn as a group INSIDE the same SVG, in the chart's own user-coordinate space,
 * clamped to stay inside the viewBox. This is deliberately different from `UiDropdown`'s panel,
 * which teleports to `body`: a chart is not inside a scrolling ancestor and the readout never has
 * to leave the box it describes. Pointer events (`pointermove` / `pointerleave`), not mouse events,
 * drive the hover state: they work for touch and pen as well as a mouse, and `mouseleave` would need
 * a paired `focus`/`blur` handler under this kit's accessibility lint
 * (`vuejs-accessibility/mouse-events-have-key-events`) on an element deliberately kept out of the
 * tab order. The SVG is `aria-hidden`; the readings table carries the data.
 */
import { computed, ref, useId, type ComputedRef, type Ref } from 'vue'
import {
  buildAreaPath,
  buildLinePath,
  CHART_BASELINE_Y as BASELINE_Y,
  CHART_MARGIN as MARGIN,
  CHART_READOUT_HEIGHT as READOUT_HEIGHT,
  CHART_READOUT_WIDTH as READOUT_WIDTH,
  CHART_WIDTH,
  computeReadoutX,
  computeReadoutY,
  findNearestPointIndex,
  type ChartPlottedPoint,
} from '../../utils/chartGeometry'
import { CHART_VIEW } from '../../utils/chartView'

/** Props accepted by {@link UiChartPlot}. */
const props = defineProps<{
  /** Every bucket with its pixel position, oldest first. */
  points: ChartPlottedPoint[]
  /** The pixel rows of the horizontal gridlines, top to bottom; the last one is the baseline. */
  gridRows: number[]
  /** Formats a raw value with its unit, for the hover readout. */
  formatReading: (value: number) => string
  /** Formats a bucket's instant in the panel's current language, for the hover readout. */
  formatInstant: (at: number) => string
}>()

/** Index into `points` the pointer is currently over; `null` when nothing is hovered. */
const hoveredIndex: Ref<number | null> = ref(null)
/** The rendered `<svg>`, measured on every pointer move to convert client pixels to user units. */
const svgElement: Ref<SVGSVGElement | null> = ref(null)
/**
 * Unique id for this chart's area gradient. A page draws six charts at once, and a shared literal
 * id would make every one of them resolve `url(#…)` to the first gradient in the document. Vue's
 * generated id is stripped to characters that are safe inside a `url()` reference.
 */
const gradientId: string = `ui-chart-fade-${useId().replace(/[^A-Za-z0-9_-]/g, '_')}`

/** The line's path data, or `null` when fewer than two points exist to draw a line through. */
const linePath: ComputedRef<string | null> = computed(() => {
  return buildLinePath(props.points)
})

/** The filled area beneath the line, closed down to the plot's floor. `null` alongside the line. */
const areaPath: ComputedRef<string | null> = computed(() => {
  return buildAreaPath(props.points, linePath.value)
})

/** The plotted point currently under the pointer, or `null` when nothing is hovered. */
const hoveredPoint: ComputedRef<ChartPlottedPoint | null> = computed(() => {
  const index = hoveredIndex.value
  return index === null ? null : (props.points[index] ?? null)
})

/** The hover readout's horizontal position, clamped so it never draws outside the viewBox. */
const readoutX: ComputedRef<number> = computed(() => {
  return computeReadoutX(hoveredPoint.value)
})

/** The hover readout's vertical position: above the point by default, flipped below near the top edge. */
const readoutY: ComputedRef<number> = computed(() => {
  return computeReadoutY(hoveredPoint.value)
})

/**
 * Tracks the pointer across the chart and selects the nearest bucket by horizontal distance.
 * Pointer events, not mouse events, so touch and pen hover the same way a mouse does.
 * @param event The native pointer-move event.
 * @returns Nothing; updates {@link hoveredIndex} synchronously.
 */
const onPointerMove = (event: PointerEvent): void => {
  const svg = svgElement.value
  const plotted = props.points
  if (svg === null || plotted.length === 0) {
    return
  }
  const rect = svg.getBoundingClientRect()
  if (rect.width === 0) {
    return
  }
  // The viewBox is stretched to fill the element's rendered box (preserveAspectRatio="none"), so
  // client pixels convert to user units with one linear scale factor.
  const userX = CHART_VIEW.x + (event.clientX - rect.left) * (CHART_VIEW.width / rect.width)
  hoveredIndex.value = findNearestPointIndex(plotted, userX)
}

/**
 * Clears the hover state once the pointer leaves the chart.
 * @returns Nothing; updates {@link hoveredIndex} synchronously.
 */
const onPointerLeave = (): void => {
  hoveredIndex.value = null
}
</script>

<template>
  <svg
    ref="svgElement"
    :viewBox="`${CHART_VIEW.x} ${CHART_VIEW.y} ${CHART_VIEW.width} ${CHART_VIEW.height}`"
    preserveAspectRatio="none"
    class="ui-chart-svg col-start-2 row-start-1 h-auto w-full touch-pan-y overflow-visible"
    aria-hidden="true"
    @pointermove="onPointerMove"
    @pointerleave="onPointerLeave"
  >
    <defs>
      <linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0" class="ui-chart-fade-top" />
        <stop offset="1" class="ui-chart-fade-bottom" />
      </linearGradient>
    </defs>

    <g v-for="(row, index) in gridRows" :key="`y-${row}`">
      <line
        :x1="MARGIN.left"
        :x2="CHART_WIDTH - MARGIN.right"
        :y1="row"
        :y2="row"
        class="ui-chart-gridline"
        :class="{ 'ui-chart-gridline--base': index === gridRows.length - 1 }"
      />
    </g>

    <path v-if="areaPath !== null" :d="areaPath" :fill="`url(#${gradientId})`" class="ui-chart-area" />
    <path v-if="linePath !== null" :d="linePath" class="ui-chart-line" />
    <circle v-if="points.length === 1" :cx="points[0].x" :cy="points[0].y" r="4" class="ui-chart-marker" />

    <g v-if="hoveredPoint !== null">
      <line
        :x1="hoveredPoint.x"
        :x2="hoveredPoint.x"
        :y1="MARGIN.top"
        :y2="BASELINE_Y"
        class="ui-chart-crosshair"
      />
      <circle
        :cx="hoveredPoint.x"
        :cy="hoveredPoint.y"
        r="4"
        class="ui-chart-marker ui-chart-marker--active"
      />
      <g :transform="`translate(${readoutX}, ${readoutY})`">
        <rect :width="READOUT_WIDTH" :height="READOUT_HEIGHT" rx="6" class="ui-chart-readout-bg" />
        <text x="8" y="16" class="ui-chart-readout-value">
          {{ formatReading(hoveredPoint.value) }}
        </text>
        <text x="8" y="30" class="ui-chart-readout-at">{{ formatInstant(hoveredPoint.at) }}</text>
      </g>
    </g>
  </svg>
</template>

<style scoped>
/*
 * Every stroke is non-scaling: the viewBox is stretched to the card's width, and a scaled 2 px
 * line becomes a 1 px hairline on a phone. The area's fade runs from a tint of the accent to
 * nothing; its opacity is a second channel on a graphic that the line above already draws at full
 * strength, so it is never asked to carry contrast.
 */
.ui-chart-line {
  fill: none;
  stroke: var(--color-accent);
  stroke-width: 2.5;
  stroke-linejoin: round;
  stroke-linecap: round;
  vector-effect: non-scaling-stroke;
}

.ui-chart-area {
  stroke: none;
}

.ui-chart-fade-top {
  stop-color: var(--color-accent);
  stop-opacity: 0.28;
}

.ui-chart-fade-bottom {
  stop-color: var(--color-accent);
  stop-opacity: 0;
}

.ui-chart-gridline {
  stroke: var(--color-border-subtle);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}

.ui-chart-gridline--base {
  stroke: var(--color-border-strong);
}

.ui-chart-marker {
  fill: var(--color-surface-1);
  stroke: var(--color-accent);
  stroke-width: 2;
  vector-effect: non-scaling-stroke;
}

.ui-chart-marker--active {
  fill: var(--color-accent);
}

.ui-chart-crosshair {
  stroke: var(--color-border-strong);
  stroke-width: 1;
  stroke-dasharray: 3 3;
  vector-effect: non-scaling-stroke;
}

.ui-chart-readout-bg {
  fill: var(--color-surface-2);
  stroke: var(--color-border-strong);
  stroke-width: 1;
  vector-effect: non-scaling-stroke;
}

.ui-chart-readout-value {
  fill: var(--color-text-primary);
  font-family: var(--font-sans);
  font-size: var(--text-sm);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.ui-chart-readout-at {
  fill: var(--color-text-muted);
  font-family: var(--font-sans);
  font-size: var(--text-xs);
}
</style>
