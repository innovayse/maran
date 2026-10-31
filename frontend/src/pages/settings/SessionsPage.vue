<script setup lang="ts">
/**
 * The caller's signed-in devices, with a way to end any of them. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout
 * this page is nested under.
 *
 * Every row is the caller's own: the endpoint takes no user parameter, and a
 * session belonging to somebody else answers "not found" rather than "forbidden",
 * so this screen cannot be pointed at another person's devices.
 *
 * Laid out as the redesign's screens are (docs/design/redesign-2026-10): the heading block with
 * its eyebrow and the two commands on its right, then the devices in one panel. Rebuilt from
 * zero, with the behaviour unchanged:
 *
 * - Ending a session confirms first, in a dialog named for the address it ends, and the dialog
 *   says so when the row is this browser's own. The destructive command is never the default.
 * - The confirmation lives OUTSIDE the branch that swaps the table for an alert or an empty
 *   state, so a refresh cannot unmount it mid-request.
 * - While the devices are on the way the table keeps its real header over placeholder rows drawn
 *   from its own `columns`, inside a {@link UiLoadingRegion} that announces the wait once.
 * - A table the width of the screen scrolls inside its own container at 390px, so the page
 *   itself never scrolls sideways; the device name truncates instead of widening the column.
 * - Only the fields the screen knows are rendered, never the response wholesale (a leaked
 *   token hash must not reach the page).
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiIconTile from '../../components/ui/UiIconTile.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { useCallerRole } from '../../composables/useCallerRole'
import { useAuthStore } from '../../stores/auth'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()
const localeStore = useLocaleStore()
const { isAdmin } = useCallerRole()

/** The session the user has asked to end and is being asked to confirm, or `null`. */
const pendingRevocation: Ref<string | null> = ref(null)

/**
 * Whether the confirmed revocation is with the server.
 *
 * The page's own flag rather than the store's `loading`: `revokeSession` sets no loading state —
 * it must not, because `loading` swaps the rows for placeholders — and the dialog needs the
 * one thing that IS in flight.
 */
const revoking: Ref<boolean> = ref(false)

/** The devices table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'device', label: t('app.sessions.deviceColumn'), kind: 'identity' },
    { key: 'address', label: t('app.sessions.addressColumn'), kind: 'mono' },
    { key: 'signedIn', label: t('app.sessions.signedInColumn'), kind: 'date', align: 'end' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})


/** The session the confirmation is open for, or `null` when none is. */
const pendingSession: ComputedRef<{ id: string; ipAddress: string; isCurrent: boolean } | null> =
  computed(() => {
    return (
      authStore.sessions.find((session) => {
        return session.id === pendingRevocation.value
      }) ?? null
    )
  })

/** The confirmation's accessible name, naming the device that would be signed out. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const pending = pendingSession.value
  return pending === null
    ? ''
    : t('app.sessions.confirmEndTitle', { address: pending.ipAddress })
})

/** The consequence the confirmation asks about — ending this browser's own session is not the same. */
const confirmationQuestion: ComputedRef<string> = computed(() => {
  return pendingSession.value?.isCurrent === true
    ? t('app.sessions.confirmCurrent')
    : t('app.sessions.confirm')
})

/**
 * Asks for confirmation before ending a session.
 * @param id The session the user clicked.
 * @returns Nothing; the row switches to its confirming state.
 */
const askToRevoke = (id: string): void => {
  pendingRevocation.value = id
}

/**
 * Abandons a pending revocation.
 * @returns Nothing; the row returns to its normal state.
 */
const cancelRevocation = (): void => {
  pendingRevocation.value = null
}

/**
 * Ends the confirmed session. Ending the current one signs this browser out, so the page
 * leaves for the sign-in screen rather than staying on a list it may no longer read.
 * @returns Resolves once the request has settled.
 */
const revoke = async (): Promise<void> => {
  const pending = pendingSession.value
  if (pending === null) {
    return
  }

  revoking.value = true
  const ended = await authStore.revokeSession(pending.id)
  revoking.value = false

  // Closed after the request settles rather than before it is sent: the dialog holds the spinner
  // that tells the user the panel is working.
  pendingRevocation.value = null

  if (ended && pending.isCurrent) {
    await authStore.logout()
    await router.push({ name: 'login' })
  }
}

/**
 * Signs out of every device and returns to the sign-in screen.
 * @returns Resolves once the request has settled.
 */
const signOutEverywhere = async (): Promise<void> => {
  await authStore.logoutEverywhere()
  await router.push({ name: 'login' })
}

onMounted(async () => {
  await authStore.loadSessions()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :title="t('app.sessions.heading')"
      :subtitle="t('app.sessions.subtitle')"
    >
      <template #actions>
        <!-- The panel's sign-in policy: how long a session lives, how many failures earn a ban.
             It belongs with access, which is this screen, rather than in the account menu where it
             read as a setting about the person looking at it. A link, not a button: it navigates.
             Administrators only; hiding it from a customer is presentation, the endpoint refuses
             them on its own (rules/security.md). -->
        <UiButton v-if="isAdmin" variant="secondary" :to="{ name: 'security-policy' }">{{ t('app.sessions.securityPolicyLink') }}</UiButton>
        <UiButton
          v-if="authStore.sessions.length > 0"
          variant="destructive"
          @click="signOutEverywhere"
        >
          {{ t('app.sessions.signOutEverywhere') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <!-- A refused sign-out, reported above the list it was asked from rather than in its place. -->
    <UiAlert v-if="authStore.sessionsState === 'loaded' && authStore.errorMessage !== null" variant="error">
      {{ authStore.errorMessage }}
    </UiAlert>

    <UiLoadingRegion :state="authStore.sessionsState">
      <template #error>
        <UiAlert variant="error">{{ authStore.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState
        v-if="authStore.sessionsState === 'loaded' && authStore.sessions.length === 0"
        :title="t('app.sessions.emptyTitle')"
        :description="t('app.sessions.emptyDescription')"
      />

      <UiCard v-else>
        <UiTable
          :caption="t('app.sessions.tableCaption')"
          :columns="columns"
          :rows="authStore.sessions"
          row-key="id"
          :loading="authStore.sessionsState === 'loading'"
        >
          <template #cell-device="{ row }">
            <div class="flex items-center gap-3">
              <!-- Decorative: the device's name beside it is the cell's whole content. -->
              <UiIconTile name="globe" size="sm" />
              <div class="flex min-w-0 flex-col items-start gap-1">
                <span class="block max-w-65 min-w-35 truncate font-semibold">{{ row.userAgent }}</span>
                <UiBadge v-if="row.isCurrent" variant="success">
                  {{ t('app.sessions.currentDevice') }}
                </UiBadge>
              </div>
            </div>
          </template>
          <template #cell-address="{ row }">{{ row.ipAddress }}</template>
          <template #cell-signedIn="{ row }">{{ formatDate(row.issuedAt, localeStore.current) }}</template>
          <template #cell-actions="{ row }">
            <!-- One command today and a menu anyway, so the last column has one shape in every
                 table of the panel. The trigger's name carries the row's address, because
                 "Actions" repeated down a column names nothing to a screen reader. -->
            <UiDropdown
              :label="t('common.actions')"
              :aria-label="t('app.sessions.rowActions', { address: row.ipAddress })"
              align="end"
              variant="bare"
              :chevron="false"
            >
              <template #trigger>
                <UiIcon name="ellipsis" size="md" />
              </template>
              <UiDropdownItem destructive @select="askToRevoke(row.id)">
                {{ t('app.sessions.end') }}
              </UiDropdownItem>
            </UiDropdown>
          </template>
        </UiTable>
      </UiCard>
    </UiLoadingRegion>

    <!-- Outside the table on purpose: the branch above swaps the table for an alert or an empty
         state, and a dialog mounted inside it would be unmounted by the very refresh it survives.
         `:open` is bound to a value that really changes, so `UiModal`s open-watcher runs. -->
    <UiConfirm
      :open="pendingRevocation !== null"
      :title="confirmationTitle"
      :question="confirmationQuestion"
      :confirm-label="t('app.sessions.confirmEnd')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="revoking"
      :acting-label="t('app.sessions.working')"
      @close="cancelRevocation"
      @confirm="revoke"
    />
  </section>
</template>
