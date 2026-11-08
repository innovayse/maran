<script setup lang="ts">
/**
 * Asks the person to confirm a DKIM key rotation or a removal of one mail domain before the page
 * sends it, naming the domain and the consequence. Dumb: the page holds which domain and action are
 * pending and carries the action out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { PendingMailDomainAction } from '../../types/mailDomain'

/** Props accepted by {@link MailDomainActionConfirm}; the page mounts it only while an action waits. */
const props = defineProps<{
  /** The domain and action awaiting confirmation. */
  pending: PendingMailDomainAction
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link MailDomainActionConfirm}. */
const emit = defineEmits<{
  /** The person abandoned the action. */
  (e: 'close'): void
  /** The person confirmed the action. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the row and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const name = props.pending.domain.name
  return props.pending.action === 'remove'
    ? t('mail.domains.confirmRemoveTitle', { name })
    : t('mail.domains.confirmRotateTitle', { name })
})

/** The consequence the confirmation asks the person to weigh. */
const confirmationQuestion: ComputedRef<string> = computed(() => {
  return props.pending.action === 'remove' ? t('mail.domains.confirmRemove') : t('mail.domains.confirmRotate')
})

/** The confirm button's label, naming the action rather than a bare "Confirm". */
const confirmationLabel: ComputedRef<string> = computed(() => {
  return props.pending.action === 'remove'
    ? t('mail.domains.confirmRemoveAction')
    : t('mail.domains.confirmRotateAction')
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
    :acting-label="t('mail.working')"
    :destructive="pending.action === 'remove'"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
