import { MovementTypeConfig, PaymentMethodConfig } from '../models/api.models';

export interface IconOption {
  icon: string;
  label: string;
}

export interface ColorOption {
  hex: string;
  label: string;
}

export interface VisualStyle {
  background: string;
  foreground: string;
}

export const MOVEMENT_ICON_OPTIONS: ReadonlyArray<IconOption> = [
  { icon: '🛒', label: 'Compras' },
  { icon: '🏠', label: 'Hogar' },
  { icon: '🎉', label: 'Salidas' },
  { icon: '⚡', label: 'Imprevistos' },
  { icon: '📱', label: 'Suscripciones' },
  { icon: '💳', label: 'Deudas' },
  { icon: '🏦', label: 'Banco' },
  { icon: '🧘', label: 'Bienestar' },
  { icon: '✈️', label: 'Viajes' },
  { icon: '🔨', label: 'Obra' },
  { icon: '🍕', label: 'Comida' },
  { icon: '🚗', label: 'Transporte' },
  { icon: '🏥', label: 'Salud' },
  { icon: '🎓', label: 'Educacion' },
  { icon: '🎬', label: 'Entretenimiento' },
  { icon: '🐾', label: 'Mascotas' },
  { icon: '👕', label: 'Ropa' },
  { icon: '🛠️', label: 'Mantenimiento' },
  { icon: '🎁', label: 'Regalos' },
  { icon: '📚', label: 'Libros' }
];

export const MOVEMENT_COLOR_OPTIONS: ReadonlyArray<ColorOption> = [
  { hex: '#B6E6BD', label: 'Verde menta' },
  { hex: '#A8D8F0', label: 'Azul cielo' },
  { hex: '#F8C4B4', label: 'Naranja suave' },
  { hex: '#C5B3E6', label: 'Purpura' },
  { hex: '#F4A9A8', label: 'Rojo coral' },
  { hex: '#F9E07F', label: 'Amarillo sol' },
  { hex: '#7ECEC1', label: 'Turquesa' },
  { hex: '#F0B5D0', label: 'Rosa chicle' },
  { hex: '#A0C4A8', label: 'Verde salvia' },
  { hex: '#B0D4F1', label: 'Azul pastel' },
  { hex: '#E6C88C', label: 'Dorado suave' },
  { hex: '#D4A5C9', label: 'Magenta claro' },
  { hex: '#8CC5B2', label: 'Jade' },
  { hex: '#F5D08A', label: 'Ambar' },
  { hex: '#9DB8D4', label: 'Azul acero' },
  { hex: '#E8A89C', label: 'Terracota' },
  { hex: '#B3D9A3', label: 'Lima suave' },
  { hex: '#C9A0D4', label: 'Lila' },
  { hex: '#A8D4C8', label: 'Menta fresca' },
  { hex: '#F2C6A0', label: 'Melocoton' }
];

export const PAYMENT_ICON_OPTIONS: ReadonlyArray<IconOption> = [
  { icon: '💵', label: 'Efectivo' },
  { icon: '💳', label: 'Tarjeta' },
  { icon: '🖤', label: 'Black card' },
  { icon: '🧡', label: 'Tarjeta naranja' },
  { icon: '💜', label: 'Tarjeta morada' },
  { icon: '💚', label: 'Tarjeta verde' },
  { icon: '🏦', label: 'Banco' },
  { icon: '📲', label: 'Billetera digital' },
  { icon: '🔁', label: 'Transferencia' },
  { icon: '🪙', label: 'Monedas' },
  { icon: '🏧', label: 'Cajero' },
  { icon: '📟', label: 'Dataphone' },
  { icon: '🧾', label: 'Codigo QR' },
  { icon: '💻', label: 'Pago web' },
  { icon: '⌚', label: 'NFC reloj' },
  { icon: '📱', label: 'NFC movil' },
  { icon: '🎟️', label: 'Voucher' },
  { icon: '🛍️', label: 'Credito tienda' },
  { icon: '🏢', label: 'Nomina' },
  { icon: '🤝', label: 'Prestado' }
];

const INVALID_ICON_VALUES = new Set(['', '?', '??', '�']);

const ICON_ALIASES: Readonly<Record<string, string>> = {
  '✈': '✈️',
  '🛠': '🛠️',
  '🎟': '🎟️',
  '🛍': '🛍️'
};

const MOVEMENT_ICON_SET = new Set(MOVEMENT_ICON_OPTIONS.map((item) => item.icon));
const PAYMENT_ICON_SET = new Set(PAYMENT_ICON_OPTIONS.map((item) => item.icon));

function sanitizeIcon(icon: string | null | undefined, allowed: ReadonlySet<string>): string | null {
  if (!icon) {
    return null;
  }

  const trimmed = icon.trim();
  if (!trimmed || INVALID_ICON_VALUES.has(trimmed)) {
    return null;
  }

  const canonical = ICON_ALIASES[trimmed] ?? trimmed;
  return allowed.has(canonical) ? canonical : null;
}

export function sanitizeMovementIcon(icon: string | null | undefined): string | null {
  return sanitizeIcon(icon, MOVEMENT_ICON_SET);
}

export function sanitizePaymentIcon(icon: string | null | undefined): string | null {
  return sanitizeIcon(icon, PAYMENT_ICON_SET);
}

export function normalizeCatalogKey(value: string): string {
  return value
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .trim()
    .toLowerCase();
}

export function stableCatalogIndex(value: string, length: number): number {
  if (!length) {
    return 0;
  }

  let hash = 2166136261;
  for (const char of normalizeCatalogKey(value)) {
    hash ^= char.charCodeAt(0);
    hash = Math.imul(hash, 16777619);
  }

  return Math.abs(hash) % length;
}

export function resolveMovementIcon(movementType: string, configs: MovementTypeConfig[] = []): string {
  const config = configs.find((item) => normalizeCatalogKey(item.name) === normalizeCatalogKey(movementType));
  const configuredIcon = sanitizeMovementIcon(config?.icon);
  if (configuredIcon) {
    return configuredIcon;
  }

  const normalized = normalizeCatalogKey(movementType);
  if (normalized.includes('basico')) return '🛒';
  if (normalized.includes('hogar')) return '🏠';
  if (normalized.includes('salida')) return '🎉';
  if (normalized.includes('imprevisto')) return '⚡';
  if (normalized.includes('suscrip')) return '📱';
  if (normalized.includes('deuda')) return '💳';
  if (normalized.includes('prestamo') || normalized.includes('banco')) return '🏦';
  if (normalized.includes('bienestar')) return '🧘';
  if (normalized.includes('viaje')) return '✈️';
  if (normalized.includes('obra')) return '🔨';
  if (normalized.includes('comida')) return '🍕';

  return MOVEMENT_ICON_OPTIONS[stableCatalogIndex(movementType, MOVEMENT_ICON_OPTIONS.length)].icon;
}

export function resolveMovementColor(movementType: string, configs: MovementTypeConfig[] = []): string {
  const config = configs.find((item) => normalizeCatalogKey(item.name) === normalizeCatalogKey(movementType));
  if (config?.color) {
    return config.color;
  }

  return MOVEMENT_COLOR_OPTIONS[stableCatalogIndex(movementType, MOVEMENT_COLOR_OPTIONS.length)].hex;
}

export function resolvePaymentIcon(paymentMethod: string, configs: PaymentMethodConfig[] = []): string {
  const config = configs.find((item) => normalizeCatalogKey(item.name) === normalizeCatalogKey(paymentMethod));
  const configuredIcon = sanitizePaymentIcon(config?.icon);
  if (configuredIcon) {
    return configuredIcon;
  }

  const normalized = normalizeCatalogKey(paymentMethod);
  if (normalized === 'efectivo') return '💵';
  if (normalized === 'transferencia') return '🔁';
  if (normalized === 'nequi') return '📲';
  if (normalized === 'tc black') return '🖤';
  if (normalized === 'tc rappi') return '🧡';
  if (normalized === 'tc nu') return '💜';
  if (normalized === 'td bancolombia' || normalized === 'bancolombia') return '🏦';

  return PAYMENT_ICON_OPTIONS[stableCatalogIndex(paymentMethod, PAYMENT_ICON_OPTIONS.length)].icon;
}

export function resolvePaymentStyle(paymentMethod: string): VisualStyle {
  const normalized = normalizeCatalogKey(paymentMethod);

  if (normalized.includes('black')) return { background: '#1A2E23', foreground: '#FFFFFF' };
  if (normalized.includes('rappi')) return { background: '#FF6B35', foreground: '#FFFFFF' };
  if (normalized.includes('bancolombia')) return { background: '#FFD700', foreground: '#1A2E23' };
  if (normalized.includes('nu')) return { background: '#7B2D8E', foreground: '#FFFFFF' };
  if (normalized.includes('efectivo')) return { background: '#B6E6BD', foreground: '#1A2E23' };
  if (normalized.includes('transfer')) return { background: '#A8D8F0', foreground: '#1A2E23' };
  if (normalized.includes('nequi')) return { background: '#00C389', foreground: '#FFFFFF' };

  return { background: '#A8D8F0', foreground: '#1A2E23' };
}
