import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useMonitoringApi } from '../composables/apis/useMonitoringApi'
import { ApiError } from '../composables/useApi'
import type { AccountDiskUsage, ChartRange, MetricBucket, MetricsChart, ServiceStatus } from '../types/monitoring'

/** The range the screen opens on: a day, because it is the one an operator judges "now" against. */
const DEFAULT_RANGE: ChartRange = 'lastDay'

/**
 * Owns what the monitoring screen is made of — the bucketed samples behind its six charts, the
 * states of the services the agent watches, and what every account occupies on disk. The page reads
 * state from here and calls its actions; it never touches the API layer (rules/vue.md: "API
 * composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: the panel's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md: "the backend owns their text"). It is kept in two refs and not one,
 * because the charts and the disk table are on screen at the same time and a refused disk read must
 * not blank the reason the charts are missing.
 *
 * **A chart answer is accepted only when its echoed range matches the range still selected.** The
 * seven-day request is the slow one, so an operator who switches to it and straight back would
 * otherwise be shown a week of points under a "24 h" segment, with nothing on screen saying so.
 * `MetricsChartDto` echoes the range precisely so that this check is possible.
 */
export const useMonitoringStore = defineStore('monitoring', () => {
  const api = useMonitoringApi()

  /** The range the charts currently show. */
  const range: Ref<ChartRange> = ref(DEFAULT_RANGE)

  /** The most recent accepted chart answer, or `null` before the first successful read. */
  const chart: Ref<MetricsChart | null> = ref(null)

  /** The watched services as last reported; empty means the agent watches none. */
  const services: Ref<ServiceStatus[]> = ref([])

  /** What each account occupies on disk, as last reported. */
  const accountDiskUsage: Ref<AccountDiskUsage[]> = ref([])

  /** True while the first load of the whole screen is in flight. */
  const loading: Ref<boolean> = ref(false)

  /** True while a chart re-read is in flight, which is what a range change starts. */
  const chartLoading: Ref<boolean> = ref(false)

  /** True once the screen has loaded at least once, successfully. */
  const isLoaded: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed chart or service read, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

  /** Backend-localized message from the most recent failed per-account disk read, or `null`. */
  const diskErrorMessage: Ref<string | null> = ref(null)

  /**
   * The accepted chart's buckets, or an empty list when none has been accepted yet. The charts
   * read this rather than `chart`, so an empty panel and an unread one draw the same empty state —
   * which is the same thing to an operator.
   */
  const buckets: ComputedRef<MetricBucket[]> = computed(() => {
    return chart.value?.buckets ?? []
  })

  /**
   * Reads the chart for a range and keeps it only if that range is still the selected one.
   * @param requested The range to read.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadChart = async (requested: ChartRange): Promise<void> => {
    chartLoading.value = true
    try {
      const answer = await api.getChart(requested)

      // The echo, not the local variable: the guard has to survive a second switch made while this
      // very request was in flight, and only the payload knows what the panel actually answered.
      if (answer.range === range.value) {
        chart.value = answer
        errorMessage.value = null
      }
    } catch (error) {
      errorMessage.value = error instanceof ApiError ? error.message : null
    } finally {
      chartLoading.value = false
    }
  }

  /**
   * Loads everything the screen shows: the charts for the selected range, the service states, and
   * the per-account disk figures.
   *
   * The three requests go together rather than one per section — they are one screen, and
   * staggering them would show an operator three panels describing three different moments.
   * @returns Resolves once every request has settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loading.value = true
    const requested = range.value
    try {
      const [answer, loadedServices] = await Promise.all([api.getChart(requested), api.listServices()])
      if (answer.range === range.value) {
        chart.value = answer
      }
      services.value = loadedServices
      errorMessage.value = null
    } catch (error) {
      // A refusal (a customer reaching an administrators-only screen) arrives here exactly like a
      // failure does, and is rendered exactly the same way: as the panel's own message.
      errorMessage.value = error instanceof ApiError ? error.message : null
    }

    // Read separately and not inside the same `Promise.all`: this is the one route on the
    // controller that discloses every tenant's system user name, so it is also the one most likely
    // to be refused on its own — and a refusal of it must not blank the charts beside it.
    try {
      accountDiskUsage.value = await api.listAccountDiskUsage()
      diskErrorMessage.value = null
    } catch (error) {
      diskErrorMessage.value = error instanceof ApiError ? error.message : null
    }

    isLoaded.value = true
    loading.value = false
  }

  /**
   * Switches the range the charts show and re-reads them from the panel.
   *
   * The buckets are re-read rather than sliced from what is held: the seven-day answer is bucketed
   * at thirty minutes and the day's at five, so a week cannot be assembled out of a day and a day
   * shown out of a week would be six times coarser than the one the operator asked for.
   * @param requested The range to switch to; the same range re-reads nothing.
   * @returns Resolves once the re-read has settled, successfully or not.
   */
  const selectRange = async (requested: ChartRange): Promise<void> => {
    if (requested === range.value) {
      return
    }
    range.value = requested
    await loadChart(requested)
  }

  return {
    range,
    chart,
    services,
    accountDiskUsage,
    loading,
    chartLoading,
    isLoaded,
    errorMessage,
    diskErrorMessage,
    buckets,
    load,
    selectRange,
  }
})
