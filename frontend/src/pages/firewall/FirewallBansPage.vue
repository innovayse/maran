<script setup lang="ts">
/**
 * The firewall's bans screen: who the host is currently refusing, why, and for how long, with the
 * form that bans an address by hand. Renders a `<section>`, not a `<main>` — the single `<main>`
 * landmark lives in the layout this page is nested under. State comes exclusively from the firewall
 * store; the page never touches the API layer (rules/vue.md: API composables are called from stores
 * only).
 *
 * **The list is one server page at a time** (`useServerTable`): page, page size, search and sort live
 * in the URL. Nothing is filtered or counted here — the panel applies its in-force rule, searches the
 * address, sorts and counts. A ban placed or lifted is followed by reading the page again, never by
 * a local patch: one address can hold several episodes, and where a row falls is the server's to say.
 *
 * **Two kinds of nothing.** Nobody banned at all is the empty state; a search matching nobody is the
 * no-results state, with the way back to every ban.
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
import FirewallBanForm from '../../components/firewall/FirewallBanForm.vue'
import FirewallBansTable from '../../components/firewall/FirewallBansTable.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useFirewallStore } from '../../stores/firewall'
import type { Ban, BanAddressRequest } from '../../types/firewall'
import type { ListLoad, ListRequest } from '../../types/list'

const { t } = useI18n()
const store = useFirewallStore()
const labels = useListLabels()

const table = useServerTable<Ban>({
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<Ban>> => {
    return store.listBans(request)
  },
})

/** The ban form, so an accepted ban can empty the fields it owns. */
const banForm = useTemplateRef<{ reset: () => void }>('banForm')

/** Whether the list is narrowed by a search, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== ''
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * Places a ban the form has already validated, then reads the page again.
 * @param request The address and duration the operator typed.
 * @returns Resolves once the attempt has settled.
 */
const placeBan = async (request: BanAddressRequest): Promise<void> => {
  if (await store.banAddress(request)) {
    banForm.value?.reset()
    await table.reload()
  }
}

/**
 * Lifts every ban in force for one address, then reads the page again.
 * @param address The address to let back in.
 * @returns Resolves once the attempt has settled.
 */
const liftBan = async (address: string): Promise<void> => {
  if (await store.unbanAddress(address)) {
    await table.reload()
  }
}

/**
 * Drops the ban form's last refusal once its fields change.
 * @returns Nothing.
 */
const clearBanError = (): void => {
  store.banErrorMessage = null
}

/**
 * Clears the search, the way back from a question that matched nothing.
 * @returns Nothing; the URL follows after the box's pause.
 */
const clearSearch = (): void => {
  table.search.value = ''
}

// A refusal from an earlier visit is not this visit's; the list itself loads from the URL.
onMounted(clearBanError)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('firewall.eyebrow')"
      :title="t('firewall.bans.heading')"
      :subtitle="t('firewall.bans.subtitle')"
    />

    <!-- A refused ban or lift is the one thing that needs attention, so it comes first. It is the
         server's own message, rendered as it was sent. -->
    <UiAlert v-if="store.banErrorMessage !== null" variant="error">
      {{ store.banErrorMessage }}
    </UiAlert>

    <UiCard>
      <FirewallBanForm ref="banForm" :submitting="store.acting" @submit="placeBan" @edit="clearBanError" />
    </UiCard>

    <UiCard>
      <UiSectionHeading class="mb-4" :title="t('firewall.bans.listTitle')" />

      <div class="flex flex-col gap-4">
        <!-- No search has anything to narrow before the first ban exists. -->
        <UiSearchInput
          v-if="!answeredNothing || isNarrowed"
          v-model="table.search.value"
          class="min-w-55 max-w-85"
          :label="t('firewall.bans.searchLabel')"
          :placeholder="t('firewall.bans.searchPlaceholder')"
          :clear-label="t('firewall.bans.searchClear')"
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
            :title="t('firewall.bans.emptyTitle')"
            :description="t('firewall.bans.emptyDescription')"
          />

          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('firewall.bans.noResultsTitle')"
            :description="t('firewall.bans.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearSearch">{{ t('firewall.bans.searchClear') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <FirewallBansTable
              :bans="store.bans"
              :busy="store.acting"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @unban="liftBan"
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
