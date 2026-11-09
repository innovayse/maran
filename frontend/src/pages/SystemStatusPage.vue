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
 * **For an administrator the screen is live.** One stream (`stores/liveDashboard`) is opened while
 * the page is on screen and closed when it is left; the processor, memory, disk and network widgets
 * draw its readings as charts, and the totals, services and activity widgets follow its
 * pushes. The one-shot read above still happens first: it decides the audience, paints the page
 * before the stream's first frame, and is what stays on screen when the stream is refused or not yet
 * delivering (the resource card stands in for the four live widgets then, so a screen never goes
 * blank for want of a stream).
 *
 * **Which widgets, and in what order, is the person's layout** (`stores/dashboardLayout`, resolved
 * by `useDashboardWidgets`). While it is on its way, or if it cannot be read, every widget the
 * bundle can draw is shown in its own order: a failed preference read never blanks the dashboard.
 * A widget a person hid is not drawn; the customise sheet in the heading turns it back on.
 *
 * State comes from the stores only; this page never touches the API layer (rules/vue.md: API
 * composables are called from stores only).
 */
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import DashboardCustomizeSheet from '../components/dashboard/DashboardCustomizeSheet.vue'
import DashboardLiveStatus from '../components/dashboard/DashboardLiveStatus.vue'
import DashboardQuickActions from '../components/dashboard/DashboardQuickActions.vue'
import DashboardQuickActionsBar from '../components/dashboard/DashboardQuickActionsBar.vue'
import DashboardResourceCard from '../components/dashboard/DashboardResourceCard.vue'
import DashboardWidgetGrid from '../components/dashboard/DashboardWidgetGrid.vue'
import DashboardTotalsGrid from '../components/dashboard/DashboardTotalsGrid.vue'
import UiAlert from '../components/ui/UiAlert.vue'
import UiButton from '../components/ui/UiButton.vue'
import UiIcon from '../components/ui/UiIcon.vue'
import UiLoadingRegion from '../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../components/ui/UiPageHeading.vue'
import UiSkeleton from '../components/ui/UiSkeleton.vue'
import { useCallerRole } from '../composables/useCallerRole'
import { useDashboardWidgets } from '../composables/useDashboardWidgets'
import { useAuthStore } from '../stores/auth'
import { useDashboardStore } from '../stores/dashboard'
import { useDashboardLayoutStore } from '../stores/dashboardLayout'
import { useLiveDashboardStore } from '../stores/liveDashboard'
import { useModulesStore } from '../stores/modules'
import { useSystemStore } from '../stores/system'
import type { AdministratorDashboard } from '../types/dashboard'
import type { DashboardLayout } from '../types/dashboardLayout'
import type { QuickAction } from '../types/quickAction'
import type { LoadingState } from '../types/loading'
import { attentionIsClear } from '../utils/attentionIsClear'

/** Which audience the dashboard is drawn for; see {@link view}. */
type DashboardView = 'pending' | 'pendingServer' | 'server' | 'customer' | 'failed'

const { t } = useI18n()
const auth = useAuthStore()
const system = useSystemStore()
const dashboard = useDashboardStore()
const modules = useModulesStore()
const live = useLiveDashboardStore()
const layoutStore = useDashboardLayoutStore()
const { widgets, actions: shortcuts, knownWidgetKeys } = useDashboardWidgets()
const { isAdmin } = useCallerRole()

/** Whether the customise sheet is open. */
const customizing: Ref<boolean> = ref(false)

/**
 * What identifies the session the screen belongs to: the person, their role and the account they
 * act for ("View as this account" is another identity, like any sign-in). The stream and the
 * figures it delivered belong to one identity and must not outlive it.
 */
const identity: ComputedRef<string> = computed(() => {
  const user = auth.user
  return user === null ? '' : `${user.id}:${user.role}:${user.accountId ?? ''}`
})

/** The administrator's sections once the read has arrived, or `null` for a customer's answer or a read still on its way. */
const server: ComputedRef<AdministratorDashboard | null> = computed(() => {
  return dashboard.loadState === 'loaded' ? dashboard.administrator : null
})

/**
 * Which audience the dashboard is drawn for, from the read's state and who the panel says the caller is.
 * - `pendingServer`: the read is in flight for a session that says it administers the server, so the
 *   figure cards are held with placeholders. A customer, who will not be shown the server, gets no
 *   placeholders (`pending`).
 * - `server` / `customer`: the panel answered and decided which sections the caller sees. That
 *   decision is the panel's, carried by the answer's `audience`; this is the SPA agreeing with it,
 *   which is advice and not enforcement (rules/vue.md). A loaded read alone would draw zeros nobody
 *   measured, and a stale administrator answer alone would draw empty panels at a customer.
 * - `failed`: the read ended in an error, drawn by the region's own error slot.
 */
const view: ComputedRef<DashboardView> = computed(() => {
  return viewOf(dashboard.loadState)
})

/**
 * The heading's verdict sentence, or `undefined` when there is none to say.
 *
 * "All systems are operational" is a claim about every check, so it is said only when the health
 * check passed AND, for an administrator, nothing in the attention notice asks to be looked at: the
 * notice below carries the bad news, and a sentence above it saying the opposite contradicts it. For
 * somebody who is not shown the server there is no attention to weigh, and the health check alone decides.
 */
const verdict: ComputedRef<string | undefined> = computed(() => {
  if (system.healthState === 'error') {
    return undefined
  }
  return server.value !== null && !attentionIsClear(server.value.attention) ? undefined : t('app.status.ok')
})

/** Whether the verdict is still being worked out: the health check, or an administrator's attention read, is in flight. */
const verdictLoading: ComputedRef<boolean> = computed(() => {
  return system.healthState === 'loading' || view.value === 'pendingServer'
})

/**
 * The heading region's state. The verdict is part of the heading rather than in place of it, so a
 * failed check does not replace the region (the title stays, the failure is an alert under it):
 * for the region the verdict is either on its way (the health check, or an administrator's attention
 * read, see {@link verdictLoading}) or settled, and which way a settled check went is {@link system}'s
 * `healthState`, read where the alert is drawn. The region must wait for the whole verdict, not only
 * the health check: its subtitle placeholder stays invisible while the region is not waiting, so a
 * region that reported "loaded" during the attention read would hold a placeholder nobody can see.
 */
const headingState: ComputedRef<LoadingState> = computed(() => {
  return verdictLoading.value ? 'loading' : 'loaded'
})

/** Whether the header's pill is offered: only to somebody shown the server, and only if Accounts is licensed. */
const offersNewAccount: ComputedRef<boolean> = computed(() => {
  return view.value === 'server' && modules.isEnabled('accounts')
})

/** Whether the services card links on to the monitoring screen. */
const linksToMonitoring: ComputedRef<boolean> = computed(() => {
  return modules.isEnabled('monitoring')
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
 * Names the audience a read in the given state is drawn for.
 *
 * A function rather than a switch inside the computed so a state added to `LoadingState` is a
 * compile error here (the return type is not satisfied) and not a silent `undefined`.
 * @param state The dashboard read's state.
 * @returns The view to draw.
 */
const viewOf = (state: LoadingState): DashboardView => {
  switch (state) {
    case 'loading':
      return isAdmin.value ? 'pendingServer' : 'pending'
    case 'error':
      return 'failed'
    case 'loaded':
      return server.value === null ? 'customer' : 'server'
  }
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

/**
 * Stores the layout the customise sheet produced and closes the sheet when the panel accepted it.
 * A refusal keeps the sheet open with the panel's reason, so nothing the person arranged is lost.
 * @param layout The whole layout.
 * @returns Resolves once the save settled.
 */
const saveLayout = async (layout: DashboardLayout): Promise<void> => {
  if (await layoutStore.save(layout)) {
    customizing.value = false
  }
}

onMounted(async () => {
  // The layout is the person's own preference and an administrator's alone; it does not wait for,
  // or hold up, the dashboard read.
  if (isAdmin.value) {
    void layoutStore.load()
  }
  await refresh()
})

// The stream is opened once the panel has confirmed this screen is the server's (not before: a
// customer must not open a stream the panel would refuse) and closed when the page is left, so no
// connection and no held figure outlives the screen that shows them.
watch(
  view,
  (audience: DashboardView) => {
    if (audience === 'server') {
      live.open()
    }
  },
  { immediate: true },
)

// A new identity (a sign-in as someone else, "View as this account" starting or ending) must not
// keep the previous one's stream or figures on screen: close the stream, read the screen again
// for the new caller, and the audience watch above opens a stream only if that caller is the server's.
watch(identity, () => {
  live.close()
  if (isAdmin.value) {
    void layoutStore.load()
  }
  void refresh()
})

onBeforeUnmount(() => {
  live.close()
})
</script>

<template>
  <section class="flex w-full flex-col gap-6">
    <!-- Healthy: the backend answered at all, and the heading's subtitle says exactly that. It does
         not interpolate any word of the answer: `agent` and `database` are English machine tokens,
         which would sit inside a translated sentence. A degraded state, if the panel ever reports
         one, needs a branch of its own with its own translated sentence, not a raw field shown to a
         customer. While the check is on its way the subtitle holds a placeholder line. -->
    <UiLoadingRegion :state="headingState">
      <div class="flex flex-col gap-6">
        <UiPageHeading
          :title="t('app.status.heading')"
          :subtitle="verdict"
          :subtitle-loading="verdictLoading"
        >
          <template v-if="view === 'server'" #actions>
            <div class="flex flex-wrap items-center gap-2">
              <DashboardLiveStatus :status="live.status" />
              <UiButton
                v-if="layoutStore.layout !== null"
                variant="secondary"
                class="whitespace-nowrap"
                data-testid="customize-open"
                @click="customizing = true"
              >
                <UiIcon name="settings" />
                {{ t('liveDashboard.customize.open') }}
              </UiButton>
              <UiButton v-if="offersNewAccount" to="/accounts/new" class="whitespace-nowrap">
                <UiIcon name="plus" />
                {{ t('app.status.actions.newAccount') }}
              </UiButton>
            </div>
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
        <template v-if="view === 'pendingServer'">
          <!-- The shortcuts need nothing from the answer, so they are drawn at once and the figures'
               placeholders below them are not pushed down when the answer lands. -->
          <DashboardQuickActionsBar v-if="shortcuts.length > 0" :actions="shortcuts" />
          <!-- The notice is the first thing the answer draws (one line when nothing needs attention),
               so its slot is held by a notice of the same box with a placeholder line for its text, in a row
               as tall as one line of that text (`text-sm` at `leading-normal`).
               Without it every figure below drops by the notice's height at the moment the answer
               lands. -->
          <UiAlert variant="info">
            <div class="flex h-5.25 items-center"><UiSkeleton /></div>
          </UiAlert>
          <DashboardTotalsGrid :counts="null" />
          <DashboardResourceCard :resources="null" />
        </template>

        <!-- Below the verdict, and only for a caller the panel filled the sections for. -->
        <template v-else-if="server !== null">
          <!-- The shortcuts lead the screen: the things an operator does most, one click each. -->
          <DashboardQuickActionsBar v-if="shortcuts.length > 0" :actions="shortcuts" />

          <DashboardWidgetGrid :server="server" :widgets="widgets" :links-to-monitoring="linksToMonitoring" />

          <DashboardCustomizeSheet
            v-if="layoutStore.layout !== null"
            :open="customizing"
            :layout="layoutStore.layout"
            :widget-keys="knownWidgetKeys"
            :saving="layoutStore.saving"
            :error-message="layoutStore.submitMessage"
            @close="customizing = false"
            @save="saveLayout"
          />
        </template>

        <!-- A customer is shown their own shortcuts and nothing of the server. The card is drawn only if
             it has a row: an empty panel under a heading reads as a failed load. -->
        <div v-else-if="view === 'customer' && customerActions.length > 0" class="@container">
          <div class="grid gap-4 @3xl:grid-cols-2">
            <DashboardQuickActions :title="t('app.status.customerArea.title')" :actions="customerActions" />
          </div>
        </div>
      </div>
    </UiLoadingRegion>
  </section>
</template>
