import { readFileSync } from 'node:fs'

import js from '@eslint/js'
import { globalIgnores } from 'eslint/config'
import globals from 'globals'
import reactHooks from 'eslint-plugin-react-hooks'
import reactRefresh from 'eslint-plugin-react-refresh'
import tseslint from 'typescript-eslint'
import prettier from 'eslint-config-prettier/flat'

// The FE style gate, the counterpart to BE/.editorconfig. Two tools, split by what each is good at:
// Prettier owns layout (.prettierrc.json) and ESLint owns everything a formatter cannot decide.
// `prettier` goes last so it can switch off the stylistic rules the formatter already settles —
// no rule should be able to disagree with `npm run format`.
//
// The third gate is structure. structure.config.json is the single source of truth for the app's
// shape; scripts/check-structure.mjs reads it for naming and colocation, and the block below turns
// the same file into the import graph — a directory may import from itself or from a strictly lower
// rank, never sideways, never upward. `api` cannot reach back into `routes`, and two features at the
// same rank stay independent of each other.
const structure = JSON.parse(
  readFileSync(new URL('./structure.config.json', import.meta.url), 'utf8'),
)

const TEST_FILES = ['**/*.test.{ts,tsx}']

const layerBlock = (dir, rank, bannedAssetDirs) => ({
  rules: {
    'no-restricted-imports': [
      'error',
      {
        patterns: [
          {
            group: [
              ...Object.entries(structure.layers)
                .filter(([other, otherRank]) => other !== dir && otherRank >= rank)
                .map(([other]) => `../${other}/*`),
              ...bannedAssetDirs.map(asset => `../${asset}/*`),
            ],
            message: `src/${dir} is layer ${rank}: it may import only from itself or a lower layer (structure.config.json).`,
          },
          // Repeated from the base block: a later config replaces `no-restricted-imports`
          // wholesale rather than merging into it, so the depth guard has to travel with it.
          {
            group: ['../../*', '../../**'],
            message:
              'src/ is one level deep: import from your own directory or from `../<feature>/…`.',
          },
        ],
      },
    ],
  },
})

// Two blocks per directory, because `src/test/` is off limits to the app but not to its tests.
// FE007 colocates every test inside the feature directory it covers, so a test file sits at a
// layer rank it did not ask for; banning `../test/*` there would leave a shared harness helper
// with no legal home. Implementation files keep the full ban — stylesheets and test setup belong
// to the entry point, not to a feature.
const layerRules = Object.entries(structure.layers).flatMap(([dir, rank]) => [
  {
    files: [`src/${dir}/**/*.{ts,tsx}`],
    ignores: TEST_FILES,
    ...layerBlock(dir, rank, structure.assetDirs),
  },
  {
    files: TEST_FILES.map(pattern => `src/${dir}/${pattern}`),
    ...layerBlock(
      dir,
      rank,
      structure.assetDirs.filter(asset => asset !== 'test'),
    ),
  },
])

export default tseslint.config([
  globalIgnores(['dist', 'coverage']),
  {
    files: ['**/*.{ts,tsx}'],
    extends: [
      js.configs.recommended,
      // Type-aware, not just syntactic: floating promises and unsafe `any` flow are the mistakes
      // worth catching in a client that talks to an API, and neither is visible without types.
      tseslint.configs.recommendedTypeChecked,
      reactHooks.configs.flat['recommended-latest'],
      reactRefresh.configs.vite,
    ],
    languageOptions: {
      ecmaVersion: 2023,
      globals: globals.browser,
      parserOptions: {
        projectService: true,
        tsconfigRootDir: import.meta.dirname,
      },
    },
    rules: {
      // `import type` is already required by verbatimModuleSyntax; this makes the fix automatic.
      '@typescript-eslint/consistent-type-imports': [
        'error',
        {
          fixStyle: 'inline-type-imports', // `typeof import('x')` is how vitest's importActual is typed; it is not a stray import.
          disallowTypeAnnotations: false,
        },
      ],
      // An unused name is a build error under noUnusedLocals, so ESLint only needs to define the
      // escape hatch the compiler shares: a leading underscore means "deliberately unused".
      '@typescript-eslint/no-unused-vars': [
        'error',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
        },
      ],
      // A dropped promise in a handler is a silently lost write. `void` marks the intent.
      '@typescript-eslint/no-floating-promises': 'error',
      // Structure rules that hold everywhere, whatever layer the file is in.
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['../../*', '../../**'],
              message:
                'src/ is one level deep: import from your own directory or from `../<feature>/…`.',
            },
          ],
        },
      ],
      // Named exports only. A default export is renamed silently at every import site, which is
      // what makes a component hard to grep for and a re-export easy to get subtly wrong.
      'no-restricted-syntax': [
        'error',
        {
          selector: 'ExportDefaultDeclaration',
          message: 'Export by name — this app has no default exports.',
        },
      ],
      'no-console': ['warn', { allow: ['warn', 'error'] }],
      eqeqeq: ['error', 'always', { null: 'ignore' }],
      'prefer-const': 'error',
      'no-var': 'error',
      'object-shorthand': 'error',
    },
  },
  {
    // Tests reach into mocks and fixtures where the typed-ness of a value is the thing under test.
    files: ['**/*.test.{ts,tsx}', 'src/test/**'],
    rules: {
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/no-unsafe-argument': 'off',
      '@typescript-eslint/no-unsafe-call': 'off',
      '@typescript-eslint/no-explicit-any': 'off',
      // `json: async () => body` is the shape a `Response` mock has to have, await or no await.
      '@typescript-eslint/require-await': 'off',
    },
  },
  // The layer map, applied per directory. It comes after the base block deliberately: these
  // entries replace `no-restricted-imports` for the files they match.
  ...layerRules,
  {
    // Config files and build scripts are Node, not browser, and live outside tsconfig.app.json's
    // `include`, so nothing type-aware can run on them.
    files: ['*.config.{ts,js}', 'scripts/**/*.{js,mjs}'],
    extends: [js.configs.recommended, tseslint.configs.disableTypeChecked],
    languageOptions: { globals: globals.node },
    // A default export is how Vite and Vitest read a config file, and a build script prints.
    rules: { 'no-console': 'off', 'no-restricted-syntax': 'off' },
  },
  prettier,
])
