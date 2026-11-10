<script setup lang="ts">
/**
 * What a reseller has used of its envelope: accounts, plans and the disk pool, each as a caption
 * (its name and "used of allowed") above a meter against its allowance, with a warning when the envelope was lowered below what already exists.
 * Existing plans and accounts keep working then and only new writes are refused, which the warning
 * says so nobody reads a full meter as a fault.
 *
 * Dumb: the screen passes the reseller; the figures are the server's counts, nothing is derived
 * here but whether a count passes its allowance.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiCard from '../ui/UiCard.vue'
import UiMeter from '../ui/UiMeter.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import type { ResellerDetail } from '../../types/reseller'
import { formatMegabytes } from '../../utils/formatBytes'

/** Props accepted by {@link ResellerUsageCard}. */
const props = defineProps<{
  /** The reseller whose usage is shown; `null` is the one state in which it is still on its way. */
  reseller: ResellerDetail | null
}>()

const { t } = useI18n()

/** One measured allowance: what it is called, how far it is used and the sentence that says so. */
interface UsageRow {
  /** Stable key of the allowance, also the row's test id. */
  key: 'accounts' | 'plans' | 'disk'
  /** The allowance's name, already translated. */
  label: string
  /** What has been used, in the unit of {@link max}. */
  used: number
  /** The allowance, in the unit of {@link used}. */
  max: number
  /** "{used} of {max}" with each figure in the unit a reader expects. */
  figure: string
}

/** Whether any count passes its allowance, which is only possible after the envelope was lowered. */
const overEnvelope: ComputedRef<boolean> = computed(() => {
  const reseller = props.reseller
  if (reseller === null) {
    return false
  }
  return (
    reseller.accountCount > reseller.envelope.maxAccounts ||
    reseller.planCount > reseller.envelope.maxPlans ||
    reseller.diskAllocatedMb > reseller.envelope.diskPoolMb
  )
})

/**
 * The three allowances in the order a reader weighs them. Each row carries its caption (name and
 * figure) so that the bar below it is never an anonymous stripe; the figures are the server's counts.
 */
const rows: ComputedRef<UsageRow[]> = computed(() => {
  const reseller = props.reseller
  if (reseller === null) {
    return []
  }
  const envelope = reseller.envelope
  return [
    {
      key: 'accounts',
      label: t('resellers.usage.accounts'),
      used: reseller.accountCount,
      max: envelope.maxAccounts,
      figure: t('resellers.usage.ratio', { used: reseller.accountCount, max: envelope.maxAccounts }),
    },
    {
      key: 'plans',
      label: t('resellers.usage.plans'),
      used: reseller.planCount,
      max: envelope.maxPlans,
      figure: t('resellers.usage.ratio', { used: reseller.planCount, max: envelope.maxPlans }),
    },
    {
      key: 'disk',
      label: t('resellers.usage.disk'),
      used: reseller.diskAllocatedMb,
      max: envelope.diskPoolMb,
      figure: t('resellers.usage.ratio', {
        used: formatMegabytes(reseller.diskAllocatedMb),
        max: formatMegabytes(envelope.diskPoolMb),
      }),
    },
  ]
})
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading :title="t('resellers.usage.heading')" :subtitle="t('resellers.usage.subtitle')" />
    <UiSkeleton v-if="reseller === null" shape="block" />
    <template v-else>
      <UiAlert v-if="overEnvelope" variant="warning">{{ t('resellers.usage.overEnvelope') }}</UiAlert>
      <ul class="m-0 flex list-none flex-col gap-4 p-0">
        <li v-for="row in rows" :key="row.key" class="flex min-w-0 flex-col gap-2" :data-testid="`usage-${row.key}`">
          <!-- The caption names the bar and says what it measures, as text a sighted reader sees: the
               bar's aria-label alone leaves three anonymous stripes. The figure above already says it,
               so the meter does not print a percentage a second time. -->
          <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-1">
            <span class="text-sm text-text-primary">{{ row.label }}</span>
            <span class="font-mono text-sm text-text-secondary tabular-nums wrap-anywhere">{{ row.figure }}</span>
          </div>
          <UiMeter :value="row.used" :max="row.max" :label="row.label" :value-text="row.figure" :show-value="false" />
        </li>
      </ul>
    </template>
  </UiCard>
</template>
