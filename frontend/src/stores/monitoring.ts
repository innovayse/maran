import { defineStore } from 'pinia'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useMonitoringApi } from '../composables/apis/useMonitoringApi'
import { ApiError } from '../composables/useApi'
import type { ListLoad, ListRequest } from '../types/list'
import type { LoadingState } from '../types/loading'
import type {
  AccountDiskUsage,
  ChartRange,
  HostMetrics,
  MetricBucket,
  MetricsChart,
  ServiceStatus,
} from '../types/monitoring'

/** The range the screen opens on: a day, because it is the one an operator judges "now" against. */
const DEFAULT_RANGE: ChartRange = 'lastDay'

/**
 * Owns what the monitoring screen is made of — the bucketed samples behind its six charts, the
 * states of the services the agent watches, and the page of accounts' disk use on screen. The page reads
 * state from here and calls its actions; it never touches the API layer (rules/vue.md: "API
 * composables are called from Pinia stores ONLY").
 *
 * Error text is never generated here: the panel's already-localized `title`/`detail` is stored
 * verbatim (rules/vue.md: "the backend owns their text"). The charts' and services' refusal is kept
 * here; the disk table is a server-paged list whose refusal travels back to its `useServerTable`
 * through {@link listAccountDiskUsage}, so a refused disk read never blanks the charts beside it.
 * The live host reading behind the disk card is read on its own too, with its own state and message
 * ({@link loadHostMetrics}), for the same reason: a refused reading must not blank the charts.
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

  /** The rows of the disk list page last answered by the panel; empty before the first answer. */
  const accountDiskUsage: Ref<AccountDiskUsage[]> = ref([])

  /**
   * Where the read of the charts and services stands: on its way, arrived, or refused. The
   * per-account disk list is paged and loaded on its own, so it never decides this.
   */
  const loadState: Ref<LoadingState> = ref('loading')

  /** The host's live reading, or `null` before one has been answered. */
  const hostMetrics: Ref<HostMetrics | null> = ref(null)

  /** Where the live host reading stands, independently of {@link loadState}. */
  const hostMetricsState: Ref<LoadingState> = ref('loading')

  /** Backend-localized message from a failed live host reading, or `null`. */
  const hostMetricsError: Ref<string | null> = ref(null)

  /** True while a chart re-read is in flight, which is what a range change starts. */
  const chartLoading: Ref<boolean> = ref(false)

  /** Backend-localized message from the most recent failed chart or service read, or `null`. */
  const errorMessage: Ref<string | null> = ref(null)

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
   * Loads the charts for the selected range and the service states.
   *
   * The two requests go together rather than one per section — they describe one moment, and
   * staggering them would show an operator two panels describing two different moments. The
   * per-account disk list is not part of it: it is a server page with its own URL state, read by
   * {@link listAccountDiskUsage}.
   * @returns Resolves once both requests have settled, successfully or not.
   */
  const load = async (): Promise<void> => {
    loadState.value = 'loading'
    const requested = range.value
    let settled: LoadingState = 'loaded'
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
      settled = 'error'
    }

    loadState.value = settled
  }

  /**
   * Reads the host's live resource use for the disk card.
   *
   * Its own request with its own state, never inside {@link load}: it is asked of the agent rather
   * than read from the samples table, so it can fail when the charts do not, and its failure must
   * not blank them — nor theirs it.
   * @returns Resolves once the request has settled, successfully or not.
   */
  const loadHostMetrics = async (): Promise<void> => {
    hostMetricsState.value = 'loading'
    try {
      hostMetrics.value = await api.getHostMetrics()
      hostMetricsError.value = null
      hostMetricsState.value = 'loaded'
    } catch (error) {
      hostMetricsError.value = error instanceof ApiError ? error.message : null
      hostMetricsState.value = 'error'
    }
  }

  /**
   * Loads one page of the accounts' disk use into {@link accountDiskUsage} and reports how the read
   * ended, for the disk table's `useServerTable`.
   *
   * Its own request, never inside {@link load}: this is the one route on the controller that
   * discloses every tenant's system user name, so it is also the one most likely to be refused on
   * its own — and a refusal of it must not blank the charts beside it.
   * @param request The page, size, search and sort, as the URL describes them.
   * @returns The page, the panel's refusal with its message, or `unreachable` when no answer arrived.
   */
  const listAccountDiskUsage = async (request: ListRequest): Promise<ListLoad<AccountDiskUsage>> => {
    try {
      const page = await api.listAccountDiskUsage(request)
      accountDiskUsage.value = page.items
      return { kind: 'loaded', page }
    } catch (error) {
      return error instanceof ApiError ? { kind: 'refused', message: error.message } : { kind: 'unreachable' }
    }
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
    loadState,
    hostMetrics,
    hostMetricsState,
    hostMetricsError,
    chartLoading,
    errorMessage,
    buckets,
    load,
    loadHostMetrics,
    listAccountDiskUsage,
    selectRange,
  }
})
