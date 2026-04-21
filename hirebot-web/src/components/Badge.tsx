type Variant = 'default' | 'green' | 'yellow' | 'red' | 'blue' | 'purple' | 'gray'

const variantClasses: Record<Variant, string> = {
  default: 'bg-slate-100 text-slate-600',
  green: 'bg-emerald-50 text-emerald-700',
  yellow: 'bg-amber-50 text-amber-700',
  red: 'bg-red-50 text-red-700',
  blue: 'bg-blue-50 text-blue-700',
  purple: 'bg-indigo-50 text-indigo-700',
  gray: 'bg-slate-100 text-slate-500',
}

export default function Badge({ children, variant = 'default' }: { children: React.ReactNode; variant?: Variant }) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium ${variantClasses[variant]}`}>
      {children}
    </span>
  )
}
