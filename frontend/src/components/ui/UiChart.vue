<script setup lang="ts">
/**
 * Inline SVG line-and-area chart for a time series — the panel's monitoring
 * screens plot CPU, memory, disk and network rate buckets through this one
 * primitive. It is the kit's only hand-drawn, non-icon SVG (rules/vue.md:
 * "icon SVG comes only from lucide via `UiIcon`; `UiChart` is the single
 * non-icon SVG site") — a data plot has no lucide equivalent to reach for.
 *
 * Behaviour: a filled area under a line, axis ticks on both edges, and a
 * hover readout showing the pointed-at bucket's value, formatted through the
 * caller's `formatValue` when given and through a plain one-decimal rounding
 * otherwise. An empty `series` — the ordinary state before the first bucket
 * arrives, not an error — renders {@link UiEmptyState} instead of an SVG with
 * nothing to plot. A single-point series draws one marker and no line: a
 * line drawn "through" one point has no direction to take, so it is not
 * faked. Both cases, and a perfectly flat series, are handled by construction
 * rather than by catching a `NaN`: {@link valueRange} always returns a
 * non-zero span, so no divide-by-zero can reach a path's `d` attribute.
 *
 * Hover readout: drawn as a group INSIDE the same SVG, in the chart's own
 * user-coordinate space, clamped to stay inside the viewBox. This is a
 * deliberately different choice from `UiDropdown`'s panel, which teleports to
 * `body` and positions itself in viewport pixels — that escape hatch exists
 * there because a menu has to outlive an ancestor's `overflow: auto` clipping
 * a table imposes. A chart is not inside a scrolling ancestor and the readout
 * never has to leave the box it describes, so teleporting it would add a
 * measuring/repositioning machine for a problem this component does not have.
 *
 * Accessibility: the SVG is presentation-only (`aria-hidden`) — a sighted
 * pointer enhancement, not the data's home. Every bucket is also rendered as
 * an ordinary `sr-only` HTML `<table>` (a legitimate raw element here, same
 * exception `UiTable` relies on: `components/ui/` implements the primitives
 * that make raw markup unnecessary everywhere else). A screen-reader user
 * therefore gets the COMPLETE series as real cells, not a lossy one-sentence
 * summary standing in for a picture. Pointer events (`pointermove`/
 * `pointerleave`), not mouse events, drive the hover state: besides working
 * for touch and pen as well as a mouse, `mouseleave` requires a paired
 * `focus`/`blur` handler under this kit's accessibility lint
 * (`vuejs-accessibility/mouse-events-have-key-events`), which would mean
 * wiring focus handling onto an element this component deliberately keeps out
 * of the tab order.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { format } from 'date-fns'
import { useI18n } from 'vue-i18n'
import {
  buildAreaPath,
  buildLinePath,
  CHART_BASELINE_Y as BASELINE_Y,
  CHART_HEIGHT,
  CHART_MARGIN as MARGIN,
  CHART_READOUT_HEIGHT as READOUT_HEIGHT,
  CHART_READOUT_WIDTH as READOUT_WIDTH,
  CHART_WIDTH,
  computeChartPoints,
  computeReadoutX,
  computeReadoutY,
  computeValueRange,
  computeXTicks,
  computeYTicks,
  findNearestPointIndex,
  sortChartSeries,
  type ChartPlottedPoint,
  type ChartTickAnchor,
} from '../../utils/chartGeometry'
import UiEmptyState from './UiEmptyState.vue'
import UiIcon from './UiIcon.vue'

/** One measured value at one instant — the shape every bucket source (CPU, memory, disk…) sends. */
export interface UiChartPoint {
  /** When the value was recorded, as a Unix epoch in milliseconds. */
  at: number
  /** The measured value, in the chart's `unit`. */
  value: number
}

/** Props accepted by {@link UiChart}. */
const props = defineProps<{
  /** The bucketed series to plot, oldest first or not — the component sorts defensively. */
  series: UiChartPoint[]
  /** Name of the plotted metric, already translated by the caller (e.g. "CPU"). */
  label: string
  /** Unit the values are in, already translated/formatted by the caller (e.g. "%", "MB/s"). */
  unit: string
  /** Optional formatter for a raw value; falls back to a plain one-decimal rounding when omitted. */
  formatValue?: (value: number) => string
}>()

const { t } = useI18n()

/** Index into {@link points} the pointer is currently over; `null` when nothing is hovered. */
const hoveredIndex: Ref<number | null> = ref(null)
/** The rendered `<svg>`, measured on every pointer move to convert client pixels to user units. */
const svgElement: Ref<SVGSVGElement | null> = ref(null)

/**
 * The series sorted by time. Defensive rather than trusting the caller's order: a bucket source
 * that emitted out of sequence would otherwise draw a path that zig-zags backwards, which reads as
 * a rendering bug rather than as the data it is. Delegates to {@link sortChartSeries}.
 */
const sortedSeries: ComputedRef<UiChartPoint[]> = computed(() => {
  return sortChartSeries(props.series)
})

/**
 * The vertical range the plot scales against — see {@link computeValueRange} for why it is never
 * zero-width.
 */
const valueRange: ComputedRef<{ min: number; max: number }> = computed(() => {
  return computeValueRange(
    sortedSeries.value.map((point) => {
      return point.value
    }),
  )
})

/** Every bucket with its pixel position. A single point is centred; two or more are spread evenly. */
const points: ComputedRef<ChartPlottedPoint[]> = computed(() => {
  return computeChartPoints(sortedSeries.value, valueRange.value)
})

/** The line's path data, or `null` when fewer than two points exist to draw a line through. */
const linePath: ComputedRef<string | null> = computed(() => {
  return buildLinePath(points.value)
})

/** The filled area beneath the line, closed down to the plot's floor. `null` alongside the line. */
const areaPath: ComputedRef<string | null> = computed(() => {
  return buildAreaPath(points.value, linePath.value)
})

/**
 * Three horizontal gridlines/labels: the range's max, midpoint and min. {@link computeYTicks}
 * selects the values and their pixel rows; the label text is formatted here, since it depends on
 * the caller's own `formatValue` prop, which the geometry module deliberately knows nothing about.
 */
const yTicks: ComputedRef<{ y: number; label: string }[]> = computed(() => {
  return computeYTicks(valueRange.value).map((tick) => {
    return { y: tick.y, label: formatValueLabel(tick.value) }
  })
})

/**
 * Up to three time labels along the bottom edge: the first, middle and last bucket.
 * {@link computeXTicks} selects which buckets and their pixel columns; the label text is formatted
 * here, for the same reason as {@link yTicks} — date formatting is not geometry.
 */
const xTicks: ComputedRef<{ x: number; label: string; anchor: ChartTickAnchor }[]> = computed(() => {
  return computeXTicks(points.value).map((tick) => {
    return { x: tick.x, label: formatAt(tick.at), anchor: tick.anchor }
  })
})

/** The plotted point currently under the pointer, or `null` when nothing is hovered. */
const hoveredPoint: ComputedRef<ChartPlottedPoint | null> = computed(() => {
  const index = hoveredIndex.value
  return index === null ? null : (points.value[index] ?? null)
})

/** The hover readout's horizontal position, clamped so it never draws outside the viewBox. */
const readoutX: ComputedRef<number> = computed(() => {
  return computeReadoutX(hoveredPoint.value)
})

/** The hover readout's vertical position: above the point by default, flipped below near the top edge. */
const readoutY: ComputedRef<number> = computed(() => {
  return computeReadoutY(hoveredPoint.value)
})

/** The most recent bucket's value, formatted with its unit, for the header line. `null` when empty. */
const latestFormatted: ComputedRef<string | null> = computed(() => {
  const plotted = points.value
  const last = plotted[plotted.length - 1]
  return last === undefined ? null : formatValueWithUnit(last.value)
})

/**
 * The formatter used when the caller supplies none: one decimal place, with a trailing ".0"
 * dropped so a whole number reads as one.
 * @param value The raw value to format.
 * @returns The value rounded to at most one decimal place.
 */
const defaultFormatValue = (value: number): string => {
  const rounded = Math.round(value * 10) / 10
  return Number.isInteger(rounded) ? String(rounded) : rounded.toFixed(1)
}

/**
 * Formats a raw value alone, through the caller's `formatValue` when given.
 * @param value The raw value to format.
 * @returns The formatted value, without its unit.
 */
const formatValueLabel = (value: number): string => {
  return (props.formatValue ?? defaultFormatValue)(value)
}

/**
 * Formats a raw value with its unit appended — the header reading and the hover readout both
 * need the unit; the axis ticks deliberately do not, since three repeats of it down one column
 * would be noise the header already said once.
 * @param value The raw value to format.
 * @returns The formatted value followed by {@link props}'s `unit`.
 */
const formatValueWithUnit = (value: number): string => {
  return `${formatValueLabel(value)} ${props.unit}`
}

/**
 * Formats a bucket's instant for the axis ticks and the hover readout alike.
 * @param at Unix epoch in milliseconds.
 * @returns A short day/time label.
 */
const formatAt = (at: number): string => {
  return format(new Date(at), 'd MMM, HH:mm')
}

/**
 * Tracks the pointer across the chart and selects the nearest bucket by horizontal distance.
 * Pointer events, not mouse events, so touch and pen hover the same way a mouse does.
 * @param event The native pointer-move event.
 * @returns Nothing; updates {@link hoveredIndex} synchronously.
 */
const onPointerMove = (event: PointerEvent): void => {
  const svg = svgElement.value
  const plotted = points.value
  if (svg === null || plotted.length === 0) {
    return
  }
  const rect = svg.getBoundingClientRect()
  if (rect.width === 0) {
    return
  }
  // The viewBox is stretched to fill the element's rendered box (preserveAspectRatio="none"), so
  // client pixels convert to user units with one linear scale factor.
  const userX = (event.clientX - rect.left) * (CHART_WIDTH / rect.width)
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
  <div class="ui-chart">
    <UiEmptyState v-if="points.length === 0" :title="label" :description="t('app.chart.emptyDescription')">
      <template #icon>
        <UiIcon name="pulse" />
      </template>
    </UiEmptyState>

    <template v-else>
      <div class="mb-2 flex items-baseline justify-between gap-2">
        <span class="text-sm font-medium text-text-secondary">{{ label }}</span>
        <span v-if="latestFormatted !== null" class="font-mono text-sm text-text-primary">{{
          latestFormatted
        }}</span>
      </div>

      <!-- Decorative: the same data is available in full, as real table cells, below. -->
      <svg
        ref="svgElement"
        :viewBox="`0 0 ${CHART_WIDTH} ${CHART_HEIGHT}`"
        preserveAspectRatio="none"
        class="ui-chart-svg h-auto w-full"
        aria-hidden="true"
        @pointermove="onPointerMove"
        @pointerleave="onPointerLeave"
      >
        <g v-for="tick in yTicks" :key="`y-${tick.y}`">
          <line :x1="MARGIN.left" :x2="CHART_WIDTH - MARGIN.right" :y1="tick.y" :y2="tick.y" class="ui-chart-gridline" />
          <text :x="MARGIN.left - 6" :y="tick.y" text-anchor="end" dominant-baseline="middle" class="ui-chart-axis-label">
            {{ tick.label }}
          </text>
        </g>

        <text
          v-for="tick in xTicks"
          :key="`x-${tick.x}`"
          :x="tick.x"
          :y="CHART_HEIGHT - 6"
          :text-anchor="tick.anchor"
          class="ui-chart-axis-label"
        >
          {{ tick.label }}
        </text>

        <path v-if="areaPath !== null" :d="areaPath" class="ui-chart-area" />
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
          <circle :cx="hoveredPoint.x" :cy="hoveredPoint.y" r="4" class="ui-chart-marker ui-chart-marker--active" />
          <g :transform="`translate(${readoutX}, ${readoutY})`">
            <rect :width="READOUT_WIDTH" :height="READOUT_HEIGHT" rx="6" class="ui-chart-readout-bg" />
            <text x="8" y="16" class="ui-chart-readout-value">{{ formatValueWithUnit(hoveredPoint.value) }}</text>
            <text x="8" y="30" class="ui-chart-readout-at">{{ formatAt(hoveredPoint.at) }}</text>
          </g>
        </g>
      </svg>

      <table class="sr-only">
        <caption>{{ t('app.chart.tableCaption', { label, unit }) }}</caption>
        <thead>
          <tr>
            <th scope="col">{{ t('app.chart.columnTime') }}</th>
            <th scope="col">{{ t('app.chart.columnValue') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="point in sortedSeries" :key="point.at">
            <td>{{ formatAt(point.at) }}</td>
            <td>{{ formatValueWithUnit(point.value) }}</td>
          </tr>
        </tbody>
      </table>
    </template>
  </div>
</template>

<style scoped>
.ui-chart-line {
  fill: none;
  stroke: var(--color-accent);
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
}

.ui-chart-area {
  fill: var(--color-accent);
  opacity: 0.12;
  stroke: none;
}

.ui-chart-gridline {
  stroke: var(--color-border-subtle);
  stroke-width: 1;
}

.ui-chart-axis-label {
  fill: var(--color-text-muted);
  font-family: var(--font-sans);
  font-size: var(--text-xs);
}

.ui-chart-marker {
  fill: var(--color-page);
  stroke: var(--color-accent);
  stroke-width: 2;
}

.ui-chart-marker--active {
  fill: var(--color-accent);
}

.ui-chart-crosshair {
  stroke: var(--color-border-strong);
  stroke-width: 1;
  stroke-dasharray: 3 3;
}

.ui-chart-readout-bg {
  fill: var(--color-surface-2);
  stroke: var(--color-border-strong);
  stroke-width: 1;
}

.ui-chart-readout-value {
  fill: var(--color-text-primary);
  font-family: var(--font-sans);
  font-size: var(--text-sm);
  font-weight: 600;
}

.ui-chart-readout-at {
  fill: var(--color-text-muted);
  font-family: var(--font-sans);
  font-size: var(--text-xs);
}
</style>
