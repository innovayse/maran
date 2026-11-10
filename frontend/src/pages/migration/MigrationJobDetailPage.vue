<script setup lang="ts">
/**
 * One whole-server migration job (issue #197, phase 2): its state and controls, its limits, its accounts with
 * their mapping and progress, the dry run, the DNS helper and the summary. Administrators only. Renders a
 * `<section>`, not a `<main>`. The page never touches the API layer (rules/vue.md).
 *
 * **Everything the page offers is advice.** A draft is mapped, judged by the dry run and started; a running job
 * shows each account's step and offers the cut-over of a synced one, which is the administrator's own decision;
 * the backend refuses an action the state does not allow, and says why in the caller's language.
 *
 * **Progress is read, not pushed.** While the job runs the page reads the job and the current page of accounts
 * again every few seconds; leaving the page stops it.
 *
 * **The credentials the import generated are shown once** in their own dialog, because a source carries none and
 * the panel keeps none after they are claimed.
 */
import { computed, onMounted, onUnmounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiInput from '../../components/ui/UiInput.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiModal from '../../components/ui/UiModal.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiPinnedToast from '../../components/ui/UiPinnedToast.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import MigrationAccountReportDialog from '../../components/migration/MigrationAccountReportDialog.vue'
import MigrationDnsTable from '../../components/migration/MigrationDnsTable.vue'
import MigrationFindingsList from '../../components/migration/MigrationFindingsList.vue'
import MigrationJobAccountsTable from '../../components/migration/MigrationJobAccountsTable.vue'
import MigrationMappingDialog from '../../components/migration/MigrationMappingDialog.vue'
import MigrationStateBadge from '../../components/migration/MigrationStateBadge.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useAccountsStore } from '../../stores/accounts'
import { useMigrationJobsStore } from '../../stores/migrationJobs'
import { takeFlashNotice } from '../../utils/flashNotice'
import type { SelectOption } from '../../components/ui/UiSelect.vue'
import type { ListLoad, ListRequest } from '../../types/list'
import type {
  MigrationAccountAction,
  MigrationJobAction,
  MigrationIssuedSecret,
  MigrationJobAccount,
  MigrationMappingRequest,
} from '../../types/migrationJob'

/** Props accepted by the page: the job from the route. */
const props = defineProps<{
  /** The job's identity. */
  id: string
}>()

/** How often progress is read again while the job runs, in milliseconds. */
const REFRESH_MS = 5000

const { t } = useI18n()
const store = useMigrationJobsStore()
const accountsStore = useAccountsStore()
const labels = useListLabels()

const table = useServerTable<MigrationJobAccount>({
  filters: { state: {} },
  /**
   * Reads one page of the job's accounts through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<MigrationJobAccount>> => {
    return store.listAccounts(props.id, request)
  },
})

/** The success toast's text, or `null`. */
const notice: Ref<string | null> = ref(null)

/** The account being mapped, or `null`. */
const mapping: Ref<MigrationJobAccount | null> = ref(null)

/** The account whose cancellation awaits confirmation, or `null`. */
const cancelling: Ref<MigrationJobAccount | null> = ref(null)

/** Whether the cancellation of the whole job awaits confirmation. */
const cancellingJob: Ref<boolean> = ref(false)

/** The credentials just claimed, or `null` while none are shown. */
const secrets: Ref<MigrationIssuedSecret[] | null> = ref(null)

/** The concurrency being edited, as typed. */
const concurrency: Ref<string> = ref('')

/** The bandwidth limit being edited, as typed. */
const bandwidth: Ref<string> = ref('')

/** The timer that reads progress again, or `null`. */
let timer: ReturnType<typeof setInterval> | null = null

/** The plans offered by the mapping dialog. */
const plans: ComputedRef<SelectOption[]> = computed(() => {
  const page = accountsStore.planChoicePage
  return page.kind === 'loaded'
    ? page.page.items.map((plan) => {
        return { value: plan.id, label: plan.displayName }
      })
    : []
})

/** Whether the job is a draft. */
const isDraft: ComputedRef<boolean> = computed(() => {
  return store.current?.state === 'Draft'
})

/** Whether the job's source is a Maran panel, which needs a pull grant per account. */
const needsGrant: ComputedRef<boolean> = computed(() => {
  return store.current?.sourceUrl !== null && store.current?.sourceUrl !== undefined
})

/** Whether the job has ended. */
const hasEnded: ComputedRef<boolean> = computed(() => {
  return store.current?.state === 'Completed' || store.current?.state === 'Cancelled'
})

/** Whether the latest answer for the accounts arrived and holds no rows. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/**
 * Reads the job and the current page of accounts again.
 * @returns Resolves once both settled.
 */
const refresh = async (): Promise<void> => {
  await store.load(props.id)
  await table.reload()
}

/**
 * Runs a job-level action and shows the page the panel answered with.
 * @param action What to do.
 * @returns Resolves once it settled and the accounts were read again.
 */
const controlJob = async (action: MigrationJobAction): Promise<void> => {
  await store.control(props.id, action)
  cancellingJob.value = false
  await table.reload()
}

/**
 * Saves the limits when both are valid numbers.
 * @returns Resolves once it settled.
 */
const saveLimits = async (): Promise<void> => {
  const concurrent = Number(concurrency.value)
  const limit = bandwidth.value.trim() === '' ? null : Number(bandwidth.value.trim())
  if (!Number.isInteger(concurrent) || concurrent < 1 || concurrent > 8 || (limit !== null && !(Number.isInteger(limit) && limit > 0))) {
    return
  }
  await store.setLimits(props.id, concurrent, limit)
}

/**
 * Opens the mapping dialog for an account, reading the plans first.
 * @param row The account.
 * @returns Resolves once the plans were asked for.
 */
const openMapping = async (row: MigrationJobAccount): Promise<void> => {
  mapping.value = row
  await accountsStore.searchPlanChoices('')
}

/**
 * Saves a mapping.
 * @param request The mapping.
 * @returns Resolves once it settled.
 */
const saveMapping = async (request: MigrationMappingRequest): Promise<void> => {
  const changed = await store.map(props.id, request)
  if (changed !== null) {
    mapping.value = null
    await table.reload()
  }
}

/**
 * Handles a control chosen on an account row: a cancellation is confirmed first.
 * @param row The account.
 * @param action What to do.
 * @returns Resolves once it settled.
 */
const controlAccount = async (row: MigrationJobAccount, action: MigrationAccountAction): Promise<void> => {
  if (action === 'cancel') {
    cancelling.value = row
    return
  }
  await store.controlAccount(props.id, row.id, action)
  await table.reload()
}

/**
 * Carries out the confirmed cancellation of one account.
 * @returns Resolves once it settled.
 */
const confirmCancelAccount = async (): Promise<void> => {
  const row = cancelling.value
  if (row === null) {
    return
  }
  await store.controlAccount(props.id, row.id, 'cancel')
  cancelling.value = null
  await table.reload()
}

/**
 * Claims the generated credentials of an account, once.
 * @param row The account.
 * @returns Resolves once they were shown or refused.
 */
const claimSecrets = async (row: MigrationJobAccount): Promise<void> => {
  secrets.value = await store.claimSecrets(props.id, row.id)
  await table.reload()
}

/**
 * Opens the comparison report of an account.
 * @param row The account.
 * @returns Resolves once it was read.
 */
const openReport = async (row: MigrationJobAccount): Promise<void> => {
  await store.loadReport(props.id, row.id)
}

onMounted(async () => {
  store.clearErrors()
  notice.value = takeFlashNotice()
  await store.load(props.id)
  concurrency.value = String(store.current?.concurrency ?? 2)
  bandwidth.value = store.current?.bandwidthLimitKib === null || store.current === null ? '' : String(store.current.bandwidthLimitKib)
  timer = setInterval(() => {
    if (store.current?.state === 'Running') {
      void refresh()
    }
  }, REFRESH_MS)
})

onUnmounted(() => {
  if (timer !== null) {
    clearInterval(timer)
  }
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="store.currentState">
      <UiEmptyState
        v-if="store.current === null"
        :title="t('migrationJobs.detail.notFoundTitle')"
        :description="t('migrationJobs.detail.notFoundDescription')"
      >
        <UiButton :to="{ name: 'migration-jobs' }">{{ t('migrationJobs.detail.back') }}</UiButton>
      </UiEmptyState>

      <div v-else class="flex min-w-0 flex-col gap-4">
        <UiPageHeading :title="store.current.name" :subtitle="t('migrationJobs.detail.subtitle', { count: store.current.accountCount })">
          <template #actions>
            <MigrationStateBadge :state="store.current.state" :name="store.current.stateName" />
            <UiButton v-if="isDraft" variant="secondary" :disabled="store.busy !== null" @click="store.dryRun(props.id)">
              {{ t('migrationJobs.detail.dryRun') }}
            </UiButton>
            <UiButton v-if="isDraft" :disabled="store.busy !== null" @click="controlJob('start')">{{ t('migrationJobs.detail.start') }}</UiButton>
            <UiButton v-if="store.current.state === 'Running'" variant="secondary" :disabled="store.busy !== null" @click="controlJob('pause')">
              {{ t('migrationJobs.detail.pause') }}
            </UiButton>
            <UiButton v-if="store.current.state === 'Paused'" :disabled="store.busy !== null" @click="controlJob('resume')">
              {{ t('migrationJobs.detail.resume') }}
            </UiButton>
            <UiButton v-if="!hasEnded" variant="destructive" :disabled="store.busy !== null" @click="cancellingJob = true">
              {{ t('migrationJobs.detail.cancel') }}
            </UiButton>
          </template>
        </UiPageHeading>

        <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

        <UiCard v-if="store.dryRunReport !== null">
          <MigrationFindingsList :report="store.dryRunReport" />
        </UiCard>

        <UiCard v-if="!hasEnded">
          <UiSectionHeading :title="t('migrationJobs.limits.title')" :description="t('migrationJobs.limits.description')" />
          <div class="mt-3 flex min-w-0 flex-wrap items-end gap-3">
            <UiInput v-model="concurrency" class="w-40" :label="t('migrationJobs.limits.concurrency')" />
            <UiInput v-model="bandwidth" class="w-56" :label="t('migrationJobs.limits.bandwidth')" />
            <UiButton variant="secondary" :disabled="store.busy !== null" @click="saveLimits">{{ t('migrationJobs.limits.save') }}</UiButton>
          </div>
        </UiCard>

        <UiCard>
          <div class="flex flex-col gap-4">
            <UiSectionHeading :title="t('migrationJobs.accounts.title')" />
            <UiToolbar>
              <UiSearchInput
                v-model="table.search.value"
                class="min-w-55 max-w-85 flex-1"
                :label="t('migrationJobs.accounts.searchLabel')"
                :placeholder="t('migrationJobs.accounts.searchPlaceholder')"
                :clear-label="t('migrationJobs.list.searchClear')"
              />
            </UiToolbar>

            <UiLoadingRegion :state="table.loadingState.value">
              <template #error>
                <UiAlert variant="error">
                  {{ table.state.value.status === 'refused' ? table.state.value.message : t('common.list.unreachable') }}
                </UiAlert>
              </template>

              <UiEmptyState v-if="answeredNothing" :title="t('migrationJobs.accounts.noResultsTitle')" :description="t('migrationJobs.accounts.noResultsDescription')" />

              <div v-else class="flex flex-col gap-4">
                <MigrationJobAccountsTable
                  :rows="store.accounts"
                  :loading="table.state.value.status === 'loading'"
                  :skeleton-rows="table.pageSize.value"
                  :sort="table.sort.value"
                  :editable="isDraft"
                  @sort="table.toggleSort"
                  @map="openMapping"
                  @control="controlAccount"
                  @report="openReport"
                  @secrets="claimSecrets"
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

        <UiCard>
          <UiSectionHeading :title="t('migrationJobs.dns.title')" :description="t('migrationJobs.dns.description')" />
          <div class="mt-3 flex min-w-0 flex-col gap-3">
            <div class="flex flex-wrap items-center gap-2">
              <UiButton variant="secondary" :disabled="store.busy !== null" @click="store.loadDns(props.id)">{{ t('migrationJobs.dns.check') }}</UiButton>
              <UiButton variant="secondary" :disabled="store.busy !== null" @click="store.saveSummary(props.id, false)">{{ t('migrationJobs.detail.summaryCsv') }}</UiButton>
              <UiButton variant="secondary" :disabled="store.busy !== null" @click="store.saveSummary(props.id, true)">{{ t('migrationJobs.detail.summaryJson') }}</UiButton>
            </div>
            <MigrationDnsTable v-if="store.dnsAdvice !== null" :advice="store.dnsAdvice" />
          </div>
        </UiCard>
      </div>
    </UiLoadingRegion>

    <MigrationMappingDialog
      :account="mapping"
      :plans="plans"
      :needs-grant="needsGrant"
      :saving="store.busy === 'mapping'"
      :error-message="store.actionErrorMessage"
      @save="saveMapping"
      @close="mapping = null"
    />

    <MigrationAccountReportDialog :report="store.accountReport" @close="store.accountReport = null" />

    <UiConfirm
      :open="cancelling !== null"
      :title="t('migrationJobs.confirm.accountTitle', { name: cancelling?.sourceAccountName ?? '' })"
      :question="t('migrationJobs.confirm.accountQuestion')"
      :confirm-label="t('migrationJobs.confirm.accountAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.busy === 'account'"
      :acting-label="t('common.saving')"
      @close="cancelling = null"
      @confirm="confirmCancelAccount"
    />

    <UiConfirm
      :open="cancellingJob"
      :title="t('migrationJobs.confirm.jobTitle', { name: store.current?.name ?? '' })"
      :question="t('migrationJobs.confirm.jobQuestion')"
      :confirm-label="t('migrationJobs.confirm.jobAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.busy === 'cancel'"
      :acting-label="t('common.saving')"
      @close="cancellingJob = false"
      @confirm="controlJob('cancel')"
    />

    <UiModal
      :open="secrets !== null"
      :title="t('migrationJobs.secrets.title')"
      :close-label="t('common.close')"
      :dismissible="false"
      @close="secrets = null"
    >
      <div class="flex min-w-0 flex-col gap-2" data-testid="issued-secrets">
        <p class="text-sm text-text-secondary">{{ t('migrationJobs.secrets.intro') }}</p>
        <ul class="flex flex-col gap-1">
          <li v-for="secret in secrets ?? []" :key="secret.subject" class="text-sm">
            <span class="font-semibold">{{ secret.subject }}</span>
            <code class="ms-2 select-all font-mono">{{ secret.secret }}</code>
          </li>
        </ul>
      </div>
      <template #footer>
        <UiButton @click="secrets = null">{{ t('migrationJobs.secrets.done') }}</UiButton>
      </template>
    </UiModal>

    <UiPinnedToast :message="notice" :close-label="t('common.close')" @dismiss="notice = null" />
  </section>
</template>
