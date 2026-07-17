import type { ButtonHTMLAttributes, FormEvent, InputHTMLAttributes, ReactNode, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'
import { AlertTriangle, Check, LoaderCircle, X } from 'lucide-react'

export function Button({ variant = 'primary', icon, children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: 'primary' | 'secondary' | 'danger' | 'ghost'; icon?: ReactNode }) {
  return <button className={`button button--${variant}`} {...props}>{icon}{children}</button>
}

export function IconButton({ label, children, ...props }: ButtonHTMLAttributes<HTMLButtonElement> & { label: string }) {
  return <button className="icon-button" title={label} aria-label={label} {...props}>{children}</button>
}

export function Badge({ tone = 'neutral', children }: { tone?: 'neutral' | 'good' | 'warn' | 'bad' | 'info'; children: ReactNode }) {
  return <span className={`badge badge--${tone}`}>{children}</span>
}

export function StatusBadge({ value }: { value: number | string | boolean }) {
  const normalized = String(value)
  const good = normalized === 'Online' || normalized === '1' || normalized === 'Sent' || normalized === 'true'
  const bad = normalized === 'Offline' || normalized === '3' || normalized === 'Failed' || normalized === 'false'
  const warn = normalized === 'SuspectedOffline' || normalized === '2' || normalized === 'Recovering' || normalized === '4' || normalized === 'Pending'
  return <Badge tone={good ? 'good' : bad ? 'bad' : warn ? 'warn' : 'neutral'}>{typeof value === 'boolean' ? (value ? '启用' : '停用') : normalized}</Badge>
}

export function Panel({ title, actions, children, className = '' }: { title?: string; actions?: ReactNode; children: ReactNode; className?: string }) {
  return <section className={`panel ${className}`}>
    {(title || actions) && <header className="panel__header"><h2>{title}</h2><div className="panel__actions">{actions}</div></header>}
    {children}
  </section>
}

export function EmptyState({ title, description }: { title: string; description?: string }) {
  return <div className="empty-state"><Check size={20} /><strong>{title}</strong>{description && <span>{description}</span>}</div>
}

export function LoadingState() {
  return <div className="loading-state"><LoaderCircle size={18} className="spin" />正在加载</div>
}

export function ErrorState({ message, retry }: { message: string; retry?: () => void }) {
  return <div className="error-state"><AlertTriangle size={18} /><span>{message}</span>{retry && <Button variant="secondary" onClick={retry}>重试</Button>}</div>
}

export function Field({ label, hint, children }: { label: string; hint?: string; children: ReactNode }) {
  return <label className="field"><span className="field__label">{label}</span>{children}{hint && <small>{hint}</small>}</label>
}

export function Input(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input className="input" {...props} />
}

export function Select(props: SelectHTMLAttributes<HTMLSelectElement>) {
  return <select className="input" {...props} />
}

export function Textarea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className="input textarea" {...props} />
}

export function Dialog({ open, title, description, children, onClose }: { open: boolean; title: string; description?: string; children: ReactNode; onClose: () => void }) {
  if (!open) return null
  return <div className="dialog-backdrop" role="presentation" onMouseDown={(event) => event.currentTarget === event.target && onClose()}>
    <section className="dialog" role="dialog" aria-modal="true" aria-label={title}>
      <header className="dialog__header"><div><h2>{title}</h2>{description && <p>{description}</p>}</div><IconButton label="关闭" onClick={onClose}><X size={18} /></IconButton></header>
      {children}
    </section>
  </div>
}

export function Form({ children, onSubmit }: { children: ReactNode; onSubmit: (event: FormEvent<HTMLFormElement>) => void }) {
  return <form className="form" onSubmit={onSubmit}>{children}</form>
}

export function PageHeader({ title, description, actions }: { title: string; description: string; actions?: ReactNode }) {
  return <header className="page-header"><div><h1>{title}</h1><p>{description}</p></div><div className="page-header__actions">{actions}</div></header>
}
