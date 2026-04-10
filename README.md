# Controleo

**Tu app de control de gastos personales.** Registra, organiza y analiza tus finanzas desde tu celular o desde la web.

Disponible en **Android**, **iOS** y **Web** (Angular — solo para usuarios Premium).

---

## Funcionalidades

### Dashboard

- Navegación por mes (anterior, siguiente, selector de mes).
- Dos vistas: **por categoría** (tipo de movimiento) o **por medio de pago**.
- Gráfico donut con la distribución de tus gastos del mes.
- Barras de progreso de presupuesto por categoría para ver cuánto llevas gastado vs. tu límite.
- Detalle por sección: toca una categoría o medio de pago para ver los gastos asociados.

### Registro rápido de gastos

- Registra un gasto en segundos: fecha, descripción, monto, tipo de movimiento y medio de pago.
- Fecha limitada al mes en curso.
- Descripción de hasta 50 caracteres.
- Monto máximo por gasto: **$10,000,000,000**.
- Formato de monto en tiempo real mientras escribes.

### Listado de gastos

- Consulta todos tus gastos del mes con paginación (5, 10 o 20 por página).
- Filtra por tipo de movimiento, medio de pago o busca por texto.
- Edita o elimina cualquier gasto registrado.

### Presupuestos mensuales

- Crea un presupuesto por cada categoría de gasto (tipo de movimiento).
- Visualiza cuánto llevas gastado vs. tu presupuesto asignado.
- Integrado con el dashboard para seguimiento visual con indicadores de progreso.

### Gastos recurrentes

- Automatiza gastos que se repiten cada mes (arriendo, suscripciones, servicios, etc.).
- Configura: descripción, monto, tipo de movimiento, medio de pago, día del mes (1-31).
- Define mes de inicio y mes de fin (opcional).
- Activa o desactiva cada recurrente en cualquier momento.
- Los gastos se generan automáticamente en el día configurado.

### Obligaciones

- Lleva el control de préstamos, tarjetas de crédito y pagos mensuales.
- Registra: descripción, pago mensual, monto total, saldo actual, tasa de interés.
- Seguimiento de cuotas: cuotas totales y cuotas restantes.
- Día de vencimiento (1-31) con recordatorio configurable (0 a 30 días antes).
- Clasifica por tipo (General u otro) y asocia a una tarjeta si aplica.

### Configuración de catálogos

- **Tipos de movimiento** (categorías): crea, edita y elimina categorías personalizadas. Asigna un emoji y un color a cada una.
- **Medios de pago**: crea, edita y elimina medios de pago personalizados. Asigna un emoji a cada uno.
- Tus catálogos se usan en gastos, presupuestos, recurrentes y obligaciones.

### Autenticación

- Registro con email y contraseña (mínimo 8 caracteres).
- Inicio de sesión con email y contraseña.
- Sesión persistente con token seguro.

---

## Plan Premium

La mayoría de funcionalidades están disponibles para todos los usuarios sin costo. El plan **Premium** desbloquea capacidades adicionales:

### Modo offline completo (Premium)

- Crea, edita y consulta gastos sin conexión a internet.
- Accede al dashboard, presupuestos, recurrentes y obligaciones sin conexión.
- Los datos se almacenan en caché en tu dispositivo.
- **Sincronización automática**: al recuperar conexión, todos los cambios se sincronizan con el servidor.
- Los usuarios gratuitos pueden ver datos en caché cuando pierden conexión, pero no pueden crear ni editar registros offline.

### App Web (Premium)

- Acceso a la **aplicación web** (Angular) con todas las funcionalidades disponibles desde el navegador.
- Incluye: registro de gastos, listado, dashboard con gráficos, presupuestos, gastos recurrentes y configuración de catálogos.
- Disponible exclusivamente para usuarios Premium.

---

## Comparativa Free vs. Premium

| Funcionalidad | Free | Premium |
|---|:---:|:---:|
| Registro, edición y eliminación de gastos | ✅ | ✅ |
| Dashboard con gráficos y distribución | ✅ | ✅ |
| Presupuestos mensuales por categoría | ✅ | ✅ |
| Gastos recurrentes (automatización mensual) | ✅ | ✅ |
| Obligaciones (préstamos, cuotas, recordatorios) | ✅ | ✅ |
| Catálogos personalizables (emojis + colores) | ✅ | ✅ |
| Sin límite de cantidad de registros | ✅ | ✅ |
| Modo offline (lectura + escritura sin conexión) | ❌ | ✅ |
| Sincronización automática al reconectarse | ❌ | ✅ |
| App Web (Angular) | ❌ | ✅ |

---

## Límites y restricciones

Estos límites aplican a **todos los usuarios** (Free y Premium):

| Concepto | Límite |
|---|---|
| Monto máximo por gasto | $10,000,000,000 |
| Longitud máxima de descripción | 50 caracteres |
| Cuotas por obligación | 1 – 120 |
| Días de recordatorio antes de vencimiento | 0 – 30 |
| Elementos por página (paginación) | 5, 10 o 20 |
| Contraseña mínima | 8 caracteres |
| Fecha de gastos | Solo mes en curso |
| Cantidad de gastos, presupuestos, recurrentes u obligaciones | **Sin límite** |

---

## Plataformas

| Plataforma | Disponibilidad |
|---|---|
| Android | Todos los usuarios |
| iOS | Todos los usuarios |
| Web (Angular) | Solo Premium |

---

## Panel de administración (Web)

Disponible únicamente para usuarios con rol de **administrador** desde la app web:

- Listado de usuarios con búsqueda y paginación.
- Otorgar o revocar acceso **Premium**.
- Otorgar o revocar rol de **administrador**.
- Habilitar o inhabilitar cuentas de usuario.
- Impersonar usuarios para soporte y diagnóstico.
- Eliminar usuarios (purga completa de todos sus datos: gastos, presupuestos, recurrentes, obligaciones y configuración).
