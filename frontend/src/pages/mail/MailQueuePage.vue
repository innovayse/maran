<script setup lang="ts">
/**
 * The mail queue screen, for administrators: one server page of the messages the host's mail server
 * is holding, searched and sorted by the panel, with a row action that removes one message and a
 * page action that asks for every deferred message to be retried now. Renders a `<section>`, not a
 * `<main>` — the single landmark lives in the layout. Rows come from the mail queue store; the page
 * never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search and sort live in the
 * query.
 *
 * **Two kinds of nothing, and a queue that could not be read is neither.** An empty queue is the
 * empty state; a search matching none is the no-results state, with the way back to the whole queue.
 * A refusal (the host's mail stack could not be read) is rendered verbatim and no table is drawn
 * beside it: an unreadable queue is not an empty one. When the host holds more messages than it
 * lists, a notice says the pages cover the newest only.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import MailQueueFlushConfirm from '../../components/mail/MailQueueFlushConfirm.vue'
import MailQueueRemoveConfirm from '../../components/mail/MailQueueRemoveConfirm.vue'
import MailQueueTable from '../../components/mail/MailQueueTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useMailQueueStore } from '../../stores/mailQueue'
import type { ListLoad, ListRequest } from '../../types/list'
import type { MailQueueEntry } from '../../types/mailQueue'

const { t } = useI18n()
const store = useMailQueueStore()
const labels = useListLabels()

const table = useServerTable<MailQueueEntry>({
  filters: {},
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<MailQueueEntry>> => {
    return store.list(request)
  },
})

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** The message whose removal waits for confirmation, or `null` when none does. */
const pendingRemoval: Ref<MailQueueEntry | null> = ref(null)

/** Whether the "retry now" question is open. */
const flushOpen: Ref<boolean> = ref(false)

/** Whether the list is narrowed by a search, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== ''
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** Whether the panel listed fewer messages than the queue holds. */
const showsTruncation: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && store.truncated && store.queueTotal !== null
})

/**
 * Opens the removal question for one message.
 * @param entry The row the administrator acted on.
 * @returns Nothing.
 */
const askRemoval = (entry: MailQueueEntry): void => {
  store.clearActionError()
  pendingRemoval.value = entry
}

/**
 * Opens the "retry now" question.
 * @returns Nothing.
 */
const askFlush = (): void => {
  store.clearActionError()
  flushOpen.value = true
}

/**
 * Closes whichever question is open without a change.
 * @returns Nothing.
 */
const cancel = (): void => {
  pendingRemoval.value = null
  flushOpen.value = false
}

/**
 * Carries out the confirmed removal; the page reads again so it refills from the next message.
 * @returns Resolves once the request has settled.
 */
const confirmRemoval = async (): Promise<void> => {
  const awaiting = pendingRemoval.value
  if (awaiting === null) {
    return
  }
  if (await store.remove(awaiting.queueId)) {
    notice.value = t('mail.queue.removed', { id: awaiting.queueId })
    await table.reload()
  }
  // Closed after the request settles rather than before it is sent: the dialog holds the spinner.
  pendingRemoval.value = null
}

/**
 * Carries out the confirmed flush; the page reads again to show what is left.
 * @returns Resolves once the request has settled.
 */
const confirmFlush = async (): Promise<void> => {
  if (await store.flush()) {
    notice.value = t('mail.queue.flushed')
    await table.reload()
  }
  flushOpen.value = false
}

/**
 * Clears the search, the way back from a question that matched nothing.
 * @returns Nothing.
 */
const clearSearch = (): void => {
  table.search.value = ''
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('mail.queue.heading')" :subtitle="t('mail.queue.subtitle')">
      <template #actions>
        <UiButton variant="secondary" :disabled="store.acting" @click="askFlush">
          {{ t('mail.queue.flush') }}
        </UiButton>
      </template>
    </UiPageHeading>

    <!-- What needs attention goes first; the backend's own sentence, verbatim. -->
    <UiAlert v-if="store.actionErrorMessage !== null && pendingRemoval === null && !flushOpen" variant="error">
      {{ store.actionErrorMessage }}
    </UiAlert>
    <UiAlert v-if="showsTruncation" variant="info">
      {{ t('mail.queue.truncated', { total: store.queueTotal ?? 0 }) }}
    </UiAlert>

    <UiCard class="min-w-0">
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('mail.queue.searchLabel')"
            :placeholder="t('mail.queue.searchPlaceholder')"
            :clear-label="t('mail.queue.searchClear')"
          />
        </UiToolbar>

        <UiLoadingRegion :state="table.loadingState.value">
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing && !isNarrowed"
            :title="t('mail.queue.emptyTitle')"
            :description="t('mail.queue.emptyDescription')"
          >
            <template #icon><UiIcon name="mail" size="lg" /></template>
          </UiEmptyState>
          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('mail.queue.noResultsTitle')"
            :description="t('mail.queue.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearSearch">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <MailQueueTable
              :rows="store.entries"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @remove="askRemoval"
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

    <MailQueueRemoveConfirm
      v-if="pendingRemoval !== null"
      :entry="pendingRemoval"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirmRemoval"
    />
    <MailQueueFlushConfirm v-if="flushOpen" :acting="store.acting" @close="cancel" @confirm="confirmFlush" />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
