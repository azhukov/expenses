import { useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'

import { LedgerError } from '../api/client'
import { queryKeys, useUnits } from '../api/queries'
import type { UnitView } from '../api/types'
import { addUnitAlias, removeUnitAlias } from '../api/writes'

import styles from './Units.module.css'

function messageOf(error: unknown): string {
  return error instanceof LedgerError ? error.message : 'The ledger could not be reached.'
}

/**
 * The unit dictionary and the spellings a receipt may print for each unit. Units themselves are
 * fixed; their aliases are what the user changes here — including undoing one a confirmation
 * taught by mistake.
 *
 * After every change the dictionary is invalidated rather than patched from the response: aliases
 * are also learned elsewhere, while confirming, and the ledger's listing is the one account of them.
 */
export function Units() {
  const units = useUnits()

  return (
    <main className={styles.page}>
      <header className={styles.header}>
        <Link to="/" className={styles.back}>
          ← Back
        </Link>
        <h1 className={styles.title}>Units</h1>
      </header>

      {units.isError ? (
        <p role="alert" className={styles.failure}>
          {messageOf(units.error)}
        </p>
      ) : null}

      {units.isPending ? <p role="status">Loading units…</p> : null}

      {(units.data ?? []).map(unit => (
        <UnitAliases key={unit.code} unit={unit} />
      ))}
    </main>
  )
}

interface Props {
  unit: UnitView
}

/**
 * One unit's aliases, with its own input and its own failure: a rejection is reported beside the
 * unit it concerns, and what the user typed is kept until the ledger accepts it.
 */
function UnitAliases({ unit }: Props) {
  const client = useQueryClient()
  const [draft, setDraft] = useState('')
  const [failure, setFailure] = useState<string | null>(null)
  const [isSaving, setIsSaving] = useState(false)

  const headingId = `unit-${unit.code}`

  async function change(action: () => Promise<unknown>): Promise<boolean> {
    setFailure(null)
    setIsSaving(true)

    try {
      await action()
    } catch (error) {
      setFailure(messageOf(error))

      return false
    } finally {
      setIsSaving(false)
    }

    await client.invalidateQueries({ queryKey: queryKeys.units })

    return true
  }

  async function add(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (draft.trim() === '') {
      return
    }

    if (await change(() => addUnitAlias(unit.code, draft))) {
      setDraft('')
    }
  }

  return (
    <section className={styles.unit} aria-labelledby={headingId}>
      <h2 id={headingId} className={styles.name}>
        {unit.name} <span className={styles.symbol}>{unit.symbol}</span>
      </h2>

      {unit.aliases.length === 0 ? (
        <p className={styles.none}>No aliases</p>
      ) : (
        <ul className={styles.aliases}>
          {unit.aliases.map(alias => (
            <li key={alias} className={styles.alias}>
              <span>{alias}</span>
              <button
                type="button"
                className={styles.remove}
                aria-label={`Remove ${alias} from ${unit.name}`}
                disabled={isSaving}
                onClick={() => void change(() => removeUnitAlias(unit.code, alias))}
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}

      <form className={styles.add} onSubmit={event => void add(event)}>
        <input
          className={styles.input}
          aria-label={`New alias for ${unit.name}`}
          value={draft}
          onChange={event => setDraft(event.target.value)}
          autoCapitalize="none"
          autoCorrect="off"
        />
        <button type="submit" className={styles.submit} disabled={isSaving}>
          Add
        </button>
      </form>

      {failure === null ? null : (
        <p role="alert" className={styles.failure}>
          {failure}
        </p>
      )}
    </section>
  )
}
