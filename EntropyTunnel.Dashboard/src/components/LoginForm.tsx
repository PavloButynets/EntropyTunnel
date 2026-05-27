import { FormEvent, useState } from "react";
import styles from "./LoginForm.module.css";

interface Props {
  onLogin: (password: string) => Promise<void>;
}

export function LoginForm({ onLogin }: Props) {
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      await onLogin(password);
    } catch {
      setError("Invalid password. Check the agent banner.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className={styles.overlay}>
      <form className={styles.card} onSubmit={handleSubmit}>
        <div className={styles.icon}>⚡</div>
        <h1 className={styles.title}>EntropyTunnel</h1>
        <p className={styles.sub}>Enter your account password to continue</p>

        <input
          className={styles.input}
          type="password"
          placeholder="Account password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoFocus
          autoComplete="off"
          required
        />

        {error && <p className={styles.error}>{error}</p>}

        <button className={styles.btn} type="submit" disabled={loading}>
          {loading ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </div>
  );
}
