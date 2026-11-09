<script setup lang="ts">
/**
 * Inline SVG line chart for a LIVE series: up to six lines on one time axis, a legend that hides
 * and shows each, a hover readout, and a point that can be picked. The dashboard plots the host's
 * processor, memory, disk and network through it as readings arrive every two seconds.
 *
 * It is the kit's second non-icon SVG site beside `UiChart` (`rules/vue.md` names `UiChart` as the
 * single one; the sentence needs the owner's approval to name both, so it is reported rather than
 * edited here). It is a separate component and not a mode of `UiChart` because the two answer
 * different questions: `UiChart` draws stored buckets on a fixed step, one series, evenly spaced;
 * this draws a stream, several series, positioned by TIME so a silence shows as a hole.
 *
 * **Appends are cheap.** A series is thinned to at most {@link MAX_DRAWN_POINTS} points and each
 * drawn series is ONE `<path d>` whose data is recomputed when its points change; nothing animates,
 * so there is no sweep to disable under reduced motion and none to wait for in a test.
 *
 * **A gap is a gap.** A point with no value, or a silence longer than three of the series' own
 * cadences, breaks the line there. A hidden tab's ten minutes are a visible hole, never a straight
 * line across it and never zero.
 *
 * **Colour is never the only channel.** Each series has its own dash pattern as well as its own
 * colour, in the line and in the legend swatch (`liveChartGeometry` holds the geometry).
 *
 * **Accessibility.** The SVG is `aria-hidden`: a sighted pointer enhancement. The data is carried
 * by an `sr-only` table of the latest twenty readings and by the figure the caller
 * prints beside the chart. Picking a point is pointer-only; the keyboard path to the same place is
 * the caller's link, so nothing is reachable only by the mouse.
 *
 * This component owns the data side: which lines are drawn, the axes' values, what is hovered. It
 * composes four parts that are not for use on their own: `UiLiveChartLegend`, `UiLiveChartReadout`,
 * `UiLiveChartAxes` with `UiLiveChartPlot` in it, and `UiLiveChartTable`.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import {
  largestReading,
  liveChartDomain,
  niceCeiling,
  shapeLiveLine,
  type LiveChartDomain,
  type LiveChartPoint,
  type LiveChartRange,
} from '../../utils/liveChartGeometry'
import { fractionOfInstant, instantAtFraction } from '../../utils/liveChartReadings'
import UiEmptyState from './UiEmptyState.vue'
import UiIcon from './UiIcon.vue'
import UiLiveChartAxes from './UiLiveChartAxes.vue'
import UiLiveChartLegend from './UiLiveChartLegend.vue'
import UiLiveChartPlot, { type UiLiveChartLine } from './UiLiveChartPlot.vue'
import UiLiveChartReadout from './UiLiveChartReadout.vue'
import UiLiveChartTable from './UiLiveChartTable.vue'
import UiSkeleton from './UiSkeleton.vue'

/** One line of the chart. */
export interface UiLiveChartSeries {
  /** Stable key: the legend's identity and the list key. */
  key: string
  /** The line's name, already translated; it names the legend toggle and the table column. */
  label: string
  /** The readings, oldest first; a `null` value is a gap. */
  points: LiveChartPoint[]
}

/** Props accepted by {@link UiLiveChart}. */
const props = withDefaults(
  defineProps<{
    /** What the chart shows, already translated; the heading and the readings table's name. */
    label: string
    /** The lines, at most six; the legend appears from the second. */
    series: UiLiveChartSeries[]
    /** Formats a value for the axis, the readout and the table, with its unit. */
    formatValue: (value: number) => string
    /** A fixed ceiling for the vertical axis (100 for a percentage); left out, the axis fits the data. */
    ceiling?: number
    /** The instant a reader picked, drawn as a marker line; `null` for none. */
    selectedAt?: number | null
    /** While the first readings are on their way: the label over a placeholder, not the empty state. */
    loading?: boolean
  }>(),
  { ceiling: undefined, selectedAt: null, loading: false },
)

/** Events emitted by {@link UiLiveChart}. */
const emit = defineEmits<{
  /** The reader clicked or tapped the plot; carries the instant (epoch milliseconds) of the nearest reading. */
  (e: 'select', at: number): void
}>()

/** The most points one drawn line is thinned to. */
const MAX_DRAWN_POINTS = 600

/** The most lines one chart draws; any further series are ignored. */
const MAX_SERIES = 6

const { t } = useI18n()

/** Keys of the series the reader hid with the legend. */
const hiddenKeys: Ref<string[]> = ref([])

/** The instant under the pointer, snapped to a reading, or `null`. */
const hoveredAt: Ref<number | null> = ref(null)

/** The lines the chart can show: the first six. */
const shown: ComputedRef<UiLiveChartSeries[]> = computed(() => {
  return props.series.slice(0, MAX_SERIES)
})

/** The series drawn: the shown ones, minus the hidden ones. */
const visible: ComputedRef<UiLiveChartSeries[]> = computed(() => {
  return shown.value.filter((line) => {
    return !hiddenKeys.value.includes(line.key)
  })
})

/** The time span of every series given, so hiding a line never moves the axis. */
const domain: ComputedRef<LiveChartDomain | null> = computed(() => {
  return liveChartDomain(
    props.series.map((line) => {
      return line.points
    }),
  )
})

/** The vertical range: from zero to the caller's ceiling, or to a round number above the largest drawn value. */
const range: ComputedRef<LiveChartRange> = computed(() => {
  if (props.ceiling !== undefined) {
    return { min: 0, max: props.ceiling }
  }
  const largest = largestReading(
    visible.value.map((line) => {
      return line.points
    }),
  )
  return { min: 0, max: niceCeiling(largest) }
})

/** What is drawn: per visible line, its thinned points and path data. */
const drawn: ComputedRef<UiLiveChartLine[]> = computed(() => {
  const span = domain.value
  if (span === null) {
    return []
  }
  return visible.value.map((line) => {
    return {
      key: line.key,
      index: props.series.indexOf(line),
      ...shapeLiveLine(line.points, span, range.value, MAX_DRAWN_POINTS),
    }
  })
})

/** Whether any visible line has a reading to show. */
const hasReadings: ComputedRef<boolean> = computed(() => {
  return props.series.some((line) => {
    return line.points.some((point) => {
      return point.value !== null
    })
  })
})

/** Position of the pointer's crosshair as a fraction of the plot width, or `null`. */
const crosshairX: ComputedRef<number | null> = computed(() => {
  return fractionOfInstant(hoveredAt.value, domain.value)
})

/** Position of the picked instant's marker as a fraction of the plot width, or `null`. */
const selectedX: ComputedRef<number | null> = computed(() => {
  return fractionOfInstant(props.selectedAt, domain.value)
})

/**
 * Finds the instant of the reading nearest a place along the plot.
 * @param fraction The place as a fraction of the plot width in 0-1.
 * @returns The instant (epoch milliseconds), or `null` when there is nothing to point at.
 */
const instantAt = (fraction: number): number | null => {
  const span = domain.value
  const lead = visible.value[0]
  return span === null || lead === undefined ? null : instantAtFraction(lead.points, span, fraction)
}

/**
 * Follows the pointer with the crosshair and the readout.
 * @param fraction The pointer's place along the plot, or `null` when it left.
 * @returns Nothing.
 */
const onHover = (fraction: number | null): void => {
  hoveredAt.value = fraction === null ? null : instantAt(fraction)
}

/**
 * Reports the picked instant.
 * @param fraction The clicked place along the plot.
 * @returns Nothing.
 */
const onPick = (fraction: number): void => {
  const at = instantAt(fraction)
  if (at !== null) {
    emit('select', at)
  }
}

/**
 * Shows or hides one line. The last visible line cannot be hidden: a chart of nothing is not a state.
 * @param key The legend entry's key.
 * @returns Nothing.
 */
const toggle = (key: string): void => {
  if (hiddenKeys.value.includes(key)) {
    hiddenKeys.value = hiddenKeys.value.filter((hidden) => {
      return hidden !== key
    })
    return
  }
  if (visible.value.length > 1) {
    hiddenKeys.value = [...hiddenKeys.value, key]
  }
}
</script>

<template>
  <div class="ui-live-chart min-w-0">
    <template v-if="loading">
      <div class="mb-2">
        <span class="font-display text-xs font-extrabold text-text-secondary wrap-anywhere">{{ label }}</span>
      </div>
      <UiSkeleton shape="block" size="lg" />
    </template>

    <UiEmptyState v-else-if="!hasReadings" :title="label" :description="t('app.chart.emptyDescription')">
      <template #icon>
        <UiIcon name="pulse" />
      </template>
    </UiEmptyState>

    <template v-else>
      <div class="mb-2 flex flex-wrap items-center justify-between gap-x-3 gap-y-1">
        <span class="font-display text-xs font-extrabold text-text-secondary wrap-anywhere">{{ label }}</span>
        <UiLiveChartLegend v-if="series.length > 1" :items="shown" :hidden-keys="hiddenKeys" @toggle="toggle" />
      </div>

      <UiLiveChartReadout :at="hoveredAt" :series="visible" :format-value="formatValue" />

      <UiLiveChartAxes :domain="domain" :range="range" :format-value="formatValue">
        <UiLiveChartPlot :lines="drawn" :crosshair-x="crosshairX" :selected-x="selectedX" @track="onHover" @pick="onPick" />
      </UiLiveChartAxes>

      <UiLiveChartTable :label="label" :series="shown" :format-value="formatValue" />
    </template>
  </div>
</template>
