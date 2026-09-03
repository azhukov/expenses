import { formatAmount } from '../format/format'

import styles from './MonthHeader.module.css'

interface Props {
  total: number
}

/**
 * One figure, and deliberately only one. No breakdown, no comparison, no chart: nobody opened this
 * app to read them, and each would need the aggregation question answered before it could show
 * anything real (D1).
 */
export function MonthHeader({ total }: Props) {
  return (
    <header className={styles.header} data-testid="month-header">
      <span className={styles.caption}>This month</span>
      <span className={styles.total}>{formatAmount(total)}</span>
    </header>
  )
}
