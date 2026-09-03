import { describeWhen, formatAmount } from '../format/format'
import { merchantLabel } from '../labels/labels'
import type { MerchantView, PurchaseView } from '../api/types'

import styles from './PurchaseRow.module.css'

interface Props {
  purchase: PurchaseView
  merchants: MerchantView[]
}

function lineCount(count: number): string {
  return count === 1 ? '1 line' : `${count} lines`
}

/**
 * One entry, for recognising a purchase rather than opening it. It is information, not a control:
 * no handler, no tab stop, and nothing that looks pressable, because there is no detail screen to
 * press through to and an affordance that leads nowhere is worse than none.
 */
export function PurchaseRow({ purchase, merchants }: Props) {
  return (
    <li className={styles.row}>
      <div className={styles.identity}>
        <span className={styles.merchant}>{merchantLabel(purchase, merchants)}</span>
        <span className={styles.detail}>
          <span data-testid="when">{describeWhen(purchase.occurredAt)}</span>
          <span aria-hidden="true"> · </span>
          <span>{lineCount(purchase.expenses.length)}</span>
          {purchase.hasReceipt ? (
            <>
              <span aria-hidden="true"> · </span>
              <span className={styles.receipt}>Receipt</span>
            </>
          ) : null}
        </span>
      </div>
      <span className={styles.amount}>{formatAmount(purchase.amount)}</span>
    </li>
  )
}
