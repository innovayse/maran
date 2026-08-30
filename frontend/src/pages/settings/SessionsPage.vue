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
import { onMounted, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
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
 * Ends one session. Ending the current one signs this browser out, so the page
 * leaves for the sign-in screen rather than staying on a list it may no longer read.
 * @param id The session to end.
 * @param isCurrent Whether it is the device making this request.
 * @returns Resolves once the request has settled.
 */
const revoke = async (id: string, isCurrent: boolean): Promise<void> => {
  pendingRevocation.value = null

  if ((await authStore.revokeSession(id)) && isCurrent) {
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
    <div class="mb-4 flex flex-wrap items-end justify-between gap-4">
      <div>
        <h1 class="text-3xl font-semibold tracking-title text-text-primary">
          {{ t('app.sessions.heading') }}
        </h1>
        <p class="mt-1 text-base text-text-secondary">{{ t('app.sessions.subtitle') }}</p>
      </div>
      <UiButton v-if="authStore.sessions.length > 0" variant="destructive" @click="signOutEverywhere">
        {{ t('app.sessions.signOutEverywhere') }}
      </UiButton>
    </div>

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
          <UiTableHeaderCell>{{ t('app.sessions.actionsColumn') }}</UiTableHeaderCell>
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
          <template v-if="pendingRevocation === session.id">
            <span class="mr-2 text-base text-text-secondary">
              {{ session.isCurrent ? t('app.sessions.confirmCurrent') : t('app.sessions.confirm') }}
            </span>
            <UiButton variant="destructive" @click="revoke(session.id, session.isCurrent)">
              {{ t('app.sessions.confirmEnd') }}
            </UiButton>
            <UiButton variant="secondary" @click="cancelRevocation">
              {{ t('app.sessions.cancel') }}
            </UiButton>
          </template>
          <UiButton v-else variant="secondary" @click="askToRevoke(session.id)">
            {{ t('app.sessions.end') }}
          </UiButton>
        </UiTableCell>
      </UiTableRow>
    </UiTable>
  </section>
</template>
