<script setup lang="ts">
/**
 * The invite-a-team-member form: the login name, the address, the features, and the client-side mirror
 * of the server's validator (`InviteSubUserCommandValidator`).
 *
 * It is its own panel (`UiCard` with a section heading), so the page composes it like any other panel.
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer and
 * refuses to emit until its own rules pass; whatever it lets through, the server re-validates and its
 * localized message is what the owner reads on failure.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import SubUserFeaturePicker from './SubUserFeaturePicker.vue'
import type { AccountFeatureName } from '../../types/accountFeature'
import type { GrantableFeature, InviteSubUserRequest } from '../../types/subUser'

/**
 * The server's login-name alphabet, character for character (letters, digits, dot, underscore, dash).
 * Advice that saves a round trip; the server re-validates it.
 */
const USERNAME_PATTERN = /^[a-zA-Z0-9._-]+$/

/** The longest login name the server accepts. */
const MAX_USERNAME_LENGTH = 64

/** The shape of an address the client bothers to refuse: something, an at sign, something. */
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+$/

/** Props accepted by {@link SubUserInviteForm}. */
defineProps<{
  /** Every feature the owner may give, from the panel. */
  features: GrantableFeature[]
  /** Whether an invite request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link SubUserInviteForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: InviteSubUserRequest): void
}>()

const { t } = useI18n()

/** The login name the person will sign in with. */
const username: Ref<string> = ref('')

/** The address the invitation is mailed to. */
const email: Ref<string> = ref('')

/** The ticked feature ids. */
const selected: Ref<AccountFeatureName[]> = ref([])

/** Whether a submit has been attempted, so nothing turns red before the owner has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the login name, or `null`. */
const usernameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (username.value.length === 0) {
    return t('subUsers.form.errors.usernameRequired')
  }
  return username.value.length > MAX_USERNAME_LENGTH || !USERNAME_PATTERN.test(username.value)
    ? t('subUsers.form.errors.usernameInvalid')
    : null
})

/** Validation message for the address, or `null`. */
const emailError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (email.value.length === 0) {
    return t('subUsers.form.errors.emailRequired')
  }
  return EMAIL_PATTERN.test(email.value) ? null : t('subUsers.form.errors.emailInvalid')
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (usernameError.value !== null || emailError.value !== null) {
    return
  }
  emit('submit', { username: username.value, email: email.value, features: selected.value })
}

/**
 * Returns every field to its starting value, so the next invitation starts from a clean form and a
 * form still showing what it just sent does not invite the same submit twice.
 * @returns Nothing.
 */
const reset = (): void => {
  username.value = ''
  email.value = ''
  selected.value = []
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('subUsers.form.heading')" />
    <UiForm @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiInput
          v-model="username"
          :label="t('subUsers.form.fields.username')"
          :placeholder="t('subUsers.form.placeholders.username')"
          :error="usernameError"
          required
        />
        <UiInput
          v-model="email"
          :label="t('subUsers.form.fields.email')"
          :placeholder="t('subUsers.form.placeholders.email')"
          :error="emailError"
          inputmode="email"
          required
        />
      </div>
      <p class="mt-5 mb-1 text-sm font-medium text-text-primary">{{ t('subUsers.form.fields.features') }}</p>
      <SubUserFeaturePicker
        v-model="selected"
        :features="features"
        :group-label="t('subUsers.features.groupLabel')"
      />
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('subUsers.form.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="submitting">
          {{ t('subUsers.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
