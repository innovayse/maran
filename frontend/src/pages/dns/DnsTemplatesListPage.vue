<script setup lang="ts">
/**
 * DNS zone templates list screen, for administrators: one server page of the templates at a time,
 * searched and sorted by the panel. A template is the set of records a new zone starts with; the
 * built-in ones ship with the panel and can be changed but not deleted, and one template is the
 * default that zones created from a site start from. Renders a `<section>`, not a `<main>`. Rows
 * come from the templates store; the page never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search and sort live in the
 * query. The list has no filters, so there is no filter sheet.
 *
 * **Two kinds of nothing.** No templates at all is the empty state, with the way to create the
 * first. A search matching none is the no-results state, with the way back to every template.
 *
 * **Deleting is confirmed** and says what it does; making a template the default is not, since it
 * changes nothing that cannot be changed back.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import DnsTemplatesTable from '../../components/dns/DnsTemplatesTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useDnsTemplatesStore } from '../../stores/dnsTemplates'
import { takeFlashNotice } from '../../utils/flashNotice'
import type { ListLoad, ListRequest } from '../../types/list'
import type { DnsTemplate } from '../../types/dnsTemplate'

const { t } = useI18n()
const store = useDnsTemplatesStore()
const labels = useListLabels()

const table = useServerTable<DnsTemplate>({
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<DnsTemplate>> => {
    return store.list(request)
  },
})

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** The template awaiting a delete confirmation, or `null` when none is. */
const pending: Ref<DnsTemplate | null> = ref(null)

/** Whether the list is narrowed by a search, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== ''
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * Clears the search, the way back from a question that matched nothing.
 * @returns Nothing; the list follows after the search box's pause.
 */
const clearSearch = (): void => {
  table.search.value = ''
}

/**
 * Makes a template the default and reads the page again, since the previous default's badge moves.
 * @param template The template the person chose.
 * @returns Resolves once the request has settled.
 */
const makeDefault = async (template: DnsTemplate): Promise<void> => {
  const changed = await store.makeDefault(template.id)
  if (changed) {
    notice.value = t('dns.templates.madeDefault', { name: template.name })
    await table.reload()
  }
}

/**
 * Starts a delete, which then waits for confirmation.
 * @param template The template the person chose to delete.
 * @returns Nothing.
 */
const ask = (template: DnsTemplate): void => {
  pending.value = template
}

/**
 * Abandons a pending delete.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed delete and reads the page again, since the total and the rows that
 * move up from the next page are the server's to say.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const template = pending.value
  if (template === null) {
    return
  }
  const deleted = await store.remove(template.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the
  // spinner that tells the person the panel is working.
  pending.value = null
  if (deleted) {
    notice.value = t('dns.templates.deleted', { name: template.name })
    await table.reload()
  }
}

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(() => {
  store.clearErrors()
  notice.value = takeFlashNotice()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('dns.templates.heading')" :subtitle="t('dns.templates.subtitle')">
      <template #actions>
        <UiButton :to="{ name: 'dns-template-new' }">{{ t('dns.templates.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('dns.templates.emptyTitle')"
      :description="t('dns.templates.emptyDescription')"
    >
      <template #icon><UiIcon name="fileText" /></template>
      <UiButton :to="{ name: 'dns-template-new' }">{{ t('dns.templates.createAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('dns.templates.searchLabel')"
            :placeholder="t('dns.templates.searchPlaceholder')"
            :clear-label="t('dns.templates.searchClear')"
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
            v-if="answeredNothing"
            :title="t('dns.templates.noResultsTitle')"
            :description="t('dns.templates.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearSearch">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <DnsTemplatesTable
              :rows="store.templates"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @make-default="makeDefault"
              @remove="ask"
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
      :open="pending !== null"
      :title="t('dns.templates.confirmDeleteTitle', { name: pending?.name ?? '' })"
      :question="t('dns.templates.confirmDelete')"
      :confirm-label="t('dns.templates.confirmDeleteAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('common.saving')"
      destructive
      @close="cancel"
      @confirm="confirm"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
