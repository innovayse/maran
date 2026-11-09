<script setup lang="ts">
/**
 * The mailboxes screen: the create form, then one server page of the mailboxes at a time, searched,
 * sorted and narrowed by account, domain and state by the panel. Renders a `<section>`, not a
 * `<main>` — the single landmark lives in the layout. Rows come from the mailboxes store; the page
 * never touches the API layer (rules/vue.md).
 *
 * **The URL is the list's state** (`useServerTable`): page, page size, search, sort, `?account=`,
 * `?domain=` (a domain's name) and `?status=` live in the query. `?account=<id>` is read through
 * `useAccountFilter().spec`, so a customer's list ignores it and the account filter is not offered
 * to them.
 *
 * **Two kinds of nothing.** No mailboxes at all is the empty state; a search or filter matching
 * none is the no-results state, with the way back to every mailbox. A server's error is rendered
 * verbatim and no table is drawn beside it.
 *
 * **Passwords are typed by the owner and never shown back**: the panel keeps a hash and answers
 * with none, so this page holds no credential to forget on unmount.
 *
 * **The plan's mailbox allowance is advice** (`usePlanLimit`): when it is used up the create form
 * is withheld and a sentence says why; the endpoint refuses a create past the plan on its own.
 */
import { computed, onMounted, ref, toRef, useTemplateRef, type ComputedRef, type Ref } from 'vue'
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
import MailboxCreateForm from '../../components/mail/MailboxCreateForm.vue'
import MailboxEditDialog from '../../components/mail/MailboxEditDialog.vue'
import MailboxPasswordDialog from '../../components/mail/MailboxPasswordDialog.vue'
import MailboxRemoveConfirm from '../../components/mail/MailboxRemoveConfirm.vue'
import MailboxesFilterSheet from '../../components/mail/MailboxesFilterSheet.vue'
import MailboxesTable from '../../components/mail/MailboxesTable.vue'
import { useAccountChoice } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useListLabels } from '../../composables/useListLabels'
import { useMailDomainChoice } from '../../composables/useMailDomainChoice'
import { usePlanLimit } from '../../composables/usePlanLimit'
import { useServerTable } from '../../composables/useServerTable'
import { useMailboxesStore } from '../../stores/mailboxes'
import { useMailChoicesStore } from '../../stores/mailChoices'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { CreateMailboxRequest, Mailbox, MailboxAction, PendingMailboxAction } from '../../types/mailbox'

const { t } = useI18n()
const store = useMailboxesStore()
const choicesStore = useMailChoicesStore()
const labels = useListLabels()
const { isAdmin, isCustomer } = useCallerRole()
/** Whether the plan still allows another mailbox; advice, the endpoint enforces. */
const { reached: planLimitReached, reason: planLimitReason } = usePlanLimit('mailboxes', toRef(store, 'mailboxes'))
const { picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { picker: createDomainPicker, search: searchCreateDomains } = useMailDomainChoice('id')
const { picker: filterDomainPicker, search: searchFilterDomains } = useMailDomainChoice('name')
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()

const table = useServerTable<Mailbox>({
  filters: { account: accountSpec, domain: {}, status: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<Mailbox>> => {
    return store.list(request)
  },
})

/** The create form, so an accepted create can empty the fields it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The mailbox and the dialog opened for it, or `null` when none is. */
const pending: Ref<PendingMailboxAction | null> = ref(null)

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
    applied.push(labels.chip('account', t('mail.mailboxes.filters.account'), accountFilterName.value))
  }
  if (filters.domain !== '') {
    applied.push(labels.chip('domain', t('mail.mailboxes.filters.domain'), filters.domain))
  }
  if (filters.status !== '') {
    const named = choicesStore.choices?.mailboxStatuses.find((choice) => {
      return choice.value === filters.status
    })
    applied.push(labels.chip('status', t('mail.mailboxes.filters.status'), named?.label ?? filters.status))
  }
  return applied
})

/**
 * Creates a mailbox, then reads the page again: where the new row falls is the server's sort to say.
 * @param request The domain, local part, password and quota the form collected.
 * @returns Resolves once the create and the re-read have settled.
 */
const create = async (request: CreateMailboxRequest): Promise<void> => {
  if (await store.create(request)) {
    notice.value = t('mail.mailboxes.created')
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Opens the dialog for one row.
 * @param mailbox The row the person acted on.
 * @param action Which dialog they asked for.
 * @returns Nothing.
 */
const ask = (mailbox: Mailbox, action: MailboxAction): void => {
  store.clearActionError()
  pending.value = { mailbox, action }
}

/**
 * Closes the open dialog without a change.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Closes the open dialog after the panel accepted its change, and says so.
 * @param messageKey The locale key of the success sentence, which names the mailbox as `{name}`.
 * @returns Nothing.
 */
const saved = (messageKey: string): void => {
  const awaiting = pending.value
  if (awaiting !== null) {
    notice.value = t(messageKey, { name: awaiting.mailbox.address })
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
  if (await store.remove(awaiting.mailbox.id)) {
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
    <UiPageHeading :title="t('mail.mailboxes.heading')" :subtitle="t('mail.mailboxes.subtitle')" />

    <!-- What needs attention goes first; the backend's own sentence, verbatim. -->
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">{{ store.createErrorMessage }}</UiAlert>
    <UiAlert v-if="store.actionErrorMessage !== null && pending === null" variant="error">
      {{ store.actionErrorMessage }}
    </UiAlert>

    <UiAlert v-if="planLimitReason !== null" variant="info">{{ planLimitReason }}</UiAlert>
    <MailboxCreateForm
      v-if="!planLimitReached"
      ref="form"
      :domain-picker="createDomainPicker"
      :limits="choicesStore.choices?.limits ?? null"
      :submitting="store.creating"
      @search-domains="searchCreateDomains"
      @submit="create"
    />

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('mail.mailboxes.emptyTitle')"
      :description="t('mail.mailboxes.emptyDescription')"
    >
      <template #icon><UiIcon name="mail" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('mail.mailboxes.searchLabel')"
            :placeholder="t('mail.mailboxes.searchPlaceholder')"
            :clear-label="t('mail.mailboxes.searchClear')"
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
            :title="t('mail.mailboxes.noResultsTitle')"
            :description="t('mail.mailboxes.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <MailboxesTable
              :rows="store.mailboxes"
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

    <MailboxesFilterSheet
      :open="filtersOpen"
      :filters="table.filters.value"
      :show-account="!isCustomer"
      :account-picker="accountPicker"
      :account-name="accountFilterName"
      :domain-picker="filterDomainPicker"
      :statuses="choicesStore.choices?.mailboxStatuses ?? []"
      @apply="table.setFilters"
      @close="closeFilters"
      @search="searchAccounts"
      @search-domains="searchFilterDomains"
    />

    <MailboxEditDialog
      v-if="pending !== null && pending.action === 'edit'"
      :mailbox="pending.mailbox"
      :limits="choicesStore.choices?.limits ?? null"
      @close="cancel"
      @saved="saved('mail.mailboxes.edit.saved')"
    />

    <MailboxPasswordDialog
      v-if="pending !== null && pending.action === 'password'"
      :mailbox="pending.mailbox"
      :limits="choicesStore.choices?.limits ?? null"
      @close="cancel"
      @saved="saved('mail.mailboxes.password.saved')"
    />

    <MailboxRemoveConfirm
      v-if="pending !== null && pending.action === 'remove'"
      :mailbox="pending.mailbox"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirmRemove"
    />

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
