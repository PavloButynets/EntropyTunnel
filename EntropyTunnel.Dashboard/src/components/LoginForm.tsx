import { FormEvent, useState } from "react";
import styles from "./LoginForm.module.css";

interface Props {
  clientId: string;
  initialPassword?: string;
  onLogin: (password: string) => Promise<void>;
}

export function LoginForm({ clientId, initialPassword = "", onLogin }: Props) {
  const [password, setPassword] = useState(initialPassword);
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
        <p className={styles.sub}>
          Enter password for <code className={styles.clientId}>{clientId}</code>
        </p>

        <input
          className={styles.input}
          type="password"
          placeholder="Password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoFocus
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
