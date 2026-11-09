<script setup lang="ts">
/**
 * Asks the person to confirm the removal of one alias, forwarder or catch-all before the page sends
 * it, naming the address and the consequence: mail sent to it is refused afterwards. Dumb: the page
 * holds which alias is pending and carries the removal out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { MailAlias } from '../../types/mailAlias'

/** Props accepted by {@link MailAliasRemoveConfirm}; the page mounts it only while a removal waits. */
const props = defineProps<{
  /** The alias awaiting removal. */
  alias: MailAlias
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link MailAliasRemoveConfirm}. */
const emit = defineEmits<{
  /** The person abandoned the removal. */
  (e: 'close'): void
  /** The person confirmed the removal. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** The confirmation's title, naming the address. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  return t('mail.aliases.confirmRemoveTitle', { name: props.alias.address })
})
</script>

<template>
  <UiConfirm
    :open="true"
    :title="confirmationTitle"
    :question="t('mail.aliases.confirmRemove')"
    :confirm-label="t('mail.aliases.confirmRemoveAction')"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('mail.working')"
    destructive
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
