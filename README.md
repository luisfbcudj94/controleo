# Controleo

App móvil de control de gastos en .NET MAUI (Android/iOS) con API ASP.NET Core que guarda registros en Excel.

## Estructura

- `Controleo.Api`: API para registrar gastos y escribir en Excel.
- `Controleo.Mobile`: App MAUI con formulario móvil.

## Datos capturados

- Fecha
- Descripción
- Valor
- Tipo de movimiento
- Medio de pago

## Configuración Excel (Drive)

La API usa `Controleo.Api/appsettings.Development.json`:

```json
"ExcelStorage": {
  "FilePath": "C:\\Users\\v-tgeethanat\\Google Drive\\controleo\\gastos.xlsx",
  "WorksheetName": "Gastos",
  "DateColumnHeader": "Fecha",
  "DescriptionColumnHeader": "Descripcion",
  "AmountColumnHeader": "Valor",
  "MovementTypeColumnHeader": "TipoMovimiento",
  "PaymentMethodColumnHeader": "MedioPago"
}
```

Ajusta `FilePath` y encabezados si tu Excel tiene una estructura distinta.

## Ejecutar en VS Code

1. Restaurar paquetes:
   - `dotnet restore Controleo.sln --ignore-failed-sources`
2. Ejecutar API:
   - task `API: Run`
   - o `dotnet run --project Controleo.Api/Controleo.Api.csproj --launch-profile http`
3. Ejecutar app MAUI en Windows (pruebas de UI):
   - `dotnet build Controleo.Mobile/Controleo.Mobile.csproj -f net9.0-windows10.0.19041.0`

## Probar API

- Catálogos: `GET http://localhost:5051/api/catalogs`
- Guardar gasto: `POST http://localhost:5051/api/expenses`

Ejemplo JSON:

```json
{
  "date": "2026-03-21",
  "description": "Mercado",
  "amount": 45000,
  "movementType": "Hogar",
  "paymentMethod": "Efectivo"
}
```

## Android APK

Prerequisitos en Windows:

- Android SDK instalado
- JDK 17+ instalado
- variables `ANDROID_HOME`/`AndroidSdkDirectory` y `JAVA_HOME` configuradas

Comando APK:

- `dotnet publish Controleo.Mobile/Controleo.Mobile.csproj -f net9.0-android -c Release -p:AndroidPackageFormat=apk`

## iOS

Para compilar/publicar iOS (`.ipa`) necesitas Mac (local o remoto con Pair to Mac).
