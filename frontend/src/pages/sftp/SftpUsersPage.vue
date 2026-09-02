<script setup lang="ts">
/**
 * SFTP logins screen: the create form, the list, and the one-time credential dialog those two
 * paths both end in. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in
 * the layout this page is nested under. State comes exclusively from the SFTP store; the page
 * never touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * One page rather than a list plus a form page, because a login has no detail to open: the row IS
 * the login, and its two actions live on it. The form itself is `SftpUserCreateForm`, which owns
 * its fields and the whole client-side validation mirror — this page only forwards the request it
 * emits to the store.
 *
 * **The list shows the prefixed login and nothing else.** The host holds `<account>_<name>` in
 * `/etc/passwd`, and somebody who reads `web` here and types `web` into an SFTP client simply
 * cannot log in. So the short suffix the customer typed is not a column, the prefixed form is, and
 * a line under the heading says whose prefix it is. The prefixed value is the server's — this page
 * never assembles one from an account name and a suffix.
 *
 * **The password leaves with the page.** `dismissCredential` runs on unmount as well as on the
 * dialog's close, so a navigation ends the one showing; nothing is written to storage, so a reload
 * ends it too.
 */
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import SftpUserCreateForm from '../../components/sftp/SftpUserCreateForm.vue'
import SftpUserCreatedDialog from '../../components/sftp/SftpUserCreatedDialog.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useLocaleStore } from '../../stores/locale'
import { useSftpStore } from '../../stores/sftp'
import { formatDate } from '../../utils/formatDate'
import type { CreateSftpUserRequest } from '../../types/sftpUser'

/** Which action a row is waiting for confirmation on. */
type PendingAction = 'resetPassword' | 'remove'

const { t } = useI18n()
const store = useSftpStore()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** The create form, so an accepted create can empty the field it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The login whose action is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/** Which action that login is waiting on. */
const pendingAction: Ref<PendingAction> = ref('remove')

/** Whether the panel answered successfully and reported no logins at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.sftpUsers.length === 0
})

/** The sentence a row shows while its action awaits confirmation. */
const confirmationText: ComputedRef<string> = computed(() => {
  return pendingAction.value === 'remove'
    ? t('sftp.list.confirmRemove')
    : t('sftp.list.confirmResetPassword')
})

/**
 * Names the account a login belongs to, for a column that would otherwise print a GUID.
 * @param id The owning account's identity, as the login row reports it.
 * @returns The account's own short name, or a placeholder when the accounts list has none.
 */
const accountName = (id: string): string => {
  const owner = accountsStore.accounts.find((account) => {
    return account.id === id
  })
  return owner?.name ?? t('sftp.list.unknownAccount')
}

/**
 * Loads the two lists the screen needs: the logins themselves and the accounts one can be created
 * for. Neither is written into the SPA.
 * @returns Resolves once both requests have settled.
 */
const refresh = async (): Promise<void> => {
  await Promise.all([store.load(), accountsStore.load()])
}

/**
 * Sends a request the form has already validated. On success the store opens the credential dialog
 * and the form is emptied; on failure the store holds the server's own message and the template
 * renders it verbatim.
 * @param request The account and the name the form collected.
 * @returns Resolves once the attempt has settled.
 */
const create = async (request: CreateSftpUserRequest): Promise<void> => {
  if (await store.create(request)) {
    form.value?.reset()
  }
}

/**
 * Starts an action on one row, which then waits for confirmation.
 * @param id The login the operator acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (id: string, action: PendingAction): void => {
  pendingId.value = id
  pendingAction.value = action
}

/**
 * Abandons a pending action.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingId.value = ''
}

/**
 * Carries out the confirmed action.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const id = pendingId.value
  const action = pendingAction.value
  pendingId.value = ''

  if (action === 'remove') {
    await store.remove(id)
    return
  }
  await store.resetPassword(id)
}

/**
 * Forgets the credential on screen, ending the only showing it gets.
 * @returns Nothing.
 */
const dismissCredential = (): void => {
  store.dismissCredential()
}

onMounted(refresh)

// The credential must not survive a navigation. The store outlives this page, so leaving the
// screen without this would leave the password in memory for the next visit to render.
onBeforeUnmount(dismissCredential)
</script>

<template>
  <section class="w-full">
    <div class="mb-4">
      <h1 class="text-3xl font-semibold tracking-title text-text-primary">
        {{ t('sftp.list.heading') }}
      </h1>
      <p class="mt-1 text-base text-text-secondary">{{ t('sftp.list.subtitle') }}</p>
      <p class="mt-1 text-sm text-text-muted">{{ t('sftp.list.prefixNote') }}</p>
    </div>

    <UiAlert v-if="store.createErrorMessage !== null" variant="error" class="mb-4">
      {{ store.createErrorMessage }}
    </UiAlert>

    <SftpUserCreateForm
      ref="form"
      class="mb-6"
      :accounts="accountsStore.accounts"
      :submitting="store.creating"
      @submit="create"
    />

    <UiSpinner v-if="store.loading" :label="t('sftp.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else-if="isEmpty"
      :title="t('sftp.list.emptyTitle')"
      :description="t('sftp.list.emptyDescription')"
    >
      <template #icon><UiIcon name="folderKey" size="lg" /></template>
    </UiEmptyState>

    <UiTable v-else-if="store.sftpUsers.length > 0" :caption="t('sftp.list.tableCaption')">
      <template #head>
        <UiTableRow>
          <UiTableHeaderCell>{{ t('sftp.list.columns.fullName') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('sftp.list.columns.account') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('sftp.list.columns.createdAt') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('sftp.list.columns.actions') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="user in store.sftpUsers" :key="user.id">
        <!-- The prefixed login, because it is the one an SFTP client accepts. -->
        <UiTableCell class="font-mono font-medium">{{ user.fullName }}</UiTableCell>
        <UiTableCell class="text-text-secondary">{{ accountName(user.accountId) }}</UiTableCell>
        <UiTableCell class="font-mono text-text-muted">
          {{ formatDate(user.createdAt, localeStore.current) }}
        </UiTableCell>
        <UiTableCell>
          <div class="flex flex-wrap items-center justify-end gap-2">
            <!-- The confirmation stays INLINE rather than becoming two more menu
                 items. A menu is a list of things one might do; a confirmation is
                 a question already asked, and burying the answer behind a second
                 press of the same trigger would hide the state the row is in. -->
            <template v-if="pendingId === user.id">
              <span class="text-sm text-text-secondary">{{ confirmationText }}</span>
              <UiButton variant="destructive" :disabled="store.acting" @click="confirm">
                {{ store.acting ? t('sftp.list.working') : t('sftp.list.confirm') }}
              </UiButton>
              <UiButton variant="secondary" @click="cancel">{{ t('sftp.list.cancel') }}</UiButton>
            </template>
            <!-- One trigger instead of a button per command: the row's actions grow
                 with the module, and a row that grows a button every time is a row
                 that stops fitting on a narrow screen. `align="end"` because this is
                 the last column — a menu aligned to the start would open off the
                 right edge. -->
            <UiDropdown
              v-else
              :label="t('sftp.list.columns.actions')"
              :aria-label="t('sftp.list.rowActions', { name: user.fullName })"
              align="end"
              variant="bare"
              :chevron="false"
            >
              <template #trigger>
                <UiIcon name="ellipsis" size="md" />
              </template>
              <UiDropdownItem @select="ask(user.id, 'resetPassword')">
                {{ t('sftp.list.resetPassword') }}
              </UiDropdownItem>
              <UiDropdownItem destructive @select="ask(user.id, 'remove')">
                {{ t('sftp.list.remove') }}
              </UiDropdownItem>
            </UiDropdown>
          </div>
        </UiTableCell>
      </UiTableRow>
    </UiTable>

    <!-- `:open` is bound to a value that really changes, and the dialog is NOT wrapped in a
         `v-if`. With `v-if` plus a literal `:open="true"` the component was created with the prop
         already true, so `UiModal`s open-watcher never ran: focus never entered the dialog, the
         focus trap sat inert, Escape never reached the panel, and focus was never restored on
         close. It looked correct on screen and was unusable from the keyboard. `UiModal` renders
         nothing of its own while closed, so there is no cost to leaving it mounted. -->
    <SftpUserCreatedDialog
      :open="store.revealedCredential !== null"
      :full-name="store.revealedCredential?.fullName ?? ''"
      :password="store.revealedCredential?.password ?? ''"
      @close="dismissCredential"
    />
  </section>
</template>
