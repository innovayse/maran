<script setup lang="ts">
/**
 * What a reseller has used of its envelope: accounts, plans and the disk pool, each as a meter
 * against its allowance, with a warning when the envelope was lowered below what already exists.
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
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading :title="t('resellers.usage.heading')" :subtitle="t('resellers.usage.subtitle')" />
    <UiSkeleton v-if="reseller === null" shape="block" />
    <template v-else>
      <UiAlert v-if="overEnvelope" variant="warning">{{ t('resellers.usage.overEnvelope') }}</UiAlert>
      <UiMeter
        :value="reseller.accountCount"
        :max="reseller.envelope.maxAccounts"
        :label="t('resellers.usage.accounts')"
        :value-text="t('resellers.usage.ratio', { used: reseller.accountCount, max: reseller.envelope.maxAccounts })"
      />
      <UiMeter
        :value="reseller.planCount"
        :max="reseller.envelope.maxPlans"
        :label="t('resellers.usage.plans')"
        :value-text="t('resellers.usage.ratio', { used: reseller.planCount, max: reseller.envelope.maxPlans })"
      />
      <UiMeter
        :value="reseller.diskAllocatedMb"
        :max="reseller.envelope.diskPoolMb"
        :label="t('resellers.usage.disk')"
        :value-text="
          t('resellers.usage.ratio', {
            used: formatMegabytes(reseller.diskAllocatedMb),
            max: formatMegabytes(reseller.envelope.diskPoolMb),
          })
        "
      />
    </template>
  </UiCard>
</template>
