<script setup lang="ts">
/**
 * The firewall's whitelist screen: the ranges the automatic bans never touch, with the form that
 * adds one. Renders a `<section>`, not a `<main>` — the single `<main>`
 * landmark lives in the layout this page is nested under. State comes exclusively from the firewall
 * store; the page never touches the API layer (rules/vue.md: API composables are called from stores
 * only).
 *
 * **The list is one server page at a time** (`useServerTable`): page, page size, search and sort live
 * in the URL. Nothing is filtered or counted here — the panel searches the range and the note, sorts
 * and counts. An exemption added or removed is followed by reading the page again, never by a local
 * patch: where a row falls is the server's sort, search and paging to say.
 *
 * **Two kinds of nothing.** No exemption at all is the empty state; a search matching none is the
 * no-results state, with the way back to every range.
 */
import { computed, onMounted, useTemplateRef, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import FirewallWhitelistForm from '../../components/firewall/FirewallWhitelistForm.vue'
import FirewallWhitelistTable from '../../components/firewall/FirewallWhitelistTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useFirewallStore } from '../../stores/firewall'
import type { AddWhitelistEntryRequest, WhitelistEntry } from '../../types/firewall'
import type { ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const store = useFirewallStore()
const labels = useListLabels()

const table = useServerTable<WhitelistEntry>({
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<WhitelistEntry>> => {
    return store.listWhitelist(request)
  },
})

/** The whitelist form, so an accepted exemption can empty the fields it owns. */
const whitelistForm = useTemplateRef<{ reset: () => void }>('whitelistForm')

/** Whether the list is narrowed by a search, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== ''
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * Adds an exemption the form has already validated, then reads the page again.
 * @param request The range and note the operator typed.
 * @returns Resolves once the attempt has settled.
 */
const addExemption = async (request: AddWhitelistEntryRequest): Promise<void> => {
  if (await store.addWhitelistEntry(request)) {
    whitelistForm.value?.reset()
    await table.reload()
  }
}

/**
 * Removes an exemption, then reads the page again: the removal pulls a row up from the next page.
 * @param id The row to remove.
 * @returns Resolves once the attempt has settled.
 */
const removeExemption = async (id: string): Promise<void> => {
  if (await store.removeWhitelistEntry(id)) {
    await table.reload()
  }
}

/**
 * Drops the exemption form's last refusal once its fields change.
 * @returns Nothing.
 */
const clearWhitelistError = (): void => {
  store.whitelistErrorMessage = null
}

/**
 * Clears the search, the way back from a question that matched nothing.
 * @returns Nothing; the URL follows after the box's pause.
 */
const clearSearch = (): void => {
  table.search.value = ''
}

// A refusal from an earlier visit is not this visit's; the list itself loads from the URL.
onMounted(clearWhitelistError)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :title="t('firewall.whitelist.heading')"
      :subtitle="t('firewall.whitelist.subtitle')"
      :note="t('firewall.whitelist.seedNote')"
    />

    <!-- A refused addition or removal is the one thing that needs attention, so it comes first. It
         is the server's own message, rendered as it was sent. -->
    <UiAlert v-if="store.whitelistErrorMessage !== null" variant="error">
      {{ store.whitelistErrorMessage }}
    </UiAlert>

    <UiCard>
      <FirewallWhitelistForm
        ref="whitelistForm"
        :submitting="store.acting"
        @submit="addExemption"
        @edit="clearWhitelistError"
      />
    </UiCard>

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('firewall.whitelist.listTitle')" />

      <div class="flex flex-col gap-4">
        <!-- No search has anything to narrow before the first exemption exists. -->
        <UiSearchInput
          v-if="!answeredNothing || isNarrowed"
          v-model="table.search.value"
          class="min-w-55 max-w-85"
          :label="t('firewall.whitelist.searchLabel')"
          :placeholder="t('firewall.whitelist.searchPlaceholder')"
          :clear-label="t('firewall.whitelist.searchClear')"
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
            :title="t('firewall.whitelist.emptyTitle')"
            :description="t('firewall.whitelist.emptyDescription')"
          />

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('firewall.whitelist.noResultsTitle')"
            :description="t('firewall.whitelist.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearSearch">{{ t('firewall.whitelist.searchClear') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <FirewallWhitelistTable
              :entries="store.whitelist"
              :busy="store.acting"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @remove="removeExemption"
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
  </section>
</template>
