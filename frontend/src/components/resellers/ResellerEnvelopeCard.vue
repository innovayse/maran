<script setup lang="ts">
/**
 * A reseller's envelope, read only: the figures an administrator granted, the three runtime ceilings
 * and the features the reseller may sell. The administrator sees it on the reseller's page and the
 * reseller sees the same card on its own overview, so each knows what the other is bound by.
 *
 * Dumb: the screen passes the envelope and, when it has them, the panel's feature names. A feature
 * the panel did not name (its list is still on its way, or could not be read) is printed by its name
 * on the wire, so the list is never blank. The SPA holds no feature names of its own.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { PlanFeatureChoice } from '../../types/plan'
import type { ResellerEnvelope } from '../../types/reseller'
import type { ResourceCeiling } from '../../types/resourceCeiling'
import { formatMegabytes } from '../../utils/formatBytes'

/** Props accepted by {@link ResellerEnvelopeCard}. */
const props = defineProps<{
  /** The envelope to describe; `null` is the one state in which it is still on its way. */
  envelope: ResellerEnvelope | null
  /** The panel's features with their names, or `null` before they arrived or after the read failed. */
  featureChoices: PlanFeatureChoice[] | null
}>()

const { t } = useI18n()

/** The features the envelope allows, named as the panel names them. */
const featureNames: ComputedRef<string> = computed(() => {
  const envelope = props.envelope
  if (envelope === null || envelope.features.length === 0) {
    return t('resellers.envelope.noFeatures')
  }
  return envelope.features
    .map((feature) => {
      return props.featureChoices?.find((choice) => {
        return choice.feature === feature
      })?.featureDisplayName ?? feature
    })
    .join(', ')
})

/**
 * Prints a ceiling the way the screens print an allowance.
 * @param ceiling The ceiling the envelope states.
 * @param format Writes the figure of a limited ceiling in its unit.
 * @returns The figure, or the words for "no limit".
 */
const ceilingText = (ceiling: ResourceCeiling, format: (figure: number) => string): string => {
  return ceiling.kind === 'unlimited' ? t('resellers.envelope.unlimited') : format(ceiling.maximum)
}

/**
 * Writes a CPU share, which is a percentage of one core.
 * @param figure The share.
 * @returns The share with its unit.
 */
const percentText = (figure: number): string => {
  return `${figure}%`
}

/**
 * Writes a plain count.
 * @param figure The count.
 * @returns The count as text.
 */
const countText = (figure: number): string => {
  return String(figure)
}
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading :title="t('resellers.envelope.heading')" :subtitle="t('resellers.envelope.subtitle')" />
    <!-- The values are read only once loaded: while loading each item draws a placeholder line. -->
    <UiDescriptionList :loading="envelope === null">
      <UiDescriptionItem :term="t('resellers.form.fields.maxAccounts')" mono>{{ envelope?.maxAccounts }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxPlans')" mono>{{ envelope?.maxPlans }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.diskPoolMb')" mono>
        {{ envelope === null ? '' : formatMegabytes(envelope.diskPoolMb) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxDiskQuotaMb')" mono>
        {{ envelope === null ? '' : formatMegabytes(envelope.maxDiskQuotaMb) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxSites')" mono>{{ envelope?.maxSites }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxDatabases')" mono>{{ envelope?.maxDatabases }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxSftpUsers')" mono>{{ envelope?.maxSftpUsers }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxFtpUsers')" mono>{{ envelope?.maxFtpUsers }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxCronEntries')" mono>{{ envelope?.maxCronEntries }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxPhpWorkersPerPool')" mono>
        {{ envelope?.maxPhpWorkersPerPool }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxMailboxes')" mono>{{ envelope?.maxMailboxes }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.mailboxQuotaPoolMib')" mono>
        {{ envelope === null ? '' : formatMegabytes(envelope.mailboxQuotaPoolMib) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.cpuQuotaPercent')" mono>
        {{ envelope === null ? '' : ceilingText(envelope.cpuQuotaPercent, percentText) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.memoryMaxMb')" mono>
        {{ envelope === null ? '' : ceilingText(envelope.memoryMaxMb, formatMegabytes) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.form.fields.maxProcesses')" mono>
        {{ envelope === null ? '' : ceilingText(envelope.maxProcesses, countText) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('resellers.envelope.featuresLabel')">{{ envelope === null ? '' : featureNames }}</UiDescriptionItem>
    </UiDescriptionList>
  </UiCard>
</template>
