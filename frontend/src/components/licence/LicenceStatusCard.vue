<script setup lang="ts">
/**
 * The licence the panel holds, as facts: the state badge and the backend's own sentence and advice,
 * then the licence id, tier, expiry, covered modules and the lease line.
 *
 * Every sentence and display name comes already localized from the backend and is rendered as plain
 * text; this component only adds its own chrome (labels, the offline note, the lease notice, the link in
 * the expiry warning). It
 * holds no state and calls nothing: the page hands it the status.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiExternalLink from '../ui/UiExternalLink.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiStack from '../ui/UiStack.vue'
import { useLocaleStore } from '../../stores/locale'
import type { LicenceStatus } from '../../types/licence'
import { formatDate } from '../../utils/formatDate'
import { formatUtcDate } from '../../utils/formatUtcDate'

/** Props accepted by {@link LicenceStatusCard}. */
const props = defineProps<{
  /** The licence status to show. */
  status: LicenceStatus
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/** The badge tone for the licence state: accepted, nothing installed, or refused. */
const badgeVariant: ComputedRef<BadgeVariant> = computed(() => {
  if (props.status.state === 'valid') {
    return 'success'
  }
  return props.status.state === 'refused' ? 'danger' : 'neutral'
})

/** The lease line: the backend's words for its state, plus the date it is valid until when there is one. */
const leaseText: ComputedRef<string> = computed(() => {
  const until = props.status.leaseValidUntil
  if (until === null) {
    return props.status.leaseDisplayName
  }
  const date = formatDate(until, localeStore.current)
  return `${props.status.leaseDisplayName}, ${t('licence.leaseValidUntil', { date })}`
})

/** Whether the lease notice is shown: only when the lease has run out. */
const leaseExpired: ComputedRef<boolean> = computed(() => {
  return props.status.lease === 'expired'
})
</script>

<template>
  <UiCard data-testid="licence-status">
    <UiSectionHeading class="mb-4" :title="t('licence.statusTitle')" />

    <UiStack gap="sm">
      <UiStack direction="row">
        <UiBadge :variant="badgeVariant">{{ status.stateDisplayName }}</UiBadge>
      </UiStack>
      <p class="text-sm text-text-primary">{{ status.sentence }}</p>
      <UiFieldHint v-if="status.refusalReasonDisplayName !== null">
        {{ status.refusalReasonDisplayName }}
      </UiFieldHint>
      <UiFieldHint v-if="status.advice !== null">{{ status.advice }}</UiFieldHint>
    </UiStack>

    <UiDescriptionList class="mt-4">
      <UiDescriptionItem v-if="status.licenceId !== null" :term="t('licence.licenceIdLabel')" mono>
        {{ status.licenceId }}
      </UiDescriptionItem>
      <UiDescriptionItem v-if="status.tier !== null" :term="t('licence.tierLabel')">
        {{ status.tier }}
      </UiDescriptionItem>
      <UiDescriptionItem v-if="status.expiry !== null" :term="t('licence.expiryLabel')">
        {{ formatUtcDate(status.expiry, localeStore.current) }}
      </UiDescriptionItem>
      <UiDescriptionItem v-if="status.modules !== null" :term="t('licence.modulesLabel')">
        <ul v-if="status.modules.length > 0" class="flex flex-wrap gap-x-3 gap-y-1 font-mono">
          <li v-for="moduleName in status.modules" :key="moduleName">{{ moduleName }}</li>
        </ul>
        <template v-else>{{ t('licence.noModules') }}</template>
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('licence.leaseLabel')" data-testid="licence-lease">
        {{ leaseText }}
      </UiDescriptionItem>
    </UiDescriptionList>

    <!-- The backend decides whether the licence is close to its expiry and words both texts; the claim link is the
         same one the server card carries, repeated here so the renewal steps and their first step are together. -->
    <UiAlert
      v-if="status.expiryWarning !== null"
      class="mt-4"
      variant="warning"
      data-testid="licence-expiry-warning"
    >
      <p class="font-semibold">{{ status.expiryWarning }}</p>
      <p v-if="status.expiryRenewalAdvice !== null" class="mt-1">{{ status.expiryRenewalAdvice }}</p>
      <UiExternalLink
        v-if="status.claimUrl !== null"
        class="mt-2 inline-block"
        :href="status.claimUrl"
        data-testid="licence-expiry-claim-link"
      >
        {{ t('licence.expiryClaimAgain') }}
      </UiExternalLink>
    </UiAlert>

    <UiAlert v-if="leaseExpired" class="mt-4" variant="warning" data-testid="licence-lease-notice">
      {{ t('licence.leaseExpiredNotice') }}
    </UiAlert>

    <UiFieldHint v-if="status.isOffline" class="mt-4" data-testid="licence-offline">
      {{ t('licence.offlineNote') }}
    </UiFieldHint>
  </UiCard>
</template>
