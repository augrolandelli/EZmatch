import { useEffect, useRef } from 'react'
import type { ReactNode } from 'react'
import { X } from 'lucide-react'

/**
 * Diálogo modal sobre el <dialog> nativo: foco atrapado, Escape para cerrar y fondo bloqueado gratis.
 * En celular ocupa el ancho y se pega abajo (tipo hoja).
 */
export function Modal({
  open,
  onClose,
  title,
  children,
}: {
  open: boolean
  onClose: () => void
  title: string
  children: ReactNode
}) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    if (open && !dialog.open) dialog.showModal()
    if (!open && dialog.open) dialog.close()
  }, [open])

  return (
    <dialog
      ref={ref}
      onClose={onClose}
      onClick={(e) => {
        // Clic en el fondo (fuera del contenido) cierra.
        if (e.target === ref.current) onClose()
      }}
      className="m-0 mt-auto w-full max-w-none rounded-t-2xl bg-surface p-0 text-ink shadow-xl backdrop:bg-black/50 sm:m-auto sm:max-w-md sm:rounded-2xl"
    >
      {open ? (
        <div className="p-5">
          <div className="mb-4 flex items-start justify-between gap-3">
            <h2 className="text-lg font-semibold">{title}</h2>
            <button onClick={onClose} className="-m-1 rounded-lg p-1 text-muted hover:bg-surface-2 hover:text-ink" aria-label="Cerrar">
              <X className="size-5" />
            </button>
          </div>
          {children}
        </div>
      ) : null}
    </dialog>
  )
}
