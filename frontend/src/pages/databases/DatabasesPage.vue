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
 * **The list is one server page at a time** (`useServerTable`): page, page size, search, sort and the
 * `account` filter all live in the URL. `?account=<id>` is the link the account page opens; it is
 * read through `useAccountFilter().spec`, so a customer's list ignores it exactly as every other
 * account-filtered page does, and the filter is not offered to them: their list is already their
 * own. Nothing is filtered here — the panel searches the full name, sorts and counts.
 *
 * **Two kinds of nothing.** No databases at all is the empty state. A search or filter matching
 * none is the no-results state, with the way back to every database.
 *
 * **The password leaves with the page.** `dismissCredential` runs on unmount as well as on the
 * dialog's close, so a navigation ends the one showing; nothing is written to storage, so a reload
 * ends it too.
 */
import { computed, onBeforeUnmount, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterSheet from '../../components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import DatabaseCreateForm from '../../components/databases/DatabaseCreateForm.vue'
import DatabaseCreatedDialog from '../../components/databases/DatabaseCreatedDialog.vue'
import DatabaseActionConfirm from '../../components/databases/DatabaseActionConfirm.vue'
import DatabasesTable from '../../components/databases/DatabasesTable.vue'
import { useAccountChoice, type AccountPickerState } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useDatabasesStore } from '../../stores/databases'
import type { CreateDatabaseRequest, Database, DatabaseAction } from '../../types/database'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const store = useDatabasesStore()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()
const { isAdmin, isCustomer } = useCallerRole()
const labels = useListLabels()

const table = useServerTable<Database>({
  filters: { account: accountSpec },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<Database>> => {
    return store.list(request)
  },
})

/** The create form, so an accepted create can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The success toast's text after a create, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** The database whose action is awaiting confirmation, or `null` when none is. */
const pending: Ref<Database | null> = ref(null)

/** Which action that database is waiting on. */
const pendingAction: Ref<DatabaseAction> = ref('drop')

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** Whether the list is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * The account filter's picker: the page of accounts matching its search, after an "any account"
 * choice, which is counted into the total so the "more matches" line stays right. Administrators only.
 */
const accountFilterPicker: ComputedRef<AccountPickerState> = computed(() => {
  const state = accountPicker.value
  return {
    ...state,
    options: [{ value: '', label: t('common.list.any') }, ...state.options],
    total: state.total + 1,
  }
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  return table.filters.value.account !== ''
    ? [labels.chip('account', t('databases.list.filters.account'), accountFilterName.value)]
    : []
})

/**
 * Opens the filter sheet.
 * @returns Nothing.
 */
const openFilters = (): void => {
  filtersOpen.value = true
}

/**
 * Closes the filter sheet.
 * @returns Nothing.
 */
const closeFilters = (): void => {
  filtersOpen.value = false
}

/**
 * Clears the search and every filter, the way back from a question that matched nothing.
 * @returns Resolves once the filters left the URL; the search follows after the box's pause.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
}

/**
 * Sends a request the form has already validated. On success the store opens the credential dialog,
 * the form is emptied and the list page is read again, since where the new row falls is the
 * server's to say; on failure the store holds the server's own message and the template renders it
 * verbatim.
 * @param request The account and the two names the form collected.
 * @returns Resolves once the attempt has settled.
 */
const create = async (request: CreateDatabaseRequest): Promise<void> => {
  if (await store.create(request)) {
    notice.value = t('databases.list.created')
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Starts an action on one row, which then waits for confirmation.
 * @param database The database the operator acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (database: Database, action: DatabaseAction): void => {
  pending.value = database
  pendingAction.value = action
}

/**
 * Abandons a pending action.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed action.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  if (pending.value === null) {
    return
  }
  const id = pending.value.id
  const action = pendingAction.value

  const dropped = action === 'drop' ? await store.remove(id) : false
  if (action === 'resetPassword') {
    await store.resetPassword(id)
  }

  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the operator the panel is working.
  pending.value = null

  // A drop changes the total and pulls rows up from the next page, so the page is read again.
  if (dropped) {
    await table.reload()
  }
}

/**
 * Forgets the credential on screen, ending the only showing it gets.
 * @returns Nothing.
 */
const dismissCredential = (): void => {
  store.dismissCredential()
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

// The credential must not survive a navigation. The store outlives this page, so leaving the
// screen without this would leave the password in memory for the next visit to render.
onBeforeUnmount(dismissCredential)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :title="t('databases.list.heading')"
      :subtitle="t('databases.list.subtitle')"
      :note="t('databases.list.prefixNote')"
    >
      <template #actions>
        <!-- The grant repair is administrators-only. Hiding the link from a customer is presentation,
             not the gate: the endpoint behind it refuses a customer on its own (rules/security.md). -->
        <UiButton v-if="isAdmin" variant="secondary" :to="{ name: 'database-grant-repair' }">{{ t('databases.grantRepair.heading') }}</UiButton>
      </template>
    </UiPageHeading>

    <!-- A rejected create is the one thing that needs attention, so it comes before the form it
         belongs to. It is the server's own message, rendered as it was sent. -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">
      {{ store.createErrorMessage }}
    </UiAlert>

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">
      {{ store.actionErrorMessage }}
    </UiAlert>

    <DatabaseCreateForm
      ref="form"
      :account-picker="accountPicker"
      :fixed-account-id="fixedAccountId"
      :submitting="store.creating"
      @search-accounts="searchAccounts"
      @submit="create"
    />

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('databases.list.sectionTitle')" />

      <div class="flex flex-col gap-4">
        <!-- No search and no filter has anything to narrow before the first database exists. -->
        <div v-if="!answeredNothing || isNarrowed" class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('databases.list.searchLabel')"
            :placeholder="t('databases.list.searchPlaceholder')"
            :clear-label="t('databases.list.searchClear')"
          />
          <!-- The account is the list's one filter, and a customer's list is already their own
               account's: there is nothing to narrow to, so the trigger is not offered. -->
          <UiFilterTrigger
            v-if="!isCustomer"
            :label="t('common.list.filters')"
            :active-count="table.activeFilterCount.value"
            :active-label="t('common.list.filtersApplied', { n: table.activeFilterCount.value }, table.activeFilterCount.value)"
            @click="openFilters"
          />
        </div>

        <UiActiveFilter
          v-if="chips.length > 0"
          :chips="chips"
          :clear-label="t('common.list.clearAll')"
          @remove="table.clearFilter"
          @clear="table.clearFilters"
        />

        <UiLoadingRegion :state="table.loadingState.value">
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing && !isNarrowed"
            :title="t('databases.list.emptyTitle')"
            :description="t('databases.list.emptyDescription')"
          >
            <template #icon><UiIcon name="database" size="lg" /></template>
          </UiEmptyState>

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('databases.list.noResultsTitle')"
            :description="t('databases.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <DatabasesTable
              :rows="store.databases"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @act="ask"
            />

            <UiPagination
              v-if="table.state.value.status === 'loaded'"
              :page="table.page.value"
              :page-size="table.pageSize.value"
              :total="table.total.value"
              :page-sizes="table.pageSizes"
              :labels="labels.pagination.value"
              @update:page="table.setPage"
              @update:page-size="table.setPageSize"
            />
          </div>
        </UiLoadingRegion>
      </div>
    </UiCard>

    <UiFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      :title="t('common.list.filters')"
      :close-label="t('common.close')"
      :apply-label="t('common.list.apply')"
      :reset-label="t('common.list.reset')"
      @update:model-value="table.setFilters"
      @close="closeFilters"
    >
      <template #default="{ draft }">
        <UiCombobox
          v-model="draft.account"
          v-bind="accountFilterPicker"
          :label="t('databases.list.filters.account')"
          :placeholder="t('common.list.any')"
          :selected-label="accountFilterName"
          @search="searchAccounts"
        />
      </template>
    </UiFilterSheet>

    <DatabaseActionConfirm
      :database="pending"
      :action="pendingAction"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirm"
    />

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
    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
