<script setup lang="ts">
/**
 * Asks the person to confirm the removal of one mailbox before the page sends it, naming the
 * address and the consequence: the mail the mailbox holds is deleted with it. Dumb: the page holds
 * which mailbox is pending and carries the removal out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { Mailbox } from '../../types/mailbox'

/** Props accepted by {@link MailboxRemoveConfirm}; the page mounts it only while a removal waits. */
const props = defineProps<{
  /** The mailbox awaiting removal. */
  mailbox: Mailbox
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link MailboxRemoveConfirm}. */
const emit = defineEmits<{
  /** The person abandoned the removal. */
  (e: 'close'): void
  /** The person confirmed the removal. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the mailbox. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  return t('mail.mailboxes.confirmRemoveTitle', { name: props.mailbox.address })
})
</script>

<template>
  <UiConfirm
    :open="true"
    :title="confirmationTitle"
    :question="t('mail.mailboxes.confirmRemove')"
    :confirm-label="t('mail.mailboxes.confirmRemoveAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('mail.working')"
    destructive
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
