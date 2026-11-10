<script setup lang="ts">
/**
 * The mail domains screen: the add form, then one server page of the mail domains at a time,
 * searched, sorted and narrowed by account and state by the panel. Renders a `<section>`, not a
 * `<main>` — the single landmark lives in the layout. Rows come from the mail domains store; the
 * page never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort, `?account=` and
 * `?status=` live in the query. `?account=<id>` is read through `useAccountFilter().spec`, so a
 * customer's list ignores it and the account filter is not offered to them.
 *
 * **Two kinds of nothing.** No domains at all is the empty state; a search or filter matching none
 * is the no-results state, with the way back to every domain. A server's error is rendered verbatim
 * and no table is drawn beside it.
 *
 * **The DNS records are a dialog off the row.** What to publish at the domain's DNS host is read
 * when the person asks for it, so the list stays one cheap read.
 */
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
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
import UiToolbar from '../../components/ui/UiToolbar.vue'
import MailDnsRecordsDialog from '../../components/mail/MailDnsRecordsDialog.vue'
import MailDomainActionConfirm from '../../components/mail/MailDomainActionConfirm.vue'
import MailDomainCreateForm from '../../components/mail/MailDomainCreateForm.vue'
import MailDomainsFilterSheet from '../../components/mail/MailDomainsFilterSheet.vue'
import MailDomainsTable from '../../components/mail/MailDomainsTable.vue'
import MailServerGate from '../../components/mail/MailServerGate.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useMailChoicesStore } from '../../stores/mailChoices'
import { useMailDomainsStore } from '../../stores/mailDomains'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type {
  CreateMailDomainRequest,
  MailDomain,
  MailDomainAction,
  PendingMailDomainAction,
} from '../../types/mailDomain'

const { t } = useI18n()
const store = useMailDomainsStore()
const choicesStore = useMailChoicesStore()
const labels = useListLabels()
const { isAdmin, isCustomer } = useCallerRole()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()

const table = useServerTable<MailDomain>({
  filters: { account: accountSpec, status: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<MailDomain>> => {
    return store.list(request)
  },
})

/** The create form, so an accepted create can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The domain and the action awaiting confirmation, or `null` when none is. */
const pending: Ref<PendingMailDomainAction | null> = ref(null)

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
  if (table.filters.value.account !== '') {
    applied.push(labels.chip('account', t('mail.domains.filters.account'), accountFilterName.value))
  }
  const status = table.filters.value.status
  if (status !== '') {
    const named = choicesStore.choices?.domainStatuses.find((choice) => {
      return choice.value === status
    })
    applied.push(labels.chip('status', t('mail.domains.filters.status'), named?.label ?? status))
  }
  return applied
})

/**
 * Adds a domain, then reads the page again: where the new row falls is the server's sort to say.
 * @param request The account and the name the form collected.
 * @returns Resolves once the create and the re-read have settled.
 */
const create = async (request: CreateMailDomainRequest): Promise<void> => {
  if (await store.create(request)) {
    notice.value = t('mail.domains.created', { name: request.name })
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Starts an action on one row, which then waits for confirmation.
 * @param domain The row the person acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (domain: MailDomain, action: MailDomainAction): void => {
  pending.value = { domain, action }
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
  const awaiting = pending.value
  if (awaiting === null) {
    return
  }
  if (awaiting.action === 'remove') {
    if (await store.remove(awaiting.domain.id)) {
      await table.reload()
    }
  } else if (await store.rotateDkim(awaiting.domain.id)) {
    notice.value = t('mail.domains.rotated', { name: awaiting.domain.name })
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

/**
 * Says that mail was turned on from the gate above the form.
 * @returns Nothing.
 */
const serverEnabled = (): void => {
  notice.value = t('mail.server.enabled')
}

/**
 * Opens the DNS records dialog of one domain.
 * @param domain The row the person acted on.
 * @returns Resolves once the records were read.
 */
const openRecords = (domain: MailDomain): Promise<void> => {
  return store.openRecords(domain)
}

onMounted(() => {
  void choicesStore.load()
})

// The records dialog belongs to this page; the store outlives it.
onBeforeUnmount((): void => {
  store.closeRecords()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading :title="t('mail.domains.heading')" :subtitle="t('mail.domains.subtitle')" />

    <!-- What needs attention goes first; the backend's own sentence, verbatim. -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">{{ store.createErrorMessage }}</UiAlert>
    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <MailServerGate @enabled="serverEnabled">
      <MailDomainCreateForm
        ref="form"
        :account-picker="accountPicker"
        :fixed-account-id="fixedAccountId"
        :submitting="store.creating"
        @search-accounts="searchAccounts"
        @submit="create"
      />
    </MailServerGate>

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('mail.domains.emptyTitle')"
      :description="t('mail.domains.emptyDescription')"
    >
      <template #icon><UiIcon name="mail" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('mail.domains.searchLabel')"
            :placeholder="t('mail.domains.searchPlaceholder')"
            :clear-label="t('mail.domains.searchClear')"
          />
          <UiFilterTrigger
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
            v-if="answeredNothing"
            :title="t('mail.domains.noResultsTitle')"
            :description="t('mail.domains.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <MailDomainsTable
              :rows="store.domains"
              :show-owner="isAdmin"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
              @act="ask"
              @records="openRecords"
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

    <MailDomainsFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      :show-account="!isCustomer"
      :account-picker="accountPicker"
      :account-name="accountFilterName"
      :statuses="choicesStore.choices?.domainStatuses ?? []"
      @apply="table.setFilters"
      @close="closeFilters"
      @search="searchAccounts"
    />

    <MailDomainActionConfirm
      v-if="pending !== null"
      :pending="pending"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirm"
    />

    <MailDnsRecordsDialog v-if="store.recordsDomain !== null" :domain="store.recordsDomain" @close="store.closeRecords" />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
