<script setup lang="ts">
/**
 * The dedicated step that pins a remote server's host key. The panel read the server's host keys;
 * the administrator chooses one, compares its fingerprint with the server itself, and types or
 * pastes the fingerprint again. Only when what was typed is exactly the chosen fingerprint does
 * the confirm control turn on. A key accepted without that comparison would let an impostor
 * machine take the server's place, so the step cannot be skipped by a click.
 *
 * **The UI is advice, the panel is law.** The check here saves a round trip and slows a hasty
 * click; the panel compares the sent fingerprint with the scanned candidates itself and refuses a
 * mismatch. The component therefore sends the CHOSEN fingerprint, not the typed text, and errs
 * permissive: surrounding whitespace in a pasted fingerprint is ignored.
 *
 * Props in, events out: the page owns the store and the busy state.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiInput from '../ui/UiInput.vue'
import UiRadioGroup, { type RadioOption } from '../ui/UiRadioGroup.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { HostKeyCandidate, RemoteServerAction } from '../../types/migrationSource'

/** Props accepted by {@link HostKeyConfirm}. */
const props = defineProps<{
  /** The scanned host keys the administrator may pin. */
  candidates: HostKeyCandidate[]
  /** Whether the server stopped presenting the key that was pinned, which earns a warning. */
  changed: boolean
  /** Whether a key is already pinned, so the step may be abandoned in favour of it. */
  cancellable: boolean
  /** The action of the open server in flight, or `null`. */
  busy: RemoteServerAction | null
}>()

/** Events emitted by {@link HostKeyConfirm}. */
const emit = defineEmits<{
  /** The administrator confirmed a key; carries the chosen fingerprint. */
  (e: 'confirm', fingerprint: string): void
  /** The administrator asked for the host keys to be scanned again. */
  (e: 'rescan'): void
  /** The administrator abandoned the review and keeps the pinned key. */
  (e: 'cancel'): void
}>()

const { t } = useI18n()

/** The fingerprint chosen among the candidates, or empty before a choice. */
const selected: Ref<string> = ref('')

/** The fingerprint as retyped or pasted by the administrator. */
const typed: Ref<string> = ref('')

/** The candidates as radio choices: each names its algorithm and its fingerprint. */
const options: ComputedRef<RadioOption[]> = computed(() => {
  return props.candidates.map((candidate) => {
    return {
      value: candidate.fingerprint,
      label: t('migration.hostKey.optionLabel', { algorithm: candidate.algorithm, fingerprint: candidate.fingerprint }),
    }
  })
})

/** Whether what was typed is exactly the chosen fingerprint (surrounding whitespace aside). */
const matches: ComputedRef<boolean> = computed(() => {
  return selected.value !== '' && typed.value.trim() === selected.value
})

/** The retype field's error: shown once something was typed that is not the chosen fingerprint. */
const typedError: ComputedRef<string | null> = computed(() => {
  if (typed.value.trim() === '') {
    return null
  }
  return selected.value === '' ? t('migration.hostKey.chooseFirst') : matches.value ? null : t('migration.hostKey.mismatch')
})

/**
 * Sends the confirmation, only when the retype matches the choice.
 * @returns Nothing; the event is emitted synchronously.
 */
const confirm = (): void => {
  if (matches.value) {
    emit('confirm', selected.value)
  }
}

/**
 * Asks for a fresh scan.
 * @returns Nothing; the event is emitted synchronously.
 */
const rescan = (): void => {
  emit('rescan')
}

/**
 * Abandons the review.
 * @returns Nothing; the event is emitted synchronously.
 */
const cancel = (): void => {
  emit('cancel')
}

// A fresh scan replaces the candidates: a choice made among the old ones means nothing against them.
watch(
  (): HostKeyCandidate[] => {
    return props.candidates
  },
  (): void => {
    selected.value = ''
    typed.value = ''
  },
)
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4" data-testid="host-key-step">
    <UiSectionHeading :title="t('migration.hostKey.title')" :subtitle="t('migration.hostKey.intro')" />

    <UiAlert v-if="changed" variant="error">{{ t('migration.hostKey.changedWarning') }}</UiAlert>

    <p class="text-sm text-text-secondary">{{ t('migration.hostKey.compare') }}</p>

    <p v-if="candidates.length === 0" class="text-sm text-text-muted">{{ t('migration.hostKey.noCandidates') }}</p>

    <template v-else>
      <UiRadioGroup v-model="selected" :legend="t('migration.hostKey.legend')" :options="options" />
      <div class="flex flex-col gap-1.5">
        <UiInput
          v-model="typed"
          :label="t('migration.hostKey.typeLabel')"
          :error="typedError"
          mono
          autocomplete="off"
        />
        <p class="text-sm text-text-muted">{{ t('migration.hostKey.typeHint') }}</p>
      </div>
    </template>

    <div class="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
      <UiButton v-if="cancellable" variant="ghost" :disabled="busy !== null" @click="cancel">
        {{ t('migration.hostKey.closeReview') }}
      </UiButton>
      <UiButton variant="secondary" :disabled="busy !== null" @click="rescan">
        {{ busy === 'rescan' ? t('migration.hostKey.rescanning') : t('migration.hostKey.rescanAction') }}
      </UiButton>
      <UiButton :disabled="!matches || busy !== null" @click="confirm">
        {{ busy === 'confirm' ? t('migration.hostKey.confirming') : t('migration.hostKey.confirmAction') }}
      </UiButton>
    </div>
  </div>
</template>
