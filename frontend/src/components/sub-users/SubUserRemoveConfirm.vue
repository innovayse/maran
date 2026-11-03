<script setup lang="ts">
/**
 * Asks the owner to confirm removing one sub-user (or cancelling a pending offer) before the page sends it, naming the person and the
 * consequence. Dumb: the page holds which sub-user is pending and carries the removal out on `confirm`.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiConfirm from '../ui/UiConfirm.vue'
import type { SubUser } from '../../types/subUser'

/** Props accepted by {@link SubUserRemoveConfirm}. */
const props = defineProps<{
  /** The sub-user awaiting confirmation, or `null` while none is (the dialog is closed). */
  subUser: SubUser | null
  /** Whether the confirmed request is in flight, which holds the dialog's spinner. */
  acting: boolean
}>()

/** Events emitted by {@link SubUserRemoveConfirm}. */
const emit = defineEmits<{
  /** The owner abandoned the removal. */
  (e: 'close'): void
  /** The owner confirmed the removal. */
  (e: 'confirm'): void
}>()

const { t } = useI18n()

/** Which wording applies: cancelling a pending offer says nothing of a deleted login. */
const section: ComputedRef<'remove' | 'cancelOffer'> = computed(() => {
  return props.subUser?.isOffer === true ? 'cancelOffer' : 'remove'
})

/** The confirmation's title, naming the person. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  return t(`subUsers.${section.value}.title`, { name: props.subUser?.username ?? '' })
})
</script>

<template>
  <UiConfirm
    :open="subUser !== null"
    :title="confirmationTitle"
    :question="t(`subUsers.${section}.question`)"
    :confirm-label="t(`subUsers.${section}.confirm`)"
    :cancel-label="t('common.cancel')"
    :close-label="t('common.close')"
    :acting="acting"
    :acting-label="t('subUsers.working')"
    @close="emit('close')"
    @confirm="emit('confirm')"
  />
</template>
