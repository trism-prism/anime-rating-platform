import type { ReactNode } from 'react'

// shared frame for the policy pages
export default function Legal({ title, updated, children }: { title: string; updated: string; children: ReactNode }) {
  return (
    <div className="legal">
      <h1>{title}</h1>
      <p className="muted">Last updated: {new Date(updated).toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric', timeZone: 'UTC' })}</p>
      {children}
    </div>
  )
}
