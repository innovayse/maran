<script setup lang="ts">
/**
 * The SFTP logins screen: the create form, then one server page of the SFTP logins at a time,
 * searched, sorted and narrowed to an account by the panel. Renders a `<section>`, not a `<main>` —
 * the single landmark lives in the layout. Rows come from the SFTP store; the page never touches the
 * API layer (rules/vue.md).
 *
 * **One module, one page.** SFTP and FTPS logins are different system users answered by different
 * modules, so each has its own page under the "File transfer" submenu; this one lists the Sftp
 * module's logins and nothing else, and no list here is assembled from two endpoints.
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort and `?account=`
 * live in the query. `?account=<id>` is the link the account page opens; it is read through
 * `useAccountFilter().spec`, so a customer's list ignores it and the account filter is not offered
 * to them: their lists are already their own.
 *
 * **Two kinds of nothing.** No logins at all is the empty state; a search or filter matching none is
 * the no-results state, with the way back to every login. A server's error is rendered verbatim and
 * no table is drawn beside it.
 *
 * **The password is shown once.** A create or a reset answers with it, the dialog shows it, and
 * leaving the page forgets it: the store's copy is cleared on unmount.
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
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import SftpUserActionConfirm from '../../components/sftp/SftpUserActionConfirm.vue'
import SftpUsersTable from '../../components/sftp/SftpUsersTable.vue'
import UiToast from '../../components/ui/UiToast.vue'
import SftpUserCreateForm from '../../components/sftp/SftpUserCreateForm.vue'
import SftpUserCreatedDialog from '../../components/sftp/SftpUserCreatedDialog.vue'
import { useAccountChoice, type AccountPickerState } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useSftpStore } from '../../stores/sftp'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { CreateSftpUserRequest, SftpUser, SftpUserAction } from '../../types/sftpUser'


const { t } = useI18n()
const store = useSftpStore()
const labels = useListLabels()
const { isAdmin, isCustomer } = useCallerRole()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()

const table = useServerTable<SftpUser>({
  filters: { account: accountSpec },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<SftpUser>> => {
    return store.list(request)
  },
})

/** The create form, so an accepted create can empty the field it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The login whose action is awaiting confirmation, or `null` when none is. */
const pending: Ref<SftpUser | null> = ref(null)

/** Which action that login is waiting on. */
const pendingAction: Ref<SftpUserAction> = ref('remove')

/** Whether the list is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** The account filter's picker, after an "any account" choice counted into the total. */
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
    ? [labels.chip('account', t('sftp.list.filters.account'), accountFilterName.value)]
    : []
})

/**
 * Creates a login, then reads the page again: where the new row falls is the server's sort to say.
 * @param request The account and the name the form collected.
 * @returns Resolves once the create and the re-read have settled.
 */
const create = async (request: CreateSftpUserRequest): Promise<void> => {
  if (await store.create(request)) {
    notice.value = t('sftp.list.created', { name: request.name })
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Starts an action on one row, which then waits for confirmation.
 * @param login The row the operator acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (login: SftpUser, action: SftpUserAction): void => {
  pending.value = login
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
 * Carries out the confirmed action; a removal reads the page again so it refills from the next one.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const login = pending.value
  if (login === null) {
    return
  }
  if (pendingAction.value === 'remove') {
    if (await store.remove(login.id)) {
      await table.reload()
    }
  } else {
    await store.resetPassword(login.id)
  }
  // Closed after the request settles rather than before it is sent: the dialog holds the spinner.
  pending.value = null
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
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

// The credential must not survive a navigation. The store outlives this page.
onBeforeUnmount(store.dismissCredential)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('sftp.list.eyebrow')"
      :title="t('sftp.list.heading')"
      :subtitle="t('sftp.list.subtitle')"
      :note="t('sftp.list.prefixNote')"
    />

    <!-- What needs attention goes first; the backend's own sentence, verbatim. -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">{{ store.createErrorMessage }}</UiAlert>
    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <SftpUserCreateForm
      ref="form"
      :account-picker="accountPicker"
      :fixed-account-id="fixedAccountId"
      :submitting="store.creating"
      @search-accounts="searchAccounts"
      @submit="create"
    />

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('sftp.list.emptyTitle')"
      :description="t('sftp.list.emptyDescription')"
    >
      <template #icon><UiIcon name="folderKey" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('sftp.list.searchLabel')"
            :placeholder="t('sftp.list.searchPlaceholder')"
            :clear-label="t('sftp.list.searchClear')"
          />
          <!-- The account is the list's one filter, and a customer's list is already their own. -->
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
            v-if="answeredNothing"
            :title="t('sftp.list.noResultsTitle')"
            :description="t('sftp.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <SftpUsersTable
              :rows="store.sftpUsers"
              :show-owner="isAdmin"
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
          :label="t('sftp.list.filters.account')"
          :placeholder="t('common.list.any')"
          :selected-label="accountFilterName"
          @search="searchAccounts"
        />
      </template>
    </UiFilterSheet>

    <SftpUserActionConfirm
      :login="pending"
      :action="pendingAction"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirm"
    />

    <SftpUserCreatedDialog
      :open="store.revealedCredential !== null"
      :full-name="store.revealedCredential?.fullName ?? ''"
      :password="store.revealedCredential?.password ?? ''"
      @close="store.dismissCredential"
    />

    <div class="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end sm:left-auto">
      <UiToast v-if="notice !== null" variant="success" :close-label="t('common.close')" @dismiss="dismissNotice">
        {{ notice }}
      </UiToast>
    </div>
  </section>
</template>
