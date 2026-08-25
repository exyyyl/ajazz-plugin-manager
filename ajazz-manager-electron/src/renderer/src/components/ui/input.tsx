import * as React from 'react'
import { cn } from '@/lib/utils'

export function Input({ className, type, ...props }: React.ComponentProps<'input'>): React.JSX.Element {
  return (
    <input
      type={type}
      className={cn(
        'h-10 w-full rounded-lg border border-white/10 bg-black/20 px-3 text-sm font-medium text-zinc-100 outline-none placeholder:font-normal placeholder:text-zinc-500 focus:border-violet-400/50 focus:ring-2 focus:ring-violet-500/10',
        className,
      )}
      {...props}
    />
  )
}
