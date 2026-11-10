<script setup lang="ts">
/**
 * The marketplace switch, always the first thing on the Marketplace screen. It says whether the
 * panel may contact the marketplace, which host that is, and exactly what the panel sends
 * (`sentItems`: the panel's own localized lines, printed as a plain list and never written in this
 * bundle). The switch is off by default; turning it on or off asks for the administrator's password
 * again in {@link PasswordConfirmDialog}, and the panel audits the change.
 *
 * With the switch off the panel makes no request to the marketplace at all, which the description
 * says in so many words. Whether a change is allowed is the panel's answer to the request: this card
 * only keeps "turn on" disabled while no host is configured, because the panel refuses that anyway
 * and the host line says why.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useMarketplaceStore } from '../../stores/marketplace'
import type { MarketplaceStatus } from '../../types/marketplace'
import PasswordConfirmDialog from './PasswordConfirmDialog.vue'

/** Props accepted by {@link MarketplaceSwitchCard}. */
const props = defineProps<{
  /** The switch and the facts the card prints, as the panel answered them. */
  status: MarketplaceStatus
}>()

const { t } = useI18n()
const store = useMarketplaceStore()

/** Whether the password dialog is open. */
const confirming: Ref<boolean> = ref(false)

/** Whether "turn on" must stay disabled: the switch is off and the server has no marketplace host. */
const cannotTurnOn: ComputedRef<boolean> = computed(() => {
  return !props.status.enabled && !props.status.hostConfigured
})

/** The panel's refusal as the dialog prints it, or the panel-owned "unreachable" sentence. */
const errorMessage: ComputedRef<string | null> = computed(() => {
  if (store.errorMessage !== null) {
    return store.errorMessage
  }
  return store.unreachable ? t('marketplace.unreachable.description') : null
})

/**
 * Opens the password dialog for the opposite position of the switch.
 * @returns Nothing.
 */
const ask = (): void => {
  store.clearError()
  confirming.value = true
}

/**
 * Closes the dialog without a change.
 * @returns Nothing.
 */
const dismiss = (): void => {
  store.clearError()
  confirming.value = false
}

/**
 * Moves the switch with the typed password and closes the dialog when the panel accepted.
 * @param password The administrator's current password.
 * @returns Resolves once the request settled.
 */
const submit = async (password: string): Promise<void> => {
  if (await store.setEnabled(!props.status.enabled, password)) {
    confirming.value = false
  }
}
</script>

<template>
  <UiCard data-testid="marketplace-switch-card">
    <div class="flex flex-col gap-4">
      <div class="flex flex-wrap items-start justify-between gap-3">
        <UiSectionHeading :title="t('marketplace.switch.title')" />
        <UiBadge :variant="status.enabled ? 'success' : 'neutral'" data-testid="marketplace-switch-state">
          {{ status.enabled ? t('marketplace.switch.stateOn') : t('marketplace.switch.stateOff') }}
        </UiBadge>
      </div>

      <p class="text-sm text-text-secondary">
        {{ status.enabled ? t('marketplace.switch.descriptionOn') : t('marketplace.switch.description') }}
      </p>

      <UiDescriptionList>
        <UiDescriptionItem :term="t('marketplace.switch.hostLabel')" mono>
          {{ status.host ?? t('marketplace.switch.hostNotConfigured') }}
        </UiDescriptionItem>
      </UiDescriptionList>

      <div v-if="status.sentItems.length > 0" class="flex flex-col gap-1.5">
        <p class="text-sm font-medium text-text-primary">{{ t('marketplace.switch.sentTitle') }}</p>
        <ul class="list-disc space-y-0.5 pl-5 text-sm text-text-secondary" data-testid="marketplace-sent-items">
          <li v-for="item in status.sentItems" :key="item" class="wrap-anywhere">{{ item }}</li>
        </ul>
      </div>

      <div class="flex justify-end">
        <UiButton
          :variant="status.enabled ? 'secondary' : 'primary'"
          :disabled="cannotTurnOn || store.statusLoading"
          @click="ask"
        >
          {{ status.enabled ? t('marketplace.switch.turnOff') : t('marketplace.switch.turnOn') }}
        </UiButton>
      </div>
    </div>

    <PasswordConfirmDialog
      :open="confirming"
      :title="status.enabled ? t('marketplace.switch.confirmOffTitle') : t('marketplace.switch.confirmOnTitle')"
      :question="status.enabled ? t('marketplace.switch.confirmOffQuestion') : t('marketplace.switch.confirmOnQuestion')"
      :submit-label="status.enabled ? t('marketplace.switch.turnOff') : t('marketplace.switch.turnOn')"
      :acting="store.statusLoading"
      :acting-label="t('marketplace.switch.working')"
      :error-message="errorMessage"
      @close="dismiss"
      @submit="submit"
    />
  </UiCard>
</template>
