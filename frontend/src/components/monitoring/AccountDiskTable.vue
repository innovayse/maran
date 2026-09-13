<script setup lang="ts">
/**
 * What every hosting account occupies on disk, beside what its plan allows, with a ratio bar per
 * row.
 *
 * **Three row states, not two.** A `usedBytes` of `null` is the panel saying the agent did not
 * report this account at all, which is a different fact from an account that holds nothing — the
 * DTO is nullable precisely so the two cannot be confused, and a row that drew an empty bar for
 * both would throw that distinction away. A `quotaBytes` of zero is the third: the plan records no
 * allowance, so there is no ratio to draw and the row says so rather than dividing by it.
 *
 * The bar itself is {@link UiMeter}, a kit primitive, so its accessible name and announced value
 * are decided once for the whole panel rather than per screen.
 */
import { useI18n } from 'vue-i18n'
import UiMeter from '../ui/UiMeter.vue'
import UiTable from '../ui/UiTable.vue'
import UiTableCell from '../ui/UiTableCell.vue'
import UiTableHeaderCell from '../ui/UiTableHeaderCell.vue'
import UiTableRow from '../ui/UiTableRow.vue'
import { formatBytes } from '../../utils/formatBytes'
import type { AccountDiskUsage } from '../../types/monitoring'

/** Props accepted by {@link AccountDiskTable}. */
defineProps<{
  /** The rows the panel answered with, in its own order. */
  rows: AccountDiskUsage[]
}>()

const { t } = useI18n()

/**
 * The used figure as text, or the panel's "not measured" wording when the agent reported none.
 * @param usedBytes The measured size, or `null` when the account was not reported.
 * @returns The formatted size, or the unmeasured label.
 */
const usedText = (usedBytes: number | null): string => {
  return usedBytes === null ? t('monitoring.disk.notMeasured') : formatBytes(usedBytes)
}

/**
 * The allowance as text, or the "no allowance recorded" wording for a zero-quota plan.
 * @param quotaBytes The plan's allowance in bytes, possibly zero.
 * @returns The formatted allowance, or the no-allowance label.
 */
const quotaText = (quotaBytes: number): string => {
  return quotaBytes === 0 ? t('monitoring.disk.noQuota') : formatBytes(quotaBytes)
}

/**
 * The ratio spelled out for the bar's assistive-technology readout — the one string that carries
 * the whole row's meaning to a screen reader, so it names both figures rather than a bare percent.
 * @param row The account's row.
 * @returns The used-against-allowed sentence.
 */
const ratioText = (row: AccountDiskUsage): string => {
  return t('monitoring.disk.ratio', { used: usedText(row.usedBytes), quota: quotaText(row.quotaBytes) })
}
</script>

<template>
  <UiTable :caption="t('monitoring.disk.caption')">
    <template #head>
      <UiTableRow>
        <UiTableHeaderCell>{{ t('monitoring.disk.columnAccount') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('monitoring.disk.columnUsed') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('monitoring.disk.columnQuota') }}</UiTableHeaderCell>
        <UiTableHeaderCell>{{ t('monitoring.disk.columnRatio') }}</UiTableHeaderCell>
      </UiTableRow>
    </template>

    <UiTableRow v-for="row in rows" :key="row.accountId" data-testid="account-disk-row">
      <UiTableCell>
        <span class="font-mono">{{ row.username }}</span>
      </UiTableCell>
      <UiTableCell>{{ usedText(row.usedBytes) }}</UiTableCell>
      <UiTableCell>{{ quotaText(row.quotaBytes) }}</UiTableCell>
      <UiTableCell>
        <!-- No bar at all for an unmeasured account: an empty bar is a picture of "using nothing",
             which is the precise claim a null exists to avoid making. -->
        <span v-if="row.usedBytes === null" class="text-sm text-text-muted">{{
          t('monitoring.disk.notMeasured')
        }}</span>
        <span v-else-if="row.quotaBytes === 0" class="text-sm text-text-muted">{{
          t('monitoring.disk.noQuota')
        }}</span>
        <UiMeter
          v-else
          :value="row.usedBytes"
          :max="row.quotaBytes"
          :label="t('monitoring.disk.meterLabel', { username: row.username })"
          :value-text="ratioText(row)"
        />
      </UiTableCell>
    </UiTableRow>
  </UiTable>
</template>
