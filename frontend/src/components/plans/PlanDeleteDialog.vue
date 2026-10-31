<script setup lang="ts">
/**
 * The confirmation for deleting a plan, and — when accounts are still on it — the explanation of
 * why it cannot be deleted yet and the way to fix that (#216).
 *
 * **A deletion the panel will refuse is not offered.** The panel refuses to delete a plan any
 * account is on (`PlanInUse`). The dialog used to say so and still leave the Delete button enabled,
 * so the click could only answer with that refusal. Now, while the plan's account count is above
 * zero, the button is disabled and the reason is printed beside it, together with a link to the
 * accounts list narrowed to this plan and the instruction for moving each one ("Change plan" on the
 * account's page). The count may be a moment old; the server keeps the last word, and a refusal it
 * sends is still rendered by the page.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiLink from '../ui/UiLink.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import type { ManagedPlan } from '../../types/plan'

/** Props accepted by {@link PlanDeleteDialog}. */
const props = defineProps<{
  /**
   * The plan that would be deleted, or `null` while no deletion is being asked about. The dialog is
   * open exactly while this is set, and stays mounted otherwise so it can animate in and out.
   */
  plan: ManagedPlan | null
  /** Whether the deletion request is in flight. */
  acting: boolean
}>()

/** Events emitted by the dialog. */
const emit = defineEmits<{
  /**
   * Fired when the dialog should close; the page owns the open state.
   * @param e The event name.
   */
  (e: 'close'): void
  /**
   * Fired when the operator confirms. The PAGE performs the deletion.
   * @param e The event name.
   */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** Whether accounts are on the plan, in which case the panel would refuse the deletion. */
const inUse: ComputedRef<boolean> = computed(() => {
  return (props.plan?.accountCount ?? 0) > 0
})

/**
 * Closes the dialog unless the request is in flight.
 * @returns Nothing.
 */
const close = (): void => {
  if (!props.acting) {
    emit('close')
  }
}

/**
 * Confirms the deletion; never reached while the plan is in use, because the button is disabled.
 * @returns Nothing.
 */
const submit = (): void => {
  if (!props.acting && !inUse.value) {
    emit('confirm')
  }
}
</script>

<template>
  <UiModal
    :open="plan !== null"
    :title="t('plans.list.deleteTitle', { name: plan?.displayName ?? '' })"
    :close-label="t('common.close')"
    :dismissible="!acting"
    @close="close"
  >
    <UiSpinner v-if="acting" :label="t('plans.list.working')" />
    <div v-else-if="plan !== null && inUse" class="flex flex-col gap-3" data-testid="plan-delete-blocked">
      <div class="flex items-start gap-2.5">
        <UiIcon name="triangleAlert" size="sm" class="mt-0.5 shrink-0 text-warning" />
        <p class="min-w-0" data-testid="confirm-message">
          {{ t('plans.list.deleteBlocked', plan.accountCount) }}
        </p>
      </div>
      <p class="text-sm text-text-muted">{{ t('plans.list.deleteBlockedHowTo') }}</p>
      <UiLink :to="{ name: 'accounts', query: { plan: plan.id } }" trailing-icon="chevronRight">{{ t('plans.list.showAccountsOnPlan', plan.accountCount) }}</UiLink>
    </div>
    <div v-else class="flex items-start gap-2.5">
      <UiIcon name="triangleAlert" size="sm" class="mt-0.5 shrink-0 text-danger" />
      <p class="min-w-0" data-testid="confirm-message">{{ t('plans.list.deleteQuestionFree') }}</p>
    </div>

    <template #footer>
      <template v-if="!acting">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton variant="destructive" :disabled="inUse" @click="submit">{{ t('common.delete') }}</UiButton>
      </template>
    </template>
  </UiModal>
</template>
