<script setup lang="ts">
/**
 * The notice a marketplace screen prints when its last request was refused or got no answer.
 *
 * Two cases, because they need different words. When the marketplace could not be reached (no answer
 * at all, or the panel's own `MarketplaceUnreachable` refusal) the notice says in the panel's words
 * that the panel and the installed modules are not affected, prints the panel's sentence when it sent
 * one, and offers a retry. Any other refusal is printed exactly as the panel worded it. The code
 * decides which case it is and nothing else: no text is looked up from it (rules/vue.md "Errors from
 * the server").
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'

/** Props accepted by {@link MarketplaceFailureNotice}. */
const props = defineProps<{
  /** The panel's sentence for the refusal, exactly as received, or `null`. */
  message: string | null
  /** True when the request got no answer at all. */
  unreachable: boolean
  /** The refusal's machine code, used to recognise an unreachable marketplace; `null` when none. */
  code: string | null
}>()

/** Events emitted by {@link MarketplaceFailureNotice}. */
const emit = defineEmits<{
  /** The administrator asked to try the request again. */
  (e: 'retry'): void
}>()

const { t } = useI18n()

/** Whether the marketplace itself could not be reached, as opposed to refusing a request. */
const isUnreachable: ComputedRef<boolean> = computed(() => {
  return props.unreachable || props.code === 'MarketplaceUnreachable'
})

/** Whether there is anything to print. */
const visible: ComputedRef<boolean> = computed(() => {
  return isUnreachable.value || props.message !== null
})
</script>

<template>
  <UiAlert v-if="visible && isUnreachable" variant="warning" data-testid="marketplace-unreachable">
    <p class="font-medium">{{ t('marketplace.unreachable.title') }}</p>
    <p>{{ message ?? t('marketplace.unreachable.description') }}</p>
    <template #actions>
      <UiButton variant="secondary" @click="emit('retry')">{{ t('marketplace.unreachable.retry') }}</UiButton>
    </template>
  </UiAlert>
  <UiAlert v-else-if="visible" variant="error" data-testid="marketplace-error">{{ message }}</UiAlert>
</template>
