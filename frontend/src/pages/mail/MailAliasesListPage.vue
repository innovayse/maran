<script setup lang="ts">
/**
 * The aliases and forwarders screen: the create form, then one server page of the aliases,
 * forwarders and catch-alls at a time, searched, sorted and narrowed by account, domain and kind by
 * the panel. Renders a `<section>`, not a `<main>` — the single landmark lives in the layout. Rows
 * come from the aliases store; the page never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort, `?account=`,
 * `?domain=` (a domain's name) and `?kind=` live in the query. `?account=<id>` is read through
 * `useAccountFilter().spec`, so a customer's list ignores it and the account filter is not offered
 * to them.
 *
 * **Two kinds of nothing.** No aliases at all is the empty state; a search or filter matching none
 * is the no-results state, with the way back to every alias. A server's error is rendered verbatim
 * and no table is drawn beside it.
 */
import { computed, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import MailAliasCreateForm from '../../components/mail/MailAliasCreateForm.vue'
import MailAliasEditDialog from '../../components/mail/MailAliasEditDialog.vue'
import MailAliasRemoveConfirm from '../../components/mail/MailAliasRemoveConfirm.vue'
import MailAliasesFilterSheet from '../../components/mail/MailAliasesFilterSheet.vue'
import MailAliasesTable from '../../components/mail/MailAliasesTable.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useMailDomainChoice } from '../../composables/useMailDomainChoice'
import { useServerTable } from '../../composables/useServerTable'
import { useMailAliasesStore } from '../../stores/mailAliases'
import { useMailChoicesStore } from '../../stores/mailChoices'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type {
  CreateMailAliasRequest,
  MailAlias,
  MailAliasAction,
  PendingMailAliasAction,
} from '../../types/mailAlias'

const { t } = useI18n()
const store = useMailAliasesStore()
const choicesStore = useMailChoicesStore()
const labels = useListLabels()
const { isAdmin, isCustomer } = useCallerRole()
const { picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { picker: createDomainPicker, search: searchCreateDomains } = useMailDomainChoice('id')
const { picker: filterDomainPicker, search: searchFilterDomains } = useMailDomainChoice('name')
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()

const table = useServerTable<MailAlias>({
  filters: { account: accountSpec, domain: {}, kind: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<MailAlias>> => {
    return store.list(request)
  },
})

/** The create form, so an accepted create can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The alias and the dialog opened for it, or `null` when none is. */
const pending: Ref<PendingMailAliasAction | null> = ref(null)

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
  const applied: ActiveFilterChip[] = []
  const filters = table.filters.value
  if (filters.account !== '') {
    applied.push(labels.chip('account', t('mail.aliases.filters.account'), accountFilterName.value))
  }
  if (filters.domain !== '') {
    applied.push(labels.chip('domain', t('mail.aliases.filters.domain'), filters.domain))
  }
  if (filters.kind !== '') {
    const named = choicesStore.choices?.aliasKinds.find((choice) => {
      return choice.value === filters.kind
    })
    applied.push(labels.chip('kind', t('mail.aliases.filters.kind'), named?.label ?? filters.kind))
  }
  return applied
})

/**
 * Creates an alias, then reads the page again: where the new row falls is the server's sort to say.
 * @param request The domain, kind, local part and targets the form collected.
 * @returns Resolves once the create and the re-read have settled.
 */
const create = async (request: CreateMailAliasRequest): Promise<void> => {
  if (await store.create(request)) {
    notice.value = t('mail.aliases.created')
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Opens the dialog for one row.
 * @param alias The row the person acted on.
 * @param action Which dialog they asked for.
 * @returns Nothing.
 */
const ask = (alias: MailAlias, action: MailAliasAction): void => {
  store.clearActionError()
  pending.value = { alias, action }
}

/**
 * Closes the open dialog without a change.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Closes the edit dialog after the panel accepted its change, and says so.
 * @returns Nothing.
 */
const saved = (): void => {
  const awaiting = pending.value
  if (awaiting !== null) {
    notice.value = t('mail.aliases.edit.saved', { name: awaiting.alias.address })
  }
  pending.value = null
}

/**
 * Carries out the confirmed removal; the page reads again so it refills from the next one.
 * @returns Resolves once the request has settled.
 */
const confirmRemove = async (): Promise<void> => {
  const awaiting = pending.value
  if (awaiting === null) {
    return
  }
  if (await store.remove(awaiting.alias.id)) {
    await table.reload()
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

onMounted(() => {
  void choicesStore.load()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('mail.aliases.heading')" :subtitle="t('mail.aliases.subtitle')" />

    <!-- What needs attention goes first; the backend's own sentence, verbatim. -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">{{ store.createErrorMessage }}</UiAlert>
    <UiAlert v-if="store.actionErrorMessage !== null && pending === null" variant="error">
      {{ store.actionErrorMessage }}
    </UiAlert>

    <MailAliasCreateForm
      ref="form"
      :domain-picker="createDomainPicker"
      :kinds="choicesStore.choices?.aliasKinds ?? []"
      :limits="choicesStore.choices?.limits ?? null"
      :submitting="store.creating"
      @search-domains="searchCreateDomains"
      @submit="create"
    />

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('mail.aliases.emptyTitle')"
      :description="t('mail.aliases.emptyDescription')"
    >
      <template #icon><UiIcon name="mail" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('mail.aliases.searchLabel')"
            :placeholder="t('mail.aliases.searchPlaceholder')"
            :clear-label="t('mail.aliases.searchClear')"
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

          <UiEmptyState
            v-if="answeredNothing"
            :title="t('mail.aliases.noResultsTitle')"
            :description="t('mail.aliases.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <MailAliasesTable
              :rows="store.aliases"
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

    <MailAliasesFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      :show-account="!isCustomer"
      :account-picker="accountPicker"
      :account-name="accountFilterName"
      :domain-picker="filterDomainPicker"
      :kinds="choicesStore.choices?.aliasKinds ?? []"
      @apply="table.setFilters"
      @close="closeFilters"
      @search="searchAccounts"
      @search-domains="searchFilterDomains"
    />

    <MailAliasEditDialog
      v-if="pending !== null && pending.action === 'edit'"
      :alias="pending.alias"
      :limits="choicesStore.choices?.limits ?? null"
      @close="cancel"
      @saved="saved"
    />

    <MailAliasRemoveConfirm
      v-if="pending !== null && pending.action === 'remove'"
      :alias="pending.alias"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirmRemove"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
