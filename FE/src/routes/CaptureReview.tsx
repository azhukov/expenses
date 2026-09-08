import { useRef, useState } from 'react'

import { useCategories, useUnits } from '../api/queries'
import type { CaptureResult } from '../api/types'
import {
  asLocalInput,
  asText,
  type EditableLine,
  type Edits,
  emptyLine,
  fiscalCreatedAt,
  lineOf,
  lowConfidence,
  reviewReasons,
} from '../capture/review'

export interface ReviewProps {
  capture: CaptureResult
  onConfirm: (edits: Edits) => void
  failure: string | null
  isConfirming: boolean
}

/**
 * The review screen. It holds the capture result untouched beside the user's edits and never
 * re-derives one from the other: the server keeps nothing about a capture after answering, so the
 * echoed outcome has to be the response's own, whatever the user has since changed.
 */
export function CaptureReview({ capture, onConfirm, failure, isConfirming }: ReviewProps) {
  const categories = useCategories()
  const units = useUnits()

  const codeOfCategory = (id: number | null) =>
    categories.data?.find(category => category.id === id)?.code ?? ''
  const codeOfUnit = (id: number | null) => units.data?.find(unit => unit.id === id)?.code ?? ''

  const candidates = capture.result?.candidates ?? []

  // Seeded once: after the first render these are the user's values, and re-deriving them from the
  // capture would silently discard an edit.
  const [lines, setLines] = useState<EditableLine[]>(() =>
    candidates.length > 0
      ? candidates.map(each => lineOf(each, codeOfCategory, codeOfUnit))
      : [emptyLine(1)],
  )
  const [amount, setAmount] = useState(() => asText(capture.result?.total))
  const [merchant, setMerchant] = useState(() => capture.result?.merchantName ?? '')
  const [occurredAt, setOccurredAt] = useState(() => asLocalInput(fiscalCreatedAt(capture)))
  const [dateEdited, setDateEdited] = useState(false)

  const nextKey = useRef(candidates.length + 1)

  const reasons = reviewReasons(capture)

  // Per line as well as in the list of reasons: which line the unsure value is on is what the user
  // needs in order to correct it, and a reason on its own does not say.
  const unsureOn = new Map(
    candidates.map(each => [each.lineNumber, lowConfidence(each.reportedConfidence)]),
  )

  function change(key: number, field: keyof EditableLine, value: string) {
    setLines(current =>
      current.map(line => (line.key === key ? { ...line, [field]: value } : line)),
    )
  }

  return (
    <form
      onSubmit={event => {
        event.preventDefault()
        onConfirm({ lines, amount, merchant, occurredAt, dateEdited })
      }}
    >
      {capture.failureReason !== null && <p role="alert">{capture.failureReason}</p>}

      {reasons.length > 0 && (
        <ul data-testid="review-reasons">
          {reasons.map(reason => (
            <li key={reason}>{reason}</li>
          ))}
        </ul>
      )}

      {failure !== null && <p role="alert">{failure}</p>}

      <label>
        Merchant
        <input value={merchant} onChange={event => setMerchant(event.target.value)} />
      </label>

      <label>
        Date
        <input
          type="datetime-local"
          value={occurredAt}
          onChange={event => {
            setDateEdited(true)
            setOccurredAt(event.target.value)
          }}
        />
      </label>

      <label>
        Total amount
        <input
          type="number"
          step="0.01"
          value={amount}
          onChange={event => setAmount(event.target.value)}
        />
      </label>

      <ul>
        {lines.map(line => (
          <li key={line.key} data-testid="line">
            {(unsureOn.get(line.key)?.length ?? 0) > 0 && (
              <p data-testid="low-confidence">
                Extraction was unsure of {unsureOn.get(line.key)?.join(', ')}.
              </p>
            )}

            <label>
              Description
              <input
                value={line.description}
                onChange={event => change(line.key, 'description', event.target.value)}
              />
            </label>

            <label>
              Amount
              <input
                type="number"
                step="0.01"
                value={line.amount}
                onChange={event => change(line.key, 'amount', event.target.value)}
              />
            </label>

            <label>
              Quantity
              <input
                type="number"
                step="0.001"
                value={line.quantity}
                onChange={event => change(line.key, 'quantity', event.target.value)}
              />
            </label>

            <label>
              Category
              <select
                value={line.categoryCode}
                onChange={event => change(line.key, 'categoryCode', event.target.value)}
              >
                <option value="">Uncategorised</option>
                {(categories.data ?? []).map(category => (
                  <option key={category.id} value={category.code}>
                    {category.name}
                  </option>
                ))}
              </select>
            </label>

            <label>
              Unit
              <select
                value={line.unitCode}
                onChange={event => change(line.key, 'unitCode', event.target.value)}
              >
                <option value="">None</option>
                {(units.data ?? []).map(unit => (
                  <option key={unit.id} value={unit.code}>
                    {unit.name}
                  </option>
                ))}
              </select>
            </label>

            <button
              type="button"
              onClick={() => setLines(current => current.filter(each => each.key !== line.key))}
            >
              Remove
            </button>
          </li>
        ))}
      </ul>

      <button
        type="button"
        onClick={() => setLines(current => [...current, emptyLine(nextKey.current++)])}
      >
        Add a line
      </button>

      <button type="submit" disabled={isConfirming}>
        Confirm
      </button>
    </form>
  )
}
