<script setup lang="ts">
/**
 * The offers to join another person's hosting account that wait for the signed-in person's answer.
 * Each row names the account, the features it would give and when the offer lapses, and carries
 * Accept and Decline. Declining asks for confirmation first because it cannot be undone from here:
 * the owner has to offer again. Dumb apart from the pending decline: the page owns the requests and
 * the account switcher's refresh that follows an accept.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiConfirm from '../ui/UiConfirm.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useLocaleStore } from '../../stores/locale'
import type { MembershipOffer } from '../../types/membershipOffer'
import { formatDate } from '../../utils/formatDate'

/** Props accepted by {@link MembershipOffersCard}. */
defineProps<{
  /** The open offers to draw, one row each. */
  offers: MembershipOffer[]
  /** The offer an answer is in flight for, or `null`; its row's buttons are held. */
  actingId: string | null
}>()

/** Events emitted by {@link MembershipOffersCard}. */
const emit = defineEmits<{
  /** The person accepted this offer. */
  (e: 'accept', offer: MembershipOffer): void
  /** The person confirmed declining this offer. */
  (e: 'decline', offer: MembershipOffer): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The offer awaiting the confirmation to decline it, or `null` while the dialog is closed. */
const declining: Ref<MembershipOffer | null> = ref(null)

/**
 * Joins an offer's feature names into one sentence fragment, or says nothing is granted.
 * @param offer The offer to describe.
 * @returns The names in the interface language, comma separated, or the "no features" text.
 */
const featureNames = (offer: MembershipOffer): string => {
  if (offer.features.length === 0) {
    return t('membershipOffers.noFeatures')
  }
  return offer.features
    .map((feature) => {
      return feature.featureDisplayName
    })
    .join(', ')
}

/**
 * Opens the decline confirmation for one offer.
 * @param offer The offer the person wants to decline.
 * @returns Nothing.
 */
const askToDecline = (offer: MembershipOffer): void => {
  declining.value = offer
}

/**
 * Closes the decline confirmation without declining.
 * @returns Nothing.
 */
const closeDecline = (): void => {
  declining.value = null
}

/**
 * Forwards the confirmed decline and closes the dialog.
 * @returns Nothing.
 */
const confirmDecline = (): void => {
  const target = declining.value
  declining.value = null
  if (target !== null) {
    emit('decline', target)
  }
}
</script>

<template>
  <UiCard>
    <UiSectionHeading
      class="mb-4"
      :title="t('membershipOffers.heading')"
      :subtitle="t('membershipOffers.subtitle')"
    />
    <ul class="flex flex-col gap-3">
      <li
        v-for="offer in offers"
        :key="offer.id"
        class="flex min-w-0 flex-col gap-3 rounded-control border border-border-subtle bg-surface-2 p-4 sm:flex-row sm:items-center sm:justify-between"
      >
        <div class="min-w-0">
          <p class="font-display text-base font-extrabold text-text-primary wrap-anywhere">{{ offer.accountName }}</p>
          <p class="mt-1 text-sm text-text-secondary wrap-anywhere">
            {{ t('membershipOffers.features', { features: featureNames(offer) }) }}
          </p>
          <p class="mt-1 text-sm text-text-muted">
            {{ t('membershipOffers.expires', { date: formatDate(offer.expiresAt, localeStore.current) }) }}
          </p>
        </div>
        <div class="flex shrink-0 flex-wrap gap-2">
          <UiButton :disabled="actingId !== null" @click="emit('accept', offer)">
            {{ t('membershipOffers.accept') }}
          </UiButton>
          <UiButton variant="secondary" :disabled="actingId !== null" @click="askToDecline(offer)">
            {{ t('membershipOffers.decline') }}
          </UiButton>
        </div>
      </li>
    </ul>

    <UiConfirm
      :open="declining !== null"
      :title="t('membershipOffers.declineTitle', { name: declining?.accountName ?? '' })"
      :question="t('membershipOffers.declineQuestion')"
      :confirm-label="t('membershipOffers.declineConfirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting-label="t('membershipOffers.working')"
      :destructive="false"
      @close="closeDecline"
      @confirm="confirmDecline"
    />
  </UiCard>
</template>
