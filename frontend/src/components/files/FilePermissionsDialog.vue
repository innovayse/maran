<script setup lang="ts">
/**
 * The permissions dialog: the octal mode typed or built from a read/write/execute grid (the two are
 * one value), a recursive switch for a folder with its own mode for the folders inside, and the owner shown read-only, because ownership is
 * not something a customer edits. Setuid, setgid and the sticky bit are not offered; the server
 * refuses them again regardless. Dumb by the usual contract.
 */
import { computed, ref, useId, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import {
  PERMISSION_BITS,
  PERMISSION_CLASSES,
  directoryModeFor,
  gridFromMode,
  isOctalMode,
  modeFromGrid,
  type PermissionBit,
  type PermissionClass,
  type PermissionGrid,
} from '../../utils/filePermissions'

/** Props accepted by {@link FilePermissionsDialog}. */
const props = defineProps<{
  /** The entry's name, for the title. */
  name: string
  /** The entry's current mode as a listing reports it. */
  currentMode: string
  /** Whether the entry is a folder, the only kind a recursive change means anything for. */
  isDirectory: boolean
  /** Who owns the entry, as `owner:group`, or `null` while unknown. Shown read-only. */
  ownerText: string | null
  /** Whether the request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last failed change, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FilePermissionsDialog}. */
const emit = defineEmits<{
  /**
   * Fired only for octal modes, carrying the files' three digits, whether the change is recursive and,
   * for a recursive change, the folders' mode (`null` otherwise).
   */
  (e: 'submit', mode: string, recursive: boolean, directoryMode: string | null): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The form's id, which the footer's submit button names through its `form` attribute. */
const formId: string = useId()

/** The mode as typed. */
const modeText: Ref<string> = ref(props.currentMode.slice(-3))

/** Whether the change reaches everything inside a folder. */
const recursive: Ref<boolean> = ref(false)

/** The folders' mode as typed, once the person has edited it; `null` while it follows the files' mode. */
const typedDirectoryMode: Ref<string | null> = ref(null)

/** Whether a submit was attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** The grid the typed mode describes. */
const grid: ComputedRef<PermissionGrid> = computed(() => {
  return gridFromMode(modeText.value)
})

/** Whether the folders' mode is asked for: a recursive change of a folder. */
const asksDirectoryMode: ComputedRef<boolean> = computed(() => {
  return props.isDirectory && recursive.value
})

/** The folders' mode shown: what the person typed, else the files' mode with execute wherever read is set. */
const directoryModeText: ComputedRef<string> = computed(() => {
  return typedDirectoryMode.value ?? directoryModeFor(modeText.value)
})

/** The validation message for the folders' mode, or `null`. */
const directoryModeError: ComputedRef<string | null> = computed(() => {
  return submitted.value && asksDirectoryMode.value && !isOctalMode(directoryModeText.value)
    ? t('filesDialogs.permissions.invalid')
    : null
})

/** The validation message for the typed mode, or `null`. */
const modeError: ComputedRef<string | null> = computed(() => {
  return submitted.value && !isOctalMode(modeText.value) ? t('filesDialogs.permissions.invalid') : null
})

/**
 * Ticks or clears one bit, rewriting the typed mode from the grid.
 * @param permissionClass The class the bit belongs to.
 * @param bit The bit.
 * @param held Whether it is now ticked.
 * @returns Nothing.
 */
const setBit = (permissionClass: PermissionClass, bit: PermissionBit, held: boolean): void => {
  const next = gridFromMode(modeText.value)
  next[permissionClass][bit] = held
  modeText.value = modeFromGrid(next)
}

/**
 * Keeps the folders' mode to octal digits as it is typed, which also ends following the files' mode.
 * @param value The text now in the field.
 * @returns Nothing.
 */
const setDirectoryMode = (value: string): void => {
  typedDirectoryMode.value = value.replace(/[^0-7]/g, '').slice(0, 3)
}

/**
 * Emits the modes when they are octal.
 * @returns Nothing.
 */
const submit = (): void => {
  submitted.value = true
  const directoryMode = asksDirectoryMode.value ? directoryModeText.value : null
  if (isOctalMode(modeText.value) && (directoryMode === null || isOctalMode(directoryMode))) {
    emit(
      'submit',
      modeText.value.padStart(3, '0'),
      props.isDirectory && recursive.value,
      directoryMode === null ? null : directoryMode.padStart(3, '0'),
    )
  }
}

// A typed mode keeps only octal digits, so the grid and the text can never disagree about a character.
watch(modeText, (value) => {
  const cleaned = value.replace(/[^0-7]/g, '').slice(0, 3)
  if (cleaned !== value) {
    modeText.value = cleaned
  }
})
</script>

<template>
  <UiModal
    :open="true"
    :title="t('filesDialogs.permissions.title', { name })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>
    <UiForm :id="formId" class="flex flex-col gap-4" @submit="submit">
      <p v-if="ownerText !== null" class="text-sm text-text-secondary">
        {{ t('filesDialogs.permissions.owner', { owner: ownerText }) }}
      </p>
      <UiInput
        v-model="modeText"
        :label="t('filesDialogs.permissions.mode')"
        :error="modeError"
        inputmode="numeric"
        mono
        required
      />
      <div class="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div
          v-for="permissionClass in PERMISSION_CLASSES"
          :key="permissionClass"
          role="group"
          :aria-label="t(`filesDialogs.permissions.classes.${permissionClass}`)"
          class="flex flex-col gap-1"
        >
          <p class="text-sm font-semibold text-text-primary">
            {{ t(`filesDialogs.permissions.classes.${permissionClass}`) }}
          </p>
          <UiCheckbox
            v-for="bit in PERMISSION_BITS"
            :key="bit"
            :model-value="grid[permissionClass][bit]"
            :label="t(`filesDialogs.permissions.bits.${bit}`)"
            @update:model-value="setBit(permissionClass, bit, $event)"
          />
        </div>
      </div>
      <UiSwitch
        v-if="isDirectory"
        v-model="recursive"
        :label="t('filesDialogs.permissions.recursive')"
      />
      <template v-if="asksDirectoryMode">
        <UiInput
          :model-value="directoryModeText"
          :label="t('filesDialogs.permissions.directoryMode')"
          :error="directoryModeError"
          inputmode="numeric"
          mono
          @update:model-value="setDirectoryMode"
        />
        <p class="text-sm text-text-secondary">{{ t('filesDialogs.permissions.directoryModeHint') }}</p>
      </template>
    </UiForm>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" :form="formId" :disabled="submitting">{{ t('filesDialogs.permissions.submit') }}</UiButton>
    </template>
  </UiModal>
</template>
