<script setup lang="ts">
/**
 * The privileges editor for one database user: which privileges it holds on which database.
 *
 * The user picks one database at a time (a database it already has a grant on, or one the owning
 * account has that it has none on), ticks privileges from the closed set the panel serves, and
 * saves. The presets are quick fills over the same checkboxes: choosing one ticks its privileges and
 * nothing else, so what is saved is always the individual set on screen and the backend validates a
 * preset and a hand-ticked set identically. Saving an empty set removes the user's grant on that
 * database, and the dialog says so.
 *
 * The privileges, their names and the presets are the panel's (rules/vue.md "Data comes from the
 * backend"); this dialog holds no list of them. Dumb by the usual contract: props in, emits out,
 * the page performs the request.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBadge from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiModal from '../ui/UiModal.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type {
  DatabasePrivilegeCatalogue,
  DatabasePrivilegeKey,
  DatabasePrivilegePreset,
  DatabaseUserDetail,
} from '../../types/databaseUser'

/** Props accepted by {@link DatabaseUserPrivilegesDialog}. */
const props = defineProps<{
  /** The user being edited, with the databases a grant may still be added on. */
  user: DatabaseUserDetail
  /** The grantable privileges and the presets over them. */
  catalogue: DatabasePrivilegeCatalogue
  /** Whether a save is in flight, which disables the controls. */
  saving: boolean
  /** The backend-localized reason the last read or save failed, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link DatabaseUserPrivilegesDialog}. */
const emit = defineEmits<{
  /** The operator saved: the complete privilege set on one database; empty removes the grant. */
  (e: 'save', databaseId: string, privileges: DatabasePrivilegeKey[]): void
  /** The operator closed the editor. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The database the checkboxes below describe; empty until one is chosen. */
const databaseId: Ref<string> = ref('')

/** The privileges ticked for {@link databaseId}. */
const ticked: Ref<DatabasePrivilegeKey[]> = ref([])

/** Every database the editor can set privileges on: the granted ones, then the grantable ones. */
const databaseOptions: ComputedRef<SelectOption[]> = computed(() => {
  const granted = props.user.grants.map((grant) => {
    return { value: grant.databaseId, label: grant.databaseFullName }
  })
  const available = props.user.grantableDatabases.map((database) => {
    return { value: database.id, label: database.fullName }
  })
  return [...granted, ...available]
})

/** The privileges the panel holds for the chosen database, empty when it has no grant there. */
const heldPrivileges: ComputedRef<DatabasePrivilegeKey[]> = computed(() => {
  return props.user.grants.find((grant) => {return grant.databaseId === databaseId.value})?.privileges ?? []
})

/** Whether the ticked set is the empty one, which saves as "remove the grant". */
const removesGrant: ComputedRef<boolean> = computed(() => {
  return ticked.value.length === 0
})

/** Whether the ticked set differs from what the panel holds, so Save has something to send. */
const changed: ComputedRef<boolean> = computed(() => {
  const held = new Set(heldPrivileges.value)
  return ticked.value.length !== held.size || ticked.value.some((key) => {return !held.has(key)})
})

/**
 * Chooses the database to edit and shows what the panel holds there.
 * @param id The database chosen.
 * @returns Nothing.
 */
const chooseDatabase = (id: string): void => {
  databaseId.value = id
  ticked.value = [...heldPrivileges.value]
}

/**
 * Whether one privilege is currently ticked.
 * @param key The privilege.
 * @returns True when ticked.
 */
const isTicked = (key: DatabasePrivilegeKey): boolean => {
  return ticked.value.includes(key)
}

/**
 * Ticks or unticks one privilege.
 * @param key The privilege.
 * @param on Whether it is now ticked.
 * @returns Nothing.
 */
const toggle = (key: DatabasePrivilegeKey, on: boolean): void => {
  ticked.value = on ? [...ticked.value.filter((held) => {return held !== key}), key] : ticked.value.filter((held) => {return held !== key})
}

/**
 * Fills the checkboxes from a preset, replacing whatever was ticked.
 * @param preset The preset chosen.
 * @returns Nothing.
 */
const applyPreset = (preset: DatabasePrivilegePreset): void => {
  ticked.value = [...preset.privileges]
}

/**
 * Sends the ticked set, in the catalogue's order so the request does not depend on click order.
 * @returns Nothing.
 */
const save = (): void => {
  if (databaseId.value === '') {
    return
  }
  const ordered = props.catalogue.privileges
    .map((privilege) => {return privilege.key})
    .filter((key) => {return ticked.value.includes(key)})
  emit('save', databaseId.value, ordered)
}

// A save re-reads the user, so the held set moves under the editor: show what the panel now holds.
watch(
  (): DatabaseUserDetail => {return props.user},
  (): void => {
    if (databaseId.value !== '') {
      ticked.value = [...heldPrivileges.value]
    }
  },
)
</script>

<template>
  <UiModal
    :open="true"
    :title="t('databases.users.privileges.title', { name: user.fullName })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>

    <section class="mb-4" :aria-label="t('databases.users.privileges.heldHeading')">
      <h3 class="mb-2 text-base font-medium text-text-primary">{{ t('databases.users.privileges.heldHeading') }}</h3>
      <p v-if="user.grants.length === 0" class="text-sm text-text-muted">{{ t('databases.users.privileges.noneHeld') }}</p>
      <ul v-else class="flex flex-col gap-2">
        <li v-for="grant in user.grants" :key="grant.databaseId" class="flex flex-wrap items-center gap-2 text-sm">
          <span class="break-all font-mono text-text-primary">{{ grant.databaseFullName }}</span>
          <UiBadge variant="neutral">{{ t('databases.users.privileges.count', { n: grant.privileges.length }, grant.privileges.length) }}</UiBadge>
        </li>
      </ul>
    </section>

    <UiSelect
      :model-value="databaseId"
      :label="t('databases.users.privileges.databaseLabel')"
      :placeholder="t('databases.users.privileges.databasePlaceholder')"
      :options="databaseOptions"
      :disabled="saving"
      @update:model-value="chooseDatabase"
    />

    <div v-if="databaseId !== ''" class="mt-4 flex flex-col gap-4">
      <div v-if="catalogue.presets.length > 0" class="flex flex-wrap items-center gap-2">
        <span class="text-sm text-text-muted">{{ t('databases.users.privileges.presetsLabel') }}</span>
        <UiButton
          v-for="preset in catalogue.presets"
          :key="preset.key"
          variant="secondary"
          :disabled="saving"
          @click="applyPreset(preset)"
        >{{ preset.name }}</UiButton>
      </div>

      <div class="grid gap-x-4 gap-y-2 sm:grid-cols-2">
        <UiCheckbox
          v-for="privilege in catalogue.privileges"
          :key="privilege.key"
          :model-value="isTicked(privilege.key)"
          :label="privilege.name"
          :disabled="saving"
          @update:model-value="toggle(privilege.key, $event)"
        />
      </div>

      <p v-if="removesGrant && heldPrivileges.length > 0" class="text-sm text-text-muted">
        {{ t('databases.users.privileges.removesGrant') }}
      </p>
    </div>

    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.close') }}</UiButton>
      <UiButton :disabled="saving || databaseId === '' || !changed" @click="save">
        {{ saving ? t('common.saving') : t('databases.users.privileges.save') }}
      </UiButton>
    </template>
  </UiModal>
</template>
