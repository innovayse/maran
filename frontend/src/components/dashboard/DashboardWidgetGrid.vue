<script setup lang="ts">
/**
 * The administrator's widgets, in the person's order: maps each widget key the layout names to the
 * component that draws it and gives it the figures it shows.
 *
 * **Live figures win, the one-shot read stands in.** Each widget that has both reads the stream's
 * latest push (`stores/liveDashboard`) and falls back to the dashboard read the page made, which has
 * the same shape. So the screen is complete from the first paint and goes live as frames arrive, and
 * a refused or silent stream leaves the one-shot figures standing rather than a blank. The four
 * resource widgets are the exception that proves it: until the stream has delivered a reading, the
 * one-shot resource card takes the processor widget's place and the memory, disk and network widgets
 * are not drawn (they have no one-shot form; the resource card shows all three figures).
 *
 * **A key this bundle cannot draw is never in `widgets`** (`useDashboardWidgets` drops it), and a
 * widget with nothing to draw yet (a finished checklist)
 * leaves its cell empty, which `empty:hidden` removes so it leaves no gap. Each section says whether
 * the panel could read it: one it could not is drawn as "could not be read" in its own place, never
 * as an empty list or a zero (issue #248), so the rest of the screen stays up.
 *
 * Wide widgets take both columns once the CONTENT PANEL is wide enough for two readable cards
 * (`@3xl`, 48rem of container width), not when the viewport is: under a large system font the
 * sidebar takes most of a wide viewport and each "half" was left a sliver.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { DashboardWidgetSpec } from '../../composables/useDashboardWidgets'
import { useLiveDashboardStore } from '../../stores/liveDashboard'
import type { AuditEvent } from '../../types/audit'
import type { AdministratorDashboard, DashboardAttention, DashboardCounts, DashboardSection } from '../../types/dashboard'
import type { ServiceStatus } from '../../types/monitoring'
import DashboardAttentionList from './DashboardAttentionList.vue'
import DashboardCpuCard from './DashboardCpuCard.vue'
import DashboardDiskCard from './DashboardDiskCard.vue'
import DashboardFirstRunChecklist from './DashboardFirstRunChecklist.vue'
import DashboardMemoryCard from './DashboardMemoryCard.vue'
import DashboardNetworkCard from './DashboardNetworkCard.vue'
import DashboardRecentAudit from './DashboardRecentAudit.vue'
import DashboardResourceCard from './DashboardResourceCard.vue'
import DashboardServicesCard from './DashboardServicesCard.vue'
import DashboardTotalsGrid from './DashboardTotalsGrid.vue'
import DashboardUnavailableCard from './DashboardUnavailableCard.vue'

const props = defineProps<{
  /** The one-shot read of the server's sections, which stands in for anything the stream has not pushed. */
  server: AdministratorDashboard
  /** The widgets to draw, in order, already without the ones the person hid. */
  widgets: DashboardWidgetSpec[]
  /** Whether the monitoring screen can be opened (its module is licensed). */
  linksToMonitoring: boolean
}>()

const { t } = useI18n()
const live = useLiveDashboardStore()

/** What the server holds: the stream's latest push, else the one-shot read's. */
const counts: ComputedRef<DashboardCounts> = computed(() => {
  return live.totals?.counts ?? props.server.counts
})

/** What needs looking at: the stream's latest push, else the one-shot read's. */
const attention: ComputedRef<DashboardAttention> = computed(() => {
  return live.totals?.attention ?? props.server.attention
})

/** The watched services: the stream's latest push, else the one-shot read's. */
const services: ComputedRef<DashboardSection<ServiceStatus[]>> = computed(() => {
  return live.services ?? props.server.services
})

/** The newest journal entries: the stream's latest push, else the one-shot read's. */
const recentAudit: ComputedRef<DashboardSection<AuditEvent[]>> = computed(() => {
  return live.activity?.recentAudit ?? props.server.recentAudit
})
</script>

<template>
  <div class="@container">
    <div class="grid gap-4 @3xl:grid-cols-2" data-testid="dashboard-widgets">
      <div
        v-for="widget in widgets"
        :key="widget.key"
        class="min-w-0 empty:hidden"
        :class="{ '@3xl:col-span-2': widget.wide }"
        :data-widget="widget.key"
      >
        <DashboardAttentionList v-if="widget.key === 'attention'" :attention="attention" />

        <!-- What a new server still needs. The card reads its own checklist and draws nothing until
             the panel has answered, nor once every step is done. -->
        <DashboardFirstRunChecklist v-else-if="widget.key === 'firstRun'" />

        <template v-else-if="widget.key === 'cpu'">
          <DashboardCpuCard
            v-if="live.latest !== null"
            :points="live.points"
            :latest="live.latest"
            :links-to-monitoring="linksToMonitoring"
          />
          <DashboardResourceCard v-else-if="server.resources.state === 'loaded'" :resources="server.resources.data" />
          <DashboardUnavailableCard
            v-else
            :title="t('app.status.resources.title')"
            :message="t('app.status.resources.unavailable')"
          />
        </template>

        <DashboardMemoryCard
          v-else-if="widget.key === 'memory' && live.latest !== null"
          :points="live.points"
          :latest="live.latest"
          :links-to-monitoring="linksToMonitoring"
        />
        <DashboardDiskCard
          v-else-if="widget.key === 'disk' && live.latest !== null"
          :points="live.points"
          :latest="live.latest"
          :links-to-monitoring="linksToMonitoring"
        />
        <DashboardNetworkCard
          v-else-if="widget.key === 'network' && live.latest !== null"
          :points="live.points"
          :latest="live.latest"
          :links-to-monitoring="linksToMonitoring"
        />

        <DashboardServicesCard
          v-else-if="widget.key === 'services'"
          :services="services"
          :links-to-monitoring="linksToMonitoring"
        />
        <DashboardTotalsGrid v-else-if="widget.key === 'totals'" :counts="counts" />

        <DashboardRecentAudit v-else-if="widget.key === 'activity'" :entries="recentAudit" />
      </div>
    </div>
  </div>
</template>
