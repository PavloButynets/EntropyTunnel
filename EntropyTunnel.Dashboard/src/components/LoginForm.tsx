import { FormEvent, useState } from "react";
import { useT } from "../i18n";
import styles from "./LoginForm.module.css";

interface Props {
  onLogin: (password: string) => Promise<void>;
}

export function LoginForm({ onLogin }: Props) {
  const { t } = useT();
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
      setError(t.loginError);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className={styles.overlay}>
      <form className={styles.card} onSubmit={handleSubmit}>
        <div className={styles.icon}>⚡</div>
        <h1 className={styles.title}>EntropyTunnel</h1>
        <p className={styles.sub}>{t.loginSub}</p>

        <input
          className={styles.input}
          type="password"
          placeholder={t.loginPlaceholder}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          autoFocus
          autoComplete="off"
          required
        />

        {error && <p className={styles.error}>{error}</p>}

        <button className={styles.btn} type="submit" disabled={loading}>
          {loading ? t.loginSigningIn : t.loginSignIn}
        </button>
      </form>
    </div>
  );
}
