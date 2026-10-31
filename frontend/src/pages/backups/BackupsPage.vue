<script setup lang="ts">
/**
 * Backups screen: the take-a-backup-now form and the history of what has been taken. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout. State comes from
 * the backups store only; the page never touches the API layer (rules/vue.md).
 *
 * The page composes and owns state; the parts are their own components, props in and events out:
 * {@link ../../components/backups/BackupAttentionNotice.vue} (whether an account's latest finished
 * run failed — asked of the panel, because it is about every row and the screen holds one page),
 * {@link ../../components/backups/BackupCreateForm.vue},
 * {@link ../../components/backups/BackupHistoryTable.vue} (the rows and their menu, where restore is
 * offered on a completed backup only) and
 * {@link ../../components/backups/BackupHistoryFilterSheet.vue}.
 *
 * **The history is a server page** (`useServerTable`): page, page size, the search on the account,
 * the sort (`startedAt`, `size`) and the `account`, `status`, `kind`, `from` and `to` filters live in
 * the URL, and the panel searches, filters, sorts and counts. Creating or deleting a backup reads
 * the current page again rather than patching it: where a row falls is the server's to decide.
 *
 * **The two actions are not shaped alike.** Delete destroys a copy and is confirmed by `UiConfirm`.
 * Restore destroys the ACCOUNT, so it opens `BackupRestoreDialog`, which states what is replaced
 * before it accepts a typed confirmation.
 *
 * Where the bytes rest is {@link ./BackupDestinationsPage.vue}, linked from the heading. The history
 * card has `min-w-0`, so the table scrolls inside its own container and never sizes the panel.
 */
import { computed, ref, useTemplateRef, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import BackupAttentionNotice from '../../components/backups/BackupAttentionNotice.vue'
import BackupCreateForm from '../../components/backups/BackupCreateForm.vue'
import BackupHistoryFilterSheet from '../../components/backups/BackupHistoryFilterSheet.vue'
import BackupHistoryTable from '../../components/backups/BackupHistoryTable.vue'
import BackupRestoreDialog from '../../components/backups/BackupRestoreDialog.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useBackupHistoryFilters } from '../../composables/useBackupHistoryFilters'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useBackupsStore } from '../../stores/backups'
import { useLocaleStore } from '../../stores/locale'
import { backupAccountName } from '../../utils/backupAccountName'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { Backup, CreateBackupRequest } from '../../types/backup'

const { t } = useI18n()
const store = useBackupsStore()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { filter: accountListFilter, spec: accountSpec, name: accountFilterName } = useAccountFilter()
const { isAdmin, isCustomer } = useCallerRole()
const localeStore = useLocaleStore()
const labels = useListLabels()

const historyFilters = useBackupHistoryFilters(accountSpec)

const table = useServerTable<Backup>({
  filters: historyFilters.schema,
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<Backup>> => {
    return store.list(request)
  },
})

/** The backup whose deletion is awaiting confirmation, or the empty string when none is. */
const pendingId: Ref<string> = ref('')

/** The backup the restore dialog is open for (whole, as the dialog states what it holds), or `null`. */
const restoreTarget: Ref<Backup | null> = ref(null)

/** The create form, so an accepted backup can empty the picker it owns. */
const createForm = useTemplateRef<{ reset: () => void }>('createForm')

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

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  return historyFilters.chips(table.filters.value, accountFilterName.value)
})

/** The backup the confirmation is open for, or `null` when none is. */
const pendingBackup: ComputedRef<Backup | null> = computed(() => {
  return store.backups.find((backup) => {
    return backup.id === pendingId.value
  }) ?? null
})

/** The confirmation's accessible name, naming the copy that would be destroyed. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const pending = pendingBackup.value
  return pending === null
    ? ''
    : t('backups.list.confirmDeleteTitle', {
        account: backupAccountName(pending, t('common.emptyValue')),
        startedAt: formatIsoTimestamp(pending.startedAt, localeStore.current),
      })
})

/**
 * Reads the current page and the attention answer again, after a backup was taken or deleted.
 * @returns Resolves once both have settled.
 */
const refresh = async (): Promise<void> => {
  await Promise.all([table.reload(), store.loadAttention(accountListFilter.value)])
}

/**
 * Sends a create request the form has already validated, then reads the history again.
 * @param request The account the form collected.
 * @returns Resolves once the attempt and the reread have settled.
 */
const create = async (request: CreateBackupRequest): Promise<void> => {
  if (await store.create(request)) {
    createForm.value?.reset()
    await refresh()
  }
}

/**
 * Names the account a restore would replace, or `null` when the panel did not name it on the row.
 *
 * `null` rather than a placeholder: the dialog compares what is typed against it, and a translated
 * "unknown account" would be a confirmation nobody can satisfy; the server judges instead.
 * @param backup The backup a restore is asked of.
 * @returns The live account's system user name, or `null` when it is not known here.
 */
const accountUsername = (backup: Backup): string | null => {
  return backup.ownerName === '' ? null : backup.ownerName
}

/**
 * Opens the restore dialog on one row.
 * @param backup The backup the operator acted on.
 * @returns Nothing.
 */
const askRestore = (backup: Backup): void => {
  restoreTarget.value = backup
}

/**
 * Closes the restore dialog.
 * @returns Nothing.
 */
const closeRestore = (): void => {
  restoreTarget.value = null
}

/**
 * Starts a deletion on one row, which then waits for confirmation.
 * @param id The backup the operator acted on.
 * @returns Nothing.
 */
const ask = (id: string): void => {
  pendingId.value = id
}

/**
 * Abandons a pending deletion.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingId.value = ''
}

/**
 * Carries out the confirmed deletion, then reads the history again.
 * @returns Resolves once the request and the reread have settled.
 */
const confirm = async (): Promise<void> => {
  const removed = await store.remove(pendingId.value)

  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the operator the panel is working.
  pendingId.value = ''
  if (removed) {
    await refresh()
  }
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
 * Clears the search and every filter, the way back from a question that matched nothing.
 * @returns Resolves once the filters left the URL; the search follows after the box's pause.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
}

// The attention notice follows the account the history is narrowed to, and only that: it is about
// the accounts' latest runs, not about the page the search and other filters happen to show.
watch(
  (): string | undefined => {
    return accountListFilter.value.accountId
  },
  (): void => {
    void store.loadAttention(accountListFilter.value)
  },
  { immediate: true },
)
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading
      :title="t('backups.list.heading')"
      :subtitle="t('backups.list.subtitle')"
      :note="t('backups.list.storageNote')"
    >
      <template #actions>
        <!-- Administrators only, as presentation: the endpoints refuse a customer on their own. -->
        <UiButton v-if="isAdmin" variant="secondary" :to="{ name: 'backup-schedule' }">{{ t('backups.list.scheduleLink') }}</UiButton>
        <UiButton v-if="isAdmin" variant="secondary" :to="{ name: 'backup-destinations' }">{{ t('backups.list.destinationsLink') }}</UiButton>
      </template>
    </UiPageHeading>

    <!-- The one thing that needs attention goes first. -->
    <BackupAttentionNotice :attention="store.attention" />

    <UiAlert v-if="store.createErrorMessage !== null" variant="error">
      {{ store.createErrorMessage }}
    </UiAlert>

    <UiAlert v-if="store.removeErrorMessage !== null" variant="error">
      {{ store.removeErrorMessage }}
    </UiAlert>

    <BackupCreateForm
      ref="createForm"
      :account-picker="accountPicker"
      :fixed-account-id="fixedAccountId"
      :submitting="store.creating"
      @search-accounts="searchAccounts"
      @submit="create"
    />

    <UiCard class="min-w-0">
      <UiSectionHeading class="mb-4" :title="t('backups.list.sectionHeading')" />

      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('backups.list.searchLabel')"
            :placeholder="t('backups.list.searchPlaceholder')"
            :clear-label="t('backups.list.searchClear')"
          />
          <UiFilterTrigger
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

          <!-- Two kinds of nothing: no backup at all is the empty state, and the form above is the
               way to take the first; a search or filter matching none is the way back to all. -->
          <UiEmptyState
            v-if="answeredNothing && !isNarrowed"
            :title="t('backups.list.emptyTitle')"
            :description="t('backups.list.emptyDescription')"
          >
            <template #icon><UiIcon name="archive" size="lg" /></template>
          </UiEmptyState>

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('backups.list.noResultsTitle')"
            :description="t('backups.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <BackupHistoryTable
              :rows="store.backups"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @restore="askRestore"
              @delete="ask"
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

    <!-- A customer's history is already their own account's; there is no account to narrow to. -->
    <BackupHistoryFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      :show-account="!isCustomer"
      :account-picker="accountPicker"
      :account-name="accountFilterName"
      @update:model-value="table.setFilters"
      @close="closeFilters"
      @search-accounts="searchAccounts"
    />

    <!-- One dialog for the whole table, not wrapped in a `v-if`, so `UiModal`s open-watcher runs and
         its focus trap is live. -->
    <UiConfirm
      :open="pendingId.length > 0"
      :title="confirmationTitle"
      :question="t('backups.list.confirmDelete')"
      :confirm-label="t('backups.list.confirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('backups.list.working')"
      @close="cancel"
      @confirm="confirm"
    />

    <!-- `v-if` because `backup` is required; `UiModal`s immediate open-watcher handles `open` true. -->
    <BackupRestoreDialog
      v-if="restoreTarget !== null"
      :open="restoreTarget !== null"
      :backup="restoreTarget"
      :account-username="accountUsername(restoreTarget)"
      @close="closeRestore"
    />
  </section>
</template>
