<script setup lang="ts">
/**
 * The FTPS logins screen: the administrator's FTPS service card, the create form, then one server
 * page of the FTPS logins at a time, searched, sorted and narrowed to an account by the panel.
 * Renders a `<section>`, not a `<main>` — the single landmark lives in the layout. Rows come from the
 * FTPS logins store; the page never touches the API layer (rules/vue.md).
 *
 * **One module, one page.** SFTP and FTPS logins are different system users answered by different
 * modules, so each has its own page under the "File transfer" submenu; this one lists the Ftp
 * module's logins and nothing else, and no list here is assembled from two endpoints. The service
 * card lives here because the daemon it switches is the one these logins sign in to.
 *
 * The list, its URL state, its two kinds of nothing and the once-shown password behave exactly as on
 * the SFTP logins page (`SftpUsersListPage.vue` states why).
 *
 * **Every FTPS switch answers**, through this page's toast, in words the panel's measured state
 * chooses (`useFtpsServerSwitch` holds the reasoning). The status and the firewall are
 * administrator-only reads, so they are only asked for an administrator.
 *
 * **A refusal is not a failure, and this screen does not pretend to tell them apart**: the wire
 * carries no code for "the account is busy", so the backend's sentence is rendered verbatim.
 */
import { computed, onBeforeUnmount, onMounted, ref, useTemplateRef, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiCombobox from '../../components/ui/UiCombobox.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterSheet from '../../components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import FtpUserActionConfirm from '../../components/ftp/FtpUserActionConfirm.vue'
import FtpUsersTable from '../../components/ftp/FtpUsersTable.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import type { ToastVariant } from '../../components/ui/UiToast.vue'
import FtpUserCreateForm from '../../components/ftp/FtpUserCreateForm.vue'
import FtpUserCreatedDialog from '../../components/ftp/FtpUserCreatedDialog.vue'
import FtpsServerPanel from '../../components/ftp/FtpsServerPanel.vue'
import { useAccountChoice, type AccountPickerState } from '../../composables/useAccountChoice'
import { useAccountFilter } from '../../composables/useAccountFilter'
import { useCallerRole } from '../../composables/useCallerRole'
import { useFtpsServerSwitch } from '../../composables/useFtpsServerSwitch'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useFirewallStore } from '../../stores/firewall'
import { useFtpUsersStore } from '../../stores/ftpUsers'
import { useFtpsServerStore } from '../../stores/ftpsServer'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { CreateFtpUserRequest, FtpUser, FtpUserAction } from '../../types/ftpUser'


const { t } = useI18n()
const store = useFtpUsersStore()
const ftpsServerStore = useFtpsServerStore()
const firewallStore = useFirewallStore()
const labels = useListLabels()
const { isAdmin, isCustomer } = useCallerRole()
const { fixedAccountId, picker: accountPicker, search: searchAccounts } = useAccountChoice()
const { spec: accountSpec, name: accountFilterName } = useAccountFilter()

const table = useServerTable<FtpUser>({
  filters: { account: accountSpec },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<FtpUser>> => {
    return store.list(request)
  },
})

/** The create form, so an accepted create can empty the field it owns. */
const form = useTemplateRef<{ reset: () => void }>('form')

/** The toast's text after a create or an FTPS switch, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/**
 * The toast's tone: success when the outcome is what was asked for, warning when the panel measured
 * something else afterwards.
 */
const noticeVariant: Ref<ToastVariant> = ref('success')

/** The service card's orchestration; a switch's outcome is announced through the page's toast. */
const ftps = useFtpsServerSwitch(
  /**
   * Shows how a switch ended.
   * @param text The sentence, already translated.
   * @param agreed Whether the measured state agrees with the switch; a disagreement is a warning.
   * @returns Nothing.
   */
  (text: string, agreed: boolean): void => {
    noticeVariant.value = agreed ? 'success' : 'warning'
    notice.value = text
  },
)

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The login whose action is awaiting confirmation, or `null` when none is. */
const pending: Ref<FtpUser | null> = ref(null)

/** Which action that login is waiting on. */
const pendingAction: Ref<FtpUserAction> = ref('remove')

/** Whether the list is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** The account filter's picker, after an "any account" choice counted into the total. */
const accountFilterPicker: ComputedRef<AccountPickerState> = computed(() => {
  const state = accountPicker.value
  return {
    ...state,
    options: [{ value: '', label: t('common.list.any') }, ...state.options],
    total: state.total + 1,
  }
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  return table.filters.value.account !== ''
    ? [labels.chip('account', t('ftp.list.filters.account'), accountFilterName.value)]
    : []
})

/**
 * Creates a login, then reads the page again: where the new row falls is the server's sort to say.
 * @param request The account and the name the form collected.
 * @returns Resolves once the create and the re-read have settled.
 */
const create = async (request: CreateFtpUserRequest): Promise<void> => {
  if (await store.create(request)) {
    noticeVariant.value = 'success'
    notice.value = t('ftp.list.created', { name: request.name })
    form.value?.reset()
    await table.reload()
  }
}

/**
 * Starts an action on one row, which then waits for confirmation.
 * @param login The row the operator acted on.
 * @param action Which action they started.
 * @returns Nothing.
 */
const ask = (login: FtpUser, action: FtpUserAction): void => {
  pending.value = login
  pendingAction.value = action
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
  const login = pending.value
  if (login === null) {
    return
  }
  if (pendingAction.value === 'remove') {
    if (await store.remove(login.id)) {
      await table.reload()
    }
  } else {
    await store.resetPassword(login.id)
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

// The administrator's two reads are only attempted for an administrator: both endpoints answer 403
// otherwise. The FTPS status is also what the credential dialog names the host and port from.
onMounted(() => {
  if (isAdmin.value) {
    void ftps.load()
  }
})

// The credential must not survive a navigation. The store outlives this page.
onBeforeUnmount(store.dismissCredential)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :title="t('ftp.list.heading')"
      :subtitle="t('ftp.list.subtitle')"
      :note="t('ftp.list.prefixNote')"
    />

    <!-- What needs attention goes first; the backend's own sentence, verbatim. -->
    <UiAlert v-if="ftps.loadFailureText.value !== null" variant="error">{{ ftps.loadFailureText.value }}</UiAlert>
    <UiAlert v-if="store.createErrorMessage !== null" variant="error">{{ store.createErrorMessage }}</UiAlert>
    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

    <FtpsServerPanel
      v-if="isAdmin && ftpsServerStore.isDisclosed && ftpsServerStore.status !== null"
      :status="ftpsServerStore.status"
      :rules="firewallStore.rules"
      :acting="ftpsServerStore.acting || firewallStore.acting"
      :failure="ftpsServerStore.actionFailure"
      @enable="ftps.enable"
      @disable="ftps.disable"
      @open-ports="ftps.openPorts"
      @close-ports="ftps.closePorts"
    />

    <FtpUserCreateForm
      ref="form"
      :account-picker="accountPicker"
      :fixed-account-id="fixedAccountId"
      :submitting="store.creating"
      :ftps-availability="ftps.availability.value"
      @search-accounts="searchAccounts"
      @submit="create"
    />

    <UiEmptyState
      v-if="answeredNothing && !isNarrowed"
      :title="t('ftp.list.emptyTitle')"
      :description="t('ftp.list.emptyDescription')"
    >
      <template #icon><UiIcon name="folderKey" size="lg" /></template>
    </UiEmptyState>

    <UiCard v-else>
      <div class="flex flex-col gap-4">
        <div class="flex flex-wrap items-end gap-3">
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('ftp.list.searchLabel')"
            :placeholder="t('ftp.list.searchPlaceholder')"
            :clear-label="t('ftp.list.searchClear')"
          />
          <!-- The account is the list's one filter, and a customer's list is already their own. -->
          <UiFilterTrigger
            v-if="!isCustomer"
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
            :title="t('ftp.list.noResultsTitle')"
            :description="t('ftp.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <FtpUsersTable
              :rows="store.ftpUsers"
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

    <UiFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      :title="t('common.list.filters')"
      :close-label="t('common.close')"
      :apply-label="t('common.list.apply')"
      :reset-label="t('common.list.reset')"
      @update:model-value="table.setFilters"
      @close="closeFilters"
    >
      <template #default="{ draft }">
        <UiCombobox
          v-model="draft.account"
          v-bind="accountFilterPicker"
          :label="t('ftp.list.filters.account')"
          :placeholder="t('common.list.any')"
          :selected-label="accountFilterName"
          @search="searchAccounts"
        />
      </template>
    </UiFilterSheet>

    <FtpUserActionConfirm
      :login="pending"
      :action="pendingAction"
      :acting="store.acting"
      @close="cancel"
      @confirm="confirm"
    />

    <FtpUserCreatedDialog
      :open="store.revealedCredential !== null"
      :full-name="store.revealedCredential?.fullName ?? ''"
      :password="store.revealedCredential?.password ?? ''"
      :hostname="ftpsServerStore.status?.hostname ?? null"
      :control-port="ftpsServerStore.status?.controlPort ?? null"
      :certificate-is-self-signed="ftpsServerStore.status?.certificateIsSelfSigned ?? false"
      @close="store.dismissCredential"
    />

    <UiPinnedToast :message="notice" :variant="noticeVariant" :close-label="t('common.close')" @dismiss="dismissNotice" />
  </section>
</template>
