<script setup lang="ts">
/**
 * The users allowed to open a site that asks for a name and a password.
 *
 * **Passwords are write-only.** The panel keeps a hash of each and never returns it, so a user who
 * is already on the site shows an empty password field: leaving it empty keeps their password,
 * typing one replaces it. A user added here must be given one. Removing a row removes the user: the
 * parent sends the whole list, and what is not in it is gone.
 *
 * With no user at all the site is open to everyone; adding the first one is what turns the
 * protection on, which the parent confirms before it sends.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiInput from '../ui/UiInput.vue'
import UiPasswordInput from '../ui/UiPasswordInput.vue'
import type { BasicAuthRow } from '../../types/siteOptions'

/** Props accepted by {@link SiteBasicAuthEditor}. */
defineProps<{
  /** The most users a site may carry, from the panel's limits. */
  maximum: number
  /** The shortest password the panel accepts, from its limits. */
  minimumPasswordLength: number
  /** Whether the editor is inert, e.g. while a save is in flight. */
  disabled: boolean
  /** The panel's message about the users, or `null` when the last refusal was not about them. */
  error: string | null
}>()

/** The users, in the order they are written to the credential file. */
const rows = defineModel<BasicAuthRow[]>({ required: true })

const { t } = useI18n()

/**
 * Replaces the row at one position with a changed copy.
 * @param index The row's position.
 * @param patch The fields to change.
 * @returns Nothing; the model is updated.
 */
const change = (index: number, patch: Partial<BasicAuthRow>): void => {
  rows.value = rows.value.map((row, position) => {
    return position === index ? { ...row, ...patch } : row
  })
}

/**
 * Appends an empty new user.
 * @returns Nothing; the model is updated.
 */
const add = (): void => {
  rows.value = [...rows.value, { name: '', password: '', existing: false }]
}

/**
 * Removes the user at one position.
 * @param index The row's position.
 * @returns Nothing; the model is updated.
 */
const remove = (index: number): void => {
  rows.value = rows.value.filter((_row, position) => {
    return position !== index
  })
}
</script>

<template>
  <div class="@container flex min-w-0 flex-col gap-4">
    <p v-if="rows.length === 0" class="text-sm text-text-muted">{{ t('siteOptions.basicAuth.empty') }}</p>

    <ul v-else class="flex min-w-0 flex-col gap-4">
      <li
        v-for="(row, index) in rows"
        :key="index"
        class="flex min-w-0 flex-col gap-3 rounded-control border border-border-subtle bg-surface-2 p-4"
      >
        <div class="grid min-w-0 gap-3 @lg:grid-cols-2">
          <UiInput
            :model-value="row.name"
            :label="t('siteOptions.basicAuth.nameLabel', { number: index + 1 })"
            :disabled="disabled || row.existing"
            autocomplete="off"
            mono
            @update:model-value="change(index, { name: $event })"
          />
          <UiPasswordInput
            :model-value="row.password"
            :label="t('siteOptions.basicAuth.passwordLabel', { number: index + 1 })"
            :placeholder="row.existing ? t('siteOptions.basicAuth.keepPlaceholder') : ''"
            :disabled="disabled"
            autocomplete="new-password"
            generate
            @update:model-value="change(index, { password: $event })"
          />
        </div>
        <div>
          <UiButton variant="secondary" :disabled="disabled" @click="remove(index)">
            {{ t('siteOptions.basicAuth.remove', { number: index + 1 }) }}
          </UiButton>
        </div>
      </li>
    </ul>

    <p v-if="error !== null" role="alert" class="text-sm text-danger wrap-anywhere">{{ error }}</p>

    <div class="flex flex-wrap items-center gap-3">
      <UiButton variant="secondary" :disabled="disabled || rows.length >= maximum" @click="add">
        {{ t('siteOptions.basicAuth.add') }}
      </UiButton>
      <UiFieldHint>
        {{ t('siteOptions.basicAuth.hint', { count: rows.length, maximum, length: minimumPasswordLength }) }}
      </UiFieldHint>
    </div>
  </div>
</template>
