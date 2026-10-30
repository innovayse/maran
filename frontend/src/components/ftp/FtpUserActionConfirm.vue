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

/** Props accepted by {@link FtpUserActionConfirm}. */
const props = defineProps<{
  /** The login awaiting confirmation, or `null` while none is (the dialog is closed). */
  login: FtpUser | null
  /** Which action that login is waiting on. */
  action: FtpUserAction
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
  const name = props.login?.fullName ?? ''
  return props.action === 'remove'
    ? t('ftp.list.confirmRemoveTitle', { name })
    : t('ftp.list.confirmResetPasswordTitle', { name })
})

/** The consequence the confirmation asks the operator to weigh. */
const confirmationQuestion: ComputedRef<string> = computed(() => {
  return props.action === 'remove' ? t('ftp.list.confirmRemove') : t('ftp.list.confirmResetPassword')
})

/** The confirm button's label, naming the action rather than a bare "Confirm" (#218). */
const confirmationLabel: ComputedRef<string> = computed(() => {
  return props.action === 'remove'
    ? t('ftp.list.confirmRemoveAction')
    : t('ftp.list.confirmResetPasswordAction')
})
</script>

<template>
  <UiConfirm
    :open="login !== null"
    :title="confirmationTitle"
    :question="confirmationQuestion"
    :confirm-label="confirmationLabel"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('ftp.list.working')"
    :destructive="action === 'remove'"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
