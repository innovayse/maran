<script setup lang="ts">
/**
 * The dry run: the administrator chooses the plan the account would be created against (and, if the
 * account is to belong to a reseller, the reseller), and the panel judges the package against this
 * server. Nothing about any account, site, database, cron entry or login changes to produce the report.
 *
 * Dumb by the usual contract: props in, emits out. The pickers' state and the report come from the
 * page's store; the chosen plan and reseller are two-way bound so the page can send them with the start.
 * The report's "can start" is advice: the panel refuses to start an import with a conflict whatever this
 * screen shows.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import MigrationFindingList from './MigrationFindingList.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import type { AccountImportReport } from '../../types/accountMove'

/** Props accepted by {@link AccountImportPlanStep}. */
defineProps<{
  /** The plan picker's state; the plans offered are the chosen reseller's, or the administrator's own. */
  planPicker: AccountPickerState
  /** The reseller picker's state. */
  resellerPicker: AccountPickerState
  /** The report of the plan the administrator judged, or `null` while none was judged. */
  report: AccountImportReport | null
  /** Whether the dry run is in flight. */
  running: boolean
}>()

/** The plan chosen; empty before a choice. */
const planId = defineModel<string>('planId', { required: true })

/** The reseller chosen; empty for the administrator's own account. */
const resellerId = defineModel<string>('resellerId', { required: true })

/** Events emitted by {@link AccountImportPlanStep}. */
const emit = defineEmits<{
  /** The plan picker needs the plans matching what was typed. */
  (e: 'searchPlans', term: string): void
  /** The reseller picker needs the resellers matching what was typed. */
  (e: 'searchResellers', term: string): void
  /** The administrator asked for the dry run. */
  (e: 'run'): void
}>()

const { t } = useI18n()

/**
 * Asks the page to run the dry run.
 * @returns Nothing; emits synchronously.
 */
const run = (): void => {
  emit('run')
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('accountMove.import.plan.title')" :subtitle="t('accountMove.import.plan.intro')" />

    <div class="grid gap-4 sm:grid-cols-2">
      <UiCombobox
        v-model="resellerId"
        v-bind="resellerPicker"
        :label="t('accountMove.import.plan.reseller')"
        :placeholder="t('accountMove.import.plan.resellerPlaceholder')"
        @search="emit('searchResellers', $event)"
      />
      <UiCombobox
        v-model="planId"
        v-bind="planPicker"
        :label="t('accountMove.import.plan.plan')"
        :placeholder="t('accountMove.import.plan.planPlaceholder')"
        required
        @search="emit('searchPlans', $event)"
      />
    </div>

    <div class="flex justify-end">
      <UiButton variant="secondary" :disabled="planId === '' || running" @click="run">
        {{ running ? t('accountMove.import.plan.running') : t('accountMove.import.plan.run') }}
      </UiButton>
    </div>

    <template v-if="report !== null">
      <UiSectionHeading :title="t('accountMove.import.report.title')" />
      <UiAlert :variant="report.canStart ? 'success' : 'error'" data-testid="report-verdict">
        {{ report.canStart ? t('accountMove.import.report.canStart') : t('accountMove.import.report.blocked') }}
      </UiAlert>
      <p v-if="report.findings.length === 0" class="text-sm text-text-muted">{{ t('accountMove.import.report.none') }}</p>
      <MigrationFindingList v-else :findings="report.findings" />
    </template>
  </div>
</template>
