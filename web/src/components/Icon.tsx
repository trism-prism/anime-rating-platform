import type { ReactNode } from 'react'

// 20px line icons for the navigation, drawn in currentColor
const PATHS = {
  home: <path d="M3 9.5 10 3.5l7 6V16a1 1 0 0 1-1 1h-3.5v-4.5h-5V17H4a1 1 0 0 1-1-1z" />,
  trending: <path d="M2.5 14.5l5-5 3 3 7-7M12.5 5.5h5v5" />,
  rankings: <path d="M4 17V9M10 17V3M16 17v-5" />,
  list: <path d="M5.5 3h9v14L10 13.5 5.5 17z" />,
  friends: (
    <>
      <circle cx="7.5" cy="7" r="2.5" />
      <path d="M2.5 16c.4-2.8 2.4-4.5 5-4.5s4.6 1.7 5 4.5M13 4.7a2.5 2.5 0 0 1 0 4.6M14.5 11.8c1.7.5 2.8 2 3 4.2" />
    </>
  ),
  login: <path d="M8 3.5H5A1.5 1.5 0 0 0 3.5 5v10A1.5 1.5 0 0 0 5 16.5h3M12 6.5l3.5 3.5-3.5 3.5M15.5 10H8" />,
} satisfies Record<string, ReactNode>

export type IconName = keyof typeof PATHS

export default function Icon({ name }: { name: IconName }) {
  return (
    <svg className="icon" width="20" height="20" viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      {PATHS[name]}
    </svg>
  )
}
