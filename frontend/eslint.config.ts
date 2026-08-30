import importX from 'eslint-plugin-import-x'
import vueA11y from 'eslint-plugin-vuejs-accessibility'
import jsdoc from 'eslint-plugin-jsdoc'
import pluginVue from 'eslint-plugin-vue'
import tseslint from 'typescript-eslint'
import prettier from 'eslint-config-prettier'

/**
 * Flat ESLint configuration for the Maran SPA shell.
 *
 * This is the mechanical enforcement layer for rules/vue.md — reviewers
 * are the last line, not the first. Every law in that file that can be
 * checked by a lint rule is wired here at `error` severity:
 *
 * - Arrow-only style ("Every function is a const arrow function"):
 *   `func-style` + `no-restricted-syntax` banning `FunctionDeclaration`/
 *   `FunctionExpression` (class methods, which cannot be arrows in JS/TS,
 *   are excluded from the `FunctionExpression` ban — see the selector).
 * - No raw HTML ("No raw HTML strings, ever"): `vue/no-v-html` plus a
 *   `no-restricted-syntax` selector blocking `innerHTML`/`outerHTML`
 *   assignment.
 * - UI kit only ("UI comes from components/ui"): `vue/no-restricted-html-elements`
 *   for interactive/data elements, turned off inside `src/components/ui/**`
 *   where those primitives are implemented.
 * - APIs only in stores ("Composables and the API layer"): `no-restricted-imports`
 *   blocks `.vue` files from importing the API composables, and
 *   `no-restricted-globals` blocks `fetch`/`XMLHttpRequest` everywhere
 *   except `useApi.ts`, their one legitimate caller.
 * - Docs ("mandatory doc comments"): `eslint-plugin-jsdoc` requires a JSDoc
 *   block, `@param`, `@returns` and a description on every named function
 *   the codebase actually writes (module-level `const fn = () => {}`
 *   declarations, exported or not) and on classes/methods; test files are
 *   exempt, matching the doc-comment rule's own test exemption.
 * - Types: no `any`, no non-null assertions, `import type` for type-only
 *   imports, explicit return types everywhere (including expression-bodied
 *   arrows), and the type-checked preset's floating/misused-promise checks.
 * - i18n ("ALL user-visible strings go through i18n"): `vue/no-bare-strings-in-template`.
 * - Cleanliness: no console/debugger, `===`, unused-vars, one-component-per-file
 *   naming, `<script setup>` only.
 */
export default tseslint.config(
  { name: 'app/files-to-ignore', ignores: ['**/dist/**', '**/node_modules/**'] },

  // Order matters: typescript-eslint's recommendedTypeChecked sets a global
  // (file-unscoped) `languageOptions.parser`. It must come BEFORE
  // pluginVue's flat/recommended config, whose own *.vue-scoped parser
  // setting (vue-eslint-parser) needs to win for .vue files by being
  // applied later. The final block below then layers the TS parser back in
  // as vue-eslint-parser's *nested* `parserOptions.parser`, so <script>
  // blocks are still type-checked as TypeScript.
  ...tseslint.configs.recommendedTypeChecked,
  pluginVue.configs['flat/recommended'],

  {
    name: 'app/type-aware-parser-options',
    languageOptions: {
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
  },
  {
    name: 'app/vue-typescript-parser',
    files: ['**/*.vue'],
    languageOptions: {
      parserOptions: {
        parser: tseslint.parser,
        extraFileExtensions: ['.vue'],
      },
    },
  },

  {
    name: 'app/rules',
    files: ['**/*.{ts,mts,vue}'],
    plugins: { jsdoc, 'import-x': importX, 'vuejs-accessibility': vueA11y },
    rules: {
      // --- Arrow-only style ---
      'func-style': ['error', 'expression'],

      // Every body is a block, including a one-line arrow. A concise body reads fine
      // until somebody adds a second statement to it, at which point the diff is the
      // rewrite of the whole function rather than one added line — and the reviewer is
      // reading a restructure where a change was meant. The backend has the same rule
      // (rules/csharp.md: no expression-bodied members), and a repository whose two
      // halves disagree about so basic a shape teaches nobody anything.
      'arrow-body-style': ['error', 'always'],
      curly: ['error', 'all'],
      'no-restricted-syntax': [
        'error',
        {
          selector:
            "MemberExpression[object.name=/^(window|globalThis|self)$/][property.name=/^(fetch|XMLHttpRequest)$/]",
          message:
            'Only src/composables/useApi.ts may perform HTTP requests, and it calls the bare global (rules/vue.md).',
        },
        {
          selector: 'FunctionDeclaration',
          message: 'Function declarations are forbidden — use a const arrow function (rules/vue.md).',
        },
        {
          // Class methods (incl. constructors) compile to FunctionExpression
          // nodes too, but JS/TS classes cannot use arrow methods, so they
          // are excluded from this ban.
          selector: 'FunctionExpression:not(MethodDefinition > FunctionExpression)',
          message: 'Function expressions are forbidden — use an arrow function (rules/vue.md).',
        },
        {
          selector: "AssignmentExpression[left.property.name=/^(inner|outer)HTML$/]",
          message: 'No raw HTML strings — do not assign innerHTML/outerHTML (rules/vue.md).',
        },
      ],

      // --- No raw HTML ---
      'vue/no-v-html': 'error',

      // --- UI kit only ---
      'vue/no-restricted-html-elements': [
        'error',
        {
          element: 'button',
          message: 'Use UiButton from src/components/ui/ instead of a raw <button> (rules/vue.md).',
        },
        {
          element: 'input',
          message: 'Use a src/components/ui/ input primitive instead of a raw <input> (rules/vue.md).',
        },
        {
          element: 'select',
          message: 'Use a src/components/ui/ select primitive instead of a raw <select> (rules/vue.md).',
        },
        {
          element: 'textarea',
          message: 'Use a src/components/ui/ textarea primitive instead of a raw <textarea> (rules/vue.md).',
        },
        { element: 'table', message: 'Use UiTable from src/components/ui/ instead of a raw <table> (rules/vue.md).' },
        { element: 'form', message: 'Use a src/components/ui/ form primitive instead of a raw <form> (rules/vue.md).' },
      ],

      // --- APIs only in stores ---
      'no-restricted-globals': [
        'error',
        { name: 'fetch', message: 'Only src/composables/useApi.ts may call fetch directly (rules/vue.md).' },
        {
          name: 'XMLHttpRequest',
          message: 'Only src/composables/useApi.ts may perform HTTP requests directly (rules/vue.md).',
        },
      ],

      // --- Docs ---
      'jsdoc/require-jsdoc': [
        'error',
        {
          publicOnly: false,
          require: {
            FunctionDeclaration: true,
            MethodDefinition: true,
            ClassDeclaration: true,
            FunctionExpression: true,
          },
          contexts: [
            // Named `const foo = () => {}` / `export const foo = () => {}`
            // declarations — the shape every function in this codebase
            // takes under the arrow-only rule above. Anonymous inline
            // callbacks (e.g. `.catch(() => ({}))`, contextually-typed
            // Pinia/lifecycle callbacks) are intentionally excluded: a
            // JSDoc block on every nested one-line callback would be noise,
            // not documentation.
            'VariableDeclaration > VariableDeclarator > ArrowFunctionExpression',
            'TSInterfaceDeclaration',
            'TSTypeAliasDeclaration',
            'TSEnumDeclaration',
          ],
        },
      ],
      'jsdoc/require-param': 'error',
      'jsdoc/require-returns': 'error',
      'jsdoc/check-param-names': 'error',
      'jsdoc/require-description': 'error',

      // --- Types ---
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/no-non-null-assertion': 'error',
      // Types are imported inline, on the same line as the values from that module:
      // `import { computed, type ComputedRef } from 'vue'`, never a second `import type`
      // line for the same module. Two lines importing one module is noise a reader has to
      // reconcile, and the pair drifts apart the moment someone edits only one of them.
      '@typescript-eslint/consistent-type-imports': ['error', {
        prefer: 'type-imports',
        fixStyle: 'inline-type-imports',
      }],
      // The rule above chooses the STYLE; this one forbids the duplicate line itself, which
      // the style rule alone does not catch when both imports already exist.
      'import-x/no-duplicates': ['error', { 'prefer-inline': true }],
      '@typescript-eslint/explicit-function-return-type': ['error', { allowExpressions: false }],
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],

      // --- i18n ---
      // Every user-visible string goes through i18n — INCLUDING the ones a sighted
      // user never reads. An alt, aria-label, title or placeholder left in English is
      // a screen-reader user being served a language they did not choose, and it is
      // invisible in review precisely because nothing on screen looks wrong.
      'vue/no-bare-strings-in-template': ['error', {
        attributes: {
          '/.+/': ['title', 'aria-label', 'aria-placeholder', 'aria-roledescription', 'aria-valuetext'],
          input: ['placeholder'],
          textarea: ['placeholder'],
          img: ['alt'],
        },
        directives: ['v-text'],
      }],

      // The other half of the same rule: the attribute must EXIST. A translated label
      // that is never written helps nobody, so these check presence, while the rule
      // above checks that what is present came from a locale.
      'vuejs-accessibility/alt-text': 'error',
      'vuejs-accessibility/anchor-has-content': 'error',
      'vuejs-accessibility/aria-props': 'error',
      'vuejs-accessibility/aria-role': 'error',
      'vuejs-accessibility/form-control-has-label': 'error',
      'vuejs-accessibility/heading-has-content': 'error',
      'vuejs-accessibility/iframe-has-title': 'error',
      'vuejs-accessibility/label-has-for': ['error', { required: { every: ['id'] } }],
      'vuejs-accessibility/mouse-events-have-key-events': 'error',
      'vuejs-accessibility/no-autofocus': 'error',
      'vuejs-accessibility/role-has-required-aria-props': 'error',

      // --- Cleanliness ---
      'no-console': 'error',
      'no-debugger': 'error',
      eqeqeq: 'error',
      'vue/multi-word-component-names': ['error', { ignores: ['App'] }],
      'vue/component-api-style': ['error', ['script-setup']],
    },
  },

  // fetch/XMLHttpRequest are legitimate only inside the one low-level
  // client composable that everything else is forbidden from bypassing.
  {
    name: 'app/use-api-may-call-fetch',
    files: ['src/composables/useApi.ts'],
    rules: {
      'no-restricted-globals': 'off',
    },
  },

  // Raw interactive elements are legitimate only inside the UI kit itself,
  // where the accessibility/styling rules are implemented once.
  {
    name: 'app/ui-kit-may-use-raw-elements',
    files: ['src/components/ui/**/*.vue'],
    rules: {
      'vue/no-restricted-html-elements': 'off',
    },
  },

  // The API layer is reachable from Pinia stores ONLY. Restricting `.vue` files
  // alone would leave a hole: any other `.ts` (a util, another composable) could
  // still import it. So the ban covers all of `src/**` and is lifted below for
  // the two places that legitimately reach the layer.
  {
    name: 'app/api-layer-is-store-only',
    files: ['src/**/*.{ts,vue}'],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/composables/useApi*', '**/composables/apis/*'],
              message:
                'Only Pinia stores may call the API layer — components and helpers go through a store action (rules/vue.md).',
            },
          ],
        },
      ],
    },
  },

  // Stores are the API layer's one legitimate consumer; the API composables
  // themselves are built on `useApi`, so both are exempt from the ban above.
  {
    name: 'app/stores-and-api-composables-may-use-the-api-layer',
    files: ['src/stores/**/*.ts', 'src/composables/apis/**/*.ts', 'src/composables/useApi.ts'],
    rules: {
      'no-restricted-imports': 'off',
    },
  },

  // Test files are exempt from the doc-comment mandate, same as every other
  // language's rules in this repo (rules/testing.md: "Test code is exempt
  // from the mandatory doc-comment rule"). Frontend tests are end-to-end
  // (Playwright) and live in e2e/, not beside the source.
  {
    name: 'app/spec-files-are-doc-exempt',
    files: ['e2e/**/*.ts', '**/*.spec.ts'],
    rules: {
      'jsdoc/require-jsdoc': 'off',
      'jsdoc/require-param': 'off',
      'jsdoc/require-returns': 'off',
      'jsdoc/check-param-names': 'off',
      'jsdoc/require-description': 'off',
    },
  },

  // Config files describe build tooling, not application code; the
  // arrow-only/no-any/explicit-return-type rules target app logic and add
  // no value against third-party plugin factory calls here.
  {
    name: 'app/config-files-are-relaxed',
    files: ['vite.config.ts', 'eslint.config.ts'],
    rules: {
      '@typescript-eslint/explicit-function-return-type': 'off',
      'jsdoc/require-jsdoc': 'off',
    },
  },

  // Must be last: turns off stylistic rules that would conflict with a
  // formatter, per the owner directive ("eslint-config-prettier last").
  prettier,
)
