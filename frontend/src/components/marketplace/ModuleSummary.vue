<script setup lang="ts">
/**
 * The summary card of a module's own screen: its state and licence, publisher, tier, price,
 * versions, the panel's reason when it cannot be installed, a warning when it was withdrawn, the
 * cabinet link when the licence is missing and the Install action.
 *
 * As on the catalogue card, every verdict is the panel's: Install is drawn only when the panel
 * says `canInstall` and the module is not on the panel yet, the cabinet link only when the panel
 * marked the address as a trusted `https` link, and all names and reasons are printed as received.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiExternalLink from '../ui/UiExternalLink.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { MarketplaceModule } from '../../types/marketplace'
import ModuleLicenceBadge from './ModuleLicenceBadge.vue'

/** Props accepted by {@link ModuleSummary}. */
const props = defineProps<{
  /** The module, as the panel describes it. */
  module: MarketplaceModule
  /** True while a request or an operation on the module runs; the Install button is then off. */
  busy: boolean
}>()

/** Events emitted by {@link ModuleSummary}. */
const emit = defineEmits<{
  /** The administrator pressed Install. */
  (e: 'install'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** When the entitlement ends, formatted for the reader, or `null`. */
const until: ComputedRef<string | null> = computed(() => {
  const end = props.module.entitlement.until
  return end === null ? null : formatDate(end, localeStore.current)
})

/** The cabinet address to buy at when the licence is missing or lapsed, or `null`. */
const purchaseUrl: ComputedRef<string | null> = computed(() => {
  const state = props.module.entitlement.state
  return state === 'notPurchased' || state === 'leaseExpired' ? (props.module.price?.purchaseUrl ?? null) : null
})

/** Whether Install is drawn: the panel allows it and the module is not on the panel yet. */
const offersInstall: ComputedRef<boolean> = computed(() => {
  return props.module.canInstall && props.module.installedVersion === null
})
</script>

<template>
  <UiCard data-testid="module-summary">
    <div class="flex flex-col gap-3">
      <UiSectionHeading :title="t('marketplace.detail.summaryTitle')" />
      <div class="flex flex-wrap items-center gap-2">
        <UiBadge>{{ t(`marketplace.states.${module.state}`) }}</UiBadge>
        <ModuleLicenceBadge :entitlement="module.entitlement" />
      </div>
      <UiDescriptionList>
        <UiDescriptionItem :term="t('marketplace.card.publisher')">{{ module.publisher }}</UiDescriptionItem>
        <UiDescriptionItem :term="t('marketplace.manifest.tier')">{{ module.tierDisplayName }}</UiDescriptionItem>
        <UiDescriptionItem v-if="module.price !== null" :term="t('marketplace.card.price')">
          {{ module.price.display }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="module.latestVersion !== null" :term="t('marketplace.card.latestVersion')" mono>
          {{ module.latestVersion }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="module.installedVersion !== null" :term="t('marketplace.card.installedVersion')" mono>
          {{ module.installedVersion }}
        </UiDescriptionItem>
        <UiDescriptionItem v-if="until !== null" :term="t('marketplace.card.licence')">
          {{ t('marketplace.card.licenceUntil', { date: until }) }}
        </UiDescriptionItem>
      </UiDescriptionList>

      <UiAlert v-if="module.state === 'revoked'" variant="warning" data-testid="module-revoked">
        <p class="font-medium">{{ t('marketplace.states.revoked') }}</p>
        <p v-if="module.cannotInstallMessage !== null">{{ module.cannotInstallMessage }}</p>
      </UiAlert>
      <p v-else-if="!offersInstall && module.cannotInstallMessage !== null" class="text-sm text-text-muted">
        {{ module.cannotInstallMessage }}
      </p>

      <div class="flex flex-wrap items-center justify-end gap-2">
        <UiExternalLink v-if="purchaseUrl !== null" :href="purchaseUrl" data-testid="module-buy-link">
          {{ t('marketplace.card.buyFor', { name: module.displayName }) }}
        </UiExternalLink>
        <UiButton v-if="offersInstall" :disabled="busy" @click="emit('install')">{{ t('marketplace.card.install') }}</UiButton>
      </div>
    </div>
  </UiCard>
</template>
