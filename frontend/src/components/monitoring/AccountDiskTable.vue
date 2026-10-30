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
 *
 * **A fourth state the bar must never draw as if it were the first.** `enforceable === false`
 * means the allowance shown is real — the plan the customer paid for — but this server cannot
 * currently back it with a kernel-enforced limit. Drawing {@link UiMeter} against it regardless
 * would tell the reader a limit is in force when the panel itself already knows it is not, which
 * is exactly the defect issue #29 exists to fix — so this row renders the used figure plain, with
 * a badge, instead of a bar that implies enforcement.
 *
 * Laid out as the design's accounts table: the account in mono, the two byte figures right-aligned
 * (heading and cells share one alignment), and the ratio bar last with its percentage printed
 * beside it. The table scrolls inside its own container on a phone, never the page.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiMeter from '../ui/UiMeter.vue'
import UiTable from '../ui/UiTable.vue'
import type { UiTableColumn } from '../../types/tableColumn'
import { formatBytes } from '../../utils/formatBytes'
import type { AccountDiskUsage } from '../../types/monitoring'

/** Props accepted by {@link AccountDiskTable}. */
defineProps<{
  /** The rows the panel answered with, in its own order. */
  rows: AccountDiskUsage[]
}>()

const { t } = useI18n()

/** The table's columns: its header and its cells read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'account', label: t('monitoring.disk.columnAccount'), kind: 'identifier' },
    { key: 'used', label: t('monitoring.disk.columnUsed'), kind: 'number' },
    { key: 'quota', label: t('monitoring.disk.columnQuota'), kind: 'number' },
    { key: 'ratio', label: t('monitoring.disk.columnRatio'), kind: 'text' },
  ]
})

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

/**
 * The plain-text readout for a row whose quota this server cannot currently enforce — used
 * instead of {@link ratioText} so the sentence never claims a limit is in force.
 * @param row The account's row.
 * @returns The used figure, marked as unenforced rather than measured against the allowance.
 */
const notEnforcedRatioText = (row: AccountDiskUsage): string => {
  return t('monitoring.disk.ratioNotEnforced', { used: usedText(row.usedBytes) })
}
</script>

<template>
  <UiTable :caption="t('monitoring.disk.caption')" :columns="columns" :rows="rows" row-key="accountId">
    <template #cell-account="{ row }">{{ row.username }}</template>
    <template #cell-used="{ row }">{{ usedText(row.usedBytes) }}</template>
    <template #cell-quota="{ row }">
      <!-- The badge's title comes from the backend's own note — the SPA never invents the
           wording for why a real, paid-for allowance is not being applied right now. -->
      <UiBadge v-if="!row.enforceable" variant="warning" class="mr-2" :title="row.note ?? undefined">
        {{ t('monitoring.disk.notEnforcedBadge') }}
      </UiBadge>
      {{ quotaText(row.quotaBytes) }}
    </template>
    <template #cell-ratio="{ row }">
      <!-- No bar at all for an unmeasured account: an empty bar is a picture of "using nothing",
           which is the precise claim a null exists to avoid making. -->
      <span v-if="row.usedBytes === null" class="text-sm text-text-muted">{{
        t('monitoring.disk.notMeasured')
      }}</span>
      <span v-else-if="row.quotaBytes === 0" class="text-sm text-text-muted">{{
        t('monitoring.disk.noQuota')
      }}</span>
      <!-- Plain text, not a bar: a bar against `quotaBytes` here would draw a limit as if it
           were in force, which is the exact defect issue #29 exists to fix. -->
      <span v-else-if="!row.enforceable" class="text-sm text-text-muted" :title="row.note ?? undefined">{{
        notEnforcedRatioText(row)
      }}</span>
      <!-- The cells beside the bar print the used and allowed figures, not the ratio, so the bar's
           own visible percentage is kept: the bar is never an anonymous stripe. -->
      <div v-else class="min-w-44">
        <UiMeter
          :value="row.usedBytes"
          :max="row.quotaBytes"
          :label="t('monitoring.disk.meterLabel', { username: row.username })"
          :value-text="ratioText(row)"
        />
      </div>
    </template>
  </UiTable>
</template>
