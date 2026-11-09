<script setup lang="ts">
/**
 * Asks the operator to confirm a password reset or a drop of one database user before the page sends
 * it, naming the user and the consequence. Dumb: the page holds which user and action are pending and
 * carries the action out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { PendingDatabaseUserAction } from '../../types/databaseUser'

/** Props accepted by {@link DatabaseUserActionConfirm}; the page mounts it only while an action waits. */
const props = defineProps<{
  /** The user and action awaiting confirmation. */
  pending: PendingDatabaseUserAction
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link DatabaseUserActionConfirm}. */
const emit = defineEmits<{
  /** The operator abandoned the action. */
  (e: 'close'): void
  /** The operator confirmed the action. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** Whether the pending action is the drop. */
const isDrop: ComputedRef<boolean> = computed(() => {
  return props.pending.action === 'drop'
})

/** The confirmation's title, naming the row and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const name = props.pending.user.fullName
  return isDrop.value
    ? t('databases.users.list.confirmDropTitle', { name })
    : t('databases.users.list.confirmResetPasswordTitle', { name })
})
</script>

<template>
  <UiConfirm
    :open="true"
    :title="confirmationTitle"
    :question="isDrop ? t('databases.users.list.confirmDrop') : t('databases.users.list.confirmResetPassword')"
    :confirm-label="isDrop ? t('databases.users.list.confirmDropAction') : t('databases.users.list.confirmResetPasswordAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('databases.users.list.working')"
    :destructive="isDrop"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
