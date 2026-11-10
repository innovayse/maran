<script setup lang="ts">
/**
 * The value labels of {@link UiChart}, down the left of the plot. Decorative, like the SVG: the
 * same data is available in full, as real table cells, in the chart's readings table. The labels
 * share ONE grid cell, so the gutter is as wide as the widest of them whatever the unit, and `top`
 * as a percentage of that cell (the plot's height) puts each on its gridline; `self-start` keeps
 * each label its own height, so `translate: -50%` centres it on the line instead of on a stretched
 * box. The labels are HTML and not `<text>` in the SVG, so they keep their size at any card width.
 */
import { chartYPercent, type UiChartYTick } from '../../utils/chartView'

/** Props accepted by {@link UiChartYAxis}. */
defineProps<{
  /** The gridlines to label, top to bottom. */
  ticks: UiChartYTick[]
}>()
</script>

<template>
  <div class="col-start-1 row-start-1 grid" aria-hidden="true">
    <span
      v-for="tick in ticks"
      :key="`y-${tick.y}`"
      class="ui-chart-axis-label relative col-start-1 row-start-1 self-start justify-self-end text-xs whitespace-nowrap text-text-muted tabular-nums"
      :style="{ top: chartYPercent(tick.y), translate: '0 -50%' }"
      >{{ tick.label }}</span
    >
  </div>
</template>
