<script setup lang="ts">
/**
 * One remote server, in two panes: the connection (what it is, how to authorize the panel on it,
 * the host key step, the connection test) and the accounts discovery found on it. This page owns
 * the server's read, the pane, the host key review, the notices and the removal.
 *
 * **The order the administrator walks.** The panel's public key goes on the remote server; the host
 * key the panel scanned is confirmed in its own step (a state `hostKeyUnconfirmed` or
 * `hostKeyChanged` puts that step in front, and a pinned key can be reviewed against a fresh scan);
 * then the connection is tested; then accounts are discovered. The panel enforces the order: the
 * test and discovery are refused without a pinned key, whatever this page shows.
 *
 * A server another administrator removed, or one that never existed, is the not-found state, not
 * an alert. While the server loads, the heading and the overview are drawn already, with a
 * placeholder in place of each value. Renders a `<section>`, not a `<main>`.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import ConnectionTestResult from '../../components/migration/ConnectionTestResult.vue'
import HostKeyConfirm from '../../components/migration/HostKeyConfirm.vue'
import PublicKeyInstructions from '../../components/migration/PublicKeyInstructions.vue'
import RemoteServerOverview from '../../components/migration/RemoteServerOverview.vue'
import RemoveRemoteServerDialog from '../../components/migration/RemoveRemoteServerDialog.vue'
import RemoteServerAccountsTab from './RemoteServerAccountsTab.vue'
import { useRemoteServersStore } from '../../stores/remoteServers'
import { flashNoticeState, takeFlashNotice } from '../../utils/flashNotice'
import type { RemovedRemoteServer, RemoteServerDetail } from '../../types/migrationSource'

/** Which pane of the detail page is on screen. */
type DetailTab = 'connection' | 'accounts'

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The server's identity, from `/migration/remote-servers/:id`. */
  id: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = useRemoteServersStore()

/** The pane currently shown. */
const tab: Ref<DetailTab> = ref('connection')

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the administrator asked to review a pinned host key against a fresh scan. */
const reviewing: Ref<boolean> = ref(false)

/** Whether the removal confirmation is open. */
const confirmingRemove: Ref<boolean> = ref(false)

/** The key the panel answered with after removing the server, or `null` while none is being shown. */
const removed: Ref<RemovedRemoteServer | null> = ref(null)

/** The removed server's name, kept for the reminder's title and the notice after leaving. */
const removedName: Ref<string> = ref('')

/**
 * The server on screen once the panel has answered with it; `null` while loading and in every
 * failure. A server the store still holds from an earlier visit is not this one and is not drawn.
 */
const server: ComputedRef<RemoteServerDetail | null> = computed(() => {
  return store.current !== null && store.current.id === props.id ? store.current : null
})

/**
 * Whether the server does not exist: the read finished and found nothing. A server this page just
 * removed is not "missing" while its reminder is still on screen.
 */
const missing: ComputedRef<boolean> = computed(() => {
  return store.currentState === 'loaded' && store.current === null && removed.value === null
})

/** Whether the server's host key is pinned. */
const pinned: ComputedRef<boolean> = computed(() => {
  const fingerprint = server.value?.hostKeyFingerprint
  return fingerprint !== undefined && fingerprint !== null && fingerprint !== ''
})

/** Whether the state demands a host key decision, so the step cannot be put away. */
const needsConfirmation: ComputedRef<boolean> = computed(() => {
  return server.value?.state === 'hostKeyUnconfirmed' || server.value?.state === 'hostKeyChanged'
})

/** Whether the host key step is on screen. */
const showHostKeyStep: ComputedRef<boolean> = computed(() => {
  return needsConfirmation.value || reviewing.value
})

/** The panes, in the order the administrator walks them. */
const tabOptions: ComputedRef<SegmentOption[]> = computed(() => {
  return [
    { value: 'connection', label: t('migration.detail.tabs.connection') },
    { value: 'accounts', label: t('migration.detail.tabs.accounts') },
  ]
})

/**
 * Switches pane.
 * @param value The tab the control reported.
 * @returns Nothing.
 */
const onTabChange = (value: string): void => {
  tab.value = value as DetailTab
}

/**
 * Pins the chosen host key. The panel compares the fingerprint with its scanned candidates itself.
 * @param fingerprint The fingerprint the administrator chose and retyped.
 * @returns Resolves once the request has settled.
 */
const confirmKey = async (fingerprint: string): Promise<void> => {
  if (await store.confirmHostKey(props.id, fingerprint)) {
    reviewing.value = false
    notice.value = t('migration.hostKey.confirmedNotice')
  }
}

/**
 * Scans the host keys again from the step itself.
 * @returns Resolves once the request has settled.
 */
const rescanKeys = async (): Promise<void> => {
  await store.rescanHostKeys(props.id)
}

/**
 * Scans the host keys again and puts the step in front, so a pinned key can be compared with what
 * the server presents now.
 * @returns Resolves once the request has settled.
 */
const reviewKey = async (): Promise<void> => {
  if (await store.rescanHostKeys(props.id)) {
    reviewing.value = true
  }
}

/**
 * Puts the host key step away and keeps the pinned key.
 * @returns Nothing.
 */
const keepKey = (): void => {
  reviewing.value = false
}

/**
 * Tests the connection with the pinned key; the outcome is on the server the store holds.
 * @returns Resolves once the request has settled.
 */
const testConnection = async (): Promise<void> => {
  await store.test(props.id)
}

/**
 * Opens the removal confirmation.
 * @returns Nothing.
 */
const askRemove = (): void => {
  confirmingRemove.value = true
}

/**
 * Abandons the removal confirmation.
 * @returns Nothing.
 */
const cancelRemove = (): void => {
  confirmingRemove.value = false
}

/**
 * Removes the server. The reminder to take the key off the remote server opens when the panel has
 * answered with it; a refusal stays above the page in the panel's own words.
 * @returns Resolves once the request has settled.
 */
const confirmRemove = async (): Promise<void> => {
  const name = server.value?.name ?? ''
  const answer = await store.remove(props.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  confirmingRemove.value = false
  if (answer !== null) {
    removedName.value = name
    removed.value = answer
  }
}

/**
 * Closes the reminder and leaves for the list, which no longer holds the server.
 * @returns Resolves once navigation has been dispatched.
 */
const finishRemoval = async (): Promise<void> => {
  removed.value = null
  await router.push({ name: 'remote-servers', state: flashNoticeState(t('migration.list.removed', { name: removedName.value })) })
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(async () => {
  store.clearErrors()
  notice.value = takeFlashNotice()
  await store.loadOne(props.id)
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="store.currentState">
      <!-- A missing server is the not-found state, never the alert: the panel answered, and what it
           said is that no such server exists. -->
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState
        v-if="missing"
        :title="t('migration.detail.notFoundTitle')"
        :description="t('migration.detail.notFoundDescription')"
      >
        <UiLink :to="{ name: 'remote-servers' }" icon="chevronLeft">{{ t('migration.detail.backToList') }}</UiLink>
      </UiEmptyState>

      <div v-else class="flex min-w-0 flex-col gap-4">
        <UiPageHeading
          :title="server?.name ?? ''"
          :subtitle="t('migration.detail.subtitle')"
          :loading="server === null"
        >
          <template #actions>
            <UiLink :to="{ name: 'remote-servers' }" icon="chevronLeft">{{ t('migration.detail.backToList') }}</UiLink>
            <template v-if="server !== null">
              <UiButton variant="secondary" :to="{ name: 'remote-server-edit', params: { id } }">
                {{ t('migration.detail.editAction') }}
              </UiButton>
              <UiButton variant="destructive" @click="askRemove">{{ t('migration.detail.removeAction') }}</UiButton>
            </template>
          </template>
        </UiPageHeading>

        <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

        <UiSegmentedControl
          :model-value="tab"
          :options="tabOptions"
          :label="t('migration.detail.tabs.label')"
          @update:model-value="onTabChange"
        />

        <template v-if="server !== null && tab === 'connection'">
          <UiCard>
            <RemoteServerOverview :server="server" :reviewing="showHostKeyStep" :busy="store.busy" @review="reviewKey" />
          </UiCard>
          <UiCard>
            <PublicKeyInstructions :authorized-keys-line="server.authorizedKeysLine" :allowed-from="server.allowedFrom" />
          </UiCard>
          <UiCard v-if="showHostKeyStep">
            <HostKeyConfirm
              :candidates="server.hostKeyCandidates"
              :changed="server.state === 'hostKeyChanged'"
              :cancellable="!needsConfirmation"
              :busy="store.busy"
              @confirm="confirmKey"
              @rescan="rescanKeys"
              @cancel="keepKey"
            />
          </UiCard>
          <UiCard>
            <ConnectionTestResult :server="server" :pinned="pinned" :busy="store.busy" @test="testConnection" />
          </UiCard>
        </template>

        <RemoteServerAccountsTab v-else-if="server !== null" :server="server" />
      </div>
    </UiLoadingRegion>

    <RemoveRemoteServerDialog
      :pending="confirmingRemove ? { id, name: server?.name ?? '' } : null"
      :acting="store.busy === 'remove'"
      :removed="removed"
      :removed-name="removedName"
      @confirm="confirmRemove"
      @cancel="cancelRemove"
      @done="finishRemoval"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
