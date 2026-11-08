<script setup lang="ts">
/**
 * The file manager's browser: one server page of the folder being looked at, with the path bar
 * above it, searched, sorted and paged by the panel. Renders a `<section>`; the single `<main>`
 * lives in the layout. Rows come from the files store; the page never touches the API layer
 * (rules/vue.md).
 *
 * **The URL is the state** (`useServerTable` and `useFilePathQuery`): the account (`?account=`), the
 * folder (`?path=`), the dot-name toggle (`?hidden=1`), the search, the sort and the page. A folder
 * can therefore be bookmarked, reloaded and walked back through. A path typed into the address is
 * sent as typed and the server's answer is shown: the panel decides nothing about what stays inside
 * a home (rules/security.md item 2).
 *
 * **Which account.** A customer owns one and gets no picker; an administrator searches the panel's
 * accounts and opens on the first one it lists.
 *
 * **Nothing is filtered here.** The panel lists, searches, sorts and counts; this page shows the
 * page it answers and the total across every page.
 *
 * **Two kinds of nothing.** An empty folder is an empty state; a search matching none is the
 * no-results state with the way back. A refusal is shown as the server's own text (a path outside
 * the home, another tenant's account answered 404) and no table is drawn beside it.
 *
 * A row's menu and the bulk bar report the operation the person asked for as `requested`. Opening a
 * file for editing is a navigation carried out by `useFileBrowser`; the other operations are carried out
 * by the dialogs and transfers that read `requested`.
 */
import { onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBreadcrumbPath from '../../components/ui/UiBreadcrumbPath.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import FileBrowserTable from '../../components/files/FileBrowserTable.vue'
import FileOperationHost from '../../components/files/FileOperationHost.vue'
import FileSelectionBar from '../../components/files/FileSelectionBar.vue'
import FileToolbar from '../../components/files/FileToolbar.vue'
import FileUploadPanel from '../../components/files/FileUploadPanel.vue'
import { useFileBrowser } from '../../composables/useFileBrowser'
import { useListLabels } from '../../composables/useListLabels'
import { useFilesStore } from '../../stores/files'

const { t } = useI18n()
const store = useFilesStore()
const labels = useListLabels()
const {
  accountChoice,
  table,
  location,
  requested,
  isSearching,
  answeredNothing,
  maxBatchItems,
  changeAccount,
  onEntryAction,
  onCreate,
  onBatchAction,
  onOperationDone,
  onOperationClosed,
  openFile,
  refresh,
  start,
} = useFileBrowser()

onMounted(start)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('filesBrowser.heading')" :subtitle="t('filesBrowser.subtitle')">
      <template #actions>
        <UiCombobox
          v-if="accountChoice.fixedAccountId.value === null"
          :model-value="location.accountId.value"
          v-bind="accountChoice.picker.value"
          :selected-label="location.selectedAccountLabel.value"
          :label="t('filesBrowser.accountLabel')"
          :placeholder="t('filesBrowser.accountPlaceholder')"
          class="w-full min-w-0 lg:w-72"
          @update:model-value="changeAccount"
          @search="accountChoice.search"
        />
        <UiButton
          variant="secondary"
          :to="{ name: 'file-trash', query: location.accountId.value === '' ? {} : { account: location.accountId.value } }"
        >
          <UiIcon name="trash" size="sm" />{{ t('filesBrowser.trashLink') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <UiEmptyState
      v-if="location.accountId.value === ''"
      :title="t('filesBrowser.chooseAccountTitle')"
      :description="t('filesBrowser.chooseAccountDescription')"
    >
      <template #icon><UiIcon name="folder" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiBreadcrumbPath
          :label="t('filesBrowser.pathLabel')"
          :root-label="t('filesBrowser.home')"
          :path="location.path.value"
          :target="location.target"
        />

        <div class="flex flex-wrap gap-2">
          <UiButton variant="secondary" @click="onCreate('createFile')">
            <UiIcon name="plus" size="sm" />{{ t('filesBrowser.newFile') }}
          </UiButton>
          <UiButton variant="secondary" @click="onCreate('createFolder')">
            <UiIcon name="plus" size="sm" />{{ t('filesBrowser.newFolder') }}
          </UiButton>
        </div>

        <FileUploadPanel :account-id="location.accountId.value" :folder="location.path.value" @finished="refresh" />

        <FileToolbar
          :search="table.search.value"
          :show-hidden="location.showHidden.value"
          :all-selected="store.allSelected"
          :can-select="store.entries.length > 0"
          :can-go-up="location.path.value !== ''"
          @update:search="table.search.value = $event"
          @update:show-hidden="location.setShowHidden"
          @select-all="store.selectAll"
          @up="location.goUp"
          @refresh="refresh"
        />

        <FileSelectionBar
          :count="store.selectedPaths.length"
          :max-items="maxBatchItems"
          @action="onBatchAction"
          @clear="store.clearSelection"
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
            :title="isSearching ? t('filesBrowser.noResultsTitle') : t('filesBrowser.emptyTitle')"
            :description="isSearching ? t('filesBrowser.noResultsDescription') : t('filesBrowser.emptyDescription')"
          >
            <template #icon><UiIcon name="folder" /></template>
            <UiButton v-if="isSearching" variant="secondary" @click="table.search.value = ''">
              {{ t('common.list.clearSearchAndFilters') }}
            </UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <FileBrowserTable
              :rows="store.entries"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              :selected-paths="store.selectedPaths"
              :folder-target="location.target"
              @sort="table.toggleSort"
              @select="store.select"
              @open="openFile"
              @action="onEntryAction"
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

    <FileOperationHost
      :order="requested"
      :account-id="location.accountId.value"
      :folder="location.path.value"
      @done="onOperationDone"
      @close="onOperationClosed"
    />
  </section>
</template>
