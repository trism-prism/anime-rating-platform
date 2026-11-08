import { Link } from 'react-router-dom'
import { DELETED_USER } from '../types'
import Avatar from './Avatar'

// picture and name as one link to the profile (one tab stop; the picture is already hidden from screen readers)
export default function UserLink({ name }: { name: string }) {
  if (name === DELETED_USER) return <span className="who"><Avatar userName={name} small /><strong>{name}</strong></span>
  return <Link to={`/u/${encodeURIComponent(name)}`} className="who user-link"><Avatar userName={name} small /><strong>{name}</strong></Link>
}
