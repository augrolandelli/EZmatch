import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, Ref, SelectHTMLAttributes, TextareaHTMLAttributes } from 'react'
import { LoaderCircle } from 'lucide-react'

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'dangerGhost'

const buttonStyles: Record<ButtonVariant, string> = {
  primary: 'bg-brand text-on-brand hover:bg-brand-hover',
  secondary: 'bg-surface text-ink border border-line hover:bg-surface-2',
  ghost: 'text-ink hover:bg-surface-2',
  danger: 'bg-danger text-white hover:opacity-90',
  dangerGhost: 'text-danger hover:bg-danger-soft',
}

export function Button({
  variant = 'primary',
  loading = false,
  className = '',
  children,
  disabled,
  ...props
}: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant; loading?: boolean }) {
  return (
    <button
      {...props}
      disabled={disabled || loading}
      className={`inline-flex min-h-10 items-center justify-center gap-2 rounded-lg px-4 text-sm font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-60 ${buttonStyles[variant]} ${className}`}
    >
      {loading ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : null}
      {children}
    </button>
  )
}

export function TextField({
  label,
  error,
  hint,
  id,
  ref,
  className = '',
  ...props
}: InputHTMLAttributes<HTMLInputElement> & { label: string; error?: string; hint?: string; ref?: Ref<HTMLInputElement> }) {
  const inputId = id ?? props.name
  return (
    <div className={`flex flex-col gap-1.5 ${className}`}>
      <label htmlFor={inputId} className="text-sm font-medium text-ink">
        {label}
      </label>
      <input
        {...props}
        id={inputId}
        ref={ref}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? `${inputId}-error` : undefined}
        className={`min-h-10 rounded-lg border bg-surface px-3 text-sm text-ink placeholder:text-muted focus:border-brand focus:outline-none focus:ring-2 focus:ring-brand/25 ${error ? 'border-danger' : 'border-line'}`}
      />
      {error ? (
        <p id={`${inputId}-error`} className="text-xs text-danger">
          {error}
        </p>
      ) : hint ? (
        <p className="text-xs text-muted">{hint}</p>
      ) : null}
    </div>
  )
}

export function Alert({ tone = 'danger', children }: { tone?: 'danger' | 'info' | 'success'; children: ReactNode }) {
  const styles = {
    danger: 'bg-danger-soft text-danger',
    info: 'bg-info-soft text-info',
    success: 'bg-brand-soft text-brand',
  }[tone]
  return (
    <div role={tone === 'danger' ? 'alert' : 'status'} className={`rounded-lg px-3 py-2.5 text-sm ${styles}`}>
      {children}
    </div>
  )
}

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <section className={`rounded-xl border border-line bg-surface p-5 ${className}`}>{children}</section>
}

/** Isotipo: una cancha vista desde arriba. */
export function Logo({ withText = true, className = '' }: { withText?: boolean; className?: string }) {
  return (
    <span className={`inline-flex items-center gap-2 ${className}`}>
      <svg viewBox="0 0 32 32" className="size-8 shrink-0" aria-hidden>
        <rect width="32" height="32" rx="8" className="fill-brand" />
        <rect x="7" y="6" width="18" height="20" rx="2.5" fill="none" stroke="currentColor" strokeWidth="2" className="text-on-brand" />
        <path d="M7 16h18M16 6v4M16 22v4" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="text-on-brand" />
      </svg>
      {withText ? <span className="text-lg font-bold tracking-tight text-ink">EZmatch</span> : null}
    </span>
  )
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <header className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-ink">{title}</h1>
        {subtitle ? <p className="mt-1 text-sm text-muted">{subtitle}</p> : null}
      </div>
      {actions}
    </header>
  )
}

export function TextArea({
  label,
  error,
  hint,
  id,
  ref,
  ...props
}: TextareaHTMLAttributes<HTMLTextAreaElement> & { label: string; error?: string; hint?: string; ref?: Ref<HTMLTextAreaElement> }) {
  const inputId = id ?? props.name
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-sm font-medium text-ink">
        {label}
      </label>
      <textarea
        {...props}
        id={inputId}
        ref={ref}
        aria-invalid={error ? true : undefined}
        className={`min-h-24 rounded-lg border bg-surface px-3 py-2 text-sm text-ink placeholder:text-muted focus:border-brand focus:outline-none focus:ring-2 focus:ring-brand/25 ${error ? 'border-danger' : 'border-line'}`}
      />
      {error ? <p className="text-xs text-danger">{error}</p> : hint ? <p className="text-xs text-muted">{hint}</p> : null}
    </div>
  )
}

export function SelectField({
  label,
  id,
  ref,
  children,
  ...props
}: SelectHTMLAttributes<HTMLSelectElement> & { label: string; ref?: Ref<HTMLSelectElement> }) {
  const inputId = id ?? props.name
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={inputId} className="text-sm font-medium text-ink">
        {label}
      </label>
      <select
        {...props}
        id={inputId}
        ref={ref}
        className="min-h-10 rounded-lg border border-line bg-surface px-2 text-sm text-ink focus:border-brand focus:outline-none focus:ring-2 focus:ring-brand/25"
      >
        {children}
      </select>
    </div>
  )
}

export function Checkbox({
  label,
  ref,
  ...props
}: Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { label: ReactNode; ref?: Ref<HTMLInputElement> }) {
  return (
    <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-ink">
      <input {...props} ref={ref} type="checkbox" className="size-4 rounded border-line accent-[var(--brand)]" />
      {label}
    </label>
  )
}

/** Pestañas simples (botones con aria-selected). */
export function Tabs<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { value: T; label: string }[] }) {
  return (
    <div role="tablist" className="mb-6 flex gap-1 overflow-x-auto border-b border-line">
      {items.map((item) => (
        <button
          key={item.value}
          role="tab"
          aria-selected={item.value === value}
          onClick={() => onChange(item.value)}
          className={`-mb-px whitespace-nowrap border-b-2 px-3 py-2.5 text-sm font-medium transition-colors ${
            item.value === value ? 'border-brand text-brand' : 'border-transparent text-muted hover:text-ink'
          }`}
        >
          {item.label}
        </button>
      ))}
    </div>
  )
}
