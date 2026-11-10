<script setup lang="ts">
/**
 * The time labels of {@link UiChart}, one row under the plot. Each is placed at its tick's
 * percentage and shifted by its anchor, so the first starts at the plot's left edge and the last
 * ends at its right edge instead of hanging out of the card. Which labels are shown is decided by
 * {@link useChartTimeAxis}, which measures the row so they never print over each other.
 *
 * The labels are HTML beside the SVG, not `<text>` inside it (see `UiChart`), and `aria-hidden`:
 * the readings table carries the data. The row sits in the chart grid's second row, second column.
 */
import { computed, useTemplateRef, type ComputedRef, type ShallowRef } from 'vue'
import { useChartTimeAxis } from '../../composables/useChartTimeAxis'
import type { ChartPlottedPoint, ChartTickAnchor } from '../../utils/chartGeometry'
import { chartXPercent } from '../../utils/chartView'

/**
 * The horizontal shift that lines a label up with its tick by its anchor: a `start` label begins at
 * the tick, an `end` label finishes on it, a `middle` label is centred on it.
 */
const ANCHOR_SHIFT: Record<ChartTickAnchor, string> = {
  start: '0',
  middle: '-50%',
  end: '-100%',
}

/** Props accepted by {@link UiChartXAxis}. */
const props = defineProps<{
  /** The plotted points the labels point at. */
  points: ChartPlottedPoint[]
  /** Formats a bucket's instant in the panel's current language. */
  formatInstant: (at: number) => string
}>()

/** The row holding the time labels, handed to the axis composable to be measured. */
const axisRow: Readonly<ShallowRef<HTMLElement | null>> = useTemplateRef<HTMLElement>('axisRow')

/** The points as a computed, so the composable follows the prop. */
const plotted: ComputedRef<ChartPlottedPoint[]> = computed(() => {
  return props.points
})

/**
 * Formats a bucket's instant through the current prop, so a re-render with a new formatter is followed.
 * @param at Unix epoch in milliseconds.
 * @returns A short day/time label.
 */
const formatAt = (at: number): string => {
  return props.formatInstant(at)
}

const { labels } = useChartTimeAxis(axisRow, plotted, formatAt)
</script>

<template>
  <div ref="axisRow" class="relative col-start-2 row-start-2 mt-1 h-4" aria-hidden="true">
    <span
      v-for="tick in labels"
      :key="`x-${tick.x}`"
      class="ui-chart-axis-label absolute top-0 text-xs whitespace-nowrap text-text-muted tabular-nums"
      :style="{ left: chartXPercent(tick.x), translate: `${ANCHOR_SHIFT[tick.anchor]} 0` }"
      >{{ tick.label }}</span
    >
  </div>
</template>
