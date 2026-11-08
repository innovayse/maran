<script setup lang="ts">
/**
 * The file manager's trash: one server page of what the account has moved to the trash, with the
 * ways out of it. Renders a `<section>`; the single `<main>` lives in the layout. Rows come from the
 * trash store; the page never touches the API layer (rules/vue.md).
 *
 * **The URL is the state** (`useServerTable`): the account (`?account=`), the search, the sort and
 * the page. **Which account.** A customer owns one and gets no picker; an administrator searches the
 * panel's accounts and opens on the first one it lists.
 *
 * **Ways out.** Restore puts ticked entries back where they were; a target that exists again is
 * refused for that entry alone, with the server's own reason, and the others still go. Emptying the
 * trash removes everything for good and asks first. Entries also leave on their own when the
 * retention the server reports runs out.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBulkBar from '../../components/ui/UiBulkBar.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiCheckbox from '../../components/ui/UiCheckbox.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import FileBatchResultNotice from '../../components/files/FileBatchResultNotice.vue'
import TrashTable from '../../components/files/TrashTable.vue'
import { useFileTrash } from '../../composables/useFileTrash'
import { useListLabels } from '../../composables/useListLabels'
import { useFileTrashStore } from '../../stores/fileTrash'
import { useFilesOptionsStore } from '../../stores/filesOptions'

const { t } = useI18n()
const store = useFileTrashStore()
const optionsStore = useFilesOptionsStore()
const labels = useListLabels()
const {
  accountChoice,
  table,
  accountId,
  selectedAccountLabel,
  restoreResult,
  confirmingEmpty,
  isSearching,
  answeredNothing,
  failureMessage,
  working,
  changeAccount,
  restoreSelected,
  emptyTrash,
  start,
} = useFileTrash()

onMounted(start)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('filesTrash.heading')" :subtitle="t('filesTrash.subtitle')">
      <template #actions>
        <UiCombobox
          v-if="accountChoice.fixedAccountId.value === null"
          :model-value="accountId"
          v-bind="accountChoice.picker.value"
          :selected-label="selectedAccountLabel"
          :label="t('filesTrash.accountLabel')"
          :placeholder="t('filesTrash.accountPlaceholder')"
          class="w-full min-w-0 lg:w-72"
          @update:model-value="changeAccount"
          @search="accountChoice.search"
        />
        <UiButton variant="secondary" :to="{ name: 'file-manager', query: accountId === '' ? {} : { account: accountId } }">
          <UiIcon name="folder" size="sm" />{{ t('filesTrash.backLink') }}
        </UiButton>
        <UiButton variant="destructive" :disabled="accountId === '' || table.total.value === 0 || working" @click="confirmingEmpty = true">
          <UiIcon name="trash" size="sm" />{{ t('filesTrash.empty.action') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="accountId === ''"
      :title="t('filesTrash.chooseAccountTitle')"
      :description="t('filesTrash.chooseAccountDescription')"
    >
      <template #icon><UiIcon name="trash" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiAlert v-if="optionsStore.options !== null" variant="info">
          {{ t('filesTrash.retention', { days: optionsStore.options.trashRetentionDays }, optionsStore.options.trashRetentionDays) }}
        </UiAlert>
        <UiAlert v-if="failureMessage !== null" variant="error">{{ failureMessage }}</UiAlert>
        <FileBatchResultNotice v-if="restoreResult !== null" :result="restoreResult" />

        <UiSearchInput
          :model-value="table.search.value"
          :label="t('filesTrash.searchLabel')"
          :placeholder="t('filesTrash.searchPlaceholder')"
          :clear-label="t('filesTrash.searchClear')"
          @update:model-value="table.search.value = $event"
        />

        <UiCheckbox
          :model-value="store.allSelected"
          :label="t('filesTrash.selectAll')"
          :disabled="store.entries.length === 0"
          @update:model-value="store.selectAll"
        />

        <UiBulkBar
          :count="store.selectedIds.length"
          :label="t('filesTrash.selection.count', { count: store.selectedIds.length }, store.selectedIds.length)"
          :actions-label="t('filesTrash.selection.actions')"
        >
          <UiButton variant="secondary" :disabled="working" @click="restoreSelected">
            <UiIcon name="rotateCw" size="sm" />{{ t('filesTrash.restore') }}
          </UiButton>
          <UiButton variant="ghost" @click="store.selectAll(false)">{{ t('filesTrash.selection.clear') }}</UiButton>
        </UiBulkBar>

        <UiLoadingRegion :state="table.loadingState.value">
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing"
            :title="isSearching ? t('filesTrash.noResultsTitle') : t('filesTrash.emptyTitle')"
            :description="isSearching ? t('filesTrash.noResultsDescription') : t('filesTrash.emptyDescription')"
          >
            <template #icon><UiIcon name="trash" /></template>
            <UiButton v-if="isSearching" variant="secondary" @click="table.search.value = ''">
              {{ t('common.list.clearSearchAndFilters') }}
            </UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <TrashTable
              :rows="store.entries"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              :selected-ids="store.selectedIds"
              @sort="table.toggleSort"
              @select="store.select"
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

    <UiConfirm
      :open="confirmingEmpty"
      :title="t('filesTrash.empty.title')"
      :question="t('filesTrash.empty.question', { count: table.total.value }, table.total.value)"
      :confirm-label="t('filesTrash.empty.confirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="working"
      :acting-label="t('filesTrash.empty.working')"
      @confirm="emptyTrash"
      @close="confirmingEmpty = false"
    />
  </section>
</template>
