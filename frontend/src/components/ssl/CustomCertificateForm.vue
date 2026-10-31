<script setup lang="ts">
/**
 * The form that installs a customer's own certificate: the chain, the private key, and the
 * submit and cancel buttons.
 *
 * It holds the pasted material and nothing else. It does not call the panel: a submit hands the
 * pair to the parent, which installs it and closes the form on success. A refusal leaves the form
 * open, so the material stays and the operator corrects only the half the panel named. Closing the
 * form unmounts it, which is what forgets the key.
 *
 * The form's own "required" message comes first; otherwise the panel's refusal for a field, passed
 * in by the parent, is shown under that field as the panel worded it.
 */
import { computed, ref, type ComputedRef, type Ref } from "vue";
import { useI18n } from "vue-i18n";
import UiButton from "../ui/UiButton.vue";
import UiCard from "../ui/UiCard.vue";
import UiFieldHint from "../ui/UiFieldHint.vue";
import UiForm from "../ui/UiForm.vue";
import UiSectionHeading from "../ui/UiSectionHeading.vue";
import UiTextarea from "../ui/UiTextarea.vue";
import type { CustomCertificateMaterial } from "../../types/certificate";

/** Props accepted by {@link CustomCertificateForm}. */
const props = defineProps<{
  /** Whether a request is in flight, which disables the submit button. */
  acting: boolean;
  /** The panel's refusal of the chain, or `null` when the last failure was not about it. */
  certificatePemRefusal: string | null;
  /** The panel's refusal of the key, or `null` when the last failure was not about it. */
  privateKeyPemRefusal: string | null;
}>();

const emit = defineEmits<{
  /**
   * The operator submitted material that passed the form's own checks.
   * @param e Event name.
   * @param material The chain and key as pasted.
   */
  (e: "submit", material: CustomCertificateMaterial): void;
  /**
   * The operator abandoned the upload.
   * @param e Event name.
   */
  (e: "cancel"): void;
}>();

const { t } = useI18n();

/** The PEM-encoded chain the operator pasted. */
const certificatePem: Ref<string> = ref("");

/** The PEM-encoded private key the operator pasted. */
const privateKeyPem: Ref<string> = ref("");

/** Whether the form has been submitted, so nothing turns red before it is tried. */
const submitted: Ref<boolean> = ref(false);

/** Message for the chain field: the form's own "required", else the panel's refusal, else `null`. */
const certificatePemError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null;
  }
  if (certificatePem.value.trim().length === 0) {
    return t("certificates.errors.certificatePemRequired");
  }
  return props.certificatePemRefusal;
});

/** Message for the key field: the form's own "required", else the panel's refusal, else `null`. */
const privateKeyPemError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null;
  }
  if (privateKeyPem.value.trim().length === 0) {
    return t("certificates.errors.privateKeyPemRequired");
  }
  return props.privateKeyPemRefusal;
});

/**
 * Hands the material to the parent once both fields are filled.
 * @returns Nothing.
 */
const submit = (): void => {
  submitted.value = true;
  if (certificatePemError.value !== null || privateKeyPemError.value !== null) {
    return;
  }
  emit("submit", { certificatePem: certificatePem.value, privateKeyPem: privateKeyPem.value });
};

/**
 * Abandons the upload.
 * @returns Nothing.
 */
const cancel = (): void => {
  emit("cancel");
};
</script>

<template>
  <UiCard>
    <UiForm @submit="submit">
      <div class="flex flex-col gap-4">
        <UiSectionHeading :title="t('certificates.installCustom')" />
        <UiTextarea
          v-model="certificatePem"
          :label="t('certificates.fields.certificatePem')"
          :error="certificatePemError"
          :rows="6"
          required
        />
        <div>
          <UiTextarea
            v-model="privateKeyPem"
            :label="t('certificates.fields.privateKeyPem')"
            :error="privateKeyPemError"
            :rows="6"
            required
          />
          <UiFieldHint class="mt-1">{{ t("certificates.hints.privateKeyPem") }}</UiFieldHint>
        </div>
        <div class="flex flex-wrap justify-end gap-2">
          <UiButton variant="secondary" type="button" @click="cancel">
            {{ t("common.cancel") }}
          </UiButton>
          <UiButton type="submit" :disabled="props.acting">{{ t("certificates.submitCustom") }}</UiButton>
        </div>
      </div>
    </UiForm>
  </UiCard>
</template>
