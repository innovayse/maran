<script setup lang="ts">
/**
 * The screen-reader table of {@link UiLiveChart}: the latest readings, one row per instant and one
 * column per line. The SVG is `aria-hidden`, so this is how the chart's data is reached without a
 * pointer. It lists only the latest {@link TABLE_ROWS} readings: the whole history would be a
 * thousand rows rewritten every two seconds, which is no help to anyone. It sits in an `sr-only`
 * wrapping div, never on the table itself (see `UiChart` for why).
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AppLocale } from '../../types/app'
import { formatLiveInstant } from '../../utils/formatLiveInstant'
import type { LiveChartPoint } from '../../utils/liveChartGeometry'
import { valueNear } from '../../utils/liveChartReadings'

/** One line of the chart, which is one column of the table. */
export interface UiLiveChartTableSeries {
  /** Stable key: the column's identity. */
  key: string
  /** The line's name, already translated. */
  label: string
  /** The readings, oldest first; a `null` value is a gap. */
  points: LiveChartPoint[]
}

/** Props accepted by {@link UiLiveChartTable}. */
const props = defineProps<{
  /** What the chart shows, already translated; named in the caption. */
  label: string
  /** The lines in order, one column each; the first one's instants are the rows. */
  series: UiLiveChartTableSeries[]
  /** Formats a value with its unit. */
  formatValue: (value: number) => string
}>()

/** How many of the latest readings the table lists. */
const TABLE_ROWS = 20

const { t, locale } = useI18n()

/** The newest readings as rows: an instant and one cell per line. */
const rows: ComputedRef<{ at: number; time: string; cells: string[] }[]> = computed(() => {
  const reference = props.series[0]
  if (reference === undefined) {
    return []
  }
  return reference.points.slice(-TABLE_ROWS).map((point) => {
    return {
      at: point.at,
      // The i18n instance is fed only from the locale store, so its value is one of the panel's locales.
      time: formatLiveInstant(point.at, locale.value as AppLocale),
      cells: props.series.map((line) => {
        const value = valueNear(line.points, point.at)
        return value === null ? t('common.emptyValue') : props.formatValue(value)
      }),
    }
  })
})
</script>

<template>
  <div class="sr-only">
    <table>
      <caption>
        {{
          t('liveDashboard.chart.tableCaption', { label, count: rows.length })
        }}
      </caption>
      <thead>
        <tr>
          <th scope="col">{{ t('app.chart.columnTime') }}</th>
          <th v-for="line in series" :key="line.key" scope="col">{{ line.label }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="row.at">
          <td>{{ row.time }}</td>
          <td v-for="(cell, index) in row.cells" :key="index">{{ cell }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
