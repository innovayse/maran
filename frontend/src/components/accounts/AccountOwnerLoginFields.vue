<script setup lang="ts">
/**
 * How the new account's owner gets a login (issue #194): by an invitation mailed to the owner
 * address, which is the default and keeps the password with the owner alone, or by an initial
 * password the operator types or generates, for a host that cannot send mail. The second choice
 * is a credential the operator knows, so the hint says the owner must replace it at the first
 * sign-in and the field is a password field that is never revealed on its own.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiPasswordInput from '../ui/UiPasswordInput.vue'
import UiRadioGroup, { type RadioOption } from '../ui/UiRadioGroup.vue'
import type { OwnerLoginMode } from '../../types/account'

defineProps<{
  /** Message under the password field, already translated; `null` when it is fine. */
  passwordError: string | null
}>()

/** The chosen way of making the login (`v-model:mode`). */
const mode = defineModel<OwnerLoginMode>('mode', { required: true })

/** The initial password as typed (`v-model:password`). */
const password = defineModel<string>('password', { required: true })

const { t } = useI18n()

/** The two ways, labelled in the interface language. */
const options: ComputedRef<RadioOption[]> = computed(() => {
  return [
    { value: 'invitation', label: t('accounts.form.loginMode.invitation') },
    { value: 'initialPassword', label: t('accounts.form.loginMode.initialPassword') },
  ]
})

/**
 * Takes the radio's value as a login mode.
 * @param value The option picked; always one of {@link options}.
 */
const onModeChange = (value: string): void => {
  mode.value = value === 'initialPassword' ? 'initialPassword' : 'invitation'
}
</script>

<template>
  <div class="flex flex-col gap-3 md:col-span-2">
    <UiRadioGroup
      :model-value="mode"
      :legend="t('accounts.form.fields.loginMode')"
      :options="options"
      @update:model-value="onModeChange"
    />
    <div v-if="mode === 'initialPassword'" class="flex flex-col gap-1">
      <UiPasswordInput
        v-model="password"
        :label="t('accounts.form.fields.initialPassword')"
        :error="passwordError"
        autocomplete="new-password"
        required
        generate
      />
      <UiFieldHint>{{ t('accounts.form.hints.initialPassword') }}</UiFieldHint>
    </div>
    <UiFieldHint v-else>{{ t('accounts.form.hints.invitation') }}</UiFieldHint>
  </div>
</template>
