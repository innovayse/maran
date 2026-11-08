<script setup lang="ts">
/**
 * The frame {@link UiLiveChart} draws its plot in: value labels down the left, the plot in the
 * default slot, time labels underneath. The labels are HTML beside the SVG and not `<text>` inside
 * it, for `UiChart`'s reason: text in a stretched viewBox shrinks with the card. They are
 * `aria-hidden`; the data is in the chart's table.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AppLocale } from '../../types/app'
import { formatChartInstant } from '../../utils/formatChartInstant'
import { LIVE_CHART_GRID_FRACTIONS, type LiveChartDomain, type LiveChartRange } from '../../utils/liveChartGeometry'

/** Props accepted by {@link UiLiveChartAxes}. */
const props = defineProps<{
  /** The time span the horizontal axis covers; `null` while there is no data, which leaves the time labels out. */
  domain: LiveChartDomain | null
  /** The values the vertical axis covers. */
  range: LiveChartRange
  /** Formats a value for its label, with its unit. */
  formatValue: (value: number) => string
}>()

const { locale } = useI18n()

/** The value labels, top to bottom, with their place as a percentage of the plot's height. */
const yTicks: ComputedRef<{ top: string; text: string }[]> = computed(() => {
  return LIVE_CHART_GRID_FRACTIONS.map((fraction) => {
    return { top: `${fraction * 100}%`, text: props.formatValue(props.range.max * (1 - fraction)) }
  })
})

/** The time labels: the first and last instant. */
const xTicks: ComputedRef<{ at: number; text: string }[]> = computed(() => {
  const span = props.domain
  // The i18n instance is fed only from the locale store, so its value is one of the panel's locales.
  return span === null
    ? []
    : [
        { at: span.from, text: formatChartInstant(span.from, locale.value as AppLocale) },
        { at: span.to, text: formatChartInstant(span.to, locale.value as AppLocale) },
      ]
})
</script>

<template>
  <div class="grid grid-cols-[auto_minmax(0,1fr)] gap-x-2">
    <!-- The labels share ONE grid cell so the gutter is as wide as the widest, and `top` as a
         percentage of that cell (the plot's height) puts each on its gridline. -->
    <div class="col-start-1 row-start-1 grid" aria-hidden="true">
      <span
        v-for="tick in yTicks"
        :key="tick.top"
        class="relative col-start-1 row-start-1 self-start justify-self-end text-xs whitespace-nowrap text-text-muted tabular-nums"
        :style="{ top: tick.top, translate: '0 -50%' }"
        >{{ tick.text }}</span
      >
    </div>

    <div class="col-start-2 row-start-1">
      <slot />
    </div>

    <div class="relative col-start-2 row-start-2 mt-1 flex h-4 justify-between" aria-hidden="true">
      <span v-for="tick in xTicks" :key="tick.at" class="text-xs whitespace-nowrap text-text-muted tabular-nums">{{
        tick.text
      }}</span>
    </div>
  </div>
</template>
