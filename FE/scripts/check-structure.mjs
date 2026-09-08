#!/usr/bin/env node
//
// The structure gate: the rules about where a file lives and what it is called, which neither
// Prettier (layout), ESLint (the import graph) nor tsc (types) can see. Everything it enforces is
// read from structure.config.json — this file holds the checks, never the shape.
//
// Usage:
//   npm run structure                 report violations, exit 1 if any
//   node scripts/check-structure.mjs --counts-only
//
// Rules:
//   FE001  a file type that does not belong in src/
//   FE002  a nested feature directory
//   FE003  a component file that is not PascalCase
//   FE004  a module file that is not camelCase
//   FE005  a CSS module with no component beside it
//   FE006  a global stylesheet outside src/styles/
//   FE007  a test with no implementation beside it
//   FE008  a feature directory with no rank in structure.config.json

import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const feRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const srcRoot = join(feRoot, 'src')
const config = JSON.parse(readFileSync(join(feRoot, 'structure.config.json'), 'utf8'))

const CODE_EXTENSIONS = ['.ts', '.tsx']
const ALLOWED_EXTENSIONS = [...CODE_EXTENSIONS, '.css']

const RULES = {
  FE001: 'Unsupported file type in src/ (expected .ts, .tsx or .css)',
  FE002: 'Nested feature directory — src/ is one level of directories deep',
  FE003: 'Component file must be PascalCase',
  FE004: 'Module file must be camelCase',
  FE005: 'CSS module has no component beside it',
  FE006: 'Global stylesheet must live in src/styles/',
  FE007: 'Test has no implementation beside it',
  FE008: 'Feature directory has no rank in structure.config.json',
}

const violations = []
const report = (id, path, detail) => violations.push({ id, path, detail })

/** Every file under src/, as paths relative to FE/ with forward slashes. */
function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(entry => {
    const full = join(dir, entry.name)
    return entry.isDirectory()
      ? walk(full)
      : [
          full
            .slice(feRoot.length + 1)
            .split(sep)
            .join('/'),
        ]
  })
}

const files = walk(srcRoot).sort()
const exists = new Set(files)

/** `Home.layout.test.tsx` -> { stem: 'Home', qualifiers: ['layout'], ext: '.tsx' } */
function parse(path) {
  const name = path.slice(path.lastIndexOf('/') + 1)
  const dot = name.indexOf('.')
  const ext = name.slice(name.lastIndexOf('.'))
  const segments = name
    .slice(dot + 1, name.length - ext.length)
    .split('.')
    .filter(Boolean)
  return {
    dir: path.slice(0, path.lastIndexOf('/')),
    name,
    stem: name.slice(0, dot),
    segments,
    ext,
  }
}

// FE008 — a feature directory the layer map does not know about is a directory ESLint imposes no
// import rules on. Declaring it is what switches its enforcement on, so an omission is a violation.
const declared = new Set([...Object.keys(config.layers), ...config.assetDirs])
for (const entry of readdirSync(srcRoot, { withFileTypes: true })) {
  if (entry.isDirectory() && !declared.has(entry.name)) {
    report('FE008', `src/${entry.name}`, `add "${entry.name}" to layers, or to assetDirs`)
  }
}

for (const path of files) {
  const { dir, name, stem, segments, ext } = parse(path)
  const isTest = segments.includes('test')
  const isCssModule = name.endsWith('.module.css')

  if (!ALLOWED_EXTENSIONS.includes(ext)) {
    report('FE001', path, ext)
    continue
  }

  // src/, or src/<feature>/ — anything deeper hides a module from the layer map.
  if (dir !== 'src' && dirname(dir) !== 'src') {
    report('FE002', path, `${dir}/ is ${dir.split('/').length - 1} levels below src/`)
  }

  if (ext === '.css') {
    if (isCssModule) {
      // A stylesheet is scoped to the component it is named for; without that component it is
      // dead weight, and a rename that left it behind is the usual way it happens.
      const owner = CODE_EXTENSIONS.map(e => `${dir}/${stem}${e}`).find(c => exists.has(c))
      if (!owner) report('FE005', path, `expected ${dir}/${stem}.tsx`)
    } else if (dir !== 'src/styles') {
      report('FE006', path, 'only CSS modules live beside the code they style')
    }
    continue
  }

  if (ext === '.tsx' && !config.lowerCaseTsx.includes(name)) {
    if (!/^[A-Z][A-Za-z0-9]*$/.test(stem)) report('FE003', path, stem)
  } else if (ext === '.ts' || config.lowerCaseTsx.includes(name)) {
    if (!/^[a-z][A-Za-z0-9]*$/.test(stem)) report('FE004', path, stem)
  }

  // Colocation: `X.test.tsx` and `X.layout.test.tsx` both answer for `X.tsx` (or `X.layout.tsx`).
  if (isTest) {
    const qualified = segments.slice(0, segments.indexOf('test'))
    const bases = [stem, ...qualified.map((_, i) => [stem, ...qualified.slice(0, i + 1)].join('.'))]
    const covered = bases.some(base => CODE_EXTENSIONS.some(e => exists.has(`${dir}/${base}${e}`)))
    if (!covered) report('FE007', path, `expected ${dir}/${stem}.ts or ${dir}/${stem}.tsx`)
  }
}

if (violations.length === 0) {
  console.log(`Clean: ${files.length} files under src/ match structure.config.json.`)
  process.exit(0)
}

const byRule = id => violations.filter(v => v.id === id)
const ids = [...new Set(violations.map(v => v.id))].sort(
  (a, b) => byRule(b).length - byRule(a).length || a.localeCompare(b),
)

console.log('== counts by rule ==')
for (const id of ids) {
  console.log(`${String(byRule(id).length).padStart(5)}  ${id}  ${RULES[id]}`)
}

if (!process.argv.includes('--counts-only')) {
  for (const id of ids) {
    console.log(`\n-- ${id}: ${RULES[id]}`)
    for (const { path, detail } of byRule(id)) console.log(`   ${path}  (${detail})`)
  }
}

process.exit(1)
