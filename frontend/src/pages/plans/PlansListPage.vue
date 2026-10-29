<script setup lang="ts">
/**
 * Plans screen: the list of every plan with its limits, and the way to define, edit and delete
 * them. Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout
 * this page is nested under. State comes exclusively from the plans store and the page never
 * touches the API layer (rules/vue.md: API composables are called from stores only).
 *
 * **Administrators only, by the endpoints and not by this page.** A customer who reaches the
 * screen gets the panel's own refusal rendered in place of the table; no role check here would be
 * more than a second copy of a decision the server already makes (rules/architecture.md).
 *
 * **The figures are figures.** Every limit is a number right-aligned in the monospace face, so a
 * column lines up on the digit and "25600" is not read as "2560". Zero is only ever a real
 * product for FTPS logins and cron entries (a plan that sells none), and there it is printed as the
 * word "None" rather than as a bare `0` that could be read as a missing value. The disk is in
 * megabytes because that is the unit the server states and the form takes; converting it here would
 * show the administrator a different number from the one they type.
 *
 * **Delete asks first and the server has the last word.** The dialog names the plan and the
 * consequence. When accounts are on the plan it says the panel will refuse, but it still lets the
 * request go: the count here may be a moment old, and the refusal — the server's own sentence,
 * with the remedy in it — is what the administrator reads, rendered unchanged above the table.
 *
 * Laid out as the redesign's list screens are: the heading block with its eyebrow and the
 * primary pill, then one panel whose table scrolls inside its own container so the page never
 * moves sideways on a phone.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiTableCell from '../../components/ui/UiTableCell.vue'
import UiTableHeaderCell from '../../components/ui/UiTableHeaderCell.vue'
import UiTableRow from '../../components/ui/UiTableRow.vue'
import { usePlansStore } from '../../stores/plans'
import type { ManagedPlan } from '../../types/plan'

const { t } = useI18n()
const store = usePlansStore()

/** The plan whose deletion awaits confirmation, or `null` when none does. */
const pending: Ref<ManagedPlan | null> = ref(null)

/** Whether the panel answered successfully and reported no plans at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.plans.length === 0
})

/** The consequence the confirmation asks the administrator to weigh, for the plan it names. */
const confirmationText: ComputedRef<string> = computed(() => {
  if (pending.value === null) {
    return ''
  }
  return pending.value.accountCount > 0
    ? t('plans.list.deleteQuestionInUse', pending.value.accountCount)
    : t('plans.list.deleteQuestionFree')
})

/**
 * Prints a count that may legitimately be zero: FTPS logins and cron entries, where zero means
 * the plan sells none.
 * @param figure The limit as the server states it.
 * @returns The figure, or the word for "none" when it is zero.
 */
const optionalCount = (figure: number): string => {
  return figure === 0 ? t('plans.list.none') : String(figure)
}

/**
 * Loads the plans.
 * @returns Resolves once the request has settled, successfully or not.
 */
const refresh = async (): Promise<void> => {
  store.clearErrors()
  await store.load()
}

/**
 * Starts a deletion, which then waits for the administrator's answer.
 * @param plan The plan they asked to delete.
 * @returns Nothing.
 */
const ask = (plan: ManagedPlan): void => {
  store.clearErrors()
  pending.value = plan
}

/**
 * Abandons the pending deletion.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed deletion.
 * @returns Resolves once the request has settled; a refusal stays in the store for the template.
 */
const confirm = async (): Promise<void> => {
  if (pending.value === null) {
    return
  }
  await store.remove(pending.value.id)
  // Closed after the request settles rather than before it is sent: the dialog holds the spinner
  // that tells the administrator the panel is working.
  pending.value = null
}

onMounted(refresh)
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiPageHeading
      :eyebrow="t('plans.list.eyebrow')"
      :title="t('plans.list.heading')"
      :subtitle="t('plans.list.subtitle')"
    >
      <template #actions>
        <UiButton :to="{ name: 'plans-new' }">{{ t('plans.list.createAction') }}</UiButton>
      </template>
    </UiPageHeading>

    <!-- A refused deletion is the one thing that needs attention, and it is the server's own
         sentence: what is in the way and what to do about it. -->
    <UiAlert v-if="store.deleteErrorMessage !== null" variant="error">
      {{ store.deleteErrorMessage }}
    </UiAlert>

    <UiSpinner v-if="store.loading && !store.isLoaded" :label="t('plans.list.loading')" />

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else-if="isEmpty"
      :title="t('plans.list.emptyTitle')"
      :description="t('plans.list.emptyDescription')"
    >
      <template #icon><UiIcon name="package" size="lg" /></template>
      <UiButton :to="{ name: 'plans-new' }">{{ t('plans.list.createAction') }}</UiButton>
    </UiEmptyState>

    <UiCard v-else-if="store.plans.length > 0">
      <UiTable :caption="t('plans.list.tableCaption')">
        <template #head>
          <UiTableRow>
            <UiTableHeaderCell>{{ t('common.name') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.accounts') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.diskQuota') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.sites') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.databases') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.sftpUsers') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.ftpUsers') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.cronEntries') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('plans.list.columns.phpWorkers') }}</UiTableHeaderCell>
            <UiTableHeaderCell align="end">{{ t('common.actions') }}</UiTableHeaderCell>
          </UiTableRow>
        </template>
        <UiTableRow v-for="plan in store.plans" :key="plan.id">
          <!-- A plan's name is whatever the operator typed: one unit, so a long one scrolls the
               table inside its container and does not break into a column of fragments. -->
          <UiTableCell class="font-medium whitespace-nowrap">{{ plan.displayName }}</UiTableCell>
          <UiTableCell mono align="end" tone="secondary">{{ plan.accountCount }}</UiTableCell>
          <UiTableCell mono align="end" class="whitespace-nowrap">{{ plan.diskQuotaMb }}</UiTableCell>
          <UiTableCell mono align="end">{{ plan.maxSites }}</UiTableCell>
          <UiTableCell mono align="end">{{ plan.maxDatabases }}</UiTableCell>
          <UiTableCell mono align="end">{{ plan.maxSftpUsers }}</UiTableCell>
          <UiTableCell mono align="end">{{ optionalCount(plan.maxFtpUsers) }}</UiTableCell>
          <UiTableCell mono align="end">{{ optionalCount(plan.maxCronEntries) }}</UiTableCell>
          <UiTableCell mono align="end">{{ plan.maxPhpWorkersPerPool }}</UiTableCell>
          <UiTableCell align="end">
            <!-- Two icon buttons, each named for the plan it acts on: the glyph carries no text,
                 and two rows' "Edit" must not be the same accessible name. -->
            <div class="flex items-center justify-end gap-1">
              <UiButton
                variant="ghost"
                size="icon-sm"
                :to="{ name: 'plans-edit', params: { id: plan.id } }"
                :aria-label="t('plans.list.edit', { name: plan.displayName })"
              >
                <UiIcon name="pencil" size="md" />
              </UiButton>
              <UiButton
                variant="ghost"
                size="icon-sm"
                :aria-label="t('plans.list.delete', { name: plan.displayName })"
                @click="ask(plan)"
              >
                <UiIcon name="trash" size="md" />
              </UiButton>
            </div>
          </UiTableCell>
        </UiTableRow>
      </UiTable>
    </UiCard>

    <!-- One dialog for the whole table: only one deletion can be awaiting an answer. `UiModal`
         restores focus to the control that opened it when the dialog closes. Not wrapped in a
         `v-if`: `:open` is bound to a value that really changes, which is what lets the modal's
         own open-watcher move focus in and trap it. -->
    <UiConfirm
      :open="pending !== null"
      :title="t('plans.list.deleteTitle', { name: pending?.displayName ?? '' })"
      :question="confirmationText"
      :confirm-label="t('common.delete')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.deleting"
      :acting-label="t('plans.list.working')"
      @close="cancel"
      @confirm="confirm"
    />
  </section>
</template>
