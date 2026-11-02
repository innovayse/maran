<script setup lang="ts">
/**
 * One account's cron entries as a table: the schedule as cron writes it and in words, the command
 * exactly as the customer wrote it, whether it is on, and each row's actions — switch, edit, run now,
 * the last scheduled run's output, the history of runs started by hand, and removal.
 *
 * Dumb by design (rules/vue.md): rows in, one event per action out. The page decides what each action
 * does; this component decides only how an entry reads.
 *
 * The command is shown exactly as the customer wrote it. It can carry a credential, which is why the
 * panel keeps it out of its logs and its audit journal — but it is their own text on their own screen,
 * and masking it would leave them unable to read their job.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiDropdown from '../ui/UiDropdown.vue'
import UiDropdownItem from '../ui/UiDropdownItem.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiTable from '../ui/UiTable.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatCronExpression } from '../../utils/cronSchedule'
import { describeCronSchedule } from '../../utils/describeCronSchedule'
import { weekdayName } from '../../utils/weekdayName'
import type { UiTableColumn } from '../../types/tableColumn'
import type { CronEntry } from '../../types/cronEntry'

/** Props accepted by {@link CronEntriesTable}. */
defineProps<{
  /** The account's entries, in the order the agent reported them. */
  rows: CronEntry[]
  /** Whether the crontab is on its way, which draws placeholder rows. */
  loading: boolean
  /** The entry a run by hand is in flight for, or the empty string; its "Run now" is held. */
  runningEntryId: string
}>()

/** Events emitted by {@link CronEntriesTable}, one per row action. */
const emit = defineEmits<{
  /** Switch the entry on or off. */
  (e: 'toggle', entry: CronEntry): void
  /** Open the form on the entry. */
  (e: 'edit', entry: CronEntry): void
  /** Run the entry now. */
  (e: 'run', entry: CronEntry): void
  /** Show what the last scheduled run left behind. */
  (e: 'output', entry: CronEntry): void
  /** Open the history of runs started by hand. */
  (e: 'history', entry: CronEntry): void
  /** Ask to remove the entry. */
  (e: 'remove', entry: CronEntry): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'schedule', label: t('cron.list.columns.schedule'), kind: 'mono' },
    { key: 'runs', label: t('cron.list.columns.runs'), kind: 'text', minWidth: '10rem' },
    { key: 'command', label: t('cron.list.columns.command'), kind: 'code', minWidth: '14rem' },
    { key: 'state', label: t('cron.list.columns.state'), kind: 'badge' },
    { key: 'actions', label: t('common.actions'), kind: 'actions' },
  ]
})

/**
 * Says when an entry runs in words, for the shapes that have a plain sentence.
 * @param entry The entry to describe.
 * @returns The sentence, or the em dash when the schedule is not one a sentence can carry exactly;
 * the expression beside it is then the whole description.
 */
const scheduleDescription = (entry: CronEntry): string => {
  const description = describeCronSchedule(entry.schedule)
  if (description === null) {
    return t('common.emptyValue')
  }
  switch (description.kind) {
    case 'everyMinute':
      return t('cron.describe.everyMinute')
    case 'everyNMinutes':
      return t('cron.describe.everyNMinutes', { count: description.minutes }, description.minutes)
    case 'hourly':
      return t('cron.describe.hourly', { minute: description.minute })
    case 'daily':
      return t('cron.describe.daily', { time: description.time })
    case 'weekly':
      return t('cron.describe.weekly', {
        time: description.time,
        weekday: weekdayName(description.weekday, localeStore.current),
      })
    default:
      return t('cron.describe.monthly', { time: description.time, day: description.day })
  }
}
</script>

<template>
  <UiTable
    :caption="t('cron.list.tableCaption')"
    :columns="columns"
    :rows="rows"
    row-key="entryId"
    :loading="loading"
  >
    <template #cell-schedule="{ row }">{{ formatCronExpression(row.schedule) }}</template>
    <template #cell-runs="{ row }">{{ scheduleDescription(row) }}</template>
    <template #cell-command="{ row }">{{ row.command }}</template>
    <template #cell-state="{ row }">
      <UiBadge :variant="row.enabled ? 'success' : 'neutral'">
        {{ row.enabled ? t('cron.list.enabled') : t('cron.list.disabled') }}
      </UiBadge>
    </template>
    <template #cell-actions="{ row }">
      <UiDropdown
        :label="t('common.actions')"
        :aria-label="t('cron.list.rowActions', { command: row.command })"
        align="end"
        variant="bare"
        :chevron="false"
      >
        <template #trigger>
          <UiIcon name="ellipsis" size="md" />
        </template>
        <UiDropdownItem @select="emit('toggle', row)">
          {{ row.enabled ? t('cron.list.disable') : t('cron.list.enable') }}
        </UiDropdownItem>
        <UiDropdownItem @select="emit('edit', row)">{{ t('cron.list.edit') }}</UiDropdownItem>
        <!-- One run at a time from this screen: the item is disabled while any run is in flight,
             because the dialog that shows the answer is one dialog. -->
        <UiDropdownItem :disabled="runningEntryId !== ''" @select="emit('run', row)">
          {{ t('cron.list.runNow') }}
        </UiDropdownItem>
        <UiDropdownItem @select="emit('output', row)">{{ t('cron.list.lastOutput') }}</UiDropdownItem>
        <UiDropdownItem @select="emit('history', row)">{{ t('cron.list.history') }}</UiDropdownItem>
        <UiDropdownItem destructive @select="emit('remove', row)">{{ t('cron.list.remove') }}</UiDropdownItem>
      </UiDropdown>
    </template>
  </UiTable>
</template>
