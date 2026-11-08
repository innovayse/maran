<script setup lang="ts">
/**
 * The one-line readout above {@link UiLiveChart}'s plot: the hovered instant and each visible line's
 * reading there. It keeps a fixed one-line box whether or not it has text, so the plot below never
 * jumps when the pointer arrives. A sighted pointer enhancement, so it is `aria-hidden`; the same
 * data is in the chart's `sr-only` table.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AppLocale } from '../../types/app'
import { formatLiveInstant } from '../../utils/formatLiveInstant'
import type { LiveChartPoint } from '../../utils/liveChartGeometry'
import { valueNear } from '../../utils/liveChartReadings'

/** One line the readout reports a reading of. */
export interface UiLiveChartReadoutSeries {
  /** Stable key: the list key. */
  key: string
  /** The line's name, already translated. */
  label: string
  /** The readings, oldest first; a `null` value is a gap. */
  points: LiveChartPoint[]
}

/** Props accepted by {@link UiLiveChartReadout}. */
const props = defineProps<{
  /** The hovered instant, epoch milliseconds; `null` while nothing is hovered and the readout is empty. */
  at: number | null
  /** The visible lines. */
  series: UiLiveChartReadoutSeries[]
  /** Formats a value with its unit. */
  formatValue: (value: number) => string
}>()

const { locale } = useI18n()

/** The text: the instant's time of day and each line's reading there; lines with no reading are left out. */
const text: ComputedRef<string | null> = computed(() => {
  const at = props.at
  if (at === null) {
    return null
  }
  const parts = props.series.flatMap((line) => {
    const value = valueNear(line.points, at)
    return value === null ? [] : [`${line.label} ${props.formatValue(value)}`]
  })
  // The i18n instance is fed only from the locale store, so its value is one of the panel's locales.
  return [formatLiveInstant(at, locale.value as AppLocale), ...parts].join(' · ')
})
</script>

<template>
  <p
    class="m-0 mb-1 h-4 truncate text-xs text-text-muted tabular-nums"
    aria-hidden="true"
    data-testid="live-chart-readout"
  >
    {{ text }}
  </p>
</template>
