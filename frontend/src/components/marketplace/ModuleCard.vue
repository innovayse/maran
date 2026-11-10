<script setup lang="ts">
/**
 * One module of the catalogue as a card: name, summary, publisher, tier, price, the versions, the
 * licence standing and the actions that fit it.
 *
 * Every verdict is the panel's. The card offers "Install" only when the panel says `canInstall` and
 * the module is not on the panel yet, and disables it while the catalogue is unverified or an
 * operation on the module is running; when the panel refuses, the card prints its
 * `cannotInstallMessage` as plain text and draws NO install button. A module whose licence is not in
 * place shows the licence state and, only when the panel marked a purchase address as a trusted
 * `https` link, a link to the cabinet through the kit's external-link primitive. A withdrawn module
 * carries a warning. All names and reasons are the panel's text, printed as received.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiExternalLink from '../ui/UiExternalLink.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatDate } from '../../utils/formatDate'
import type { MarketplaceModule, MarketplaceModuleState } from '../../types/marketplace'
import ModuleLicenceBadge from './ModuleLicenceBadge.vue'

/** Props accepted by {@link ModuleCard}. */
const props = defineProps<{
  /** The module, as the catalogue lists it. */
  module: MarketplaceModule
  /** False while the catalogue is not verified; installing is then off whatever the module says. */
  catalogueVerified: boolean
  /** True while a request or an operation on this module is running. */
  busy: boolean
}>()

/** Events emitted by {@link ModuleCard}. */
const emit = defineEmits<{
  /** The administrator pressed Install on this module. */
  (e: 'install', module: MarketplaceModule): void
}>()

/** The tone each catalogue state is drawn in. */
const STATE_TONES: Record<MarketplaceModuleState, BadgeVariant> = {
  available: 'neutral',
  installed: 'success',
  updateAvailable: 'info',
  revoked: 'warning',
  incompatible: 'warning',
}

const { t } = useI18n()
const localeStore = useLocaleStore()

/** Whether the licence is missing or lapsed, which is when the cabinet link is offered. */
const licenceMissing: ComputedRef<boolean> = computed(() => {
  const state = props.module.entitlement.state
  return state === 'notPurchased' || state === 'leaseExpired'
})

/** The cabinet address to buy at, or `null`; the panel sends it only when it is a trusted `https` link. */
const purchaseUrl: ComputedRef<string | null> = computed(() => {
  return licenceMissing.value ? (props.module.price?.purchaseUrl ?? null) : null
})

/** Whether the Install button is drawn: the panel allows it and the module is not on the panel yet. */
const offersInstall: ComputedRef<boolean> = computed(() => {
  return props.module.canInstall && props.module.installedVersion === null
})

/** The refusal to print as text when there is no install button and the panel gave a reason. */
const refusal: ComputedRef<string | null> = computed(() => {
  return offersInstall.value ? null : props.module.cannotInstallMessage
})

/** When the entitlement ends, formatted for the reader, or `null`. */
const until: ComputedRef<string | null> = computed(() => {
  const end = props.module.entitlement.until
  return end === null ? null : formatDate(end, localeStore.current)
})
</script>

<template>
  <UiCard :data-testid="`module-card-${module.id}`">
    <div class="flex flex-col gap-3">
      <UiSectionHeading :title="module.displayName" :subtitle="module.summary" />

      <div class="flex flex-wrap items-center gap-2">
        <UiBadge :variant="STATE_TONES[module.state]">{{ t(`marketplace.states.${module.state}`) }}</UiBadge>
        <ModuleLicenceBadge :entitlement="module.entitlement" />
        <UiBadge v-if="module.restartRequired" variant="warning">{{ t('marketplace.lifecycle.installed') }}</UiBadge>
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
      <p v-else-if="refusal !== null" class="text-sm text-text-muted" data-testid="module-cannot-install">{{ refusal }}</p>

      <div class="flex flex-wrap items-center justify-end gap-2">
        <UiExternalLink v-if="purchaseUrl !== null" :href="purchaseUrl" data-testid="module-buy-link">
          {{ t('marketplace.card.buyFor', { name: module.displayName }) }}
        </UiExternalLink>
        <UiButton
          :to="{ name: 'marketplace-module', params: { id: module.id } }"
          variant="secondary"
          :aria-label="t('marketplace.card.detailsFor', { name: module.displayName })"
        >
          {{ t('marketplace.card.details') }}
        </UiButton>
        <UiButton
          v-if="offersInstall"
          :disabled="busy || !catalogueVerified"
          :aria-label="t('marketplace.card.installFor', { name: module.displayName })"
          @click="emit('install', module)"
        >
          {{ t('marketplace.card.install') }}
        </UiButton>
      </div>
    </div>
  </UiCard>
</template>
