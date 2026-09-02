<script setup lang="ts">
/**
 * Databases screen: the create form, the list, and the one-time credential dialog those two paths
 * both end in. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the
 * layout this page is nested under. State comes exclusively from the databases store; the page
 * never touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * One page rather than a list plus a form page, because a database has no detail to open: the row
 * IS the database, and its two actions live on it. The form itself is `DatabaseCreateForm`, which
 * owns its fields and the whole client-side validation mirror — this page only forwards the
 * request it emits to the store.
 *
 * **The list shows the prefixed name and nothing else.** MySQL holds `<account>_<name>`, and an
 * operator who reads `shop` here and types `shop` into a mysql client is told the database does
 * not exist. So the short suffix the customer typed is not a column, the prefixed form is, and a
 * line under the heading says whose prefix it is. The prefixed value is the server's — this page
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
import DatabaseCreateForm from '../../components/databases/DatabaseCreateForm.vue'
import DatabaseCreatedDialog from '../../components/databases/DatabaseCreatedDialog.vue'
import { useAccountsStore } from '../../stores/accounts'
import { useDatabasesStore } from '../../stores/databases'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { CreateDatabaseRequest } from '../../types/database'

/** Which action a row is waiting for confirmation on. */
type PendingAction = 'resetPassword' | 'drop'

const { t } = useI18n()
const store = useDatabasesStore()
const accountsStore = useAccountsStore()
const localeStore = useLocaleStore()

/** The create form, so an accepted create can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The database whose action is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/** Which action that database is waiting on. */
const pendingAction: Ref<PendingAction> = ref('drop')

/** Whether the panel answered successfully and reported no databases at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.databases.length === 0
})

/** The sentence a row shows while its action awaits confirmation. */
const confirmationText: ComputedRef<string> = computed(() => {
  return pendingAction.value === 'drop'
    ? t('databases.list.confirmDrop')
    : t('databases.list.confirmResetPassword')
})

/**
 * Names the account a database belongs to, for a column that would otherwise print a GUID.
 * @param id The owning account's identity, as the database row reports it.
 * @returns The account's own short name, or a placeholder when the accounts list has none.
 */
const accountName = (id: string): string => {
  const owner = accountsStore.accounts.find((account) => {
    return account.id === id
  })
  return owner?.name ?? t('databases.list.unknownAccount')
}

/**
 * Loads the two lists the screen needs: the databases themselves and the accounts one can be
 * created for. Neither is written into the SPA.
 * @returns Resolves once both requests have settled.
 */
const refresh = async (): Promise<void> => {
  await Promise.all([store.load(), accountsStore.load()])
}

/**
 * Sends a request the form has already validated. On success the store opens the credential dialog
 * and the form is emptied; on failure the store holds the server's own message and the template
 * renders it verbatim.
 * @param request The account and the two names the form collected.
 * @returns Resolves once the attempt has settled.
 */
const create = async (request: CreateDatabaseRequest): Promise<void> => {
  if (await store.create(request)) {
    form.value?.reset()
  }
}

/**
 * Starts an action on one row, which then waits for confirmation.
 * @param id The database the operator acted on.
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

  if (action === 'drop') {
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
        {{ t('databases.list.heading') }}
      </h1>
      <p class="mt-1 text-base text-text-secondary">{{ t('databases.list.subtitle') }}</p>
      <p class="mt-1 text-sm text-text-muted">{{ t('databases.list.prefixNote') }}</p>
    </div>

    <UiAlert v-if="store.createErrorMessage !== null" variant="error" class="mb-4">
      {{ store.createErrorMessage }}
    </UiAlert>

    <DatabaseCreateForm
      ref="form"
      class="mb-6"
      :accounts="accountsStore.accounts"
      :submitting="store.creating"
      @submit="create"
    />

    <UiSpinner v-if="store.loading" :label="t('databases.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else-if="isEmpty"
      :title="t('databases.list.emptyTitle')"
      :description="t('databases.list.emptyDescription')"
    >
      <template #icon><UiIcon name="database" size="lg" /></template>
    </UiEmptyState>

    <UiTable v-else-if="store.databases.length > 0" :caption="t('databases.list.tableCaption')">
      <template #head>
        <UiTableRow>
          <UiTableHeaderCell>{{ t('databases.list.columns.fullName') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('databases.list.columns.dbUserName') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('databases.list.columns.account') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('databases.list.columns.createdAt') }}</UiTableHeaderCell>
          <UiTableHeaderCell>{{ t('databases.list.columns.actions') }}</UiTableHeaderCell>
        </UiTableRow>
      </template>
      <UiTableRow v-for="database in store.databases" :key="database.id">
        <!-- The prefixed name, because it is the one that works in a mysql client. -->
        <UiTableCell class="font-mono font-medium">{{ database.fullName }}</UiTableCell>
        <UiTableCell class="font-mono text-text-secondary">{{ database.dbUserName }}</UiTableCell>
        <UiTableCell class="text-text-secondary">{{ accountName(database.accountId) }}</UiTableCell>
        <UiTableCell class="font-mono text-text-muted">
          {{ formatDate(database.createdAt, localeStore.current) }}
        </UiTableCell>
        <UiTableCell>
          <div class="flex flex-wrap items-center justify-end gap-2">
            <!-- The confirmation stays INLINE rather than becoming two more menu
                 items. A menu is a list of things one might do; a confirmation is
                 a question already asked, and burying the answer behind a second
                 press of the same trigger would hide the state the row is in. -->
            <template v-if="pendingId === database.id">
              <span class="text-sm text-text-secondary">{{ confirmationText }}</span>
              <UiButton variant="destructive" :disabled="store.acting" @click="confirm">
                {{ store.acting ? t('databases.list.working') : t('databases.list.confirm') }}
              </UiButton>
              <UiButton variant="secondary" @click="cancel">{{ t('databases.list.cancel') }}</UiButton>
            </template>
            <!-- One trigger instead of a button per command: the row's actions grow
                 with the module, and a row that grows a button every time is a row
                 that stops fitting on a narrow screen. `align="end"` because this is
                 the last column — a menu aligned to the start would open off the
                 right edge. -->
            <UiDropdown
              v-else
              :label="t('databases.list.columns.actions')"
              :aria-label="t('databases.list.rowActions', { name: database.fullName })"
              align="end"
              variant="bare"
              :chevron="false"
            >
              <template #trigger>
                <UiIcon name="ellipsis" size="md" />
              </template>
              <UiDropdownItem @select="ask(database.id, 'resetPassword')">
                {{ t('databases.list.resetPassword') }}
              </UiDropdownItem>
              <UiDropdownItem destructive @select="ask(database.id, 'drop')">
                {{ t('databases.list.drop') }}
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
    <DatabaseCreatedDialog
      :open="store.revealedCredential !== null"
      :database-full-name="store.revealedCredential?.databaseFullName ?? null"
      :db-user-name="store.revealedCredential?.dbUserName ?? ''"
      :password="store.revealedCredential?.password ?? ''"
      @close="dismissCredential"
    />
  </section>
</template>
