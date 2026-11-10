<script setup lang="ts">
/**
 * Stands in front of a mail screen's create form and says what the form cannot, for the person who
 * can act on it.
 *
 * **Who it speaks to.** Mail is switched on once for the whole server, by an administrator. While it
 * is off every create is refused with "ask the server administrator" — the right sentence for a
 * customer or a reseller and a circle for the administrator they would be asking. So an
 * administrator is told up front that mail is off and given the way to turn it on, here, and the form
 * that would only be refused is not offered. Everyone else loads nothing and sees the form, whose
 * refusal still carries the panel's own sentence.
 *
 * **Advice, not enforcement.** The status endpoint is the administrator's and every mail operation
 * re-checks on the server. A status that could not be read leaves the form offered.
 *
 * The turn-on request carries the host name the administrator types and the starting values the
 * panel states; the SPA holds no figure of its own.
 */
import { onMounted, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { useCallerRole } from '../../composables/useCallerRole'
import { useMailServerStore } from '../../stores/mailServer'

/** Events emitted by {@link MailServerGate}. */
const emit = defineEmits<{
  /** Fired once the panel has turned mail on, so the page can say so. */
  (e: 'enabled'): void
}>()

const { t } = useI18n()
const store = useMailServerStore()
const { isAdmin } = useCallerRole()

/** The host name the administrator typed. */
const hostname: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/**
 * Turns mail on, when the host name is not empty. Whether it is a name the server can present is the
 * panel's to say, and its localized refusal is what the person reads.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  if (hostname.value.trim().length === 0) {
    return
  }
  if (await store.enable(hostname.value.trim())) {
    emit('enabled')
  }
}

onMounted(() => {
  // Advice for the administrator only: nobody else can read the status, and nobody else can act on it.
  if (isAdmin.value) {
    void store.load()
  }
})
</script>

<template>
  <UiCard v-if="isAdmin && store.isOff">
    <UiSectionHeading class="mb-2" :title="t('mail.server.off.heading')" />
    <p class="mb-4 text-sm text-text-muted">{{ t('mail.server.off.description') }}</p>
    <UiAlert v-if="store.enableErrorMessage !== null" class="mb-4" variant="error">
      {{ store.enableErrorMessage }}
    </UiAlert>
    <UiForm @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiInput
          v-model="hostname"
          mono
          :label="t('mail.server.off.hostname')"
          :placeholder="t('mail.server.off.hostnamePlaceholder')"
          :error="submitted && hostname.trim().length === 0 ? t('mail.server.off.hostnameRequired') : null"
          required
        />
      </div>
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('mail.server.off.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="store.enabling">
          {{ t('mail.server.off.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
  <slot v-else />
</template>
