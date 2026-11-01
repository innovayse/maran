<script setup lang="ts">
/**
 * The actions one hosting account offers its administrator: suspend or reactivate, change the plan,
 * change the owner's address, and — apart, under a rule — delete.
 *
 * Dumb: it emits which action was clicked and the account page asks the confirming question. Which
 * of suspend and reactivate applies depends on the account's status, so while the account loads no
 * action is offered and a placeholder holds the row.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSkeleton from '../ui/UiSkeleton.vue'
import type { AccountStatus } from '../../types/account'

/** Props accepted by {@link AccountActionsCard}. */
defineProps<{
  /** The account's lifecycle state, or `null` while it has not been read. */
  status: AccountStatus | null
  /** Whether the account is still on its way, so no action is offered yet. */
  loading: boolean
}>()

/** Events emitted by {@link AccountActionsCard}; the page owns every dialog they open. */
const emit = defineEmits<{
  /**
   * Fired when the operator asks to suspend the account.
   * @param e The event name.
   */
  (e: 'suspend'): void
  /**
   * Fired when the operator asks to reactivate the account.
   * @param e The event name.
   */
  (e: 'reactivate'): void
  /**
   * Fired when the operator asks to change the account's plan.
   * @param e The event name.
   */
  (e: 'editPlan'): void
  /**
   * Fired when the operator asks to change the owner's address.
   * @param e The event name.
   */
  (e: 'editOwnerEmail'): void
  /**
   * Fired when the operator asks to delete the account.
   * @param e The event name.
   */
  (e: 'delete'): void
}>()

const { t } = useI18n()
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading :title="t('common.actions')" />
    <!-- Which actions apply depends on the account's status, so none is offered before it
         is known; a placeholder holds the row. -->
    <UiSkeleton v-if="loading" shape="block" />
    <div v-else class="flex flex-wrap items-center gap-2">
      <UiButton v-if="status === 'active'" variant="secondary" @click="emit('suspend')">
        {{ t('accounts.detail.suspend') }}
      </UiButton>
      <UiButton v-else variant="secondary" @click="emit('reactivate')">
        {{ t('accounts.detail.reactivate') }}
      </UiButton>
      <UiButton variant="secondary" @click="emit('editPlan')">
        {{ t('accounts.edit.planAction') }}
      </UiButton>
      <UiButton variant="secondary" @click="emit('editOwnerEmail')">
        {{ t('accounts.edit.ownerEmailAction') }}
      </UiButton>
    </div>
    <!-- Deletion sits apart, under a rule: it is the one action that cannot be undone, and it
         must not read as a sibling of the two above it. -->
    <div v-if="!loading" class="border-t border-border-subtle pt-4">
      <UiButton variant="destructive" @click="emit('delete')">
        {{ t('common.delete') }}
      </UiButton>
    </div>
  </UiCard>
</template>
