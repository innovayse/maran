<script setup lang="ts">
/**
 * One live figure of the host with its chart: the shape the processor, memory, disk and network
 * widgets share, so the four cannot drift apart.
 *
 * **Both halves lead somewhere.** The head (icon, name, the figure) is ONE link to the monitoring
 * screen, naming that metric in its address; the chart under it is the other half. Clicking or
 * tapping a point in the chart does not navigate (a chart is no link, and a pointer gesture is no
 * keyboard path): it picks the instant, and a real link then appears under the chart, "Open
 * 14:03:07 in monitoring", which opens the monitoring screen with the metric (`metric`) and the
 * instant (`at`, epoch milliseconds) in its address. So every route to the
 * destination is a link a keyboard reaches, and nothing is reachable only by the mouse. When the
 * monitoring module is not licensed there is no destination, so no link is drawn at all rather than
 * one that ends at the upgrade page.
 *
 * The monitoring screen does not read `metric` or `at` yet: it opens at its top, on its own
 * range. The address carries them so the destination can act on them without this card changing.
 *
 * The card holds one piece of state, the picked instant, because nothing else needs to know it.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import type { AppLocale } from '../../types/app'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import type { UiIconName } from '../ui/UiIcon.vue'
import UiIconTile from '../ui/UiIconTile.vue'
import UiLiveChart, { type UiLiveChartSeries } from '../ui/UiLiveChart.vue'
import { formatLiveInstant } from '../../utils/formatLiveInstant'

const props = defineProps<{
  /** Which metric the card shows; the `metric` carried in the monitoring address and the card's test id. */
  metric: string
  /** The card's name, already translated. */
  title: string
  /** The glyph in the icon square. */
  icon: UiIconName
  /** The big figure, already formatted; `null` while the first reading is on its way. */
  figure: string | null
  /** The line under the figure (what it is out of, the load averages), already formatted; `null` for none. */
  detail: string | null
  /** The chart's lines, at most six. */
  series: UiLiveChartSeries[]
  /** Formats a value for the chart's axis and readout, with its unit. */
  formatValue: (value: number) => string
  /** A fixed ceiling for the chart's vertical axis, or left out to fit the data. */
  ceiling?: number
  /** Whether the monitoring screen can be opened (its module is licensed). */
  linksToMonitoring: boolean
}>()

const { t, locale } = useI18n()

/** The instant the reader picked in the chart, or `null`. */
const pickedAt: Ref<number | null> = ref(null)

/** Where the head leads: the monitoring screen on this metric. */
const headLink: ComputedRef<{ path: string; query: Record<string, string> }> = computed(() => {
  return { path: '/monitoring', query: { metric: props.metric } }
})

/** Where the picked instant leads: the same screen and metric, centred on that instant. */
const pickedLink: ComputedRef<{ path: string; query: Record<string, string> } | null> = computed(() => {
  return pickedAt.value === null
    ? null
    : { path: '/monitoring', query: { metric: props.metric, at: String(pickedAt.value) } }
})

/** The picked instant's time of day, for the link's text. */
const pickedText: ComputedRef<string> = computed(() => {
  // The i18n instance is fed only from the locale store, so its value is one of the panel's locales.
  return pickedAt.value === null ? '' : formatLiveInstant(pickedAt.value, locale.value as AppLocale)
})

/**
 * Remembers the instant the reader picked.
 * @param at The instant, epoch milliseconds.
 * @returns Nothing.
 */
const pick = (at: number): void => {
  pickedAt.value = at
}
</script>

<template>
  <UiCard class="flex flex-col gap-3" :data-testid="`metric-card-${metric}`">
    <component
      :is="linksToMonitoring ? RouterLink : 'div'"
      v-bind="linksToMonitoring ? { to: headLink } : {}"
      class="flex min-w-0 items-center gap-3 rounded-control text-text-primary focus-visible:shadow-focus focus-visible:outline-none"
      :data-testid="`metric-head-${metric}`"
    >
      <UiIconTile :name="icon" />
      <span class="flex min-w-0 flex-col">
        <span class="font-display text-xs font-extrabold text-text-secondary break-words">{{ title }}</span>
        <span class="font-display text-2xl leading-tight font-black tabular-nums break-words">{{
          figure ?? t('common.emptyValue')
        }}</span>
        <span v-if="detail !== null" class="text-xs text-text-muted break-words">{{ detail }}</span>
      </span>
    </component>

    <UiLiveChart
      :label="title"
      :series="series"
      :format-value="formatValue"
      :ceiling="ceiling"
      :selected-at="pickedAt"
      :loading="figure === null"
      @select="pick"
    />

    <UiButton v-if="linksToMonitoring && pickedLink !== null" :to="pickedLink" variant="secondary" size="bar">
      {{ t('liveDashboard.chart.openAt', { time: pickedText }) }}
    </UiButton>
  </UiCard>
</template>
