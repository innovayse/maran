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
 * what the server is actually doing — because this is the screen an operator sees most often, and a
 * single health sentence alone would tell them nothing about the machine.
 *
 * The two reads are separate on purpose. The health check has no authentication and no dependency on
 * the database; the dashboard has both. A panel whose database is down must still be able to say
 * "the API is up, the database is not" — which it cannot do if the sentence waits on a read that is
 * going to fail.
 *
 * **The screen is the approved dashboard mockup** (`docs/design/redesign-2026-10/`): an accent eyebrow
 * and a title with the primary action as a pill, then the one thing that needs attention
 * as a notice, then the figure cards, then the services, the quick actions and the latest activity.
 * The health verdict is the heading's sentence when it is good, and a notice of its own when it is
 * not. The mockup's accounts table is not drawn: the dashboard payload carries no account rows, and
 * loading the accounts list here would be a second read this page never made — the count tile and
 * the "New account" pill lead to the screen that owns them.
 *
 * Quick actions and the pill are offered only for modules the licence permits, so none of them is a
 * link that ends at the upgrade page.
 *
 * **A customer gets their own, smaller card instead of the server's.** They are shown the verdict
 * and a card of shortcuts into what their account holds, from data the page already has: the
 * licence catalogue and the signed-in identity. Nothing about the server as a whole is drawn for
 * them, and an empty panel is never drawn at all.
 *
 * State comes from the stores only; this page never touches the API layer (rules/vue.md: API
 * composables are called from stores only).
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { RouterLink } from 'vue-router'
import DashboardAttentionList from '../components/dashboard/DashboardAttentionList.vue'
import DashboardCountGrid from '../components/dashboard/DashboardCountGrid.vue'
import DashboardQuickActions from '../components/dashboard/DashboardQuickActions.vue'
import DashboardRecentAudit from '../components/dashboard/DashboardRecentAudit.vue'
import DashboardResourceCard from '../components/dashboard/DashboardResourceCard.vue'
import ServiceStatusBadges from '../components/monitoring/ServiceStatusBadges.vue'
import UiAlert from '../components/ui/UiAlert.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiCard from '../components/ui/UiCard.vue'
import UiIcon from '../components/ui/UiIcon.vue'
import UiLoadingRegion from '../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../components/ui/UiSectionHeading.vue'
import { useCallerRole } from '../composables/useCallerRole'
import { useAuthStore } from '../stores/auth'
import { useDashboardStore } from '../stores/dashboard'
import { useModulesStore } from '../stores/modules'
import { useSystemStore } from '../stores/system'
import type { QuickAction } from '../types/quickAction'
import type { LoadingState } from '../types/loading'

const { t } = useI18n()
const auth = useAuthStore()
const system = useSystemStore()
const dashboard = useDashboardStore()
const modules = useModulesStore()
const { isAdmin } = useCallerRole()

/**
 * The heading region's state. The verdict is part of the heading rather than in place of it, so a
 * failed check does not replace the region (the title stays, the failure is an alert under it):
 * for the region the check is either on its way or settled, and which way it settled is
 * {@link system}'s `healthState`, read where the alert is drawn.
 */
const headingState: ComputedRef<LoadingState> = computed(() => {
  return system.healthState === 'loading' ? 'loading' : 'loaded'
})

/**
 * Whether the dashboard sections should be drawn.
 *
 * Both halves are required. A loaded read alone would draw zeros nobody measured on the first paint,
 * and `isAdministrator` alone would draw empty panels at a customer. The panel decided the second
 * one; this is the SPA agreeing with it, which is advice and not enforcement (rules/vue.md).
 */
const showsServerDetail: ComputedRef<boolean> = computed(() => {
  return dashboard.loadState === 'loaded' && dashboard.isAdministrator
})

/**
 * Whether the customer's card of shortcuts should be drawn: the panel answered, and answered that
 * the caller is not an administrator. The mirror of {@link showsServerDetail}, so exactly one of the
 * two is ever true and a caller is never shown both or neither once the read has settled.
 */
const showsCustomerArea: ComputedRef<boolean> = computed(() => {
  return dashboard.loadState === 'loaded' && !dashboard.isAdministrator
})

/**
 * Whether the figure cards are held with placeholders while the dashboard is read. Only for a
 * caller whose session says they administer the server: the panel's own answer decides what is
 * drawn once it is in, and a customer, who will not be shown the server, is not shown its
 * placeholders either.
 */
const holdsServerDetail: ComputedRef<boolean> = computed(() => {
  return dashboard.loadState === 'loading' && isAdmin.value
})

/** Whether the header's pill is offered: only to somebody shown the server, and only if Accounts is licensed. */
const offersNewAccount: ComputedRef<boolean> = computed(() => {
  return showsServerDetail.value && modules.isEnabled('accounts')
})

/** Whether the services card links on to the monitoring screen. */
const linksToMonitoring: ComputedRef<boolean> = computed(() => {
  return modules.isEnabled('monitoring')
})

/**
 * The shortcuts an operator reaches for on a new server, in the order they build one up: an account,
 * then its site, then its database. Each leads to the screen that owns the work — the dashboard does
 * none of it itself.
 */
const quickActions: ComputedRef<QuickAction[]> = computed(() => {
  return permitted([
    { key: 'createAccount', to: '/accounts/new', icon: 'users', module: 'accounts', label: t('app.status.quickActions.createAccount') },
    { key: 'addSite', to: '/sites/new', icon: 'earth', module: 'sites', label: t('app.status.quickActions.addSite') },
    { key: 'createDatabase', to: '/databases', icon: 'database', module: 'databases', label: t('app.status.quickActions.createDatabase') },
    { key: 'backups', to: '/backups', icon: 'archive', module: 'backups', label: t('app.status.quickActions.backups') },
  ])
})

/**
 * What a customer can open from here: their own account, then the things it holds. The account row
 * is offered only to an identity that owns one — `/accounts/me` answers "not found" for anybody
 * else — and each of the rest only when the licence permits its module, so no row ends at the
 * upgrade page. Advice, not enforcement: the panel refuses whatever the sidebar offers.
 */
const customerActions: ComputedRef<QuickAction[]> = computed(() => {
  const ownsAccount = auth.user !== null && auth.user.accountId !== null
  return permitted([
    { key: 'myAccount', to: '/my-account', icon: 'user', module: null, label: t('app.status.customerArea.myAccount') },
    { key: 'mySites', to: '/sites', icon: 'earth', module: 'sites', label: t('app.status.customerArea.mySites') },
    { key: 'myDatabases', to: '/databases', icon: 'database', module: 'databases', label: t('app.status.customerArea.myDatabases') },
    { key: 'myBackups', to: '/backups', icon: 'archive', module: 'backups', label: t('app.status.customerArea.myBackups') },
  ]).filter((action) => {
    return action.key !== 'myAccount' || ownsAccount
  })
})

/**
 * Keeps the shortcuts the licence permits.
 * @param candidates The shortcuts in the order they should appear.
 * @returns The permitted ones, in the same order.
 */
const permitted = (candidates: QuickAction[]): QuickAction[] => {
  return candidates.filter((candidate) => {
    return candidate.module === null || modules.isEnabled(candidate.module)
  })
}

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
  <section class="flex w-full flex-col gap-6">
    <!-- Healthy: the backend answered at all, and the heading's subtitle says exactly that. It does
         not interpolate the answer's `status` field: that is an English machine token, which would
         sit inside a translated sentence, and on success it can only ever be the literal `ok`
         (`/health` constructs its report with it). A degraded state, if the panel ever reports one,
         needs a branch of its own with its own translated sentence, not a raw field shown to a
         customer. While the check is on its way the subtitle holds a placeholder line. -->
    <UiLoadingRegion :state="headingState">
      <div class="flex flex-col gap-6">
        <UiPageHeading
          :eyebrow="t('app.status.eyebrow')"
          :title="t('app.status.heading')"
          :subtitle="system.healthState === 'error' ? undefined : t('app.status.ok')"
          :subtitle-loading="system.healthState === 'loading'"
        >
          <template v-if="offersNewAccount" #actions>
            <UiButton to="/accounts/new" class="whitespace-nowrap">
              <UiIcon name="plus" />
              {{ t('app.status.actions.newAccount') }}
            </UiButton>
          </template>
        </UiPageHeading>

        <!-- A bad verdict: the backend's own already-localized text, verbatim (rules/vue.md), or the
             one frontend-owned string when the request never reached it. A good one is the
             heading's subtitle above and draws nothing here. -->
        <UiAlert v-if="system.healthState === 'error'" variant="error">
          {{ system.errorMessage ?? t('app.status.unreachable') }}
        </UiAlert>
      </div>
    </UiLoadingRegion>

    <UiLoadingRegion :state="dashboard.loadState">
      <!-- The dashboard read failed for somebody who should have seen it. Said out loud rather than
           left as an absence: a screen that silently shows only the health sentence to an
           administrator looks exactly like the defect this page was fixing. -->
      <template #error>
        <UiAlert variant="error">{{ dashboard.errorMessage ?? t('app.status.unreachable') }}</UiAlert>
      </template>

      <div class="flex flex-col gap-6">
        <!-- While the read is in flight, the figure cards are drawn with their real labels and a
             placeholder for every figure, so the answer lands without moving anything. -->
        <template v-if="holdsServerDetail">
          <DashboardCountGrid loading />
          <DashboardResourceCard loading />
        </template>

        <!-- Below the verdict, and only for a caller the panel filled the sections for. -->
        <template v-else-if="showsServerDetail">
          <!-- Each section is drawn only when the panel answered it. A missing section means the agent
               or a module could not be read, and the panel isolates that on its own side — so the rest
               of the screen stays up instead of going dark together. The attention notice is first:
               the one thing that needs looking at goes above everything else. -->
          <DashboardAttentionList v-if="dashboard.attention !== null" :attention="dashboard.attention" />

          <DashboardCountGrid v-if="dashboard.counts !== null" :counts="dashboard.counts" />
          <DashboardResourceCard v-if="dashboard.resources !== null" :resources="dashboard.resources" />

          <UiCard v-if="dashboard.services.length > 0">
            <UiSectionHeading class="mb-4" :title="t('monitoring.services.title')">
              <template v-if="linksToMonitoring" #action>
                <RouterLink
                  to="/monitoring"
                  class="rounded-control text-accent-text hover:underline focus-visible:shadow-focus focus-visible:outline-none motion-reduce:transition-none"
                >{{ t('monitoring.heading') }}</RouterLink>
              </template>
            </UiSectionHeading>
            <ServiceStatusBadges :statuses="dashboard.services" />
          </UiCard>

          <!-- Two columns only when the CONTENT PANEL is wide enough for two readable cards (`@3xl`,
               48rem of container width), not when the viewport is: under a large system font the
               rem-sized sidebar takes most of a wide viewport and each "half" was left a sliver. -->
          <div class="@container">
            <div class="grid gap-4 @3xl:grid-cols-2">
              <DashboardQuickActions
                v-if="quickActions.length > 0"
                :title="t('app.status.quickActions.title')"
                :actions="quickActions"
              />

              <DashboardRecentAudit :entries="dashboard.recentAudit" :class="{ '@3xl:col-span-2': quickActions.length === 0 }" />
            </div>
          </div>
        </template>

        <!-- A customer is shown their own shortcuts and nothing of the server. The card is drawn only if
             it has a row: an empty panel under a heading reads as a failed load. -->
        <div v-else-if="showsCustomerArea && customerActions.length > 0" class="@container">
          <div class="grid gap-4 @3xl:grid-cols-2">
            <DashboardQuickActions :title="t('app.status.customerArea.title')" :actions="customerActions" />
          </div>
        </div>
      </div>
    </UiLoadingRegion>
  </section>
</template>
