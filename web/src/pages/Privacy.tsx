export default function Privacy() {
  return (
    <div className="synopsis" style={{ maxWidth: 680, margin: '0 auto', whiteSpace: 'normal' }}>
      <h1>Privacy</h1>
      <p className="muted">Last updated: 2026-11-04</p>
      <p>
        This is a hobby and portfolio project. It is not a commercial service, and it keeps
        only what it needs to work.
      </p>

      <h2>What we store</h2>
      <ul>
        <li>Your email address and display name.</li>
        <li>Your password, hashed. It is never stored in plain text.</li>
        <li>Your ratings, reviews and watch list.</li>
      </ul>

      <h2>How it is used</h2>
      <p>
        Your email is used only to verify your account, to send sign-in codes if you turn on
        two-step verification, and to send the verification message.
        These emails are sent through Google's Gmail API or Resend.
      </p>
      <p>
        We do not sell or share your data. There are no ads and no tracking. Your sign-in is
        kept in an HttpOnly cookie, the only cookie we use. The names of the accounts signed in
        on this device are kept in your browser's local storage.
      </p>

      <h2>Anime data</h2>
      <p>Anime information comes from AniList.</p>

      <h2>Deleting your data</h2>
      <p>To delete your account and data, contact the owner. Contact: see the repository owner.</p>
    </div>
  )
}
