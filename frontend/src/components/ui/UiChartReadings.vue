<script setup lang="ts">
/**
 * The chart's data as a real table, for assistive technology: every bucket as a row of cells, since
 * the plot itself is `aria-hidden`.
 *
 * `sr-only` sits on a wrapping div, never on the table itself: a table ignores the 1px width
 * `sr-only` gives it and sizes to its content, and being absolutely positioned it escapes the
 * content panel's `overflow-x-hidden`. With a long caption (the Armenian one is ~400px) that
 * widened the whole document at 390px. A div does honour the 1px box and its `overflow: hidden`,
 * so the table inside is clipped.
 */
import { useI18n } from 'vue-i18n'
import type { ChartPoint } from '../../utils/chartGeometry'

/** Props accepted by {@link UiChartReadings}. */
defineProps<{
  /** The buckets to list, already sorted by time. */
  series: ChartPoint[]
  /** The table's caption, already translated. */
  caption: string
  /** Formats a bucket's instant in the panel's current language. */
  formatInstant: (at: number) => string
  /** Formats a raw value with its unit. */
  formatReading: (value: number) => string
}>()

const { t } = useI18n()
</script>

<template>
  <div class="sr-only">
    <table>
      <caption>
        {{
          caption
        }}
      </caption>
      <thead>
        <tr>
          <th scope="col">{{ t('app.chart.columnTime') }}</th>
          <th scope="col">{{ t('app.chart.columnValue') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="point in series" :key="point.at">
          <td>{{ formatInstant(point.at) }}</td>
          <td>{{ formatReading(point.value) }}</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>
