import * as React from 'react'
import { cva, type VariantProps } from 'class-variance-authority'
import { cn } from '@/lib/utils'

const badgeVariants = cva('inline-flex items-center rounded-full border px-2.5 py-1 text-xs font-semibold', {
  variants: {
    variant: {
      default: 'border-violet-400/20 bg-violet-400/10 text-violet-200',
      success: 'border-emerald-400/20 bg-emerald-400/10 text-emerald-300',
      warning: 'border-amber-400/20 bg-amber-400/10 text-amber-300',
      danger: 'border-red-400/20 bg-red-400/10 text-red-300',
      muted: 'border-white/10 bg-white/[0.04] text-zinc-400',
    },
  },
  defaultVariants: { variant: 'default' },
})

export function Badge({
  className,
  variant,
  ...props
}: React.HTMLAttributes<HTMLDivElement> & VariantProps<typeof badgeVariants>): React.JSX.Element {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />
}
