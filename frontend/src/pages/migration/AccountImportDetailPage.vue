<script setup lang="ts">
/**
 * One account import, from the accepted package to how it ended (issue #127). The screen follows the
 * import's state, which the panel owns:
 *
 * - **Staged:** the package is verified and waits. The administrator chooses the plan (and reseller),
 *   runs the dry run, reads every module's findings, retypes the account name and the key fingerprint,
 *   and starts the job, or discards the package.
 * - **Running:** the job creates the account; this page reads the import again every few seconds and
 *   shows the step it is at.
 * - **Completed, rolled back, discarded:** the outcome, with the generated credentials fetched once.
 *
 * The page owns the import's read, the polling, the chosen plan and reseller and the notices; the cards
 * are dumb. An import that does not exist is the not-found state, not an alert. The credentials are held
 * by the store until their dialog is closed, and the page drops them when it is left. Renders a
 * `<section>`, not a `<main>`.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import AccountImportConfirmStep from '../../components/migration/AccountImportConfirmStep.vue'
import AccountImportOutcome from '../../components/migration/AccountImportOutcome.vue'
import AccountImportPlanStep from '../../components/migration/AccountImportPlanStep.vue'
import AccountImportProgress from '../../components/migration/AccountImportProgress.vue'
import AccountImportSummary from '../../components/migration/AccountImportSummary.vue'
import IssuedSecretsDialog from '../../components/migration/IssuedSecretsDialog.vue'
import { usePlanChoice } from '../../composables/usePlanChoice'
import { useResellerChoice } from '../../composables/useResellerChoice'
import { useAccountImportsStore } from '../../stores/accountImports'
import type { AccountImport, MigrationFinding } from '../../types/accountMove'

/** How often a running import is read again, in milliseconds. A UI concern: often enough to watch, rarely enough to be cheap. */
const PROGRESS_POLL_MS = 2000

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The import's identity, from `/migration/account-import/:id`. */
  id: string
}>()

const { t } = useI18n()
const store = useAccountImportsStore()
// The plan picker offers the chosen reseller's plans; the closure reads `resellerId` when a search runs,
// which is after setup, so the reactive state below it is declared by then.
const { picker: planPicker, search: searchPlans } = usePlanChoice((): string | null => {
  return resellerId.value === '' ? null : resellerId.value
})
const { picker: resellerPicker, search: searchResellers } = useResellerChoice()

/** The plan chosen for the dry run and the start; empty before a choice. */
const planId: Ref<string> = ref('')

/** The reseller chosen to own the account; empty for the administrator's own. */
const resellerId: Ref<string> = ref('')

/** The plan the report on screen was run for, so a changed plan cannot start on another plan's report. */
const judgedPlanId: Ref<string> = ref('')

/** Whether the discard confirmation is open. */
const confirmingDiscard: Ref<boolean> = ref(false)

/** The pending read of a running import, or `null` while none is scheduled. */
const pollTimer: Ref<ReturnType<typeof setTimeout> | null> = ref(null)

/** The import on screen once the panel has answered with it; an import the store holds from an earlier visit is not drawn. */
const accountImport: ComputedRef<AccountImport | null> = computed(() => {
  return store.current !== null && store.current.id === props.id ? store.current : null
})

/** Whether the import does not exist: the read finished and found nothing. */
const missing: ComputedRef<boolean> = computed(() => {
  return store.currentState === 'loaded' && accountImport.value === null
})

/** Whether the report on screen is the one for the plan now chosen and holds no conflict. */
const canStart: ComputedRef<boolean> = computed(() => {
  return store.report !== null && store.report.canStart && judgedPlanId.value === planId.value && planId.value !== ''
})

/** The findings worth showing after the import ended: everything the dry run said that is not a conflict. */
const afterFindings: ComputedRef<MigrationFinding[]> = computed(() => {
  return (store.report?.findings ?? []).filter((finding) => {
    return finding.severity !== 'Conflict'
  })
})

/**
 * Runs the dry run for the chosen plan.
 * @returns Resolves once the request has settled.
 */
const runReport = async (): Promise<void> => {
  const chosen = planId.value
  if (await store.loadReport(props.id, chosen)) {
    judgedPlanId.value = chosen
  }
}

/**
 * Starts the import with the chosen plan and the administrator's retyped confirmations.
 * @param confirmAccountName The account name as typed.
 * @param confirmKeyFingerprint The key fingerprint as typed.
 * @returns Resolves once the request has settled.
 */
const startImport = async (confirmAccountName: string, confirmKeyFingerprint: string): Promise<void> => {
  await store.start(props.id, {
    planId: planId.value,
    resellerId: resellerId.value === '' ? null : resellerId.value,
    confirmAccountName,
    confirmKeyFingerprint,
  })
}

/**
 * Opens the discard confirmation.
 * @returns Nothing.
 */
const askDiscard = (): void => {
  confirmingDiscard.value = true
}

/**
 * Abandons the discard confirmation.
 * @returns Nothing.
 */
const cancelDiscard = (): void => {
  confirmingDiscard.value = false
}

/**
 * Discards the package and closes the confirmation.
 * @returns Resolves once the request has settled.
 */
const confirmDiscard = async (): Promise<void> => {
  await store.discard(props.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the spinner
  // that tells the person the panel is working.
  confirmingDiscard.value = false
}

/**
 * Fetches the generated credentials, which the store then holds for the dialog.
 * @returns Resolves once the request has settled.
 */
const revealSecrets = async (): Promise<void> => {
  await store.claimSecrets(props.id)
}

/**
 * Closes the credentials dialog and forgets them.
 * @returns Nothing.
 */
const closeSecrets = (): void => {
  store.dismissSecrets()
}

/**
 * Cancels the scheduled read of a running import.
 * @returns Nothing.
 */
const stopPolling = (): void => {
  if (pollTimer.value !== null) {
    clearTimeout(pollTimer.value)
    pollTimer.value = null
  }
}

/**
 * Schedules the next read of a running import; the read schedules the one after it for as long as the
 * import still runs.
 * @returns Nothing.
 */
const schedulePoll = (): void => {
  stopPolling()
  pollTimer.value = setTimeout(async () => {
    await store.refreshQuietly(props.id)
    if (accountImport.value?.state === 'Running') {
      schedulePoll()
    }
  }, PROGRESS_POLL_MS)
}

// A changed reseller offers other plans; a plan chosen among the old ones means nothing against them.
watch(resellerId, (): void => {
  planId.value = ''
})

// Polling follows the state, so a job started on this page, or one already running when it opens, is watched.
watch(
  (): string | undefined => {
    return accountImport.value?.state
  },
  (state): void => {
    if (state === 'Running') {
      schedulePoll()
    } else {
      stopPolling()
    }
  },
  { immediate: true },
)

onMounted(async () => {
  store.clearErrors()
  await Promise.all([store.loadOne(props.id), searchPlans(''), searchResellers('')])
})

// The credentials are the one copy there will ever be; they must not stay in memory once nobody can see them.
onBeforeUnmount(() => {
  stopPolling()
  store.dismissSecrets()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="store.currentState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState
        v-if="missing"
        :title="t('accountMove.import.detail.notFoundTitle')"
        :description="t('accountMove.import.detail.notFoundDescription')"
      >
        <UiLink :to="{ name: 'account-import' }" icon="chevronLeft">{{ t('accountMove.import.detail.backToForm') }}</UiLink>
      </UiEmptyState>

      <div v-else class="flex min-w-0 flex-col gap-4">
        <UiPageHeading
          :title="t('accountMove.import.detail.heading', { name: accountImport?.accountName ?? '' })"
          :subtitle="t('accountMove.import.detail.subtitle', { host: accountImport?.sourceHostName ?? '' })"
          :loading="accountImport === null"
        >
          <template #actions>
            <UiLink :to="{ name: 'account-import' }" icon="chevronLeft">{{ t('accountMove.import.detail.backToForm') }}</UiLink>
          </template>
        </UiPageHeading>

        <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>

        <template v-if="accountImport !== null">
          <UiCard>
            <AccountImportSummary :account-import="accountImport" />
          </UiCard>

          <template v-if="accountImport.state === 'Staged'">
            <UiCard>
              <AccountImportPlanStep
                v-model:plan-id="planId"
                v-model:reseller-id="resellerId"
                :plan-picker="planPicker"
                :reseller-picker="resellerPicker"
                :report="store.report"
                :running="store.busy === 'report'"
                @search-plans="searchPlans"
                @search-resellers="searchResellers"
                @run="runReport"
              />
            </UiCard>
            <UiCard>
              <AccountImportConfirmStep
                :account-name="accountImport.accountName"
                :key-fingerprint="accountImport.keyFingerprint"
                :can-start="canStart"
                :busy="store.busy"
                @start="startImport"
                @discard="askDiscard"
              />
            </UiCard>
          </template>

          <UiCard v-else-if="accountImport.state === 'Running'">
            <AccountImportProgress :stage="accountImport.stage" :account-name="accountImport.accountName" />
          </UiCard>

          <UiCard v-else>
            <AccountImportOutcome
              :account-import="accountImport"
              :findings="afterFindings"
              :busy="store.busy"
              @reveal="revealSecrets"
            />
          </UiCard>
        </template>
      </div>
    </UiLoadingRegion>

    <UiConfirm
      :open="confirmingDiscard"
      :title="t('accountMove.import.confirm.discardTitle')"
      :question="t('accountMove.import.confirm.discardQuestion')"
      :confirm-label="t('accountMove.import.confirm.discardAction')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.busy === 'discard'"
      :acting-label="t('common.saving')"
      destructive
      @close="cancelDiscard"
      @confirm="confirmDiscard"
    />

    <IssuedSecretsDialog v-if="store.secrets !== null" :secrets="store.secrets" @close="closeSecrets" />
  </section>
</template>
