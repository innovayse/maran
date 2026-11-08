<script setup lang="ts">
/**
 * Replaces the destinations of one alias, forwarder or catch-all, from the aliases list. The
 * address and the kind stay as they are: changing either is a different alias, so it is a removal
 * and a new one. The textarea starts from the current destinations, one per line.
 *
 * The client-side checks are only the ones the panel states in its choices (the most destinations);
 * the server is the authority and its already-localized refusal is shown inside the dialog.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import { useMailAliasesStore } from '../../stores/mailAliases'
import { parseMailTargets } from '../../utils/mailTargets'
import type { MailLimits } from '../../types/mailChoice'
import type { MailAlias } from '../../types/mailAlias'

/** Props accepted by {@link MailAliasEditDialog}; the page mounts it only while an alias is being edited. */
const props = defineProps<{
  /** The alias being changed. */
  alias: MailAlias
  /** The bounds the panel states for the form, or `null` while they have not arrived. */
  limits: MailLimits | null
}>()

/** Events emitted by {@link MailAliasEditDialog}. */
const emit = defineEmits<{
  /** The dialog should close without a change. */
  (e: 'close'): void
  /** The panel accepted the change. */
  (e: 'saved'): void
}>()

const { t } = useI18n()
const store = useMailAliasesStore()

/** The destinations as typed, one per line; starts at the current ones. */
const targets: Ref<string> = ref(props.alias.targets.join('\n'))

/** Whether a save has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the destinations, or `null`. */
const targetsError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const entries = parseMailTargets(targets.value)
  if (entries.length === 0) {
    return t('mail.aliases.form.errors.targetsRequired')
  }
  const most = props.limits?.maximumAliasTargets
  return most !== undefined && entries.length > most ? t('mail.aliases.form.errors.targetsTooMany', { n: most }) : null
})

/**
 * Closes the dialog unless a request is in flight.
 * @returns Nothing.
 */
const close = (): void => {
  if (!store.acting) {
    emit('close')
  }
}

/**
 * Sends the change, and reports success to the page; a refusal stays on screen.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (targetsError.value !== null) {
    return
  }
  if (await store.update(props.alias.id, { targets: parseMailTargets(targets.value) })) {
    emit('saved')
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('mail.aliases.edit.title', { name: alias.address })"
    :close-label="t('common.close')"
    :dismissible="!store.acting"
    @close="close"
  >
    <UiSpinner v-if="store.acting" :label="t('mail.working')" />
    <UiForm v-else @submit="submit">
      <div class="flex flex-col gap-4">
        <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>
        <UiTextarea
          v-model="targets"
          mono
          :rows="4"
          :label="t('mail.aliases.form.fields.targets')"
          :error="targetsError"
          required
        />
        <p class="text-sm text-text-muted">{{ t('mail.aliases.edit.hint') }}</p>
      </div>
    </UiForm>

    <template #footer>
      <template v-if="!store.acting">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton @click="submit">{{ t('mail.aliases.edit.submit') }}</UiButton>
      </template>
    </template>
  </UiModal>
</template>
