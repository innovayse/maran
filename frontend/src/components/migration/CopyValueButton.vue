<script setup lang="ts">
/**
 * A secondary button that copies one value to the clipboard and says so. Used for the values an
 * administrator must carry to another server by hand: a key fingerprint, a one-time token, a
 * generated credential.
 *
 * A clipboard write can fail in a locked-down browser; the failure is swallowed on purpose and
 * leaves the control unchanged, because the value is always shown as selectable text beside it.
 * The result is also spoken from a status region that is in the page before it has anything to say,
 * since a change of a button's name is not announced by itself.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiIcon from '../ui/UiIcon.vue'

/** Props accepted by {@link CopyValueButton}. */
const props = defineProps<{
  /** The value to put on the clipboard. */
  value: string
  /** The control's text and accessible name while nothing was copied, already translated. */
  label: string
}>()

const { t } = useI18n()

/** Whether the value was copied during this showing, so the control can say so. */
const copied: Ref<boolean> = ref(false)

/**
 * Copies the value to the clipboard.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copy = async (): Promise<void> => {
  try {
    await navigator.clipboard.writeText(props.value)
    copied.value = true
  } catch {
    copied.value = false
  }
}
</script>

<template>
  <span class="inline-flex">
    <UiButton variant="secondary" @click="copy">
      <UiIcon :name="copied ? 'check' : 'copy'" size="sm" />
      {{ copied ? t('common.copied') : label }}
    </UiButton>
    <span class="sr-only" role="status">{{ copied ? t('common.copied') : '' }}</span>
  </span>
</template>
