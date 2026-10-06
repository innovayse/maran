<script setup lang="ts">
/**
 * The panel's landing page: every sign-in arrives here.
 *
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout it is
 * nested under (`DefaultLayout`), and a nested one would break screen-reader landmark navigation.
 *
 * **Two things, in this order, and the order is the point.** First, whether the panel is answering
 * at all: that is what this route has always been and what it must keep being, because it is the one
 * screen that has to say something useful when everything else cannot. Then, for an administrator,
 * what the server is actually doing — which is what issue #70 was about: the whole page used to be
 * the single sentence below, on the screen an operator sees most often.
 *
 * The two reads are separate on purpose. The health check has no authentication and no dependency on
 * the database; the dashboard has both. A panel whose database is down must still be able to say
 * "the API is up, the database is not" — which it cannot do if the sentence waits on a read that is
 * going to fail.
 *
 * State comes from the stores only; this page never touches the API layer (rules/vue.md: API
 * composables are called from stores only).
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import DashboardAttentionList from '../components/dashboard/DashboardAttentionList.vue'
import DashboardCountGrid from '../components/dashboard/DashboardCountGrid.vue'
import DashboardRecentAudit from '../components/dashboard/DashboardRecentAudit.vue'
import DashboardResourceCard from '../components/dashboard/DashboardResourceCard.vue'
import ServiceStatusBadges from '../components/monitoring/ServiceStatusBadges.vue'
import UiAlert from '../components/ui/UiAlert.vue'
import UiCard from '../components/ui/UiCard.vue'
import UiPageHeading from '../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../components/ui/UiSectionHeading.vue'
import UiSpinner from '../components/ui/UiSpinner.vue'
import { useDashboardStore } from '../stores/dashboard'
import { useSystemStore } from '../stores/system'

const { t } = useI18n()
const system = useSystemStore()
const dashboard = useDashboardStore()

/**
 * Whether the dashboard sections should be drawn.
 *
 * Both halves are required. `isLoaded` alone would draw zeros nobody measured on the first paint,
 * and `isAdministrator` alone would draw empty panels at a customer. The panel decided the second
 * one; this is the SPA agreeing with it, which is advice and not enforcement (rules/vue.md).
 */
const showsServerDetail: ComputedRef<boolean> = computed(() => {
  return dashboard.isLoaded && dashboard.isAdministrator
})

/**
 * Reads both the health verdict and, for whoever may see it, the server's own state.
 *
 * Together rather than one after the other: they answer to different parts of the screen and
 * neither waits on the other's failure.
 * @returns Resolves when both reads have settled, successfully or not.
 */
const refresh = async (): Promise<void> => {
  await Promise.all([system.checkHealth(), dashboard.load()])
}

onMounted(refresh)
</script>

<template>
  <section class="w-full">
    <UiPageHeading class="mb-4" :title="t('app.status.heading')" />

    <!-- Healthy: the backend answered at all. The sentence says so and stops there.
         It used to interpolate the answer's `status` field, which put an English machine token —
         `(ok)` — inside a Russian sentence on the first screen after login, in all three
         languages, as the only line on the page. The parenthesis also
         carried nothing: `/health` constructs its report with the literal `"ok"`
         (`Maran.Host/HealthChecks/HealthEndpoint.cs`), and any answer that is not a 2xx takes one
         of the branches below, so the slot could never hold a second value. A degraded state, if
         the panel ever reports one, needs a branch of its own with its own translated sentence —
         not a raw field shown to a customer. -->
    <UiCard v-if="system.status !== null">
      <p>{{ t('app.status.ok') }}</p>
    </UiCard>

    <!-- Backend answered with an error: its text is already localized
         server-side, render it verbatim (rules/vue.md). A failure gets the
         panel's error treatment here exactly as it does on every other
         screen, rather than reading as ordinary card copy. -->
    <UiAlert v-else-if="system.errorMessage !== null" variant="error">{{ system.errorMessage }}</UiAlert>

    <!-- The request never reached the backend: the one case with no
         server-provided message, covered by a frontend-owned string. -->
    <UiAlert v-else-if="system.unreachable" variant="error">{{ t('app.status.unreachable') }}</UiAlert>

    <!-- Nothing has settled yet. The store keeps no loading flag, so the
         pending state is "no verdict of any kind" — without this branch the
         first paint of the page is a blank panel. -->
    <UiSpinner v-else :label="t('app.status.checking')" />

    <!-- Below the verdict, and only for a caller the panel filled the sections for. A customer sees
         the verdict and nothing else: their own landing screen is the client zone, which is issue
         #49's subject, and a second one invented here is a thing that issue would have to undo. -->
    <div v-if="showsServerDetail" class="mt-4 flex flex-col gap-4">
      <!-- Each section is drawn only when the panel answered it. A missing section means the agent
           or a module could not be read, and the panel isolates that on its own side — so the rest
           of the screen stays up instead of going dark together. -->
      <DashboardResourceCard v-if="dashboard.resources !== null" :resources="dashboard.resources" />

      <UiCard v-if="dashboard.services.length > 0">
        <UiSectionHeading class="mb-3" :title="t('monitoring.services.title')" />
        <ServiceStatusBadges :statuses="dashboard.services" />
      </UiCard>

      <DashboardCountGrid v-if="dashboard.counts !== null" :counts="dashboard.counts" />
      <DashboardAttentionList v-if="dashboard.attention !== null" :attention="dashboard.attention" />
      <DashboardRecentAudit :entries="dashboard.recentAudit" />
    </div>

    <!-- The dashboard read failed for somebody who should have seen it. Said out loud rather than
         left as an absence: a screen that silently shows only the health sentence to an
         administrator looks exactly like the defect this page was fixing. -->
    <UiAlert v-else-if="dashboard.errorMessage !== null" class="mt-4" variant="error">
      {{ dashboard.errorMessage }}
    </UiAlert>
  </section>
</template>
