<script setup lang="ts">
/**
 * The actions one reseller offers its administrator: change the envelope, show the reseller's
 * accounts and plans, suspend or reactivate, and apart, under a rule, delete.
 *
 * Dumb: it emits which action was clicked and the reseller page asks the confirming question. Which
 * of suspend and reactivate applies depends on the status, so while the reseller loads no action is
 * offered and a placeholder holds the row. The two links are real links, not buttons that navigate.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import type { ResellerStatus } from '../../types/reseller'

/** Props accepted by {@link ResellerActionsCard}. */
defineProps<{
  /** The reseller's identity, for the links; the card is drawn only once it is known. */
  resellerId: string
  /** The reseller's lifecycle state; `null` is the one state in which it is still on its way. */
  status: ResellerStatus | null
}>()

/** Events emitted by {@link ResellerActionsCard}; the page owns every dialog they open. */
const emit = defineEmits<{
  /** Fired when the administrator asks to suspend the reseller. */
  (e: 'suspend'): void
  /** Fired when the administrator asks to reactivate the reseller. */
  (e: 'reactivate'): void
  /** Fired when the administrator asks to delete the reseller. */
  (e: 'delete'): void
}>()

const { t } = useI18n()
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading :title="t('common.actions')" />
    <UiSkeleton v-if="status === null" shape="block" />
    <div v-else class="flex flex-wrap items-center gap-2">
      <UiButton variant="secondary" :to="{ name: 'reseller-edit', params: { id: resellerId } }">
        {{ t('resellers.detail.editEnvelope') }}
      </UiButton>
      <UiButton variant="secondary" :to="{ name: 'accounts', query: { reseller: resellerId } }">
        {{ t('resellers.detail.showAccounts') }}
      </UiButton>
      <UiButton variant="secondary" :to="{ name: 'plans', query: { reseller: resellerId } }">
        {{ t('resellers.detail.showPlans') }}
      </UiButton>
      <UiButton v-if="status === 'active'" variant="secondary" @click="emit('suspend')">
        {{ t('resellers.detail.suspend') }}
      </UiButton>
      <UiButton v-else variant="secondary" @click="emit('reactivate')">
        {{ t('resellers.detail.reactivate') }}
      </UiButton>
    </div>
    <!-- Deletion sits apart, under a rule: it is the one action that cannot be undone, and it must
         not read as a sibling of the ones above it. -->
    <div v-if="status !== null" class="border-t border-border-subtle pt-4">
      <UiButton variant="destructive" @click="emit('delete')">{{ t('common.delete') }}</UiButton>
    </div>
  </UiCard>
</template>
