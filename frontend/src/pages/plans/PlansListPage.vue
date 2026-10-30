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
 * **Delete asks first, and a deletion the panel would refuse is not offered.** The dialog
 * ({@link PlanDeleteDialog}) names the plan and the consequence. When accounts are on the plan its
 * Delete button is disabled, the reason is printed, and it links to the accounts on the plan so the
 * administrator can move them (#216). The count may be a moment old, so the server keeps the last
 * word: a refusal it sends is rendered unchanged above the table.
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
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiToast from '../../components/ui/UiToast.vue'
import UiTable from '../../components/ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import PlanDeleteDialog from '../../components/plans/PlanDeleteDialog.vue'
import { usePlansStore } from '../../stores/plans'
import { formatMegabytes } from '../../utils/formatBytes'
import type { ManagedPlan } from '../../types/plan'
import { takeFlashNotice } from '../../utils/flashNotice'

const { t } = useI18n()
const store = usePlansStore()

/** The plan whose deletion awaits confirmation, or `null` when none does. */
const pending: Ref<ManagedPlan | null> = ref(null)

/** The success toast's text, or `null` while none is showing. */
const notice: Ref<string | null> = ref(null)

/** Whether the panel answered successfully and reported no plans at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.loadState === 'loaded' && store.plans.length === 0
})


/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('common.name'), kind: 'identity' },
    { key: 'accounts', label: t('plans.list.columns.accounts'), kind: 'number' },
    { key: 'diskQuota', label: t('plans.list.columns.diskQuota'), kind: 'number' },
    { key: 'sites', label: t('plans.list.columns.sites'), kind: 'number' },
    { key: 'databases', label: t('plans.list.columns.databases'), kind: 'number' },
    { key: 'sftpUsers', label: t('plans.list.columns.sftpUsers'), kind: 'number' },
    { key: 'ftpUsers', label: t('plans.list.columns.ftpUsers'), kind: 'number' },
    { key: 'cronEntries', label: t('plans.list.columns.cronEntries'), kind: 'number' },
    { key: 'phpWorkers', label: t('plans.list.columns.phpWorkers'), kind: 'number' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
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

/**
 * Hides the success toast.
 * @returns Nothing.
 */
const dismissNotice = (): void => {
  notice.value = null
}

onMounted(async () => {
  notice.value = takeFlashNotice()
  await refresh()
})
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

    <UiLoadingRegion :state="store.loadState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
      </template>

      <UiEmptyState v-if="isEmpty" :title="t('plans.list.emptyTitle')" :description="t('plans.list.emptyDescription')">
        <template #icon><UiIcon name="package" size="lg" /></template>
        <UiButton :to="{ name: 'plans-new' }">{{ t('plans.list.createAction') }}</UiButton>
      </UiEmptyState>

      <UiCard v-else>
        <UiTable
          :caption="t('plans.list.tableCaption')"
          :columns="columns"
          :rows="store.plans"
          row-key="id"
          :loading="store.loadState === 'loading'"
        >
          <template #cell-name="{ row }">{{ row.displayName }}</template>
          <template #cell-accounts="{ row }">{{ row.accountCount }}</template>
          <template #cell-diskQuota="{ row }">{{ formatMegabytes(row.diskQuotaMb) }}</template>
          <template #cell-sites="{ row }">{{ row.maxSites }}</template>
          <template #cell-databases="{ row }">{{ row.maxDatabases }}</template>
          <template #cell-sftpUsers="{ row }">{{ row.maxSftpUsers }}</template>
          <template #cell-ftpUsers="{ row }">{{ optionalCount(row.maxFtpUsers) }}</template>
          <template #cell-cronEntries="{ row }">{{ optionalCount(row.maxCronEntries) }}</template>
          <template #cell-phpWorkers="{ row }">{{ row.maxPhpWorkersPerPool }}</template>
          <template #cell-actions="{ row }">
            <!-- Two icon buttons, each named for the plan it acts on: the glyph carries no text,
                 and two rows' "Edit" must not be the same accessible name. -->
            <div class="flex items-center justify-end gap-1">
              <UiButton
                variant="ghost"
                size="icon-sm"
                :to="{ name: 'plans-edit', params: { id: row.id } }"
                :aria-label="t('plans.list.edit', { name: row.displayName })"
              >
                <UiIcon name="pencil" size="md" />
              </UiButton>
              <UiButton
                variant="ghost"
                size="icon-sm"
                :aria-label="t('plans.list.delete', { name: row.displayName })"
                @click="ask(row)"
              >
                <UiIcon name="trash" size="md" />
              </UiButton>
            </div>
          </template>
        </UiTable>
      </UiCard>
    </UiLoadingRegion>

    <!-- One dialog for the whole table: only one deletion can be awaiting an answer. Always mounted
         and opened by `pending` being set, so it animates and `UiModal` returns focus to the
         control that opened it. -->
    <PlanDeleteDialog
      :plan="pending"
      :acting="store.deleting"
      @close="cancel"
      @confirm="confirm"
    />
    <div class="pointer-events-none fixed inset-x-4 bottom-4 z-50 flex justify-end sm:left-auto">
      <UiToast v-if="notice !== null" variant="success" :close-label="t('common.close')" @dismiss="dismissNotice">
        {{ notice }}
      </UiToast>
    </div>
  </section>
</template>
