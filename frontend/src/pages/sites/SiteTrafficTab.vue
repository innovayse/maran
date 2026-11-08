<script setup lang="ts">
/**
 * One site's traffic over a window the operator picks: totals, a plot per day, the exact status
 * codes and the most requested paths.
 *
 * Everything shown comes from the panel's summary. The windows offered come from the site choices,
 * so the SPA names no window of its own; the first one the panel lists is the starting window.
 *
 * Three states are told apart and none is dressed as another. A summary with no recorded traffic
 * (`hasData: false`) is an ordinary first state and gets an empty state. A failed read shows the
 * panel's sentence (or the unreachable message when it never answered). And a summary whose figures
 * may be short — the panel saw a gap in the log — says since when above the numbers, because a
 * count that looks complete and is not is the one thing this tab must not do.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import TrafficDayChart from '../../components/sites/TrafficDayChart.vue'
import TrafficPathsTable from '../../components/sites/TrafficPathsTable.vue'
import TrafficStatusTable from '../../components/sites/TrafficStatusTable.vue'
import TrafficSummaryCards from '../../components/sites/TrafficSummaryCards.vue'
import { useLocaleStore } from '../../stores/locale'
import { useSitesStore } from '../../stores/sites'
import { useSiteTrafficStore } from '../../stores/siteTraffic'
import { formatDate } from '../../utils/formatDate'
import type { LoadingState } from '../../types/loading'
import type { SiteTraffic } from '../../types/siteTraffic'

/** Props accepted by {@link SiteTrafficTab}. */
const props = defineProps<{
  /** The site whose traffic is shown. */
  siteId: string
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
const sitesStore = useSitesStore()
const store = useSiteTrafficStore()

/** The window picked, as the choice's value text; empty until the panel has listed the windows. */
const windowValue: Ref<string> = ref('')

/** The windows the panel offers, as segmented-control options. */
const windowOptions: ComputedRef<SegmentOption[]> = computed(() => {
  return (sitesStore.choices?.trafficWindows ?? []).map((choice) => {
    return { value: String(choice.value), label: choice.label }
  })
})

/**
 * Whether the panel was asked for its traffic windows and listed none. Without a window there is
 * nothing to read, and the tab says so instead of waiting for a read that will never start.
 */
const windowsMissing: Ref<boolean> = ref(false)

/** Where the read stands, for the loading region. */
const regionState: ComputedRef<LoadingState> = computed(() => {
  switch (store.traffic.state) {
    case 'loading':
      return 'loading'
    case 'loaded':
      return 'loaded'
    default:
      return 'error'
  }
})

/** The summary once the panel has answered with one; `null` while loading or after a failure. */
const traffic: ComputedRef<SiteTraffic | null> = computed(() => {
  return store.traffic.state === 'loaded' ? store.traffic.traffic : null
})

/** The sentence saying the figures may be short, or `null` when the panel reported them complete. */
const incompleteText: ComputedRef<string | null> = computed(() => {
  const since = traffic.value?.incompleteSince ?? null
  return since === null ? null : t('siteTraffic.incomplete', { date: formatDate(since, localeStore.current) })
})

/**
 * Reads the traffic of the picked window.
 * @returns Resolves once the request has settled.
 */
const loadWindow = async (): Promise<void> => {
  if (windowValue.value === '') {
    return
  }
  await store.loadTraffic(props.siteId, Number(windowValue.value))
}

/**
 * Switches the window and reads it again.
 * @param value The window the control reported.
 * @returns Resolves once the request has settled.
 */
const onWindowChange = async (value: string): Promise<void> => {
  windowValue.value = value
  await loadWindow()
}

/**
 * Learns which windows the panel offers, starts on the first of them and reads it.
 * @returns Resolves once the requests have settled.
 */
const load = async (): Promise<void> => {
  if (sitesStore.choices === null) {
    await sitesStore.loadChoices()
  }
  const first = sitesStore.choices?.trafficWindows[0]
  if (first === undefined) {
    windowsMissing.value = true
    return
  }
  windowValue.value = String(first.value)
  await loadWindow()
}

onMounted(load)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSegmentedControl
      v-if="windowOptions.length > 0"
      :model-value="windowValue"
      :options="windowOptions"
      :label="t('siteTraffic.windowLabel')"
      @update:model-value="onWindowChange"
    />

    <UiAlert v-if="windowsMissing" variant="error">{{ t('app.status.unreachable') }}</UiAlert>

    <UiLoadingRegion v-else :state="regionState">
      <template #error>
        <UiAlert v-if="store.traffic.state === 'failed'" variant="error">
          {{ store.traffic.message ?? t('app.status.unreachable') }}
        </UiAlert>
      </template>

      <UiCard v-if="traffic === null && store.traffic.state === 'loading'">
        <UiSkeleton shape="block" />
      </UiCard>

      <UiEmptyState
        v-else-if="traffic !== null && !traffic.hasData"
        :title="t('siteTraffic.emptyTitle')"
        :description="t('siteTraffic.emptyDescription')"
      />

      <div v-else-if="traffic !== null" class="flex min-w-0 flex-col gap-4">
        <UiAlert v-if="incompleteText !== null" variant="warning">{{ incompleteText }}</UiAlert>

        <TrafficSummaryCards :traffic="traffic" />
        <TrafficDayChart :per-day="traffic.perDay" />

        <div class="grid min-w-0 gap-4 lg:grid-cols-2">
          <UiCard class="flex min-w-0 flex-col gap-3">
            <UiSectionHeading :title="t('siteTraffic.status.heading')" />
            <TrafficStatusTable :rows="traffic.statusCodes" />
          </UiCard>
          <UiCard class="flex min-w-0 flex-col gap-3">
            <UiSectionHeading :title="t('siteTraffic.paths.heading')" />
            <TrafficPathsTable :rows="traffic.topPaths" />
          </UiCard>
        </div>
      </div>
    </UiLoadingRegion>
  </div>
</template>
