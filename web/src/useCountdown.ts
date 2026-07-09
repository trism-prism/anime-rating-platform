import { useEffect, useState } from 'react'

// seconds left on a "send again" button; setLeft(n) starts a countdown
export function useCountdown() {
  const [left, setLeft] = useState(0)
  useEffect(() => {
    if (left <= 0) return
    const t = setTimeout(() => setLeft(left - 1), 1000)
    return () => clearTimeout(t)
  }, [left])
  return [left, setLeft] as const
}
