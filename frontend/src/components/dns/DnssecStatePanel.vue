<script setup lang="ts">
/**
 * Where a zone's DNSSEC signing stands: the state as a badge, a sentence on what it means, the
 * plain statement that keys do not roll over by themselves, and what the name server reported.
 * Dumb by the usual contract: the zone page owns the store.
 *
 * The state names and the sentences are the panel's own chrome. A server outcome (a refusal, a
 * failure) never passes through here; the page renders those from the backend as sent.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { DnsPolicyState } from '../../types/dnsSettings'
import type { ZoneDnssec } from '../../types/dnsDnssec'

/** Props accepted by {@link DnssecStatePanel}. */
const props = defineProps<{
  /** The zone's signing as the panel stated it. */
  dnssec: ZoneDnssec
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The badge tone of the state: signed is the healthy one, a withdrawal needs attention. */
const badgeVariant: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.dnssec.state) {
    case 'signing':
      return 'success'
    case 'withdrawing':
      return 'warning'
    default:
      return 'neutral'
  }
})

/**
 * Names what the name server reported about one property of the signing.
 * @param state The report: seen in force, seen not in force, or not asked.
 * @returns The sentence for the report.
 */
const reportText = (state: DnsPolicyState): string => {
  return t(`dns.dnssec.observed.reports.${state}`)
}
</script>

<template>
  <UiCard class="flex max-w-3xl min-w-0 flex-col gap-3">
    <div class="flex flex-wrap items-center gap-2">
      <span class="text-sm text-text-secondary">{{ t('dns.dnssec.stateTerm') }}</span>
      <UiBadge :variant="badgeVariant">{{ t(`dns.dnssec.states.${dnssec.state}`) }}</UiBadge>
    </div>
    <p class="text-sm">{{ t(`dns.dnssec.descriptions.${dnssec.state}`) }}</p>
    <!-- Said in plain text on every state: the owner must know that nothing replaces the key. -->
    <p class="text-sm" data-testid="dnssec-no-rollover">
      {{ dnssec.rolloverAutomatic ? t('dns.dnssec.rolloverAutomatic') : t('dns.dnssec.noRollover') }}
    </p>
    <p v-if="dnssec.withdrawalStartedAt !== null" class="text-sm text-text-secondary">
      {{ t('dns.dnssec.withdrawalStarted', { when: formatIsoTimestamp(dnssec.withdrawalStartedAt, localeStore.current) }) }}
    </p>

    <UiDescriptionList v-if="dnssec.observed !== null">
      <UiDescriptionItem :term="t('dns.dnssec.observed.mode')">{{ reportText(dnssec.observed.modeObserved) }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('dns.dnssec.observed.publishedKey')">
        {{ reportText(dnssec.observed.publishedKeyObserved) }}
      </UiDescriptionItem>
      <UiDescriptionItem v-if="dnssec.observed.publishedKeyObserved === 'inForce'" :term="t('dns.dnssec.observed.publishedKeyTag')" mono>
        {{ dnssec.observed.publishedKeyTag }}
      </UiDescriptionItem>
    </UiDescriptionList>
  </UiCard>
</template>
