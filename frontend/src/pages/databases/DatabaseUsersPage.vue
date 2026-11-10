<script setup lang="ts">
/**
 * Database users screen: the create form, the list of local MySQL users, the privileges editor and
 * the one-time credential dialog. Renders a `<section>`, not a `<main>`. State comes exclusively from
 * the database-users store; the page never touches the API layer (rules/vue.md).
 *
 * A user reaches the server from the server itself only: there is no remote access here. What a user
 * may do is set per database in the privileges editor, from a closed set of privileges the panel
 * serves. A database's dedicated user appears in the list too, can have its privileges edited and its
 * password reset, but is dropped only with its database.
 *
 * **The list is one server page at a time** (`useServerTable`): page, page size, search, sort and the
 * `account` filter live in the URL, and nothing is filtered here. The filter sheet is not offered to a
 * customer, whose list is already their own account's.
 *
 * **The password leaves with the page.** `dismissCredential` runs on unmount as well as on the
 * dialog's close, and the editor is closed on unmount likewise.
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
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import DatabaseCreatedDialog from '../../components/databases/DatabaseCreatedDialog.vue'
import DatabaseUserActionConfirm from '../../components/databases/DatabaseUserActionConfirm.vue'
import DatabaseUserCreateForm from '../../components/databases/DatabaseUserCreateForm.vue'
import DatabaseUserPrivilegesDialog from '../../components/databases/DatabaseUserPrivilegesDialog.vue'
import DatabaseUsersTable from '../../components/databases/DatabaseUsersTable.vue'
import { useAccountChoice, type AccountPickerState } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useDatabaseUsersStore } from '../../stores/databaseUsers'
import type {
  CreateDatabaseUserRequest,
  DatabasePrivilegeKey,
  DatabaseUser,
  DatabaseUserAction,
  PendingDatabaseUserAction,
} from '../../types/databaseUser'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const store = useDatabaseUsersStore()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()
const { isCustomer } = useCallerRole()
const labels = useListLabels()

const table = useServerTable<DatabaseUser>({
  filters: { account: accountSpec },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<DatabaseUser>> => {
    return store.list(request)
  },
})

/** The create form, so an accepted create can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The success toast's text after a create or a privilege change, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** The user and the action awaiting confirmation, or `null` when none is. */
const pending: Ref<PendingDatabaseUserAction | null> = ref(null)

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

/** The account filter's picker, after an "any account" choice. Administrators only. */
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
    ? [labels.chip('account', t('databases.users.list.filters.account'), accountFilterName.value)]
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
 * @returns Resolves once the filters left the URL.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
}

/**
 * Sends a request the form has already validated; on success the store opens the credential dialog,
 * the form is emptied and the list page is read again.
 * @param request The account and the name the form collected.
 * @returns Resolves once the attempt has settled.
 */
const create = async (request: CreateDatabaseUserRequest): Promise<void> => {
  if (await store.create(request)) {
    notice.value = t('databases.users.list.created')
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Starts a confirmable action on one row.
 * @param user The user the operator acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (user: DatabaseUser, action: DatabaseUserAction): void => {
  pending.value = { user, action }
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
  const { user, action } = pending.value
  let dropped = false
  if (action === 'drop') {
    dropped = await store.remove(user.id)
  } else {
    await store.resetPassword(user.id)
  }
  // Closed after the request settles: the dialog holds the spinner that says the panel is working.
  pending.value = null
  // A drop changes the total and pulls rows up from the next page, so the page is read again.
  if (dropped) {
    await table.reload()
  }
}

/**
 * Opens the privileges editor on one row.
 * @param user The user to edit.
 * @returns Resolves once the editor's data has been read.
 */
const editPrivileges = async (user: DatabaseUser): Promise<void> => {
  await store.openEditor(user.id)
}

/**
 * Saves one database's privilege set and refreshes the list, whose grant counts it changes.
 * @param databaseId The database the privileges are on.
 * @param privileges The complete set; empty removes the grant.
 * @returns Resolves once the request has settled.
 */
const savePrivileges = async (databaseId: string, privileges: DatabasePrivilegeKey[]): Promise<void> => {
  if (await store.savePrivileges(databaseId, privileges)) {
    notice.value = t('databases.users.privileges.saved')
    await table.reload()
  }
}

/**
 * Closes the privileges editor.
 * @returns Nothing.
 */
const closeEditor = (): void => {
  store.closeEditor()
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

// The credential and the editor must not survive a navigation: the store outlives this page.
onBeforeUnmount(() => {
  dismissCredential()
  closeEditor()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :title="t('databases.users.list.heading')"
      :subtitle="t('databases.users.list.subtitle')"
      :note="t('databases.users.list.localNote')"
    />

    <UiAlert v-if="store.createErrorMessage !== null" variant="error">{{ store.createErrorMessage }}</UiAlert>
    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <!-- The editor's own read failed, so there is no dialog to show it in. -->
    <UiAlert v-if="store.editedUser === null && store.editorErrorMessage !== null" variant="error">
      {{ store.editorErrorMessage }}
    </UiAlert>

    <DatabaseUserCreateForm
      ref="form"
      :account-picker="accountPicker"
      :fixed-account-id="fixedAccountId"
      :submitting="store.creating"
      @search-accounts="searchAccounts"
      @submit="create"
    />

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('databases.users.list.sectionTitle')" />

      <div class="flex flex-col gap-4">
        <!-- No search and no filter has anything to narrow before the first user exists. -->
        <UiToolbar v-if="!answeredNothing || isNarrowed">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('databases.users.list.searchLabel')"
            :placeholder="t('databases.users.list.searchPlaceholder')"
            :clear-label="t('databases.users.list.searchClear')"
          />
          <!-- The account is the list's one filter, and a customer's list is already their own. -->
          <UiFilterTrigger
            v-if="!isCustomer"
            :label="t('common.list.filters')"
            :active-count="table.activeFilterCount.value"
            :active-label="t('common.list.filtersApplied', { n: table.activeFilterCount.value }, table.activeFilterCount.value)"
            @click="openFilters"
          />
        </UiToolbar>

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
            :title="t('databases.users.list.emptyTitle')"
            :description="t('databases.users.list.emptyDescription')"
          >
            <template #icon><UiIcon name="database" size="lg" /></template>
          </UiEmptyState>

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('databases.users.list.noResultsTitle')"
            :description="t('databases.users.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <DatabaseUsersTable
              :rows="store.users"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @act="ask"
              @edit-privileges="editPrivileges"
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
          :label="t('databases.users.list.filters.account')"
          :placeholder="t('common.list.any')"
          :selected-label="accountFilterName"
          @search="searchAccounts"
        />
      </template>
    </UiFilterSheet>

    <DatabaseUserActionConfirm
      v-if="pending !== null"
      :pending="pending"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirm"
    />

    <DatabaseUserPrivilegesDialog
      v-if="store.editedUser !== null && store.catalogue !== null"
      :user="store.editedUser"
      :catalogue="store.catalogue"
      :saving="store.editing"
      :error-message="store.editorErrorMessage"
      @save="savePrivileges"
      @close="closeEditor"
    />

    <!-- Mounted only while a credential is held; the same dialog the databases page shows. -->
    <DatabaseCreatedDialog
      v-if="store.revealedCredential !== null"
      :credential="store.revealedCredential"
      @close="dismissCredential"
    />
    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
