import { createApp, defineComponent, h, ref, type VNode } from 'vue'
import UiButton from '@/components/ui/UiButton.vue'
import UiCheckbox from '@/components/ui/UiCheckbox.vue'
import UiInput from '@/components/ui/UiInput.vue'
import UiRadio from '@/components/ui/UiRadio.vue'
import UiSelect from '@/components/ui/UiSelect.vue'
import UiSwitch from '@/components/ui/UiSwitch.vue'
import { createAppI18n } from '@/i18n'
import '@/assets/css/main.css'

const params = new URLSearchParams(window.location.search)
document.documentElement.dataset.theme = params.get('theme') === 'light' ? 'light' : 'dark'

// Fixture labels, not product copy: the harness exists to be measured, never shown.
const OPTIONS = [
  { value: 'a', label: 'Option A' },
  { value: 'b', label: 'Option B' },
]

const Harness = defineComponent({
  setup: (): (() => VNode) => {
    const text = ref('')
    const select = ref('a')
    const checked = ref(false)
    const on = ref(false)

    // Every control sits on a panel, the surface the kit is designed against.
    return (): VNode =>
      {return h(
        'section',
        {
          'aria-label': 'Kit controls',
          style: 'display: flex; flex-direction: column; align-items: flex-start; gap: 16px; padding: 24px; background: var(--s1)',
        },
        [
          h(UiInput, { modelValue: text.value, label: 'Name', 'onUpdate:modelValue': (v: string) => {text.value = v} }),
          h(UiSelect, { modelValue: select.value, label: 'Kind', options: OPTIONS, 'onUpdate:modelValue': (v: string) => {select.value = v} }),
          h(UiCheckbox, { modelValue: checked.value, label: 'Agree', 'onUpdate:modelValue': (v: boolean) => {checked.value = v} }),
          h(UiRadio, { value: 'x', label: 'Choice', name: 'g', checked: false }),
          h(UiSwitch, { modelValue: on.value, label: 'Enabled', 'onUpdate:modelValue': (v: boolean) => {on.value = v} }),
          h(UiButton, { variant: 'primary' }, () => {return 'Primary'}),
          h(UiButton, { variant: 'secondary' }, () => {return 'Secondary'}),
          h(UiButton, { variant: 'field' }, () => {return 'Field'}),
          h(UiButton, { variant: 'destructive' }, () => {return 'Destructive'}),
          h(UiButton, { variant: 'ghost' }, () => {return 'Ghost'}),
        ],
      )}
  },
})

createApp(Harness).use(createAppI18n()).mount('#app')
