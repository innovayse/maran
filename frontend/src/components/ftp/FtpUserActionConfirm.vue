<script setup lang="ts">
/**
 * Asks the operator to confirm a reset or a removal of one login before the page sends it, naming
 * the login and the consequence. Dumb: the page holds which login and action are pending and carries
 * the action out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { FtpUser, FtpUserAction } from '../../types/ftpUser'

/** A login and the action the operator started on it; the two are only ever known together. */
export interface PendingFtpUserAction {
  /** The login the action is aimed at. */
  login: FtpUser
  /** Which action that login is waiting on. */
  action: FtpUserAction
}

/** Props accepted by {@link FtpUserActionConfirm}; the page mounts it only while an action waits. */
const props = defineProps<{
  /** The login and action awaiting confirmation. */
  pending: PendingFtpUserAction
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link FtpUserActionConfirm}. */
const emit = defineEmits<{
  /** The operator abandoned the action. */
  (e: 'close'): void
  /** The operator confirmed the action. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the row and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const name = props.pending.login.fullName
  return props.pending.action === 'remove'
    ? t('ftp.list.confirmRemoveTitle', { name })
    : t('ftp.list.confirmResetPasswordTitle', { name })
})

/** The consequence the confirmation asks the operator to weigh. */
const confirmationQuestion: ComputedRef<string> = computed(() => {
  return props.pending.action === 'remove' ? t('ftp.list.confirmRemove') : t('ftp.list.confirmResetPassword')
})

/** The confirm button's label, naming the action rather than a bare "Confirm" (#218). */
const confirmationLabel: ComputedRef<string> = computed(() => {
  return props.pending.action === 'remove'
    ? t('ftp.list.confirmRemoveAction')
    : t('ftp.list.confirmResetPasswordAction')
})
</script>

<template>
  <UiConfirm
    :open="true"
    :title="confirmationTitle"
    :question="confirmationQuestion"
    :confirm-label="confirmationLabel"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('ftp.list.working')"
    :destructive="pending.action === 'remove'"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
