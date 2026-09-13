<script setup lang="ts">
/**
 * The caller's signed-in devices, with a way to end any of them. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout
 * this page is nested under.
 *
 * Every row is the caller's own: the endpoint takes no user parameter, and a
 * session belonging to somebody else answers "not found" rather than "forbidden",
 * so this screen cannot be pointed at another person's devices.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import { useAuthStore } from '../../stores/auth'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const router = useRouter()
const authStore = useAuthStore()
const localeStore = useLocaleStore()

/** The session the user has asked to end and is being asked to confirm, or `null`. */
const pendingRevocation: Ref<string | null> = ref(null)

/**
 * Whether the confirmed revocation is with the server.
 *
 * The page's own flag rather than the store's `loading`: `revokeSession` sets no loading state —
 * it must not, because `loading` swaps this whole table for a spinner — and the dialog needs the
 * one thing that IS in flight.
 */
const revoking: Ref<boolean> = ref(false)

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
  <section class="w-full">
    <UiPageHeading
      class="mb-4"
      :title="t('app.sessions.heading')"
      :subtitle="t('app.sessions.subtitle')"
    >
      <template #actions>
        <UiButton
          v-if="authStore.sessions.length > 0"
          variant="destructive"
          @click="signOutEverywhere"
        >
          {{ t('app.sessions.signOutEverywhere') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <UiSpinner v-if="authStore.loading" :label="t('app.sessions.loading')" />

    <UiAlert v-else-if="authStore.errorMessage !== null" variant="error">
      {{ authStore.errorMessage }}
    </UiAlert>

    <UiEmptyState
      v-else-if="authStore.sessions.length === 0"
      :title="t('app.sessions.emptyTitle')"
      :description="t('app.sessions.emptyDescription')"
    />

    <UiTable v-else :caption="t('app.sessions.tableCaption')">
      <template #head>
        <UiTableRow>
          <UiTableHeaderCell>{{ t('app.sessions.deviceColumn') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('app.sessions.addressColumn') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('app.sessions.signedInColumn') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('common.actions') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>

      <UiTableRow v-for="session in authStore.sessions" :key="session.id">
        <UiTableCell>
          <span class="block max-w-[320px] truncate">{{ session.userAgent }}</span>
          <UiBadge v-if="session.isCurrent" variant="success">
            {{ t('app.sessions.currentDevice') }}
          </UiBadge>
        </UiTableCell>
        <UiTableCell>
          <span class="font-mono">{{ session.ipAddress }}</span>
        </UiTableCell>
        <UiTableCell>{{ formatDate(session.issuedAt, localeStore.current) }}</UiTableCell>
        <UiTableCell>
          <!-- One command today and a menu anyway, so the last column has one shape in every
               table of the panel and an operator learns the control once. The trigger's name
               carries the row's address, because "Actions" repeated down a column of identical
               triggers names nothing to a screen reader. -->
          <UiDropdown
            :label="t('common.actions')"
            :aria-label="t('app.sessions.rowActions', { address: session.ipAddress })"
            align="start"
            variant="bare"
            :chevron="false"
          >
            <template #trigger>
              <UiIcon name="ellipsis" size="md" />
            </template>
            <UiDropdownItem destructive @select="askToRevoke(session.id)">
              {{ t('app.sessions.end') }}
            </UiDropdownItem>
          </UiDropdown>
        </UiTableCell>
      </UiTableRow>
    </UiTable>

    <!-- Outside the table on purpose: the branch above swaps the table for a spinner or an empty
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
